namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows.Forms;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// Pick БУОС/Узел from База данных typical catalog. First column «Имя» = instance name.
  /// </summary>
  public sealed class PickFromTypicalCatalogDialog : Form
  {
    private readonly DataGridView _grid;
    private readonly bool _buos;
    private List<CatalogPickRow> _rows = new List<CatalogPickRow>();

    public CatalogPickRow Selected { get; private set; }

    private PickFromTypicalCatalogDialog(bool buos, string title)
    {
      _buos = buos;
      Text = title;
      Width = 900;
      Height = 520;
      MinimumSize = new Size(640, 400);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = false;
      MaximizeBox = false;
      MinimizeBox = false;

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 3,
        Padding = new Padding(10)
      };
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      Controls.Add(root);

      root.Controls.Add(new Label
      {
        Text = "Выберите тип из каталога. Колонка «Имя» — имя экземпляра (пусто по умолчанию, введите сразу).",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 0, 0, 6)
      }, 0, 0);

      _grid = new DataGridView
      {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
      };
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Имя",
        DataPropertyName = nameof(CatalogPickRow.InstanceName),
        FillWeight = 18,
        ReadOnly = false
      });
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Тип",
        DataPropertyName = nameof(CatalogPickRow.TypeName),
        FillWeight = 18,
        ReadOnly = true
      });
      if (buos)
      {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
          HeaderText = "Жидкость м³/сут",
          DataPropertyName = nameof(CatalogPickRow.CombinedCapacity),
          FillWeight = 16,
          ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
          HeaderText = "ПНГ ст.м³/сут",
          DataPropertyName = nameof(CatalogPickRow.GasCapacity),
          FillWeight = 16,
          ReadOnly = true
        });
      }

      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Длина м",
        DataPropertyName = nameof(CatalogPickRow.LengthM),
        FillWeight = 12,
        ReadOnly = true
      });
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Ширина м",
        DataPropertyName = nameof(CatalogPickRow.WidthM),
        FillWeight = 12,
        ReadOnly = true
      });
      _grid.CellDoubleClick += (_, e) =>
      {
        if (e.RowIndex >= 0)
        {
          AcceptSelection();
        }
      };
      root.Controls.Add(_grid, 0, 1);

      var buttons = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        Padding = new Padding(0, 8, 0, 0)
      };
      var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
      var ok = new Button { Text = "Вставить", AutoSize = true };
      ok.Click += (_, __) => AcceptSelection();
      buttons.Controls.Add(cancel);
      buttons.Controls.Add(ok);
      root.Controls.Add(buttons, 0, 2);
      AcceptButton = ok;
      CancelButton = cancel;

      // Focus «Имя» only after the HWND exists — BeginInvoke in ctor throws.
      Shown += (_, __) => TryBeginEditInstanceName();
      LoadCatalog();
    }

    public static CatalogPickRow ShowPick(IWin32Window owner, bool buos)
    {
      string title = buos ? "Добавить БУОС из типовых" : "Добавить Узел из типовых";
      using var dlg = new PickFromTypicalCatalogDialog(buos, title);
      DialogResult r = UiModal.ShowDialog(dlg, owner);
      return r == DialogResult.OK ? dlg.Selected : null;
    }

    private void LoadCatalog()
    {
      TracingCache.EnsureCreated();
      AppPaths paths = AppPaths.Load();
      if (_buos)
      {
        string path = paths.BuosWorkbook;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
          throw new InvalidOperationException(
            "Нет каталога БУОС (paths.json → buosWorkbook). Откройте «База данных» → вкладку каталога и укажите Excel.");
        }

        BuosWorkbookLoader.WorkbookData book = BuosWorkbookLoader.LoadWorkbook(path);
        _rows = book.Variants.Select(v => new CatalogPickRow
        {
          InstanceName = string.Empty,
          TypeName = v.Name,
          CombinedCapacity = v.CombinedCapacity.ToString("0.###", CultureInfo.CurrentCulture),
          GasCapacity = v.GasCapacity.ToString("0.###", CultureInfo.CurrentCulture),
          LengthM = v.LengthM.ToString("0.###", CultureInfo.CurrentCulture),
          WidthM = v.WidthM.ToString("0.###", CultureInfo.CurrentCulture),
          LengthValue = v.LengthM > 0 ? v.LengthM : 250,
          WidthValue = v.WidthM > 0 ? v.WidthM : 150
        }).ToList();
      }
      else
      {
        string path = paths.NodesWorkbook;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
          throw new InvalidOperationException(
            "Нет каталога Узлы (paths.json → nodesWorkbook). Откройте «База данных» → вкладку каталога и укажите Excel.");
        }

        NodesWorkbookLoader.WorkbookData book = NodesWorkbookLoader.LoadWorkbook(path);
        _rows = book.Variants.Select(v => new CatalogPickRow
        {
          InstanceName = string.Empty,
          TypeName = v.Name,
          LengthM = v.LengthM.ToString("0.###", CultureInfo.CurrentCulture),
          WidthM = v.WidthM.ToString("0.###", CultureInfo.CurrentCulture),
          LengthValue = v.LengthM > 0 ? v.LengthM : 20,
          WidthValue = v.WidthM > 0 ? v.WidthM : 20
        }).ToList();
      }

      _grid.DataSource = _rows;
      if (_rows.Count > 0)
      {
        _grid.ClearSelection();
        _grid.Rows[0].Selected = true;
        if (_grid.Columns.Count > 0)
        {
          _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
      }

      TryBeginEditInstanceName();
    }

    private void TryBeginEditInstanceName()
    {
      if (_rows.Count == 0 || _grid.Rows.Count == 0 || _grid.Columns.Count == 0)
      {
        return;
      }

      try
      {
        _grid.CurrentCell = _grid.Rows[0].Cells[0];
        // Only edit once HWND exists — never BeginInvoke before IsHandleCreated.
        if (!IsHandleCreated || !_grid.IsHandleCreated || IsDisposed || _grid.IsDisposed)
        {
          return;
        }

        _grid.BeginEdit(selectAll: true);
      }
      catch
      {
        // Never surface Invoke/handle timing as a MessageBox.
      }
    }

    private void AcceptSelection()
    {
      _grid.EndEdit();
      if (_grid.CurrentRow == null || _grid.CurrentRow.Index < 0 || _grid.CurrentRow.Index >= _rows.Count)
      {
        MessageBox.Show(this, "Выберите строку каталога.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
      }

      CatalogPickRow row = _rows[_grid.CurrentRow.Index];
      if (string.IsNullOrWhiteSpace(row.InstanceName))
      {
        MessageBox.Show(this, "Укажите «Имя» экземпляра.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        TryBeginEditInstanceName();
        return;
      }

      row.InstanceName = row.InstanceName.Trim();
      if (IsDuplicateInstanceName(row.InstanceName))
      {
        string kind = _buos ? "БУОС" : "Узел";
        MessageBox.Show(
          this,
          kind + " с именем «" + row.InstanceName + "» уже есть на чертеже. Укажите уникальное «Имя».",
          Text,
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
        TryBeginEditInstanceName();
        return;
      }

      Selected = row;
      DialogResult = DialogResult.OK;
      Close();
    }

    private bool IsDuplicateInstanceName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        return false;
      }

      try
      {
        if (_buos)
        {
          return CadBuosPlacementOverlay.ListPlacedSites()
            .Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        return CadNodePlacementOverlay.ListPlacedSites()
          .Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
      }
      catch
      {
        return false;
      }
    }

    public sealed class CatalogPickRow
    {
      public string InstanceName { get; set; }
      public string TypeName { get; set; }
      public string CombinedCapacity { get; set; }
      public string GasCapacity { get; set; }
      public string LengthM { get; set; }
      public string WidthM { get; set; }
      public double LengthValue { get; set; }
      public double WidthValue { get; set; }
    }
  }
}
