namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Linq;
  using System.Windows.Forms;
  using NcTestRibbon.Cad;

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
    private bool _closePromptDone;

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

      FormClosing += OnFormClosing;

      SelectTab(initialTab);
    }

    public SourceDataTab ActiveTab
    {
      get
      {
        int index = _tabs.SelectedIndex;
        if (index < 0 || index > 3)
        {
          return SourceDataTab.Pads;
        }

        return (SourceDataTab)index;
      }
    }

    public bool HasAnyDirtyCoords =>
      _pads.HasDirtyCoords || _inserts.HasDirtyCoords
      || _buos.HasDirtyCoords || _nodes.HasDirtyCoords;

    public List<DirtyCoordItem> CollectDirtyItems()
    {
      var items = new List<DirtyCoordItem>();
      items.AddRange(_pads.EnumerateDirtyItems());
      items.AddRange(_inserts.EnumerateDirtyItems());
      items.AddRange(_buos.EnumerateDirtyItems());
      items.AddRange(_nodes.EnumerateDirtyItems());
      foreach (string name in CadPadInteraction.PendingPadNames)
      {
        if (items.Any(i => i.Group == "Кустовые"
            && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
          continue;
        }

        items.Add(new DirtyCoordItem
        {
          Group = "Кустовые",
          Name = name,
          Detail = "ожидает записи с чертежа"
        });
      }

      return items;
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

    /// <summary>Open Кустовые tab and select KP by CAD XData name.</summary>
    public void SelectPadByName(string name)
    {
      SelectTab(SourceDataTab.Pads);
      try { System.Windows.Forms.Application.DoEvents(); }
      catch { }

      _pads.TrySelectPadByName(name);
    }

    public void ApplyPadCadPose(string name, double x, double y, double rotationDeg, bool saveExcel)
    {
      SelectTab(SourceDataTab.Pads);
      try { System.Windows.Forms.Application.DoEvents(); }
      catch { }

      _pads.TrySelectPadByName(name);
      _pads.ApplyCadPose(name, x, y, rotationDeg, saveExcel);
    }

    public PadOptimizationWindow PadsPanel => _pads;

    /// <summary>
    /// Ribbon «Добавить Кустовую» / «Добавить БУОС»: open tab and start CAD place jig.
    /// </summary>
    public void BeginPlaceForTab(SourceDataTab tab)
    {
      SelectTab(tab);
      try { System.Windows.Forms.Application.DoEvents(); }
      catch { }

      if (tab == SourceDataTab.Pads)
      {
        _pads.BeginPlaceSelected();
        return;
      }

      if (tab == SourceDataTab.Buos)
      {
        _buos.BeginPlaceSelected();
        return;
      }

      if (tab == SourceDataTab.Nodes)
      {
        _nodes.BeginPlaceSelected();
        return;
      }

      throw new InvalidOperationException(
        "Размещение с ленты поддерживается для вкладок Кустовые, БУОС и Узлы.");
    }


    /// <summary>Save dirty coords for all tabs (+ flush CAD pending pads). Optional confirm.</summary>
    public void SaveAllToSource(bool confirm)
    {
      if (confirm)
      {
        DialogResult ok = MessageBox.Show(
          this,
          "Сохранить изменённые положения во все открытые исходные Excel?",
          "Сохранить в исходные",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }
      }

      if (CadPadInteraction.HasPendingExcelSync)
      {
        CadPadInteraction.CommitPendingToExcel();
      }

      _pads.SaveDirtyCoords(confirm: false);
      _inserts.SaveDirtyCoords(confirm: false);
      _buos.SaveDirtyCoords(confirm: false);
      _nodes.SaveDirtyCoords(confirm: false);
    }

    /// <summary>Reload every tab from its Excel (or only the active tab).</summary>
    public void LoadFromSource(bool confirm, bool activeTabOnly)
    {
      if (confirm)
      {
        string scope = activeTabOnly
          ? "активной вкладки («" + ActiveTabLabel() + "»)"
          : "всех вкладок";
        DialogResult ok = MessageBox.Show(
          this,
          "Загрузить положения из исходных Excel для " + scope
            + " и сбросить изменения на чертеже?",
          "Загрузить из исходных",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }
      }

      CadPadInteraction.ClearPending();

      if (activeTabOnly)
      {
        ReloadTab(ActiveTab, confirm: false);
        return;
      }

      ReloadTab(SourceDataTab.Pads, confirm: false);
      ReloadTab(SourceDataTab.Inserts, confirm: false);
      ReloadTab(SourceDataTab.Buos, confirm: false);
      ReloadTab(SourceDataTab.Nodes, confirm: false);
    }

    public void ReloadActiveTabFromSource(bool confirm)
    {
      LoadFromSource(confirm, activeTabOnly: true);
    }

    public void ApplyDirtyPromptAction(DirtyCoordsAction action)
    {
      if (action == DirtyCoordsAction.Save)
      {
        SaveAllToSource(confirm: false);
        return;
      }

      if (action == DirtyCoordsAction.Load)
      {
        LoadFromSource(confirm: false, activeTabOnly: false);
      }
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
      try { _pads.NotifyHostClosing(); }
      catch { }

      try { _inserts.NotifyHostClosing(); }
      catch { }

      try { _buos.NotifyHostClosing(); }
      catch { }

      try { _nodes.NotifyHostClosing(); }
      catch { }

      if (_closePromptDone)
      {
        return;
      }

      List<DirtyCoordItem> dirty = CollectDirtyItems();
      if (dirty.Count == 0)
      {
        return;
      }

      DirtyCoordsAction action = DirtyCoordsDialog.ShowPrompt(
        this,
        "Исходные данные",
        "Есть изменённые положения (жёлтые). Сохранить в исходные или загрузить из Excel?",
        dirty);
      _closePromptDone = true;
      if (action == DirtyCoordsAction.Cancel)
      {
        // Same as old No: discard CAD previews back to last Excel.
        try { _pads.DiscardDirtyCoords(); }
        catch { }

        try { _inserts.DiscardDirtyCoords(); }
        catch { }

        try { _buos.DiscardDirtyCoords(); }
        catch { }

        try { _nodes.DiscardDirtyCoords(); }
        catch { }

        CadPadInteraction.ClearPending();
        return;
      }

      ApplyDirtyPromptAction(action);
    }

    private void ReloadTab(SourceDataTab tab, bool confirm)
    {
      switch (tab)
      {
        case SourceDataTab.Pads:
          _pads.ReloadFromSource(confirm);
          break;
        case SourceDataTab.Inserts:
          _inserts.ReloadFromSource(confirm);
          break;
        case SourceDataTab.Buos:
          _buos.ReloadFromSource(confirm);
          break;
        case SourceDataTab.Nodes:
          _nodes.ReloadFromSource(confirm);
          break;
      }
    }

    private string ActiveTabLabel()
    {
      return ActiveTab switch
      {
        SourceDataTab.Inserts => "Врезки",
        SourceDataTab.Buos => "БУОС",
        SourceDataTab.Nodes => "Узлы",
        _ => "Кустовые"
      };
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
