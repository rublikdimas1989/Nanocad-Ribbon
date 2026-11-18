namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.ObjectModel;
  using System.Globalization;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Data;
  using System.Windows.Media;

  /// <summary>
  /// UI shell of End-variant TopologyInputsWindow («Оптимизация … кустовых площадок»).
  /// Built in code (no XAML pack URI). Logic is stubbed via StubUi.
  /// </summary>
  public sealed class PadOptimizationWindow : Window
  {
    private readonly ObservableCollection<PadRow> _pads = new ObservableCollection<PadRow>();
    private readonly TextBox _months;
    private readonly TextBox _fluidFile;
    private readonly TextBox _count;
    private readonly TextBox _x;
    private readonly TextBox _y;
    private readonly TextBox _background;
    private readonly TextBox _alpha;
    private readonly TextBox _log;
    private readonly TextBlock _status;
    private readonly ComboBox _mode;
    private readonly ComboBox _coordinateUnit;
    private readonly CheckBox _gray;
    private readonly DataGrid _grid;

    public PadOptimizationWindow()
    {
      Title = "Оптимизация кустовых площадок";
      Width = 1250;
      Height = 760;
      MinWidth = 850;
      MinHeight = 600;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;

      _pads.Add(new PadRow { Name = "КП-1", X = "0", Y = "0", Liquid = true, Oil = true, Gas = true, FirstLaunch = "1" });
      _pads.Add(new PadRow { Name = "КП-2", X = "1000", Y = "500", Liquid = true, Oil = true, Gas = false, FirstLaunch = "1" });

      var root = new DockPanel { Margin = new Thickness(8) };
      Content = root;

      var top = new StackPanel();
      DockPanel.SetDock(top, Dock.Top);
      root.Children.Add(top);

      WrapPanel Row()
      {
        var row = new WrapPanel { Margin = new Thickness(0, 3, 0, 3) };
        top.Children.Add(row);
        return row;
      }

      TextBox Field(Panel parent, string label, string value, double width)
      {
        parent.Children.Add(new TextBlock
        {
          Text = label,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(5)
        });
        var box = new TextBox
        {
          Text = value,
          Width = width,
          Padding = new Thickness(4),
          Margin = new Thickness(3)
        };
        parent.Children.Add(box);
        return box;
      }

      void Button(Panel parent, string label, Action action)
      {
        var button = new Button
        {
          Content = label,
          Padding = new Thickness(9, 5, 9, 5),
          Margin = new Thickness(3)
        };
        button.Click += (_, __) =>
        {
          try
          {
            action();
          }
          catch (Exception ex)
          {
            MessageBox.Show(this, ex.Message, "Проверьте исходные данные",
              MessageBoxButton.OK, MessageBoxImage.Warning);
          }
        };
        parent.Children.Add(button);
      }

      WrapPanel Group(string title)
      {
        var box = new GroupBox { Header = title, Margin = new Thickness(0, 3, 0, 3) };
        top.Children.Add(box);
        var row = new WrapPanel { Margin = new Thickness(4) };
        box.Content = row;
        return row;
      }

      ComboBox Choice(Panel parent, string label, string[] items, string selected)
      {
        parent.Children.Add(new TextBlock
        {
          Text = label,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(5)
        });
        var combo = new ComboBox
        {
          Width = 160,
          Margin = new Thickness(3),
          Padding = new Thickness(4)
        };
        foreach (string item in items)
        {
          combo.Items.Add(item);
        }

        combo.SelectedItem = selected;
        parent.Children.Add(combo);
        return combo;
      }

      var first = Row();
      _months = Field(first, "Месяцев:", "12", 60);
      Button(first, "Применить месяцы", () =>
        StubUi.Notify(this, "PadOptimization.ApplyMonths → " + _months.Text));
      _fluidFile = Field(first, "Excel свойств:", string.Empty, 340);
      Button(first, "Обзор…", () => StubUi.Notify(this, "PadOptimization.BrowseFluidExcel"));
      Button(first, "Загрузить свойства", () => StubUi.Notify(this, "PadOptimization.LoadFluidProps"));

      var units = Group("Единицы профилей");
      Choice(units, "Жидкость", new[] { "м³/сут", "м³/ч", "тыс. м³/сут" }, "м³/сут");
      Choice(units, "Нефть", new[] { "т/сут" }, "т/сут");
      Choice(units, "ПНГ", new[] { "м³/сут", "м³/ч", "тыс. м³/сут" }, "м³/сут");
      units.Children.Add(new TextBlock
      {
        Text = "ПНГ: 1 тыс. м³/сут = 1000 м³/сут",
        Margin = new Thickness(12),
        VerticalAlignment = VerticalAlignment.Center
      });

      var collector = Group("Пункт сбора — БУОС");
      var modePanel = new StackPanel { Orientation = Orientation.Horizontal };
      collector.Children.Add(modePanel);
      _mode = Choice(modePanel, "Размещение", new[] { "Фиксированное", "Определить автоматически" }, "Определить автоматически");
      _count = Field(collector, "Площадок (0 — подобрать):", "0", 45);
      _x = Field(collector, "X:", "0", 110);
      _y = Field(collector, "Y:", "0", 110);
      Button(collector, "Вставить X/Y", () => StubUi.Notify(this, "PadOptimization.PasteXY"));

      void ModeChanged()
      {
        bool fixedMode = string.Equals(_mode.SelectedItem as string, "Фиксированное", StringComparison.Ordinal);
        _x.IsEnabled = fixedMode;
        _y.IsEnabled = fixedMode;
        _count.IsEnabled = !fixedMode;
      }

      _mode.SelectionChanged += (_, __) => ModeChanged();
      ModeChanged();

      var bg = Group("Космоснимок / растровая подложка");
      _background = Field(bg, "ECW / GeoTIFF:", string.Empty, 380);
      Button(bg, "Обзор…", () => StubUi.Notify(this, "PadOptimization.BrowseBackground"));
      Button(bg, "Отключить", () => { _background.Text = string.Empty; });
      _alpha = Field(bg, "Прозрачность 0…1:", "0.5", 55);
      _gray = new CheckBox
      {
        Content = "Ч/б",
        IsChecked = false,
        Margin = new Thickness(8),
        VerticalAlignment = VerticalAlignment.Center
      };
      bg.Children.Add(_gray);

      var extras = Row();
      var coordinatePanel = new StackPanel { Orientation = Orientation.Horizontal };
      extras.Children.Add(coordinatePanel);
      _coordinateUnit = Choice(coordinatePanel, "Единицы X/Y", new[] { "метры", "километры" }, "метры");
      Button(extras, "Открыть JSON…", () => StubUi.Notify(this, "PadOptimization.OpenJson"));
      Button(extras, "Сохранить как…", () => StubUi.Notify(this, "PadOptimization.SaveAs"));
      Button(extras, "Расчётные параметры", () => StubUi.Notify(this, "PadOptimization.CalcParams"));

      _status = new TextBlock
      {
        Text = "UI-оболочка. Расчёт / JSON — на сервере позже.",
        Margin = new Thickness(4),
        Foreground = Brushes.DarkSlateGray
      };
      DockPanel.SetDock(_status, Dock.Bottom);
      root.Children.Add(_status);

      _log = new TextBox
      {
        Height = 105,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Text = "Оболочка TopologyInputsWindow / TopologyPadWindow." + Environment.NewLine
          + "Логика оптимизации и профилей КП отключена (stub)." + Environment.NewLine
      };
      DockPanel.SetDock(_log, Dock.Bottom);
      root.Children.Add(_log);

      var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, 5) };
      DockPanel.SetDock(buttons, Dock.Bottom);
      root.Children.Add(buttons);

      Button(buttons, "Добавить КП", () =>
      {
        var dialog = new TopologyPadWindowShell("КП-" + (_pads.Count + 1)) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
          _pads.Add(new PadRow
          {
            Name = dialog.PadName,
            X = dialog.PadX,
            Y = dialog.PadY,
            Liquid = true,
            Oil = true,
            Gas = true,
            FirstLaunch = "1"
          });
          Log("Добавлена КП: " + dialog.PadName);
        }
      });
      Button(buttons, "Вставить КП из Excel", () => StubUi.Notify(this, "PadOptimization.PasteFromExcel"));
      Button(buttons, "Редактировать / профили", () =>
      {
        if (_grid.SelectedItem is not PadRow row)
        {
          throw new InvalidOperationException("Выберите КП в таблице.");
        }

        var dialog = new TopologyPadWindowShell(row.Name, row.X, row.Y) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
          row.Name = dialog.PadName;
          row.X = dialog.PadX;
          row.Y = dialog.PadY;
          _grid.Items.Refresh();
          Log("Обновлена КП: " + row.Name);
        }
      });
      Button(buttons, "Варианты оптимизации", () => StubUi.Notify(this, "PadOptimization.ShowVariants"));
      Button(buttons, "Удалить", () =>
      {
        if (_grid.SelectedItem is PadRow row)
        {
          _pads.Remove(row);
          Log("Удалена КП: " + row.Name);
        }
      });
      Button(buttons, "Сохранить настройки", () => StubUi.Notify(this, "PadOptimization.SaveSettings"));
      Button(buttons, "Рассчитать оптимальную сеть", () => StubUi.Notify(this, "PadOptimization.CalculateNetwork"));

      _grid = new DataGrid
      {
        ItemsSource = _pads,
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        SelectionMode = DataGridSelectionMode.Single
      };
      _grid.Columns.Add(TextCol("КП", nameof(PadRow.Name)));
      _grid.Columns.Add(TextCol("X", nameof(PadRow.X)));
      _grid.Columns.Add(TextCol("Y", nameof(PadRow.Y)));
      _grid.Columns.Add(CheckCol("Жидкость", nameof(PadRow.Liquid)));
      _grid.Columns.Add(CheckCol("Нефть", nameof(PadRow.Oil)));
      _grid.Columns.Add(CheckCol("ПНГ", nameof(PadRow.Gas)));
      _grid.Columns.Add(TextCol("Первый запуск, месяц", nameof(PadRow.FirstLaunch)));
      _grid.MouseDoubleClick += (_, __) =>
      {
        if (_grid.SelectedItem is PadRow row)
        {
          var dialog = new TopologyPadWindowShell(row.Name, row.X, row.Y) { Owner = this };
          if (dialog.ShowDialog() == true)
          {
            row.Name = dialog.PadName;
            row.X = dialog.PadX;
            row.Y = dialog.PadY;
            _grid.Items.Refresh();
          }
        }
      };
      root.Children.Add(_grid);

      Log("Загружена оболочка «Оптимизация кустовых площадок» (без серверной логики).");
    }

    private void Log(string text)
    {
      _log.AppendText(text + Environment.NewLine);
      _log.ScrollToEnd();
      _status.Text = text;
    }

    private static DataGridTextColumn TextCol(string header, string path)
    {
      return new DataGridTextColumn
      {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
      };
    }

    private static DataGridCheckBoxColumn CheckCol(string header, string path)
    {
      return new DataGridCheckBoxColumn
      {
        Header = header,
        Binding = new Binding(path),
        Width = 85
      };
    }

    public sealed class PadRow
    {
      public string Name { get; set; } = string.Empty;
      public string X { get; set; } = "0";
      public string Y { get; set; } = "0";
      public bool Liquid { get; set; } = true;
      public bool Oil { get; set; } = true;
      public bool Gas { get; set; } = true;
      public string FirstLaunch { get; set; } = "1";
    }
  }

  /// <summary>
  /// UI shell of End-variant TopologyPadWindow (per-pad profiles / БУОС load).
  /// Code-built; calculation stubbed.
  /// </summary>
  public sealed class TopologyPadWindowShell : Window
  {
    private readonly TextBox _name;
    private readonly TextBox _x;
    private readonly TextBox _y;
    private readonly TextBox _horizon;
    private readonly TextBox _pressure;
    private readonly CheckBox _sour;
    private readonly DataGrid _grid;
    private readonly ObservableCollection<MonthRow> _rows = new ObservableCollection<MonthRow>();

    public string PadName => (_name.Text ?? string.Empty).Trim();
    public string PadX => (_x.Text ?? string.Empty).Trim();
    public string PadY => (_y.Text ?? string.Empty).Trim();

    public TopologyPadWindowShell(string name, string x = "0", string y = "0")
    {
      Title = name + " — месячные профили и загрузка БУОС";
      Width = 1220;
      Height = 820;
      MinWidth = 960;
      MinHeight = 650;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;

      var root = new DockPanel { Margin = new Thickness(12) };
      Content = root;

      var buttons = new StackPanel
      {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right
      };
      DockPanel.SetDock(buttons, Dock.Bottom);
      root.Children.Add(buttons);

      var apply = new Button
      {
        Content = "Применить",
        Margin = new Thickness(5),
        Padding = new Thickness(16, 6, 16, 6),
        IsDefault = true
      };
      apply.Click += (_, __) =>
      {
        if (string.IsNullOrWhiteSpace(PadName))
        {
          MessageBox.Show(this, "Укажите имя КП.", Title);
          return;
        }

        DialogResult = true;
      };
      buttons.Children.Add(apply);
      buttons.Children.Add(new Button
      {
        Content = "Отмена",
        IsCancel = true,
        Margin = new Thickness(5),
        Padding = new Thickness(16, 6, 16, 6)
      });

      var message = new TextBlock
      {
        Text = "UI-оболочка TopologyPadWindow. Расчёт загрузки БУОС — stub.",
        Foreground = Brushes.Firebrick,
        Margin = new Thickness(6),
        TextWrapping = TextWrapping.Wrap
      };
      DockPanel.SetDock(message, Dock.Bottom);
      root.Children.Add(message);

      var tabs = new TabControl();
      root.Children.Add(tabs);

      var general = new StackPanel { Margin = new Thickness(12) };
      tabs.Items.Add(new TabItem
      {
        Header = "Куст",
        Content = new ScrollViewer
        {
          Content = general,
          VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        }
      });

      general.Children.Add(Label("Имя КП"));
      _name = Box(name);
      general.Children.Add(_name);
      general.Children.Add(Label("X (единицы из общих настроек)"));
      _x = Box(x);
      general.Children.Add(_x);
      general.Children.Add(Label("Y"));
      _y = Box(y);
      general.Children.Add(_y);
      var paste = new Button
      {
        Content = "Вставить X/Y из буфера",
        Margin = new Thickness(0, 8, 0, 8),
        Padding = new Thickness(8, 4, 8, 4),
        HorizontalAlignment = HorizontalAlignment.Left
      };
      paste.Click += (_, __) => StubUi.Notify(this, "TopologyPad.PasteXY");
      general.Children.Add(paste);
      general.Children.Add(new TextBlock
      {
        Text = "Профили сред редактируются на вкладке «Месяцы и загрузка БУОС».",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0)
      });

      var panel = new DockPanel { Margin = new Thickness(6) };
      tabs.Items.Add(new TabItem { Header = "Месяцы и загрузка БУОС", Content = panel });
      tabs.SelectedIndex = 1;

      var top = new StackPanel();
      DockPanel.SetDock(top, Dock.Top);
      panel.Children.Add(top);

      _sour = new CheckBox { Content = "Сернистая среда", Margin = new Thickness(6, 4, 6, 4) };
      top.Children.Add(_sour);
      top.Children.Add(new TextBlock
      {
        Text = "Схема транспорта от этой КП",
        Margin = new Thickness(6)
      });
      foreach (string mode in new[] { "Без сепаратора", "Две трубы (вода отдельно)", "Полная сепарация" })
      {
        top.Children.Add(new RadioButton
        {
          Content = mode,
          GroupName = "Separation",
          IsChecked = mode.StartsWith("Без", StringComparison.Ordinal),
          Margin = new Thickness(6, 2, 6, 2)
        });
      }

      var pressureRow = new WrapPanel();
      top.Children.Add(pressureRow);
      pressureRow.Children.Add(new TextBlock
      {
        Text = "Доступное давление на КП, бар (абс.):",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6)
      });
      _pressure = new TextBox { Text = "0", Width = 90, Margin = new Thickness(4), Padding = new Thickness(4) };
      pressureRow.Children.Add(_pressure);

      var tools = new WrapPanel();
      top.Children.Add(tools);
      tools.Children.Add(new TextBlock
      {
        Text = "Месяцев:",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6)
      });
      _horizon = new TextBox { Text = "12", Width = 55, Margin = new Thickness(4), Padding = new Thickness(4) };
      tools.Children.Add(_horizon);
      var applyMonths = new Button
      {
        Content = "Применить месяцы",
        Margin = new Thickness(3),
        Padding = new Thickness(8, 4, 8, 4)
      };
      applyMonths.Click += (_, __) =>
      {
        if (!int.TryParse(_horizon.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int months)
            || months < 1 || months > 600)
        {
          MessageBox.Show(this, "Горизонт: 1…600 месяцев.", Title);
          return;
        }

        RebuildRows(months);
        StubUi.Notify(this, "TopologyPad.ApplyMonths → " + months);
      };
      tools.Children.Add(applyMonths);

      var cards = new Grid();
      top.Children.Add(cards);
      string[] fluids = { "Жидкость", "Нефть", "ПНГ" };
      for (int i = 0; i < fluids.Length; i++)
      {
        cards.ColumnDefinitions.Add(new ColumnDefinition());
        var card = new StackPanel { Margin = new Thickness(8) };
        var border = new Border
        {
          Child = card,
          BorderBrush = Brushes.LightGray,
          BorderThickness = new Thickness(1),
          CornerRadius = new CornerRadius(4),
          Margin = new Thickness(4)
        };
        Grid.SetColumn(border, i);
        cards.Children.Add(border);
        card.Children.Add(new CheckBox
        {
          Content = "Учитывать: " + fluids[i],
          IsChecked = true,
          FontWeight = FontWeights.SemiBold,
          Margin = new Thickness(0, 0, 0, 4)
        });
        card.Children.Add(new TextBlock
        {
          Text = "Нет расчёта: stub UI",
          Foreground = Brushes.Gray,
          TextWrapping = TextWrapping.Wrap,
          Margin = new Thickness(0, 0, 0, 4)
        });
        card.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(6, 0, 6, 4) });
        var actions = new WrapPanel();
        card.Children.Add(actions);
        string fluid = fluids[i];
        AddSmall(actions, "К пику", () => StubUi.Notify(this, "TopologyPad.JumpPeak:" + fluid));
        AddSmall(actions, "Вставить столбец", () => StubUi.Notify(this, "TopologyPad.Paste:" + fluid));
        AddSmall(actions, "Обнулить", () =>
        {
          foreach (MonthRow row in _rows)
          {
            if (fluid == "Жидкость") row.Liquid = "0";
            else if (fluid == "Нефть") row.Oil = "0";
            else row.Gas = "0";
          }

          _grid.Items.Refresh();
        });
      }

      _grid = new DataGrid
      {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
        RowHeaderWidth = 0,
        FrozenColumnCount = 1,
        ItemsSource = _rows
      };
      _grid.Columns.Add(new DataGridTextColumn
      {
        Header = "Месяц",
        IsReadOnly = true,
        Binding = new Binding(nameof(MonthRow.Month)),
        Width = 65
      });
      _grid.Columns.Add(Editable("Жидкость\nм³/сут", nameof(MonthRow.Liquid)));
      _grid.Columns.Add(Editable("Нефть\nт/сут", nameof(MonthRow.Oil)));
      _grid.Columns.Add(Editable("ПНГ\nм³/сут", nameof(MonthRow.Gas)));
      _grid.Columns.Add(new DataGridTextColumn
      {
        Header = "Ограничивает в месяце",
        IsReadOnly = true,
        Binding = new Binding(nameof(MonthRow.Status)),
        Width = new DataGridLength(1.5, DataGridLengthUnitType.Star),
        MinWidth = 160
      });
      panel.Children.Add(_grid);

      RebuildRows(12);
    }

    private void RebuildRows(int months)
    {
      _rows.Clear();
      for (int m = 1; m <= months; m++)
      {
        _rows.Add(new MonthRow
        {
          Month = m.ToString(CultureInfo.InvariantCulture),
          Liquid = "0",
          Oil = "0",
          Gas = "0",
          Status = "—"
        });
      }
    }

    private static void AddSmall(Panel panel, string title, Action action)
    {
      var button = new Button
      {
        Content = title,
        Padding = new Thickness(8, 4, 8, 4),
        Margin = new Thickness(3)
      };
      button.Click += (_, __) => action();
      panel.Children.Add(button);
    }

    private static TextBlock Label(string text)
    {
      return new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap };
    }

    private static TextBox Box(string value)
    {
      return new TextBox { Text = value, Padding = new Thickness(5) };
    }

    private static DataGridTextColumn Editable(string header, string path)
    {
      return new DataGridTextColumn
      {
        Header = header,
        Binding = new Binding(path)
        {
          Mode = BindingMode.TwoWay,
          UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        },
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        MinWidth = 100
      };
    }

    public sealed class MonthRow
    {
      public string Month { get; set; } = string.Empty;
      public string Liquid { get; set; } = "0";
      public string Oil { get; set; } = "0";
      public string Gas { get; set; } = "0";
      public string Status { get; set; } = "—";
    }
  }
}
