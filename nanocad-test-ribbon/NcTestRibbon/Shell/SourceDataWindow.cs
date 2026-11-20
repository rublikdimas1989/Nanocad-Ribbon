namespace NcTestRibbon.Shell
{
  using System;
  using System.Drawing;
  using System.Windows.Forms;

  public enum SourceDataTab
  {
    Pads = 0,
    Inserts = 1,
    Buos = 2,
    Nodes = 3
  }

  /// <summary>
  /// Modeless host: Кустовые / Врезки / placement БУОС / placement Узлы.
  /// Catalog variants live in Settings (after tracing tab).
  /// </summary>
  public sealed class SourceDataWindow : Form
  {
    private readonly TabControl _tabs;
    private readonly PadOptimizationWindow _pads;
    private readonly InsertPointsWindow _inserts;
    private readonly SitePlacementWindow _buos;
    private readonly SitePlacementWindow _nodes;

    public SourceDataWindow(SourceDataTab initialTab = SourceDataTab.Pads)
    {
      Text = "Исходные данные";
      Width = 1180;
      Height = 740;
      MinimumSize = new Size(800, 520);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = true;

      _tabs = new TabControl
      {
        Dock = DockStyle.Fill,
        Padding = new Point(12, 4)
      };
      Controls.Add(_tabs);

      _pads = new PadOptimizationWindow();
      _inserts = new InsertPointsWindow();
      _buos = new SitePlacementWindow(SitePlacementKind.Buos);
      _nodes = new SitePlacementWindow(SitePlacementKind.Nodes);

      _tabs.TabPages.Add(MakePage("Кустовые площадки", _pads));
      _tabs.TabPages.Add(MakePage("Врезки", _inserts));
      _tabs.TabPages.Add(MakePage("БУОС", _buos));
      _tabs.TabPages.Add(MakePage("Узлы", _nodes));

      FormClosing += (_, __) =>
      {
        try { _pads.NotifyHostClosing(); }
        catch { }

        try { _inserts.NotifyHostClosing(); }
        catch { }

        try { _buos.NotifyHostClosing(); }
        catch { }

        try { _nodes.NotifyHostClosing(); }
        catch { }
      };

      SelectTab(initialTab);
    }

    public void SelectTab(SourceDataTab tab)
    {
      int index = (int)tab;
      if (index < 0 || index >= _tabs.TabCount)
      {
        index = 0;
      }

      _tabs.SelectedIndex = index;
      BringToFront();
      Activate();
    }

    private static TabPage MakePage(string title, Control content)
    {
      var page = new TabPage(title) { Padding = new Padding(0), UseVisualStyleBackColor = true };
      content.Dock = DockStyle.Fill;
      page.Controls.Add(content);
      return page;
    }
  }
}
