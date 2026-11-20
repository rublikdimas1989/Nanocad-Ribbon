namespace NcTestRibbon.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Shared rectangular AOI + site-layer offsets in Tracing_cache\Settings\geometry.conf.
  /// Same key=value style as interface.conf.
  /// </summary>
  public sealed class GeometryConf
  {
    public bool HasAoi { get; set; }

    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    /// <summary>БУОС: от контура площадки до линии реки, м (End default 100).</summary>
    public double BuosRiverBufferM { get; set; } = 100;

    /// <summary>БУОС: от контура до дорог и существующих труб, м (End default 30).</summary>
    public double BuosInfraBufferM { get; set; } = 30;

    /// <summary>Узлы: от контура площадки до линии реки, м (End default 10).</summary>
    public double NodeRiverBufferM { get; set; } = 10;

    public bool ShowBuosLayer { get; set; }

    public bool ShowNodesLayer { get; set; }

    public static string FilePath => Path.Combine(TracingCache.SettingsDir, "geometry.conf");

    public static GeometryConf Load()
    {
      TracingCache.EnsureCreated();
      var conf = new GeometryConf();
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
          if (key.Equals("HasAoi", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool has))
          {
            conf.HasAoi = has;
          }
          else if (key.Equals("MinX", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double minX))
          {
            conf.MinX = minX;
          }
          else if (key.Equals("MinY", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double minY))
          {
            conf.MinY = minY;
          }
          else if (key.Equals("MaxX", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double maxX))
          {
            conf.MaxX = maxX;
          }
          else if (key.Equals("MaxY", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double maxY))
          {
            conf.MaxY = maxY;
          }
          else if (key.Equals("BuosRiverBufferM", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double br) && br >= 0)
          {
            conf.BuosRiverBufferM = br;
          }
          else if (key.Equals("BuosInfraBufferM", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double bi) && bi >= 0)
          {
            conf.BuosInfraBufferM = bi;
          }
          else if (key.Equals("NodeRiverBufferM", StringComparison.OrdinalIgnoreCase) && TryParseDouble(value, out double nr) && nr >= 0)
          {
            conf.NodeRiverBufferM = nr;
          }
          else if (key.Equals("ShowBuosLayer", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool sb))
          {
            conf.ShowBuosLayer = sb;
          }
          else if (key.Equals("ShowNodesLayer", StringComparison.OrdinalIgnoreCase) && TryParseBool(value, out bool sn))
          {
            conf.ShowNodesLayer = sn;
          }
        }
      }
      catch
      {
        // Keep defaults.
      }

      NormalizeAoi(conf);
      return conf;
    }

    public void Save()
    {
      TracingCache.EnsureCreated();
      NormalizeAoi(this);
      var lines = new List<string>
      {
        "# NcTestRibbon geometry.conf — shared AOI + site offsets",
        "HasAoi=" + (HasAoi ? "1" : "0"),
        "MinX=" + Format(MinX),
        "MinY=" + Format(MinY),
        "MaxX=" + Format(MaxX),
        "MaxY=" + Format(MaxY),
        "BuosRiverBufferM=" + Format(BuosRiverBufferM),
        "BuosInfraBufferM=" + Format(BuosInfraBufferM),
        "NodeRiverBufferM=" + Format(NodeRiverBufferM),
        "ShowBuosLayer=" + (ShowBuosLayer ? "1" : "0"),
        "ShowNodesLayer=" + (ShowNodesLayer ? "1" : "0")
      };
      File.WriteAllText(FilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void Set(Action<GeometryConf> edit)
    {
      if (edit == null)
      {
        throw new ArgumentNullException(nameof(edit));
      }

      GeometryConf conf = Load();
      edit(conf);
      if (conf.BuosRiverBufferM < 0 || conf.BuosInfraBufferM < 0 || conf.NodeRiverBufferM < 0
          || !double.IsFinite(conf.BuosRiverBufferM) || !double.IsFinite(conf.BuosInfraBufferM)
          || !double.IsFinite(conf.NodeRiverBufferM))
      {
        throw new ArgumentOutOfRangeException(nameof(conf), "Отступ должен быть конечным неотрицательным числом.");
      }

      conf.Save();
    }

    public void SetAoi(double x0, double y0, double x1, double y1)
    {
      MinX = Math.Min(x0, x1);
      MaxX = Math.Max(x0, x1);
      MinY = Math.Min(y0, y1);
      MaxY = Math.Max(y0, y1);
      HasAoi = MaxX > MinX && MaxY > MinY
        && double.IsFinite(MinX) && double.IsFinite(MinY)
        && double.IsFinite(MaxX) && double.IsFinite(MaxY);
    }

    public void ClearAoi()
    {
      HasAoi = false;
      MinX = MinY = MaxX = MaxY = 0;
    }

    private static void NormalizeAoi(GeometryConf conf)
    {
      if (!conf.HasAoi)
      {
        return;
      }

      if (!double.IsFinite(conf.MinX) || !double.IsFinite(conf.MinY)
          || !double.IsFinite(conf.MaxX) || !double.IsFinite(conf.MaxY)
          || conf.MaxX <= conf.MinX || conf.MaxY <= conf.MinY)
      {
        conf.HasAoi = false;
        return;
      }

      double minX = Math.Min(conf.MinX, conf.MaxX);
      double maxX = Math.Max(conf.MinX, conf.MaxX);
      double minY = Math.Min(conf.MinY, conf.MaxY);
      double maxY = Math.Max(conf.MinY, conf.MaxY);
      conf.MinX = minX;
      conf.MaxX = maxX;
      conf.MinY = minY;
      conf.MaxY = maxY;
    }

    private static bool TryParseDouble(string text, out double value)
    {
      return double.TryParse((text ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseBool(string text, out bool value)
    {
      string t = (text ?? string.Empty).Trim();
      if (t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase) || t.Equals("yes", StringComparison.OrdinalIgnoreCase))
      {
        value = true;
        return true;
      }

      if (t == "0" || t.Equals("false", StringComparison.OrdinalIgnoreCase) || t.Equals("no", StringComparison.OrdinalIgnoreCase))
      {
        value = false;
        return true;
      }

      value = false;
      return false;
    }

    private static string Format(double value)
    {
      return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
  }
}
