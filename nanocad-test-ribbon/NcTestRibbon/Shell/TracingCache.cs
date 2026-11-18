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

    /// <summary>Hard-bound rates workbook (not browsable).</summary>
    public static string PriceXlsx => Path.Combine(SettingsDir, "Price.xlsx");

    public static string TracingConf => Path.Combine(SettingsDir, "tracing.conf");
    public static string TrenchRoadConf => Path.Combine(SettingsDir, "trench_road.conf");

    /// <summary>Create cache folders and placeholder conf files. Safe to call on every open.</summary>
    public static void EnsureCreated()
    {
      Directory.CreateDirectory(SettingsDir);
      Directory.CreateDirectory(MapsDir);
      Directory.CreateDirectory(SourceDataDir);

      EnsureTextFile(
        TracingConf,
        "# NcTestRibbon tracing settings (shell placeholder)\r\n"
        + "# Real save/load later. Path is fixed under Tracing_cache\\Settings\\\r\n");

      EnsureTextFile(
        TrenchRoadConf,
        "# NcTestRibbon trench/road settings (shell placeholder)\r\n"
        + "# Real save/load later. Path is fixed under Tracing_cache\\Settings\\\r\n");

      // Touch empty Price.xlsx only if missing — user replaces with real workbook.
      if (!File.Exists(PriceXlsx))
      {
        File.WriteAllBytes(PriceXlsx, Array.Empty<byte>());
      }
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
