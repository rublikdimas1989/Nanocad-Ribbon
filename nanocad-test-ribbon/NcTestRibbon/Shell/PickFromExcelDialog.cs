namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.IO;
  using System.Linq;
  using System.Windows.Forms;

  public sealed class PickFromExcelItem
  {
    public string Name { get; set; }
    public string Detail { get; set; }
    public object Tag { get; set; }
    public bool HasCoords { get; set; }
  }

  /// <summary>
  /// «Выбрать из файла»: browse Excel → list → Explorer-style multi-select (Ctrl/Shift) → place selected.
  /// </summary>
  public sealed class PickFromExcelDialog : Form
  {
    private readonly TextBox _pathBox;
    private readonly DataGridView _grid;
    private readonly Button _ok;
    private readonly Func<string, IList<PickFromExcelItem>> _loader;
    private readonly string _fileFilter;
    private readonly string _browseTitle;
    private List<PickFromExcelItem> _items = new List<PickFromExcelItem>();

    public string SelectedPath { get; private set; }
    public IList<PickFromExcelItem> SelectedItems { get; private set; } = Array.Empty<PickFromExcelItem>();

    public PickFromExcelDialog(
      string title,
      string initialPath,
      string browseTitle,
      string fileFilter,
      Func<string, IList<PickFromExcelItem>> loader)
    {
      _loader = loader ?? throw new ArgumentNullException(nameof(loader));
      _fileFilter = string.IsNullOrWhiteSpace(fileFilter)
        ? "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Все файлы|*.*"
        : fileFilter;
      _browseTitle = string.IsNullOrWhiteSpace(browseTitle) ? "выбрать из файла" : browseTitle;

      Text = string.IsNullOrWhiteSpace(title) ? "Выбрать из файла" : title;
      Width = 720;
      Height = 480;
      MinimumSize = new Size(520, 360);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 4,
        Padding = new Padding(10)
      };
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      Controls.Add(root);

      var fileRow = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        AutoSize = true,
        WrapContents = false
      };
      fileRow.Controls.Add(new Label
      {
        Text = "Файл:",
        AutoSize = true,
        Margin = new Padding(0, 8, 6, 0)
      });
      _pathBox = new TextBox
      {
        Width = 480,
        ReadOnly = true,
        Margin = new Padding(0, 4, 6, 0)
      };
      fileRow.Controls.Add(_pathBox);
      var browse = new Button { Text = "выбрать из файла…", AutoSize = true };
      browse.Click += (_, __) => Browse();
      fileRow.Controls.Add(browse);
      root.Controls.Add(fileRow, 0, 0);

      root.Controls.Add(new Label
      {
        Text = "Выбор: Ctrl+клик — добавить/снять; Shift+клик — диапазон. Размещаются только выделенные строки.",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 4, 0, 4)
      }, 0, 1);

      _grid = new DataGridView
      {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
      };
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Имя",
        DataPropertyName = nameof(PickFromExcelItem.Name),
        FillWeight = 35
      });
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Положение",
        DataPropertyName = nameof(PickFromExcelItem.Detail),
        FillWeight = 65
      });
      _grid.CellDoubleClick += (_, e) =>
      {
        if (e.RowIndex >= 0)
        {
          AcceptSelection();
        }
      };
      root.Controls.Add(_grid, 0, 2);

      var buttons = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        Padding = new Padding(0, 8, 0, 0)
      };
      var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
      _ok = new Button { Text = "Разместить", AutoSize = true, Enabled = false };
      _ok.Click += (_, __) => AcceptSelection();
      buttons.Controls.Add(cancel);
      buttons.Controls.Add(_ok);
      root.Controls.Add(buttons, 0, 3);

      AcceptButton = _ok;
      CancelButton = cancel;

      if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
      {
        TryLoad(initialPath);
      }
      else
      {
        Shown += (_, __) =>
        {
          if (_items.Count == 0)
          {
            Browse();
          }
        };
      }

      _grid.SelectionChanged += (_, __) => UpdateOk();
    }

    /// <summary>Multi-select pick; returns selected rows (Explorer Ctrl/Shift).</summary>
    public static IList<PickFromExcelItem> ShowPickMany(
      IWin32Window owner,
      string title,
      string initialPath,
      string browseTitle,
      Func<string, IList<PickFromExcelItem>> loader,
      out string path)
    {
      using var dlg = new PickFromExcelDialog(title, initialPath, browseTitle, null, loader);
      DialogResult r = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
      if (r != DialogResult.OK || dlg.SelectedItems == null || dlg.SelectedItems.Count == 0)
      {
        path = null;
        return Array.Empty<PickFromExcelItem>();
      }

      path = dlg.SelectedPath;
      return dlg.SelectedItems.ToList();
    }

    private void Browse()
    {
      using var ofd = new OpenFileDialog
      {
        Title = _browseTitle,
        Filter = _fileFilter,
        CheckFileExists = true
      };
      if (!string.IsNullOrWhiteSpace(_pathBox.Text) && File.Exists(_pathBox.Text))
      {
        try
        {
          ofd.InitialDirectory = Path.GetDirectoryName(_pathBox.Text);
          ofd.FileName = Path.GetFileName(_pathBox.Text);
        }
        catch
        {
        }
      }

      if (ofd.ShowDialog(this) != DialogResult.OK)
      {
        return;
      }

      TryLoad(ofd.FileName);
    }

    private void TryLoad(string path)
    {
      try
      {
        IList<PickFromExcelItem> loaded = _loader(path) ?? Array.Empty<PickFromExcelItem>();
        _items = loaded.ToList();
        SelectedPath = path;
        _pathBox.Text = path;
        _grid.DataSource = null;
        _grid.DataSource = _items;
        if (_items.Count > 0)
        {
          _grid.ClearSelection();
          _grid.Rows[0].Selected = true;
          _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }

        UpdateOk();
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          this,
          "Не удалось прочитать файл:\n" + ex.Message,
          Text,
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
      }
    }

    private void UpdateOk()
    {
      _ok.Enabled = CollectSelected().Count > 0 && !string.IsNullOrWhiteSpace(SelectedPath);
      int n = CollectSelected().Count;
      _ok.Text = n <= 1 ? "Разместить" : ("Разместить (" + n + ")");
    }

    private List<PickFromExcelItem> CollectSelected()
    {
      var list = new List<PickFromExcelItem>();
      if (_grid.SelectedRows == null || _grid.SelectedRows.Count == 0)
      {
        return list;
      }

      foreach (DataGridViewRow row in _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index))
      {
        if (row.Index >= 0 && row.Index < _items.Count)
        {
          list.Add(_items[row.Index]);
        }
      }

      return list;
    }

    private void AcceptSelection()
    {
      List<PickFromExcelItem> items = CollectSelected();
      if (items.Count == 0 || string.IsNullOrWhiteSpace(SelectedPath))
      {
        return;
      }

      SelectedItems = items;
      DialogResult = DialogResult.OK;
      Close();
    }
  }
}
