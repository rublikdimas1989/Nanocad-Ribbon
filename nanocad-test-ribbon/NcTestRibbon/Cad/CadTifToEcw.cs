namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.Text;
  using System.Text.Json;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Convert GeoTIFF → ECW via out-of-process NcGdalGeo translate (preserves GeoTransform),
  /// then attach like a normal ECW (Screen_ESRI.ecw path).
  /// </summary>
  public static class CadTifToEcw
  {
    public sealed class ConvertResult
    {
      public string SourcePath { get; set; } = string.Empty;
      public string EcwPath { get; set; } = string.Empty;
      public string Note { get; set; } = string.Empty;
      public double[] GeoTransform { get; set; }
    }

    public static ConvertResult Convert(string tifPath)
    {
      if (string.IsNullOrWhiteSpace(tifPath) || !File.Exists(tifPath))
      {
        throw new FileNotFoundException("TIF не найден.", tifPath);
      }

      string ext = Path.GetExtension(tifPath) ?? string.Empty;
      if (!ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
        && !ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
      {
        throw new InvalidOperationException("Ожидается GeoTIFF (.tif/.tiff), получено: " + ext);
      }

      string exe = FindNcGdalGeo();
      if (exe == null)
      {
        throw new FileNotFoundException(
          "NcGdalGeo.exe не найден. Нужен GDAL runtime (LoadFile\\tools\\NcGdalGeo) "
          + "из v1.2.48+ или nanocad-gdal-runtime zip.");
      }

      string ecwPath = PreferEcwOutputPath(tifPath);
      string ecwDir = Path.GetDirectoryName(ecwPath);
      if (!string.IsNullOrEmpty(ecwDir))
      {
        Directory.CreateDirectory(ecwDir);
      }

      var psi = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = "translate \"" + tifPath + "\" \"" + ecwPath + "\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty
      };

      using Process proc = Process.Start(psi);
      if (proc == null)
      {
        throw new InvalidOperationException("NcGdalGeo Process.Start returned null.");
      }

      string stdout = proc.StandardOutput.ReadToEnd();
      string stderr = proc.StandardError.ReadToEnd();
      if (!proc.WaitForExit(600000))
      {
        try { proc.Kill(); } catch { /* ignore */ }
        throw new TimeoutException("NcGdalGeo translate timed out for " + Path.GetFileName(tifPath));
      }

      if (string.IsNullOrWhiteSpace(stdout))
      {
        throw new InvalidOperationException(
          "NcGdalGeo translate empty stdout exit=" + proc.ExitCode
          + " stderr=" + Truncate(stderr, 500));
      }

      using JsonDocument doc = JsonDocument.Parse(stdout.Trim());
      JsonElement root = doc.RootElement;
      if (root.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.False)
      {
        string err = root.TryGetProperty("error", out JsonElement e) ? e.GetString() : stdout;
        throw new InvalidOperationException("TIF→ECW failed: " + Truncate(err ?? stdout, 1200));
      }

      string dst = root.TryGetProperty("dst", out JsonElement d) ? d.GetString() : ecwPath;
      if (string.IsNullOrWhiteSpace(dst) || !File.Exists(dst))
      {
        throw new InvalidOperationException("TIF→ECW: output ECW missing after translate.");
      }

      double[] gt = null;
      if (root.TryGetProperty("geoTransform", out JsonElement gtEl) && gtEl.GetArrayLength() >= 6)
      {
        gt = new double[6];
        for (int i = 0; i < 6; i++)
        {
          gt[i] = gtEl[i].GetDouble();
        }
      }

      return new ConvertResult
      {
        SourcePath = tifPath,
        EcwPath = dst,
        GeoTransform = gt,
        Note = RasterGeoTransform.PackageStamp()
          + " TIF→ECW via NcGdalGeo translate → " + Path.GetFileName(dst)
      };
    }

    /// <summary>
    /// Tracing_cache\Maps\{name}.ecw (preferred) or next to source if Maps unavailable.
    /// </summary>
    public static string PreferEcwOutputPath(string tifPath)
    {
      string baseName = Path.GetFileNameWithoutExtension(tifPath) ?? "raster";
      try
      {
        TracingCache.EnsureCreated();
        Directory.CreateDirectory(TracingCache.MapsDir);
        return Path.Combine(TracingCache.MapsDir, baseName + ".ecw");
      }
      catch
      {
        return Path.Combine(
          Path.GetDirectoryName(tifPath) ?? ".",
          baseName + "_nc.ecw");
      }
    }

    private static string FindNcGdalGeo()
    {
      var candidates = new List<string>();
      void Add(string path)
      {
        if (string.IsNullOrWhiteSpace(path))
        {
          return;
        }

        try
        {
          string full = Path.GetFullPath(path);
          if (File.Exists(full)
            && !candidates.Exists(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)))
          {
            candidates.Add(full);
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
          "ASDC", "LoadFile", "NANOCAD", "tools", "NcGdalGeo", "NcGdalGeo.exe"));
      }
      catch
      {
      }

      try
      {
        string asm = typeof(CadTifToEcw).Assembly.Location;
        if (!string.IsNullOrEmpty(asm))
        {
          string dir = Path.GetDirectoryName(asm);
          Add(Path.Combine(dir, "tools", "NcGdalGeo", "NcGdalGeo.exe"));
          string walk = dir;
          for (int i = 0; i < 6 && !string.IsNullOrEmpty(walk); i++)
          {
            Add(Path.Combine(walk, "installer", "tools", "NcGdalGeo", "NcGdalGeo.exe"));
            walk = Directory.GetParent(walk)?.FullName;
          }
        }
      }
      catch
      {
      }

      return candidates.Count > 0 ? candidates[0] : null;
    }

    private static string Truncate(string text, int max)
    {
      if (string.IsNullOrEmpty(text) || text.Length <= max)
      {
        return text ?? string.Empty;
      }

      return text.Substring(0, max) + "…";
    }
  }
}
