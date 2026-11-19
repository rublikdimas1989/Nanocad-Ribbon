namespace NcTestRibbon.Configuration
{
  using System;
  using System.IO;
  using System.Text;
  using System.Text.Json;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Remembered local file paths under Tracing_cache\Settings\paths.json.
  /// Stored portable: relative under Tracing_cache when possible, else %LOCALAPPDATA%\…;
  /// expanded to absolute paths in memory on Load.
  /// </summary>
  public sealed class AppPaths
  {
    public int Version { get; set; } = 2;

    /// <summary>Last opened KP / Top_tree workbook (xlsx).</summary>
    public string PadsWorkbook { get; set; } = string.Empty;

    /// <summary>Last opened inserts / tie-ins workbook (xlsx, sheet «Врезки»).</summary>
    public string InsertsWorkbook { get; set; } = string.Empty;

    /// <summary>Last opened BUOS variants workbook (xlsx, sheet «БУОС»).</summary>
    public string BuosWorkbook { get; set; } = string.Empty;

    /// <summary>Last opened junction/node types workbook (xlsx, sheet «Узлы»).</summary>
    public string NodesWorkbook { get; set; } = string.Empty;

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
          data.Version = 2;
        }

        string[] raw =
        {
          data.PadsWorkbook,
          data.InsertsWorkbook,
          data.BuosWorkbook,
          data.NodesWorkbook,
          data.PriceWorkbook,
          data.FluidProperties,
          data.Background,
          data.ProfilesJson
        };

        data.ExpandStoredPaths();

        // Migrate absolute LocalAppData / cache paths to portable form on first load.
        if (NeedsPortableRewrite(raw))
        {
          try { data.Save(); }
          catch { }
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
      Version = 2;
      CompactPathsForStorage();
      string json = JsonSerializer.Serialize(this, JsonOptions);
      File.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
      ExpandStoredPaths();
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

    /// <summary>Expand env tokens and resolve relative paths against Tracing_cache.</summary>
    public void ExpandStoredPaths()
    {
      PadsWorkbook = ExpandPath(PadsWorkbook);
      InsertsWorkbook = ExpandPath(InsertsWorkbook);
      BuosWorkbook = ExpandPath(BuosWorkbook);
      NodesWorkbook = ExpandPath(NodesWorkbook);
      PriceWorkbook = ExpandPath(PriceWorkbook);
      FluidProperties = ExpandPath(FluidProperties);
      Background = ExpandPath(Background);
      ProfilesJson = ExpandPath(ProfilesJson);
    }

    /// <summary>Store portable: relative under Tracing_cache, else %LOCALAPPDATA%\…</summary>
    public void CompactPathsForStorage()
    {
      PadsWorkbook = CompactPath(PadsWorkbook);
      InsertsWorkbook = CompactPath(InsertsWorkbook);
      BuosWorkbook = CompactPath(BuosWorkbook);
      NodesWorkbook = CompactPath(NodesWorkbook);
      PriceWorkbook = CompactPath(PriceWorkbook);
      FluidProperties = CompactPath(FluidProperties);
      Background = CompactPath(Background);
      ProfilesJson = CompactPath(ProfilesJson);
    }

    public static string ExpandPath(string stored)
    {
      if (string.IsNullOrWhiteSpace(stored))
      {
        return string.Empty;
      }

      string s = Environment.ExpandEnvironmentVariables(stored.Trim());
      s = s.Replace('/', Path.DirectorySeparatorChar);
      if (!Path.IsPathRooted(s))
      {
        s = Path.GetFullPath(Path.Combine(TracingCache.Root, s));
      }
      else
      {
        try { s = Path.GetFullPath(s); }
        catch { }
      }

      return s;
    }

    public static string CompactPath(string absoluteOrStored)
    {
      if (string.IsNullOrWhiteSpace(absoluteOrStored))
      {
        return string.Empty;
      }

      string full;
      try
      {
        full = Path.GetFullPath(ExpandPath(absoluteOrStored));
      }
      catch
      {
        return absoluteOrStored.Trim();
      }

      string cacheRoot = Path.GetFullPath(TracingCache.Root);
      if (IsUnderDirectory(full, cacheRoot))
      {
        return ToRelative(cacheRoot, full);
      }

      string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      if (!string.IsNullOrEmpty(localAppData))
      {
        localAppData = Path.GetFullPath(localAppData);
        if (IsUnderDirectory(full, localAppData))
        {
          string rest = full.Substring(localAppData.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
          return "%LOCALAPPDATA%\\" + rest.Replace(Path.AltDirectorySeparatorChar, '\\');
        }
      }

      return full;
    }

    private static bool NeedsPortableRewrite(string[] rawStored)
    {
      foreach (string raw in rawStored)
      {
        if (string.IsNullOrWhiteSpace(raw))
        {
          continue;
        }

        string trimmed = raw.Trim();
        if (trimmed.IndexOf("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase) >= 0
          || trimmed.IndexOf("%LocalAppData%", StringComparison.OrdinalIgnoreCase) >= 0)
        {
          continue;
        }

        // Relative under cache is already portable.
        if (!Path.IsPathRooted(Environment.ExpandEnvironmentVariables(trimmed)))
        {
          continue;
        }

        string compact = CompactPath(trimmed);
        if (!string.Equals(NormalizeCompare(trimmed), NormalizeCompare(compact), StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }

      return false;
    }

    private static string NormalizeCompare(string path)
    {
      return (path ?? string.Empty).Trim().Replace('/', '\\');
    }

    private static bool IsUnderDirectory(string fullPath, string directory)
    {
      if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(directory))
      {
        return false;
      }

      string prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
      return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        || string.Equals(fullPath, directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRelative(string root, string fullPath)
    {
      string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
      string relative = fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        ? fullPath.Substring(prefix.Length)
        : fullPath;
      return relative.Replace(Path.AltDirectorySeparatorChar, '\\');
    }
  }
}
