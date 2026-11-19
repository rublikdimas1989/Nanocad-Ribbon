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

  /// <summary>
  /// End TopologyTieInsWindow analogue (WinForms): display-only grid, CAD place for X/Y.
  /// </summary>
  public sealed class InsertPointsWindow : UserControl
  {
    private readonly BindingList<InsertRow> _rows = new BindingList<InsertRow>();
    private readonly TextBox _file;
    private readonly TextBox _log;
    private readonly Label _status;
    private readonly DataGridView _grid;
    private readonly CheckBox _showOnDrawing;
    private readonly Button _placeButton;
    private readonly Button _saveButton;
    private InsertWorkbookLoader.WorkbookData _book;
    private bool _suppressShowToggle;
    private bool _discarding;

    public InsertPointsWindow()
    {
      Text = "Точки врезки";
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
        Text = "Мировые X/Y в метрах. Положение — только «Задать положение вручную» на чертеже. "
          + "Поля среды / давления / лимита — из Excel (лист «Врезки»). Макс. приём нефти — т/сут; пусто = без лимита.",
        AutoSize = true,
        MaximumSize = new Size(1120, 0),
        Margin = new Padding(3, 0, 3, 8)
      });

      var fileRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
      top.Controls.Add(fileRow);
      fileRow.Controls.Add(new Label { Text = "Таблицы врезок (xlsx):", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
      _file = new TextBox { Width = 480, ReadOnly = true, Margin = new Padding(3), Text = paths.InsertsWorkbook ?? string.Empty };
      fileRow.Controls.Add(_file);
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
          AppPaths.Set(p => p.ShowInsertsOnDrawing = _showOnDrawing.Checked);
          if (_showOnDrawing.Checked)
          {
            RefreshDrawing(force: true);
          }
          else
          {
            CadInsertOverlay.Clear();
            Log("Врезки на чертеже скрыты (слой " + CadInsertOverlay.LayerName + ").");
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

      _placeButton = MakeButton(cadRow, "Задать положение вручную", PlaceSelected);
      _placeButton.Enabled = false;
      _saveButton = MakeButton(cadRow, "Сохранить положения", SaveCoordinates);
      _saveButton.Enabled = false;

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
      _grid.Columns.Add(CheckCol("Включена", nameof(InsertRow.Enabled), 70));
      _grid.Columns.Add(TextCol("Имя", nameof(InsertRow.Name), 120));
      _grid.Columns.Add(TextCol("X, м", nameof(InsertRow.X), 110));
      _grid.Columns.Add(TextCol("Y, м", nameof(InsertRow.Y), 110));
      _grid.Columns.Add(TextCol("Среда", nameof(InsertRow.FluidText), 90));
      _grid.Columns.Add(TextCol("P противодавления,\nбар (изб.)", nameof(InsertRow.BackPressureText), 120));
      _grid.Columns.Add(TextCol("Макс. приём нефти,\nт/сут", nameof(InsertRow.MaxOilText), 130));
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
        Text = "Откройте таблицы врезок (xlsx)."
      };
      root.Controls.Add(_status, 0, 3);

      BindGrid();

      if (!string.IsNullOrWhiteSpace(paths.InsertsWorkbook) && File.Exists(paths.InsertsWorkbook))
      {
        try { LoadWorkbook(paths.InsertsWorkbook, remember: false); }
        catch (Exception ex) { Log("Не удалось загрузить прошлые врезки: " + ex.Message); }
      }
      else
      {
        Log("Откройте xlsx с листом «Врезки» через «Обзор…».");
      }

      if (paths.ShowInsertsOnDrawing)
      {
        _suppressShowToggle = true;
        try { _showOnDrawing.Checked = true; }
        finally { _suppressShowToggle = false; }
        try { RefreshDrawing(force: true); }
        catch (Exception ex) { Log("Показ на чертеже: " + ex.Message); }
      }

      UpdateButtons();
    }

    private static Button MakeButton(Control parent, string text, Action action)
    {
      var button = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
      button.Click += (_, __) =>
      {
        try { action(); }
        catch (Exception ex)
        {
          MessageBox.Show(button.FindForm(), ex.Message, "Точки врезки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        MessageBox.Show(this, ex.Message, "Точки врезки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Log(ex.Message);
      }
    }

    public void NotifyHostClosing()
    {
      try { AppPaths.Set(p => p.ShowInsertsOnDrawing = _showOnDrawing.Checked); }
      catch { }

      try { DiscardUnsaved(); }
      catch (Exception ex) { Log("Отмена несохранённых положений: " + ex.Message); }
    }

    private void DiscardUnsaved()
    {
      if (_discarding)
      {
        return;
      }

      _discarding = true;
      try
      {
        List<InsertRow> dirty = _rows.Where(r => r.CoordsDirty).ToList();
        if (dirty.Count == 0)
        {
          return;
        }

        foreach (InsertRow row in dirty)
        {
          row.RestoreCommitted();
        }

        BindGrid();
        CadInsertOverlay.ShowInserts(_rows.Select(ToGeom));
        if (!_showOnDrawing.Checked)
        {
          CadInsertOverlay.Clear();
        }

        Log("Несохранённые положения врезок отменены («" + dirty.Count + "» → Excel).");
      }
      finally
      {
        _discarding = false;
      }
    }

    private void UpdateButtons()
    {
      InsertRow row = Selected();
      _placeButton.Enabled = row?.Record != null;
      _saveButton.Enabled = _rows.Any(r => r.CoordsDirty);
    }

    private InsertRow Selected()
    {
      if (_grid.CurrentRow == null || _grid.CurrentRow.Index < 0 || _grid.CurrentRow.Index >= _rows.Count)
      {
        return null;
      }

      return _rows[_grid.CurrentRow.Index];
    }

    private void PlaceSelected()
    {
      InsertRow row = Selected() ?? throw new InvalidOperationException("Выберите строку точки врезки.");
      var geom = ToGeom(row);
      RunWithCadFocus(() =>
      {
        if (!CadInsertOverlay.TryPlace(geom, out double x, out double y))
        {
          Log("Размещение «" + row.Name + "» отменено.");
          return;
        }

        row.ApplyPreviewPose(x, y);
        if (!_showOnDrawing.Checked)
        {
          _showOnDrawing.Checked = true;
        }
        else
        {
          CadInsertOverlay.UpdateInsert(ToGeom(row));
        }

        Log("Новые координаты «" + row.Name + "»: X=" + row.X + "; Y=" + row.Y);
      });
      RefreshRow(row);
      UpdateButtons();
    }

    private void RunWithCadFocus(Action cadWork)
    {
      bool wasEnabled = Enabled;
      try
      {
        Enabled = false;
        Hide();
        try { Application.DoEvents(); } catch { }
        cadWork?.Invoke();
      }
      catch (Exception ex)
      {
        Log(ex.GetType().Name + ": " + ex.Message);
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
      finally
      {
        if (!IsDisposed)
        {
          Show();
          Enabled = wasEnabled;
          Activate();
          BringToFront();
          BindGrid(Selected());
        }
      }
    }

    private void RefreshRow(InsertRow row)
    {
      int index = row == null ? -1 : _rows.IndexOf(row);
      if (index >= 0)
      {
        _rows.ResetItem(index);
      }

      BindGrid(row);
    }

    private void BindGrid(InsertRow prefer = null)
    {
      string keep = prefer?.Name ?? Selected()?.Name;
      _grid.DataSource = null;
      _grid.DataSource = _rows;
      _status.Text = "Сетка врезок: rows=" + _rows.Count + ", grid=" + _grid.Rows.Count;
      if (!string.IsNullOrEmpty(keep))
      {
        for (int i = 0; i < _rows.Count; i++)
        {
          if (string.Equals(_rows[i].Name, keep, StringComparison.OrdinalIgnoreCase))
          {
            _grid.ClearSelection();
            _grid.Rows[i].Selected = true;
            _grid.CurrentCell = _grid.Rows[i].Cells[1];
            break;
          }
        }
      }
    }

    private void SaveCoordinates()
    {
      if (_book == null || string.IsNullOrWhiteSpace(_book.Path))
      {
        throw new InvalidOperationException("Нет открытого файла врезок.");
      }

      List<InsertWorkbookLoader.InsertRecord> dirty = _rows
        .Where(r => r.CoordsDirty && r.Record != null)
        .Select(r => r.Record)
        .ToList();
      if (dirty.Count == 0)
      {
        throw new InvalidOperationException("Нет изменённых координат.");
      }

      if (MessageBox.Show(this,
        "Перезаписать положения (X/Y) в файле?\n\n" + _book.Path + "\n\nСтрок: " + dirty.Count,
        "Сохранить положения", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
      {
        return;
      }

      int updated = InsertWorkbookLoader.SaveInsertCoordinates(_book.Path, dirty);
      foreach (InsertRow row in _rows.Where(r => r.CoordsDirty))
      {
        row.CaptureCommitted();
      }

      BindGrid();
      UpdateButtons();
      Log("Сохранено координат врезок в Excel: " + updated);
    }

    private void RefreshDrawing(bool force = false)
    {
      if (!force && !_showOnDrawing.Checked)
      {
        return;
      }

      int drawn = CadInsertOverlay.ShowInserts(_rows.Select(ToGeom));
      Log("На чертеже обновлено врезок: " + drawn + " (слой " + CadInsertOverlay.LayerName + ").");
    }

    private static CadInsertOverlay.InsertGeom ToGeom(InsertRow row)
    {
      return new CadInsertOverlay.InsertGeom
      {
        Name = row.Name,
        X = row.Record?.X ?? 0,
        Y = row.Record?.Y ?? 0,
        Enabled = row.Enabled
      };
    }

    private void BrowseWorkbook()
    {
      using var dialog = new OpenFileDialog
      {
        Title = "Таблицы врезок (xlsx)",
        Filter = "Excel (*.xlsx)|*.xlsx",
        CheckFileExists = true
      };
      if (!string.IsNullOrWhiteSpace(_file.Text) && File.Exists(_file.Text))
      {
        dialog.InitialDirectory = Path.GetDirectoryName(_file.Text);
        dialog.FileName = Path.GetFileName(_file.Text);
      }

      if (dialog.ShowDialog(this) == DialogResult.OK)
      {
        LoadWorkbook(dialog.FileName, remember: true);
      }
    }

    private void LoadWorkbook(string path, bool remember)
    {
      InsertWorkbookLoader.WorkbookData book = InsertWorkbookLoader.LoadWorkbook(path);
      _book = book;
      _rows.Clear();
      foreach (InsertWorkbookLoader.InsertRecord rec in book.Inserts)
      {
        var row = new InsertRow
        {
          Name = rec.Name,
          Enabled = rec.Enabled,
          FluidText = InsertWorkbookLoader.FluidDisplayName(rec.Fluid),
          BackPressureText = rec.BackPressureBar.ToString("0.###", CultureInfo.CurrentCulture),
          MaxOilText = rec.MaxOilRateTDay.HasValue
            ? rec.MaxOilRateTDay.Value.ToString("0.###", CultureInfo.CurrentCulture)
            : string.Empty,
          Record = rec
        };
        row.CaptureCommittedFromExcel(rec.X, rec.Y);
        _rows.Add(row);
      }

      _file.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.InsertsWorkbook = path);
      }

      Log("Загружено врезок: " + book.Inserts.Count + " (" + Path.GetFileName(path) + ")");
      BindGrid();
      UpdateButtons();
      if (_showOnDrawing.Checked)
      {
        RefreshDrawing(force: true);
      }
    }

    private void Log(string text)
    {
      _log.AppendText(text + Environment.NewLine);
      _log.SelectionStart = _log.TextLength;
      _log.ScrollToCaret();
      _status.Text = text;
    }

    private static DataGridViewTextBoxColumn TextCol(string header, string prop, int fill)
    {
      return new DataGridViewTextBoxColumn
      {
        HeaderText = header,
        DataPropertyName = prop,
        Name = prop,
        ReadOnly = true,
        FillWeight = fill
      };
    }

    private static DataGridViewCheckBoxColumn CheckCol(string header, string prop, int fill)
    {
      return new DataGridViewCheckBoxColumn
      {
        HeaderText = header,
        DataPropertyName = prop,
        Name = prop,
        ReadOnly = true,
        FillWeight = fill
      };
    }

    public sealed class InsertRow : INotifyPropertyChanged
    {
      private string _x = "0";
      private string _y = "0";
      private bool _dirty;

      public string Name { get; set; } = string.Empty;
      public bool Enabled { get; set; } = true;
      public string X
      {
        get => _x;
        set { if (_x != value) { _x = value; OnPropertyChanged(nameof(X)); } }
      }

      public string Y
      {
        get => _y;
        set { if (_y != value) { _y = value; OnPropertyChanged(nameof(Y)); } }
      }

      public string FluidText { get; set; } = "Нефть";
      public string BackPressureText { get; set; } = "0";
      public string MaxOilText { get; set; } = string.Empty;
      public bool CoordsDirty
      {
        get => _dirty;
        set { if (_dirty != value) { _dirty = value; OnPropertyChanged(nameof(CoordsDirty)); } }
      }

      [Browsable(false)]
      public InsertWorkbookLoader.InsertRecord Record { get; set; }

      [Browsable(false)]
      public double CommittedX { get; private set; }

      [Browsable(false)]
      public double CommittedY { get; private set; }

      public event PropertyChangedEventHandler PropertyChanged;

      public void CaptureCommittedFromExcel(double x, double y)
      {
        CommittedX = x;
        CommittedY = y;
        if (Record != null)
        {
          Record.X = x;
          Record.Y = y;
        }

        X = x.ToString("G17", CultureInfo.CurrentCulture);
        Y = y.ToString("G17", CultureInfo.CurrentCulture);
        CoordsDirty = false;
      }

      public void CaptureCommitted()
      {
        CommittedX = Record?.X ?? 0;
        CommittedY = Record?.Y ?? 0;
        CoordsDirty = false;
      }

      public void ApplyPreviewPose(double x, double y)
      {
        if (Record != null)
        {
          Record.X = x;
          Record.Y = y;
        }

        X = x.ToString("G17", CultureInfo.CurrentCulture);
        Y = y.ToString("G17", CultureInfo.CurrentCulture);
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
        CoordsDirty = false;
      }

      private void OnPropertyChanged(string name)
      {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
      }
    }
  }
}
