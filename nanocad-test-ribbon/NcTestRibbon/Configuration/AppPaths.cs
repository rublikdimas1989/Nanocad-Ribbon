namespace NcTestRibbon.Configuration
{
  using System;
  using System.IO;
  using System.Text;
  using System.Text.Json;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Remembered local file paths under Tracing_cache\Settings\paths.json.
  /// Used by pad window / settings — not hardcoded only.
  /// </summary>
  public sealed class AppPaths
  {
    public int Version { get; set; } = 1;

    /// <summary>Last opened KP / Top_tree workbook (xlsx).</summary>
    public string PadsWorkbook { get; set; } = string.Empty;

    /// <summary>Last opened inserts / tie-ins workbook (xlsx, sheet «Врезки»).</summary>
    public string InsertsWorkbook { get; set; } = string.Empty;

    /// <summary>Last rates workbook (Price.xlsx / .xlsm or custom path).</summary>
    public string PriceWorkbook { get; set; } = string.Empty;

    /// <summary>Excel fluid properties book.</summary>
    public string FluidProperties { get; set; } = string.Empty;

    /// <summary>Background raster path.</summary>
    public string Background { get; set; } = string.Empty;

    /// <summary>Last Top_tree / profiles JSON.</summary>
    public string ProfilesJson { get; set; } = string.Empty;

    /// <summary>Remember «Показывать на чертеже» for the pad window.</summary>
    public bool ShowPadsOnDrawing { get; set; }

    /// <summary>Remember «Показывать на чертеже» for insert points.</summary>
    public bool ShowInsertsOnDrawing { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(TracingCache.SettingsDir, "paths.json");

    public static AppPaths Load()
    {
      TracingCache.EnsureCreated();
      string path = FilePath;
      if (!File.Exists(path))
      {
        var fresh = new AppPaths();
        fresh.Save();
        return fresh;
      }

      try
      {
        string text = File.ReadAllText(path, Encoding.UTF8);
        AppPaths data = JsonSerializer.Deserialize<AppPaths>(text, JsonOptions) ?? new AppPaths();
        if (data.Version <= 0)
        {
          data.Version = 1;
        }

        return data;
      }
      catch
      {
        return new AppPaths();
      }
    }

    public void Save()
    {
      TracingCache.EnsureCreated();
      Version = 1;
      string json = JsonSerializer.Serialize(this, JsonOptions);
      File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void Set(Action<AppPaths> edit)
    {
      if (edit == null)
      {
        throw new ArgumentNullException(nameof(edit));
      }

      AppPaths paths = Load();
      edit(paths);
      paths.Save();
    }
  }
}
