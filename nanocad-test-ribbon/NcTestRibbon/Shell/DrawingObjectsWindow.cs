namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.Drawing;
  using System.Linq;
  using System.Windows.Forms;
  using NcTestRibbon.Cad;

  public enum DrawingObjectsTab
  {
    Pads = 0,
    Buos = 1,
    Nodes = 2,
    Inserts = 3
  }

  /// <summary>
  /// Ribbon «Объекты чертежа»: only on-drawing objects; per-tab Add/Save/Load.
  /// No «Показывать на чертеже» — CAD presence is the list source of truth.
  /// </summary>
  public sealed class DrawingObjectsWindow : Form
  {
    private readonly TabControl _tabs;
    private readonly DrawingObjectsCadPanel _pads;
    private readonly DrawingObjectsCadPanel _buos;
    private readonly DrawingObjectsCadPanel _nodes;
    private readonly DrawingObjectsCadPanel _inserts;
    private readonly Button _addButton;
    private readonly Button _deleteButton;
    private readonly Button _saveButton;
    private readonly Button _loadButton;
    private readonly Button _refreshButton;
    private bool _closePromptDone;
    private bool _suppressCadRefresh;

    public DrawingObjectsWindow(DrawingObjectsTab initialTab = DrawingObjectsTab.Pads)
    {
      Text = "Объекты чертежа";
      Width = 1180;
      Height = 780;
      MinimumSize = new Size(800, 520);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = true;

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 2,
        Padding = new Padding(8)
      };
      root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
      Controls.Add(root);

      var toolbar = new FlowLayoutPanel
      {
        Dock = DockStyle.Fill,
        AutoSize = true,
        WrapContents = false,
        Padding = new Padding(0, 0, 0, 6)
      };
      root.Controls.Add(toolbar, 0, 0);

      _addButton = MakeToolButton(toolbar, "Добавить", OnAdd);
      _deleteButton = MakeToolButton(toolbar, "Удалить", OnDelete);
      _saveButton = MakeToolButton(toolbar, "Сохранить в исходные", OnSave);
      _loadButton = MakeToolButton(toolbar, "Загрузить из исходных", OnLoad);
      _refreshButton = MakeToolButton(toolbar, "Обновить с чертежа", () =>
      {
        if (!_suppressCadRefresh)
        {
          ActivePanel().RefreshFromCad();
        }
      });

      _tabs = new TabControl
      {
        Dock = DockStyle.Fill,
        Padding = new Point(12, 4)
      };
      root.Controls.Add(_tabs, 0, 1);

      _pads = new DrawingObjectsCadPanel(DrawingObjectsCadPanel.Kind.Pads);
      _buos = new DrawingObjectsCadPanel(DrawingObjectsCadPanel.Kind.Buos);
      _nodes = new DrawingObjectsCadPanel(DrawingObjectsCadPanel.Kind.Nodes);
      _inserts = new DrawingObjectsCadPanel(DrawingObjectsCadPanel.Kind.Inserts);

      _tabs.TabPages.Add(MakePage("Кустовые", _pads));
      _tabs.TabPages.Add(MakePage("БУОС", _buos));
      _tabs.TabPages.Add(MakePage("Узлы", _nodes));
      _tabs.TabPages.Add(MakePage("Врезки", _inserts));
      _tabs.SelectedIndexChanged += (_, __) =>
      {
        UpdateToolbarLabels();
        if (!_suppressCadRefresh)
        {
          ActivePanel().RefreshFromCad();
        }
      };

      FormClosing += OnFormClosing;
      Shown += (_, __) =>
      {
        if (!_suppressCadRefresh)
        {
          ActivePanel().RefreshFromCad();
        }
      };
      SelectTab(initialTab);
      UpdateToolbarLabels();
    }

    public DrawingObjectsTab ActiveTab
    {
      get
      {
        int index = _tabs.SelectedIndex;
        if (index < 0 || index > 3)
        {
          return DrawingObjectsTab.Pads;
        }

        return (DrawingObjectsTab)index;
      }
    }

    public bool HasAnyDirtyCoords =>
      _pads.HasDirtyCoords || _buos.HasDirtyCoords
      || _nodes.HasDirtyCoords || _inserts.HasDirtyCoords;

    public List<DirtyCoordItem> CollectDirtyItems()
    {
      var items = new List<DirtyCoordItem>();
      items.AddRange(_pads.EnumerateDirtyItems());
      items.AddRange(_buos.EnumerateDirtyItems());
      items.AddRange(_nodes.EnumerateDirtyItems());
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

    public void SelectTab(DrawingObjectsTab tab)
    {
      int index = (int)tab;
      if (index < 0 || index >= _tabs.TabCount)
      {
        index = 0;
      }

      _tabs.SelectedIndex = index;
      UpdateToolbarLabels();
      BringToFront();
      Activate();
    }

    public void SelectPadByName(string name)
    {
      SelectTab(DrawingObjectsTab.Pads);
      _pads.RefreshFromCad();
    }

    public void ApplyPadCadPose(string name, double x, double y, double rotationDeg, bool saveExcel)
    {
      SelectTab(DrawingObjectsTab.Pads);
      _pads.RefreshFromCad();
      if (saveExcel)
      {
        try { _pads.SaveToSource(confirm: false, confirmTitle: "Сохранить в исходные КП"); }
        catch { }
      }
    }

    public void ApplyDirtyPromptAction(DirtyCoordsAction action)
    {
      if (action == DirtyCoordsAction.Save)
      {
        try { _pads.SaveToSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        try { _buos.SaveToSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        try { _nodes.SaveToSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        try { _inserts.SaveToSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        return;
      }

      if (action == DirtyCoordsAction.Load)
      {
        try { _pads.LoadFromSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        try { _buos.LoadFromSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        try { _nodes.LoadFromSource(confirm: false, confirmTitle: string.Empty); }
        catch { }

        try { _inserts.LoadFromSource(confirm: false, confirmTitle: string.Empty); }
        catch { }
      }
    }

    public void SaveActiveTab(bool confirm)
    {
      ActivePanel().SaveToSource(confirm, SaveConfirmTitle());
    }

    public void LoadActiveTab(bool confirm)
    {
      ActivePanel().LoadFromSource(confirm, LoadConfirmTitle());
    }

    private DrawingObjectsCadPanel ActivePanel()
    {
      return ActiveTab switch
      {
        DrawingObjectsTab.Buos => _buos,
        DrawingObjectsTab.Nodes => _nodes,
        DrawingObjectsTab.Inserts => _inserts,
        _ => _pads
      };
    }

    private void UpdateToolbarLabels()
    {
      switch (ActiveTab)
      {
        case DrawingObjectsTab.Buos:
          _addButton.Text = "Добавить БУОС";
          _saveButton.Text = "Сохранить в исходные БУОС";
          _loadButton.Text = "Загрузить из исходных БУОС";
          break;
        case DrawingObjectsTab.Nodes:
          _addButton.Text = "Добавить Узел";
          _saveButton.Text = "Сохранить в исходные Узлы";
          _loadButton.Text = "Загрузить из исходных Узлы";
          break;
        case DrawingObjectsTab.Inserts:
          _addButton.Text = "Добавить врезки";
          _saveButton.Text = "Сохранить в исходные Врезки";
          _loadButton.Text = "Загрузить из исходных Врезки";
          break;
        default:
          _addButton.Text = "Добавить Кустовую";
          _saveButton.Text = "Сохранить в исходные КП";
          _loadButton.Text = "Загрузить из исходных КП";
          break;
      }
    }

    private void OnAdd()
    {
      Guard(() =>
      {
        _suppressCadRefresh = true;
        try
        {
          switch (ActiveTab)
          {
            case DrawingObjectsTab.Buos:
              CadPlaceFromExcel.PlaceBuosFromCatalog(this);
              break;
            case DrawingObjectsTab.Nodes:
              CadPlaceFromExcel.PlaceNodeFromCatalog(this);
              break;
            case DrawingObjectsTab.Inserts:
              CadPlaceFromExcel.PlaceInsert(this);
              break;
            default:
              CadPlaceFromExcel.PlacePad(this);
              break;
          }
        }
        finally
        {
          _suppressCadRefresh = false;
        }

        // One refresh after the whole add (no per-item reopen/flicker).
        ActivePanel().RefreshFromCad();
      });
    }

    private void OnDelete()
    {
      Guard(() =>
      {
        int removed = ActivePanel().DeleteSelectedFromCad();
        if (removed == 0)
        {
          MessageBox.Show(
            this,
            "Выберите строки для удаления с чертежа.",
            Text,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        }
      });
    }

    private void OnSave()
    {
      Guard(() => SaveActiveTab(confirm: true));
    }

    private void OnLoad()
    {
      Guard(() => LoadActiveTab(confirm: true));
    }

    private string SaveConfirmTitle()
    {
      return ActiveTab switch
      {
        DrawingObjectsTab.Buos => "Сохранить в исходные БУОС",
        DrawingObjectsTab.Nodes => "Сохранить в исходные Узлы",
        DrawingObjectsTab.Inserts => "Сохранить в исходные Врезки",
        _ => "Сохранить в исходные КП"
      };
    }

    private string LoadConfirmTitle()
    {
      return ActiveTab switch
      {
        DrawingObjectsTab.Buos => "Загрузить из исходных БУОС",
        DrawingObjectsTab.Nodes => "Загрузить из исходных Узлы",
        DrawingObjectsTab.Inserts => "Загрузить из исходных Врезки",
        _ => "Загрузить из исходных КП"
      };
    }

    private void Guard(Action action)
    {
      try { action(); }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
      }
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
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
        "Объекты чертежа",
        "Есть изменённые положения (жёлтые). Сохранить в исходные или загрузить из Excel?",
        dirty);
      _closePromptDone = true;
      if (action == DirtyCoordsAction.Cancel)
      {
        try { _pads.DiscardDirtyCoords(); }
        catch { }

        try { _buos.DiscardDirtyCoords(); }
        catch { }

        try { _nodes.DiscardDirtyCoords(); }
        catch { }

        try { _inserts.DiscardDirtyCoords(); }
        catch { }

        CadPadInteraction.ClearPending();
        return;
      }

      ApplyDirtyPromptAction(action);
    }

    private static Button MakeToolButton(Control parent, string text, Action click)
    {
      var button = new Button
      {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 0),
        Padding = new Padding(10, 4, 10, 4)
      };
      button.Click += (_, __) => click();
      parent.Controls.Add(button);
      return button;
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
