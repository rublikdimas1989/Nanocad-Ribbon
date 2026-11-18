//
// Две тестовые команды ленты «Тест».
// Имена [CommandMethod] должны совпадать с intername / command в NC_TestRibbon.json.
//

namespace NcTestRibbon
{
  using System;
  using System.Net.Http;
  using System.Threading.Tasks;
  using System.Windows.Forms;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Runtime;

  public class TestCommands
  {
    private const string DefaultHttpUrl = "https://httpbin.org/get";

    [CommandMethod("NcTestHello")]
    public void NcTestHello()
    {
      Document doc = Application.DocumentManager.MdiActiveDocument;
      Editor ed = doc?.Editor;

      const string text = "Тест 1: Hello";
      ed?.WriteMessage("\n" + text);
      Application.ShowAlertDialog(text);
    }

    [CommandMethod("NcTestHttp")]
    public void NcTestHttp()
    {
      Document doc = Application.DocumentManager.MdiActiveDocument;
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
          Application.ShowAlertDialog(
            "NcTestHttp — успех\n\nURL: " + url
            + "\n\nОтвет (обрезан):\n" + preview);
        }
        catch (Exception ex)
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
