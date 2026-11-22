namespace NcTestRibbon.Cad
{
  using System;
  using System.Diagnostics;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Windows.Forms;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;
  using NcTestRibbon.Shell;

  /// <summary>
  /// Ribbon «Добавить Кустовую/БУОС/Узел»: Excel pick → place ONLY the selected object.
  /// Does not open SourceData and does not redraw the full set.
  /// </summary>
  public static class CadPlaceFromExcel
  {
    public static void PlacePad(IWin32Window owner = null)
    {
      owner ??= TryMainWindow();
      string initial = AppPaths.Load().PadsWorkbook;
      PickFromExcelItem picked = PickFromExcelDialog.ShowPick(
        owner,
        "Добавить Кустовую",
        initial,
        "выбрать из файла",
        path => PadWorkbookLoader.LoadWorkbook(path).Pads.Select(ToPickItem).ToList(),
        out string path);

      if (picked == null || string.IsNullOrWhiteSpace(path))
      {
        return;
      }

      PadWorkbookLoader.WorkbookData book = PadWorkbookLoader.LoadWorkbook(path);
      string name = picked.Tag as string ?? picked.Name;
      PadWorkbookLoader.PadRecord pad = book.Pads.Find(p =>
        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
      if (pad == null)
      {
        throw new InvalidOperationException("КП «" + name + "» нет в " + Path.GetFileName(path));
      }

      AppPaths.Set(p => p.PadsWorkbook = path);

      var geom = new CadPadOverlay.PadGeom
      {
        Name = pad.Name,
        X = pad.X,
        Y = pad.Y,
        WidthM = pad.WidthM > 0 ? pad.WidthM : 150,
        LengthM = pad.LengthM > 0 ? pad.LengthM : 250,
        RotationDeg = pad.RotationDeg
      };

      if (!CadPadOverlay.TryPlace(geom, out double x, out double y))
      {
        return;
      }

      pad.X = x;
      pad.Y = y;
      geom.X = x;
      geom.Y = y;
      CadPadOverlay.UpdatePad(geom);
      PadWorkbookLoader.SavePadCoordinates(path, new[] { pad });
    }

    public static void PlaceBuos(IWin32Window owner = null)
    {
      PlaceSite(
        owner,
        "Добавить БУОС",
        SitePlacementWorkbookLoader.BuosSheetName,
        () => AppPaths.Load().BuosPlacementWorkbook,
        path => AppPaths.Set(p => p.BuosPlacementWorkbook = path),
        buos: true);
    }

    public static void PlaceNode(IWin32Window owner = null)
    {
      PlaceSite(
        owner,
        "Добавить Узел",
        SitePlacementWorkbookLoader.NodesSheetName,
        () => AppPaths.Load().NodesPlacementWorkbook,
        path => AppPaths.Set(p => p.NodesPlacementWorkbook = path),
        buos: false);
    }

    private static void PlaceSite(
      IWin32Window owner,
      string title,
      string sheetName,
      Func<string> getInitialPath,
      Action<string> rememberPath,
      bool buos)
    {
      owner ??= TryMainWindow();
      PickFromExcelItem picked = PickFromExcelDialog.ShowPick(
        owner,
        title,
        getInitialPath(),
        "выбрать из файла",
        path => SitePlacementWorkbookLoader.LoadWorkbook(path, sheetName).Sites.Select(ToPickItem).ToList(),
        out string path);

      if (picked == null || string.IsNullOrWhiteSpace(path))
      {
        return;
      }

      SitePlacementWorkbookLoader.WorkbookData book =
        SitePlacementWorkbookLoader.LoadWorkbook(path, sheetName);
      string name = picked.Tag as string ?? picked.Name;
      SitePlacementWorkbookLoader.PlacementRecord site = book.Sites.Find(s =>
        string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
      if (site == null)
      {
        throw new InvalidOperationException(
          "«" + name + "» нет в " + Path.GetFileName(path) + " («" + sheetName + "»).");
      }

      rememberPath(path);

      double x;
      double y;
      if (buos)
      {
        var geom = new CadBuosPlacementOverlay.SiteGeom
        {
          Name = site.Name,
          X = site.X,
          Y = site.Y,
          WidthM = site.WidthM > 0 ? site.WidthM : 150,
          LengthM = site.LengthM > 0 ? site.LengthM : 250,
          RotationDeg = site.AngleDeg
        };
        if (!CadBuosPlacementOverlay.TryPlace(geom, out x, out y))
        {
          return;
        }

        geom.X = x;
        geom.Y = y;
        CadBuosPlacementOverlay.UpdatePad(geom);
      }
      else
      {
        var geom = new CadNodePlacementOverlay.SiteGeom
        {
          Name = site.Name,
          X = site.X,
          Y = site.Y,
          WidthM = site.WidthM > 0 ? site.WidthM : 150,
          LengthM = site.LengthM > 0 ? site.LengthM : 250,
          RotationDeg = site.AngleDeg
        };
        if (!CadNodePlacementOverlay.TryPlace(geom, out x, out y))
        {
          return;
        }

        geom.X = x;
        geom.Y = y;
        CadNodePlacementOverlay.UpdatePad(geom);
      }

      site.X = x;
      site.Y = y;
      SitePlacementWorkbookLoader.SavePlacementPose(path, sheetName, new[] { site });
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
          : "координаты не заданы",
        Tag = p.Name
      };
    }

    private static PickFromExcelItem ToPickItem(SitePlacementWorkbookLoader.PlacementRecord s)
    {
      bool has = Math.Abs(s.X) > 1e-9 || Math.Abs(s.Y) > 1e-9;
      return new PickFromExcelItem
      {
        Name = s.Name,
        HasCoords = has,
        Detail = has
          ? "X=" + s.X.ToString("0.###", CultureInfo.CurrentCulture)
            + "; Y=" + s.Y.ToString("0.###", CultureInfo.CurrentCulture)
            + "; угол=" + s.AngleDeg.ToString("0.###", CultureInfo.CurrentCulture) + "°"
          : "координаты не заданы",
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
  }
}
