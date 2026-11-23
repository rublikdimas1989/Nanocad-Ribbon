namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows.Forms;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Ribbon «Добавить Кустовую/БУОС/Узел»: Excel multi-select → insert at stored X/Y/angle
  /// (no place jig). Zoom once to the whole inserted set. БУОС/Узел from catalog: pick point
  /// on current view (no zoom). No SourceData / no draw-all / no per-item window refresh.
  /// </summary>
  public static class CadPlaceFromExcel
  {
    public static void PlacePad(IWin32Window owner = null)
    {
      owner ??= TryMainWindow();
      string initial = AppPaths.Load().PadsWorkbook;
      IList<PickFromExcelItem> picked = PickFromExcelDialog.ShowPickMany(
        owner,
        "Добавить Кустовую",
        initial,
        "выбрать из файла",
        path => PadWorkbookLoader.LoadWorkbook(path).Pads.Select(ToPickItem).ToList(),
        out string path);

      if (picked == null || picked.Count == 0 || string.IsNullOrWhiteSpace(path))
      {
        return;
      }

      PadWorkbookLoader.WorkbookData book = PadWorkbookLoader.LoadWorkbook(path);
      AppPaths.Set(p => p.PadsWorkbook = path);

      var geoms = new List<CadPadOverlay.PadGeom>();
      foreach (PickFromExcelItem item in picked)
      {
        string name = item.Tag as string ?? item.Name;
        PadWorkbookLoader.PadRecord pad = book.Pads.Find(p =>
          string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (pad == null)
        {
          throw new InvalidOperationException("КП «" + name + "» нет в " + Path.GetFileName(path));
        }

        geoms.Add(new CadPadOverlay.PadGeom
        {
          Name = pad.Name,
          X = pad.X,
          Y = pad.Y,
          WidthM = pad.WidthM > 0 ? pad.WidthM : 150,
          LengthM = pad.LengthM > 0 ? pad.LengthM : 250,
          RotationDeg = pad.RotationDeg
        });
      }

      // Draw all first, then one zoom to the added set (anti-flicker).
      CadPadOverlay.InsertManyAtStored(geoms);
      CadPadOverlay.ZoomToPads(geoms);
    }

    public static void PlaceBuos(IWin32Window owner = null)
    {
      PlaceBuosFromCatalog(owner);
    }

    public static void PlaceNode(IWin32Window owner = null)
    {
      PlaceNodeFromCatalog(owner);
    }

    /// <summary>
    /// Add БУОС from Типовые: empty editable «Имя», then pick insert point on current view (no zoom).
    /// </summary>
    public static void PlaceBuosFromCatalog(IWin32Window owner = null)
    {
      owner ??= TryMainWindow();
      PickFromTypicalCatalogDialog.CatalogPickRow pick =
        PickFromTypicalCatalogDialog.ShowPick(owner, buos: true);
      if (pick == null)
      {
        return;
      }

      if (!EnsureUniqueSiteName(owner, pick.InstanceName, buos: true))
      {
        return;
      }

      var geom = new CadBuosPlacementOverlay.SiteGeom
      {
        Name = pick.InstanceName,
        X = 0,
        Y = 0,
        RotationDeg = 0,
        WidthM = pick.WidthValue > 0 ? pick.WidthValue : 150,
        LengthM = pick.LengthValue > 0 ? pick.LengthValue : 250
      };

      using (HideOwnerForCadPick(owner))
      {
        if (!CadBuosPlacementOverlay.TryPlaceOnCurrentView(geom, out double x, out double y, zoomFirst: false))
        {
          return;
        }

        geom.X = x;
        geom.Y = y;
      }

      // Re-check after pick — another instance may have been placed meanwhile.
      if (!EnsureUniqueSiteName(owner, geom.Name, buos: true))
      {
        return;
      }

      CadBuosPlacementOverlay.InsertAtStored(geom);
      // No zoom-to — stay on current view.
    }

    /// <summary>
    /// Add Узел from Типовые: empty editable «Имя», then pick insert point on current view (no zoom).
    /// </summary>
    public static void PlaceNodeFromCatalog(IWin32Window owner = null)
    {
      owner ??= TryMainWindow();
      PickFromTypicalCatalogDialog.CatalogPickRow pick =
        PickFromTypicalCatalogDialog.ShowPick(owner, buos: false);
      if (pick == null)
      {
        return;
      }

      if (!EnsureUniqueSiteName(owner, pick.InstanceName, buos: false))
      {
        return;
      }

      var geom = new CadNodePlacementOverlay.SiteGeom
      {
        Name = pick.InstanceName,
        X = 0,
        Y = 0,
        RotationDeg = 0,
        WidthM = pick.WidthValue > 0 ? pick.WidthValue : 20,
        LengthM = pick.LengthValue > 0 ? pick.LengthValue : 20
      };

      using (HideOwnerForCadPick(owner))
      {
        if (!CadNodePlacementOverlay.TryPlaceOnCurrentView(geom, out double x, out double y, zoomFirst: false))
        {
          return;
        }

        geom.X = x;
        geom.Y = y;
      }

      if (!EnsureUniqueSiteName(owner, geom.Name, buos: false))
      {
        return;
      }

      CadNodePlacementOverlay.InsertAtStored(geom);
    }

    /// <summary>
    /// Require non-empty unique instance name among placed БУОС or Узлы. Warn + cancel on conflict.
    /// </summary>
    private static bool EnsureUniqueSiteName(IWin32Window owner, string name, bool buos)
    {
      string kind = buos ? "БУОС" : "Узел";
      if (string.IsNullOrWhiteSpace(name))
      {
        MessageBox.Show(
          owner,
          "Укажите «Имя» экземпляра " + kind + ".",
          kind,
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
        return false;
      }

      string trimmed = name.Trim();
      bool duplicate = buos
        ? CadBuosPlacementOverlay.ListPlacedSites()
          .Any(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase))
        : CadNodePlacementOverlay.ListPlacedSites()
          .Any(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
      if (!duplicate)
      {
        return true;
      }

      MessageBox.Show(
        owner,
        kind + " с именем «" + trimmed + "» уже есть на чертеже. Укажите уникальное «Имя».",
        kind,
        MessageBoxButtons.OK,
        MessageBoxIcon.Warning);
      return false;
    }

    public static void PlaceInsert(IWin32Window owner = null)
    {
      owner ??= TryMainWindow();
      string initial = EnsurePlacementWorkbookDialog.EnsureInsertPath(
        owner,
        AppPaths.Load().InsertsWorkbook,
        path => AppPaths.Set(p => p.InsertsWorkbook = path));
      if (string.IsNullOrWhiteSpace(initial))
      {
        return;
      }

      IList<PickFromExcelItem> picked = PickFromExcelDialog.ShowPickMany(
        owner,
        "Добавить врезки",
        initial,
        "выбрать из файла",
        path => InsertWorkbookLoader.LoadWorkbook(path).Inserts.Select(ToPickItem).ToList(),
        out string path);

      if (picked == null || picked.Count == 0 || string.IsNullOrWhiteSpace(path))
      {
        return;
      }

      InsertWorkbookLoader.WorkbookData book = InsertWorkbookLoader.LoadWorkbook(path);
      AppPaths.Set(p => p.InsertsWorkbook = path);

      var geoms = new List<CadInsertOverlay.InsertGeom>();
      foreach (PickFromExcelItem item in picked)
      {
        string name = item.Tag as string ?? item.Name;
        InsertWorkbookLoader.InsertRecord rec = book.Inserts.Find(i =>
          string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if (rec == null)
        {
          throw new InvalidOperationException("Врезка «" + name + "» нет в " + Path.GetFileName(path));
        }

        geoms.Add(new CadInsertOverlay.InsertGeom
        {
          Name = rec.Name,
          X = rec.X,
          Y = rec.Y
        });
      }

      CadInsertOverlay.InsertManyAtStored(geoms);
      CadInsertOverlay.ZoomToInserts(geoms);
    }

    private static IDisposable HideOwnerForCadPick(IWin32Window owner)
    {
      Form form = owner as Form;
      if (form == null || !form.Visible)
      {
        return NullScope.Instance;
      }

      // Do not Hide/Opacity=0 — that flickers «Объекты чертежа». Only disable input.
      bool wasEnabled = form.Enabled;
      try { form.Enabled = false; }
      catch { }

      return new RestoreFormScope(form, wasEnabled);
    }

    private static PickFromExcelItem ToPickItem(PadWorkbookLoader.PadRecord p)
    {
      bool has = Math.Abs(p.X) > 1e-9 || Math.Abs(p.Y) > 1e-9;
      return new PickFromExcelItem
      {
        Name = p.Name,
        HasCoords = has,
        Detail = has
          ? "X=" + p.X.ToString("0.###", CultureInfo.CurrentCulture)
            + "; Y=" + p.Y.ToString("0.###", CultureInfo.CurrentCulture)
            + "; угол=" + p.RotationDeg.ToString("0.###", CultureInfo.CurrentCulture) + "°"
          : "координаты не заданы (вставка в 0;0)",
        Tag = p.Name
      };
    }

    private static PickFromExcelItem ToPickItem(InsertWorkbookLoader.InsertRecord s)
    {
      bool has = Math.Abs(s.X) > 1e-9 || Math.Abs(s.Y) > 1e-9;
      return new PickFromExcelItem
      {
        Name = s.Name,
        HasCoords = has,
        Detail = has
          ? "X=" + s.X.ToString("0.###", CultureInfo.CurrentCulture)
            + "; Y=" + s.Y.ToString("0.###", CultureInfo.CurrentCulture)
          : "координаты не заданы (вставка в 0;0)",
        Tag = s.Name
      };
    }

    private static IWin32Window TryMainWindow()
    {
      try
      {
        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd != IntPtr.Zero)
        {
          return new Win32Window(hwnd);
        }
      }
      catch
      {
      }

      return null;
    }

    private sealed class Win32Window : IWin32Window
    {
      public Win32Window(IntPtr handle)
      {
        Handle = handle;
      }

      public IntPtr Handle { get; }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new NullScope();
      public void Dispose() { }
    }

    private sealed class RestoreFormScope : IDisposable
    {
      private readonly Form _form;
      private readonly bool _wasEnabled;
      private bool _done;

      public RestoreFormScope(Form form, bool wasEnabled)
      {
        _form = form;
        _wasEnabled = wasEnabled;
      }

      public void Dispose()
      {
        if (_done)
        {
          return;
        }

        _done = true;
        try
        {
          _form.Enabled = _wasEnabled;
          // No Show/BringToFront — window never left; avoids blink.
        }
        catch
        {
        }
      }
    }
  }
}
