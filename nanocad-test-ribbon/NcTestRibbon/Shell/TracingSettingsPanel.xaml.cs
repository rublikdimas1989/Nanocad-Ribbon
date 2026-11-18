namespace NcTestRibbon.Shell
{
  using System.Windows;
  using System.Windows.Controls;

  /// <summary>Former InitialData content — tab «Настройки трассировки».</summary>
  public partial class TracingSettingsPanel : UserControl
  {
    public TracingSettingsPanel()
    {
      InitializeComponent();
      try
      {
        StatusText.Text = "UI-оболочка. Расчёт на сервере — позже.";
        TxtRatesFilePath.Text = TracingCache.PriceXlsx;
      }
      catch
      {
      }
    }

    private void AdditionalMedium_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "TracingSettings.AdditionalMedium_SelectionChanged");
    }

    private void BtnLoadRates_Click(object sender, RoutedEventArgs e)
    {
      StubUi.Notify(this, "TracingSettings.ApplyRates → " + TracingCache.PriceXlsx);
    }

    private void CmbDiameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "TracingSettings.CmbDiameter_SelectionChanged");
    }

    private void DownsampleFactor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "TracingSettings.DownsampleFactor_SelectionChanged");
    }

    private void PatRadius_TextChanged(object sender, TextChangedEventArgs e)
    {
      StubUi.Notify(this, "TracingSettings.PatRadius_TextChanged");
    }

    private void PipeParameter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "TracingSettings.PipeParameter_SelectionChanged");
    }
  }
}
