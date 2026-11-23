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
  /// End-style Forest / Height colorize via NcGdalGeo → Maps\&lt;name&gt;_geo.tif → attach.
  /// Forest: 0/1 → ForestGreen (#228B22). Height: viridis 7-band by min/max.
  /// </summary>
  public static class CadRasterColorize
  {
    public enum Mode
    {
      Forest,
      Height
    }

    public sealed class Result
    {
      public string SourcePath { get; set; } = string.Empty;
      public string OutputPath { get; set; } = string.Empty;
      public string Note { get; set; } = string.Empty;
      public Mode Mode { get; set; }
    }

    public static Result Colorize(string tifPath, Mode mode)
    {
      if (string.IsNullOrWhiteSpace(tifPath) || !File.Exists(tifPath))
      {
        throw new FileNotFoundException("TIF не найден.", tifPath);
      }

      string sourceFull = Path.GetFullPath(tifPath);
      string exe = FindNcGdalGeo();
      if (exe == null)
      {
        throw new FileNotFoundException(
          "NcGdalGeo.exe не найден (LoadFile\\tools\\NcGdalGeo). Нужен GDAL runtime.");
      }

      string modeArg = mode == Mode.Forest ? "forest" : "height";
      string dst = CadTifToEcw.PreferMapsOutputPath(sourceFull, ".tif");
      if (SamePath(sourceFull, dst))
      {
        throw new InvalidOperationException("Refusing overwrite source: " + sourceFull);
      }

      string mapsDir = Path.GetDirectoryName(dst);
      if (!string.IsNullOrEmpty(mapsDir))
      {
        Directory.CreateDirectory(mapsDir);
      }

      var psi = new ProcessStartInfo
      {
        FileName = exe,
        Arguments = "colorize " + modeArg + " \"" + sourceFull + "\" \"" + dst + "\"",
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
        throw new TimeoutException("colorize timed out: " + Path.GetFileName(sourceFull));
      }

      if (!File.Exists(sourceFull))
      {
        throw new InvalidOperationException("BUG: source deleted during colorize: " + sourceFull);
      }

      if (string.IsNullOrWhiteSpace(stdout))
      {
        throw new InvalidOperationException(
          "colorize empty stdout exit=" + proc.ExitCode + " stderr=" + Truncate(stderr, 500));
      }

      using JsonDocument doc = JsonDocument.Parse(stdout.Trim());
      JsonElement root = doc.RootElement;
      if (root.TryGetProperty("ok", out JsonElement okEl) && okEl.ValueKind == JsonValueKind.False)
      {
        string err = root.TryGetProperty("error", out JsonElement e) ? e.GetString() : stdout;
        throw new InvalidOperationException("colorize failed: " + Truncate(err ?? stdout, 1200));
      }

      string outPath = root.TryGetProperty("dst", out JsonElement d) ? d.GetString() : dst;
      if (string.IsNullOrWhiteSpace(outPath) || !File.Exists(outPath))
      {
        throw new InvalidOperationException("colorize output missing: " + (outPath ?? dst));
      }

      string range = string.Empty;
      if (mode == Mode.Height
        && root.TryGetProperty("min", out JsonElement mn)
        && root.TryGetProperty("max", out JsonElement mx))
      {
        range = " min=" + mn.GetDouble().ToString("R") + " max=" + mx.GetDouble().ToString("R");
      }

      return new Result
      {
        SourcePath = sourceFull,
        OutputPath = outPath,
        Mode = mode,
        Note = RasterGeoTransform.PackageStamp()
          + " colorize " + modeArg + " → " + Path.GetFileName(outPath)
          + range
          + " (source kept: " + Path.GetFileName(sourceFull) + ")"
      };
    }

    private static bool SamePath(string a, string b)
    {
      try
      {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
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
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
          string full = Path.GetFullPath(path);
          if (File.Exists(full)
            && !candidates.Exists(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase)))
          {
            candidates.Add(full);
          }
        }
        catch { /* ignore */ }
      }

      try
      {
        Add(Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "ASDC", "LoadFile", "NANOCAD", "tools", "NcGdalGeo", "NcGdalGeo.exe"));
      }
      catch { /* ignore */ }

      try
      {
        string asm = typeof(CadRasterColorize).Assembly.Location;
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
      catch { /* ignore */ }

      return candidates.Count > 0 ? candidates[0] : null;
    }

    private static string Truncate(string text, int max)
    {
      if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
      return text.Substring(0, max) + "…";
    }
  }
}
