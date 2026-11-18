namespace NcTestRibbon.Shell
{
  using System;
  using System.IO;
  using System.Text;
  using System.Windows;

  /// <summary>
  /// Unified settings shell: tracing (former InitialData) + trench/road.
  /// Ensures Tracing_cache folders and stub conf files on open.
  /// </summary>
  public partial class SettingsWindow : Window
  {
    private readonly TrenchRoadSettingsPanel _trenchPanel = new TrenchRoadSettingsPanel();

    public SettingsWindow()
    {
      InitializeComponent();
      TracingCache.EnsureCreated();
      TrenchHost.Content = _trenchPanel;
      TxtCacheInfo.Text =
        "Кэш: " + TracingCache.Root
        + "  |  расценки: " + TracingCache.PriceXlsx
        + "  |  " + Path.GetFileName(TracingCache.TracingConf)
        + " + " + Path.GetFileName(TracingCache.TrenchRoadConf);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        TracingCache.EnsureCreated();
        WriteTracingConfStub();
        _trenchPanel.SaveStubConf();
        StubUi.Notify(
          this,
          "Settings.Save → "
          + TracingCache.TracingConf
          + " ; "
          + TracingCache.TrenchRoadConf);
        DialogResult = true;
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          this,
          ex.Message,
          "Проверьте настройки",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
      }
    }

    private static void WriteTracingConfStub()
    {
      var sb = new StringBuilder();
      sb.AppendLine("# tracing.conf — shell snapshot " + DateTime.Now.ToString("O"));
      sb.AppendLine("price_xlsx=" + TracingCache.PriceXlsx);
      sb.AppendLine("maps_dir=" + TracingCache.MapsDir);
      sb.AppendLine("source_data_dir=" + TracingCache.SourceDataDir);
      sb.AppendLine("# UI values are not serialized yet (shell only).");
      File.WriteAllText(TracingCache.TracingConf, sb.ToString(), Encoding.UTF8);
    }
  }
}
