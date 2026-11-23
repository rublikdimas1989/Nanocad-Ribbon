namespace NcTestRibbon.Shell
{
  using System;
  using System.Drawing;
  using System.IO;
  using System.Windows.Forms;
  using NcTestRibbon.Rates;

  /// <summary>
  /// When placement Excel for БУОС/Узлы is missing: create template or open existing.
  /// </summary>
  public static class EnsurePlacementWorkbookDialog
  {
    /// <summary>
    /// Returns a usable workbook path, or null if cancelled.
    /// Remembers path via <paramref name="rememberPath"/> when creating/opening.
    /// </summary>
    public static string EnsurePath(
      IWin32Window owner,
      string noun,
      string sheetName,
      string currentPath,
      Action<string> rememberPath)
    {
      if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
      {
        return currentPath;
      }

      using var dlg = new ChoiceForm(noun);
      DialogResult r = UiModal.ShowDialog(dlg, owner);
      if (r != DialogResult.OK || dlg.Choice == Choice.None)
      {
        return null;
      }

      TracingCache.EnsureCreated();
      string path;
      if (dlg.Choice == Choice.Create)
      {
        path = PromptCreate(owner, noun, sheetName);
      }
      else
      {
        path = PromptOpen(owner, noun, currentPath);
      }

      if (string.IsNullOrWhiteSpace(path))
      {
        return null;
      }

      // Opened an existing file (possibly catalog-only): ensure placement sheet exists.
      try
      {
        SitePlacementWorkbookLoader.EnsurePlacementSheet(path, sheetName);
      }
      catch (Exception ex)
      {
        UiModal.Msg(
          ex.Message,
          "Файл размещения «" + noun + "»",
          MessageBoxButtons.OK,
          MessageBoxIcon.Warning);
        return null;
      }

      rememberPath?.Invoke(path);
      return path;
    }

    /// <summary>Same create/open dialog for inserts workbook (sheet «Врезки»).</summary>
    public static string EnsureInsertPath(
      IWin32Window owner,
      string currentPath,
      Action<string> rememberPath)
    {
      if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
      {
        return currentPath;
      }

      using var dlg = new ChoiceForm("Врезки");
      DialogResult r = UiModal.ShowDialog(dlg, owner);
      if (r != DialogResult.OK || dlg.Choice == Choice.None)
      {
        return null;
      }

      TracingCache.EnsureCreated();
      string path;
      if (dlg.Choice == Choice.Create)
      {
        path = PromptCreateInsert(owner);
      }
      else
      {
        path = PromptOpen(owner, "Врезки", currentPath);
      }

      if (string.IsNullOrWhiteSpace(path))
      {
        return null;
      }

      rememberPath?.Invoke(path);
      return path;
    }

    private static string PromptCreateInsert(IWin32Window owner)
    {
      string suggested = Path.Combine(TracingCache.SourceDataDir, "Врезки.xlsx");
      using var sfd = new SaveFileDialog
      {
        Title = "Создать файл «Врезки»",
        Filter = "Excel (*.xlsx)|*.xlsx|Все файлы|*.*",
        DefaultExt = "xlsx",
        AddExtension = true,
        OverwritePrompt = true,
        FileName = Path.GetFileName(suggested),
        InitialDirectory = Directory.Exists(TracingCache.SourceDataDir)
          ? TracingCache.SourceDataDir
          : null
      };

      DialogResult r = UiModal.ShowDialog(sfd, owner);
      if (r != DialogResult.OK || string.IsNullOrWhiteSpace(sfd.FileName))
      {
        return null;
      }

      InsertWorkbookLoader.CreateSampleWorkbook(sfd.FileName);
      return sfd.FileName;
    }

    private static string PromptCreate(IWin32Window owner, string noun, string sheetName)
    {
      string suggested = Path.Combine(
        TracingCache.SourceDataDir,
        noun.IndexOf("БУОС", StringComparison.OrdinalIgnoreCase) >= 0
          ? "Размещение_БУОС.xlsx"
          : "Размещение_Узлы.xlsx");

      using var sfd = new SaveFileDialog
      {
        Title = "Создать файл размещения «" + noun + "»",
        Filter = "Excel (*.xlsx)|*.xlsx|Все файлы|*.*",
        DefaultExt = "xlsx",
        AddExtension = true,
        OverwritePrompt = true,
        FileName = Path.GetFileName(suggested),
        InitialDirectory = Directory.Exists(TracingCache.SourceDataDir)
          ? TracingCache.SourceDataDir
          : null
      };

      DialogResult r = UiModal.ShowDialog(sfd, owner);
      if (r != DialogResult.OK || string.IsNullOrWhiteSpace(sfd.FileName))
      {
        return null;
      }

      SitePlacementWorkbookLoader.CreateSampleWorkbook(sfd.FileName, sheetName);
      return sfd.FileName;
    }

    private static string PromptOpen(IWin32Window owner, string noun, string currentPath)
    {
      using var ofd = new OpenFileDialog
      {
        Title = "Открыть файл размещения «" + noun + "»",
        Filter = "Excel (*.xlsx;*.xlsm)|*.xlsx;*.xlsm|Все файлы|*.*",
        CheckFileExists = true
      };

      try
      {
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
          string dir = Path.GetDirectoryName(currentPath);
          if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
          {
            ofd.InitialDirectory = dir;
          }
        }
        else if (Directory.Exists(TracingCache.SourceDataDir))
        {
          ofd.InitialDirectory = TracingCache.SourceDataDir;
        }
      }
      catch
      {
      }

      DialogResult r = UiModal.ShowDialog(ofd, owner);
      if (r != DialogResult.OK || string.IsNullOrWhiteSpace(ofd.FileName))
      {
        return null;
      }

      return ofd.FileName;
    }

    private enum Choice
    {
      None,
      Create,
      Open
    }

    private sealed class ChoiceForm : Form
    {
      public Choice Choice { get; private set; } = Choice.None;

      public ChoiceForm(string noun)
      {
        Text = "Файл размещения «" + noun + "»";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 150);
        Font = SystemFonts.MessageBoxFont;

        var label = new Label
        {
          AutoSize = false,
          Dock = DockStyle.Top,
          Height = 64,
          Padding = new Padding(12, 14, 12, 8),
          Text = "Файл размещения не задан или не найден.\n"
            + "Создайте новый шаблон Excel или откройте существующий."
        };
        Controls.Add(label);

        var buttons = new FlowLayoutPanel
        {
          Dock = DockStyle.Bottom,
          FlowDirection = FlowDirection.RightToLeft,
          Height = 48,
          Padding = new Padding(8, 8, 8, 8),
          WrapContents = false
        };
        Controls.Add(buttons);

        var cancel = new Button
        {
          Text = "Отмена",
          AutoSize = true,
          DialogResult = DialogResult.Cancel,
          Margin = new Padding(6, 0, 0, 0)
        };
        var open = new Button
        {
          Text = "Открыть существующий",
          AutoSize = true,
          Margin = new Padding(6, 0, 0, 0)
        };
        var create = new Button
        {
          Text = "Создать новый файл",
          AutoSize = true,
          Margin = new Padding(6, 0, 0, 0)
        };
        create.Click += (_, __) =>
        {
          Choice = Choice.Create;
          DialogResult = DialogResult.OK;
          Close();
        };
        open.Click += (_, __) =>
        {
          Choice = Choice.Open;
          DialogResult = DialogResult.OK;
          Close();
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(open);
        buttons.Controls.Add(create);
        CancelButton = cancel;
        AcceptButton = create;
      }
    }
  }
}
