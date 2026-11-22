namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Collections.ObjectModel;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;
  // AppPaths remembers Price / workbook locations in Tracing_cache\Settings\paths.json.

  /// <summary>
  /// Tab «Настройки трассировки»: local End-variant logic —
  /// Price.xlsx / Price.xlsm rates + PipeMedium transitions saved to tracing.conf.
  /// </summary>
  public class TracingSettingsPanel : UserControl
  {
    private readonly FrameworkElement _root;
    private readonly DataGrid _dgRates;
    private readonly TextBlock _statusText;
    private readonly TextBox _ratesPath;
    private readonly TextBlock _catalogStatus;
    private readonly TextBlock _emptyHint;
    private readonly ObservableCollection<RateItem> _rates = new ObservableCollection<RateItem>();

    private Dictionary<string, Dictionary<string, string>> _mediumOptions;
    private string _editingMedium = "gas";
    private bool _changingMedium;
    private bool _loadingPipeCatalog;
    private List<PipeCatalogRow> _pipeCatalog = new List<PipeCatalogRow>();

    public TracingSettingsPanel()
    {
      _root = ShellXaml.Load(ShellXaml.TracingSettingsResource);
      Content = _root;

      _statusText = Find<TextBlock>("StatusText");
      _ratesPath = Find<TextBox>("TxtRatesFilePath");
      _catalogStatus = Find<TextBlock>("TxtPipeCatalogStatus");
      _emptyHint = Find<TextBlock>("TblEmptyHint");
      _dgRates = Find<DataGrid>("DgRates");
      if (_dgRates != null)
      {
        _dgRates.ItemsSource = _rates;
      }

      if (_ratesPath != null)
      {
        PathTextDisplay.SetPath(_ratesPath, TracingCache.ResolveRatesFilePath());
        _ratesPath.Loaded += (_, __) => PathTextDisplay.ShowEnd(_ratesPath);
      }

      Wire(Find<ComboBox>("DownsampleFactor"), (_, __) => { });
      Wire(Find<ComboBox>("CmbEnvironment"), (_, __) => OnPipeParameterChanged());
      Wire(Find<ComboBox>("CmbDiameter"), (_, __) => OnPipeParameterChanged());
      Wire(Find<ComboBox>("CmbThickness"), (_, __) => OnPipeParameterChanged());
      Wire(Find<ComboBox>("CmbSteelGrade"), (_, __) => OnPipeParameterChanged());
      Wire(Find<ComboBox>("CmbPipeType"), (_, __) => OnPipeParameterChanged());
      Wire(Find<ComboBox>("CmbAdditionalMedium"), AdditionalMedium_SelectionChanged);

      Button applyRates = Find<Button>("BtnLoadRates");
      if (applyRates != null)
      {
        applyRates.Click += (_, __) => ApplyRates(showErrors: true);
      }

      try
      {
        TracingCache.EnsureCreated();
        AppSettings.Initialize();
        LoadGeneralFromConfig();
        InitializeMediumEditors();
        if (TracingCache.RatesFileExists())
        {
          ApplyRates(showErrors: false);
        }
        else if (_statusText != null)
        {
          _statusText.Text =
            "Положите Price.xlsx или Price.xlsm в Tracing_cache\\Settings\\ и нажмите «Применить расценки».";
        }
      }
      catch (Exception ex)
      {
        if (_statusText != null)
        {
          _statusText.Text = "Ошибка загрузки настроек: " + ex.Message;
        }
      }
    }

    public void SaveToConfig()
    {
      CaptureMedium();
      if (_mediumOptions != null)
      {
        foreach (KeyValuePair<string, Dictionary<string, string>> pair in _mediumOptions)
        {
          PipeMediumSettings.Validate(pair.Value);
        }
      }

      using SettingsSection section = AppSettings.OpenSection(AppSettings.General);
      section.SetValue("RatesFilePath", TracingCache.ResolveRatesFilePath());
      section.SetValue("BlockX", TextOf("BlockX"));
      section.SetValue("BlockY", TextOf("BlockY"));
      section.SetValue("BlockW", TextOf("BlockW"));
      section.SetValue("BlockH", TextOf("BlockH"));
      section.SetValue("MaxIter", TextOf("MaxIter"));
      section.SetValue("Environment", ComboText("CmbEnvironment"));
      section.SetValue("Diameter", ComboText("CmbDiameter"));
      section.SetValue("Thickness", ComboText("CmbThickness"));
      section.SetValue("SteelGrade", ComboText("CmbSteelGrade"));
      section.SetValue("PipeType", ComboText("CmbPipeType"));
      section.SetValue("CasingDiameter", TextOf("TxtCasingDiameter"));
      section.SetValue("SteelRoughness", TextOf("TxtSteelRoughness"));
      section.SetValue("PatRoughness", TextOf("TxtPatRoughness"));
      section.SetValue("PatNaturalRadius", TextOf("TxtPatNaturalRadius"));
      section.SetValue("MinRadius", TextOf("TxtMinRadius"));
      section.SetValue("ProfileMinRadius", TextOf("TxtProfileMinRadius"));
      section.SetValue("MaxLongitudinalSurfaceSlope", TextOf("TxtMaxLongitudinalSurfaceSlope"));
      section.SetValue("MaxTransverseSlope", TextOf("TxtMaxTransverseSlope"));
      section.SetValue("CorridorWidth", TextOf("TxtCorridorWidth"));
      section.SetValue("StartSiteDiameter", TextOf("TxtStartSiteDiameter"));
      section.SetValue("FinishSiteDiameter", TextOf("TxtFinishSiteDiameter"));
      section.SetValue("TypicalAngles", TextOf("TxtTypicalAngles"));
      section.SetValue("RiverProtectionZone", TextOf("TxtRiverProtectionZone"));
      section.SetValue("MinDepth", TextOf("TxtMinDepth"));
      section.SetValue("MinStep", TextOf("TxtMinStep"));

      if (_mediumOptions != null)
      {
        section.SetValue(PipeMediumSettings.StorageKey, _mediumOptions);
        string pipeMedium = PipeMediumSettings.Medium(ComboText("CmbEnvironment"));
        foreach (KeyValuePair<string, string> p in _mediumOptions[pipeMedium])
        {
          section.SetValue(p.Key, p.Value);
        }
      }

      section.Commit();
      if (_statusText != null)
      {
        _statusText.Text = "Сохранено в " + TracingCache.TracingConf;
      }
    }

    private void LoadGeneralFromConfig()
    {
      using SettingsSection section = AppSettings.OpenSection(AppSettings.General);
      SetText("BlockX", section.GetValue("BlockX", "0"));
      SetText("BlockY", section.GetValue("BlockY", "0"));
      SetText("BlockW", section.GetValue("BlockW", "2000"));
      SetText("BlockH", section.GetValue("BlockH", "2000"));
      SetText("MaxIter", section.GetValue("MaxIter", "50000000"));
      SelectCombo("CmbEnvironment", Convert.ToString(section.GetValue("Environment", "Газопровод")));
      SelectCombo("CmbDiameter", Convert.ToString(section.GetValue("Diameter", "530")));
      SelectCombo("CmbThickness", Convert.ToString(section.GetValue("Thickness")));
      SelectCombo("CmbSteelGrade", Convert.ToString(section.GetValue("SteelGrade")));
      SelectCombo("CmbPipeType", Convert.ToString(section.GetValue("PipeType")));
      SetText("TxtCasingDiameter", section.GetValue("CasingDiameter", TextOf("TxtCasingDiameter")));
      SetText("TxtSteelRoughness", section.GetValue("SteelRoughness", TextOf("TxtSteelRoughness")));
      SetText("TxtPatRoughness", section.GetValue("PatRoughness", TextOf("TxtPatRoughness")));
      SetText("TxtPatNaturalRadius", section.GetValue("PatNaturalRadius", TextOf("TxtPatNaturalRadius")));
      SetText("TxtMinRadius", section.GetValue("MinRadius", TextOf("TxtMinRadius")));
      SetText("TxtProfileMinRadius", section.GetValue("ProfileMinRadius", TextOf("TxtProfileMinRadius")));
    }

    private void InitializeMediumEditors()
    {
      using SettingsSection section = AppSettings.OpenSection(AppSettings.General);
      _mediumOptions = PipeMediumSettings.Load(section);
      _editingMedium = PipeMediumSettings.Medium(ComboText("CmbEnvironment"));
      _changingMedium = true;
      try
      {
        ComboBox combo = Find<ComboBox>("CmbAdditionalMedium");
        if (combo != null)
        {
          combo.SelectedItem = combo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == _editingMedium)
            ?? combo.Items[0];
        }

        ShowMedium();
      }
      finally
      {
        _changingMedium = false;
      }
    }

    private void AdditionalMedium_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (_changingMedium || _mediumOptions == null)
      {
        return;
      }

      if (Find<ComboBox>("CmbAdditionalMedium")?.SelectedItem is not ComboBoxItem item)
      {
        return;
      }

      CaptureMedium();
      _editingMedium = (string)item.Tag;
      ShowMedium();
    }

    private void CaptureMedium()
    {
      if (_mediumOptions == null)
      {
        return;
      }

      Dictionary<string, string> p = _mediumOptions[_editingMedium];
      foreach ((string Key, CheckBox Box) x in MediumChecks())
      {
        if (x.Box != null)
        {
          p[x.Key] = (x.Box.IsChecked == true).ToString();
        }
      }

      foreach ((string Key, TextBox Box) x in MediumNumbers())
      {
        if (x.Box != null)
        {
          p[x.Key] = x.Box.Text ?? string.Empty;
        }
      }

      p["RiverTransitionType"] = IsChecked("RbRiverClosed")
        ? "Переход реки закрытый (ГНБ)" : "Переход реки открытый";
      p["RoadTransitionType"] = IsChecked("RbRoadClosed")
        ? "Переход дороги закрытый (ГНБ)" : "Переход дороги открытый";
      p["PipeTransitionType"] = IsChecked("RbPipeClosed")
        ? "Переход трубы закрытый (ГНБ)" : "Переход трубы открытый";
    }

    private void ShowMedium()
    {
      if (_mediumOptions == null)
      {
        return;
      }

      Dictionary<string, string> p = _mediumOptions[_editingMedium];
      foreach ((string Key, CheckBox Box) x in MediumChecks())
      {
        if (x.Box != null)
        {
          x.Box.IsChecked = PipeMediumSettings.Enabled(p, x.Key);
        }
      }

      foreach ((string Key, TextBox Box) x in MediumNumbers())
      {
        if (x.Box != null && p.TryGetValue(x.Key, out string value))
        {
          x.Box.Text = value;
        }
      }

      SetRadio("RbRiverClosed", "RbRiverOpen", p["RiverTransitionType"].Contains("ГНБ", StringComparison.Ordinal));
      SetRadio("RbRoadClosed", "RbRoadOpen", p["RoadTransitionType"].Contains("ГНБ", StringComparison.Ordinal));
      SetRadio("RbPipeClosed", "RbPipeOpen", p["PipeTransitionType"].Contains("ГНБ", StringComparison.Ordinal));
    }

    private void OnPipeParameterChanged()
    {
      if (_loadingPipeCatalog)
      {
        return;
      }

      try
      {
        UpdatePipeCatalogSelection();
        if (TracingCache.RatesFileExists())
        {
          ApplyRates(showErrors: false);
        }
      }
      catch (Exception ex)
      {
        System.Windows.MessageBox.Show(Window.GetWindow(this) ?? System.Windows.Application.Current?.MainWindow,
          ex.Message, "Номенклатура труб", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    private void ApplyRates(bool showErrors)
    {
      try
      {
        CaptureMedium();
        string pipeMedium = PipeMediumSettings.Medium(ComboText("CmbEnvironment"));
        IReadOnlyDictionary<string, string> profile = _mediumOptions != null
          ? _mediumOptions[pipeMedium]
          : PipeMediumSettings.TextDefaults;

        string ratesPath = TracingCache.ResolveRatesFilePath();
        if (_ratesPath != null)
        {
          PathTextDisplay.SetPath(_ratesPath, ratesPath);
        }

        if (!string.IsNullOrWhiteSpace(ratesPath) && System.IO.File.Exists(ratesPath))
        {
          AppPaths.Set(p => p.PriceWorkbook = ratesPath);
        }

        var request = new TracingRatesLoader.Request
        {
          RatesFilePath = ratesPath,
          EnvironmentLabel = ComboText("CmbEnvironment"),
          DiameterText = ComboText("CmbDiameter"),
          ThicknessText = ComboText("CmbThickness"),
          SteelGrade = ComboText("CmbSteelGrade"),
          PipeType = ComboText("CmbPipeType"),
          RoadOpen = IsChecked("RbRoadOpen"),
          PipeOpen = IsChecked("RbPipeOpen"),
          PneumoTest = IsChecked("ChkPneumoTest"),
          HydroTest = IsChecked("ChkHydroTest"),
          PpuInsulation = IsChecked("ChkPPUInsulation"),
          Ehs = IsChecked("ChkEHS"),
          DiagnosticOther = IsChecked("ChkDiagnosticOther"),
          InLineInspection = IsChecked("ChkInLineInspection"),
          GeoDef = IsChecked("ChkGeoPipeDefmark") || IsChecked("ChkGeoBendDefmark") || IsChecked("ChkGeoUzaDefmark"),
          GeoRep = IsChecked("ChkGeoPipeReper") || IsChecked("ChkGeoBendReper") || IsChecked("ChkGeoUzaReper"),
          GeoTerm = IsChecked("ChkGeoPipeThermo") || IsChecked("ChkGeoBendThermo") || IsChecked("ChkGeoUzaThermo"),
          MediumProfile = profile
        };

        TracingRatesLoader.Result loaded = TracingRatesLoader.Load(request);
        _pipeCatalog = loaded.Catalog;
        UpdatePipeCatalogSelection();
        if (!string.IsNullOrWhiteSpace(loaded.SelectedCasing))
        {
          SetText("TxtCasingDiameter", loaded.SelectedCasing);
        }

        _rates.Clear();
        foreach (RateItem item in loaded.Rates)
        {
          _rates.Add(item);
        }

        if (_emptyHint != null)
        {
          _emptyHint.Visibility = _rates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_catalogStatus != null)
        {
          _catalogStatus.Text = loaded.Status;
        }

        if (_statusText != null)
        {
          _statusText.Text =
            "Расценки загружены локально из " + Path.GetFileName(ratesPath)
            + " (" + _rates.Count + ").";
        }
      }
      catch (Exception ex)
      {
        if (_statusText != null)
        {
          _statusText.Text = ex.Message;
        }

        if (showErrors)
        {
          System.Windows.MessageBox.Show(Window.GetWindow(this) ?? System.Windows.Application.Current?.MainWindow,
            ex.Message, "Расценки", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
      }
    }

    private void UpdatePipeCatalogSelection()
    {
      if (_pipeCatalog == null || _pipeCatalog.Count == 0)
      {
        return;
      }

      _loadingPipeCatalog = true;
      try
      {
        void Fill(ComboBox box, IEnumerable<string> values)
        {
          if (box == null)
          {
            return;
          }

          string old = ComboText(box.Name);
          var items = values.Distinct().Select(v => new ComboBoxItem { Content = v }).ToArray();
          box.Items.Clear();
          foreach (ComboBoxItem item in items)
          {
            box.Items.Add(item);
          }

          box.SelectedItem = items.FirstOrDefault(x => (string)x.Content == old) ?? items.FirstOrDefault();
        }

        Fill(Find<ComboBox>("CmbDiameter"),
          _pipeCatalog.Select(p => p.Diameter.ToString(CultureInfo.InvariantCulture)).OrderBy(int.Parse));
        if (!int.TryParse(ComboText("CmbDiameter"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int diameter))
        {
          return;
        }

        Fill(Find<ComboBox>("CmbThickness"),
          _pipeCatalog.Where(p => p.Diameter == diameter).Select(p => Format(p.Wall)).OrderBy(x => x));
        if (!double.TryParse(ComboText("CmbThickness").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double wall))
        {
          return;
        }

        PipeCatalogRow[] matching = _pipeCatalog.Where(p => p.Diameter == diameter && Math.Abs(p.Wall - wall) < 1e-8).ToArray();
        Fill(Find<ComboBox>("CmbSteelGrade"), matching.Select(p => p.Steel));
        string steel = ComboText("CmbSteelGrade");
        Fill(Find<ComboBox>("CmbPipeType"), matching.Where(p => p.Steel == steel).Select(p => p.Type));
        string type = ComboText("CmbPipeType");
        PipeCatalogRow selected = matching.FirstOrDefault(p => p.Steel == steel && p.Type == type);
        if (selected != null)
        {
          SetText("TxtCasingDiameter", Format(selected.Casing));
          SetText("TxtMinRadius", selected.Diameter.ToString(CultureInfo.InvariantCulture));
          SetText("TxtProfileMinRadius", selected.Diameter.ToString(CultureInfo.InvariantCulture));
        }
      }
      finally
      {
        _loadingPipeCatalog = false;
      }
    }

    private IEnumerable<(string Key, CheckBox Box)> MediumChecks()
    {
      yield return ("PneumoTest", Find<CheckBox>("ChkPneumoTest"));
      yield return ("HydroTest", Find<CheckBox>("ChkHydroTest"));
      yield return ("PrelimPneumo", Find<CheckBox>("ChkPrelimPneumo"));
      yield return ("PrelimHydro", Find<CheckBox>("ChkPrelimHydro"));
      yield return ("PPUInsulation", Find<CheckBox>("ChkPPUInsulation"));
      yield return ("JointInsulation", Find<CheckBox>("ChkJointInsulation"));
      yield return ("EHS", Find<CheckBox>("ChkEHS"));
      yield return ("RiverInstallUza", Find<CheckBox>("ChkRiverInstallUza"));
      yield return ("RiverUseCase", Find<CheckBox>("ChkRiverCase"));
      yield return ("RiverUseLiner", Find<CheckBox>("ChkRiverLiner"));
      yield return ("RiverBallasting", Find<CheckBox>("ChkRiverBallasting"));
      yield return ("SwampBallasting", Find<CheckBox>("ChkSwampBallasting"));
      yield return ("Gabions", Find<CheckBox>("ChkGabions"));
      yield return ("GeoGrid", Find<CheckBox>("ChkGeoGrid"));
      yield return ("RenoMattress", Find<CheckBox>("ChkRenoMattress"));
      yield return ("GeoPipeDefmark", Find<CheckBox>("ChkGeoPipeDefmark"));
      yield return ("GeoPipeReper", Find<CheckBox>("ChkGeoPipeReper"));
      yield return ("GeoPipeThermo", Find<CheckBox>("ChkGeoPipeThermo"));
      yield return ("GeoBendDefmark", Find<CheckBox>("ChkGeoBendDefmark"));
      yield return ("GeoBendReper", Find<CheckBox>("ChkGeoBendReper"));
      yield return ("GeoBendThermo", Find<CheckBox>("ChkGeoBendThermo"));
      yield return ("GeoUzaDefmark", Find<CheckBox>("ChkGeoUzaDefmark"));
      yield return ("GeoUzaReper", Find<CheckBox>("ChkGeoUzaReper"));
      yield return ("GeoUzaThermo", Find<CheckBox>("ChkGeoUzaThermo"));
      yield return ("DiagnosticOther", Find<CheckBox>("ChkDiagnosticOther"));
      yield return ("InLineInspection", Find<CheckBox>("ChkInLineInspection"));
    }

    private IEnumerable<(string Key, TextBox Box)> MediumNumbers()
    {
      yield return ("MinRiverCrossing", Find<TextBox>("TxtMinRiverCrossing"));
      yield return ("MinRoadCrossing", Find<TextBox>("TxtMinRoadCrossing"));
      yield return ("MinPipeCrossingNormal", Find<TextBox>("TxtMinPipeCrossingNormal"));
      yield return ("MinPipeCrossingGas", Find<TextBox>("TxtMinPipeCrossingGas"));
      yield return ("GabionsAreaPerBankM2", Find<TextBox>("TxtGabionsAreaPerBank"));
      yield return ("GeoGridAreaPerBankM2", Find<TextBox>("TxtGeoGridAreaPerBank"));
      yield return ("RenoAreaPerBankM2", Find<TextBox>("TxtRenoAreaPerBank"));
      yield return ("GeoDefmarkSpacingMeters", Find<TextBox>("TxtGeoDefmarkSpacing"));
      yield return ("GeoReperSpacingMeters", Find<TextBox>("TxtGeoReperSpacing"));
      yield return ("GeoThermoSpacingMeters", Find<TextBox>("TxtGeoThermoSpacing"));
      yield return ("Переход_Река_МинУгол", Find<TextBox>("TxtRiverMinAngle"));
      yield return ("Переход_Река_МаксУгол", Find<TextBox>("TxtRiverMaxAngle"));
      yield return ("Переход_Дорога_МинУгол", Find<TextBox>("TxtRoadMinAngle"));
      yield return ("Переход_Дорога_МаксУгол", Find<TextBox>("TxtRoadMaxAngle"));
      yield return ("Переход_ТрубаБезГаза_МинУгол", Find<TextBox>("TxtNonGasMinAngle"));
      yield return ("Переход_ТрубаБезГаза_МаксУгол", Find<TextBox>("TxtNonGasMaxAngle"));
      yield return ("Переход_ТрубаСГазом_МинУгол", Find<TextBox>("TxtGasMinAngle"));
      yield return ("Переход_ТрубаСГазом_МаксУгол", Find<TextBox>("TxtGasMaxAngle"));
    }

    private T Find<T>(string name) where T : class => ShellXaml.Find<T>(_root, name);

    private static void Wire(ComboBox combo, SelectionChangedEventHandler handler)
    {
      if (combo != null)
      {
        combo.SelectionChanged += handler;
      }
    }

    private string TextOf(string name) => Find<TextBox>(name)?.Text ?? string.Empty;

    private void SetText(string name, object value)
    {
      TextBox box = Find<TextBox>(name);
      if (box != null && value != null)
      {
        box.Text = Convert.ToString(value, CultureInfo.InvariantCulture);
      }
    }

    private string ComboText(string name)
    {
      ComboBox box = Find<ComboBox>(name);
      if (box?.SelectedItem is ComboBoxItem item)
      {
        return Convert.ToString(item.Content) ?? string.Empty;
      }

      return box?.Text ?? string.Empty;
    }

    private void SelectCombo(string name, string value)
    {
      ComboBox box = Find<ComboBox>(name);
      if (box == null || string.IsNullOrWhiteSpace(value))
      {
        return;
      }

      foreach (object obj in box.Items)
      {
        if (obj is ComboBoxItem item && string.Equals(Convert.ToString(item.Content), value, StringComparison.OrdinalIgnoreCase))
        {
          box.SelectedItem = item;
          return;
        }
      }
    }

    private bool IsChecked(string name) => Find<CheckBox>(name)?.IsChecked == true
      || Find<RadioButton>(name)?.IsChecked == true;

    private void SetRadio(string closedName, string openName, bool closed)
    {
      RadioButton closedRb = Find<RadioButton>(closedName);
      RadioButton openRb = Find<RadioButton>(openName);
      if (closedRb != null)
      {
        closedRb.IsChecked = closed;
      }

      if (openRb != null)
      {
        openRb.IsChecked = !closed;
      }
    }

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
  }
}
