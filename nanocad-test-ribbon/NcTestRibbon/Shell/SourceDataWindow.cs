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
  /// «База данных»: Кустовые / Врезки (Excel browse) + БУОС типовые / Узлы типовые (catalogs).
  /// No Show/Save/Load CAD controls — placement is «Объекты чертежа».
  /// </summary>
  public sealed class SourceDataWindow : Form
  {
    private readonly TabControl _tabs;
    private readonly PadOptimizationWindow _pads;
    private readonly InsertPointsWindow _inserts;
    private readonly BuosVariantsWindow _buosTypical;
    private readonly NodeVariantsWindow _nodesTypical;
    private bool _closePromptDone;

    public SourceDataWindow(SourceDataTab initialTab = SourceDataTab.Pads)
    {
      Text = "База данных";
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

      _pads = new PadOptimizationWindow(databaseBrowseOnly: true);
      _inserts = new InsertPointsWindow(databaseBrowseOnly: true);
      _buosTypical = new BuosVariantsWindow();
      _nodesTypical = new NodeVariantsWindow();

      _tabs.TabPages.Add(MakePage("Кустовые площадки", _pads));
      _tabs.TabPages.Add(MakePage("Врезки", _inserts));
      _tabs.TabPages.Add(MakePage("БУОС типовые", _buosTypical));
      _tabs.TabPages.Add(MakePage("Узлы типовые", _nodesTypical));

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
      _pads.HasDirtyCoords || _inserts.HasDirtyCoords;

    public List<DirtyCoordItem> CollectDirtyItems()
    {
      var items = new List<DirtyCoordItem>();
      items.AddRange(_pads.EnumerateDirtyItems());
      items.AddRange(_inserts.EnumerateDirtyItems());
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
      try { Application.DoEvents(); }
      catch { }

      _pads.TrySelectPadByName(name);
    }

    public void ApplyPadCadPose(string name, double x, double y, double rotationDeg, bool saveExcel)
    {
      SelectTab(SourceDataTab.Pads);
      try { Application.DoEvents(); }
      catch { }

      _pads.TrySelectPadByName(name);
      _pads.ApplyCadPose(name, x, y, rotationDeg, saveExcel);
    }

    public PadOptimizationWindow PadsPanel => _pads;

    /// <summary>Legacy: placement is in «Объекты чертежа», not База данных.</summary>
    public void BeginPlaceForTab(SourceDataTab tab)
    {
      SelectTab(tab);
      throw new InvalidOperationException(
        "Размещение выполняется в «Объекты чертежа», не в «База данных».");
    }

    /// <summary>Save dirty coords for Кустовые/Врезки only (+ flush CAD pending pads).</summary>
    public void SaveAllToSource(bool confirm)
    {
      if (confirm)
      {
        DialogResult ok = MessageBox.Show(
          this,
          "Сохранить изменённые положения КП/Врезок в исходные Excel?",
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
    }

    /// <summary>Reload Кустовые/Врезки from Excel.</summary>
    public void LoadFromSource(bool confirm, bool activeTabOnly)
    {
      if (confirm)
      {
        string scope = activeTabOnly
          ? "активной вкладки («" + ActiveTabLabel() + "»)"
          : "вкладок Кустовые/Врезки";
        DialogResult ok = MessageBox.Show(
          this,
          "Загрузить положения из исходных Excel для " + scope + "?",
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
        CadPadInteraction.ClearPending();
        return;
      }

      if (action == DirtyCoordsAction.Load)
      {
        LoadFromSource(confirm: false, activeTabOnly: false);
      }
    }

    /// <summary>After close-dialog Save: drop yellow/pending so the prompt cannot loop.</summary>
    public void ClearDirtyAfterCloseSave()
    {
      try { _pads.ClearDirtyFlags(); }
      catch { }

      try { _inserts.ClearDirtyFlags(); }
      catch { }

      CadPadInteraction.ClearPending();
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
      try { _pads.NotifyHostClosing(); }
      catch { }

      try { _inserts.NotifyHostClosing(); }
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
        "База данных",
        "Есть изменённые положения (жёлтые). Сохранить в исходные или загрузить из Excel?",
        dirty);
      _closePromptDone = true;
      if (action == DirtyCoordsAction.Cancel)
      {
        try { _pads.DiscardDirtyCoords(); }
        catch { }

        try { _inserts.DiscardDirtyCoords(); }
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
      }
    }

    private string ActiveTabLabel()
    {
      return ActiveTab switch
      {
        SourceDataTab.Inserts => "Врезки",
        SourceDataTab.Buos => "БУОС типовые",
        SourceDataTab.Nodes => "Узлы типовые",
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
