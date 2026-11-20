namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Text;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Resolve Hexagon ECW OEM encode license (ECW_ENCODE_COMPANY + ECW_ENCODE_KEY).
  /// Sources (End-style): env, Tracing_cache Settings, LoadFile gdal conf, End/ASDC probes.
  /// </summary>
  public static class EcwEncodeLicense
  {
    public sealed class License
    {
      public string Company { get; set; } = string.Empty;
      public string Key { get; set; } = string.Empty;
      public string Source { get; set; } = string.Empty;

      public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Company) && !string.IsNullOrWhiteSpace(Key);
    }

    public static License TryResolve(out string diagnostics)
    {
      var log = new StringBuilder();
      var lic = new License();

      TryFromEnv(lic, log);
      if (lic.IsComplete)
      {
        diagnostics = log.ToString().TrimEnd();
        return lic;
      }

      foreach (string path in EnumerateConfCandidates())
      {
        log.AppendLine("conf?: " + path + (File.Exists(path) ? " [ok]" : " [missing]"));
        if (!File.Exists(path))
        {
          continue;
        }

        if (TryFromConfFile(path, lic, log))
        {
          diagnostics = log.ToString().TrimEnd();
          return lic;
        }
      }

      diagnostics = log.ToString().TrimEnd();
      return lic;
    }

    public static string ExampleConfPath
    {
      get
      {
        try
        {
          TracingCache.EnsureCreated();
          return Path.Combine(TracingCache.SettingsDir, "ecw_encode.conf");
        }
        catch
        {
          return "Tracing_cache\\Settings\\ecw_encode.conf";
        }
      }
    }

    private static void TryFromEnv(License lic, StringBuilder log)
    {
      string company = Environment.GetEnvironmentVariable("ECW_ENCODE_COMPANY");
      string key = Environment.GetEnvironmentVariable("ECW_ENCODE_KEY");
      if (!string.IsNullOrWhiteSpace(company))
      {
        lic.Company = company.Trim();
      }

      if (!string.IsNullOrWhiteSpace(key))
      {
        lic.Key = key.Trim();
      }

      if (lic.IsComplete)
      {
        lic.Source = "env ECW_ENCODE_COMPANY/KEY";
        log.AppendLine("license: env");
      }
      else if (!string.IsNullOrWhiteSpace(company) || !string.IsNullOrWhiteSpace(key))
      {
        log.AppendLine("env: partial (need both COMPANY and KEY)");
      }
    }

    private static bool TryFromConfFile(string path, License lic, StringBuilder log)
    {
      try
      {
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
          string line = (raw ?? string.Empty).Trim();
          if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
          {
            continue;
          }

          int eq = line.IndexOf('=');
          if (eq <= 0)
          {
            continue;
          }

          string name = line.Substring(0, eq).Trim();
          string value = line.Substring(eq + 1).Trim().Trim('"');
          if (name.Equals("ECW_ENCODE_COMPANY", StringComparison.OrdinalIgnoreCase)
            || name.Equals("COMPANY", StringComparison.OrdinalIgnoreCase))
          {
            lic.Company = value;
          }
          else if (name.Equals("ECW_ENCODE_KEY", StringComparison.OrdinalIgnoreCase)
            || name.Equals("KEY", StringComparison.OrdinalIgnoreCase))
          {
            lic.Key = value;
          }
        }

        if (lic.IsComplete)
        {
          lic.Source = path;
          log.AppendLine("license: " + path);
          return true;
        }

        log.AppendLine("conf incomplete: " + path);
      }
      catch (Exception ex)
      {
        log.AppendLine("conf error " + path + ": " + ex.Message);
      }

      return false;
    }

    private static IEnumerable<string> EnumerateConfCandidates()
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
          string full = Path.GetFullPath(path);
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
        TracingCache.EnsureCreated();
        Add(Path.Combine(TracingCache.SettingsDir, "ecw_encode.conf"));
        Add(Path.Combine(TracingCache.SettingsDir, "gdal_ecw.conf"));
      }
      catch
      {
      }

      string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      Add(Path.Combine(local, "ASDC", "LoadFile", "NANOCAD", "gdal", "ecw_encode.conf"));
      Add(Path.Combine(local, "ASDC", "LoadFile", "NANOCAD", "gdal", "ecw.conf"));
      Add(Path.Combine(local, "ASDC", "LoadFile", "NANOCAD", "ecw_encode.conf"));

      // End / ASDC sibling LoadFile trees (license from End config).
      try
      {
        string loadRoot = Path.Combine(local, "ASDC", "LoadFile");
        if (Directory.Exists(loadRoot))
        {
          foreach (string dir in Directory.EnumerateDirectories(loadRoot))
          {
            Add(Path.Combine(dir, "ecw_encode.conf"));
            Add(Path.Combine(dir, "gdal", "ecw_encode.conf"));
            Add(Path.Combine(dir, "Tracing_cache", "Settings", "ecw_encode.conf"));
          }
        }
      }
      catch
      {
      }

      string envPath = Environment.GetEnvironmentVariable("NC_ECW_ENCODE_CONF");
      if (!string.IsNullOrWhiteSpace(envPath))
      {
        Add(envPath);
      }

      return list;
    }
  }
}
