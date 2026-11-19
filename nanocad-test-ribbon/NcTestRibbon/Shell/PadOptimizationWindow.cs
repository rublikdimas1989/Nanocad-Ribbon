namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows;
  using System.Windows.Controls;
  using System.Windows.Data;
  using System.Windows.Input;
  using System.Windows.Media;
  using System.Windows.Threading;
  using Microsoft.Win32;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// KP table from Excel: profiles, CAD overlay (locked layer), place/rotate, save X/Y.
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
    private readonly CheckBox _showOnDrawing;
    private readonly Button _placeButton;
    private readonly Button _rotateButton;
    private readonly Button _saveButton;
    private PadWorkbookLoader.WorkbookData _book;

    public PadOptimizationWindow()
    {
      Title = "Кустовые площадки";
      Width = 1140;
      Height = 700;
      MinWidth = 720;
      MinHeight = 480;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      // Closing the window must NOT erase KP entities — they stay on the drawing.
      HookDispatcherExceptions();

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

      TextBox Field(Panel parent, string label, string value, double width, bool readOnly = true)
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
          IsReadOnly = readOnly
        };
        parent.Children.Add(box);
        return box;
      }

      void Button(Panel parent, string label, Action action, out Button created)
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
            Log(ex.Message);
          }
        };
        parent.Children.Add(button);
        created = button;
      }

      var fluidRow = Row();
      _fluidFile = Field(fluidRow, "Excel свойств:", paths.FluidProperties ?? string.Empty, 420);
      Button(fluidRow, "Обзор…", () => BrowseFluidExcel(), out _);

      var tables = Row();
      _tablesFile = Field(tables, "Таблицы КП (xlsx):", paths.PadsWorkbook ?? string.Empty, 420);
      Button(tables, "Обзор…", () => BrowsePadsWorkbook(), out _);

      var cadRow = Row();
      _showOnDrawing = new CheckBox
      {
        Content = "Показывать на чертеже",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(5, 3, 12, 3)
      };
      _showOnDrawing.Checked += (_, __) =>
      {
        try
        {
          RefreshDrawingOverlay(force: true);
        }
        catch (Exception ex)
        {
          MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
          Log("Показ на чертеже: " + ex.Message);
          _showOnDrawing.IsChecked = false;
        }
      };
      _showOnDrawing.Unchecked += (_, __) =>
      {
        try
        {
          CadPadOverlay.Clear();
          Log("КП на чертеже скрыты (слой " + CadPadOverlay.LayerName + ").");
        }
        catch (Exception ex)
        {
          Log("Не удалось очистить чертёж: " + ex.Message);
        }
      };
      cadRow.Children.Add(_showOnDrawing);

      Button(cadRow, "Задать положение вручную", () => PlaceSelectedPad(), out _placeButton);
      _placeButton.IsEnabled = false;

      Button(cadRow, "Повернуть вручную", () => ManualRotateSelected(), out _rotateButton);
      _rotateButton.IsEnabled = false;

      Button(cadRow, "Сохранить положения", () => SaveCoordinates(), out _saveButton);
      _saveButton.IsEnabled = false;

      top.Children.Add(new TextBlock
      {
        Text = "В таблице правятся только X/Y и угол. «Сернистая среда» — из Excel (Да/Нет), только просмотр. «Повернуть вручную» — вокруг центра КП.",
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
        IsReadOnly = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single
      };
      _grid.Columns.Add(TextCol("КП", nameof(PadRow.Name)));
      _grid.Columns.Add(TextCol("X", nameof(PadRow.X), readOnly: false));
      _grid.Columns.Add(TextCol("Y", nameof(PadRow.Y), readOnly: false));
      _grid.Columns.Add(TextCol("Угол °", nameof(PadRow.RotationText), readOnly: false));
      _grid.Columns.Add(TextCol("Ширина, м", nameof(PadRow.WidthM)));
      _grid.Columns.Add(TextCol("Длина, м", nameof(PadRow.LengthM)));
      _grid.Columns.Add(CheckCol("Сернистая среда", nameof(PadRow.SourMedium), readOnly: true));
      _grid.Columns.Add(CheckCol("Жидкость", nameof(PadRow.Liquid)));
      _grid.Columns.Add(CheckCol("Нефть", nameof(PadRow.Oil)));
      _grid.Columns.Add(CheckCol("ПНГ", nameof(PadRow.Gas)));
      _grid.Columns.Add(TextCol("Первый запуск, месяц", nameof(PadRow.FirstLaunch)));
      _grid.MouseDoubleClick += Grid_MouseDoubleClick;
      _grid.SelectionChanged += (_, __) => UpdateActionButtons();
      _grid.CellEditEnding += Grid_CellEditEnding;
      _grid.LoadingRow += Grid_LoadingRow;
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

      UpdateActionButtons();
    }

    private void Grid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
      if (e.Row.Item is PadRow row && row.CoordsDirty)
      {
        e.Row.Background = Brushes.Yellow;
      }
      else
      {
        e.Row.ClearValue(Control.BackgroundProperty);
      }
    }

    private void Grid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
      if (e.EditAction != DataGridEditAction.Commit || e.Row?.Item is not PadRow row || row.Record == null)
      {
        UpdateActionButtons();
        return;
      }

      string header = e.Column?.Header as string ?? string.Empty;
      if (e.EditingElement is TextBox box)
      {
        string text = box.Text ?? string.Empty;
        if (header == "X" && TryParseCoord(text, out double x))
        {
          row.X = x.ToString("G17", CultureInfo.CurrentCulture);
          row.Record.X = x;
          row.CoordsDirty = true;
        }
        else if (header == "Y" && TryParseCoord(text, out double y))
        {
          row.Y = y.ToString("G17", CultureInfo.CurrentCulture);
          row.Record.Y = y;
          row.CoordsDirty = true;
        }
        else if (header == "Угол °" && TryParseAngle(text, out double angle))
        {
          row.RotationDeg = CadPadOverlay.NormalizeDegrees(angle);
          row.RotationText = row.RotationDeg.ToString("0.###", CultureInfo.CurrentCulture);
          row.CoordsDirty = true;
          if (_showOnDrawing.IsChecked == true)
          {
            try
            {
              CadPadOverlay.UpdatePad(ToGeom(row));
            }
            catch (Exception ex)
            {
              Log("Обновление угла на чертеже: " + ex.Message);
            }
          }
        }
      }

      UpdateActionButtons();
      _grid.Items.Refresh();
    }

    private static bool TryParseCoord(string text, out double value)
    {
      value = 0;
      if (string.IsNullOrWhiteSpace(text))
      {
        return false;
      }

      text = text.Trim().Replace(',', '.');
      return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseAngle(string text, out double angle)
    {
      angle = 0;
      if (!TryParseCoord(text, out angle))
      {
        return false;
      }

      return double.IsFinite(angle) && angle >= 0 && angle <= 360;
    }

    private void UpdateActionButtons()
    {
      bool hasRow = _grid.SelectedItem is PadRow { Record: not null };
      _placeButton.IsEnabled = hasRow;
      _rotateButton.IsEnabled = hasRow;
      _saveButton.IsEnabled = _pads.Any(p => p.CoordsDirty);
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

    private void ManualRotateSelected()
    {
      if (_grid.SelectedItem is not PadRow row || row.Record == null)
      {
        throw new InvalidOperationException("Выберите КП в таблице.");
      }

      // Ensure the pad exists on the drawing before ROTATE.
      if (_showOnDrawing.IsChecked != true)
      {
        _showOnDrawing.IsChecked = true;
      }
      else
      {
        CadPadOverlay.UpdatePad(ToGeom(row));
      }

      RunWithCadFocus(() =>
      {
        if (!CadPadOverlay.TryManualRotate(row.Name, out double x, out double y, out double rot, out bool poseUpdated))
        {
          Log("Поворот «" + row.Name + "» отменён.");
          return;
        }

        if (poseUpdated)
        {
          row.Record.X = x;
          row.Record.Y = y;
          row.X = x.ToString("G17", CultureInfo.CurrentCulture);
          row.Y = y.ToString("G17", CultureInfo.CurrentCulture);
          row.RotationDeg = CadPadOverlay.NormalizeDegrees(rot);
          row.RotationText = row.RotationDeg.ToString("0.###", CultureInfo.CurrentCulture);
          row.CoordsDirty = true;
          _grid.Items.Refresh();
          UpdateActionButtons();
          Log("Поворот вручную «" + row.Name + "»: X=" + row.X + "; Y=" + row.Y
            + "; угол=" + row.RotationText + "°");
        }
        else
        {
          Log("Поворот «" + row.Name + "» выполнен; позу с чертежа прочитать не удалось.");
        }
      });
    }

    private void PlaceSelectedPad()
    {
      if (_grid.SelectedItem is not PadRow row || row.Record == null)
      {
        throw new InvalidOperationException("Выберите КП в таблице.");
      }

      var geom = ToGeom(row);
      RunWithCadFocus(() =>
      {
        if (!CadPadOverlay.TryPlace(geom, out double x, out double y))
        {
          Log("Размещение «" + row.Name + "» отменено.");
          return;
        }

        row.Record.X = x;
        row.Record.Y = y;
        row.X = x.ToString("G17", CultureInfo.CurrentCulture);
        row.Y = y.ToString("G17", CultureInfo.CurrentCulture);
        row.CoordsDirty = true;

        // Always materialize the placed pad on the drawing.
        if (_showOnDrawing.IsChecked != true)
        {
          _showOnDrawing.IsChecked = true;
        }
        else
        {
          CadPadOverlay.UpdatePad(ToGeom(row));
        }

        _grid.Items.Refresh();
        UpdateActionButtons();
        Log("Новые координаты «" + row.Name + "»: X=" + row.X + "; Y=" + row.Y
          + "; угол=" + row.RotationText + "°");
      });
    }

    /// <summary>
    /// Yield the modal WPF dialog to CAD (jig / zoom / ROTATE) without Hide()/Show().
    /// Hide+Show on a ShowDialog window owned by nCad/Qt re-enters PresentationFramework
    /// through hostQtUi and can crash with CLR 0xE0434352 (WPF frames on the stack).
    /// </summary>
    private void RunWithCadFocus(Action cadWork)
    {
      Visibility previous = Visibility;
      bool wasEnabled = IsEnabled;
      try
      {
        IsEnabled = false;
        Visibility = Visibility.Collapsed;
        try
        {
          Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }
        catch
        {
        }

        cadWork?.Invoke();
      }
      catch (Exception ex)
      {
        string msg = ex.GetType().FullName + ": " + ex.Message;
        WriteCrashLog("CAD interaction", ex);
        Log(msg);
        try
        {
          MessageBox.Show(this, msg, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
        }
      }
      finally
      {
        try
        {
          Visibility = previous == Visibility.Collapsed ? Visibility.Visible : previous;
          IsEnabled = wasEnabled;
          if (Visibility == Visibility.Visible)
          {
            Activate();
          }
        }
        catch (Exception ex)
        {
          WriteCrashLog("UI restore", ex);
          try
          {
            Log("UI restore: " + ex.Message);
          }
          catch
          {
          }
        }
      }
    }

    private void HookDispatcherExceptions()
    {
      try
      {
        Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        Dispatcher.UnhandledException += OnDispatcherUnhandledException;
      }
      catch
      {
      }

      try
      {
        if (Application.Current != null)
        {
          Application.Current.DispatcherUnhandledException -= OnAppDispatcherUnhandledException;
          Application.Current.DispatcherUnhandledException += OnAppDispatcherUnhandledException;
        }
      }
      catch
      {
      }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
      try
      {
        WriteCrashLog("Dispatcher", e.Exception);
        Log("WPF: " + e.Exception.GetType().Name + ": " + e.Exception.Message);
        e.Handled = true;
      }
      catch
      {
        e.Handled = true;
      }
    }

    private static void OnAppDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
      try
      {
        WriteCrashLog("AppDispatcher", e.Exception);
        e.Handled = true;
      }
      catch
      {
        e.Handled = true;
      }
    }

    private static void WriteCrashLog(string where, Exception ex)
    {
      try
      {
        TracingCache.EnsureCreated();
        string path = Path.Combine(TracingCache.SettingsDir, "nc_test_ribbon_crash.log");
        string line = DateTime.Now.ToString("s", CultureInfo.InvariantCulture)
          + " [" + where + "] "
          + (ex?.GetType().FullName ?? "?") + ": " + (ex?.Message ?? "")
          + Environment.NewLine
          + (ex?.StackTrace ?? "")
          + Environment.NewLine
          + "----"
          + Environment.NewLine;
        File.AppendAllText(path, line);
      }
      catch
      {
      }
    }

    private void SaveCoordinates()
    {
      if (_book == null || string.IsNullOrWhiteSpace(_book.Path))
      {
        throw new InvalidOperationException("Нет открытого файла таблиц КП.");
      }

      List<PadWorkbookLoader.PadRecord> dirty = _pads
        .Where(p => p.CoordsDirty && p.Record != null)
        .Select(p => p.Record)
        .ToList();
      if (dirty.Count == 0)
      {
        throw new InvalidOperationException("Нет изменённых координат.");
      }

      MessageBoxResult confirm = MessageBox.Show(
        this,
        "Перезаписать положения (X/Y) в файле?\n\n" + _book.Path + "\n\nСтрок: " + dirty.Count,
        "Сохранить положения",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
      if (confirm != MessageBoxResult.Yes)
      {
        return;
      }

      int updated = PadWorkbookLoader.SavePadCoordinates(_book.Path, dirty);
      foreach (PadRow row in _pads)
      {
        row.CoordsDirty = false;
      }

      _grid.Items.Refresh();
      UpdateActionButtons();
      Log("Сохранено координат в Excel: " + updated + " (" + Path.GetFileName(_book.Path) + ")");
    }

    private void RefreshDrawingOverlay(bool force = false)
    {
      if (!force && _showOnDrawing.IsChecked != true)
      {
        return;
      }

      if (_pads.Count == 0)
      {
        CadPadOverlay.Clear();
        Log("Нет КП для отображения.");
        return;
      }

      int drawn = CadPadOverlay.ShowPads(_pads.Select(ToGeom));
      Log("На чертеже обновлено КП: " + drawn
        + " (слой " + CadPadOverlay.LayerName + ", заблокирован; 0°: Длина→X, Ширина→Y).");
    }

    private static CadPadOverlay.PadGeom ToGeom(PadRow row)
    {
      double width = 150;
      double length = 250;
      double.TryParse((row.WidthM ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out width);
      double.TryParse((row.LengthM ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out length);
      if (row.Record != null)
      {
        width = row.Record.WidthM;
        length = row.Record.LengthM;
      }

      return new CadPadOverlay.PadGeom
      {
        Name = row.Name,
        X = row.Record?.X ?? 0,
        Y = row.Record?.Y ?? 0,
        WidthM = width > 0 ? width : 150,
        LengthM = length > 0 ? length : 250,
        RotationDeg = row.RotationDeg
      };
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
          SourMedium = pad.SourMedium,
          Oil = pad.ActiveOil,
          Liquid = pad.ActiveLiquid,
          Gas = pad.ActiveGas,
          FirstLaunch = "1",
          Record = pad,
          CoordsDirty = false,
          RotationDeg = 0,
          RotationText = "0"
        });
      }

      _tablesFile.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.PadsWorkbook = path);
      }

      // Overlay sourMedium from sibling / remembered Top_tree JSON when Excel has no column.
      int fromJson = 0;
      if (!book.SourMediumFromSheet)
      {
        string jsonHint = AppPaths.Load().ProfilesJson;
        fromJson = PadWorkbookLoader.ApplySourMediumFromJson(book, jsonHint);
        if (fromJson > 0)
        {
          foreach (PadRow row in _pads)
          {
            if (row.Record != null)
            {
              row.SourMedium = row.Record.SourMedium;
            }
          }
        }
      }

      foreach (PadRow row in _pads)
      {
        row.CoordsDirty = false;
      }

      string caps = book.CapacitiesFromBuosSheet
        ? "ёмкости БУОС из листа"
        : "ёмкости БУОС по умолчанию";
      Log("Загружено КП: " + book.Pads.Count
        + "; профилей мес.: " + book.Months
        + "; " + caps
        + (fromJson > 0 ? "; sourMedium из JSON: " + fromJson : string.Empty)
        + " (" + Path.GetFileName(path) + ")");
      UpdateActionButtons();
      if (_showOnDrawing.IsChecked == true)
      {
        RefreshDrawingOverlay(force: true);
      }
    }

    private void Log(string text)
    {
      _log.AppendText(text + Environment.NewLine);
      _log.ScrollToEnd();
      _status.Text = text;
    }

    private static DataGridTextColumn TextCol(string header, string path, bool readOnly = true)
    {
      return new DataGridTextColumn
      {
        Header = header,
        Binding = new Binding(path)
        {
          Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
          UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
        },
        IsReadOnly = readOnly,
        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
      };
    }

    private static DataGridCheckBoxColumn CheckCol(string header, string path, bool readOnly = true)
    {
      return new DataGridCheckBoxColumn
      {
        Header = header,
        Binding = new Binding(path),
        IsReadOnly = readOnly,
        Width = header.Length > 10 ? 120 : 85
      };
    }

    public sealed class PadRow
    {
      public string Name { get; set; } = string.Empty;
      public string X { get; set; } = "0";
      public string Y { get; set; } = "0";
      public string WidthM { get; set; } = "150";
      public string LengthM { get; set; } = "250";
      public string RotationText { get; set; } = "0";
      public double RotationDeg { get; set; }
      /// <summary>Display-only: from Excel «Сернистая среда» (Да/Нет).</summary>
      public bool SourMedium { get; set; }
      public bool Liquid { get; set; } = true;
      public bool Oil { get; set; } = true;
      public bool Gas { get; set; } = true;
      public string FirstLaunch { get; set; } = "1";
      public bool CoordsDirty { get; set; }
      public PadWorkbookLoader.PadRecord Record { get; set; }
    }
  }
}
