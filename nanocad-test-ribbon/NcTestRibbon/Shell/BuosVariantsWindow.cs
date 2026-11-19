namespace NcTestRibbon.Shell
{
  using System;
  using System.ComponentModel;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Windows.Forms;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// End BuosCatalogPanel / TopologyBuosType catalogue — display-only Excel grid (no site X/Y).
  /// </summary>
  public sealed class BuosVariantsWindow : Form
  {
    private readonly BindingList<BuosRow> _rows = new BindingList<BuosRow>();
    private readonly TextBox _file;
    private readonly TextBox _log;
    private readonly Label _status;
    private readonly DataGridView _grid;

    public BuosVariantsWindow()
    {
      Text = "Варианты БУОС";
      Width = 1100;
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
        Text = "Каталог типов БУОС (End TopologyBuosType): производительность и габарит одной установки. "
          + "Координаты площадок здесь не задаются. Правки — в Excel; имена уникальны.",
        AutoSize = true,
        MaximumSize = new Size(1040, 0),
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
      _grid.Columns.Add(CheckCol("В подборе", nameof(BuosRow.UseInTopology), 70));
      _grid.Columns.Add(TextCol("Жидкость\nм³/сут", nameof(BuosRow.CombinedText), 100));
      _grid.Columns.Add(TextCol("ПНГ\nст. м³/сут", nameof(BuosRow.GasText), 110));
      _grid.Columns.Add(TextCol("Длина\nм", nameof(BuosRow.LengthText), 70));
      _grid.Columns.Add(TextCol("Ширина\nм", nameof(BuosRow.WidthText), 70));
      _grid.Columns.Add(CheckCol("Цена Excel", nameof(BuosRow.UseExcelPrice), 80));
      _grid.Columns.Add(TextCol("Стоимость\nруб.", nameof(BuosRow.CostText), 100));
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

      BindGrid();
      if (!string.IsNullOrWhiteSpace(paths.BuosWorkbook) && File.Exists(paths.BuosWorkbook))
      {
        try { LoadWorkbook(paths.BuosWorkbook, remember: false); }
        catch (Exception ex) { Log("Не удалось загрузить прошлые варианты БУОС: " + ex.Message); }
      }
      else
      {
        Log("Откройте xlsx с листом «БУОС» / «Варианты БУОС» через «Обзор…».");
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
      _rows.Clear();
      foreach (BuosWorkbookLoader.BuosVariant v in book.Variants)
      {
        _rows.Add(new BuosRow
        {
          Name = v.Name,
          UseInTopology = v.UseInTopology,
          CombinedText = v.CombinedCapacity.ToString("0.###", CultureInfo.CurrentCulture),
          GasText = v.GasCapacity.ToString("0.###", CultureInfo.CurrentCulture),
          LengthText = v.LengthM.ToString("0.###", CultureInfo.CurrentCulture),
          WidthText = v.WidthM.ToString("0.###", CultureInfo.CurrentCulture),
          UseExcelPrice = v.UseExcelPrice,
          CostText = v.CostRub.ToString("0.###", CultureInfo.CurrentCulture)
        });
      }

      _file.Text = path;
      if (remember)
      {
        AppPaths.Set(p => p.BuosWorkbook = path);
      }

      Log("Загружено типов БУОС: " + book.Variants.Count + " (" + Path.GetFileName(path) + ")");
      BindGrid();
    }

    private void BindGrid()
    {
      _grid.DataSource = null;
      _grid.DataSource = _rows;
      _status.Text = "Сетка БУОС: rows=" + _rows.Count;
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

    public sealed class BuosRow
    {
      public string Name { get; set; } = string.Empty;
      public bool UseInTopology { get; set; }
      public string CombinedText { get; set; } = string.Empty;
      public string GasText { get; set; } = string.Empty;
      public string LengthText { get; set; } = string.Empty;
      public string WidthText { get; set; } = string.Empty;
      public bool UseExcelPrice { get; set; }
      public string CostText { get; set; } = string.Empty;
    }
  }
}
