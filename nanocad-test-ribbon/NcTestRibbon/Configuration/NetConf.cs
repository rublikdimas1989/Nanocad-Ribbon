namespace NcTestRibbon.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Text;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Solver / map-upload host in Tracing_cache\Settings\net.conf (key=value).
  /// Host is editable as host:port; BaseAddress builds http://host:port/.
  /// </summary>
  public sealed class NetConf
  {
    public const string DefaultHost = "localhost:5080";

    /// <summary>Editable host string, default localhost:5080 (no scheme required).</summary>
    public string Host { get; set; } = DefaultHost;

    public static string FilePath => Path.Combine(TracingCache.SettingsDir, "net.conf");

    public static NetConf Load()
    {
      TracingCache.EnsureCreated();
      var conf = new NetConf();
      string path = FilePath;
      if (!File.Exists(path))
      {
        conf.Save();
        return conf;
      }

      try
      {
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
          string line = raw.Trim();
          if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
          {
            continue;
          }

          int eq = line.IndexOf('=');
          if (eq <= 0)
          {
            continue;
          }

          string key = line.Substring(0, eq).Trim();
          string value = line.Substring(eq + 1).Trim();
          if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.Host = value;
          }
          else if (key.Equals("BaseAddress", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            // Accept legacy full URL and normalize to host for the editor.
            conf.Host = StripScheme(value);
          }
        }
      }
      catch
      {
        // Keep defaults.
      }

      if (string.IsNullOrWhiteSpace(conf.Host))
      {
        conf.Host = DefaultHost;
      }

      return conf;
    }

    public void Save()
    {
      TracingCache.EnsureCreated();
      if (string.IsNullOrWhiteSpace(Host))
      {
        Host = DefaultHost;
      }

      var lines = new List<string>
      {
        "# NcTestRibbon net.conf — solver BaseAddress host",
        "Host=" + Host.Trim(),
        "BaseAddress=" + BuildBaseAddress(Host)
      };
      File.WriteAllText(FilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void Set(Action<NetConf> edit)
    {
      if (edit == null)
      {
        throw new ArgumentNullException(nameof(edit));
      }

      NetConf conf = Load();
      edit(conf);
      if (string.IsNullOrWhiteSpace(conf.Host))
      {
        throw new ArgumentException("Укажите хост (например localhost:5080).");
      }

      // Validate Uri construction early.
      _ = new Uri(BuildBaseAddress(conf.Host));
      conf.Save();
    }

    public static string BuildBaseAddress(string hostOrUrl)
    {
      string raw = (hostOrUrl ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(raw))
      {
        raw = DefaultHost;
      }

      if (!raw.Contains("://", StringComparison.Ordinal))
      {
        raw = "http://" + raw;
      }

      if (!raw.EndsWith("/", StringComparison.Ordinal))
      {
        raw += "/";
      }

      return raw;
    }

    public Uri BaseAddressUri => new Uri(BuildBaseAddress(Host));

    private static string StripScheme(string url)
    {
      string s = (url ?? string.Empty).Trim();
      if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
      {
        s = s.Substring("http://".Length);
      }
      else if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      {
        s = s.Substring("https://".Length);
      }

      return s.TrimEnd('/');
    }
  }
}
