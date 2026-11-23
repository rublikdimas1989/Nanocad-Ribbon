namespace NcTestRibbon.Shell
{
  using System;
  using System.Diagnostics;
  using System.Windows.Forms;

  /// <summary>
  /// Safe modal helpers for nanoCAD + modeless «Объекты чертежа».
  /// Never use a visible WinForms Form (esp. DrawingObjectsWindow) as ShowDialog owner —
  /// that nests modal on a modeless singleton and throws
  /// «Форму, уже отображенную, нельзя отобразить как модальное…».
  /// </summary>
  public static class UiModal
  {
    /// <summary>
    /// Owner for MessageBox / FileDialog: CAD main hwnd as raw IWin32Window, never a Form.
    /// Returns null if unavailable (owner-less ShowDialog is fine).
    /// </summary>
    public static IWin32Window CadOwnerOrNull()
    {
      try
      {
        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
          return null;
        }

        // Do not resolve to a WinForms Form — even if MainWindowHandle is our modeless window.
        return new HwndOwner(hwnd);
      }
      catch
      {
        return null;
      }
    }

    /// <summary>
    /// Strip Form owners (DrawingObjectsWindow etc.). Keep raw HWND owners only.
    /// </summary>
    public static IWin32Window SanitizeOwner(IWin32Window owner)
    {
      if (owner == null)
      {
        return CadOwnerOrNull();
      }

      if (owner is Form)
      {
        return CadOwnerOrNull();
      }

      return owner;
    }

    /// <summary>
    /// Show a modal Form. Never passes a WinForms Form as owner.
    /// Hides the dialog first if it was somehow already Visible.
    /// </summary>
    public static DialogResult ShowDialog(Form dialog, IWin32Window preferredOwner = null)
    {
      if (dialog == null)
      {
        throw new ArgumentNullException(nameof(dialog));
      }

      if (dialog is DrawingObjectsWindow)
      {
        // Hard guard — singleton must stay modeless.
        ((DrawingObjectsWindow)dialog).EnsureVisible();
        return DialogResult.None;
      }

      try
      {
        if (dialog.Visible)
        {
          dialog.Hide();
        }
      }
      catch
      {
      }

      IWin32Window owner = SanitizeOwner(preferredOwner);
      return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    /// <summary>FileDialog / CommonDialog variant.</summary>
    public static DialogResult ShowDialog(CommonDialog dialog, IWin32Window preferredOwner = null)
    {
      if (dialog == null)
      {
        throw new ArgumentNullException(nameof(dialog));
      }

      IWin32Window owner = SanitizeOwner(preferredOwner);
      return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    /// <summary>MessageBox that never uses a Form owner.</summary>
    public static DialogResult Msg(
      string text,
      string caption,
      MessageBoxButtons buttons = MessageBoxButtons.OK,
      MessageBoxIcon icon = MessageBoxIcon.None)
    {
      IWin32Window owner = CadOwnerOrNull();
      return owner == null
        ? MessageBox.Show(text, caption, buttons, icon)
        : MessageBox.Show(owner, text, caption, buttons, icon);
    }

    private sealed class HwndOwner : IWin32Window
    {
      public HwndOwner(IntPtr handle)
      {
        Handle = handle;
      }

      public IntPtr Handle { get; }
    }
  }
}
