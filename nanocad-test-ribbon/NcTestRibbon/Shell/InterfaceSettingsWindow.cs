namespace NcTestRibbon.Shell
{
  using System;
  using System.Drawing;
  using System.Globalization;
  using System.Windows.Forms;
  using NcTestRibbon.Configuration;

  /// <summary>
  /// Font size + insert circle diameter → Tracing_cache\Settings\interface.conf
  /// </summary>
  public sealed class InterfaceSettingsWindow : Form
  {
    private readonly TextBox _font;
    private readonly TextBox _diameter;
    private readonly Label _path;

    public InterfaceSettingsWindow()
    {
      Text = "Оформление интерфейса (CAD)";
      Width = 520;
      Height = 260;
      MinimumSize = new Size(420, 220);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      MaximizeBox = false;
      MinimizeBox = false;

      InterfaceConf conf = InterfaceConf.Load();

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 5,
        Padding = new Padding(12)
      };
      root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
      root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
      Controls.Add(root);

      root.Controls.Add(new Label
      {
        Text = "Размер шрифта подписей КП / врезок, м:",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 8, 3, 3)
      }, 0, 0);
      _font = new TextBox
      {
        Text = conf.LabelFontHeightM.ToString("0.###", CultureInfo.CurrentCulture),
        Dock = DockStyle.Fill,
        Margin = new Padding(3)
      };
      root.Controls.Add(_font, 1, 0);

      root.Controls.Add(new Label
      {
        Text = "Диаметр круга точки врезки, м:",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 8, 3, 3)
      }, 0, 1);
      _diameter = new TextBox
      {
        Text = conf.InsertCircleDiameterM.ToString("0.###", CultureInfo.CurrentCulture),
        Dock = DockStyle.Fill,
        Margin = new Padding(3)
      };
      root.Controls.Add(_diameter, 1, 1);

      _path = new Label
      {
        Text = InterfaceConf.FilePath,
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 12, 3, 3)
      };
      root.SetColumnSpan(_path, 2);
      root.Controls.Add(_path, 0, 2);

      var help = new Label
      {
        Text = "Значения применяются к новым отрисовкам на чертеже (слой NC_INSERTS / подписи NC_KP_PADS).",
        AutoSize = true,
        MaximumSize = new Size(460, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 8, 3, 3)
      };
      root.Controls.Add(help, 0, 3);
      root.SetColumnSpan(help, 2);

      var buttons = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        Margin = new Padding(3, 16, 3, 3)
      };
      root.SetColumnSpan(buttons, 2);
      root.Controls.Add(buttons, 0, 4);

      var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true };
      var save = new Button { Text = "Сохранить", AutoSize = true };
      save.Click += (_, __) =>
      {
        try
        {
          Save();
          DialogResult = DialogResult.OK;
          Close();
        }
        catch (Exception ex)
        {
          MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
      };
      buttons.Controls.Add(cancel);
      buttons.Controls.Add(save);
      AcceptButton = save;
      CancelButton = cancel;
    }

    private void Save()
    {
      if (!TryParse(_font.Text, out double font) || font <= 0)
      {
        throw new InvalidOperationException("Укажите положительный размер шрифта (м).");
      }

      if (!TryParse(_diameter.Text, out double diam) || diam <= 0)
      {
        throw new InvalidOperationException("Укажите положительный диаметр круга врезки (м).");
      }

      InterfaceConf.Set(c =>
      {
        c.LabelFontHeightM = font;
        c.InsertCircleDiameterM = diam;
      });
    }

    private static bool TryParse(string text, out double value)
    {
      return double.TryParse((text ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
  }
}
