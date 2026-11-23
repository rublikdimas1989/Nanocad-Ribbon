namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Media;
  using System.Windows.Shapes;

  /// <summary>
  /// UI shell of End TopologyTrenchSettingsPanel («Траншея и дорога»).
  /// Preview drawing ported from End TopologyTrenchPreview + CrossSection layout.
  /// Values stay local in trench_road.conf until topology calc is wired.
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

    private sealed class PreviewPipe
    {
      public string Id { get; set; }
      public string Fluid { get; set; }
      public int DiameterMm { get; set; }
      public bool Outlet { get; set; }
      public double Offset { get; set; }
    }

    private sealed class CrossSectionLayout
    {
      public double Width { get; set; }
      public double LeftExtent { get; set; }
      public double RightExtent { get; set; }
      public double RoadOffset { get; set; }
      public double RoadWidth { get; set; }
      public List<PreviewPipe> Pipes { get; set; } = new List<PreviewPipe>();
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
    private readonly Canvas _canvas = new Canvas
    {
      Height = 210,
      Background = Brushes.WhiteSmoke,
      ClipToBounds = true
    };
    private readonly TextBlock _previewNote = new TextBlock
    {
      TextWrapping = TextWrapping.Wrap,
      Margin = new Thickness(0, 6, 0, 10)
    };
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

      var previewHost = new StackPanel();
      var previewBorder = new Border
      {
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(1),
        Child = _canvas
      };
      previewHost.Children.Add(previewBorder);
      previewHost.Children.Add(_previewNote);
      root.Children.Add(previewHost);
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
          + "При полном поиске проверяется, помещаются ли отводы заданного радиуса. После изменения радиуса выполните полную трассировку заново. Экспорт не пересчитывает профиль и стоимость.\n"
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
      _canvas.SizeChanged += (_, __) => Refresh();
      Loaded += (_, __) => Refresh();
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
        _previewNote.Text = string.Empty;
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
      _canvas.Children.Clear();

      Scheme scheme = _schemes.First(s => s.Id == schemeId);
      double minimumWidth = Read(schemeId, "MinimumWidth", 1.3);
      double gap12 = scheme.HasGap12 ? Read(schemeId, "PipeGap12", 0.5) : 0.5;
      double gap23 = scheme.HasGap23 ? Read(schemeId, "PipeGap23", 0.5) : 0.5;
      double additionalGap = scheme.HasAdditionalGap ? Read(schemeId, "AdditionalGap", 0.5) : 0.5;
      double sideClearance = Read(schemeId, "SideClearance", 0.2);
      double roadClearance = Read(schemeId, "RoadClearance", 5);
      double roadWidth = Math.Max(Read(schemeId, "RoadWidth", 6), 0.01);
      bool roadOnRight = (_editors[schemeId]["RoadOnRight"] as CheckBox)?.IsChecked == true;

      if (minimumWidth <= 0 || roadWidth <= 0)
      {
        throw new ArgumentException("ширина траншеи и дороги должна быть больше нуля.");
      }

      List<PreviewPipe> pipes = BuildExamplePipes(schemeId);
      CrossSectionLayout layout = BuildCrossSection(
        pipes, minimumWidth, gap12, gap23, additionalGap, sideClearance, roadClearance, roadWidth, roadOnRight);

      double roadLeft = layout.RoadOffset - layout.RoadWidth / 2;
      double roadRight = layout.RoadOffset + layout.RoadWidth / 2;
      double lo = Math.Min(-layout.RightExtent, roadLeft) - 0.5;
      double hi = Math.Max(layout.LeftExtent, roadRight) + 0.5;

      double width = Math.Max(600, _canvas.ActualWidth > 0 ? _canvas.ActualWidth : 800);
      double scale = (width - 40) / Math.Max(hi - lo, 0.01);

      // Physical left (positive offset) draws to the left of the canvas.
      double X(double x) => 20 + (hi - x) * scale;

      var trench = new Rectangle
      {
        Width = Math.Max(1, layout.Width * scale),
        Height = 95,
        Fill = Brushes.Bisque,
        Stroke = Brushes.SaddleBrown
      };
      Canvas.SetLeft(trench, X(layout.LeftExtent));
      Canvas.SetTop(trench, 75);
      _canvas.Children.Add(trench);

      var road = new Rectangle
      {
        Width = Math.Max(1, layout.RoadWidth * scale),
        Height = 30,
        Fill = Brushes.SlateGray
      };
      Canvas.SetLeft(road, X(roadRight));
      Canvas.SetTop(road, 70);
      _canvas.Children.Add(road);

      AddLabel(
        "Дорога " + layout.RoadWidth.ToString("0.###", CultureInfo.CurrentCulture) + " м",
        X(roadRight),
        38);
      AddLabel(
        "Траншея " + layout.Width.ToString("0.###", CultureInfo.CurrentCulture) + " м",
        X(layout.LeftExtent),
        180);

      _canvas.Children.Add(new Line
      {
        X1 = X(0),
        Y1 = 65,
        X2 = X(0),
        Y2 = 173,
        Stroke = Brushes.DarkOrange,
        StrokeThickness = 1
      });

      foreach (PreviewPipe pipe in layout.Pipes)
      {
        double d = pipe.DiameterMm / 1000.0;
        double size = Math.Max(4, d * scale);
        var circle = new Ellipse
        {
          Width = size,
          Height = size,
          Stroke = Brushes.Black,
          Fill = PipeBrush(pipe)
        };
        Canvas.SetLeft(circle, X(pipe.Offset) - size / 2);
        Canvas.SetTop(circle, 125 - size / 2);
        _canvas.Children.Add(circle);
        AddLabel(pipe.Id, X(pipe.Offset) - 4, 92);
      }

      string legend = string.Join(
        "; ",
        layout.Pipes.Select(p => p.Id + " — " + (p.Outlet ? "выход БУОС" : FluidName(p.Fluid))));
      string diameters = string.Join(" / ", layout.Pipes.Select(p => p.DiameterMm.ToString(CultureInfo.InvariantCulture)));
      _previewNote.Text =
        scheme.Title + ".\n"
        + legend + ".\n"
        + "Край траншеи — край дороги: "
        + roadClearance.ToString("0.###", CultureInfo.CurrentCulture) + " м. "
        + "Пример D: " + diameters + " мм. "
        + "Ширина в расчёте зависит от реальных диаметров и не меньше заданной. "
        + "Дорога в DXF — ось с указанной шириной.";
    }

    private void AddLabel(string text, double x, double y)
    {
      var label = new TextBlock { Text = text, FontSize = 12 };
      Canvas.SetLeft(label, x);
      Canvas.SetTop(label, y);
      _canvas.Children.Add(label);
    }

    private static Brush PipeBrush(PreviewPipe pipe)
    {
      if (pipe.Outlet)
      {
        return Brushes.DarkOrange;
      }

      return pipe.Fluid switch
      {
        "oil" => Brushes.IndianRed,
        "gas" => Brushes.Gold,
        _ => Brushes.SkyBlue
      };
    }

    private static string FluidName(string key)
    {
      return key switch
      {
        "oil" => "Нефть",
        "gas" => "ПНГ",
        "mixed" => "Нефть + вода + ПНГ",
        "oil_gas" => "Нефть + ПНГ",
        "combined" => "Жидкость (нефть + вода)",
        _ => "Жидкость"
      };
    }

    /// <summary>Example pipes matching End TopologyTrenchPreview for each scheme kind.</summary>
    private static List<PreviewPipe> BuildExamplePipes(string schemeId)
    {
      var incoming = new List<(string Fluid, int Diameter)>();
      bool addOutlet = false;

      switch (schemeId)
      {
        case "SingleOutlet":
          addOutlet = true;
          break;
        case "SingleInlet":
          incoming.Add(("mixed", 426));
          break;
        case "OneWithOutlet":
          incoming.Add(("mixed", 426));
          addOutlet = true;
          break;
        case "TwoPipes":
          incoming.Add(("oil_gas", 426));
          incoming.Add(("liquid", 426));
          break;
        case "TwoWithOutlet":
          incoming.Add(("oil_gas", 426));
          incoming.Add(("liquid", 426));
          addOutlet = true;
          break;
        case "ThreePipes":
          incoming.Add(("oil", 426));
          incoming.Add(("liquid", 426));
          incoming.Add(("gas", 426));
          break;
        case "FourPipes":
          incoming.Add(("oil", 426));
          incoming.Add(("liquid", 426));
          incoming.Add(("gas", 426));
          addOutlet = true;
          break;
        default:
          incoming.Add(("mixed", 426));
          break;
      }

      var pipes = new List<PreviewPipe>();
      for (int i = 0; i < incoming.Count; i++)
      {
        pipes.Add(new PreviewPipe
        {
          Id = (i + 1).ToString(CultureInfo.InvariantCulture),
          Fluid = incoming[i].Fluid,
          DiameterMm = incoming[i].Diameter,
          Outlet = false
        });
      }

      if (addOutlet)
      {
        pipes.Add(new PreviewPipe
        {
          Id = (incoming.Count + 1).ToString(CultureInfo.InvariantCulture),
          Fluid = "oil",
          DiameterMm = 426,
          Outlet = true
        });
      }

      if (pipes.Count == 0)
      {
        throw new ArgumentException("Для поперечника нужны трубы.");
      }

      return pipes;
    }

    /// <summary>
    /// Local port of TopologyCorridors.CrossSection for preview-only pipes
    /// (no TopologySettings / network lanes).
    /// </summary>
    private static CrossSectionLayout BuildCrossSection(
      List<PreviewPipe> pipes,
      double minimumWidth,
      double gap12,
      double gap23,
      double additionalGap,
      double sideClearance,
      double roadClearance,
      double roadWidth,
      bool roadOnRight)
    {
      double Gap(int i)
      {
        if (pipes[i].Outlet)
        {
          return additionalGap;
        }

        return i == 1 ? gap12 : i == 2 ? gap23 : additionalGap;
      }

      double occupied = pipes.Sum(p => p.DiameterMm / 1000.0)
        + Enumerable.Range(1, pipes.Count - 1).Sum(Gap);
      double width = Math.Max(minimumWidth, occupied + 2 * sideClearance);

      var layout = new CrossSectionLayout
      {
        Width = width,
        LeftExtent = width / 2,
        RightExtent = width / 2,
        RoadWidth = roadWidth,
        Pipes = pipes
      };

      double position = occupied / 2;
      for (int i = 0; i < pipes.Count; i++)
      {
        if (i > 0)
        {
          position -= Gap(i);
        }

        double d = pipes[i].DiameterMm / 1000.0;
        pipes[i].Offset = position - d / 2;
        position -= d;
      }

      layout.RoadOffset = (roadOnRight ? -1 : 1)
        * (width / 2 + roadClearance + roadWidth / 2);
      return layout;
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
