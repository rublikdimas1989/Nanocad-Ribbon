namespace NcTestRibbon
{
  using System;
  using System.Diagnostics;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Windows.Forms;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Runtime;
  using CadApp = HostMgd.ApplicationServices.Application;
  using WinFormsApp = System.Windows.Forms.Application;

  public class TestCommands
  {
    private const string DefaultHttpUrl = "https://httpbin.org/get";
    private static bool _visualStylesTried;

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
    /// Opens DemoForm with ShowDialog (same modal WinForms path as NcTestHttp).
    /// Command name NcTestWpf kept for ribbon compatibility; UI is WinForms, not WPF.
    /// Modeless Form.Show() does not reliably appear under nanoCAD.
    /// </summary>
    [CommandMethod("NcTestWpf")]
    public void NcTestWpf()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;

      try
      {
        ed?.WriteMessage("\nNcTestWpf: start (WinForms ShowDialog).");
        EnsureVisualStyles(ed);

        IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
        ed?.WriteMessage("\nNcTestWpf: MainWindowHandle=" + hwnd.ToInt64()
          + ", STA=" + System.Threading.Thread.CurrentThread.GetApartmentState());

        using (DemoForm form = new DemoForm())
        {
          WinFormsApp.DoEvents();

          DialogResult result;
          if (hwnd != IntPtr.Zero)
          {
            result = form.ShowDialog(new WindowWrapper(hwnd));
          }
          else
          {
            ed?.WriteMessage("\nNcTestWpf: no owner HWND, ShowDialog() without owner.");
            result = form.ShowDialog();
          }

          ed?.WriteMessage("\nNcTestWpf: form closed, DialogResult=" + result);
        }
      }
      catch (System.Exception ex)
      {
        WriteException(ed, "NcTestWpf", ex);
        try
        {
          MessageBox.Show(
            "NcTestWpf failed.\n\n" + ex.GetType().Name + ": " + ex.Message
            + (ex.InnerException != null ? "\n\nInner: " + ex.InnerException.Message : string.Empty),
            "NcTestWpf",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        }
        catch
        {
          // ignore secondary UI failures
        }
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

  /// <summary>IWin32Window wrapper for ShowDialog owner (nanoCAD main HWND).</summary>
  internal sealed class WindowWrapper : IWin32Window
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
