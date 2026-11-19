namespace NcTestRibbon.Shell
{
  using System;
  using System.IO;
  using System.Text;

  /// <summary>
  /// Local cache root for shell settings / maps / source data.
  /// %LocalAppData%\ASDC\LoadFile\NANOCAD\Tracing_cache\
  /// </summary>
  public static class TracingCache
  {
    public static string Root
    {
      get
      {
        return Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
          "ASDC",
          "LoadFile",
          "NANOCAD",
          "Tracing_cache");
      }
    }

    public static string SettingsDir => Path.Combine(Root, "Settings");
    public static string MapsDir => Path.Combine(Root, "Maps");
    public static string SourceDataDir => Path.Combine(Root, "SourceData");

    /// <summary>Preferred rates workbook name (xlsx). Kept for display / docs.</summary>
    public static string PriceXlsx => Path.Combine(SettingsDir, "Price.xlsx");

    /// <summary>Macro-enabled rates workbook alternate name.</summary>
    public static string PriceXlsm => Path.Combine(SettingsDir, "Price.xlsm");

    public static string TracingConf => Path.Combine(SettingsDir, "tracing.conf");
    public static string TrenchRoadConf => Path.Combine(SettingsDir, "trench_road.conf");
    public static string PathsJson => Path.Combine(SettingsDir, "paths.json");

    /// <summary>
    /// Resolve rates workbook: remembered paths.json priceWorkbook, else Price.xlsx, else Price.xlsm.
    /// Returns preferred xlsx path when neither exists (for UI / error messages).
    /// </summary>
    public static string ResolveRatesFilePath()
    {
      try
      {
        var remembered = Configuration.AppPaths.Load().PriceWorkbook;
        if (IsUsableRatesFile(remembered))
        {
          return remembered;
        }
      }
      catch
      {
        // Fall through to default Settings files.
      }

      if (IsUsableRatesFile(PriceXlsx))
      {
        return PriceXlsx;
      }

      if (IsUsableRatesFile(PriceXlsm))
      {
        return PriceXlsm;
      }

      return PriceXlsx;
    }

    public static bool RatesFileExists()
    {
      return IsUsableRatesFile(PriceXlsx) || IsUsableRatesFile(PriceXlsm);
    }

    private static bool IsUsableRatesFile(string path)
    {
      try
      {
        return File.Exists(path) && new FileInfo(path).Length > 0;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>Create cache folders and placeholder conf files. Safe to call on every open.</summary>
    public static void EnsureCreated()
    {
      Directory.CreateDirectory(SettingsDir);
      Directory.CreateDirectory(MapsDir);
      Directory.CreateDirectory(SourceDataDir);

      // tracing.conf is created/overwritten as JSON by AppSettings on first use.
      // Do not create an empty Price workbook — user places Price.xlsx or Price.xlsm there.
      EnsureTextFile(
        TrenchRoadConf,
        "# NcTestRibbon trench/road settings (shell placeholder)\r\n"
        + "# Path is fixed under Tracing_cache\\Settings\\\r\n");
    }

    private static void EnsureTextFile(string path, string contents)
    {
      if (File.Exists(path))
      {
        return;
      }

      File.WriteAllText(path, contents, Encoding.UTF8);
    }
  }
}
