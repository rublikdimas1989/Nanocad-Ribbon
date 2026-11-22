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

    public static Func<SourceDataWindow> GetLiveSourceData { get; set; }

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

        _attached = false;
      }
    }

    private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs e)
    {
      if (_prompting)
      {
        return;
      }

      try
      {
        SourceDataWindow live = null;
        try { live = GetLiveSourceData?.Invoke(); }
        catch { }

        var items = new List<DirtyCoordItem>();
        if (live != null && !live.IsDisposed)
        {
          items.AddRange(live.CollectDirtyItems());
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

        if (live != null && !live.IsDisposed)
        {
          if (action == DirtyCoordsAction.Cancel)
          {
            // Drawing is closing — discard unsaved poses back to last Excel.
            try { live.ApplyDirtyPromptAction(DirtyCoordsAction.Load); }
            catch { CadPadInteraction.ClearPending(); }
          }
          else
          {
            live.ApplyDirtyPromptAction(action);
          }
        }
        else if (action == DirtyCoordsAction.Save)
        {
          CadPadInteraction.CommitPendingToExcel();
        }
        else
        {
          CadPadInteraction.ClearPending();
        }
      }
      catch
      {
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
