namespace NcTestRibbon.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text;
  using NcTestRibbon.Shell;

  /// <summary>
  /// CAD label / insert-marker appearance in Tracing_cache\Settings\interface.conf (key=value).
  /// Shared by insert-point circles/labels and KP labels.
  /// </summary>
  public sealed class InterfaceConf
  {
    /// <summary>DBText height for KP / insert labels, meters (drawing units).</summary>
    public double LabelFontHeightM { get; set; } = 5.0;

    /// <summary>Insert-point circle diameter, meters.</summary>
    public double InsertCircleDiameterM { get; set; } = 10.0;

    public static string FilePath => Path.Combine(TracingCache.SettingsDir, "interface.conf");

    public static InterfaceConf Load()
    {
      TracingCache.EnsureCreated();
      var conf = new InterfaceConf();
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
          if (key.Equals("LabelFontHeightM", StringComparison.OrdinalIgnoreCase)
            && TryParseDouble(value, out double font) && font > 0)
          {
            conf.LabelFontHeightM = font;
          }
          else if (key.Equals("InsertCircleDiameterM", StringComparison.OrdinalIgnoreCase)
            && TryParseDouble(value, out double diam) && diam > 0)
          {
            conf.InsertCircleDiameterM = diam;
          }
        }
      }
      catch
      {
        // Keep defaults.
      }

      return conf;
    }

    public void Save()
    {
      TracingCache.EnsureCreated();
      var lines = new List<string>
      {
        "# NcTestRibbon interface.conf — CAD labels / insert markers",
        "LabelFontHeightM=" + Format(LabelFontHeightM),
        "InsertCircleDiameterM=" + Format(InsertCircleDiameterM)
      };
      File.WriteAllText(FilePath, string.Join(Environment.NewLine, lines) + Environment.NewLine,
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void Set(Action<InterfaceConf> edit)
    {
      if (edit == null)
      {
        throw new ArgumentNullException(nameof(edit));
      }

      InterfaceConf conf = Load();
      edit(conf);
      if (conf.LabelFontHeightM <= 0 || conf.LabelFontHeightM > 1000)
      {
        throw new ArgumentOutOfRangeException(nameof(conf.LabelFontHeightM), "Размер шрифта подписей: 0…1000 м.");
      }

      if (conf.InsertCircleDiameterM <= 0 || conf.InsertCircleDiameterM > 1000)
      {
        throw new ArgumentOutOfRangeException(nameof(conf.InsertCircleDiameterM), "Диаметр круга врезки: 0…1000 м.");
      }

      conf.Save();
    }

    private static bool TryParseDouble(string text, out double value)
    {
      return double.TryParse((text ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Format(double value)
    {
      return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
  }
}
