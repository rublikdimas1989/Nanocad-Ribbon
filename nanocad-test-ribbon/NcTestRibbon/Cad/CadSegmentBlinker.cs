namespace NcTestRibbon.Cad
{
  using System;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.DatabaseServices;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// End-style blink for a CAD curve: Highlight / Unhighlight every 200 ms
  /// (same tick as End <c>MainWindow.ManualProgress</c> DispatcherTimer).
  /// Toggle off clears highlight and stops the timer.
  /// </summary>
  public static class CadSegmentBlinker
  {
    private static readonly object Gate = new object();
    private static System.Windows.Forms.Timer _timer;
    private static Document _document;
    private static ObjectId _entityId = ObjectId.Null;
    private static bool _bright = true;
    private static bool _highlighted;
    private static bool _hooksAttached;

    public static bool IsActive
    {
      get
      {
        lock (Gate)
        {
          return _timer != null && _timer.Enabled && !_entityId.IsNull;
        }
      }
    }

    /// <summary>
    /// If blinking — stop (button released). Else resolve pickfirst/prompt curve and start.
    /// </summary>
    public static bool ToggleFromSelection(Editor ed)
    {
      if (ed == null)
      {
        return false;
      }

      if (IsActive)
      {
        Stop();
        ed.WriteMessage("\nNcTestBlink: мигание ВЫКЛ (кнопка отжата).");
        return false;
      }

      ObjectId id = ResolveCurveId(ed);
      if (id.IsNull)
      {
        ed.WriteMessage("\nNcTestBlink: выберите полилинию (или кривую), затем снова нажмите кнопку.");
        return false;
      }

      Start(ed.Document, id);
      ed.WriteMessage("\nNcTestBlink: мигание ВКЛ. Нажмите кнопку ещё раз, чтобы отжать.");
      return true;
    }

    public static void Stop()
    {
      lock (Gate)
      {
        StopUnlocked();
      }
    }

    private static void Start(Document doc, ObjectId entityId)
    {
      if (doc == null || entityId.IsNull)
      {
        return;
      }

      lock (Gate)
      {
        StopUnlocked();
        _document = doc;
        _entityId = entityId;
        _bright = true;
        _highlighted = false;
        AttachHooksUnlocked();

        _timer = new System.Windows.Forms.Timer { Interval = 200 };
        _timer.Tick += OnTick;
        _timer.Start();
        ApplyHighlightUnlocked(true);
      }
    }

    private static void StopUnlocked()
    {
      if (_timer != null)
      {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer.Dispose();
        _timer = null;
      }

      ClearHighlightUnlocked();
      DetachHooksUnlocked();
      _document = null;
      _entityId = ObjectId.Null;
      _bright = true;
    }

    private static ObjectId ResolveCurveId(Editor ed)
    {
      try
      {
        PromptSelectionResult implied = ed.SelectImplied();
        if (implied.Status == PromptStatus.OK && implied.Value != null)
        {
          foreach (SelectedObject selected in implied.Value)
          {
            if (selected == null)
            {
              continue;
            }

            ObjectId id = selected.ObjectId;
            if (IsCurve(ed.Document.Database, id))
            {
              return id;
            }
          }
        }
      }
      catch
      {
        // Fall through to interactive pick.
      }

      var options = new PromptEntityOptions("\nВыберите полилинию / кривую для мигания: ");
      options.SetRejectMessage("\nНужна кривая (Polyline, Line, Arc, …).");
      options.AddAllowedClass(typeof(Curve), exactMatch: false);
      PromptEntityResult result = ed.GetEntity(options);
      return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
    }

    private static bool IsCurve(Database db, ObjectId id)
    {
      if (db == null || id.IsNull)
      {
        return false;
      }

      try
      {
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
          DBObject obj = tr.GetObject(id, OpenMode.ForRead, openErased: false);
          bool ok = obj is Curve;
          tr.Commit();
          return ok;
        }
      }
      catch
      {
        return false;
      }
    }

    private static void OnTick(object sender, EventArgs e)
    {
      lock (Gate)
      {
        if (_document == null || _entityId.IsNull)
        {
          return;
        }

        _bright = !_bright;
        ApplyHighlightUnlocked(_bright);
      }
    }

    private static void ApplyHighlightUnlocked(bool on)
    {
      Document doc = _document;
      ObjectId id = _entityId;
      if (doc == null || id.IsNull)
      {
        return;
      }

      try
      {
        if (id.IsErased)
        {
          StopUnlocked();
          try
          {
            doc.Editor?.WriteMessage("\nNcTestBlink: объект удалён — мигание ВЫКЛ.");
          }
          catch
          {
          }

          return;
        }

        using (doc.LockDocument())
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          Entity entity = tr.GetObject(id, OpenMode.ForRead, openErased: false) as Entity;
          if (entity == null)
          {
            tr.Commit();
            StopUnlocked();
            return;
          }

          if (on)
          {
            entity.Highlight();
            _highlighted = true;
          }
          else if (_highlighted)
          {
            entity.Unhighlight();
            _highlighted = false;
          }

          tr.Commit();
        }

        doc.Editor?.UpdateScreen();
      }
      catch (System.Exception ex)
      {
        try
        {
          doc.Editor?.WriteMessage("\nNcTestBlink: остановлено — " + ex.Message);
        }
        catch
        {
        }

        StopUnlocked();
      }
    }

    private static void ClearHighlightUnlocked()
    {
      if (!_highlighted || _document == null || _entityId.IsNull)
      {
        _highlighted = false;
        return;
      }

      try
      {
        using (_document.LockDocument())
        using (Transaction tr = _document.Database.TransactionManager.StartTransaction())
        {
          Entity entity = tr.GetObject(_entityId, OpenMode.ForRead, openErased: true) as Entity;
          entity?.Unhighlight();
          tr.Commit();
        }

        _document.Editor?.UpdateScreen();
      }
      catch
      {
        // Ignore cleanup failures (document closing).
      }

      _highlighted = false;
    }

    private static void AttachHooksUnlocked()
    {
      if (_hooksAttached)
      {
        return;
      }

      CadApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
      _hooksAttached = true;
    }

    private static void DetachHooksUnlocked()
    {
      if (!_hooksAttached)
      {
        return;
      }

      try
      {
        CadApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
      }
      catch
      {
      }

      _hooksAttached = false;
    }

    private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
    {
      lock (Gate)
      {
        if (e?.Document != null && ReferenceEquals(e.Document, _document))
        {
          StopUnlocked();
        }
      }
    }
  }
}
