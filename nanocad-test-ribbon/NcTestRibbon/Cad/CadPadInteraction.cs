namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Reflection;
  using System.Windows.Forms;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbPolyline = Teigha.DatabaseServices.Polyline;

  /// <summary>
  /// Pad CAD interaction: double-click opens «Исходные данные» → Кустовые;
  /// after MOVE/ROTATE/grip edits, prompt to write X/Y/angle back to Excel.
  /// Uses HostMgd Document/Database events; BeginDoubleClick via reflection when present.
  /// </summary>
  public static class CadPadInteraction
  {
    private static readonly object Gate = new object();
    private static bool _attached;
    private static string _status = "not attempted";
    private static int _suppress;
    private static readonly HashSet<ObjectId> Pending = new HashSet<ObjectId>();
    private static readonly HashSet<Document> HookedDocs = new HashSet<Document>();
    private static Delegate _beginDoubleClickHandler;

    public static string Status => _status;

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
          _status = "Pad CAD interaction attached (double-click + CommandEnded sync).";
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

        foreach (Document doc in new List<Document>(HookedDocs))
        {
          UnhookDocument(doc);
        }

        HookedDocs.Clear();
        Pending.Clear();
        _attached = false;
      }
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

        // Prefer polyline (footprint); labels also carry XData.
        lock (Gate)
        {
          Pending.Add(ent.ObjectId);
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
        Pending.Clear();
      }
    }

    private static void OnCommandEnded(object sender, CommandEventArgs e)
    {
      if (_suppress > 0)
      {
        lock (Gate)
        {
          Pending.Clear();
        }

        return;
      }

      string cmd = (e?.GlobalCommandName ?? string.Empty).Trim().ToUpperInvariant();
      if (!IsTransformCommand(cmd))
      {
        // Still process pending if grip/edit command names differ by locale.
        lock (Gate)
        {
          if (Pending.Count == 0)
          {
            return;
          }
        }

        if (!string.IsNullOrEmpty(cmd) && cmd != "SETVAR" && cmd != "#")
        {
          // Unknown command with pending pad edits — treat as possible grip/transform.
          if (!cmd.Contains("GRIP") && !cmd.Contains("MOVE") && !cmd.Contains("ROTATE")
              && !cmd.Contains("STRETCH") && cmd != "U" && cmd != "UNDO")
          {
            lock (Gate)
            {
              Pending.Clear();
            }

            return;
          }
        }
        else if (string.IsNullOrEmpty(cmd))
        {
          lock (Gate)
          {
            if (Pending.Count == 0)
            {
              return;
            }
          }
        }
      }

      ObjectId[] ids;
      lock (Gate)
      {
        ids = new ObjectId[Pending.Count];
        Pending.CopyTo(ids);
        Pending.Clear();
      }

      if (ids.Length == 0)
      {
        return;
      }

      try
      {
        ProcessTransformedPads(ids);
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

    private static void ProcessTransformedPads(ObjectId[] ids)
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

          if (CadPadOverlay.TryReadPadPose(name, out double x, out double y, out double rot))
          {
            byName[name] = new Pose { X = x, Y = y, RotationDeg = rot };
          }
        }

        tr.Commit();
      }

      foreach (KeyValuePair<string, Pose> pair in byName)
      {
        PromptAndSyncPad(pair.Key, pair.Value.X, pair.Value.Y, pair.Value.RotationDeg);
      }
    }

    private static void PromptAndSyncPad(string name, double x, double y, double rotationDeg)
    {
      DialogResult answer;
      try
      {
        answer = MessageBox.Show(
          "Обновить данные в Excel / конфигурации?\n\nКП: " + name
          + "\nX=" + x.ToString("0.###")
          + "; Y=" + y.ToString("0.###")
          + "; угол=" + CadPadOverlay.NormalizeDegrees(rotationDeg).ToString("0.###") + "°",
          "Кустовые площадки",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Question);
      }
      catch
      {
        return;
      }

      bool saveExcel = answer == DialogResult.Yes;
      try
      {
        TestCommandsBridge.ApplyPadCadPose(name, x, y, rotationDeg, saveExcel);
      }
      catch (Exception ex)
      {
        try
        {
          MessageBox.Show(
            "Не удалось обновить КП «" + name + "».\n\n" + ex.Message,
            "Кустовые площадки",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
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
        // Non-interactive nested pick at click point (AutoCAD-compatible API).
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
