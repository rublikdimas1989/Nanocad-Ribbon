namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows.Forms;
  using System.Windows.Interop;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;
  using WpfApp = System.Windows.Application;
  using WpfShutdownMode = System.Windows.ShutdownMode;

  /// <summary>
  /// KP table from Excel (WinForms DataGridView): profiles, CAD overlay, place/rotate, save X/Y.
  /// Modeless: stays open across place/rotate. Unsaved moves revert CAD to Excel on close.
  /// </summary>
  public sealed class PadOptimizationWindow : UserControl
  {
    private readonly BindingList<PadRow> _pads = new BindingList<PadRow>();
    private readonly TextBox _fluidFile;
    private readonly TextBox _tablesFile;
    private readonly TextBox _log;
    private readonly Label _status;
    private readonly DataGridView _grid;
    private readonly CheckBox _showOnDrawing;
    private readonly Button _placeButton;
    private readonly Button _rotateButton;
    private readonly Button _saveButton;
    private PadWorkbookLoader.WorkbookData _book;
    private bool _suppressShowToggle;
    private bool _discardingSession;

    public PadOptimizationWindow()
    {
      // Hosted in SourceDataWindow; closing host discards unsaved place/rotate.
      Text = "Кустовые площадки";
      Dock = DockStyle.Fill;
      AppPaths paths = AppPaths.Load();
      TracingCache.EnsureCreated();

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 4,
        Padding = new Padding(8)
      };
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100f));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
      Controls.Add(root);

      var top = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(0, 0, 0, 4)
      };
      root.Controls.Add(top, 0, 0);

      FlowLayoutPanel Row()
      {
        var row = new FlowLayoutPanel
        {
          AutoSize = true,
          WrapContents = true,
          Margin = new Padding(0, 2, 0, 2)
        };
        top.Controls.Add(row);
        return row;
      }

      TextBox Field(Control parent, string label, string value, int width)
      {
        parent.Controls.Add(new Label
        {
          Text = label,
          AutoSize = true,
          Margin = new Padding(5, 8, 0, 0)
        });
        var box = new TextBox
        {
          Text = value ?? string.Empty,
          Width = width,
          Margin = new Padding(3),
          ReadOnly = true
        };
        parent.Controls.Add(box);
        return box;
      }

      Button MakeButton(Control parent, string label, Action action, out Button created)
      {
        var button = new Button
        {
          Text = label,
          AutoSize = true,
          Margin = new Padding(3),
          Padding = new Padding(6, 3, 6, 3)
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
              MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log(ex.Message);
          }
        };
        parent.Controls.Add(button);
        created = button;
        return button;
      }

      var fluidRow = Row();
      _fluidFile = Field(fluidRow, "Excel свойств:", paths.FluidProperties ?? string.Empty, 420);
      MakeButton(fluidRow, "Обзор…", () => BrowseFluidExcel(), out _);

      var tables = Row();
      _tablesFile = Field(tables, "Таблицы КП (xlsx):", paths.PadsWorkbook ?? string.Empty, 420);
      MakeButton(tables, "Обзор…", () => BrowsePadsWorkbook(), out _);

      var cadRow = Row();
      _showOnDrawing = new CheckBox
      {
        Text = "Показывать на чертеже",
        AutoSize = true,
        Margin = new Padding(5, 6, 12, 3)
      };
      _showOnDrawing.CheckedChanged += (_, __) =>
      {
        if (_suppressShowToggle)
        {
          return;
        }

        try
        {
          AppPaths.Set(p => p.ShowPadsOnDrawing = _showOnDrawing.Checked);
          if (_showOnDrawing.Checked)
          {
            RefreshDrawingOverlay(force: true);
          }
          else
          {
            CadPadOverlay.Clear();
            Log("КП на чертеже скрыты (слой " + CadPadOverlay.LayerName + ").");
          }
        }
        catch (Exception ex)
        {
          MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          Log("Показ на чертеже: " + ex.Message);
          _suppressShowToggle = true;
          try
          {
            _showOnDrawing.Checked = false;
            AppPaths.Set(p => p.ShowPadsOnDrawing = false);
          }
          finally
          {
            _suppressShowToggle = false;
          }
        }
      };
      cadRow.Controls.Add(_showOnDrawing);

      MakeButton(cadRow, "Задать положение вручную", () => PlaceSelectedPad(), out _placeButton);
      _placeButton.Enabled = false;

      MakeButton(cadRow, "Повернуть вручную", () => ManualRotateSelected(), out _rotateButton);
      _rotateButton.Enabled = false;

      MakeButton(cadRow, "Сохранить положения", () => SaveCoordinates(), out _saveButton);
      _saveButton.Enabled = false;

      top.Controls.Add(new Label
      {
        Text = "Таблица только для просмотра (все поля из Excel). Положение/угол — только «Задать положение вручную» / «Повернуть вручную» на чертеже.",
        AutoSize = true,
        MaximumSize = new Size(1080, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(5, 4, 5, 6)
      });

      _grid = new DataGridView
      {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        ReadOnly = true,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
      };
      _grid.Columns.Add(TextCol("КП", nameof(PadRow.Name), 70));
      _grid.Columns.Add(TextCol("X", nameof(PadRow.X), 90));
      _grid.Columns.Add(TextCol("Y", nameof(PadRow.Y), 90));
      _grid.Columns.Add(TextCol("Угол °", nameof(PadRow.RotationText), 70));
      _grid.Columns.Add(TextCol("Ширина, м", nameof(PadRow.WidthM), 80));
      _grid.Columns.Add(TextCol("Длина, м", nameof(PadRow.LengthM), 80));
      _grid.Columns.Add(CheckCol("Сернистая среда", nameof(PadRow.SourMedium), 110));
      _grid.Columns.Add(CheckCol("Жидкость", nameof(PadRow.Liquid), 80));
      _grid.Columns.Add(CheckCol("Нефть", nameof(PadRow.Oil), 70));
      _grid.Columns.Add(CheckCol("ПНГ", nameof(PadRow.Gas), 60));
      _grid.Columns.Add(TextCol("Первый запуск, месяц", nameof(PadRow.FirstLaunch), 110));
      _grid.CellDoubleClick += Grid_CellDoubleClick;
      _grid.SelectionChanged += (_, __) => UpdateActionButtons();
      _grid.CellFormatting += Grid_CellFormatting;
      root.Controls.Add(_grid, 0, 1);

      _log = new TextBox
      {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true
      };
      root.Controls.Add(_log, 0, 2);

      _status = new Label
      {
        Dock = DockStyle.Fill,
        Text = "Откройте таблицы КП (xlsx).",
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.DimGray
      };
      root.Controls.Add(_status, 0, 3);

      BindPadsGrid();

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

      // Restore «Показывать на чертеже» after workbook load so overlay uses current rows.
      if (paths.ShowPadsOnDrawing)
      {
        _suppressShowToggle = true;
        try
        {
          _showOnDrawing.Checked = true;
        }
        finally
        {
          _suppressShowToggle = false;
        }

        try
        {
          RefreshDrawingOverlay(force: true);
        }
        catch (Exception ex)
        {
          Log("Показ на чертеже: " + ex.Message);
        }
      }

      UpdateActionButtons();
      Load += (_, __) => BindPadsGrid();
    }

    public void NotifyHostClosing()
    {
      try
      {
        AppPaths.Set(p => p.ShowPadsOnDrawing = _showOnDrawing.Checked);
      }
      catch
      {
      }

      try
      {
        if (!_pads.Any(p => p.CoordsDirty))
        {
          return;
        }

        DialogResult apply = MessageBox.Show(
          FindForm() ?? (IWin32Window)this,
          "Применить новые координаты?",
          "Кустовые площадки",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (apply == DialogResult.Yes)
        {
          SaveCoordinates(confirmOverwrite: false);
        }
        else
        {
          DiscardUnsavedSession();
        }
      }
      catch (Exception ex)
      {
        WriteCrashLog("FormClosing coords", ex);
        try
        {
          Log("Закрытие: координаты КП — " + ex.Message);
        }
        catch
        {
        }
      }
    }

    /// <summary>
    /// Without «Сохранить положения», restore Record/grid/CAD to last Excel-committed pose.
    /// </summary>
    private void DiscardUnsavedSession()
    {
      if (_discardingSession)
      {
        return;
      }

      _discardingSession = true;
      try
      {
        List<PadRow> dirty = _pads.Where(p => p.CoordsDirty).ToList();
        if (dirty.Count == 0)
        {
          return;
        }

        foreach (PadRow row in dirty)
        {
          row.RestoreCommitted();
        }

        BindPadsGrid();
        UpdateActionButtons();

        // Rewrite CAD to last Excel-committed poses (temporary preview discarded).
        CadPadOverlay.ShowPads(_pads.Select(ToGeom));
        if (!_showOnDrawing.Checked)
        {
          CadPadOverlay.Clear();
        }

        Log("Несохранённые положения отменены («" + dirty.Count
          + "» КП → последние из Excel).");
      }
      finally
      {
        _discardingSession = false;
      }
    }

    private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
      if (e.RowIndex < 0 || e.RowIndex >= _pads.Count)
      {
        return;
      }

      PadRow row = _pads[e.RowIndex];
      DataGridViewRow viewRow = _grid.Rows[e.RowIndex];
      viewRow.DefaultCellStyle.BackColor = row.CoordsDirty ? Color.Yellow : _grid.DefaultCellStyle.BackColor;
    }

    private void UpdateActionButtons()
    {
      PadRow row = SelectedPad();
      bool hasRow = row?.Record != null;
      _placeButton.Enabled = hasRow;
      _rotateButton.Enabled = hasRow;
      _saveButton.Enabled = _pads.Any(p => p.CoordsDirty);
    }

    private PadRow SelectedPad()
    {
      if (_grid.CurrentRow == null || _grid.CurrentRow.Index < 0 || _grid.CurrentRow.Index >= _pads.Count)
      {
        return null;
      }

      return _pads[_grid.CurrentRow.Index];
    }

    private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
      if (e.RowIndex < 0)
      {
        return;
      }

      PadRow row = SelectedPad();
      if (row?.Record == null)
      {
        return;
      }

      try
      {
        OpenPadDetail(row.Record);
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    private void OpenPadDetail(PadWorkbookLoader.PadRecord pad)
    {
      if (_book == null)
      {
        throw new InvalidOperationException("Сначала откройте таблицы КП (xlsx).");
      }

      EnsureWpfApp();
      PadWorkbookLoader.PadRecord live = _book.Pads.FirstOrDefault(p =>
        string.Equals(p.Name, pad.Name, StringComparison.OrdinalIgnoreCase)) ?? pad;
      PadLoadPreview.Result load = PadLoadPreview.Build(live, _book);
      var detail = new TopologyPadDetailWindow(live, _book, load);
      new WindowInteropHelper(detail) { Owner = Handle };
      detail.ShowDialog();
      Log("Профили: " + live.Name + " (" + (load.Months?.Count ?? 0) + " мес.)");
    }

    private static void EnsureWpfApp()
    {
      if (WpfApp.Current != null)
      {
        return;
      }

      try
      {
        new WpfApp { ShutdownMode = WpfShutdownMode.OnExplicitShutdown };
      }
      catch
      {
      }
    }

    private void ManualRotateSelected()
    {
      PadRow row = SelectedPad();
      if (row?.Record == null)
      {
        throw new InvalidOperationException("Выберите КП в таблице.");
      }

      if (!_showOnDrawing.Checked)
      {
        _showOnDrawing.Checked = true;
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
          row.ApplyPreviewPose(x, y, CadPadOverlay.NormalizeDegrees(rot));
          Log("Поворот вручную «" + row.Name + "»: X=" + row.X + "; Y=" + row.Y
            + "; угол=" + row.RotationText + "°");
        }
        else
        {
          Log("Поворот «" + row.Name + "» выполнен; позу с чертежа прочитать не удалось.");
        }
      });
      RefreshPadRow(row);
      UpdateActionButtons();
    }

    private void PlaceSelectedPad()
    {
      PadRow row = SelectedPad();
      if (row?.Record == null)
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

        row.ApplyPreviewPose(x, y, row.RotationDeg);

        if (!_showOnDrawing.Checked)
        {
          _showOnDrawing.Checked = true;
        }
        else
        {
          CadPadOverlay.UpdatePad(ToGeom(row));
        }

        Log("Новые координаты «" + row.Name + "»: X=" + row.X + "; Y=" + row.Y
          + "; угол=" + row.RotationText + "°");
      });
      RefreshPadRow(row);
      UpdateActionButtons();
    }

    /// <summary>
    /// Yield host Form to CAD (jig / zoom / ROTATE). Hide host — does not dispose the window.
    /// </summary>
    private void RunWithCadFocus(Action cadWork)
    {
      bool wasEnabled = Enabled;
      Form host = FindForm();
      try
      {
        Enabled = false;
        if (host != null && !host.IsDisposed)
        {
          host.Hide();
        }
        else
        {
          Hide();
        }

        try
        {
          Application.DoEvents();
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
          MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
      finally
      {
        try
        {
          if (!IsDisposed)
          {
            Enabled = wasEnabled;
            if (host != null && !host.IsDisposed)
            {
              host.Show();
              host.Activate();
              host.BringToFront();
            }
            else
            {
              Show();
              Focus();
              BringToFront();
            }

            BindPadsGrid(SelectedPad());
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

    private void RefreshPadRow(PadRow row)
    {
      if (row == null)
      {
        BindPadsGrid();
        return;
      }

      int index = _pads.IndexOf(row);
      if (index >= 0)
      {
        _pads.ResetItem(index);
      }

      BindPadsGrid(row);
    }

    /// <summary>
    /// Rebind DataGridView to BindingList and log counts into status/log.
    /// </summary>
    private void BindPadsGrid(PadRow preferSelect = null)
    {
      try
      {
        string keepName = preferSelect?.Name ?? SelectedPad()?.Name;

        // Reset DataSource so WinForms rebuilds all rows from _pads.
        _grid.DataSource = null;
        _grid.DataSource = _pads;

        int gridRows = _grid.Rows.Count;
        int pads = _pads.Count;
        string bindMsg = "Сетка: pads=" + pads + ", grid.Rows=" + gridRows
          + ", DataSource=" + (_grid.DataSource != null ? _grid.DataSource.GetType().Name : "null");
        _status.Text = bindMsg;
        Log(bindMsg);

        if (!string.IsNullOrEmpty(keepName))
        {
          for (int i = 0; i < _pads.Count; i++)
          {
            if (string.Equals(_pads[i].Name, keepName, StringComparison.OrdinalIgnoreCase))
            {
              _grid.ClearSelection();
              _grid.Rows[i].Selected = true;
              _grid.CurrentCell = _grid.Rows[i].Cells[0];
              try
              {
                _grid.FirstDisplayedScrollingRowIndex = i;
              }
              catch
              {
              }
              break;
            }
          }
        }
      }
      catch (Exception ex)
      {
        WriteCrashLog("BindPadsGrid", ex);
        try
        {
          Log("BindPadsGrid: " + ex.Message);
        }
        catch
        {
        }
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
      SaveCoordinates(confirmOverwrite: true);
    }

    private void SaveCoordinates(bool confirmOverwrite)
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

      if (confirmOverwrite)
      {
        DialogResult confirm = MessageBox.Show(
          this,
          "Перезаписать положения (X/Y) в файле?\n\n" + _book.Path + "\n\nСтрок: " + dirty.Count,
          "Сохранить положения",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
          return;
        }
      }

      int updated = PadWorkbookLoader.SavePadCoordinates(_book.Path, dirty);
      foreach (PadRow row in _pads)
      {
        if (row.CoordsDirty)
        {
          row.CaptureCommitted();
        }
      }

      BindPadsGrid();
      UpdateActionButtons();
      Log("Сохранено координат в Excel: " + updated + " (" + Path.GetFileName(_book.Path) + ")");
    }

    private void RefreshDrawingOverlay(bool force = false)
    {
      if (!force && !_showOnDrawing.Checked)
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
      using var dialog = new OpenFileDialog
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

      if (dialog.ShowDialog(this) == DialogResult.OK)
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
      using var dialog = new OpenFileDialog
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

      if (dialog.ShowDialog(this) != DialogResult.OK)
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
        var row = new PadRow
        {
          Name = pad.Name,
          WidthM = pad.WidthM.ToString("0.###", CultureInfo.CurrentCulture),
          LengthM = pad.LengthM.ToString("0.###", CultureInfo.CurrentCulture),
          SourMedium = pad.SourMedium,
          Oil = pad.ActiveOil,
          Liquid = pad.ActiveLiquid,
          Gas = pad.ActiveGas,
          FirstLaunch = "1",
          Record = pad,
          RotationDeg = 0,
          RotationText = "0"
        };
        row.CaptureCommittedFromExcel(pad.X, pad.Y, rotationDeg: 0);
        _pads.Add(row);
      }

      _tablesFile.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.PadsWorkbook = path);
      }

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

      string caps = book.CapacitiesFromBuosSheet
        ? "ёмкости БУОС из листа"
        : "ёмкости БУОС по умолчанию";
      Log("Загружено КП: " + book.Pads.Count
        + "; профилей мес.: " + book.Months
        + "; " + caps
        + (fromJson > 0 ? "; sourMedium из JSON: " + fromJson : string.Empty)
        + " (" + Path.GetFileName(path) + ")");
      BindPadsGrid();
      UpdateActionButtons();
      if (_showOnDrawing.Checked)
      {
        RefreshDrawingOverlay(force: true);
      }
    }

    private void Log(string text)
    {
      _log.AppendText(text + Environment.NewLine);
      _log.SelectionStart = _log.TextLength;
      _log.ScrollToCaret();
      _status.Text = text;
    }

    private static DataGridViewTextBoxColumn TextCol(string header, string prop, int fillWeight)
    {
      return new DataGridViewTextBoxColumn
      {
        HeaderText = header,
        DataPropertyName = prop,
        Name = prop,
        ReadOnly = true,
        FillWeight = fillWeight
      };
    }

    private static DataGridViewCheckBoxColumn CheckCol(string header, string prop, int fillWeight)
    {
      return new DataGridViewCheckBoxColumn
      {
        HeaderText = header,
        DataPropertyName = prop,
        Name = prop,
        ReadOnly = true,
        FillWeight = fillWeight,
        TrueValue = true,
        FalseValue = false
      };
    }

    public sealed class PadRow : INotifyPropertyChanged
    {
      private string _x = "0";
      private string _y = "0";
      private string _rotationText = "0";
      private double _rotationDeg;
      private bool _coordsDirty;

      public string Name { get; set; } = string.Empty;
      public string X
      {
        get => _x;
        set
        {
          if (_x == value)
          {
            return;
          }

          _x = value;
          OnPropertyChanged(nameof(X));
        }
      }

      public string Y
      {
        get => _y;
        set
        {
          if (_y == value)
          {
            return;
          }

          _y = value;
          OnPropertyChanged(nameof(Y));
        }
      }

      public string WidthM { get; set; } = "150";
      public string LengthM { get; set; } = "250";
      public string RotationText
      {
        get => _rotationText;
        set
        {
          if (_rotationText == value)
          {
            return;
          }

          _rotationText = value;
          OnPropertyChanged(nameof(RotationText));
        }
      }

      public double RotationDeg
      {
        get => _rotationDeg;
        set => _rotationDeg = value;
      }

      /// <summary>Display-only: from Excel «Сернистая среда» (Да/Нет).</summary>
      public bool SourMedium { get; set; }
      public bool Liquid { get; set; } = true;
      public bool Oil { get; set; } = true;
      public bool Gas { get; set; } = true;
      public string FirstLaunch { get; set; } = "1";
      public bool CoordsDirty
      {
        get => _coordsDirty;
        set
        {
          if (_coordsDirty == value)
          {
            return;
          }

          _coordsDirty = value;
          OnPropertyChanged(nameof(CoordsDirty));
        }
      }

      [Browsable(false)]
      public PadWorkbookLoader.PadRecord Record { get; set; }

      [Browsable(false)]
      public double CommittedX { get; private set; }

      [Browsable(false)]
      public double CommittedY { get; private set; }

      [Browsable(false)]
      public double CommittedRotationDeg { get; private set; }

      public event PropertyChangedEventHandler PropertyChanged;

      public void CaptureCommittedFromExcel(double x, double y, double rotationDeg)
      {
        CommittedX = x;
        CommittedY = y;
        CommittedRotationDeg = CadPadOverlay.NormalizeDegrees(rotationDeg);
        if (Record != null)
        {
          Record.X = CommittedX;
          Record.Y = CommittedY;
        }

        X = CommittedX.ToString("G17", CultureInfo.CurrentCulture);
        Y = CommittedY.ToString("G17", CultureInfo.CurrentCulture);
        RotationDeg = CommittedRotationDeg;
        RotationText = CommittedRotationDeg.ToString("0.###", CultureInfo.CurrentCulture);
        CoordsDirty = false;
      }

      public void CaptureCommitted()
      {
        CommittedX = Record?.X ?? 0;
        CommittedY = Record?.Y ?? 0;
        CommittedRotationDeg = CadPadOverlay.NormalizeDegrees(RotationDeg);
        CoordsDirty = false;
      }

      public void ApplyPreviewPose(double x, double y, double rotationDeg)
      {
        if (Record != null)
        {
          Record.X = x;
          Record.Y = y;
        }

        X = x.ToString("G17", CultureInfo.CurrentCulture);
        Y = y.ToString("G17", CultureInfo.CurrentCulture);
        RotationDeg = CadPadOverlay.NormalizeDegrees(rotationDeg);
        RotationText = RotationDeg.ToString("0.###", CultureInfo.CurrentCulture);
        CoordsDirty = true;
      }

      public void RestoreCommitted()
      {
        if (Record != null)
        {
          Record.X = CommittedX;
          Record.Y = CommittedY;
        }

        X = CommittedX.ToString("G17", CultureInfo.CurrentCulture);
        Y = CommittedY.ToString("G17", CultureInfo.CurrentCulture);
        RotationDeg = CommittedRotationDeg;
        RotationText = CommittedRotationDeg.ToString("0.###", CultureInfo.CurrentCulture);
        CoordsDirty = false;
      }

      private void OnPropertyChanged(string name)
      {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
      }
    }
  }
}
