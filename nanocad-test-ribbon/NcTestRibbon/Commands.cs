namespace NcTestRibbon
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Windows.Forms;
  using System.Windows.Forms.Integration;
  using System.Windows.Interop;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;
  using NcTestRibbon.Shell;
  using Teigha.DatabaseServices;
  using Teigha.Runtime;
  using CadApp = HostMgd.ApplicationServices.Application;
  using WinFormsApp = System.Windows.Forms.Application;
  using WinFormsIWin32Window = System.Windows.Forms.IWin32Window;
  using WpfApp = System.Windows.Application;
  using WpfWindow = System.Windows.Window;

  public class TestCommands
  {
    private const string DefaultHttpUrl = "https://httpbin.org/get";
    private static bool _visualStylesTried;
    private static bool _wpfAppCreated;
    private static SourceDataWindow _sourceDataLive;
    private static CartographyWindow _cartographyLive;
    private static AllowableSitesWindow _allowableSitesLive;
    private static bool _bridgeWired;
    private static readonly object SourceDataGate = new object();
    private static bool _sourceDataOpening;
    private static long _lastSourceDataOpenTicks;

    static TestCommands()
    {
      WireCadBridge();
    }

    /// <summary>CAD double-click / MOVE sync → UI + Excel.</summary>
    internal static void WireCadBridge()
    {
      if (_bridgeWired)
      {
        return;
      }

      TestCommandsBridge.OpenPadsWindow = OpenPadsWindowStatic;
      TestCommandsBridge.ApplyPadCadPose = ApplyPadCadPoseStatic;
      DrawingDirtyGuard.GetLiveSourceData = () =>
        _sourceDataLive != null && !_sourceDataLive.IsDisposed ? _sourceDataLive : null;
      try { DrawingDirtyGuard.TryAttach(); }
      catch { }

      _bridgeWired = true;
    }

    private static void OpenPadsWindowStatic(string padName)
    {
      var cmd = new TestCommands();
      cmd.OpenSourceDataForm(SourceDataTab.Pads, "NcTestPadDblClick");
      lock (SourceDataGate)
      {
        if (_sourceDataLive != null && !_sourceDataLive.IsDisposed && !string.IsNullOrWhiteSpace(padName))
        {
          _sourceDataLive.SelectPadByName(padName);
        }
      }
    }

    private static void ApplyPadCadPoseStatic(
      string name, double x, double y, double rotationDeg, bool saveExcel)
    {
      if (_sourceDataLive != null && !_sourceDataLive.IsDisposed)
      {
        try
        {
          _sourceDataLive.ApplyPadCadPose(name, x, y, rotationDeg, saveExcel);
          return;
        }
        catch
        {
          // Fall through to Excel-only path when KP not loaded in the open window.
        }
      }

      if (!saveExcel)
      {
        return;
      }

      string path = AppPaths.Load().PadsWorkbook;
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException(
          "Нет пути к таблицам КП (paths.json → padsWorkbook). Откройте КП во «Исходные данные».");
      }

      PadWorkbookLoader.WorkbookData book = PadWorkbookLoader.LoadWorkbook(path);
      PadWorkbookLoader.PadRecord pad = book.Pads.Find(p =>
        string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
      if (pad == null)
      {
        throw new InvalidOperationException("КП «" + name + "» нет в " + Path.GetFileName(path));
      }

      pad.X = x;
      pad.Y = y;
      pad.RotationDeg = CadPadOverlay.NormalizeDegrees(rotationDeg);
      PadWorkbookLoader.SavePadCoordinates(path, new[] { pad });

      // Redraw label + rectangle at committed pose.
      CadPadOverlay.UpdatePad(new CadPadOverlay.PadGeom
      {
        Name = pad.Name,
        X = pad.X,
        Y = pad.Y,
        WidthM = pad.WidthM > 0 ? pad.WidthM : 150,
        LengthM = pad.LengthM > 0 ? pad.LengthM : 250,
        RotationDeg = pad.RotationDeg
      });
    }

    /// <summary>Manual: open pads window for implied/selected pad (fallback without BeginDoubleClick).</summary>
    [CommandMethod("NcTestPadOpen")]
    public void NcTestPadOpen()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        string name = null;
        PromptSelectionResult sel = ed?.SelectImplied();
        if (sel != null && sel.Status == PromptStatus.OK && sel.Value != null)
        {
          ObjectId[] ids = sel.Value.GetObjectIds();
          using (doc.LockDocument())
          using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
          {
            foreach (ObjectId id in ids ?? Array.Empty<ObjectId>())
            {
              var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
              if (ent == null
                  || !string.Equals(ent.Layer, CadPadOverlay.LayerName, StringComparison.OrdinalIgnoreCase))
              {
                continue;
              }

              name = CadPadOverlay.ReadPadNamePublic(ent);
              if (string.IsNullOrWhiteSpace(name) && ent is DBText text)
              {
                name = text.TextString;
              }

              if (!string.IsNullOrWhiteSpace(name))
              {
                break;
              }
            }

            tr.Commit();
          }
        }

        OpenPadsWindowStatic(name);
        ed?.WriteMessage("\nNcTestPadOpen: Исходные данные → Кустовые"
          + (string.IsNullOrWhiteSpace(name) ? "." : " («" + name + "»)."));
      }
      catch (System.Exception ex)
      {
        WriteException(ed, "NcTestPadOpen", ex);
      }
    }

    [CommandMethod("NcTestHello")]
    public void NcTestHello()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;

      const string text = "Тест 1: Hello";
      ed?.WriteMessage("\n" + text);
      CadApp.ShowAlertDialog(text);
    }

    [CommandMethod("NcTestHttp")]
    public void NcTestHttp()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;

      using (HttpTestForm form = new HttpTestForm(DefaultHttpUrl))
      {
        if (form.ShowDialog() != DialogResult.OK)
        {
          ed?.WriteMessage("\nNcTestHttp: отменено.");
          return;
        }

        string url = form.Url;
        ed?.WriteMessage("\nNcTestHttp: GET " + url);

        try
        {
          string body = FetchUrl(url);
          string preview = Truncate(body, 2000);
          ed?.WriteMessage("\nNcTestHttp: OK, длина ответа "
            + body.Length + " символов.");
          CadApp.ShowAlertDialog(
            "NcTestHttp — успех\n\nURL: " + url
            + "\n\nОтвет (обрезан):\n" + preview);
        }
        catch (System.Exception ex)
        {
          ed?.WriteMessage("\nNcTestHttp: ошибка — " + ex.Message);
          MessageBox.Show(
            "Не удалось выполнить HTTP GET.\n\nURL: " + url
            + "\n\n" + ex.Message,
            "NcTestHttp",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
      }
    }

    /// <summary>
    /// Opens End-variant MainWindow WPF shell (UI only; logic stubbed).
    /// </summary>
    [CommandMethod("NcTestWpf")]
    public void NcTestWpf()
    {
      OpenWpfWindow(ed: null, "NcTestWpf", () => new MainWindow());
    }

    /// <summary>Opens Settings WPF shell (tracing + trench/road tabs).</summary>
    [CommandMethod("NcTestSettings")]
    public void NcTestSettings()
    {
      OpenWpfWindow(ed: null, "NcTestSettings", () => new SettingsWindow());
    }

    /// <summary>Alias for NcTestSettings (legacy name).</summary>
    [CommandMethod("NcTestInitialData")]
    public void NcTestInitialData()
    {
      NcTestSourceData();
    }

    /// <summary>
    /// «Исходные данные» — Кустовые / Врезки / placement БУОС / Узлы (default: pads).
    /// </summary>
    [CommandMethod("NcTestSourceData")]
    public void NcTestSourceData()
    {
      OpenSourceDataForm(SourceDataTab.Pads, "NcTestSourceData");
    }

    /// <summary>
    /// Display-only KP table tab inside «Исходные данные».
    /// </summary>
    [CommandMethod("NcTestPadOpt")]
    public void NcTestPadOpt()
    {
      OpenSourceDataForm(SourceDataTab.Pads, "NcTestPadOpt");
    }

    /// <summary>
    /// Topology insert points tab — End TopologyTieIns analogue.
    /// </summary>
    [CommandMethod("NcTestInserts")]
    public void NcTestInserts()
    {
      OpenSourceDataForm(SourceDataTab.Inserts, "NcTestInserts");
    }

    /// <summary>
    /// Placement БУОС tab inside «Исходные данные».
    /// </summary>
    [CommandMethod("NcTestBuos")]
    public void NcTestBuos()
    {
      OpenSourceDataForm(SourceDataTab.Buos, "NcTestBuos");
    }

    /// <summary>
    /// Placement Узлы tab inside «Исходные данные».
    /// </summary>
    [CommandMethod("NcTestNodes")]
    public void NcTestNodes()
    {
      OpenSourceDataForm(SourceDataTab.Nodes, "NcTestNodes");
    }

    /// <summary>Legacy alias → «Сохранить в исходные».</summary>
    [CommandMethod("NcTestPadSyncExcel")]
    public void NcTestPadSyncExcel()
    {
      NcTestSaveSources();
    }

    /// <summary>
    /// Ribbon flyout «Площадные объекты» → Сохранить в исходные (confirm, then write).
    /// </summary>
    [CommandMethod("NcTestSaveSources")]
    public void NcTestSaveSources()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        OpenSourceDataForm(SourceDataTab.Pads, "NcTestSaveSources");
        SourceDataWindow live;
        lock (SourceDataGate)
        {
          live = _sourceDataLive;
        }

        if (live == null || live.IsDisposed)
        {
          if (!CadPadInteraction.HasPendingExcelSync)
          {
            ed?.WriteMessage("\nNcTestSaveSources: нет ожидающих изменений.");
            return;
          }

          DialogResult confirm = MessageBox.Show(
            "Сохранить положения КП с чертежа в исходный Excel?",
            "Сохранить в исходные",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
          if (confirm != DialogResult.Yes)
          {
            return;
          }

          int n = CadPadInteraction.CommitPendingToExcel();
          ed?.WriteMessage("\nNcTestSaveSources: записано КП: " + n + ".");
          return;
        }

        List<DirtyCoordItem> dirty = live.CollectDirtyItems();
        if (dirty.Count == 0 && !CadPadInteraction.HasPendingExcelSync)
        {
          ed?.WriteMessage("\nNcTestSaveSources: нет изменённых положений.");
          try { PadExcelSyncUi.SetPending(false); }
          catch { }

          return;
        }

        DialogResult ok = MessageBox.Show(
          live,
          "Сохранить изменённые положения в исходные Excel?\n\nОбъектов: "
            + Math.Max(dirty.Count, CadPadInteraction.PendingPadNames.Count),
          "Сохранить в исходные",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }

        live.SaveAllToSource(confirm: false);
        ed?.WriteMessage("\nNcTestSaveSources: положения сохранены в исходные.");
      }
      catch (System.Exception ex)
      {
        WriteException(ed, "NcTestSaveSources", ex);
        try
        {
          CadApp.ShowAlertDialog("Не удалось сохранить в исходные:\n" + ex.Message);
        }
        catch
        {
        }
      }
    }

    /// <summary>
    /// Ribbon flyout «Площадные объекты» → Загрузить из исходных (confirm, then reload).
    /// </summary>
    [CommandMethod("NcTestLoadSources")]
    public void NcTestLoadSources()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        OpenSourceDataForm(SourceDataTab.Pads, "NcTestLoadSources");
        SourceDataWindow live;
        lock (SourceDataGate)
        {
          live = _sourceDataLive;
        }

        if (live == null || live.IsDisposed)
        {
          ed?.WriteMessage("\nNcTestLoadSources: окно «Исходные данные» не открыто.");
          return;
        }

        DialogResult ok = MessageBox.Show(
          live,
          "Загрузить положения из исходных Excel для всех вкладок и сбросить изменения на чертеже?",
          "Загрузить из исходных",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
        if (ok != DialogResult.Yes)
        {
          return;
        }

        live.LoadFromSource(confirm: false, activeTabOnly: false);
        ed?.WriteMessage("\nNcTestLoadSources: положения загружены из исходных.");
      }
      catch (System.Exception ex)
      {
        WriteException(ed, "NcTestLoadSources", ex);
        try
        {
          CadApp.ShowAlertDialog("Не удалось загрузить из исходных:\n" + ex.Message);
        }
        catch
        {
        }
      }
    }

    /// <summary>
    /// Ribbon flyout «Площадные объекты» → Добавить Кустовую.
    /// </summary>
    [CommandMethod("NcTestAddPad")]
    public void NcTestAddPad()
    {
      OpenSourceDataAndPlace(SourceDataTab.Pads, "NcTestAddPad");
    }

    /// <summary>
    /// Ribbon flyout «Площадные объекты» → Добавить БУОС.
    /// </summary>
    [CommandMethod("NcTestAddBuos")]
    public void NcTestAddBuos()
    {
      OpenSourceDataAndPlace(SourceDataTab.Buos, "NcTestAddBuos");
    }

    private void OpenSourceDataAndPlace(SourceDataTab tab, string label)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        OpenSourceDataForm(tab, label);
        if (_sourceDataLive == null || _sourceDataLive.IsDisposed)
        {
          return;
        }

        _sourceDataLive.BeginPlaceForTab(tab);
        ed?.WriteMessage("\n" + label + ": place jig started (" + tab + ").");
      }
      catch (System.Exception ex)
      {
        WriteException(ed, label, ex);
        try
        {
          MessageBox.Show(
            label + " failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            label,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
    }

    private void OpenSourceDataForm(SourceDataTab tab, string label)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        EnsureVisualStyles(ed);
        // Detail profiles dialog (pads) is still WPF.
        EnsureWpfApplication(ed);

        lock (SourceDataGate)
        {
          long now = DateTime.UtcNow.Ticks;
          // Debounce rapid double-click / multi-event open storms (first click was spawning many windows).
          if (_sourceDataOpening
              || (now - _lastSourceDataOpenTicks < TimeSpan.TicksPerMillisecond * 750
                  && _sourceDataLive != null && !_sourceDataLive.IsDisposed))
          {
            if (_sourceDataLive != null && !_sourceDataLive.IsDisposed)
            {
              _sourceDataLive.SelectTab(tab);
            }

            ed?.WriteMessage("\n" + label + ": debounce → reuse open window (" + tab + ").");
            return;
          }

          if (_sourceDataLive != null && !_sourceDataLive.IsDisposed)
          {
            _sourceDataLive.SelectTab(tab);
            ed?.WriteMessage("\n" + label + ": already open → tab " + tab + ".");
            return;
          }

          _sourceDataOpening = true;
          _lastSourceDataOpenTicks = now;
        }

        SourceDataWindow form = null;
        try
        {
          IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
          form = new SourceDataWindow(tab);
          lock (SourceDataGate)
          {
            // Another opener won while we constructed — dispose orphan and reuse winner.
            if (_sourceDataLive != null && !_sourceDataLive.IsDisposed)
            {
              try { form.Dispose(); }
              catch { }

              _sourceDataLive.SelectTab(tab);
              ed?.WriteMessage("\n" + label + ": singleton race → reuse open window.");
              return;
            }

            _sourceDataLive = form;
            form.FormClosed += (_, __) =>
            {
              lock (SourceDataGate)
              {
                if (ReferenceEquals(_sourceDataLive, form))
                {
                  _sourceDataLive = null;
                }
              }
            };
          }

          if (hwnd != IntPtr.Zero)
          {
            form.Show(new WindowWrapper(hwnd));
          }
          else
          {
            form.Show();
          }

          ed?.WriteMessage("\n" + label + ": Исходные данные open → " + tab + ".");
        }
        finally
        {
          lock (SourceDataGate)
          {
            _sourceDataOpening = false;
            _lastSourceDataOpenTicks = DateTime.UtcNow.Ticks;
          }
        }
      }
      catch (System.Exception ex)
      {
        lock (SourceDataGate)
        {
          _sourceDataOpening = false;
        }

        WriteException(ed, label, ex);
        try
        {
          MessageBox.Show(
            label + " failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            label,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
    }

    /// <summary>
    /// CAD label font + insert circle diameter → interface.conf
    /// </summary>
    [CommandMethod("NcTestInterface")]
    public void NcTestInterface()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      const string label = "NcTestInterface";
      try
      {
        EnsureVisualStyles(ed);
        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        using (InterfaceSettingsWindow form = new InterfaceSettingsWindow())
        {
          DialogResult result = hwnd != IntPtr.Zero
            ? form.ShowDialog(new WindowWrapper(hwnd))
            : form.ShowDialog();
          ed?.WriteMessage("\n" + label + ": DialogResult=" + result);
        }
      }
      catch (System.Exception ex)
      {
        WriteException(ed, label, ex);
      }
    }

    /// <summary>
    /// Toggle End-style CAD blink on a selected polyline/curve.
    /// Pickfirst (or prompt) → blink until the same command / ribbon button is pressed again.
    /// </summary>
    [CommandMethod("NcTestBlink", CommandFlags.UsePickSet | CommandFlags.Redraw)]
    public void NcTestBlink()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      if (ed == null)
      {
        return;
      }

      try
      {
        CadSegmentBlinker.ToggleFromSelection(ed);
      }
      catch (System.Exception ex)
      {
        WriteException(ed, "NcTestBlink", ex);
        CadSegmentBlinker.Stop();
      }
    }

    /// <summary>Force-stop blink (same as releasing the ribbon toggle).</summary>
    [CommandMethod("NcTestBlinkOff")]
    public void NcTestBlinkOff()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      CadSegmentBlinker.Stop();
      ed?.WriteMessage("\nNcTestBlinkOff: мигание ВЫКЛ.");
    }

    /// <summary>
    /// Insert ECW/raster with End-style georef (world coords + scale), not plain IMAGEATTACH.
    /// </summary>
    [CommandMethod("NcTestEcw")]
    public void NcTestEcw()
    {
      AttachGeoreferencedRaster("NcTestEcw", RasterAttachMode.Direct);
    }

    /// <summary>Alias for NcTestEcw (any georeferenced raster: ECW/TIFF/JPG/…).</summary>
    [CommandMethod("NcTestRaster")]
    public void NcTestRaster()
    {
      AttachGeoreferencedRaster("NcTestRaster", RasterAttachMode.Direct);
    }

    /// <summary>
    /// «Картография» — Forest / Height / космоснимок toggles + opacity (End-style).
    /// Open/colorize buttons live in the panel (ribbon TIF/Forest/Height moved here).
    /// </summary>
    [CommandMethod("NcTestCartography")]
    public void NcTestCartography()
    {
      OpenCartographyForm("NcTestCartography");
    }

    /// <summary>
    /// «Рассчитать допустимые площадки» — End site-grid window (WinForms, modeless).
    /// </summary>
    [CommandMethod("NcTestAllowableSites")]
    public void NcTestAllowableSites()
    {
      OpenAllowableSitesForm("NcTestAllowableSites");
    }

    /// <summary>
    /// GeoTIFF → ECW (NcGdalGeo translate / CreateCopy, preserve GeoTransform) then attach like ECW.
    /// Kept as command; ribbon open lives in Картография.
    /// </summary>
    [CommandMethod("NcTestTif")]
    public void NcTestTif()
    {
      AttachGeoreferencedRaster("NcTestTif", RasterAttachMode.TifToEcw);
    }

    /// <summary>End Forest colorize (0/1 → ForestGreen) → Maps\*_geo.tif → attach.</summary>
    [CommandMethod("NcTestForest")]
    public void NcTestForest()
    {
      AttachGeoreferencedRaster("NcTestForest", RasterAttachMode.ColorizeForest);
    }

    /// <summary>End Height colorize (viridis 7-band) → Maps\*_geo.tif → attach.</summary>
    [CommandMethod("NcTestHeight")]
    public void NcTestHeight()
    {
      AttachGeoreferencedRaster("NcTestHeight", RasterAttachMode.ColorizeHeight);
    }

    private void OpenCartographyForm(string label)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        EnsureVisualStyles(ed);
        if (_cartographyLive != null && !_cartographyLive.IsDisposed)
        {
          _cartographyLive.BringToFront();
          _cartographyLive.Activate();
          ed?.WriteMessage("\n" + label + ": already open.");
          return;
        }

        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        CartographyWindow form = new CartographyWindow();
        _cartographyLive = form;
        form.FormClosed += (_, __) =>
        {
          if (ReferenceEquals(_cartographyLive, form))
          {
            _cartographyLive = null;
          }
        };

        if (hwnd != IntPtr.Zero)
        {
          form.Show(new WindowWrapper(hwnd));
        }
        else
        {
          form.Show();
        }

        ed?.WriteMessage("\n" + label + ": Картография open.");
      }
      catch (System.Exception ex)
      {
        WriteException(ed, label, ex);
        try
        {
          MessageBox.Show(
            label + " failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            label,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
    }

    private void OpenAllowableSitesForm(string label)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        EnsureVisualStyles(ed);
        if (_allowableSitesLive != null && !_allowableSitesLive.IsDisposed)
        {
          _allowableSitesLive.BringToFront();
          _allowableSitesLive.Activate();
          ed?.WriteMessage("\n" + label + ": already open.");
          return;
        }

        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        AllowableSitesWindow form = new AllowableSitesWindow();
        _allowableSitesLive = form;
        form.FormClosed += (_, __) =>
        {
          if (ReferenceEquals(_allowableSitesLive, form))
          {
            _allowableSitesLive = null;
          }
        };

        if (hwnd != IntPtr.Zero)
        {
          form.Show(new WindowWrapper(hwnd));
        }
        else
        {
          form.Show();
        }

        ed?.WriteMessage("\n" + label + ": Allowable sites open.");
      }
      catch (System.Exception ex)
      {
        WriteException(ed, label, ex);
        try
        {
          MessageBox.Show(
            label + " failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            label,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
    }

    private enum RasterAttachMode
    {
      Direct,
      TifToEcw,
      ColorizeForest,
      ColorizeHeight
    }

    private void AttachGeoreferencedRaster(string label, RasterAttachMode mode)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        EnsureVisualStyles(ed);
        AppPaths paths = AppPaths.Load();
        string initial = paths.Background;
        string initialDir = string.Empty;
        if (!string.IsNullOrWhiteSpace(initial))
        {
          try
          {
            string dir = Path.GetDirectoryName(initial);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
              initialDir = dir;
            }
          }
          catch
          {
          }
        }

        using (OpenFileDialog dlg = new OpenFileDialog())
        {
          if (mode == RasterAttachMode.ColorizeForest)
          {
            dlg.Title = "Forest.tif → colorize (End ForestGreen) → Maps\\*_geo.tif";
            dlg.Filter = "GeoTIFF Forest (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
          }
          else if (mode == RasterAttachMode.ColorizeHeight)
          {
            dlg.Title = "Height.tif → colorize (End viridis) → Maps\\*_geo.tif";
            dlg.Filter = "GeoTIFF Height (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
          }
          else if (mode == RasterAttachMode.TifToEcw)
          {
            dlg.Title = "GeoTIFF → ECW (геопривязка сохраняется)";
            dlg.Filter = "GeoTIFF (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
          }
          else
          {
            dlg.Title = "Растр с геопривязкой (ECW / TIFF / …)";
            dlg.Filter =
              "Растры (*.ecw;*.tif;*.tiff;*.jpg;*.jpeg;*.png;*.bmp)|*.ecw;*.tif;*.tiff;*.jpg;*.jpeg;*.png;*.bmp|"
              + "ECW (*.ecw)|*.ecw|GeoTIFF (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
          }

          dlg.FilterIndex = 1;
          dlg.CheckFileExists = true;
          dlg.Multiselect = false;
          if (!string.IsNullOrEmpty(initialDir))
          {
            dlg.InitialDirectory = initialDir;
          }

          if (!string.IsNullOrWhiteSpace(initial) && File.Exists(initial))
          {
            dlg.FileName = initial;
          }

          IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
          DialogResult pick = hwnd != IntPtr.Zero
            ? dlg.ShowDialog(new WindowWrapper(hwnd))
            : dlg.ShowDialog();
          if (pick != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.FileName))
          {
            ed?.WriteMessage("\n" + label + ": отменено.");
            return;
          }

          string pickedPath = dlg.FileName;
          AppPaths.Set(p => p.Background = pickedPath);
          ed?.WriteMessage("\n" + label + ": " + RasterGeoTransform.PackageStamp());

          string rasterPath = pickedPath;
          string convertNote = string.Empty;
          if (mode == RasterAttachMode.TifToEcw)
          {
            ed?.WriteMessage("\n" + label + ": TIF→Maps (ECW if OEM license, else free GeoTIFF)…");
            CadTifToEcw.ConvertResult converted = CadTifToEcw.Convert(pickedPath);
            rasterPath = !string.IsNullOrWhiteSpace(converted.OutputPath)
              ? converted.OutputPath
              : converted.EcwPath;
            convertNote = converted.Note;
            ed?.WriteMessage("\n" + label + ": " + convertNote);
            AppPaths.Set(p => p.Background = rasterPath);
          }
          else if (mode == RasterAttachMode.ColorizeForest || mode == RasterAttachMode.ColorizeHeight)
          {
            var cmode = mode == RasterAttachMode.ColorizeForest
              ? CadRasterColorize.Mode.Forest
              : CadRasterColorize.Mode.Height;
            ed?.WriteMessage("\n" + label + ": colorize " + cmode + " → Maps\\*_geo.tif…");
            CadRasterColorize.Result colored = CadRasterColorize.Colorize(pickedPath, cmode);
            rasterPath = colored.OutputPath;
            convertNote = colored.Note;
            ed?.WriteMessage("\n" + label + ": " + convertNote);
            AppPaths.Set(p => p.Background = rasterPath);
          }

          CadRasterAttach.Result result = CadRasterAttach.Attach(rasterPath, writeWorldSidecar: true);
          CadRasterAttach.ZoomToImage(result.ImageId);

          string msg =
            label + " — вставлено\n\n"
            + (string.IsNullOrEmpty(convertNote) ? string.Empty : convertNote + "\n\n")
            + Path.GetFileName(result.Path) + "\n"
            + "Геопривязка: " + result.GeoSource + "\n"
            + "UL (мир): X=" + result.OriginX.ToString("R")
            + "  Y=" + result.OriginY.ToString("R") + "\n"
            + "Пиксель: dX=" + result.PixelSizeX.ToString("R")
            + "  dY=" + result.PixelSizeY.ToString("R") + "\n"
            + "Размер: " + result.WidthPx + "×" + result.HeightPx + " px\n"
            + (string.IsNullOrEmpty(result.WorldFileWritten)
              ? string.Empty
              : "World: " + Path.GetFileName(result.WorldFileWritten) + "\n")
            + "Слой: " + CadRasterAttach.LayerName;

          ed?.WriteMessage("\n" + label + ": OK — " + Path.GetFileName(result.Path)
            + " (" + result.GeoSource + ")");
          CadApp.ShowAlertDialog(msg);
        }
      }
      catch (System.Exception ex)
      {
        WriteException(ed, label, ex);
        try
        {
          MessageBox.Show(
            label + " failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            label,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
    }

    /// <summary>Small WinForms smoke dialog (fallback).</summary>
    [CommandMethod("NcTestDemo")]
    public void NcTestDemo()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      try
      {
        EnsureVisualStyles(ed);
        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        using (DemoForm form = new DemoForm())
        {
          if (hwnd != IntPtr.Zero)
          {
            form.ShowDialog(new WindowWrapper(hwnd));
          }
          else
          {
            form.ShowDialog();
          }
        }
      }
      catch (System.Exception ex)
      {
        WriteException(ed, "NcTestDemo", ex);
      }
    }

    private void OpenWpfWindow(Editor ed, string label, Func<WpfWindow> factory)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      ed = ed ?? doc?.Editor;

      try
      {
        ed?.WriteMessage("\n" + label + ": opening WPF shell (ShowDialog).");
        EnsureWpfApplication(ed);

        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        ed?.WriteMessage("\n" + label + ": HWND=" + hwnd.ToInt64()
          + ", STA=" + System.Threading.Thread.CurrentThread.GetApartmentState());

        WpfWindow window = factory();
        if (hwnd != IntPtr.Zero)
        {
          new WindowInteropHelper(window) { Owner = hwnd };
        }

        ElementHost.EnableModelessKeyboardInterop(window);
        WinFormsApp.DoEvents();
        bool? result = window.ShowDialog();
        ed?.WriteMessage("\n" + label + ": closed, DialogResult=" + result);
      }
      catch (System.Exception ex)
      {
        WriteException(ed, label, ex);
        try
        {
          MessageBox.Show(
            label + " failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            label,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
        }
      }
    }

    private static void EnsureWpfApplication(Editor ed)
    {
      if (WpfApp.Current != null || _wpfAppCreated)
      {
        HookWpfUnhandled(ed);
        return;
      }

      try
      {
        new WpfApp
        {
          ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
        };
        _wpfAppCreated = true;
        HookWpfUnhandled(ed);
        ed?.WriteMessage("\nWPF Application created (OnExplicitShutdown).");
      }
      catch (System.Exception ex)
      {
        ed?.WriteMessage("\nWPF Application init: " + ex.Message);
      }
    }

    private static bool _wpfUnhandledHooked;

    private static void HookWpfUnhandled(Editor ed)
    {
      if (_wpfUnhandledHooked || WpfApp.Current == null)
      {
        return;
      }

      try
      {
        WpfApp.Current.DispatcherUnhandledException += (_, e) =>
        {
          try
          {
            string dir = NcTestRibbon.Shell.TracingCache.SettingsDir;
            Directory.CreateDirectory(dir);
            File.AppendAllText(
              Path.Combine(dir, "nc_test_ribbon_crash.log"),
              DateTime.Now.ToString("s") + " [AppDispatcher] "
              + e.Exception.GetType().FullName + ": " + e.Exception.Message
              + Environment.NewLine + e.Exception.StackTrace + Environment.NewLine + "----\n");
            ed?.WriteMessage("\nWPF unhandled: " + e.Exception.GetType().Name + ": " + e.Exception.Message);
          }
          catch
          {
          }

          e.Handled = true;
        };
        _wpfUnhandledHooked = true;
      }
      catch (System.Exception ex)
      {
        ed?.WriteMessage("\nWPF unhandled hook: " + ex.Message);
      }
    }

    private static void EnsureVisualStyles(Editor ed)
    {
      if (_visualStylesTried)
      {
        return;
      }

      _visualStylesTried = true;
      try
      {
        WinFormsApp.EnableVisualStyles();
        ed?.WriteMessage("\nNcTestWpf: EnableVisualStyles OK.");
      }
      catch (System.Exception ex)
      {
        ed?.WriteMessage("\nNcTestWpf: EnableVisualStyles skipped — " + ex.Message);
      }
    }

    private static void WriteException(Editor ed, string prefix, System.Exception ex)
    {
      ed?.WriteMessage("\n" + prefix + ": ошибка — " + ex.GetType().FullName + ": " + ex.Message);
      if (ex.InnerException != null)
      {
        ed?.WriteMessage("\n  inner: " + ex.InnerException.GetType().FullName
          + ": " + ex.InnerException.Message);
      }

      ed?.WriteMessage("\n  stack: " + (ex.StackTrace ?? string.Empty));
    }

    private static string FetchUrl(string url)
    {
      using (HttpClient client = new HttpClient())
      {
        client.Timeout = TimeSpan.FromSeconds(20);
        Task<string> task = client.GetStringAsync(url);
        return task.GetAwaiter().GetResult();
      }
    }

    private static string Truncate(string value, int maxLen)
    {
      if (string.IsNullOrEmpty(value) || value.Length <= maxLen)
      {
        return value ?? string.Empty;
      }

      return value.Substring(0, maxLen) + "\n…";
    }
  }

  /// <summary>
  /// WinForms IWin32Window wrapper for Form.ShowDialog(owner).
  /// WPF windows use WindowInteropHelper.Owner instead (no Forms IWin32Window).
  /// </summary>
  internal sealed class WindowWrapper : WinFormsIWin32Window
  {
    public WindowWrapper(IntPtr handle)
    {
      Handle = handle;
    }

    public IntPtr Handle { get; }
  }

  /// <summary>
  /// Modal demo UI (WinForms) — same ShowDialog pattern as HttpTestForm / RibbonExecDoc.
  /// </summary>
  internal sealed class DemoForm : Form
  {
    public DemoForm()
    {
      Text = "NcTestWpf — демо окно";
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      MaximizeBox = false;
      MinimizeBox = false;
      ShowInTaskbar = false;
      ClientSize = new System.Drawing.Size(420, 160);

      Label title = new Label
      {
        AutoSize = false,
        Font = new System.Drawing.Font(Font.FontFamily, 12f, System.Drawing.FontStyle.Bold),
        Text = "Демо-окно (WinForms)",
        Location = new System.Drawing.Point(16, 16),
        Size = new System.Drawing.Size(388, 28)
      };

      Label body = new Label
      {
        AutoSize = false,
        Text = "Отдельная форма плагина nanoCAD.\n"
          + "Команда NcTestWpf открывает это окно через Form.ShowDialog()\n"
          + "(как NcTestHttp — modal WinForms).",
        Location = new System.Drawing.Point(16, 52),
        Size = new System.Drawing.Size(388, 60)
      };

      Button closeButton = new Button
      {
        Text = "OK / Закрыть",
        DialogResult = DialogResult.OK,
        Location = new System.Drawing.Point(284, 120),
        Width = 120,
        Height = 28
      };
      AcceptButton = closeButton;

      Controls.Add(title);
      Controls.Add(body);
      Controls.Add(closeButton);
    }
  }

  internal sealed class HttpTestForm : Form
  {
    private readonly TextBox _urlBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    public string Url => (_urlBox.Text ?? string.Empty).Trim();

    public HttpTestForm(string initialUrl)
    {
      Text = "NcTestHttp — HTTP GET";
      StartPosition = FormStartPosition.CenterScreen;
      FormBorderStyle = FormBorderStyle.FixedDialog;
      MaximizeBox = false;
      MinimizeBox = false;
      ShowInTaskbar = false;
      ClientSize = new System.Drawing.Size(520, 120);

      Label label = new Label
      {
        AutoSize = true,
        Text = "URL:",
        Location = new System.Drawing.Point(12, 16)
      };

      _urlBox = new TextBox
      {
        Location = new System.Drawing.Point(56, 12),
        Width = 440,
        Text = initialUrl ?? string.Empty
      };

      _okButton = new Button
      {
        Text = "Выполнить",
        DialogResult = DialogResult.OK,
        Location = new System.Drawing.Point(300, 60),
        Width = 96
      };

      _cancelButton = new Button
      {
        Text = "Отмена",
        DialogResult = DialogResult.Cancel,
        Location = new System.Drawing.Point(400, 60),
        Width = 96
      };

      AcceptButton = _okButton;
      CancelButton = _cancelButton;

      Controls.Add(label);
      Controls.Add(_urlBox);
      Controls.Add(_okButton);
      Controls.Add(_cancelButton);
    }

    protected override void OnShown(EventArgs e)
    {
      base.OnShown(e);
      _urlBox.Focus();
      _urlBox.SelectAll();
    }
  }
}
