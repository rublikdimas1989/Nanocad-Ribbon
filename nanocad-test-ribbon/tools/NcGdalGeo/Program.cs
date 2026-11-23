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
        Console.Error.WriteLine("  NcGdalGeo translate <srcRaster> <dst.ecw|.tif>");
        Console.Error.WriteLine("  NcGdalGeo colorize forest|height <srcRaster> <dst.tif>");
        return 2;
      }

      bool translate = string.Equals(args[0], "translate", StringComparison.OrdinalIgnoreCase);
      bool colorize = string.Equals(args[0], "colorize", StringComparison.OrdinalIgnoreCase);
      if (translate && args.Length < 3)
      {
        Console.Error.WriteLine("usage: NcGdalGeo translate <srcRaster> <dst.ecw|.tif>");
        return 2;
      }

      if (colorize && args.Length < 4)
      {
        Console.Error.WriteLine("usage: NcGdalGeo colorize forest|height <srcRaster> <dst.tif>");
        return 2;
      }

      string mode = colorize ? args[1] : null;
      string srcPath = Path.GetFullPath(colorize ? args[2] : (translate ? args[1] : args[0]));
      string dstPath = colorize || translate ? Path.GetFullPath(colorize ? args[3] : args[2]) : null;

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

        if (colorize)
        {
          return RunColorize(mode, srcPath, dstPath, nativeDir);
        }

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

      string dstExt = (Path.GetExtension(dstPath) ?? string.Empty).ToLowerInvariant();
      bool wantEcw = dstExt == ".ecw";
      bool wantTif = dstExt == ".tif" || dstExt == ".tiff";
      if (!wantEcw && !wantTif)
      {
        WriteErr("Destination must be .ecw or .tif/.tiff: " + dstPath);
        return 1;
      }

      // Never delete/overwrite the source raster (Maps\Forest.tif → same path bug).
      string srcFull = Path.GetFullPath(srcPath);
      string dstFull = Path.GetFullPath(dstPath);
      if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
      {
        WriteErr("Refusing translate: destination equals source (" + srcFull + "). "
          + "Use Maps\\<name>_geo.tif|.ecw");
        return 4;
      }

      string dstDir = Path.GetDirectoryName(dstFull);
      if (!string.IsNullOrEmpty(dstDir))
      {
        Directory.CreateDirectory(dstDir);
      }

      if (File.Exists(dstFull))
      {
        // Only remove a prior *output* file, never the source.
        File.Delete(dstFull);
      }

      dstPath = dstFull;
      srcPath = srcFull;

      string company = Environment.GetEnvironmentVariable("ECW_ENCODE_COMPANY");
      string key = Environment.GetEnvironmentVariable("ECW_ENCODE_KEY");
      bool haveLicense = !string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(key);
      string driverName = wantEcw ? "ECW" : "GTiff";
      if (haveLicense)
      {
        Gdal.SetConfigOption("ECW_ENCODE_COMPANY", company.Trim());
        Gdal.SetConfigOption("ECW_ENCODE_KEY", key.Trim());
        Environment.SetEnvironmentVariable("ECW_ENCODE_COMPANY", company.Trim());
        Environment.SetEnvironmentVariable("ECW_ENCODE_KEY", key.Trim());
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

        Driver drv = Gdal.GetDriverByName(driverName);
        if (drv == null)
        {
          WriteErr(driverName + " driver MISSING. last=" + Gdal.GetLastErrorMsg());
          return 1;
        }

        var optList = new List<string>();
        if (wantEcw)
        {
          if (!haveLicense)
          {
            WriteErr("ECW_ENCODE_KEY/COMPANY required for ECW CreateCopy. "
              + "Set env or Tracing_cache\\Settings\\ecw_encode.conf (End-style OEM license).");
            return 3;
          }

          optList.Add("TARGET=95");
          optList.Add("LARGE_OK=YES");
          optList.Add("ECW_ENCODE_COMPANY=" + company.Trim());
          optList.Add("ECW_ENCODE_KEY=" + key.Trim());
        }
        else
        {
          // Free encode path (End Forest.tif style): GeoTIFF keeps embedded GT.
          optList.Add("COMPRESS=LZW");
          optList.Add("TILED=YES");
          optList.Add("BIGTIFF=IF_SAFER");
        }

        string[] options = optList.ToArray();
        using (Dataset dst = drv.CreateCopy(dstPath, src, 0, options, null, null))
        {
          if (dst == null)
          {
            WriteErr(driverName + " CreateCopy null: " + Gdal.GetLastErrorMsg());
            return wantEcw ? 3 : 1;
          }

          dst.FlushCache();
        }
      }

      if (!File.Exists(dstPath) || new FileInfo(dstPath).Length < 16)
      {
        WriteErr("Output missing/empty after CreateCopy. last=" + Gdal.GetLastErrorMsg());
        return 1;
      }

      using (Dataset verify = Gdal.Open(dstPath, Access.GA_ReadOnly))
      {
        if (verify == null)
        {
          WriteErr("Created file but reopen failed: " + Gdal.GetLastErrorMsg());
          return 1;
        }

        double[] vgt = new double[6];
        verify.GetGeoTransform(vgt);
        if (!IsUsableGeoTransform(vgt))
        {
          WriteErr("Created file but GeoTransform lost on reopen.");
          return 1;
        }

        var payload = new Dictionary<string, object>
        {
          ["ok"] = true,
          ["action"] = "translate",
          ["src"] = srcPath,
          ["dst"] = dstPath,
          ["driver"] = verify.GetDriver() != null ? verify.GetDriver().ShortName : driverName,
          ["width"] = verify.RasterXSize,
          ["height"] = verify.RasterYSize,
          ["geoTransform"] = vgt,
          ["bytes"] = new FileInfo(dstPath).Length,
          ["license"] = haveLicense,
          ["nativeDir"] = nativeDir,
          ["version"] = Gdal.VersionInfo("--version") ?? ""
        };
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return 0;
      }
    }

    /// <summary>
    /// End-style colorize: forest (0/1 → ForestGreen) or height (viridis 7-band).
    /// Writes RGB GeoTIFF with same GeoTransform; never overwrites source.
    /// </summary>
    private static int RunColorize(string mode, string srcPath, string dstPath, string nativeDir)
    {
      bool forest = string.Equals(mode, "forest", StringComparison.OrdinalIgnoreCase);
      bool height = string.Equals(mode, "height", StringComparison.OrdinalIgnoreCase);
      if (!forest && !height)
      {
        WriteErr("colorize mode must be forest|height, got: " + mode);
        return 2;
      }

      if (!File.Exists(srcPath))
      {
        WriteErr("Source not found: " + srcPath);
        return 1;
      }

      string srcFull = Path.GetFullPath(srcPath);
      string dstFull = Path.GetFullPath(dstPath);
      if (string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
      {
        WriteErr("Refusing colorize: destination equals source (" + srcFull + ")");
        return 4;
      }

      string dstExt = (Path.GetExtension(dstFull) ?? "").ToLowerInvariant();
      if (dstExt != ".tif" && dstExt != ".tiff")
      {
        WriteErr("colorize destination must be .tif/.tiff");
        return 2;
      }

      string dstDir = Path.GetDirectoryName(dstFull);
      if (!string.IsNullOrEmpty(dstDir))
      {
        Directory.CreateDirectory(dstDir);
      }

      if (File.Exists(dstFull))
      {
        File.Delete(dstFull);
      }

      float minVal = float.MaxValue;
      float maxVal = float.MinValue;
      int outW = 0, outH = 0;
      double[] outGt = new double[6];

      using (Dataset src = Gdal.Open(srcFull, Access.GA_ReadOnly))
      {
        if (src == null)
        {
          WriteErr("Gdal.Open null: " + Gdal.GetLastErrorMsg());
          return 1;
        }

        int w = src.RasterXSize;
        int h = src.RasterYSize;
        outW = w;
        outH = h;
        if (w <= 0 || h <= 0)
        {
          WriteErr("Empty raster size");
          return 1;
        }

        double[] gt = new double[6];
        src.GetGeoTransform(gt);
        Array.Copy(gt, outGt, 6);
        if (!IsUsableGeoTransform(gt))
        {
          WriteErr("Source GeoTransform identity/degenerate");
          return 1;
        }

        Band srcBand = src.GetRasterBand(1);
        double noData = 0;
        int hasNoData = 0;
        try { srcBand.GetNoDataValue(out noData, out hasNoData); } catch { /* ignore */ }

        if (height)
        {
          // Pass 1: min/max of finite samples (End MapRenderingService).
          float[] rowScan = new float[w];
          for (int y = 0; y < h; y++)
          {
            srcBand.ReadRaster(0, y, w, 1, rowScan, w, 1, 0, 0);
            for (int x = 0; x < w; x++)
            {
              float v = rowScan[x];
              if (float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) >= 1e8f)
              {
                continue;
              }

              if (hasNoData != 0 && Math.Abs(v - (float)noData) < 1e-6f)
              {
                continue;
              }

              if (v < minVal) minVal = v;
              if (v > maxVal) maxVal = v;
            }
          }

          if (maxVal - minVal < 0.001f)
          {
            maxVal = minVal + 1f;
          }

          if (minVal == float.MaxValue)
          {
            WriteErr("Height raster has no finite samples");
            return 1;
          }
        }

        Driver gtiff = Gdal.GetDriverByName("GTiff");
        if (gtiff == null)
        {
          WriteErr("GTiff driver missing");
          return 1;
        }

        string[] createOpts = { "COMPRESS=LZW", "TILED=YES", "PHOTOMETRIC=RGB", "INTERLEAVE=PIXEL" };
        using (Dataset dst = gtiff.Create(dstFull, w, h, 3, DataType.GDT_Byte, createOpts))
        {
          if (dst == null)
          {
            WriteErr("GTiff.Create null: " + Gdal.GetLastErrorMsg());
            return 1;
          }

          dst.SetGeoTransform(gt);
          string proj = src.GetProjection();
          if (!string.IsNullOrEmpty(proj))
          {
            dst.SetProjection(proj);
          }

          Band rBand = dst.GetRasterBand(1);
          Band gBand = dst.GetRasterBand(2);
          Band bBand = dst.GetRasterBand(3);

          float[] srcRow = new float[w];
          byte[] rRow = new byte[w];
          byte[] gRow = new byte[w];
          byte[] bRow = new byte[w];

          // End ForestGreen #228B22 / ColorUtils.GetForestColor base.
          const byte forestR = 0x22, forestG = 0x8B, forestB = 0x22;
          // End NoData / non-forest background for CAD visibility.
          const byte bgR = 230, bgG = 230, bgB = 230;
          const byte ndR = 180, ndG = 180, ndB = 180;

          for (int y = 0; y < h; y++)
          {
            srcBand.ReadRaster(0, y, w, 1, srcRow, w, 1, 0, 0);
            for (int x = 0; x < w; x++)
            {
              float v = srcRow[x];
              bool nodata = float.IsNaN(v) || float.IsInfinity(v) || Math.Abs(v) >= 1e8f
                || (hasNoData != 0 && Math.Abs(v - (float)noData) < 1e-6f);

              if (forest)
              {
                if (!nodata && v > 0.5f)
                {
                  // End: intensity boost from value (Binary 1 → slight boost).
                  int boost = Math.Min(255, (int)(v * 50));
                  rRow[x] = (byte)Math.Min(255, forestR + boost / 4);
                  gRow[x] = (byte)Math.Min(255, forestG + boost / 2);
                  bRow[x] = (byte)Math.Min(255, forestB + boost / 4);
                }
                else
                {
                  rRow[x] = bgR;
                  gRow[x] = bgG;
                  bRow[x] = bgB;
                }
              }
              else
              {
                if (nodata)
                {
                  rRow[x] = ndR;
                  gRow[x] = ndG;
                  bRow[x] = ndB;
                }
                else
                {
                  float t = (v - minVal) / (maxVal - minVal);
                  GetHeightColorBand(t, out byte rr, out byte gg, out byte bb);
                  rRow[x] = rr;
                  gRow[x] = gg;
                  bRow[x] = bb;
                }
              }
            }

            rBand.WriteRaster(0, y, w, 1, rRow, w, 1, 0, 0);
            gBand.WriteRaster(0, y, w, 1, gRow, w, 1, 0, 0);
            bBand.WriteRaster(0, y, w, 1, bRow, w, 1, 0, 0);
          }

          dst.FlushCache();
        }
      }

      if (!File.Exists(srcFull))
      {
        WriteErr("BUG: source deleted during colorize");
        return 1;
      }

      if (!File.Exists(dstFull) || new FileInfo(dstFull).Length < 16)
      {
        WriteErr("colorize output missing/empty");
        return 1;
      }

      var payload = new Dictionary<string, object>
      {
        ["ok"] = true,
        ["action"] = "colorize",
        ["mode"] = forest ? "forest" : "height",
        ["src"] = srcFull,
        ["dst"] = dstFull,
        ["driver"] = "GTiff",
        ["width"] = outW,
        ["height"] = outH,
        ["bands"] = 3,
        ["geoTransform"] = outGt,
        ["bytes"] = new FileInfo(dstFull).Length,
        ["nativeDir"] = nativeDir
      };
      if (height)
      {
        payload["min"] = minVal;
        payload["max"] = maxVal;
      }

      Console.WriteLine(JsonSerializer.Serialize(payload));
      return 0;
    }

    /// <summary>End ColorUtils.GetHeightColorBand — viridis-like 7 bands.</summary>
    private static void GetHeightColorBand(float t, out byte r, out byte g, out byte b)
    {
      if (t < 0f) t = 0f;
      if (t > 1f) t = 1f;
      if (t < 0.143f) { r = 68; g = 1; b = 84; return; }
      if (t < 0.286f) { r = 72; g = 40; b = 120; return; }
      if (t < 0.429f) { r = 62; g = 104; b = 137; return; }
      if (t < 0.571f) { r = 49; g = 104; b = 142; return; }
      if (t < 0.714f) { r = 38; g = 131; b = 143; return; }
      if (t < 0.857f) { r = 53; g = 183; b = 121; return; }
      r = 253; g = 231; b = 37;
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
