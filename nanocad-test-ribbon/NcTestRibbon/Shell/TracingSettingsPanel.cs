namespace NcTestRibbon.Shell
{
  using System.Windows;
  using System.Windows.Controls;

  /// <summary>
  /// Former InitialData content — tab «Настройки трассировки».
  /// UI loaded from embedded XAML (no Page/BAML pack URI).
  /// </summary>
  public class TracingSettingsPanel : UserControl
  {
    private readonly FrameworkElement _root;
    private readonly TextBlock _statusText;
    private readonly TextBlock _ratesPath;

    public TracingSettingsPanel()
    {
      _root = ShellXaml.Load(ShellXaml.TracingSettingsResource);
      Content = _root;

      _statusText = ShellXaml.Find<TextBlock>(_root, "StatusText");
      _ratesPath = ShellXaml.Find<TextBlock>(_root, "TxtRatesFilePath");

      if (_statusText != null)
      {
        _statusText.Text = "UI-оболочка. Расчёт на сервере — позже.";
      }

      if (_ratesPath != null)
      {
        _ratesPath.Text = TracingCache.PriceXlsx;
      }

      Wire(ShellXaml.Find<ComboBox>(_root, "DownsampleFactor"), DownsampleFactor_SelectionChanged);
      Wire(ShellXaml.Find<ComboBox>(_root, "CmbEnvironment"), PipeParameter_SelectionChanged);
      Wire(ShellXaml.Find<ComboBox>(_root, "CmbDiameter"), CmbDiameter_SelectionChanged);
      Wire(ShellXaml.Find<ComboBox>(_root, "CmbThickness"), PipeParameter_SelectionChanged);
      Wire(ShellXaml.Find<ComboBox>(_root, "CmbSteelGrade"), PipeParameter_SelectionChanged);
      Wire(ShellXaml.Find<ComboBox>(_root, "CmbPipeType"), PipeParameter_SelectionChanged);
      Wire(ShellXaml.Find<ComboBox>(_root, "CmbAdditionalMedium"), AdditionalMedium_SelectionChanged);

      TextBox patRadius = ShellXaml.Find<TextBox>(_root, "TxtPatNaturalRadius");
      if (patRadius != null)
      {
        patRadius.TextChanged += PatRadius_TextChanged;
      }

      Button applyRates = ShellXaml.Find<Button>(_root, "BtnLoadRates");
      if (applyRates != null)
      {
        applyRates.Click += BtnLoadRates_Click;
      }
    }

    private static void Wire(ComboBox combo, SelectionChangedEventHandler handler)
    {
      if (combo != null)
      {
        combo.SelectionChanged += handler;
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
