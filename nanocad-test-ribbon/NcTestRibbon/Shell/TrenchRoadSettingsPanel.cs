namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Media;
  using System.Windows.Shapes;

  /// <summary>
  /// UI shell of End TopologyTrenchSettingsPanel («Траншея и дорога»).
  /// Code-built; no topology calculation — values stay local until Save stubs conf.
  /// </summary>
  public sealed class TrenchRoadSettingsPanel : UserControl
  {
    private sealed class Scheme
    {
      public string Id { get; set; }
      public string Title { get; set; }
      public bool HasGap12 { get; set; }
      public bool HasGap23 { get; set; }
      public bool HasAdditionalGap { get; set; }
    }

    private readonly ComboBox _kind = new ComboBox
    {
      Margin = new Thickness(0, 6, 0, 12),
      MinWidth = 500
    };

    private readonly TextBlock _error = new TextBlock
    {
      Foreground = Brushes.Firebrick,
      TextWrapping = TextWrapping.Wrap
    };

    private readonly TextBox _radius = new TextBox { Padding = new Thickness(5), Text = "1.5" };
    private readonly Canvas _preview = new Canvas { Height = 120, Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)) };
    private readonly Dictionary<string, StackPanel> _panels = new Dictionary<string, StackPanel>();
    private readonly Dictionary<string, Dictionary<string, FrameworkElement>> _editors =
      new Dictionary<string, Dictionary<string, FrameworkElement>>();
    private readonly List<Scheme> _schemes;

    public TrenchRoadSettingsPanel()
    {
      _schemes = new List<Scheme>
      {
        new Scheme { Id = "SingleInlet", Title = "1 труба от КП — без сепаратора" },
        new Scheme { Id = "TwoPipes", Title = "2 трубы от КП — жидкость и нефть + ПНГ", HasGap12 = true },
        new Scheme { Id = "ThreePipes", Title = "3 трубы — нефть, жидкость, ПНГ от КП", HasGap12 = true, HasGap23 = true },
        new Scheme { Id = "SingleOutlet", Title = "1 труба — общий выход БУОС → врезка" },
        new Scheme { Id = "OneWithOutlet", Title = "1 труба от КП + выход БУОС", HasAdditionalGap = true },
        new Scheme { Id = "TwoWithOutlet", Title = "2 трубы от КП + выход БУОС", HasGap12 = true, HasAdditionalGap = true },
        new Scheme { Id = "FourPipes", Title = "3 трубы от КП + выход БУОС (4 и более труб)", HasGap12 = true, HasGap23 = true, HasAdditionalGap = true }
      };

      var root = new StackPanel { Margin = new Thickness(12) };
      Content = root;

      root.Children.Add(new TextBlock
      {
        Text = "Редактируемая схема траншеи",
        FontWeight = FontWeights.SemiBold
      });
      root.Children.Add(_kind);
      root.Children.Add(new TextBlock
      {
        Text = "Для каждой схемы сохраняются свои размеры. При расчёте и экспорте схема выбирается по фактическому количеству труб на общем участке. Для одной и двух труб от КП, а также их сочетаний с выходом БУОС заданы отдельные схемы. Четыре и более труб используют расширяемую схему.",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
      });

      var previewBorder = new Border
      {
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(1),
        Child = _preview,
        Margin = new Thickness(0, 0, 0, 8)
      };
      root.Children.Add(previewBorder);
      root.Children.Add(_error);

      foreach (Scheme scheme in _schemes)
      {
        var panel = new StackPanel();
        _panels[scheme.Id] = panel;
        _editors[scheme.Id] = new Dictionary<string, FrameworkElement>();
        root.Children.Add(panel);

        AddNumber(panel, scheme.Id, "MinimumWidth", "Минимальная ширина траншеи, м", "1.3");
        if (scheme.HasGap12)
        {
          AddNumber(panel, scheme.Id, "PipeGap12", "Зазор в свету между первой и второй трубами от КП, м", "0.5");
        }

        if (scheme.HasGap23)
        {
          AddNumber(panel, scheme.Id, "PipeGap23", "Зазор в свету между второй и третьей трубами от КП, м", "0.5");
        }

        if (scheme.HasAdditionalGap)
        {
          AddNumber(panel, scheme.Id, "AdditionalGap", "Зазор до выходной или дополнительной трубы, м", "0.5");
        }

        AddNumber(panel, scheme.Id, "SideClearance", "От крайней трубы до стенки траншеи, м", "0.2");
        AddNumber(panel, scheme.Id, "RoadClearance", "От края траншеи до края дороги, м", "5");
        AddNumber(panel, scheme.Id, "RoadWidth", "Ширина дороги, м", "6");

        var side = new CheckBox
        {
          Content = "Дорога справа по единому направлению поперечника",
          IsChecked = true,
          Margin = new Thickness(0, 10, 0, 10)
        };
        side.Checked += (_, __) => Refresh();
        side.Unchecked += (_, __) => Refresh();
        panel.Children.Add(side);
        _editors[scheme.Id]["RoadOnRight"] = side;
      }

      root.Children.Add(new TextBlock
      {
        Text = "Радиус дуги отвода, м (проверка полной трассировки и оформление DXF; общий для всех схем)",
        Margin = new Thickness(0, 12, 0, 4),
        TextWrapping = TextWrapping.Wrap
      });
      _radius.TextChanged += (_, __) => Refresh();
      root.Children.Add(_radius);
      root.Children.Add(new TextBlock
      {
        Text = "Одна труба располагается по оси траншеи. Ширина автоматически увеличивается, если выбранные диаметры и зазоры не помещаются. Изменённые параметры затрат действуют при новом расчёте топологии.\n"
          + "Конфиг оболочки: " + TracingCache.TrenchRoadConf,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 12, 0, 0),
        Foreground = Brushes.DimGray
      });

      _kind.DisplayMemberPath = "Title";
      _kind.SelectedValuePath = "Id";
      _kind.ItemsSource = _schemes;
      _kind.SelectionChanged += (_, __) => Refresh();
      _kind.SelectedIndex = 0;
      _preview.SizeChanged += (_, __) => Refresh();
      Refresh();
    }

    private void AddNumber(StackPanel panel, string schemeId, string key, string label, string value)
    {
      panel.Children.Add(new TextBlock
      {
        Text = label,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 3)
      });
      var box = new TextBox { Text = value, Padding = new Thickness(5) };
      box.TextChanged += (_, __) => Refresh();
      panel.Children.Add(box);
      _editors[schemeId][key] = box;
    }

    private void Refresh()
    {
      if (_kind.SelectedValue is not string id)
      {
        return;
      }

      foreach (KeyValuePair<string, StackPanel> pair in _panels)
      {
        pair.Value.Visibility = pair.Key == id ? Visibility.Visible : Visibility.Collapsed;
      }

      try
      {
        DrawPreview(id);
        _error.Text = string.Empty;
      }
      catch (Exception ex)
      {
        _error.Text = "Проверьте размеры выбранной схемы: " + ex.Message;
      }
    }

    private double Read(string schemeId, string key, double fallback)
    {
      if (!_editors[schemeId].TryGetValue(key, out FrameworkElement editor) || editor is not TextBox box)
      {
        return fallback;
      }

      string text = (box.Text ?? string.Empty).Trim().Replace(',', '.');
      if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
          || !double.IsFinite(value) || value < 0)
      {
        throw new ArgumentException(key + ": ожидается неотрицательное число.");
      }

      return value;
    }

    private void DrawPreview(string schemeId)
    {
      _preview.Children.Clear();
      double trench = Math.Max(Read(schemeId, "MinimumWidth", 1.3), 0.5);
      double road = Math.Max(Read(schemeId, "RoadWidth", 6), 0.5);
      double clearance = Read(schemeId, "RoadClearance", 5);
      bool right = (_editors[schemeId]["RoadOnRight"] as CheckBox)?.IsChecked == true;

      double width = _preview.ActualWidth;
      if (width < 10)
      {
        width = 640;
      }

      double scale = Math.Min((width - 40) / (trench + clearance + road), 18);
      double trenchW = trench * scale;
      double roadW = road * scale;
      double gapW = clearance * scale;
      double total = trenchW + gapW + roadW;
      double x0 = (width - total) / 2;
      double y = 40;

      var trenchRect = new Rectangle
      {
        Width = trenchW,
        Height = 36,
        Fill = new SolidColorBrush(Color.FromRgb(210, 180, 140)),
        Stroke = Brushes.SaddleBrown
      };
      Canvas.SetLeft(trenchRect, x0);
      Canvas.SetTop(trenchRect, y);
      _preview.Children.Add(trenchRect);

      double roadX = right ? x0 + trenchW + gapW : x0 - gapW - roadW;
      if (!right)
      {
        // Keep preview inside canvas: flip layout so trench stays centered-ish.
        roadX = x0 + trenchW + gapW;
      }

      var roadRect = new Rectangle
      {
        Width = roadW,
        Height = 28,
        Fill = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
        Stroke = Brushes.DimGray
      };
      Canvas.SetLeft(roadRect, roadX);
      Canvas.SetTop(roadRect, y + 4);
      _preview.Children.Add(roadRect);

      _preview.Children.Add(LabelAt(x0, y - 18, "Траншея " + trench.ToString("0.###", CultureInfo.CurrentCulture) + " м"));
      _preview.Children.Add(LabelAt(roadX, y - 18, "Дорога " + road.ToString("0.###", CultureInfo.CurrentCulture) + " м"));
    }

    private static TextBlock LabelAt(double x, double y, string text)
    {
      var label = new TextBlock { Text = text, FontSize = 11 };
      Canvas.SetLeft(label, x);
      Canvas.SetTop(label, y);
      return label;
    }

    /// <summary>Stub persist of current UI values into trench_road.conf.</summary>
    public void SaveStubConf()
    {
      TracingCache.EnsureCreated();
      var lines = new List<string>
      {
        "# trench_road.conf — shell snapshot " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
        "elbow_radius_m=" + (_radius.Text ?? string.Empty).Trim()
      };

      if (_kind.SelectedItem is Scheme selected)
      {
        lines.Add("selected_scheme=" + selected.Id);
        foreach (KeyValuePair<string, FrameworkElement> pair in _editors[selected.Id])
        {
          if (pair.Value is TextBox box)
          {
            lines.Add(selected.Id + "." + pair.Key + "=" + (box.Text ?? string.Empty).Trim());
          }
          else if (pair.Value is CheckBox check)
          {
            lines.Add(selected.Id + "." + pair.Key + "=" + (check.IsChecked == true ? "1" : "0"));
          }
        }
      }

      System.IO.File.WriteAllLines(TracingCache.TrenchRoadConf, lines);
    }
  }
}
