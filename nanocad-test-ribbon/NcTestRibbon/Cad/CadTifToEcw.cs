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
  /// GeoTIFF → Maps\&lt;name&gt;.ecw (OEM license) or free Maps\&lt;name&gt;.tif (End Forest.tif style),
  /// then attach with preserved GeoTransform.
  /// </summary>
  public static class CadTifToEcw
  {
    public sealed class ConvertResult
    {
      public string SourcePath { get; set; } = string.Empty;
      public string EcwPath { get; set; } = string.Empty;
      public string OutputPath { get; set; } = string.Empty;
      public string Note { get; set; } = string.Empty;
      public double[] GeoTransform { get; set; }
      public bool UsedFreeEncode { get; set; }
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

      EcwEncodeLicense.License lic = EcwEncodeLicense.TryResolve(out string licDiag);
      string mapsEcw = PreferMapsOutputPath(tifPath, ".ecw");
      EnsureParentDir(mapsEcw);

      if (lic.IsComplete)
      {
        ConvertResult ecw = RunTranslate(exe, tifPath, mapsEcw, lic, licDiag);
        if (ecw != null)
        {
          return ecw;
        }
      }

      // Free encode (End Forest.tif style): GeoTIFF in Maps with embedded GT — no OEM key.
      string mapsTif = PreferMapsOutputPath(tifPath, ".tif");
      EnsureParentDir(mapsTif);
      ConvertResult free = RunTranslate(exe, tifPath, mapsTif, license: null, licDiag);
      if (free == null)
      {
        throw new InvalidOperationException(
          "TIF→растр не удался.\n"
          + "Для ECW нужен OEM: " + EcwEncodeLicense.ExampleConfPath + "\n"
          + "  ECW_ENCODE_COMPANY=...\n  ECW_ENCODE_KEY=...\n"
          + "(или env / End LoadFile ecw_encode.conf)\n\n"
          + "License probe:\n" + Truncate(licDiag, 1200));
      }

      free.UsedFreeEncode = true;
      free.Note = RasterGeoTransform.PackageStamp()
        + " free encode → " + Path.GetFileName(free.OutputPath)
        + " (End Forest.tif style; ECW OEM missing — put key in "
        + Path.GetFileName(EcwEncodeLicense.ExampleConfPath) + ")";
      return free;
    }

    private static ConvertResult RunTranslate(
      string exe,
      string src,
      string dst,
      EcwEncodeLicense.License license,
      string licDiag)
    {
      var psi = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = "translate \"" + src + "\" \"" + dst + "\"",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty
      };

      if (license != null && license.IsComplete)
      {
        psi.Environment["ECW_ENCODE_COMPANY"] = license.Company;
        psi.Environment["ECW_ENCODE_KEY"] = license.Key;
      }
      else
      {
        // Avoid inheriting a partial/broken env pair into the child.
        psi.Environment.Remove("ECW_ENCODE_COMPANY");
        psi.Environment.Remove("ECW_ENCODE_KEY");
      }

      using Process proc = Process.Start(psi);
      if (proc == null)
      {
        return null;
      }

      string stdout = proc.StandardOutput.ReadToEnd();
      string stderr = proc.StandardError.ReadToEnd();
      if (!proc.WaitForExit(600000))
      {
        try { proc.Kill(); } catch { /* ignore */ }
        throw new TimeoutException("NcGdalGeo translate timed out for " + Path.GetFileName(src));
      }

      if (string.IsNullOrWhiteSpace(stdout))
      {
        if (proc.ExitCode == 3)
        {
          return null; // license required — caller may free-fallback
        }

        throw new InvalidOperationException(
          "NcGdalGeo translate empty stdout exit=" + proc.ExitCode
          + " stderr=" + Truncate(stderr, 500)
          + "\n" + Truncate(licDiag, 400));
      }

      using JsonDocument doc = JsonDocument.Parse(stdout.Trim());
      JsonElement root = doc.RootElement;
      if (root.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.False)
      {
        string err = root.TryGetProperty("error", out JsonElement e) ? e.GetString() : stdout;
        if (proc.ExitCode == 3
          || (err != null && err.IndexOf("ECW_ENCODE", StringComparison.OrdinalIgnoreCase) >= 0))
        {
          return null;
        }

        throw new InvalidOperationException("Translate failed: " + Truncate(err ?? stdout, 1200));
      }

      string outPath = root.TryGetProperty("dst", out JsonElement d) ? d.GetString() : dst;
      if (string.IsNullOrWhiteSpace(outPath) || !File.Exists(outPath))
      {
        throw new InvalidOperationException("Translate: output missing after CreateCopy.");
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

      string driver = root.TryGetProperty("driver", out JsonElement dr) ? (dr.GetString() ?? "") : "";
      bool isEcw = outPath.EndsWith(".ecw", StringComparison.OrdinalIgnoreCase);
      return new ConvertResult
      {
        SourcePath = src,
        EcwPath = isEcw ? outPath : string.Empty,
        OutputPath = outPath,
        GeoTransform = gt,
        UsedFreeEncode = !isEcw,
        Note = RasterGeoTransform.PackageStamp()
          + (isEcw
            ? " TIF→ECW (" + driver + ") license=" + (license?.Source ?? "?")
            : " TIF→GTiff free encode (" + driver + ")")
          + " → " + Path.GetFileName(outPath)
      };
    }

    /// <summary>Tracing_cache\Maps\{name}.ecw|.tif</summary>
    public static string PreferMapsOutputPath(string tifPath, string extension)
    {
      string baseName = Path.GetFileNameWithoutExtension(tifPath) ?? "raster";
      string ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
      try
      {
        TracingCache.EnsureCreated();
        Directory.CreateDirectory(TracingCache.MapsDir);
        return Path.Combine(TracingCache.MapsDir, baseName + ext);
      }
      catch
      {
        return Path.Combine(Path.GetDirectoryName(tifPath) ?? ".", baseName + "_nc" + ext);
      }
    }

    public static string PreferEcwOutputPath(string tifPath) => PreferMapsOutputPath(tifPath, ".ecw");

    private static void EnsureParentDir(string path)
    {
      string dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
      {
        Directory.CreateDirectory(dir);
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
