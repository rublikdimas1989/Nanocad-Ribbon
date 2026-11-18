namespace NcTestRibbon.Shell
{
  using System.Windows;

  public partial class InitialData : Window
  {
    public InitialData()
    {
      InitializeComponent();
      try { StatusText.Text = "UI-оболочка. Расчёт на сервере — позже."; } catch { }
    }

private void AdditionalMedium_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.AdditionalMedium_SelectionChanged");
    }

private void BtnLoadRatesFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.BtnLoadRatesFile_Click");
    }

private void BtnLoadRates_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.BtnLoadRates_Click");
    }

private void BtnSave_Click(object sender, System.Windows.RoutedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.BtnSave_Click");
    }

private void CmbDiameter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.CmbDiameter_SelectionChanged");
    }

private void DownsampleFactor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.DownsampleFactor_SelectionChanged");
    }

private void PatRadius_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.PatRadius_TextChanged");
    }

private void PipeParameter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
      StubUi.Notify(this, "InitialData.PipeParameter_SelectionChanged");
    }
  }
}
