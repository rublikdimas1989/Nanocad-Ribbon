namespace NcTestRibbon.Cad
{
  using System;
  using System.Diagnostics;
  using System.Globalization;
  using System.IO;
  using System.Text;
  using System.Text.Json;

  /// <summary>
  /// End-style raster georeference (GDAL GeoTransform): upper-left corner + affine.
  /// Sources: world/TAF sidecar, bundled OSGeo.GDAL Open+GetGeoTransform (ECW plugin),
  /// or gdalinfo -json when available.
  /// </summary>
  public sealed class RasterGeoTransform
  {
    /// <summary>GT[0] — X of upper-left corner of upper-left pixel.</summary>
    public double OriginX { get; set; }

    /// <summary>GT[3] — Y of upper-left corner of upper-left pixel.</summary>
    public double OriginY { get; set; }

    /// <summary>GT[1] — pixel width (X scale).</summary>
    public double PixelSizeX { get; set; } = 1;

    /// <summary>GT[5] — pixel height (usually negative for north-up).</summary>
    public double PixelSizeY { get; set; } = -1;

    /// <summary>GT[2] — row rotation.</summary>
    public double RotateX { get; set; }

    /// <summary>GT[4] — column rotation.</summary>
    public double RotateY { get; set; }

    public int WidthPx { get; set; }

    public int HeightPx { get; set; }

    public string Source { get; set; } = string.Empty;

    public (double X, double Y) PixelToWorld(double pixelX, double pixelY)
    {
      return (
        OriginX + pixelX * PixelSizeX + pixelY * RotateX,
        OriginY + pixelX * RotateY + pixelY * PixelSizeY);
    }

    public static RasterGeoTransform Resolve(string rasterPath, out string note)
    {
      if (string.IsNullOrWhiteSpace(rasterPath) || !File.Exists(rasterPath))
      {
        throw new FileNotFoundException("Растр не найден.", rasterPath);
      }

      if (TryReadWorldFile(rasterPath, out RasterGeoTransform fromWorld, out string worldNote))
      {
        note = worldNote;
        TryFillSize(rasterPath, fromWorld);
        return fromWorld;
      }

      // End path: OSGeo.GDAL Open + GetGeoTransform (embedded ECW GT via ECW plugin).
      if (TryReadManagedGdal(rasterPath, out RasterGeoTransform fromManaged, out string managedNote))
      {
        note = managedNote;
        return fromManaged;
      }

      if (TryReadGdalInfo(rasterPath, out RasterGeoTransform fromGdal, out string gdalNote))
      {
        note = gdalNote;
        return fromGdal;
      }

      string loadFileHint = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ASDC", "LoadFile", "NANOCAD", "gdal", "x64", "gdal_wrap.dll");
      throw new InvalidOperationException(
        "Нет геопривязки для «" + Path.GetFileName(rasterPath) + "».\n"
        + "Ожидается embedded GDAL GeoTransform (как End) или world/TAF рядом с файлом.\n\n"
        + "GDAL status:\n" + (string.IsNullOrWhiteSpace(managedNote) ? "(нет)" : managedNote) + "\n\n"
        + "Проверьте, что INSTALL.bat скопировал:\n"
        + "  " + loadFileHint + "\n"
        + "  …\\gdal\\x64\\plugins\\gdal_ECW_JP2ECW.dll\n"
        + "  …\\gdal\\x64\\plugins\\NCSEcw.dll\n"
        + "и рядом с плагином: gdal_csharp.dll.\n"
        + "Затем NETLOAD / перезапуск nanoCAD.");
    }

    /// <summary>Write ESRI world sidecar; returns full path written.</summary>
    public string WriteEsriWorldFile(string rasterPath)
    {
      string worldPath = PreferWorldPath(rasterPath);
      // ESRI world file uses centre of upper-left pixel.
      double cx = OriginX + PixelSizeX / 2.0;
      double cy = OriginY + PixelSizeY / 2.0;
      var sb = new StringBuilder();
      sb.AppendLine(F(PixelSizeX));
      sb.AppendLine(F(RotateY));
      sb.AppendLine(F(RotateX));
      sb.AppendLine(F(PixelSizeY));
      sb.AppendLine(F(cx));
      sb.AppendLine(F(cy));
      File.WriteAllText(worldPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      return worldPath;
    }

    public static string PreferWorldPath(string rasterPath)
    {
      string ext = Path.GetExtension(rasterPath) ?? string.Empty;
      string worldExt = ext.Equals(".ecw", StringComparison.OrdinalIgnoreCase) ? ".eww"
        : ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ? ".tfw"
        : ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jgw"
        : ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? ".pgw"
        : ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ? ".bpw"
        : ".wld";
      return Path.ChangeExtension(rasterPath, worldExt);
    }

    private static bool TryReadWorldFile(string rasterPath, out RasterGeoTransform geo, out string note)
    {
      geo = null;
      note = string.Empty;
      string[] candidates =
      {
        Path.ChangeExtension(rasterPath, ".eww"),
        Path.ChangeExtension(rasterPath, ".wld"),
        Path.ChangeExtension(rasterPath, ".tfw"),
        Path.ChangeExtension(rasterPath, ".tifw"),
        Path.ChangeExtension(rasterPath, ".jgw"),
        Path.ChangeExtension(rasterPath, ".pgw"),
        Path.ChangeExtension(rasterPath, ".sdw"),
        Path.ChangeExtension(rasterPath, ".bpw"),
        Path.ChangeExtension(rasterPath, ".taf"),
        rasterPath + ".wld"
      };

      foreach (string path in candidates)
      {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
          continue;
        }

        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 6)
        {
          continue;
        }

        if (!TryParse(lines[0], out double a)
          || !TryParse(lines[1], out double d)
          || !TryParse(lines[2], out double b)
          || !TryParse(lines[3], out double e)
          || !TryParse(lines[4], out double c)
          || !TryParse(lines[5], out double f))
        {
          continue;
        }

        // World file stores centre of UL pixel → GDAL corner.
        geo = new RasterGeoTransform
        {
          PixelSizeX = a,
          RotateY = d,
          RotateX = b,
          PixelSizeY = e,
          OriginX = c - a / 2.0 - b / 2.0,
          OriginY = f - d / 2.0 - e / 2.0,
          Source = Path.GetFileName(path)
        };
        note = "world: " + Path.GetFileName(path);
        return true;
      }

      return false;
    }

    private static bool TryReadManagedGdal(string rasterPath, out RasterGeoTransform geo, out string note)
    {
      geo = null;
      note = string.Empty;
      var status = new StringBuilder();
      try
      {
        string arch = Environment.Is64BitProcess ? "x64" : "x86";
        List<string> candidates = EnumerateGdalBaseCandidates();
        status.AppendLine("arch=" + arch);
        status.AppendLine("candidates:");
        foreach (string c in candidates)
        {
          string wrap = Path.Combine(c, "gdal", arch, "gdal_wrap.dll");
          status.AppendLine("  " + c + (File.Exists(wrap) ? " [wrap OK]" : " [no wrap]"));
        }

        string chosen = null;
        foreach (string baseDir in candidates)
        {
          if (File.Exists(Path.Combine(baseDir, "gdal", arch, "gdal_wrap.dll")))
          {
            chosen = baseDir;
            break;
          }
        }

        if (chosen == null)
        {
          note = status.ToString().TrimEnd()
            + "\nGDAL init: gdal_wrap.dll not found under any candidate\\gdal\\" + arch;
          return false;
        }

        status.AppendLine("using base: " + chosen);
        if (!GdalRuntimeBootstrap.TryInitialize(chosen, out string diagnostics, out string error))
        {
          note = status.ToString().TrimEnd()
            + "\nGDAL init failed: " + (error ?? "(null)")
            + "\n--- diagnostics ---\n" + (diagnostics ?? string.Empty);
          return false;
        }

        status.AppendLine("init OK");
        try
        {
          OSGeo.GDAL.Driver ecw = OSGeo.GDAL.Gdal.GetDriverByName("ECW");
          status.AppendLine("ECW driver: " + (ecw != null ? ecw.ShortName : "MISSING"));
        }
        catch (Exception dex)
        {
          status.AppendLine("ECW driver check: " + dex.Message);
        }

        OSGeo.GDAL.Dataset dataset = null;
        try
        {
          dataset = OSGeo.GDAL.Gdal.Open(rasterPath, OSGeo.GDAL.Access.GA_ReadOnly);
        }
        catch (Exception openEx)
        {
          note = status.ToString().TrimEnd()
            + "\nGdal.Open threw: " + openEx.GetType().Name + ": " + openEx.Message
            + "\nLastError: " + SafeGdalLastError();
          return false;
        }

        if (dataset == null)
        {
          note = status.ToString().TrimEnd()
            + "\nGdal.Open returned null. LastError: " + SafeGdalLastError();
          return false;
        }

        using (dataset)
        {
          if (dataset.RasterXSize <= 0 || dataset.RasterYSize <= 0)
          {
            note = status.ToString().TrimEnd() + "\nGdal.Open: empty raster size.";
            return false;
          }

          double[] t = new double[6];
          dataset.GetGeoTransform(t);
          status.AppendLine(
            "GT=[" + string.Join(", ", Array.ConvertAll(t, v => v.ToString("R", CultureInfo.InvariantCulture))) + "]");
          status.AppendLine("size=" + dataset.RasterXSize + "x" + dataset.RasterYSize);

          foreach (double value in t)
          {
            if (!double.IsFinite(value))
            {
              note = status.ToString().TrimEnd() + "\nGeoTransform has non-finite values.";
              return false;
            }
          }

          bool identity = t[0] == 0 && t[1] == 1 && t[2] == 0 && t[3] == 0 && t[4] == 0 && t[5] == 1;
          double det = t[1] * t[5] - t[2] * t[4];
          if (identity || !double.IsFinite(det) || Math.Abs(det) < 1e-30)
          {
            note = status.ToString().TrimEnd()
              + "\nGeoTransform identity/degenerate — ECW opened but no usable embedded affine.";
            return false;
          }

          geo = new RasterGeoTransform
          {
            OriginX = t[0],
            PixelSizeX = t[1],
            RotateX = t[2],
            OriginY = t[3],
            RotateY = t[4],
            PixelSizeY = t[5],
            WidthPx = dataset.RasterXSize,
            HeightPx = dataset.RasterYSize,
            Source = "OSGeo.GDAL"
          };
          note = "GDAL GetGeoTransform (embedded) @ " + chosen;
          return true;
        }
      }
      catch (Exception ex)
      {
        note = status.ToString().TrimEnd()
          + "\nGDAL: " + ex.GetType().Name + ": " + ex.Message
          + "\nLastError: " + SafeGdalLastError();
        return false;
      }
    }

    private static string SafeGdalLastError()
    {
      try
      {
        return OSGeo.GDAL.Gdal.GetLastErrorMsg() ?? string.Empty;
      }
      catch
      {
        return "(unavailable)";
      }
    }

    /// <summary>
    /// Prefer LoadFile\NANOCAD (where INSTALL copies gdal\), then plugin assembly dir.
    /// </summary>
    private static List<string> EnumerateGdalBaseCandidates()
    {
      var list = new List<string>();
      void Add(string path)
      {
        if (string.IsNullOrWhiteSpace(path))
        {
          return;
        }

        try
        {
          string full = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
          if (!list.Exists(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)))
          {
            list.Add(full);
          }
        }
        catch
        {
        }
      }

      try
      {
        Add(Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "ASDC", "LoadFile", "NANOCAD"));
      }
      catch
      {
      }

      try
      {
        string asm = typeof(RasterGeoTransform).Assembly.Location;
        if (!string.IsNullOrEmpty(asm))
        {
          Add(Path.GetDirectoryName(asm));
        }
      }
      catch
      {
      }

      try
      {
        Add(AppContext.BaseDirectory);
      }
      catch
      {
      }

      try
      {
        string env = Environment.GetEnvironmentVariable("NC_TEST_GDAL_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
          Add(env);
        }
      }
      catch
      {
      }

      return list;
    }

    private static bool TryReadGdalInfo(string rasterPath, out RasterGeoTransform geo, out string note)
    {
      geo = null;
      note = string.Empty;
      string exe = FindGdalInfo();
      if (exe == null)
      {
        return false;
      }

      try
      {
        var psi = new ProcessStartInfo
        {
          FileName = exe,
          Arguments = "-json \"" + rasterPath + "\"",
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8
        };
        using Process proc = Process.Start(psi);
        if (proc == null)
        {
          return false;
        }

        string stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(60000);
        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
          return false;
        }

        using JsonDocument doc = JsonDocument.Parse(stdout);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("geoTransform", out JsonElement gt) || gt.GetArrayLength() < 6)
        {
          return false;
        }

        double[] t = new double[6];
        for (int i = 0; i < 6; i++)
        {
          t[i] = gt[i].GetDouble();
        }

        bool identity = t[0] == 0 && t[1] == 1 && t[2] == 0 && t[3] == 0 && t[4] == 0 && t[5] == 1;
        double det = t[1] * t[5] - t[2] * t[4];
        if (identity || !double.IsFinite(det) || Math.Abs(det) < 1e-30)
        {
          return false;
        }

        int w = 0, h = 0;
        if (root.TryGetProperty("size", out JsonElement size) && size.GetArrayLength() >= 2)
        {
          w = size[0].GetInt32();
          h = size[1].GetInt32();
        }

        geo = new RasterGeoTransform
        {
          OriginX = t[0],
          PixelSizeX = t[1],
          RotateX = t[2],
          OriginY = t[3],
          RotateY = t[4],
          PixelSizeY = t[5],
          WidthPx = w,
          HeightPx = h,
          Source = "gdalinfo"
        };
        note = "gdalinfo GeoTransform";
        return true;
      }
      catch
      {
        return false;
      }
    }

    private static string FindGdalInfo()
    {
      string[] names = { "gdalinfo.exe", "gdalinfo" };
      string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
      foreach (string dir in pathEnv.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
      {
        foreach (string name in names)
        {
          string full = Path.Combine(dir.Trim('"'), name);
          if (File.Exists(full))
          {
            return full;
          }
        }
      }

      return null;
    }

    private static void TryFillSize(string rasterPath, RasterGeoTransform geo)
    {
      if (geo.WidthPx > 0 && geo.HeightPx > 0)
      {
        return;
      }

      if (TryReadManagedGdal(rasterPath, out RasterGeoTransform managed, out _) && managed.WidthPx > 0)
      {
        geo.WidthPx = managed.WidthPx;
        geo.HeightPx = managed.HeightPx;
        return;
      }

      if (TryReadGdalInfo(rasterPath, out RasterGeoTransform gdal, out _) && gdal.WidthPx > 0)
      {
        geo.WidthPx = gdal.WidthPx;
        geo.HeightPx = gdal.HeightPx;
      }
    }

    private static bool TryParse(string text, out double value)
    {
      return double.TryParse(
        (text ?? string.Empty).Trim().Replace(',', '.'),
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out value);
    }

    private static string F(double value)
    {
      return value.ToString("R", CultureInfo.InvariantCulture);
    }
  }
}
