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
  /// GeoTIFF → Maps\&lt;name&gt;_geo.ecw (OEM) or Maps\&lt;name&gt;_geo.tif (free encode).
  /// Never deletes/overwrites the source file.
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

      string sourceFull = Path.GetFullPath(tifPath);
      string ext = Path.GetExtension(sourceFull) ?? string.Empty;
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

      EnsureMapsDir();
      EcwEncodeLicense.License lic = EcwEncodeLicense.TryResolve(out string licDiag);

      if (lic.IsComplete)
      {
        string mapsEcw = PreferMapsOutputPath(sourceFull, ".ecw");
        AssertSafeOutput(sourceFull, mapsEcw);
        ConvertResult ecw = RunTranslate(exe, sourceFull, mapsEcw, lic, licDiag);
        if (ecw != null)
        {
          return ecw;
        }
      }

      // Free encode: Maps\<name>_geo.tif — never same path as source.
      string mapsTif = PreferMapsOutputPath(sourceFull, ".tif");
      AssertSafeOutput(sourceFull, mapsTif);
      ConvertResult free = RunTranslate(exe, sourceFull, mapsTif, license: null, licDiag);
      if (free == null)
      {
        throw new InvalidOperationException(
          "TIF→растр не удался (source left intact: " + Path.GetFileName(sourceFull) + ").\n"
          + "Для ECW нужен OEM: " + EcwEncodeLicense.ExampleConfPath + "\n"
          + "  ECW_ENCODE_COMPANY=...\n  ECW_ENCODE_KEY=...\n\n"
          + "License probe:\n" + Truncate(licDiag, 1200));
      }

      free.UsedFreeEncode = true;
      free.Note = RasterGeoTransform.PackageStamp()
        + " free encode → " + Path.GetFileName(free.OutputPath)
        + " (source kept: " + Path.GetFileName(sourceFull) + ")";
      return free;
    }

    private static ConvertResult RunTranslate(
      string exe,
      string src,
      string dst,
      EcwEncodeLicense.License license,
      string licDiag)
    {
      AssertSafeOutput(src, dst);
      EnsureParentDir(dst);

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

      // Source must still exist after translate.
      if (!File.Exists(src))
      {
        throw new InvalidOperationException(
          "BUG: source was deleted during translate: " + src
          + "\nstdout=" + Truncate(stdout, 400));
      }

      if (string.IsNullOrWhiteSpace(stdout))
      {
        if (proc.ExitCode == 3)
        {
          return null;
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
        throw new InvalidOperationException(
          "Translate: output missing after CreateCopy: " + (outPath ?? dst)
          + " (source intact=" + File.Exists(src) + ")");
      }

      if (SamePath(src, outPath))
      {
        throw new InvalidOperationException("BUG: translate output equals source path: " + src);
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

    /// <summary>Tracing_cache\Maps\{name}_geo.ecw|.tif — never the source path.</summary>
    public static string PreferMapsOutputPath(string tifPath, string extension)
    {
      string baseName = Path.GetFileNameWithoutExtension(tifPath) ?? "raster";
      if (baseName.EndsWith("_geo", StringComparison.OrdinalIgnoreCase))
      {
        // Avoid Forest_geo_geo.tif if re-run on prior output.
        baseName = baseName.Substring(0, baseName.Length - 4);
        if (string.IsNullOrWhiteSpace(baseName))
        {
          baseName = "raster";
        }
      }

      string ext = extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
      string mapsDir = EnsureMapsDir();
      string candidate = Path.Combine(mapsDir, baseName + "_geo" + ext);
      // If somehow still collides with source, add a unique suffix.
      if (SamePath(tifPath, candidate))
      {
        candidate = Path.Combine(
          mapsDir,
          baseName + "_geo_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ext);
      }

      return candidate;
    }

    public static string PreferEcwOutputPath(string tifPath) => PreferMapsOutputPath(tifPath, ".ecw");

    private static string EnsureMapsDir()
    {
      TracingCache.EnsureCreated();
      Directory.CreateDirectory(TracingCache.MapsDir);
      return TracingCache.MapsDir;
    }

    private static void EnsureParentDir(string path)
    {
      string dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
      {
        Directory.CreateDirectory(dir);
      }
    }

    private static void AssertSafeOutput(string source, string dest)
    {
      if (string.IsNullOrWhiteSpace(dest))
      {
        throw new InvalidOperationException("Empty output path.");
      }

      if (SamePath(source, dest))
      {
        throw new InvalidOperationException(
          "Refusing to overwrite source raster: " + source);
      }
    }

    private static bool SamePath(string a, string b)
    {
      try
      {
        return string.Equals(
          Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
          Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
          StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
