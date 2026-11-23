namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Reflection;
  using System.Threading;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// Pad CAD interaction: double-click opens «Исходные данные» → Кустовые;
  /// after MOVE/ROTATE/grip, mark pending poses and highlight ribbon
  /// «Обновить данные в Excel» (no MessageBox — one pending set until user clicks).
  /// UI preview is debounced — no Excel write / no full grid rebuild per MOVE step.
  /// </summary>
  public static class CadPadInteraction
  {
    private static readonly object Gate = new object();
    private static bool _attached;
    private static string _status = "not attempted";
    private static int _suppress;
    /// <summary>ObjectIds touched during the current CAD command (cleared each CommandEnded).</summary>
    private static readonly HashSet<ObjectId> SessionModified = new HashSet<ObjectId>();
    /// <summary>Pad name → pose waiting for Excel write (survives until CommitPending).</summary>
    private static readonly Dictionary<string, Pose> PendingPoses =
      new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<Document> HookedDocs = new HashSet<Document>();
    private static Delegate _beginDoubleClickHandler;
    private static long _lastSessionHandledTicks;
    private const long SessionDebounceTicks = TimeSpan.TicksPerMillisecond * 400;
    private static long _lastDoubleClickTicks;
    private const long DoubleClickDebounceTicks = TimeSpan.TicksPerMillisecond * 800;
    /// <summary>Coalesce rapid GRIP/MOVE CommandEnded into one yellow-grid flush.</summary>
    private static Timer _uiFlushTimer;
    private const int UiFlushDelayMs = 250;
    private static int _uiFlushPosted;

    public static string Status => _status;

    public static bool HasPendingExcelSync
    {
      get
      {
        lock (Gate)
        {
          return PendingPoses.Count > 0;
        }
      }
    }

    public static IReadOnlyCollection<string> PendingPadNames
    {
      get
      {
        lock (Gate)
        {
          return new List<string>(PendingPoses.Keys);
        }
      }
    }

    public static IDisposable SuppressNotifications()
    {
      return new SuppressScope();
    }

    public static void TryAttach()
    {
      lock (Gate)
      {
        if (_attached)
        {
          return;
        }

        try
        {
          try { CadApp.DocumentManager.DocumentCreated += OnDocumentCreated; }
          catch { }

          try { CadApp.DocumentManager.DocumentActivated += OnDocumentActivated; }
          catch { }

          CadApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
          TryHookBeginDoubleClick();

          Document active = CadApp.DocumentManager.MdiActiveDocument;
          if (active != null)
          {
            HookDocument(active);
          }

          _attached = true;
          if (string.IsNullOrEmpty(_status) || _status == "not attempted")
          {
            _status = "Pad CAD interaction attached (double-click + ribbon Excel sync).";
          }
        }
        catch (Exception ex)
        {
          _status = "Attach failed: " + ex.GetType().Name + ": " + ex.Message;
          _attached = false;
        }
      }
    }

    public static void TryDetach()
    {
      lock (Gate)
      {
        try
        {
          try { CadApp.DocumentManager.DocumentCreated -= OnDocumentCreated; }
          catch { }

          try { CadApp.DocumentManager.DocumentActivated -= OnDocumentActivated; }
          catch { }

          CadApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
        }
        catch
        {
        }

        TryUnhookBeginDoubleClick();
        CancelUiFlush();

        foreach (Document doc in new List<Document>(HookedDocs))
        {
          UnhookDocument(doc);
        }

        HookedDocs.Clear();
        SessionModified.Clear();
        PendingPoses.Clear();
        _attached = false;
      }

      try { PadExcelSyncUi.SetPending(false); }
      catch { }
    }

    private static void CancelUiFlush()
    {
      try
      {
        Timer t = _uiFlushTimer;
        _uiFlushTimer = null;
        t?.Dispose();
      }
      catch
      {
      }

      try { CadApp.Idle -= OnIdleFlushPendingUi; }
      catch { }

      Interlocked.Exchange(ref _uiFlushPosted, 0);
    }

    /// <summary>Drop pending CAD poses without writing Excel (load-from-source / discard).</summary>
    public static void ClearPending(bool updateRibbon = true)
    {
      lock (Gate)
      {
        PendingPoses.Clear();
        SessionModified.Clear();
        CancelUiFlush();
      }

      if (updateRibbon)
      {
        try { PadExcelSyncUi.SetPending(false); }
        catch { }
      }
    }

    /// <summary>
    /// Ribbon «Сохранить в исходные»: write all pending X/Y/angle to Excel and clear red state.
    /// </summary>
    public static int CommitPendingToExcel()
    {
      List<KeyValuePair<string, Pose>> batch;
      lock (Gate)
      {
        batch = new List<KeyValuePair<string, Pose>>(PendingPoses);
        PendingPoses.Clear();
        SessionModified.Clear();
        CancelUiFlush();
      }

      try { PadExcelSyncUi.SetPending(false); }
      catch { }

      if (batch.Count == 0)
      {
        return 0;
      }

      int ok = 0;
      var errors = new List<string>();
      using (SuppressNotifications())
      {
        foreach (KeyValuePair<string, Pose> pair in batch)
        {
          try
          {
            TestCommandsBridge.ApplyPadCadPose(
              pair.Key, pair.Value.X, pair.Value.Y, pair.Value.RotationDeg, true);
            ok++;
          }
          catch (Exception ex)
          {
            errors.Add(pair.Key + ": " + ex.Message);
            // Keep failed pads pending so user can retry.
            lock (Gate)
            {
              PendingPoses[pair.Key] = pair.Value;
            }
          }
        }
      }

      bool stillPending;
      lock (Gate)
      {
        stillPending = PendingPoses.Count > 0;
      }

      if (stillPending)
      {
        try { PadExcelSyncUi.SetPending(true); }
        catch { }
      }

      if (errors.Count > 0)
      {
        throw new InvalidOperationException(
          "Обновлено " + ok + " из " + batch.Count + ".\n" + string.Join("\n", errors));
      }

      return ok;
    }

    private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
    {
      if (e?.Document != null)
      {
        HookDocument(e.Document);
      }
    }

    private static void OnDocumentActivated(object sender, DocumentCollectionEventArgs e)
    {
      if (e?.Document != null)
      {
        HookDocument(e.Document);
      }

      // Re-apply red highlight if pending (ribbon may rebuild on doc switch).
      if (HasPendingExcelSync)
      {
        try { PadExcelSyncUi.SetPending(true); }
        catch { }
      }
    }

    private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
    {
      if (e?.Document != null)
      {
        UnhookDocument(e.Document);
      }
    }

    private static void HookDocument(Document doc)
    {
      if (doc == null)
      {
        return;
      }

      lock (Gate)
      {
        if (!HookedDocs.Add(doc))
        {
          return;
        }
      }

      try
      {
        doc.CommandEnded += OnCommandEnded;
        doc.CommandCancelled += OnCommandCancelled;
        try { doc.CommandFailed += OnCommandCancelled; }
        catch { }

        doc.Database.ObjectModified += OnObjectModified;
      }
      catch
      {
        lock (Gate)
        {
          HookedDocs.Remove(doc);
        }
      }
    }

    private static void UnhookDocument(Document doc)
    {
      if (doc == null)
      {
        return;
      }

      try
      {
        doc.CommandEnded -= OnCommandEnded;
        doc.CommandCancelled -= OnCommandCancelled;
        try { doc.CommandFailed -= OnCommandCancelled; }
        catch { }

        doc.Database.ObjectModified -= OnObjectModified;
      }
      catch
      {
      }

      lock (Gate)
      {
        HookedDocs.Remove(doc);
      }
    }

    private static void OnObjectModified(object sender, ObjectEventArgs e)
    {
      // Ignore our own UpdatePad / Excel redraw writes.
      if (_suppress > 0 || e?.DBObject == null)
      {
        return;
      }

      try
      {
        Entity ent = e.DBObject as Entity;
        if (ent == null || !IsPadLayer(ent))
        {
          return;
        }

        lock (Gate)
        {
          SessionModified.Add(ent.ObjectId);
        }
      }
      catch
      {
      }
    }

    private static void OnCommandCancelled(object sender, CommandEventArgs e)
    {
      lock (Gate)
      {
        SessionModified.Clear();
      }
    }

    private static void OnCommandEnded(object sender, CommandEventArgs e)
    {
      if (_suppress > 0)
      {
        lock (Gate)
        {
          SessionModified.Clear();
        }

        return;
      }

      string cmd = (e?.GlobalCommandName ?? string.Empty).Trim().ToUpperInvariant();

      // One pending mark per move session: only transform/grip commands promote session → pending.
      // Empty / idle CommandEnded must NOT re-process (was the MessageBox infinite loop).
      if (!IsTransformCommand(cmd))
      {
        lock (Gate)
        {
          SessionModified.Clear();
        }

        return;
      }

      long now = DateTime.UtcNow.Ticks;
      lock (Gate)
      {
        if (now - _lastSessionHandledTicks < SessionDebounceTicks && SessionModified.Count == 0)
        {
          return;
        }
      }

      ObjectId[] ids;
      lock (Gate)
      {
        if (SessionModified.Count == 0)
        {
          return;
        }

        ids = new ObjectId[SessionModified.Count];
        SessionModified.CopyTo(ids);
        SessionModified.Clear();
        _lastSessionHandledTicks = now;
      }

      try
      {
        PromoteSessionToPending(ids);
      }
      catch
      {
      }
    }

    private static bool IsTransformCommand(string cmd)
    {
      if (string.IsNullOrEmpty(cmd))
      {
        return false;
      }

      return cmd == "MOVE" || cmd == "ROTATE" || cmd == "STRETCH"
        || cmd.StartsWith("GRIP_", StringComparison.Ordinal)
        || cmd.Contains("GRIP")
        || cmd == "MOVECOPY" || cmd == "ROTATE3D";
    }

    private static void PromoteSessionToPending(ObjectId[] ids)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      var byName = new Dictionary<string, Pose>(StringComparer.OrdinalIgnoreCase);
      using (doc.LockDocument())
      using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
      {
        foreach (ObjectId id in ids)
        {
          if (id.IsNull || id.IsErased)
          {
            continue;
          }

          Entity ent = tr.GetObject(id, OpenMode.ForRead, openErased: true) as Entity;
          if (ent == null || !IsPadLayer(ent))
          {
            continue;
          }

          string name = CadPadOverlay.ReadPadNamePublic(ent);
          if (string.IsNullOrWhiteSpace(name))
          {
            if (ent is DBText text && !string.IsNullOrWhiteSpace(text.TextString))
            {
              name = text.TextString.Trim();
            }
          }

          if (string.IsNullOrWhiteSpace(name) || byName.ContainsKey(name))
          {
            continue;
          }

          // Pose read is read-only; still suppress in case overlay helpers touch DB.
          using (SuppressNotifications())
          {
            if (CadPadOverlay.TryReadPadPose(name, out double x, out double y, out double rot))
            {
              byName[name] = new Pose { X = x, Y = y, RotationDeg = rot };
            }
          }
        }

        tr.Commit();
      }

      if (byName.Count == 0)
      {
        return;
      }

      lock (Gate)
      {
        foreach (KeyValuePair<string, Pose> pair in byName)
        {
          PendingPoses[pair.Key] = pair.Value;
        }
      }

      // Ribbon pending flag once; yellow grid via debounced flush (not per grip step).
      try { PadExcelSyncUi.SetPending(true); }
      catch { }

      ScheduleUiFlush();
    }

    private static void ScheduleUiFlush()
    {
      try
      {
        lock (Gate)
        {
          if (_uiFlushTimer == null)
          {
            _uiFlushTimer = new Timer(OnUiFlushTimer, null, UiFlushDelayMs, Timeout.Infinite);
          }
          else
          {
            _uiFlushTimer.Change(UiFlushDelayMs, Timeout.Infinite);
          }
        }
      }
      catch
      {
        FlushPendingToUi();
      }
    }

    private static void OnUiFlushTimer(object state)
    {
      // Timer is thread-pool — hop to CAD idle so WinForms grid updates stay on UI thread.
      if (Interlocked.Exchange(ref _uiFlushPosted, 1) != 0)
      {
        return;
      }

      try
      {
        CadApp.Idle += OnIdleFlushPendingUi;
      }
      catch
      {
        Interlocked.Exchange(ref _uiFlushPosted, 0);
        try { FlushPendingToUi(); }
        catch { }
      }
    }

    private static void OnIdleFlushPendingUi(object sender, EventArgs e)
    {
      try { CadApp.Idle -= OnIdleFlushPendingUi; }
      catch { }

      Interlocked.Exchange(ref _uiFlushPosted, 0);
      FlushPendingToUi();
    }

    private static void FlushPendingToUi()
    {
      List<KeyValuePair<string, Pose>> batch;
      lock (Gate)
      {
        if (PendingPoses.Count == 0)
        {
          return;
        }

        batch = new List<KeyValuePair<string, Pose>>(PendingPoses);
      }

      // In-place yellow only (ApplyPadCadPose saveExcel:false) — no Excel, no CAD rewrite.
      using (SuppressNotifications())
      {
        foreach (KeyValuePair<string, Pose> pair in batch)
        {
          try
          {
            TestCommandsBridge.ApplyPadCadPose(
              pair.Key, pair.Value.X, pair.Value.Y, pair.Value.RotationDeg, false);
          }
          catch
          {
            // Window may be closed / pad not loaded — Excel write still on Save.
          }
        }
      }
    }

    private static void TryHookBeginDoubleClick()
    {
      try
      {
        EventInfo ev = typeof(CadApp).GetEvent(
          "BeginDoubleClick",
          BindingFlags.Public | BindingFlags.Static);
        if (ev == null)
        {
          _status = "BeginDoubleClick not found — use selection + CommandEnded only.";
          return;
        }

        MethodInfo handler = typeof(CadPadInteraction).GetMethod(
          nameof(OnBeginDoubleClick),
          BindingFlags.NonPublic | BindingFlags.Static);
        _beginDoubleClickHandler = Delegate.CreateDelegate(ev.EventHandlerType, handler);
        ev.AddEventHandler(null, _beginDoubleClickHandler);
      }
      catch (Exception ex)
      {
        _beginDoubleClickHandler = null;
        _status = "BeginDoubleClick hook failed: " + ex.Message;
      }
    }

    private static void TryUnhookBeginDoubleClick()
    {
      if (_beginDoubleClickHandler == null)
      {
        return;
      }

      try
      {
        EventInfo ev = typeof(CadApp).GetEvent(
          "BeginDoubleClick",
          BindingFlags.Public | BindingFlags.Static);
        ev?.RemoveEventHandler(null, _beginDoubleClickHandler);
      }
      catch
      {
      }

      _beginDoubleClickHandler = null;
    }

    /// <summary>Reflection target for HostMgd BeginDoubleClick (sender, eventArgs).</summary>
    private static void OnBeginDoubleClick(object sender, EventArgs e)
    {
      if (_suppress > 0)
      {
        return;
      }

      long now = DateTime.UtcNow.Ticks;
      lock (Gate)
      {
        if (now - _lastDoubleClickTicks < DoubleClickDebounceTicks)
        {
          return;
        }

        _lastDoubleClickTicks = now;
      }

      try
      {
        string name = TryPickPadNameFromDoubleClick(e);
        if (string.IsNullOrWhiteSpace(name))
        {
          name = TryPadNameFromImpliedSelection();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
          return;
        }

        TestCommandsBridge.OpenPadsWindow(name);
      }
      catch
      {
      }
    }

    private static string TryPickPadNameFromDoubleClick(EventArgs e)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      if (ed == null)
      {
        return null;
      }

      Point3d? point = TryReadClickPoint(e);
      if (point == null)
      {
        return null;
      }

      try
      {
        Type optsType = Type.GetType("HostMgd.EditorInput.PromptNestedEntityOptions, hostmgd", false)
          ?? typeof(PromptEntityOptions);
        object opts = Activator.CreateInstance(optsType, "\n");
        PropertyInfo useNi = optsType.GetProperty("UseNonInteractivePickPoint");
        PropertyInfo niPt = optsType.GetProperty("NonInteractivePickPoint");
        if (useNi != null && niPt != null)
        {
          useNi.SetValue(opts, true);
          niPt.SetValue(opts, point.Value);
        }
        else
        {
          return null;
        }

        MethodInfo getNested = ed.GetType().GetMethod("GetNestedEntity", new[] { optsType });
        if (getNested == null)
        {
          return null;
        }

        object result = getNested.Invoke(ed, new[] { opts });
        PropertyInfo statusProp = result?.GetType().GetProperty("Status");
        object status = statusProp?.GetValue(result);
        if (status == null || !string.Equals(status.ToString(), "OK", StringComparison.OrdinalIgnoreCase))
        {
          return null;
        }

        PropertyInfo idProp = result.GetType().GetProperty("ObjectId");
        ObjectId id = idProp != null ? (ObjectId)idProp.GetValue(result) : ObjectId.Null;
        if (id.IsNull)
        {
          return null;
        }

        using (doc.LockDocument())
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
          tr.Commit();
          return ResolvePadName(ent);
        }
      }
      catch
      {
        return null;
      }
    }

    private static string TryPadNameFromImpliedSelection()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;
      if (doc == null || ed == null)
      {
        return null;
      }

      try
      {
        PromptSelectionResult sel = ed.SelectImplied();
        if (sel == null || sel.Status != PromptStatus.OK || sel.Value == null)
        {
          return null;
        }

        ObjectId[] ids = sel.Value.GetObjectIds();
        if (ids == null || ids.Length == 0)
        {
          return null;
        }

        using (doc.LockDocument())
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          foreach (ObjectId id in ids)
          {
            if (id.IsNull)
            {
              continue;
            }

            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
            string name = ResolvePadName(ent);
            if (!string.IsNullOrWhiteSpace(name))
            {
              tr.Commit();
              return name;
            }
          }

          tr.Commit();
        }
      }
      catch
      {
      }

      return null;
    }

    private static string ResolvePadName(Entity ent)
    {
      if (ent == null || !IsPadLayer(ent))
      {
        return null;
      }

      string name = CadPadOverlay.ReadPadNamePublic(ent);
      if (!string.IsNullOrWhiteSpace(name))
      {
        return name;
      }

      if (ent is DBText text && !string.IsNullOrWhiteSpace(text.TextString))
      {
        return text.TextString.Trim();
      }

      return null;
    }

    private static Point3d? TryReadClickPoint(EventArgs e)
    {
      if (e == null)
      {
        return null;
      }

      try
      {
        PropertyInfo loc = e.GetType().GetProperty("Location")
          ?? e.GetType().GetProperty("PickPoint")
          ?? e.GetType().GetProperty("Point");
        if (loc == null)
        {
          return null;
        }

        object val = loc.GetValue(e);
        if (val is Point3d p3)
        {
          return p3;
        }
      }
      catch
      {
      }

      return null;
    }

    private static bool IsPadLayer(Entity ent)
    {
      return ent != null
        && string.Equals(ent.Layer, CadPadOverlay.LayerName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Pose
    {
      public double X;
      public double Y;
      public double RotationDeg;
    }

    private sealed class SuppressScope : IDisposable
    {
      public SuppressScope()
      {
        System.Threading.Interlocked.Increment(ref _suppress);
      }

      public void Dispose()
      {
        System.Threading.Interlocked.Decrement(ref _suppress);
      }
    }
  }

  /// <summary>
  /// Bridge so Cad layer does not reference Shell WinForms types tightly at call sites.
  /// Implemented by <see cref="TestCommands"/>.
  /// </summary>
  public static class TestCommandsBridge
  {
    public static Action<string> OpenPadsWindow { get; set; }
      = _ => { };

    public static Action<string, double, double, double, bool> ApplyPadCadPose { get; set; }
      = (_, __, ___, ____, _____) => { };
  }
}
