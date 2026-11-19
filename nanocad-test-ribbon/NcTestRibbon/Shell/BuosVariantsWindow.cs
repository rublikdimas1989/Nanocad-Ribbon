namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Windows.Forms;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// End BuosCatalogPanel / TopologyBuosType catalogue — display-only Excel grid (no site X/Y).
  /// Cost from Price.xlsx sheet «Элементы топологии» by BUOS name.
  /// </summary>
  public sealed class BuosVariantsWindow : Form
  {
    private readonly BindingList<BuosRow> _rows = new BindingList<BuosRow>();
    private readonly TextBox _file;
    private readonly TextBox _log;
    private readonly Label _status;
    private readonly DataGridView _grid;
    private Dictionary<string, double> _prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private string _priceFile = string.Empty;

    public BuosVariantsWindow()
    {
      Text = "Варианты БУОС";
      Width = 1000;
      Height = 520;
      MinimumSize = new Size(720, 360);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = true;

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
        Text = "Каталог типов БУОС: производительность и габарит. Координаты и «в подборе» здесь не задаются. "
          + "Стоимость — из Price.xlsx, лист «Элементы топологии» (имя в A, цена в B).",
        AutoSize = true,
        MaximumSize = new Size(960, 0),
        Margin = new Padding(3, 0, 3, 8)
      });

      var fileRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
      top.Controls.Add(fileRow);
      fileRow.Controls.Add(new Label { Text = "Таблицы БУОС (xlsx):", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
      _file = new TextBox
      {
        Width = 480,
        ReadOnly = true,
        Margin = new Padding(3),
        Text = paths.BuosWorkbook ?? string.Empty
      };
      fileRow.Controls.Add(_file);
      var browse = new Button { Text = "Обзор…", AutoSize = true, Margin = new Padding(3) };
      browse.Click += (_, __) =>
      {
        try { BrowseWorkbook(); }
        catch (Exception ex)
        {
          MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          Log(ex.Message);
        }
      };
      fileRow.Controls.Add(browse);
      var refreshPrices = new Button { Text = "Обновить цены", AutoSize = true, Margin = new Padding(3) };
      refreshPrices.Click += (_, __) =>
      {
        try
        {
          ReloadPrices();
          ApplyPricesToRows();
          BindGrid();
          LogPriceStatus();
        }
        catch (Exception ex)
        {
          MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
          Log(ex.Message);
        }
      };
      fileRow.Controls.Add(refreshPrices);

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
      _grid.Columns.Add(TextCol("Имя типа БУОС", nameof(BuosRow.Name), 140));
      _grid.Columns.Add(TextCol("Жидкость\nм³/сут", nameof(BuosRow.CombinedText), 100));
      _grid.Columns.Add(TextCol("ПНГ\nст. м³/сут", nameof(BuosRow.GasText), 110));
      _grid.Columns.Add(TextCol("Длина\nм", nameof(BuosRow.LengthText), 70));
      _grid.Columns.Add(TextCol("Ширина\nм", nameof(BuosRow.WidthText), 70));
      _grid.Columns.Add(TextCol("Стоимость\nруб.", nameof(BuosRow.CostText), 160));
      _grid.CellFormatting += OnCellFormatting;
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
        Text = "Откройте xlsx с листом «БУОС»."
      };
      root.Controls.Add(_status, 0, 3);

      ReloadPrices();
      BindGrid();
      if (!string.IsNullOrWhiteSpace(paths.BuosWorkbook) && File.Exists(paths.BuosWorkbook))
      {
        try { LoadWorkbook(paths.BuosWorkbook, remember: false); }
        catch (Exception ex) { Log("Не удалось загрузить прошлые варианты БУОС: " + ex.Message); }
      }
      else
      {
        Log("Откройте xlsx с листом «БУОС» / «Варианты БУОС» через «Обзор…».");
        LogPriceStatus();
      }
    }

    private void BrowseWorkbook()
    {
      using var dialog = new OpenFileDialog
      {
        Title = "Варианты БУОС (xlsx)",
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
      BuosWorkbookLoader.WorkbookData book = BuosWorkbookLoader.LoadWorkbook(path);
      ReloadPrices();
      _rows.Clear();
      foreach (BuosWorkbookLoader.BuosVariant v in book.Variants)
      {
        var row = new BuosRow
        {
          Name = v.Name,
          CombinedText = v.CombinedCapacity.ToString("0.###", CultureInfo.CurrentCulture),
          GasText = v.GasCapacity.ToString("0.###", CultureInfo.CurrentCulture),
          LengthText = v.LengthM.ToString("0.###", CultureInfo.CurrentCulture),
          WidthText = v.WidthM.ToString("0.###", CultureInfo.CurrentCulture)
        };
        ApplyPrice(row);
        _rows.Add(row);
      }

      _file.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.BuosWorkbook = path);
      }

      Log("Загружено типов БУОС: " + book.Variants.Count + " (" + Path.GetFileName(path) + ")");
      LogPriceStatus();
      BindGrid();
    }

    private void ReloadPrices()
    {
      _priceFile = TracingCache.ResolveRatesFilePath();
      _prices = TopologyElementPrices.Load(_priceFile);
    }

    private void ApplyPricesToRows()
    {
      foreach (BuosRow row in _rows)
      {
        ApplyPrice(row);
      }
    }

    private void ApplyPrice(BuosRow row)
    {
      if (TopologyElementPrices.TryGet(_prices, row.Name, out double price))
      {
        row.CostMissing = false;
        row.CostText = price.ToString("0.###", CultureInfo.CurrentCulture);
      }
      else
      {
        row.CostMissing = true;
        row.CostText = TopologyElementPrices.MissingHint;
      }
    }

    private void LogPriceStatus()
    {
      string fileLabel = string.IsNullOrWhiteSpace(_priceFile) ? "(нет)" : _priceFile;
      if (!File.Exists(_priceFile))
      {
        Log("Price не найден: " + fileLabel + ". Положите Price.xlsx в Tracing_cache\\Settings\\.");
        return;
      }

      int missing = 0;
      foreach (BuosRow row in _rows)
      {
        if (row.CostMissing)
        {
          missing++;
        }
      }

      Log("Цены: " + Path.GetFileName(_priceFile) + ", лист «" + TopologyElementPrices.SheetName
        + "», записей: " + _prices.Count
        + (missing > 0 ? "; без цены: " + missing + " (красные)" : "; все цены найдены") + ".");
    }

    private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count)
      {
        return;
      }

      BuosRow row = _rows[e.RowIndex];
      if (!row.CostMissing)
      {
        return;
      }

      DataGridViewRow dgRow = _grid.Rows[e.RowIndex];
      dgRow.DefaultCellStyle.ForeColor = Color.Firebrick;
      if (_grid.Columns[e.ColumnIndex].DataPropertyName == nameof(BuosRow.CostText))
      {
        e.CellStyle.ForeColor = Color.Firebrick;
        e.CellStyle.BackColor = Color.FromArgb(255, 230, 230);
        e.CellStyle.SelectionForeColor = Color.Firebrick;
        e.CellStyle.SelectionBackColor = Color.FromArgb(255, 210, 210);
      }
    }

    private void BindGrid()
    {
      _grid.DataSource = null;
      _grid.DataSource = _rows;
      _status.Text = "Сетка БУОС: rows=" + _rows.Count;
      _grid.Refresh();
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

    public sealed class BuosRow
    {
      public string Name { get; set; } = string.Empty;
      public string CombinedText { get; set; } = string.Empty;
      public string GasText { get; set; } = string.Empty;
      public string LengthText { get; set; } = string.Empty;
      public string WidthText { get; set; } = string.Empty;
      public string CostText { get; set; } = TopologyElementPrices.MissingHint;
      public bool CostMissing { get; set; } = true;
    }
  }
}
