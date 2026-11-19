namespace NcTestRibbon.Shell
{
  using System;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Data;
  using System.Windows.Input;
  using System.Windows.Media;
  using Microsoft.Win32;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// Display-only KP table from Excel. Double-click opens profiles + «Ограничивает в месяце».
  /// </summary>
  public sealed class PadOptimizationWindow : Window
  {
    private readonly System.Collections.ObjectModel.ObservableCollection<PadRow> _pads
      = new System.Collections.ObjectModel.ObservableCollection<PadRow>();
    private readonly TextBox _fluidFile;
    private readonly TextBox _tablesFile;
    private readonly TextBox _log;
    private readonly TextBlock _status;
    private readonly DataGrid _grid;
    private PadWorkbookLoader.WorkbookData _book;

    public PadOptimizationWindow()
    {
      Title = "Кустовые площадки";
      Width = 1100;
      Height = 640;
      MinWidth = 720;
      MinHeight = 480;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;

      AppPaths paths = AppPaths.Load();
      TracingCache.EnsureCreated();

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
          Margin = new Thickness(3),
          IsReadOnly = true
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

      var fluidRow = Row();
      _fluidFile = Field(fluidRow, "Excel свойств:", paths.FluidProperties ?? string.Empty, 420);
      Button(fluidRow, "Обзор…", () => BrowseFluidExcel());

      var tables = Row();
      _tablesFile = Field(tables, "Таблицы КП (xlsx):", paths.PadsWorkbook ?? string.Empty, 420);
      Button(tables, "Обзор…", () => BrowsePadsWorkbook());

      top.Children.Add(new TextBlock
      {
        Text = "Только просмотр. Двойной щелчок по КП — профили и «Ограничивает в месяце». Правки — в Excel.",
        Margin = new Thickness(5, 2, 5, 6),
        Foreground = Brushes.DarkSlateGray,
        TextWrapping = TextWrapping.Wrap
      });

      _status = new TextBlock
      {
        Text = "Откройте таблицы КП (xlsx).",
        Margin = new Thickness(4),
        Foreground = Brushes.DarkSlateGray
      };
      DockPanel.SetDock(_status, Dock.Bottom);
      root.Children.Add(_status);

      _log = new TextBox
      {
        Height = 90,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
      };
      DockPanel.SetDock(_log, Dock.Bottom);
      root.Children.Add(_log);

      _grid = new DataGrid
      {
        ItemsSource = _pads,
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single
      };
      _grid.Columns.Add(TextCol("КП", nameof(PadRow.Name)));
      _grid.Columns.Add(TextCol("X", nameof(PadRow.X)));
      _grid.Columns.Add(TextCol("Y", nameof(PadRow.Y)));
      _grid.Columns.Add(TextCol("Ширина, м", nameof(PadRow.WidthM)));
      _grid.Columns.Add(TextCol("Длина, м", nameof(PadRow.LengthM)));
      _grid.Columns.Add(CheckCol("Жидкость", nameof(PadRow.Liquid)));
      _grid.Columns.Add(CheckCol("Нефть", nameof(PadRow.Oil)));
      _grid.Columns.Add(CheckCol("ПНГ", nameof(PadRow.Gas)));
      _grid.Columns.Add(TextCol("Первый запуск, месяц", nameof(PadRow.FirstLaunch)));
      _grid.MouseDoubleClick += Grid_MouseDoubleClick;
      root.Children.Add(_grid);

      if (!string.IsNullOrWhiteSpace(paths.FluidProperties) && File.Exists(paths.FluidProperties))
      {
        LoadFluidProperties(paths.FluidProperties, remember: false);
      }

      if (!string.IsNullOrWhiteSpace(paths.PadsWorkbook) && File.Exists(paths.PadsWorkbook))
      {
        try
        {
          LoadPadsWorkbook(paths.PadsWorkbook, remember: false);
        }
        catch (Exception ex)
        {
          Log("Не удалось загрузить прошлые таблицы КП: " + ex.Message);
        }
      }
      else
      {
        Log("Откройте таблицы КП (xlsx) через «Обзор…».");
      }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      if (_grid.SelectedItem is not PadRow row || row.Record == null)
      {
        return;
      }

      try
      {
        OpenPadDetail(row.Record);
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    private void OpenPadDetail(PadWorkbookLoader.PadRecord pad)
    {
      if (_book == null)
      {
        throw new InvalidOperationException("Сначала откройте таблицы КП (xlsx).");
      }

      PadWorkbookLoader.PadRecord live = _book.Pads.FirstOrDefault(p =>
        string.Equals(p.Name, pad.Name, StringComparison.OrdinalIgnoreCase)) ?? pad;
      PadLoadPreview.Result load = PadLoadPreview.Build(live, _book);
      var detail = new TopologyPadDetailWindow(live, _book, load) { Owner = this };
      detail.ShowDialog();
      Log("Профили: " + live.Name + " (" + (load.Months?.Count ?? 0) + " мес.)");
    }

    private void BrowseFluidExcel()
    {
      var dialog = new OpenFileDialog
      {
        Title = "Excel свойств флюида",
        Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|All|*.*",
        CheckFileExists = true
      };
      if (!string.IsNullOrWhiteSpace(_fluidFile.Text) && File.Exists(_fluidFile.Text))
      {
        dialog.InitialDirectory = Path.GetDirectoryName(_fluidFile.Text);
        dialog.FileName = Path.GetFileName(_fluidFile.Text);
      }

      if (dialog.ShowDialog(this) == true)
      {
        LoadFluidProperties(dialog.FileName, remember: true);
      }
    }

    private void LoadFluidProperties(string path, bool remember)
    {
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new FileNotFoundException("Файл свойств флюида не найден.", path);
      }

      _fluidFile.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.FluidProperties = path);
      }

      Log("Свойства загружены: " + Path.GetFileName(path));
    }

    private void BrowsePadsWorkbook()
    {
      var dialog = new OpenFileDialog
      {
        Title = "Таблицы КП / Top_tree (xlsx)",
        Filter = "Excel (*.xlsx)|*.xlsx",
        CheckFileExists = true
      };
      string current = _tablesFile.Text;
      if (!string.IsNullOrWhiteSpace(current) && File.Exists(current))
      {
        dialog.InitialDirectory = Path.GetDirectoryName(current);
        dialog.FileName = Path.GetFileName(current);
      }

      if (dialog.ShowDialog(this) != true)
      {
        return;
      }

      LoadPadsWorkbook(dialog.FileName, remember: true);
    }

    private void LoadPadsWorkbook(string path, bool remember)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        throw new InvalidOperationException("Укажите файл таблиц КП (xlsx).");
      }

      PadWorkbookLoader.WorkbookData book = PadWorkbookLoader.LoadWorkbook(path);
      _book = book;
      _pads.Clear();
      foreach (PadWorkbookLoader.PadRecord pad in book.Pads)
      {
        _pads.Add(new PadRow
        {
          Name = pad.Name,
          X = pad.X.ToString("G17", CultureInfo.CurrentCulture),
          Y = pad.Y.ToString("G17", CultureInfo.CurrentCulture),
          WidthM = pad.WidthM.ToString("0.###", CultureInfo.CurrentCulture),
          LengthM = pad.LengthM.ToString("0.###", CultureInfo.CurrentCulture),
          Oil = pad.ActiveOil,
          Liquid = pad.ActiveLiquid,
          Gas = pad.ActiveGas,
          FirstLaunch = "1",
          Record = pad
        });
      }

      _tablesFile.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.PadsWorkbook = path);
      }

      string caps = book.CapacitiesFromBuosSheet
        ? "ёмкости БУОС из листа"
        : "ёмкости БУОС по умолчанию";
      Log("Загружено КП: " + book.Pads.Count
        + "; профилей мес.: " + book.Months
        + "; " + caps
        + " (" + Path.GetFileName(path) + ")");
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
        IsReadOnly = true,
        Width = 85
      };
    }

    public sealed class PadRow
    {
      public string Name { get; set; } = string.Empty;
      public string X { get; set; } = "0";
      public string Y { get; set; } = "0";
      public string WidthM { get; set; } = "150";
      public string LengthM { get; set; } = "250";
      public bool Liquid { get; set; } = true;
      public bool Oil { get; set; } = true;
      public bool Gas { get; set; } = true;
      public string FirstLaunch { get; set; } = "1";
      public PadWorkbookLoader.PadRecord Record { get; set; }
    }
  }
}
