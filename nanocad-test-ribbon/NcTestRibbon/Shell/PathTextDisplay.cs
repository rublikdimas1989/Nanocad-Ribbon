namespace NcTestRibbon.Shell
{
  using System;
  using System.Windows;
  using System.Windows.Forms;
  using WpfTextBox = System.Windows.Controls.TextBox;
  using WinFormsTextBox = System.Windows.Forms.TextBox;
  using WinFormsControl = System.Windows.Forms.Control;
  using WinFormsForm = System.Windows.Forms.Form;
  using WinFormsUserControl = System.Windows.Forms.UserControl;

  /// <summary>
  /// Keep long paths LTR and scroll so the filename (end) stays visible.
  /// WinForms helpers use System.Windows.Forms.Control only (no WPF Control).
  /// </summary>
  internal static class PathTextDisplay
  {
    public static void Configure(WinFormsTextBox box)
    {
      if (box == null)
      {
        return;
      }

      box.RightToLeft = RightToLeft.No;
      box.TextAlign = System.Windows.Forms.HorizontalAlignment.Left;
    }

    public static void ShowEnd(WinFormsTextBox box)
    {
      if (box == null || box.IsDisposed)
      {
        return;
      }

      Configure(box);
      try
      {
        string text = box.Text ?? string.Empty;
        box.SelectionLength = 0;
        box.SelectionStart = text.Length;
        box.ScrollToCaret();
      }
      catch
      {
      }
    }

    public static void SetPath(WinFormsTextBox box, string path)
    {
      if (box == null)
      {
        return;
      }

      Configure(box);
      box.Text = path ?? string.Empty;
      if (box.IsHandleCreated)
      {
        ShowEnd(box);
      }
      else
      {
        void OnHandle(object sender, EventArgs e)
        {
          box.HandleCreated -= OnHandle;
          ShowEnd(box);
        }

        box.HandleCreated += OnHandle;
      }
    }

    public static void HookShowEndOnVisible(WinFormsTextBox box, WinFormsControl host)
    {
      if (box == null || host == null)
      {
        return;
      }

      Configure(box);
      EventHandler show = (_, __) => ShowEnd(box);
      host.VisibleChanged += (_, __) =>
      {
        if (host.Visible)
        {
          ShowEnd(box);
        }
      };
      if (host is WinFormsForm form)
      {
        form.Shown += show;
        form.Load += show;
      }
      else if (host is WinFormsUserControl panel)
      {
        panel.Load += show;
      }
    }

    public static void Configure(WpfTextBox box)
    {
      if (box == null)
      {
        return;
      }

      box.FlowDirection = System.Windows.FlowDirection.LeftToRight;
      box.TextAlignment = System.Windows.TextAlignment.Left;
    }

    public static void ShowEnd(WpfTextBox box)
    {
      if (box == null)
      {
        return;
      }

      Configure(box);
      try
      {
        string text = box.Text ?? string.Empty;
        box.CaretIndex = text.Length;
        box.SelectionLength = 0;
        box.ScrollToHorizontalOffset(Math.Max(0, box.ExtentWidth - box.ViewportWidth));
      }
      catch
      {
      }
    }

    public static void SetPath(WpfTextBox box, string path)
    {
      if (box == null)
      {
        return;
      }

      Configure(box);
      box.Text = path ?? string.Empty;
      box.Dispatcher.BeginInvoke(new Action(() => ShowEnd(box)), System.Windows.Threading.DispatcherPriority.Loaded);
    }
  }
}
