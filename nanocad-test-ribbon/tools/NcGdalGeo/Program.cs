using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using OSGeo.GDAL;

namespace NcGdalGeo
{
  internal static class Program
  {
    private static int Main(string[] args)
    {
      Console.OutputEncoding = Encoding.UTF8;
      if (args == null || args.Length < 1)
      {
        Console.Error.WriteLine("usage:");
        Console.Error.WriteLine("  NcGdalGeo <rasterPath>");
        Console.Error.WriteLine("  NcGdalGeo translate <srcRaster> <dst.ecw>");
        return 2;
      }

      bool translate = string.Equals(args[0], "translate", StringComparison.OrdinalIgnoreCase);
      if (translate && args.Length < 3)
      {
        Console.Error.WriteLine("usage: NcGdalGeo translate <srcRaster> <dst.ecw>");
        return 2;
      }

      string srcPath = Path.GetFullPath(translate ? args[1] : args[0]);
      string dstPath = translate ? Path.GetFullPath(args[2]) : null;

      string toolDir = AppDomain.CurrentDomain.BaseDirectory;
      string gdalRoot = FindGdalRoot(toolDir);
      string nativeDir = gdalRoot != null
        ? Path.Combine(gdalRoot, "x64")
        : Path.Combine(toolDir, "x64");
      if (!File.Exists(Path.Combine(nativeDir, "gdal_wrap.dll")))
      {
        nativeDir = toolDir;
      }

      string pluginDir = Path.Combine(nativeDir, "plugins");
      string dataDir = gdalRoot != null
        ? Path.Combine(gdalRoot, "data")
        : Path.Combine(toolDir, "data");
      string shareDir = gdalRoot != null
        ? Path.Combine(gdalRoot, "share")
        : Path.Combine(toolDir, "share");

      try
      {
        BootstrapNatives(nativeDir, pluginDir, dataDir, shareDir);
        Gdal.AllRegister();

        if (translate)
        {
          return RunTranslate(srcPath, dstPath, nativeDir);
        }

        return RunGeoTransform(srcPath, nativeDir);
      }
      catch (Exception ex)
      {
        string last = string.Empty;
        try { last = Gdal.GetLastErrorMsg(); } catch { /* ignore */ }
        WriteErr(ex.GetType().Name + ": " + ex.Message + " | " + last);
        return 1;
      }
    }

