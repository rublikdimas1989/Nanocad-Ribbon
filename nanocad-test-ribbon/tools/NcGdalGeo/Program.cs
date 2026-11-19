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
        Console.Error.WriteLine("usage: NcGdalGeo <rasterPath>");
        return 2;
      }

      string raster = Path.GetFullPath(args[0]);
      string toolDir = AppDomain.CurrentDomain.BaseDirectory;
      // Prefer LoadFile\NANOCAD\gdal\x64 (walk up from tools\NcGdalGeo or plugin dir).
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

        Gdal.AllRegister();
        using (Dataset ds = Gdal.Open(raster, Access.GA_ReadOnly))
        {
          if (ds == null)
          {
            WriteErr("Gdal.Open null: " + Gdal.GetLastErrorMsg());
            return 1;
          }

          double[] gt = new double[6];
          ds.GetGeoTransform(gt);
          bool identity = gt[0] == 0 && gt[1] == 1 && gt[2] == 0 && gt[3] == 0 && gt[4] == 0 && gt[5] == 1;
          double det = gt[1] * gt[5] - gt[2] * gt[4];
          if (identity || double.IsNaN(det) || double.IsInfinity(det) || Math.Abs(det) < 1e-30)
          {
            WriteErr("GeoTransform identity/degenerate; driver=" + (ds.GetDriver() != null ? ds.GetDriver().ShortName : "?")
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
      catch (Exception ex)
      {
        string last = string.Empty;
        try { last = Gdal.GetLastErrorMsg(); } catch { /* ignore */ }
        WriteErr(ex.GetType().Name + ": " + ex.Message + " | " + last);
        return 1;
      }
    }

    private static void WriteErr(string msg)
    {
      Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
      {
        ["ok"] = false,
        ["error"] = msg
      }));
    }

    /// <summary>Directory that contains x64\gdal_wrap.dll (usually …\gdal).</summary>
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
