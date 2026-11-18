namespace NcTestRibbon.Configuration
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Linq;
  using System.Text.Json;
  using NcTestRibbon.Cost;

  /// <summary>Three independent option snapshots (End PipeMediumSettings).</summary>
  public static class PipeMediumSettings
  {
    public const string StorageKey = "PipeMediumOptions";
    public static readonly string[] Media = { "gas", "oil", "liquid" };

    public static string Medium(string value)
    {
      return value is "gas" or "Газ" or "Газопровод" ? "gas"
        : value is "oil" or "Нефть" or "Нефтепровод" ? "oil"
        : "liquid";
    }

    public static string Label(string medium)
    {
      return Medium(medium) == "gas" ? "Газ" : Medium(medium) == "oil" ? "Нефть" : "Жидкость";
    }

    public static readonly string[] BooleanKeys =
    {
      "PneumoTest", "HydroTest", "PrelimPneumo", "PrelimHydro", "PPUInsulation", "JointInsulation", "EHS",
      "RiverInstallUza", "RiverUseCase", "RiverUseLiner", "RiverBallasting", "SwampBallasting", "Gabions", "GeoGrid", "RenoMattress",
      "GeoPipeDefmark", "GeoPipeReper", "GeoPipeThermo", "GeoBendDefmark", "GeoBendReper", "GeoBendThermo",
      "GeoUzaDefmark", "GeoUzaReper", "GeoUzaThermo", "DiagnosticOther", "InLineInspection"
    };

    public static readonly Dictionary<string, string> TextDefaults = new Dictionary<string, string>
    {
      ["RiverTransitionType"] = "Переход реки закрытый (ГНБ)",
      ["RoadTransitionType"] = "Переход дороги закрытый (ГНБ)",
      ["PipeTransitionType"] = "Переход трубы закрытый (ГНБ)",
      ["MinRiverCrossing"] = "130",
      ["MinRoadCrossing"] = "50",
      ["MinPipeCrossingNormal"] = "30",
      ["MinPipeCrossingGas"] = "50",
      ["GabionsAreaPerBankM2"] = "200",
      ["GeoGridAreaPerBankM2"] = "200",
      ["RenoAreaPerBankM2"] = "200",
      ["GeoDefmarkSpacingMeters"] = "800",
      ["GeoReperSpacingMeters"] = "800",
      ["GeoThermoSpacingMeters"] = "800",
      ["Переход_Река_МинУгол"] = "60",
      ["Переход_Река_МаксУгол"] = "90",
      ["Переход_Дорога_МинУгол"] = "60",
      ["Переход_Дорога_МаксУгол"] = "90",
      ["Переход_ТрубаБезГаза_МинУгол"] = "60",
      ["Переход_ТрубаБезГаза_МаксУгол"] = "90",
      ["Переход_ТрубаСГазом_МинУгол"] = "60",
      ["Переход_ТрубаСГазом_МаксУгол"] = "90"
    };

    public static Dictionary<string, Dictionary<string, string>> Load(SettingsSection section)
    {
      var legacy = TextDefaults.ToDictionary(
        p => p.Key,
        p => Convert.ToString(section.GetValue(p.Key, p.Value), CultureInfo.InvariantCulture) ?? p.Value);

      foreach (string key in BooleanKeys)
      {
        bool fallback = key == "RiverInstallUza"
          || (key is "RiverUseCase" or "RiverUseLiner" && legacy["RiverTransitionType"].Contains("открыт"));
        if (key.StartsWith("Geo", StringComparison.Ordinal)
            && new[] { "Defmark", "Reper", "Thermo" }.Any(key.EndsWith))
        {
          string suffix = new[] { "Defmark", "Reper", "Thermo" }.First(key.EndsWith);
          fallback = Convert.ToString(section.GetValue(key[..^suffix.Length] + "Mode")) == suffix;
        }

        object value = section.GetValue(key, fallback);
        legacy[key] = (value is int i ? i != 0 : bool.TryParse(Convert.ToString(value), out bool b) && b).ToString();
      }

      foreach (string key in new[] { "GeoDefmarkSpacingMeters", "GeoReperSpacingMeters", "GeoThermoSpacingMeters" })
      {
        legacy[key] = Convert.ToString(
          section.GetValue(key, section.GetValue("GeoMaxSpacingMeters", "800")),
          CultureInfo.InvariantCulture) ?? "800";
      }

      Dictionary<string, Dictionary<string, string>> stored =
        section.Get<Dictionary<string, Dictionary<string, string>>>(StorageKey);

      return Media.ToDictionary(m => m, m =>
      {
        var profile = new Dictionary<string, string>(legacy);
        if (stored != null && stored.TryGetValue(m, out Dictionary<string, string> saved) && saved != null)
        {
          foreach (KeyValuePair<string, string> p in saved)
          {
            if (profile.ContainsKey(p.Key))
            {
              profile[p.Key] = p.Value;
            }
          }
        }

        return profile;
      });
    }

    public static void Validate(IReadOnlyDictionary<string, string> profile)
    {
      foreach (string key in BooleanKeys)
      {
        if (!profile.TryGetValue(key, out string value) || !bool.TryParse(value, out _))
        {
          throw new ArgumentException("Некорректный параметр: " + key);
        }
      }

      foreach (KeyValuePair<string, string> item in TextDefaults)
      {
        string value = profile.TryGetValue(item.Key, out string v) ? v : item.Value;
        if (item.Key.EndsWith("TransitionType", StringComparison.Ordinal))
        {
          if (value != item.Value && value != item.Value.Replace("закрытый (ГНБ)", "открытый"))
          {
            throw new ArgumentException("Некорректный тип перехода: " + value);
          }

          continue;
        }

        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            || !double.IsFinite(n) || n < 0
            || (item.Key.Contains("Spacing", StringComparison.Ordinal) && n <= 0))
        {
          throw new ArgumentException(
            item.Key + ": требуется конечное неотрицательное число; шаг мониторинга должен быть больше нуля.");
        }
      }

      foreach (string prefix in new[] { "Переход_Река", "Переход_Дорога", "Переход_ТрубаБезГаза", "Переход_ТрубаСГазом" })
      {
        _ = CrossingAngleSettings.Read(profile, prefix);
      }
    }

    public static bool Enabled(IReadOnlyDictionary<string, string> p, string key)
    {
      return p.TryGetValue(key, out string s) && bool.TryParse(s, out bool b) && b;
    }

    public static void Apply(Dictionary<string, string> rates, IReadOnlyDictionary<string, string> p)
    {
      foreach (KeyValuePair<string, string> item in p)
      {
        string key = item.Key switch
        {
          "RiverUseCase" => "RiverCase",
          "RiverUseLiner" => "RiverLiner",
          _ => item.Key
        };
        rates[item.Key.StartsWith("Переход_", StringComparison.Ordinal) ? item.Key : "Cfg_" + key] = item.Value;
      }

      foreach (string kind in new[] { "River", "Road", "Pipe" })
      {
        rates[kind + "Transition"] = p[kind + "TransitionType"];
      }

      foreach (var pair in new[]
               {
                 ("MinRiverCrossing", "lenghtRiverCrossing"),
                 ("MinRoadCrossing", "lenghtRoadCrossing"),
                 ("MinPipeCrossingNormal", "lenghtTubeNonGasCrossing"),
                 ("MinPipeCrossingGas", "lenghtTubeWithGasCrossing")
               })
      {
        rates[pair.Item2] = p[pair.Item1];
      }
    }
  }
}
