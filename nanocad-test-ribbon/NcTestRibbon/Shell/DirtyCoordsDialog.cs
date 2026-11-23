namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Linq;
  using System.Windows.Forms;

  public enum DirtyCoordsAction
  {
    Cancel = 0,
    Save = 1,
    Load = 2
  }

  public sealed class DirtyCoordItem
  {
    public string Group { get; set; }
    public string Name { get; set; }
    public string Detail { get; set; }
  }

  /// <summary>
  /// Yellow dirty-list prompt with «Сохранить положения» / «Загрузить из исходных».
  /// Used on SourceData close and drawing close.
  /// </summary>
  public sealed class DirtyCoordsDialog : Form
  {
    private readonly DataGridView _grid;
    public DirtyCoordsAction ResultAction { get; private set; } = DirtyCoordsAction.Cancel;

    public DirtyCoordsDialog(string title, string message, IEnumerable<DirtyCoordItem> items)
    {
      Text = string.IsNullOrWhiteSpace(title) ? "Изменённые положения" : title;
      Width = 640;
      Height = 420;
      MinimumSize = new Size(480, 300);
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
        Text = message ?? "Есть несохранённые положения на чертеже (жёлтые строки).",
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 0, 0, 8)
      }, 0, 0);

      _grid = new DataGridView
      {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
      };
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Тип",
        DataPropertyName = nameof(DirtyCoordItem.Group),
        FillWeight = 30
      });
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Объект",
        DataPropertyName = nameof(DirtyCoordItem.Name),
        FillWeight = 35
      });
      _grid.Columns.Add(new DataGridViewTextBoxColumn
      {
        HeaderText = "Положение",
        DataPropertyName = nameof(DirtyCoordItem.Detail),
        FillWeight = 45
      });
      _grid.CellFormatting += (_, e) =>
      {
        if (e.RowIndex < 0)
        {
          return;
        }

        _grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.Yellow;
      };
      List<DirtyCoordItem> list = (items ?? Enumerable.Empty<DirtyCoordItem>()).ToList();
      _grid.DataSource = list;
      root.Controls.Add(_grid, 0, 1);

      var buttons = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        WrapContents = false,
        Padding = new Padding(0, 8, 0, 0)
      };
      var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
      var load = new Button { Text = "Загрузить из исходных", AutoSize = true };
      var save = new Button { Text = "Сохранить положения", AutoSize = true };
      cancel.Click += (_, __) =>
      {
        ResultAction = DirtyCoordsAction.Cancel;
        DialogResult = DialogResult.Cancel;
        Close();
      };
      load.Click += (_, __) =>
      {
        ResultAction = DirtyCoordsAction.Load;
        DialogResult = DialogResult.OK;
        Close();
      };
      save.Click += (_, __) =>
      {
        ResultAction = DirtyCoordsAction.Save;
        DialogResult = DialogResult.OK;
        Close();
      };
      buttons.Controls.Add(cancel);
      buttons.Controls.Add(load);
      buttons.Controls.Add(save);
      root.Controls.Add(buttons, 0, 2);

      AcceptButton = save;
      CancelButton = cancel;
    }

    public static DirtyCoordsAction ShowPrompt(
      IWin32Window owner,
      string title,
      string message,
      IEnumerable<DirtyCoordItem> items)
    {
      using var dlg = new DirtyCoordsDialog(title, message, items);
      DialogResult r = owner == null ? dlg.ShowDialog() : dlg.ShowDialog(owner);
      if (r != DialogResult.OK)
      {
        return DirtyCoordsAction.Cancel;
      }

      return dlg.ResultAction;
    }
  }
}
