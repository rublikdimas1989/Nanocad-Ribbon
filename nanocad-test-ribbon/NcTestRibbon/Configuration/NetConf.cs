namespace NcTestRibbon.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Solver / map-upload host in Tracing_cache\Settings\net.conf (key=value).
  /// Host is editable as host:port; BaseAddress builds http://host:port/.
  /// DatasetId is written after a successful DXF upload (api/v1/datasets).
  /// </summary>
  public sealed class NetConf
  {
    public const string DefaultHost = "localhost:5080";

    /// <summary>Default relative path for BUOS site search (POST JSON).</summary>
    public const string DefaultBuosSearchPath = "api/v1/buos/search";

    /// <summary>Editable host string, default localhost:5080 (no scheme required).</summary>
    public string Host { get; set; } = DefaultHost;

    /// <summary>Last dataset id returned by POST api/v1/datasets (DXF upload).</summary>
    public string DatasetId { get; set; } = string.Empty;

    /// <summary>
    /// Relative solver path for «Рассчитать слой БУОС» (no leading slash).
    /// Override in net.conf if the server uses a different route.
    /// </summary>
    public string BuosSearchPath { get; set; } = DefaultBuosSearchPath;

    /// <summary>Comma-separated CAD layer names used as river geometry (default «Реки»).</summary>
    public string RiverLayers { get; set; } = "Реки";

    /// <summary>Comma-separated forbidden-zone layer names.</summary>
    public string ForbiddenLayers { get; set; } = "Запретные зоны";

    /// <summary>Target placement layer name sent to the solver (DXF constraint name).</summary>
    public string BuosPlacementLayer { get; set; } = "Размещение БУОС";

    /// <summary>CAD layer for drawn BUOS footprints (default NC_BUOS_SITES).</summary>
    public string BuosCadLayer { get; set; } = "NC_BUOS_SITES";

    public double CenterStepM { get; set; } = 50;

    public double TerrainSampleStepM { get; set; } = 10;

    /// <summary>Comma-separated rotation angles in degrees (default 0,30,…,150).</summary>
    public string RotationAnglesDeg { get; set; } = "0,30,60,90,120,150";

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
          else if (key.Equals("DatasetId", StringComparison.OrdinalIgnoreCase))
          {
            conf.DatasetId = value ?? string.Empty;
          }
          else if (key.Equals("BuosSearchPath", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.BuosSearchPath = NormalizeRelativePath(value);
          }
          else if (key.Equals("RiverLayers", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.RiverLayers = value;
          }
          else if (key.Equals("ForbiddenLayers", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.ForbiddenLayers = value;
          }
          else if (key.Equals("BuosPlacementLayer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.BuosPlacementLayer = value;
          }
          else if (key.Equals("BuosCadLayer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.BuosCadLayer = value;
          }
          else if (key.Equals("CenterStepM", StringComparison.OrdinalIgnoreCase)
              && double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double cs)
              && cs > 0 && double.IsFinite(cs))
          {
            conf.CenterStepM = cs;
          }
          else if (key.Equals("TerrainSampleStepM", StringComparison.OrdinalIgnoreCase)
              && double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ts)
              && ts > 0 && double.IsFinite(ts))
          {
            conf.TerrainSampleStepM = ts;
          }
          else if (key.Equals("RotationAnglesDeg", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
          {
            conf.RotationAnglesDeg = value;
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

      if (string.IsNullOrWhiteSpace(conf.BuosSearchPath))
      {
        conf.BuosSearchPath = DefaultBuosSearchPath;
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

      if (string.IsNullOrWhiteSpace(BuosSearchPath))
      {
        BuosSearchPath = DefaultBuosSearchPath;
      }
      else
      {
        BuosSearchPath = NormalizeRelativePath(BuosSearchPath);
      }

      var lines = new List<string>
      {
        "# NcTestRibbon net.conf — solver host + last DXF datasetId + BUOS search",
        "# Host / BaseAddress: upload + search base URL",
        "# DatasetId: written after successful POST api/v1/datasets (DXF upload)",
        "# BuosSearchPath: relative POST path for «Рассчитать слой БУОС» (default "
          + DefaultBuosSearchPath + ")",
        "Host=" + Host.Trim(),
        "BaseAddress=" + BuildBaseAddress(Host),
        "DatasetId=" + (DatasetId ?? string.Empty).Trim(),
        "BuosSearchPath=" + BuosSearchPath.Trim(),
        "RiverLayers=" + (RiverLayers ?? string.Empty).Trim(),
        "ForbiddenLayers=" + (ForbiddenLayers ?? string.Empty).Trim(),
        "BuosPlacementLayer=" + (BuosPlacementLayer ?? string.Empty).Trim(),
        "BuosCadLayer=" + (string.IsNullOrWhiteSpace(BuosCadLayer) ? "NC_BUOS_SITES" : BuosCadLayer.Trim()),
        "CenterStepM=" + CenterStepM.ToString("0.###", CultureInfo.InvariantCulture),
        "TerrainSampleStepM=" + TerrainSampleStepM.ToString("0.###", CultureInfo.InvariantCulture),
        "RotationAnglesDeg=" + (RotationAnglesDeg ?? string.Empty).Trim()
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

    public string[] ParseLayerList(string csv)
    {
      if (string.IsNullOrWhiteSpace(csv))
      {
        return Array.Empty<string>();
      }

      return csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToArray();
    }

    public double[] ParseRotationAngles()
    {
      string raw = RotationAnglesDeg ?? string.Empty;
      var list = new List<double>();
      foreach (string part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
      {
        if (double.TryParse(part.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)
            && double.IsFinite(deg))
        {
          list.Add(deg);
        }
      }

      if (list.Count == 0)
      {
        return new[] { 0d, 30d, 60d, 90d, 120d, 150d };
      }

      return list.ToArray();
    }

    private static string NormalizeRelativePath(string path)
    {
      string p = (path ?? string.Empty).Trim().Replace('\\', '/');
      while (p.StartsWith("/", StringComparison.Ordinal))
      {
        p = p.Substring(1);
      }

      return string.IsNullOrWhiteSpace(p) ? DefaultBuosSearchPath : p;
    }

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
