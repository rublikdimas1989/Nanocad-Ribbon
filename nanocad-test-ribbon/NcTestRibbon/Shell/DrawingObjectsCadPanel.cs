namespace NcTestRibbon.Shell
{
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows.Forms;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// One tab of «Объекты чертежа»: lists ONLY objects present on the drawing.
  /// No «Показывать на чертеже» — CAD is the source of truth for the list.
  /// </summary>
  public sealed class DrawingObjectsCadPanel : UserControl
  {
    public enum Kind
    {
      Pads,
      Buos,
      Nodes,
      Inserts
    }

    private readonly Kind _kind;
    private readonly BindingList<CadRow> _rows = new BindingList<CadRow>();
    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly Dictionary<string, PoseSnapshot> _excelBaseline =
      new Dictionary<string, PoseSnapshot>(StringComparer.OrdinalIgnoreCase);

    public DrawingObjectsCadPanel(Kind kind)
    {
      _kind = kind;
      Dock = DockStyle.Fill;

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 2,
        Padding = new Padding(4)
      };
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
      Controls.Add(root);

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
      _grid.Columns.Add(Col("Имя", nameof(CadRow.Name), 22));
      _grid.Columns.Add(Col("X", nameof(CadRow.XText), 14));
      _grid.Columns.Add(Col("Y", nameof(CadRow.YText), 14));
      if (kind != Kind.Inserts)
      {
        _grid.Columns.Add(Col("Угол", nameof(CadRow.AngleText), 12));
        _grid.Columns.Add(Col("Ширина", nameof(CadRow.WidthText), 12));
        _grid.Columns.Add(Col("Длина", nameof(CadRow.LengthText), 12));
      }

      if (kind == Kind.Buos || kind == Kind.Nodes)
      {
        _grid.Columns.Add(Col("Тип", nameof(CadRow.TypeName), 14));
      }

      _grid.CellFormatting += OnCellFormatting;
      root.Controls.Add(_grid, 0, 0);

      _status = new Label
      {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "На чертеже: 0"
      };
      root.Controls.Add(_status, 0, 1);

      // Do not auto-refresh on VisibleChanged — causes flicker when the parent
      // hides briefly for CAD point pick / multi-add. Parent refreshes explicitly.
    }

    /// <summary>Delete selected rows from the drawing and refresh the list.</summary>
    public int DeleteSelectedFromCad()
    {
      var names = new List<string>();
      foreach (DataGridViewRow viewRow in _grid.SelectedRows)
      {
        if (viewRow.DataBoundItem is CadRow row && !string.IsNullOrWhiteSpace(row.Name))
        {
          names.Add(row.Name);
        }
      }

      if (names.Count == 0 && _grid.CurrentRow?.DataBoundItem is CadRow current)
      {
        names.Add(current.Name);
      }

      if (names.Count == 0)
      {
        return 0;
      }

      switch (_kind)
      {
        case Kind.Buos:
          CadBuosPlacementOverlay.RemoveByNames(names);
          break;
        case Kind.Nodes:
          CadNodePlacementOverlay.RemoveByNames(names);
          break;
        case Kind.Inserts:
          CadInsertOverlay.RemoveByNames(names);
          break;
        default:
          CadPadOverlay.RemoveByNames(names);
          break;
      }

      RefreshFromCad();
      return names.Count;
    }

    public bool HasDirtyCoords => _rows.Any(r => r.CoordsDirty);

    public IEnumerable<DirtyCoordItem> EnumerateDirtyItems()
    {
      string group = GroupLabel();
      return _rows.Where(r => r.CoordsDirty).Select(r => new DirtyCoordItem
      {
        Group = group,
        Name = r.Name,
        Detail = "X=" + r.XText + "; Y=" + r.YText
          + (_kind == Kind.Inserts ? string.Empty : "; угол=" + r.AngleText)
      });
    }

    public void RefreshFromCad()
    {
      LoadExcelBaseline();
      var next = new List<CadRow>();
      switch (_kind)
      {
        case Kind.Buos:
          foreach (var g in CadBuosPlacementOverlay.ListPlacedSites())
          {
            next.Add(FromSite(g));
          }

          break;
        case Kind.Nodes:
          foreach (var g in CadNodePlacementOverlay.ListPlacedSites())
          {
            next.Add(FromSite(g));
          }

          break;
        case Kind.Inserts:
          foreach (var g in CadInsertOverlay.ListPlacedInserts())
          {
            next.Add(FromInsert(g));
          }

          break;
        default:
          foreach (var g in CadPadOverlay.ListPlacedPads())
          {
            next.Add(FromPad(g));
          }

          break;
      }

      ReplaceRowsInPlace(next);
      MarkDirtyAgainstBaseline();
      _status.Text = _rows.Count == 0
        ? "На чертеже нет объектов этого типа."
        : "На чертеже: " + _rows.Count;
      try { _grid.Invalidate(); }
      catch { }
    }

    /// <summary>
    /// Update BindingList without DataSource=null (avoids grid flicker / blink).
    /// </summary>
    private void ReplaceRowsInPlace(List<CadRow> next)
    {
      bool raise = _rows.RaiseListChangedEvents;
      _rows.RaiseListChangedEvents = false;
      try
      {
        _rows.Clear();
        foreach (CadRow row in next)
        {
          _rows.Add(row);
        }
      }
      finally
      {
        _rows.RaiseListChangedEvents = raise;
      }

      if (_grid.DataSource != _rows)
      {
        _grid.DataSource = _rows;
      }
      else
      {
        _rows.ResetBindings();
      }
    }

    public void SaveToSource(bool confirm, string confirmTitle)
    {
      // Flush pending KP Excel sync first — during drawing close CAD list may be empty.
      if (_kind == Kind.Pads && CadPadInteraction.HasPendingExcelSync)
      {
        CadPadInteraction.CommitPendingToExcel();
      }

      // Sync poses in place (no DataSource tear-down) so Save does not blink the window.
      SyncPosesFromCadInPlace();
      if (_rows.Count == 0)
      {
        return;
      }

      if (confirm)
      {
        DialogResult ok = MessageBox.Show(
          FindForm() ?? (IWin32Window)this,
          confirmTitle + "?",
          confirmTitle,
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }
      }

      switch (_kind)
      {
        case Kind.Buos:
          SaveBuos();
          break;
        case Kind.Nodes:
          SaveNodes();
          break;
        case Kind.Inserts:
          SaveInserts();
          break;
        default:
          SavePads();
          break;
      }

      // Accept the just-saved CAD poses as baseline and clear yellow immediately.
      AcceptSavedBaselineFromRows();
      if (_kind == Kind.Pads)
      {
        CadPadInteraction.ClearPending();
      }

      try { _grid.Invalidate(); }
      catch { }
    }

    /// <summary>Update X/Y/angle on existing rows from CAD; add/remove without rebinding.</summary>
    private void SyncPosesFromCadInPlace()
    {
      LoadExcelBaseline();
      var next = new List<CadRow>();
      switch (_kind)
      {
        case Kind.Buos:
          foreach (var g in CadBuosPlacementOverlay.ListPlacedSites())
          {
            next.Add(FromSite(g));
          }

          break;
        case Kind.Nodes:
          foreach (var g in CadNodePlacementOverlay.ListPlacedSites())
          {
            next.Add(FromSite(g));
          }

          break;
        case Kind.Inserts:
          foreach (var g in CadInsertOverlay.ListPlacedInserts())
          {
            next.Add(FromInsert(g));
          }

          break;
        default:
          foreach (var g in CadPadOverlay.ListPlacedPads())
          {
            next.Add(FromPad(g));
          }

          break;
      }

      // Merge into existing CadRow instances when names match — less UI churn.
      var byName = _rows.Where(r => !string.IsNullOrWhiteSpace(r.Name))
        .ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
      var merged = new List<CadRow>();
      foreach (CadRow n in next)
      {
        if (byName.TryGetValue(n.Name, out CadRow existing))
        {
          existing.X = n.X;
          existing.Y = n.Y;
          existing.AngleDeg = n.AngleDeg;
          existing.WidthM = n.WidthM;
          existing.LengthM = n.LengthM;
          existing.TypeName = n.TypeName;
          merged.Add(existing);
        }
        else
        {
          merged.Add(n);
        }
      }

      ReplaceRowsInPlace(merged);
      MarkDirtyAgainstBaseline();
    }

    /// <summary>After Excel write: treat current CAD rows as clean (no yellow).</summary>
    private void AcceptSavedBaselineFromRows()
    {
      _excelBaseline.Clear();
      foreach (CadRow row in _rows)
      {
        _excelBaseline[row.Name] = new PoseSnapshot(row.X, row.Y, row.AngleDeg);
        row.CoordsDirty = false;
      }
    }

    /// <summary>After close-dialog Save: drop yellow/pending state without touching CAD.</summary>
    public void ClearDirtyFlags()
    {
      foreach (CadRow row in _rows)
      {
        row.CoordsDirty = false;
      }

      try { _grid.Invalidate(); }
      catch { }
    }

    public void LoadFromSource(bool confirm, string confirmTitle)
    {
      if (confirm)
      {
        DialogResult ok = MessageBox.Show(
          FindForm() ?? (IWin32Window)this,
          confirmTitle + " и обновить объекты на чертеже?",
          confirmTitle,
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }
      }

      RefreshFromCad();
      if (_rows.Count == 0)
      {
        return;
      }

      switch (_kind)
      {
        case Kind.Buos:
          LoadBuosOntoCad();
          break;
        case Kind.Nodes:
          LoadNodesOntoCad();
          break;
        case Kind.Inserts:
          LoadInsertsOntoCad();
          break;
        default:
          LoadPadsOntoCad();
          break;
      }

      RefreshFromCad();
    }

    public void DiscardDirtyCoords()
    {
      try { LoadFromSource(confirm: false, confirmTitle: string.Empty); }
      catch { }
    }

    private void SavePads()
    {
      string path = AppPaths.Load().PadsWorkbook;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException("Нет файла КП (padsWorkbook) для записи.");
      }

      var dirty = _rows.Select(r => new PadWorkbookLoader.PadRecord
      {
        Name = r.Name,
        X = r.X,
        Y = r.Y,
        RotationDeg = r.AngleDeg,
        WidthM = r.WidthM,
        LengthM = r.LengthM
      }).ToList();
      PadWorkbookLoader.SavePadCoordinates(path, dirty);
      foreach (CadRow row in _rows)
      {
        row.CoordsDirty = false;
      }
    }

    private void SaveBuos()
    {
      string path = EnsurePlacementPath(buos: true);
      var dirty = _rows.Select(r => new SitePlacementWorkbookLoader.PlacementRecord
      {
        Name = r.Name,
        TypeName = r.TypeName ?? string.Empty,
        X = r.X,
        Y = r.Y,
        AngleDeg = r.AngleDeg,
        WidthM = r.WidthM,
        LengthM = r.LengthM
      }).ToList();
      int n = SitePlacementWorkbookLoader.SavePlacementPose(
        path, SitePlacementWorkbookLoader.BuosSheetName, dirty);
      if (n <= 0)
      {
        throw new InvalidOperationException(
          "Не записано ни одной строки в «" + SitePlacementWorkbookLoader.BuosSheetName + "».");
      }

      foreach (CadRow row in _rows)
      {
        row.CoordsDirty = false;
      }
    }

    private void SaveNodes()
    {
      string path = EnsurePlacementPath(buos: false);
      var dirty = _rows.Select(r => new SitePlacementWorkbookLoader.PlacementRecord
      {
        Name = r.Name,
        TypeName = r.TypeName ?? string.Empty,
        X = r.X,
        Y = r.Y,
        AngleDeg = r.AngleDeg,
        WidthM = r.WidthM,
        LengthM = r.LengthM
      }).ToList();
      int n = SitePlacementWorkbookLoader.SavePlacementPose(
        path, SitePlacementWorkbookLoader.NodesSheetName, dirty);
      if (n <= 0)
      {
        throw new InvalidOperationException(
          "Не записано ни одной строки в «" + SitePlacementWorkbookLoader.NodesSheetName + "».");
      }

      foreach (CadRow row in _rows)
      {
        row.CoordsDirty = false;
      }
    }

    private void SaveInserts()
    {
      string path = AppPaths.Load().InsertsWorkbook;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException("Нет файла врезок (insertsWorkbook) для записи.");
      }

      var dirty = _rows.Select(r => new InsertWorkbookLoader.InsertRecord
      {
        Name = r.Name,
        X = r.X,
        Y = r.Y
      }).ToList();
      InsertWorkbookLoader.SaveInsertCoordinates(path, dirty);
      foreach (CadRow row in _rows)
      {
        row.CoordsDirty = false;
      }
    }

    private void LoadPadsOntoCad()
    {
      string path = AppPaths.Load().PadsWorkbook;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException("Нет файла КП для загрузки.");
      }

      PadWorkbookLoader.WorkbookData book = PadWorkbookLoader.LoadWorkbook(path);
      foreach (CadRow row in _rows.ToList())
      {
        PadWorkbookLoader.PadRecord rec = book.Pads.Find(p =>
          string.Equals(p.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (rec == null)
        {
          continue;
        }

        CadPadOverlay.UpdatePad(new CadPadOverlay.PadGeom
        {
          Name = rec.Name,
          X = rec.X,
          Y = rec.Y,
          RotationDeg = rec.RotationDeg,
          WidthM = rec.WidthM > 0 ? rec.WidthM : row.WidthM,
          LengthM = rec.LengthM > 0 ? rec.LengthM : row.LengthM
        });
      }

      CadPadInteraction.ClearPending();
    }

    private void LoadBuosOntoCad()
    {
      string path = EnsurePlacementPath(buos: true);
      var book = SitePlacementWorkbookLoader.LoadWorkbook(path, SitePlacementWorkbookLoader.BuosSheetName);
      foreach (CadRow row in _rows.ToList())
      {
        SitePlacementWorkbookLoader.PlacementRecord rec = book.Sites.Find(s =>
          string.Equals(s.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (rec == null)
        {
          continue;
        }

        CadBuosPlacementOverlay.UpdatePad(new CadBuosPlacementOverlay.SiteGeom
        {
          Name = rec.Name,
          TypeName = rec.TypeName,
          X = rec.X,
          Y = rec.Y,
          RotationDeg = rec.AngleDeg,
          WidthM = rec.WidthM > 0 ? rec.WidthM : row.WidthM,
          LengthM = rec.LengthM > 0 ? rec.LengthM : row.LengthM
        });
      }
    }

    private void LoadNodesOntoCad()
    {
      string path = EnsurePlacementPath(buos: false);
      var book = SitePlacementWorkbookLoader.LoadWorkbook(path, SitePlacementWorkbookLoader.NodesSheetName);
      foreach (CadRow row in _rows.ToList())
      {
        SitePlacementWorkbookLoader.PlacementRecord rec = book.Sites.Find(s =>
          string.Equals(s.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (rec == null)
        {
          continue;
        }

        CadNodePlacementOverlay.UpdatePad(new CadNodePlacementOverlay.SiteGeom
        {
          Name = rec.Name,
          TypeName = rec.TypeName,
          X = rec.X,
          Y = rec.Y,
          RotationDeg = rec.AngleDeg,
          WidthM = rec.WidthM > 0 ? rec.WidthM : row.WidthM,
          LengthM = rec.LengthM > 0 ? rec.LengthM : row.LengthM
        });
      }
    }

    private void LoadInsertsOntoCad()
    {
      string path = AppPaths.Load().InsertsWorkbook;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException("Нет файла врезок.");
      }

      var book = InsertWorkbookLoader.LoadWorkbook(path);
      foreach (CadRow row in _rows.ToList())
      {
        InsertWorkbookLoader.InsertRecord rec = book.Inserts.Find(i =>
          string.Equals(i.Name, row.Name, StringComparison.OrdinalIgnoreCase));
        if (rec == null)
        {
          continue;
        }

        CadInsertOverlay.UpdateInsert(new CadInsertOverlay.InsertGeom
        {
          Name = rec.Name,
          X = rec.X,
          Y = rec.Y
        });
      }
    }

    private string EnsurePlacementPath(bool buos)
    {
      return PlacementWorkbookPaths.Ensure(FindForm(), buos);
    }

    private void LoadExcelBaseline()
    {
      _excelBaseline.Clear();
      try
      {
        switch (_kind)
        {
          case Kind.Buos:
            {
              string path = AppPaths.Load().BuosPlacementWorkbook;
              if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
              {
                return;
              }

              // Create «Размещение БУОС» if user pointed a catalog workbook.
              SitePlacementWorkbookLoader.EnsurePlacementSheet(
                path, SitePlacementWorkbookLoader.BuosSheetName);
              foreach (var s in SitePlacementWorkbookLoader.LoadWorkbook(
                path, SitePlacementWorkbookLoader.BuosSheetName).Sites)
              {
                _excelBaseline[s.Name] = new PoseSnapshot(s.X, s.Y, s.AngleDeg);
              }

              break;
            }

          case Kind.Nodes:
            {
              string path = AppPaths.Load().NodesPlacementWorkbook;
              if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
              {
                return;
              }

              SitePlacementWorkbookLoader.EnsurePlacementSheet(
                path, SitePlacementWorkbookLoader.NodesSheetName);
              foreach (var s in SitePlacementWorkbookLoader.LoadWorkbook(
                path, SitePlacementWorkbookLoader.NodesSheetName).Sites)
              {
                _excelBaseline[s.Name] = new PoseSnapshot(s.X, s.Y, s.AngleDeg);
              }

              break;
            }

          case Kind.Inserts:
            {
              string path = AppPaths.Load().InsertsWorkbook;
              if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
              {
                return;
              }

              foreach (var s in InsertWorkbookLoader.LoadWorkbook(path).Inserts)
              {
                _excelBaseline[s.Name] = new PoseSnapshot(s.X, s.Y, 0);
              }

              break;
            }

          default:
            {
              string path = AppPaths.Load().PadsWorkbook;
              if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
              {
                return;
              }

              foreach (var s in PadWorkbookLoader.LoadWorkbook(path).Pads)
              {
                _excelBaseline[s.Name] = new PoseSnapshot(s.X, s.Y, s.RotationDeg);
              }

              break;
            }
        }
      }
      catch
      {
      }
    }

    private void MarkDirtyAgainstBaseline()
    {
      foreach (CadRow row in _rows)
      {
        if (_kind == Kind.Pads && CadPadInteraction.PendingPadNames.Contains(row.Name))
        {
          row.CoordsDirty = true;
          continue;
        }

        if (!_excelBaseline.TryGetValue(row.Name, out PoseSnapshot basePose))
        {
          // On drawing but not in Excel yet → treat as dirty until saved.
          row.CoordsDirty = true;
          continue;
        }

        row.CoordsDirty = !Near(row.X, basePose.X) || !Near(row.Y, basePose.Y)
          || (_kind != Kind.Inserts && !Near(row.AngleDeg, basePose.Angle));
      }
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-4;

    private CadRow FromPad(CadPadOverlay.PadGeom g)
    {
      return new CadRow
      {
        Name = g.Name,
        X = g.X,
        Y = g.Y,
        AngleDeg = g.RotationDeg,
        WidthM = g.WidthM,
        LengthM = g.LengthM
      };
    }

    private CadRow FromSite(CadBuosPlacementOverlay.SiteGeom g)
    {
      return new CadRow
      {
        Name = g.Name,
        TypeName = g.TypeName,
        X = g.X,
        Y = g.Y,
        AngleDeg = g.RotationDeg,
        WidthM = g.WidthM,
        LengthM = g.LengthM
      };
    }

    private CadRow FromSite(CadNodePlacementOverlay.SiteGeom g)
    {
      return new CadRow
      {
        Name = g.Name,
        TypeName = g.TypeName,
        X = g.X,
        Y = g.Y,
        AngleDeg = g.RotationDeg,
        WidthM = g.WidthM,
        LengthM = g.LengthM
      };
    }

    private CadRow FromInsert(CadInsertOverlay.InsertGeom g)
    {
      return new CadRow
      {
        Name = g.Name,
        X = g.X,
        Y = g.Y
      };
    }

    private string GroupLabel()
    {
      return _kind switch
      {
        Kind.Buos => "БУОС",
        Kind.Nodes => "Узлы",
        Kind.Inserts => "Врезки",
        _ => "Кустовые"
      };
    }

    private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
    {
      if (e.RowIndex < 0 || e.RowIndex >= _rows.Count)
      {
        return;
      }

      // Always set both states so yellow clears after Save without rebinding.
      e.CellStyle.BackColor = _rows[e.RowIndex].CoordsDirty
        ? Color.LightYellow
        : _grid.DefaultCellStyle.BackColor;
    }

    private static DataGridViewTextBoxColumn Col(string header, string prop, float weight)
    {
      return new DataGridViewTextBoxColumn
      {
        HeaderText = header,
        DataPropertyName = prop,
        FillWeight = weight,
        ReadOnly = true
      };
    }

    private sealed class PoseSnapshot
    {
      public PoseSnapshot(double x, double y, double angle)
      {
        X = x;
        Y = y;
        Angle = angle;
      }

      public double X { get; }
      public double Y { get; }
      public double Angle { get; }
    }

    private sealed class CadRow
    {
      public string Name { get; set; }
      public string TypeName { get; set; }
      public double X { get; set; }
      public double Y { get; set; }
      public double AngleDeg { get; set; }
      public double WidthM { get; set; }
      public double LengthM { get; set; }
      public bool CoordsDirty { get; set; }

      public string XText => X.ToString("0.###", CultureInfo.CurrentCulture);
      public string YText => Y.ToString("0.###", CultureInfo.CurrentCulture);
      public string AngleText => AngleDeg.ToString("0.###", CultureInfo.CurrentCulture);
      public string WidthText => WidthM.ToString("0.###", CultureInfo.CurrentCulture);
      public string LengthText => LengthM.ToString("0.###", CultureInfo.CurrentCulture);
    }
  }
}
