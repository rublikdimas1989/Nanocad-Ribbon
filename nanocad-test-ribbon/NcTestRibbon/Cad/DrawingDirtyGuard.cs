namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.Windows.Forms;
  using HostMgd.ApplicationServices;
  using NcTestRibbon.Shell;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// On drawing close, if positions changed: yellow list + Save / Load from source.
  /// </summary>
  public static class DrawingDirtyGuard
  {
    private static readonly object Gate = new object();
    private static bool _attached;
    private static bool _prompting;
    /// <summary>Document already handled for this close — do not re-show the prompt.</summary>
    private static Document _handledCloseDoc;

    public static Func<SourceDataWindow> GetLiveSourceData { get; set; }

    public static Func<DrawingObjectsWindow> GetLiveDrawingObjects { get; set; }

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
          CadApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
          try { CadApp.DocumentManager.DocumentCreated += OnDocumentCreated; }
          catch { }

          _attached = true;
        }
        catch
        {
          _attached = false;
        }
      }
    }

    public static void TryDetach()
    {
      lock (Gate)
      {
        if (!_attached)
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

        try { CadApp.DocumentManager.DocumentCreated -= OnDocumentCreated; }
        catch { }

        _attached = false;
        _handledCloseDoc = null;
      }
    }

    private static void OnDocumentCreated(object sender, DocumentCollectionEventArgs e)
    {
      // New drawing — allow a fresh close prompt for the next document.
      if (!ReferenceEquals(_handledCloseDoc, e?.Document))
      {
        _handledCloseDoc = null;
      }
    }

    private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
    {
      Document doc = e?.Document;
      if (_prompting)
      {
        return;
      }

      // Same close cycle already answered — never re-show.
      if (doc != null && ReferenceEquals(_handledCloseDoc, doc))
      {
        return;
      }

      try
      {
        SourceDataWindow sourceLive = null;
        DrawingObjectsWindow drawingLive = null;
        try { sourceLive = GetLiveSourceData?.Invoke(); }
        catch { }

        try { drawingLive = GetLiveDrawingObjects?.Invoke(); }
        catch { }

        var items = new List<DirtyCoordItem>();
        if (drawingLive != null && !drawingLive.IsDisposed)
        {
          items.AddRange(drawingLive.CollectDirtyItems());
        }
        else if (sourceLive != null && !sourceLive.IsDisposed)
        {
          items.AddRange(sourceLive.CollectDirtyItems());
        }
        else
        {
          foreach (string name in CadPadInteraction.PendingPadNames)
          {
            items.Add(new DirtyCoordItem
            {
              Group = "Кустовые",
              Name = name,
              Detail = "ожидает записи с чертежа"
            });
          }
        }

        if (items.Count == 0)
        {
          return;
        }

        _prompting = true;
        // Mark handled before modal returns — nested DocumentToBeDestroyed must not re-prompt
        // (PadMoveLight Idle flush / pending «ожидает записи» race after Save).
        _handledCloseDoc = doc;

        IWin32Window owner = null;
        try
        {
          IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
          if (hwnd != IntPtr.Zero)
          {
            owner = new Win32Window(hwnd);
          }
        }
        catch
        {
        }

        DirtyCoordsAction action = DirtyCoordsDialog.ShowPrompt(
          owner,
          "Закрытие чертежа",
          "На чертеже есть изменённые положения (жёлтые). Сохранить в исходные или загрузить из Excel?",
          items);

        // Suppress MOVE/grip → pending while we flush/clear (avoids re-queue during Save).
        using (CadPadInteraction.SuppressNotifications())
        {
          if (action == DirtyCoordsAction.Save)
          {
            // Flush pending KP poses to Excel first (works even if CAD list is already empty).
            // discardFailures: close-save must not leave «ожидает записи» pending.
            try { CadPadInteraction.CommitPendingToExcel(discardFailures: true); }
            catch { }

            if (drawingLive != null && !drawingLive.IsDisposed)
            {
              try { drawingLive.ApplyDirtyPromptAction(DirtyCoordsAction.Save); }
              catch { }
            }
            else if (sourceLive != null && !sourceLive.IsDisposed)
            {
              try { sourceLive.ApplyDirtyPromptAction(DirtyCoordsAction.Save); }
              catch { }
            }

            // Save chosen: clear dirty so a second DocumentToBeDestroyed cannot re-prompt.
            CadPadInteraction.ClearPending();
            try { drawingLive?.ClearDirtyAfterCloseSave(); }
            catch { }

            try { sourceLive?.ClearDirtyAfterCloseSave(); }
            catch { }
          }
          else if (action == DirtyCoordsAction.Load)
          {
            if (drawingLive != null && !drawingLive.IsDisposed)
            {
              try { drawingLive.ApplyDirtyPromptAction(DirtyCoordsAction.Load); }
              catch { CadPadInteraction.ClearPending(); }
            }
            else if (sourceLive != null && !sourceLive.IsDisposed)
            {
              try { sourceLive.ApplyDirtyPromptAction(DirtyCoordsAction.Load); }
              catch { CadPadInteraction.ClearPending(); }
            }
            else
            {
              CadPadInteraction.ClearPending();
            }
          }
          else
          {
            // Cancel — discard pending so close can finish without looping.
            try
            {
              if (drawingLive != null && !drawingLive.IsDisposed)
              {
                drawingLive.ApplyDirtyPromptAction(DirtyCoordsAction.Load);
              }
              else if (sourceLive != null && !sourceLive.IsDisposed)
              {
                sourceLive.ApplyDirtyPromptAction(DirtyCoordsAction.Load);
              }
            }
            catch
            {
            }

            CadPadInteraction.ClearPending();
          }
        }

        _handledCloseDoc = doc;
      }
      catch
      {
        try { CadPadInteraction.ClearPending(); }
        catch { }

        if (doc != null)
        {
          _handledCloseDoc = doc;
        }
      }
      finally
      {
        _prompting = false;
      }
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