    private static int RunGeoTransform(string raster, string nativeDir)
    {
      using (Dataset ds = Gdal.Open(raster, Access.GA_ReadOnly))
      {
        if (ds == null)
        {
          WriteErr("Gdal.Open null: " + Gdal.GetLastErrorMsg());
          return 1;
        }

        double[] gt = new double[6];
        ds.GetGeoTransform(gt);
        if (!IsUsableGeoTransform(gt))
        {
          WriteErr("GeoTransform identity/degenerate; driver="
            + (ds.GetDriver() != null ? ds.GetDriver().ShortName : "?")
            + " last=" + Gdal.GetLastErrorMsg());
          return 1;
        }

        var payload = new Dictionary<string, object>
        {
          ["ok"] = true,
          ["driver"] = ds.GetDriver() != null ? ds.GetDriver().ShortName : "",
          ["width"] = ds.RasterXSize,
          ["height"] = ds.RasterYSize,
          ["geoTransform"] = gt,
          ["ecwDriver"] = Gdal.GetDriverByName("ECW") != null,
          ["version"] = Gdal.VersionInfo("--version") ?? "",
          ["nativeDir"] = nativeDir
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return 0;
      }
    }

    private static int RunTranslate(string srcPath, string dstPath, string nativeDir)
    {
      if (!File.Exists(srcPath))
      {
        WriteErr("Source not found: " + srcPath);
        return 1;
      }

      string dstExt = Path.GetExtension(dstPath) ?? string.Empty;
      if (!dstExt.Equals(".ecw", StringComparison.OrdinalIgnoreCase))
      {
        WriteErr("Destination must be .ecw: " + dstPath);
        return 1;
      }

      string dstDir = Path.GetDirectoryName(dstPath);
      if (!string.IsNullOrEmpty(dstDir))
      {
        Directory.CreateDirectory(dstDir);
      }

      if (File.Exists(dstPath))
      {
        File.Delete(dstPath);
      }

      using (Dataset src = Gdal.Open(srcPath, Access.GA_ReadOnly))
      {
        if (src == null)
        {
          WriteErr("Gdal.Open null: " + Gdal.GetLastErrorMsg());
          return 1;
        }

        double[] gt = new double[6];
        src.GetGeoTransform(gt);
        if (!IsUsableGeoTransform(gt))
        {
          WriteErr("Source GeoTransform identity/degenerate; cannot preserve georef. last="
            + Gdal.GetLastErrorMsg());
          return 1;
        }

        Driver ecw = Gdal.GetDriverByName("ECW");
        if (ecw == null)
        {
          WriteErr("ECW driver MISSING (need gdal_ECW_JP2ECW.dll + NCSEcw.dll under plugins).");
          return 1;
        }

        // Same idea as gdal_translate -of ECW: CreateCopy preserves GT/projection.
        string[] options =
        {
          "TARGET=95",
          "LARGE_OK=YES"
        };

        // CreateCopy(path, src, bStrict, options, callback, callbackData)
        using (Dataset dst = ecw.CreateCopy(dstPath, src, 0, options, null, null))
        {
          if (dst == null)
          {
            WriteErr("ECW CreateCopy null: " + Gdal.GetLastErrorMsg());
            return 1;
          }

          dst.FlushCache();
        }
      }

      if (!File.Exists(dstPath) || new FileInfo(dstPath).Length < 16)
      {
        WriteErr("ECW output missing/empty after CreateCopy. last=" + Gdal.GetLastErrorMsg());
        return 1;
      }

      // Re-open to confirm embedded GT survived (attach path uses NcGdalGeo geo).
      using (Dataset verify = Gdal.Open(dstPath, Access.GA_ReadOnly))
      {
        if (verify == null)
        {
          WriteErr("Created ECW but reopen failed: " + Gdal.GetLastErrorMsg());
          return 1;
        }

        double[] vgt = new double[6];
        verify.GetGeoTransform(vgt);
        if (!IsUsableGeoTransform(vgt))
        {
          WriteErr("Created ECW but GeoTransform lost on reopen.");
          return 1;
        }

        var payload = new Dictionary<string, object>
        {
          ["ok"] = true,
          ["action"] = "translate",
          ["src"] = srcPath,
          ["dst"] = dstPath,
          ["driver"] = verify.GetDriver() != null ? verify.GetDriver().ShortName : "ECW",
          ["width"] = verify.RasterXSize,
          ["height"] = verify.RasterYSize,
          ["geoTransform"] = vgt,
          ["bytes"] = new FileInfo(dstPath).Length,
          ["nativeDir"] = nativeDir,
          ["version"] = Gdal.VersionInfo("--version") ?? ""
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return 0;
      }
    }

    private static void BootstrapNatives(string nativeDir, string pluginDir, string dataDir, string shareDir)
    {
      Native.SetDllDirectory(nativeDir);
      string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
      Environment.SetEnvironmentVariable("PATH", nativeDir + ";" + pluginDir + ";" + path);
      Native.LoadLibraryExW(Path.Combine(nativeDir, "gdal.dll"), IntPtr.Zero, Native.LOAD_WITH_ALTERED_SEARCH_PATH);
      Native.LoadLibraryExW(Path.Combine(nativeDir, "gdal_wrap.dll"), IntPtr.Zero, Native.LOAD_WITH_ALTERED_SEARCH_PATH);
      if (Directory.Exists(pluginDir))
      {
        Native.LoadLibraryExW(Path.Combine(pluginDir, "NCSEcw.dll"), IntPtr.Zero, Native.LOAD_WITH_ALTERED_SEARCH_PATH);
        Native.LoadLibraryExW(Path.Combine(pluginDir, "gdal_ECW_JP2ECW.dll"), IntPtr.Zero, Native.LOAD_WITH_ALTERED_SEARCH_PATH);
      }

      if (Directory.Exists(dataDir))
      {
        Environment.SetEnvironmentVariable("GDAL_DATA", dataDir);
        Gdal.SetConfigOption("GDAL_DATA", dataDir);
      }

      if (Directory.Exists(shareDir))
      {
        Environment.SetEnvironmentVariable("PROJ_LIB", shareDir);
        Gdal.SetConfigOption("PROJ_LIB", shareDir);
      }

      if (Directory.Exists(pluginDir))
      {
        Environment.SetEnvironmentVariable("GDAL_DRIVER_PATH", pluginDir);
        Gdal.SetConfigOption("GDAL_DRIVER_PATH", pluginDir);
      }
    }

    private static bool IsUsableGeoTransform(double[] gt)
    {
      if (gt == null || gt.Length < 6)
      {
        return false;
      }

      foreach (double v in gt)
      {
        if (double.IsNaN(v) || double.IsInfinity(v))
        {
          return false;
        }
      }

      bool identity = gt[0] == 0 && gt[1] == 1 && gt[2] == 0 && gt[3] == 0 && gt[4] == 0 && gt[5] == 1;
      double det = gt[1] * gt[5] - gt[2] * gt[4];
      return !identity && !(double.IsNaN(det) || double.IsInfinity(det)) && Math.Abs(det) >= 1e-30;
    }

    private static void WriteErr(string msg)
    {
      Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
      {
        ["ok"] = false,
        ["error"] = msg
      }));
    }

    private static string FindGdalRoot(string startDir)
    {
      string dir = startDir;
      for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
      {
        string direct = Path.Combine(dir, "x64", "gdal_wrap.dll");
        if (File.Exists(direct))
        {
          return dir;
        }

        string nested = Path.Combine(dir, "gdal", "x64", "gdal_wrap.dll");
        if (File.Exists(nested))
        {
          return Path.Combine(dir, "gdal");
        }

        DirectoryInfo parent = Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        dir = parent?.FullName;
      }

      return null;
    }
  }

  internal static class Native
  {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

    public const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
  }
}
