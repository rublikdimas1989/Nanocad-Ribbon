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
  /// Sources: world/TAF sidecar, or gdalinfo -json when available.
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

      if (TryReadGdalInfo(rasterPath, out RasterGeoTransform fromGdal, out string gdalNote))
      {
        note = gdalNote;
        return fromGdal;
      }

      throw new InvalidOperationException(
        "Нет геопривязки для «" + Path.GetFileName(rasterPath) + "».\n"
        + "Нужен world/TAF рядом с файлом (.eww/.wld/.tfw/.jgw/.pgw/.sdw/.taf) "
        + "или gdalinfo в PATH (как в End: GDAL GeoTransform).\n"
        + "IMAGEATTACH без world-файла ставит масштаб 1 (пиксель = единица чертежа).");
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
