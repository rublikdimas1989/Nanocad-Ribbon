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
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  public enum SitePlacementKind
  {
    Buos,
    Nodes
  }

  /// <summary>
  /// Source-data placement tab for БУОС / Узлы: Excel sizes + CAD place/rotate/show
  /// (Inserts UX + KP width/height/angle).
  /// </summary>
  public sealed class SitePlacementWindow : UserControl
  {
    private readonly SitePlacementKind _kind;
    private readonly string _noun;
    private readonly string _sheetName;
    private readonly string _layerName;
    private readonly BindingList<SiteRow> _rows = new BindingList<SiteRow>();
    private readonly TextBox _file;
    private readonly TextBox _log;
    private readonly Label _status;
    private readonly DataGridView _grid;
    private readonly CheckBox _showOnDrawing;
    private readonly Button _saveButton;
    private readonly Button _loadButton;
    private SitePlacementWorkbookLoader.WorkbookData _book;
    private bool _suppressShowToggle;
    private bool _discarding;

    public SitePlacementWindow(SitePlacementKind kind)
    {
      _kind = kind;
      bool buos = kind == SitePlacementKind.Buos;
      _noun = buos ? "БУОС" : "Узел";
      _sheetName = buos
        ? SitePlacementWorkbookLoader.BuosSheetName
        : SitePlacementWorkbookLoader.NodesSheetName;
      _layerName = buos
        ? CadBuosPlacementOverlay.LayerName
        : CadNodePlacementOverlay.LayerName;

      Text = buos ? "Размещение БУОС" : "Размещение узлов";
      Dock = DockStyle.Fill;
      AppPaths paths = AppPaths.Load();
      TracingCache.EnsureCreated();

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 4,
        Padding = new Padding(10)
      };
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
      Controls.Add(root);

      var top = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false
      };
      root.Controls.Add(top, 0, 0);

      top.Controls.Add(new Label
      {
        Text = "Мировые X/Y и угол — MOVE/ROTATE/grip на чертеже, затем «Сохранить положение». "
          + "Ширина/длина — из Excel (лист «" + _sheetName + "»). Имена уникальны. "
          + "Каталог размеров/стоимостей — вкладка «" + (buos ? "БУОС" : "Узлы") + "» в «Настройки».",
        AutoSize = true,
        MaximumSize = new Size(1120, 0),
        Margin = new Padding(3, 0, 3, 8)
      });

      var fileRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
      top.Controls.Add(fileRow);
      fileRow.Controls.Add(new Label
      {
        Text = buos ? "Таблицы размещения БУОС (xlsx):" : "Таблицы размещения узлов (xlsx):",
        AutoSize = true,
        Margin = new Padding(3, 8, 0, 0)
      });
      _file = new TextBox { Width = 480, ReadOnly = true, Margin = new Padding(3) };
      fileRow.Controls.Add(_file);
      string initialPath = buos ? paths.BuosPlacementWorkbook : paths.NodesPlacementWorkbook;
      PathTextDisplay.SetPath(_file, initialPath);
      PathTextDisplay.HookShowEndOnVisible(_file, this);
      var browse = new Button { Text = "Обзор…", AutoSize = true, Margin = new Padding(3) };
      browse.Click += (_, __) => Guard(BrowseWorkbook);
      fileRow.Controls.Add(browse);

      var cadRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
      top.Controls.Add(cadRow);
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
          PersistShowFlag(_showOnDrawing.Checked);
          if (_showOnDrawing.Checked)
          {
            RefreshDrawing(force: true);
          }
          else
          {
            ClearOverlay();
            Log(_noun + " на чертеже скрыты (слой " + _layerName + ").");
          }
        }
        catch (Exception ex)
        {
          MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          _suppressShowToggle = true;
          try { _showOnDrawing.Checked = false; }
          finally { _suppressShowToggle = false; }
        }
      };
      cadRow.Controls.Add(_showOnDrawing);

      _saveButton = MakeButton(cadRow, "Сохранить положение", () => SaveCoordinates(confirmOverwrite: true));
      _saveButton.Enabled = false;
      _loadButton = MakeButton(cadRow, "Загрузить из исходных", () => ReloadFromSource(confirm: true));
      _loadButton.Enabled = false;

      _grid = new DataGridView
      {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        ReadOnly = true,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
      };
      _grid.Columns.Add(TextCol("Имя", nameof(SiteRow.Name), 120));
      _grid.Columns.Add(TextCol("X, м", nameof(SiteRow.X), 100));
      _grid.Columns.Add(TextCol("Y, м", nameof(SiteRow.Y), 100));
      _grid.Columns.Add(TextCol("Угол °", nameof(SiteRow.RotationText), 70));
      _grid.Columns.Add(TextCol("Ширина, м", nameof(SiteRow.WidthM), 90));
      _grid.Columns.Add(TextCol("Длина, м", nameof(SiteRow.LengthM), 90));
      _grid.SelectionChanged += (_, __) => UpdateButtons();
      _grid.CellFormatting += (_, e) =>
      {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count)
        {
          return;
        }

        _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor =
          _rows[e.RowIndex].CoordsDirty ? Color.Yellow : _grid.DefaultCellStyle.BackColor;
      };
      root.Controls.Add(_grid, 0, 1);

      _log = new TextBox
      {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
      };
      root.Controls.Add(_log, 0, 2);

      _status = new Label
      {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.DimGray,
        Text = "Откройте xlsx размещения («" + _sheetName + "»)."
      };
      root.Controls.Add(_status, 0, 3);

      BindGrid();

      if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
      {
        try { LoadWorkbook(initialPath, remember: false); }
        catch (Exception ex) { Log("Не удалось загрузить: " + ex.Message); }
      }
      else
      {
        Log("Откройте xlsx с листом «" + _sheetName + "» через «Обзор…».");
      }

      // Checkbox restore only — opening SourceData must not mutate the drawing.
      // Placement is via ribbon «Добавить БУОС» / «Добавить Узел».
      bool show = buos ? paths.ShowBuosOnDrawing : paths.ShowNodesOnDrawing;
      if (show)
      {
        _suppressShowToggle = true;
        try { _showOnDrawing.Checked = true; }
        finally { _suppressShowToggle = false; }
        Log("«Показывать на чертеже» восстановлен без перерисовки. Переключите чекбокс, чтобы обновить слой.");
      }

      UpdateButtons();
    }

    /// <summary>
    /// Ribbon «Добавить БУОС» / similar: select first row if needed, insert at stored pose.
    /// </summary>
    public void BeginPlaceSelected()
    {
      if (_rows.Count == 0)
      {
        throw new InvalidOperationException(
          "Нет строк размещения. Откройте xlsx с листом «" + _sheetName + "».");
      }

      if (SelectedRow()?.Record == null)
      {
        _grid.ClearSelection();
        _grid.Rows[0].Selected = true;
        _grid.CurrentCell = _grid.Rows[0].Cells[0];
      }

      PlaceSelected();
    }

    public bool TrySelectByName(string name)
    {
      if (string.IsNullOrWhiteSpace(name) || _rows.Count == 0)
      {
        return false;
      }

      for (int i = 0; i < _rows.Count; i++)
      {
        if (!string.Equals(_rows[i].Name, name, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        _grid.ClearSelection();
        if (i < _grid.Rows.Count)
        {
          _grid.Rows[i].Selected = true;
          _grid.CurrentCell = _grid.Rows[i].Cells[0];
          try { _grid.FirstDisplayedScrollingRowIndex = i; }
          catch { }
        }

        return true;
      }

      return false;
    }

    public void NotifyHostClosing()
    {
      try { PersistShowFlag(_showOnDrawing.Checked); }
      catch { }
      // Dirty prompt is handled once by SourceDataWindow / drawing-close dialog.
    }

    public bool HasDirtyCoords => _rows.Any(r => r.CoordsDirty);

    public IEnumerable<DirtyCoordItem> EnumerateDirtyItems()
    {
      string group = _kind == SitePlacementKind.Buos ? "БУОС" : "Узлы";
      foreach (SiteRow row in _rows.Where(r => r.CoordsDirty))
      {
        yield return new DirtyCoordItem
        {
          Group = group,
          Name = row.Name,
          Detail = "X=" + row.X + "; Y=" + row.Y + "; угол=" + row.RotationText + "°"
        };
      }
    }

    public void SaveDirtyCoords(bool confirm)
    {
      if (!HasDirtyCoords)
      {
        return;
      }

      SaveCoordinates(confirmOverwrite: confirm);
    }

    public void ReloadFromSource(bool confirm)
    {
      string path = _book?.Path;
      if (string.IsNullOrWhiteSpace(path))
      {
        AppPaths paths = AppPaths.Load();
        path = _kind == SitePlacementKind.Buos ? paths.BuosPlacementWorkbook : paths.NodesPlacementWorkbook;
      }

      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException("Нет файла размещения «" + _sheetName + "» для загрузки из исходных.");
      }

      if (confirm)
      {
        DialogResult ok = MessageBox.Show(
          FindForm() ?? (IWin32Window)this,
          "Загрузить положения «" + _sheetName + "» из исходного Excel и сбросить изменения на чертеже?\n\n" + path,
          "Загрузить из исходных",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }
      }

      LoadWorkbook(path, remember: false);
      Log(_noun + " загружены из исходных.");
    }

    public void DiscardDirtyCoords()
    {
      DiscardUnsaved();
    }

    private void PersistShowFlag(bool show)
    {
      if (_kind == SitePlacementKind.Buos)
      {
        AppPaths.Set(p => p.ShowBuosOnDrawing = show);
      }
      else
      {
        AppPaths.Set(p => p.ShowNodesOnDrawing = show);
      }
    }

    private void RememberWorkbook(string path)
    {
      if (_kind == SitePlacementKind.Buos)
      {
        AppPaths.Set(p => p.BuosPlacementWorkbook = path);
      }
      else
      {
        AppPaths.Set(p => p.NodesPlacementWorkbook = path);
      }
    }

    private void ClearOverlay()
    {
      if (_kind == SitePlacementKind.Buos)
      {
        CadBuosPlacementOverlay.Clear();
      }
      else
      {
        CadNodePlacementOverlay.Clear();
      }
    }

    private void UpdateOverlay(SiteRow row)
    {
      if (row == null)
      {
        return;
      }

      if (_kind == SitePlacementKind.Buos)
      {
        CadBuosPlacementOverlay.UpdatePad(ToBuosGeom(row));
      }
      else
      {
        CadNodePlacementOverlay.UpdatePad(ToNodeGeom(row));
      }
    }

    private int ShowAllOverlay()
    {
      if (_kind == SitePlacementKind.Buos)
      {
        return CadBuosPlacementOverlay.ShowPads(_rows.Select(ToBuosGeom));
      }

      return CadNodePlacementOverlay.ShowPads(_rows.Select(ToNodeGeom));
    }

    private static CadBuosPlacementOverlay.SiteGeom ToBuosGeom(SiteRow row)
    {
      return new CadBuosPlacementOverlay.SiteGeom
      {
        Name = row.Name,
        X = row.X,
        Y = row.Y,
        WidthM = row.WidthM,
        LengthM = row.LengthM,
        RotationDeg = row.RotationDeg
      };
    }

    private static CadNodePlacementOverlay.SiteGeom ToNodeGeom(SiteRow row)
    {
      return new CadNodePlacementOverlay.SiteGeom
      {
        Name = row.Name,
        X = row.X,
        Y = row.Y,
        WidthM = row.WidthM,
        LengthM = row.LengthM,
        RotationDeg = row.RotationDeg
      };
    }

    private Button MakeButton(Control parent, string text, Action action)
    {
      var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
      button.Click += (_, __) =>
      {
        try { action(); }
        catch (Exception ex)
        {
          MessageBox.Show(button.FindForm(), ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          Log(ex.Message);
        }
      };
      parent.Controls.Add(button);
      return button;
    }

    private void Guard(Action action)
    {
      try { action(); }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Log(ex.Message);
      }
    }

    private void BrowseWorkbook()
    {
      using var dlg = new OpenFileDialog
      {
        Filter = "Excel (*.xlsx)|*.xlsx|Все файлы|*.*",
        Title = "Таблицы размещения «" + _sheetName + "»"
      };
      if (dlg.ShowDialog(this) != DialogResult.OK)
      {
        return;
      }

      LoadWorkbook(dlg.FileName, remember: true);
    }

    private void LoadWorkbook(string path, bool remember)
    {
      _book = SitePlacementWorkbookLoader.LoadWorkbook(path, _sheetName);
      _rows.Clear();
      foreach (SitePlacementWorkbookLoader.PlacementRecord rec in _book.Sites)
      {
        _rows.Add(SiteRow.FromRecord(rec));
      }

      PathTextDisplay.SetPath(_file, path);
      if (remember)
      {
        RememberWorkbook(path);
      }

      BindGrid();
      Log("Загружено: " + _rows.Count + " («" + _book.SheetName + "»).");
      _status.Text = "Лист «" + _book.SheetName + "»: " + _rows.Count + " строк.";
      if (_showOnDrawing.Checked)
      {
        RefreshDrawing(force: true);
      }

      UpdateButtons();
    }

    private void PlaceSelected()
    {
      SiteRow row = SelectedRow();
      if (row?.Record == null)
      {
        throw new InvalidOperationException("Выберите строку в таблице.");
      }

      RunWithCadFocus(() =>
      {
        if (_kind == SitePlacementKind.Buos)
        {
          var geom = ToBuosGeom(row);
          CadBuosPlacementOverlay.InsertAtStored(geom);
          CadBuosPlacementOverlay.ZoomToPads(new[] { geom });
        }
        else
        {
          var geom = ToNodeGeom(row);
          CadNodePlacementOverlay.InsertAtStored(geom);
          CadNodePlacementOverlay.ZoomToPads(new[] { geom });
        }

        if (!_showOnDrawing.Checked)
        {
          _suppressShowToggle = true;
          try { _showOnDrawing.Checked = true; }
          finally { _suppressShowToggle = false; }
          PersistShowFlag(true);
        }

        Log("Вставлено «" + row.Name + "» из базы: X=" + row.X + "; Y=" + row.Y
          + "; угол=" + row.RotationText + "°");
      });
      RefreshRow(row);
      UpdateButtons();
    }

    private void ManualRotateSelected()
    {
      SiteRow row = SelectedRow();
      if (row?.Record == null)
      {
        throw new InvalidOperationException("Выберите строку в таблице.");
      }

      if (!_showOnDrawing.Checked)
      {
        _showOnDrawing.Checked = true;
      }
      else
      {
        UpdateOverlay(row);
      }

      RunWithCadFocus(() =>
      {
        bool ok;
        double x, y, rot;
        bool poseUpdated;
        if (_kind == SitePlacementKind.Buos)
        {
          ok = CadBuosPlacementOverlay.TryManualRotate(row.Name, out x, out y, out rot, out poseUpdated);
          if (ok && poseUpdated)
          {
            row.ApplyPreviewPose(x, y, CadBuosPlacementOverlay.NormalizeDegrees(rot));
          }
        }
        else
        {
          ok = CadNodePlacementOverlay.TryManualRotate(row.Name, out x, out y, out rot, out poseUpdated);
          if (ok && poseUpdated)
          {
            row.ApplyPreviewPose(x, y, CadNodePlacementOverlay.NormalizeDegrees(rot));
          }
        }

        if (!ok)
        {
          Log("Поворот «" + row.Name + "» отменён.");
          return;
        }

        if (poseUpdated)
        {
          Log("Поворот вручную «" + row.Name + "»: X=" + row.X + "; Y=" + row.Y
            + "; угол=" + row.RotationText + "°");
        }
        else
        {
          Log("Поворот «" + row.Name + "» выполнен; позу с чертежа прочитать не удалось.");
        }
      });
      RefreshRow(row);
      UpdateButtons();
    }

    private void SaveCoordinates(bool confirmOverwrite)
    {
      if (_book == null || string.IsNullOrWhiteSpace(_book.Path))
      {
        throw new InvalidOperationException("Нет открытого файла размещения.");
      }

      List<SitePlacementWorkbookLoader.PlacementRecord> dirty = _rows
        .Where(r => r.CoordsDirty && r.Record != null)
        .Select(r => r.Record)
        .ToList();
      if (dirty.Count == 0)
      {
        throw new InvalidOperationException("Нет изменённых координат.");
      }

      if (confirmOverwrite)
      {
        DialogResult confirm = MessageBox.Show(
          this,
          "Перезаписать положения (X/Y/угол) в файле?\n\n" + _book.Path + "\n\nСтрок: " + dirty.Count,
          "Сохранить положения",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
          return;
        }
      }

      int n = SitePlacementWorkbookLoader.SavePlacementPose(_book.Path, _sheetName, dirty);
      foreach (SiteRow row in _rows.Where(r => r.CoordsDirty))
      {
        row.CaptureCommitted();
      }

      BindGrid();
      UpdateButtons();
      Log("Сохранено положений: " + n + " → " + _book.Path);
      _status.Text = "Сохранено: " + n + " строк.";
    }

    private void DiscardUnsaved()
    {
      _discarding = true;
      try
      {
        foreach (SiteRow row in _rows.Where(r => r.CoordsDirty))
        {
          row.RestoreCommitted();
        }

        BindGrid();
        if (_showOnDrawing.Checked)
        {
          RefreshDrawing(force: true);
        }
        else
        {
          ClearOverlay();
        }

        UpdateButtons();
        Log("Несохранённые координаты отменены (чертёж и таблица восстановлены).");
      }
      finally
      {
        _discarding = false;
      }
    }

    private void RefreshDrawing(bool force)
    {
      if (!force && !_showOnDrawing.Checked)
      {
        return;
      }

      int n = ShowAllOverlay();
      Log("На чертеже: " + n + " → слой " + _layerName + ".");
    }

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

        try { System.Windows.Forms.Application.DoEvents(); }
        catch { }

        cadWork?.Invoke();
      }
      catch (Exception ex)
      {
        Log(ex.GetType().Name + ": " + ex.Message);
        try
        {
          MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
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

            BindGrid(SelectedRow());
          }
        }
        catch (Exception ex)
        {
          Log("UI restore: " + ex.Message);
        }
      }
    }

    private void UpdateButtons()
    {
      _saveButton.Enabled = _rows.Any(r => r.CoordsDirty);
      string remembered = _kind == SitePlacementKind.Buos
        ? AppPaths.Load().BuosPlacementWorkbook
        : AppPaths.Load().NodesPlacementWorkbook;
      _loadButton.Enabled = (_book != null && !string.IsNullOrWhiteSpace(_book.Path))
        || !string.IsNullOrWhiteSpace(remembered);
    }

    private SiteRow SelectedRow()
    {
      if (_grid.CurrentRow == null || _grid.CurrentRow.Index < 0 || _grid.CurrentRow.Index >= _rows.Count)
      {
        return null;
      }

      return _rows[_grid.CurrentRow.Index];
    }

    private void RefreshRow(SiteRow row)
    {
      if (row == null)
      {
        BindGrid();
        return;
      }

      int index = _rows.IndexOf(row);
      if (index >= 0)
      {
        _rows.ResetItem(index);
      }

      BindGrid(row);
    }

    private void BindGrid(SiteRow preferSelect = null)
    {
      string keep = preferSelect?.Name ?? SelectedRow()?.Name;
      _grid.DataSource = null;
      _grid.DataSource = _rows;
      if (!string.IsNullOrEmpty(keep))
      {
        for (int i = 0; i < _rows.Count; i++)
        {
          if (string.Equals(_rows[i].Name, keep, StringComparison.OrdinalIgnoreCase))
          {
            _grid.ClearSelection();
            _grid.Rows[i].Selected = true;
            _grid.CurrentCell = _grid.Rows[i].Cells[0];
            break;
          }
        }
      }
    }

    private void Log(string text)
    {
      if (_discarding)
      {
        // still append
      }

      string line = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture) + "  " + text;
      if (_log.TextLength == 0)
      {
        _log.Text = line;
      }
      else
      {
        _log.AppendText(Environment.NewLine + line);
      }
    }

    private static DataGridViewTextBoxColumn TextCol(string header, string prop, int width)
    {
      return new DataGridViewTextBoxColumn
      {
        HeaderText = header,
        DataPropertyName = prop,
        FillWeight = width,
        MinimumWidth = 60,
        ReadOnly = true
      };
    }

    private sealed class SiteRow : INotifyPropertyChanged
    {
      public SitePlacementWorkbookLoader.PlacementRecord Record { get; private set; }

      public string Name => Record?.Name ?? string.Empty;

      public double X { get; private set; }

      public double Y { get; private set; }

      public double RotationDeg { get; private set; }

      public string RotationText => RotationDeg.ToString("0.###", CultureInfo.CurrentCulture);

      public double WidthM => Record?.WidthM ?? 0;

      public double LengthM => Record?.LengthM ?? 0;

      public bool CoordsDirty { get; private set; }

      private double _committedX;
      private double _committedY;
      private double _committedRot;

      public static SiteRow FromRecord(SitePlacementWorkbookLoader.PlacementRecord rec)
      {
        var row = new SiteRow { Record = rec };
        row.X = rec.X;
        row.Y = rec.Y;
        row.RotationDeg = rec.AngleDeg;
        row.CaptureCommitted();
        return row;
      }

      public void ApplyPreviewPose(double x, double y, double rotationDeg)
      {
        X = x;
        Y = y;
        RotationDeg = rotationDeg;
        if (Record != null)
        {
          Record.X = x;
          Record.Y = y;
          Record.AngleDeg = rotationDeg;
        }

        CoordsDirty = !Near(_committedX, X) || !Near(_committedY, Y) || !Near(_committedRot, RotationDeg);
        RaiseAll();
      }

      public void CaptureCommitted()
      {
        _committedX = X;
        _committedY = Y;
        _committedRot = RotationDeg;
        CoordsDirty = false;
        RaiseAll();
      }

      public void RestoreCommitted()
      {
        ApplyPreviewPose(_committedX, _committedY, _committedRot);
        CoordsDirty = false;
        RaiseAll();
      }

      private static bool Near(double a, double b)
      {
        return Math.Abs(a - b) < 1e-9;
      }

      public event PropertyChangedEventHandler PropertyChanged;

      private void RaiseAll()
      {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
      }
    }
  }
}
