namespace NcTestRibbon.Shell
{
  using System;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Windows.Forms;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Net;
  using NcTestRibbon.Rates;

  /// <summary>
  /// End «Площадки — допустимость и земляные работы» adapted for nanoCAD WinForms.
  /// Largest BUOS/node footprints as labels; shared AOI; upload map to solver.
  /// </summary>
  public sealed class AllowableSitesWindow : Form
  {
    private readonly Label _buosSize;
    private readonly TextBox _buosRiver;
    private readonly TextBox _buosInfra;
    private readonly TextBox _nodeRiver;
    private readonly Label _aoiInfo;
    private readonly Label _status;
    private readonly CheckBox _showBuos;
    private readonly Button _btnUpload;
    private readonly Button _calcBuos;
    private readonly Button _cancelCalc;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressMsg;
    private readonly Panel _progressPanel;
    private bool _busy;
    private CancellationTokenSource _calcCts;

    public AllowableSitesWindow()
    {
      Text = "Площадки — допустимость и земляные работы";
      Width = 680;
      Height = 720;
      MinimumSize = new Size(560, 560);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = true;
      Font = new Font("Segoe UI", 9f);

      GeometryConf geo = GeometryConf.Load();

      var scroll = new Panel
      {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Padding = new Padding(12)
      };
      Controls.Add(scroll);

      var root = new FlowLayoutPanel
      {
        Dock = DockStyle.Top,
        AutoSize = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Width = 620,
        Padding = new Padding(4)
      };
      scroll.Controls.Add(root);

      _buosSize = new Label
      {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 0, 0, 12),
        Font = new Font(Font, FontStyle.Bold)
      };
      root.Controls.Add(_buosSize);

      root.Controls.Add(MakeLabel("БУОС: от контура площадки до линии реки, м"));
      _buosRiver = MakeNum(geo.BuosRiverBufferM);
      root.Controls.Add(_buosRiver);

      root.Controls.Add(MakeLabel("БУОС: от контура до дорог и существующих труб, м"));
      _buosInfra = MakeNum(geo.BuosInfraBufferM);
      root.Controls.Add(_buosInfra);

      root.Controls.Add(MakeLabel("Узлы: от контура площадки до линии реки, м"));
      _nodeRiver = MakeNum(geo.NodeRiverBufferM);
      root.Controls.Add(_nodeRiver);

      root.Controls.Add(new Label
      {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 6, 0, 8),
        Text = "Если линия реки — её ось, включите в отступ полуширину русла. "
          + "Отступы этого окна используются без скрытого добавления охранных зон из настроек труб. "
          + "Для узлов по умолчанию 10 м; значение можно изменить."
      });

      var areaRow = new FlowLayoutPanel
      {
        AutoSize = true,
        WrapContents = true,
        Margin = new Padding(0, 4, 0, 4)
      };
      root.Controls.Add(areaRow);

      var selectArea = new Button
      {
        Text = "Выделить область прямоугольником",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      selectArea.Click += (_, __) => SelectArea();
      areaRow.Controls.Add(selectArea);

      var resetArea = new Button
      {
        Text = "Сбросить область",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      resetArea.Click += (_, __) => ResetArea();
      areaRow.Controls.Add(resetArea);

      _aoiInfo = new Label
      {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 4, 0, 10)
      };
      root.Controls.Add(_aoiInfo);

      var calcRow = new FlowLayoutPanel
      {
        AutoSize = true,
        WrapContents = true,
        Margin = new Padding(0, 4, 0, 4)
      };
      root.Controls.Add(calcRow);

      _calcBuos = new Button
      {
        Text = "Рассчитать слой БУОС",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      _calcBuos.Click += async (_, __) => await CalculateBuosAsync().ConfigureAwait(true);
      calcRow.Controls.Add(_calcBuos);

      _cancelCalc = new Button
      {
        Text = "Отменить расчёт",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      _cancelCalc.Click += (_, __) =>
      {
        if (_calcCts != null && !_calcCts.IsCancellationRequested)
        {
          _calcCts.Cancel();
          SetStatus("Отмена расчёта…");
        }
        else
        {
          SetStatus("Нет активного расчёта.");
        }
      };
      calcRow.Controls.Add(_cancelCalc);

      _progressPanel = new Panel
      {
        Width = 600,
        Height = 28,
        Margin = new Padding(0, 4, 0, 4),
        Visible = false
      };
      root.Controls.Add(_progressPanel);

      // ProgressBar + message side-by-side (message next to bar).
      _progressBar = new ProgressBar
      {
        Left = 0,
        Top = 4,
        Width = 220,
        Height = 18,
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 30
      };
      _progressPanel.Controls.Add(_progressBar);

      _progressMsg = new Label
      {
        Left = 230,
        Top = 4,
        Width = 360,
        Height = 20,
        AutoEllipsis = true,
        ForeColor = Color.DimGray,
        Text = string.Empty
      };
      _progressPanel.Controls.Add(_progressMsg);

      _btnUpload = new Button
      {
        Text = "Обновить карту на сервере",
        AutoSize = true,
        Margin = new Padding(0, 8, 8, 8),
        Font = new Font(Font, FontStyle.Bold)
      };
      _btnUpload.Click += async (_, __) => await UploadMapAsync().ConfigureAwait(true);
      root.Controls.Add(_btnUpload);

      _showBuos = new CheckBox
      {
        Text = "Показать слой БУОС",
        AutoSize = true,
        Checked = geo.ShowBuosLayer,
        Margin = new Padding(0, 8, 0, 4)
      };
      _showBuos.CheckedChanged += (_, __) => PersistUiFlags();
      root.Controls.Add(_showBuos);

      _status = new Label
      {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 8, 0, 8),
        ForeColor = Color.DimGray
      };
      root.Controls.Add(_status);

      root.Controls.Add(new Label
      {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 8, 0, 4),
        Text = "Центры перебираются с шагом CenterStepM (net.conf, по умолчанию 50 м). "
          + "Углы БУОС — RotationAnglesDeg в net.conf (0…150°). "
          + "AOI → geometry.conf. "
          + "«Обновить карту» → POST api/v1/datasets (DXF) → DatasetId в net.conf. "
          + "«Рассчитать слой БУОС» → POST search-jobs (202) → poll statusUrl → resultUrl → NC_BUOS_SITES."
      });

      MapUploadClient.StatusChanged += OnUploadStatus;
      FormClosed += (_, __) =>
      {
        MapUploadClient.StatusChanged -= OnUploadStatus;
        try { _calcCts?.Cancel(); }
        catch { }
        try { PersistOffsets(); }
        catch { }
        // Hide AOI frame; keep geometry.conf coords for next open.
        try { CadAoiOverlay.Hide(); }
        catch { }
      };

      RefreshCatalogLabels();
      UpdateAoiInfo(geo);
      if (geo.HasAoi)
      {
        try { CadAoiOverlay.Show(geo); }
        catch (Exception ex) { SetStatus("AOI: " + ex.Message); }
      }

      try { CadBuosSitesOverlay.SetVisible(_showBuos.Checked); }
      catch { }

      if (!string.IsNullOrEmpty(MapUploadClient.LastStatus))
      {
        SetStatus(MapUploadClient.LastStatus);
      }
      else
      {
        SetStatus("Выделите AOI → обновите карту (DatasetId) → «Рассчитать слой БУОС».");
      }
    }

    private void OnUploadStatus(string message)
    {
      if (IsDisposed)
      {
        return;
      }

      if (InvokeRequired)
      {
        BeginInvoke(new Action(() => SetStatus(message)));
        return;
      }

      SetStatus(message);
    }

    private void RefreshCatalogLabels()
    {
      try
      {
        AppPaths paths = AppPaths.Load();
        if (TryLargestBuos(paths.BuosWorkbook, out double bw, out double bl, out _))
        {
          _buosSize.Text = "Площадка БУОС ширина х длина - "
            + FormatDim(bw) + " х " + FormatDim(bl);
          _buosSize.ForeColor = SystemColors.ControlText;
        }
        else
        {
          _buosSize.Text = "Площадка БУОС ширина х длина - — (откройте каталог в «Настройки» → БУОС)";
          _buosSize.ForeColor = Color.DarkRed;
        }
      }
      catch (Exception ex)
      {
        _buosSize.Text = "Площадка БУОС: ошибка каталога — " + ex.Message;
        _buosSize.ForeColor = Color.DarkRed;
      }
    }

    /// <summary>
    /// Largest footprint: max area (W×L); ties → max width, then max length.
    /// </summary>
    internal static bool TryLargestBuos(string workbook, out double widthM, out double lengthM, out string name)
    {
      widthM = lengthM = 0;
      name = null;
      if (string.IsNullOrWhiteSpace(workbook) || !File.Exists(workbook))
      {
        return false;
      }

      var data = BuosWorkbookLoader.LoadWorkbook(workbook);
      var best = data.Variants
        .Where(v => v != null && v.WidthM > 0 && v.LengthM > 0)
        .OrderByDescending(v => v.WidthM * v.LengthM)
        .ThenByDescending(v => v.WidthM)
        .ThenByDescending(v => v.LengthM)
        .FirstOrDefault();
      if (best == null)
      {
        return false;
      }

      widthM = best.WidthM;
      lengthM = best.LengthM;
      name = best.Name;
      return true;
    }

    private void SelectArea()
    {
      if (_busy)
      {
        return;
      }

      try
      {
        PersistOffsets();
        Hide();
        if (!CadAoiOverlay.TrySelectRectangle(out double minX, out double minY, out double maxX, out double maxY))
        {
          Show();
          Activate();
          SetStatus("Выделение области отменено.");
          return;
        }

        GeometryConf.Set(c =>
        {
          ApplyOffsetsTo(c);
          c.SetAoi(minX, minY, maxX, maxY);
          c.ShowBuosLayer = _showBuos.Checked;
        });
        GeometryConf geo = GeometryConf.Load();
        CadAoiOverlay.Show(geo);
        UpdateAoiInfo(geo);
        SetStatus("Область сохранена в geometry.conf и подсвечена на чертеже.");
      }
      catch (Exception ex)
      {
        SetStatus("Ошибка выделения области: " + ex.Message);
      }
      finally
      {
        Show();
        Activate();
        BringToFront();
      }
    }

    private void ResetArea()
    {
      try
      {
        GeometryConf.Set(c =>
        {
          ApplyOffsetsTo(c);
          c.ClearAoi();
          c.ShowBuosLayer = _showBuos.Checked;
        });
        CadAoiOverlay.Clear();
        UpdateAoiInfo(GeometryConf.Load());
        SetStatus("Область сброшена.");
      }
      catch (Exception ex)
      {
        SetStatus("Ошибка сброса области: " + ex.Message);
      }
    }

    private async Task UploadMapAsync()
    {
      if (_busy)
      {
        return;
      }

      _busy = true;
      _btnUpload.Enabled = false;
      try
      {
        PersistOffsets();
        SetStatus("Сохранение DXF…");
        string dxf = CadDxfExport.SaveMapSidecar();
        SetStatus("Передача DXF на " + NetConf.Load().Host + "…");
        string datasetId = await MapUploadClient.UploadMapAsync(dxf).ConfigureAwait(true);
        NetConf.Set(c => c.DatasetId = datasetId);
        string ok = MapUploadClient.StatusOk + " (DatasetId=" + datasetId + " → net.conf)";
        MapUploadClient.PublishStatus(MapUploadClient.StatusOk);
        SetStatus(ok);
      }
      catch (Exception ex)
      {
        MapUploadClient.PublishStatus(MapUploadClient.StatusFail);
        SetStatus(MapUploadClient.StatusFail + ": " + ex.Message);
      }
      finally
      {
        _busy = false;
        _btnUpload.Enabled = true;
      }
    }

    private async Task CalculateBuosAsync()
    {
      if (_busy)
      {
        return;
      }

      _busy = true;
      _calcBuos.Enabled = false;
      _btnUpload.Enabled = false;
      _calcCts?.Dispose();
      _calcCts = new CancellationTokenSource();
      CancellationToken ct = _calcCts.Token;
      ShowProgress(indeterminate: true, message: "Запуск задачи…");
      try
      {
        PersistOffsets();
        GeometryConf geo = GeometryConf.Load();
        NetConf net = NetConf.Load();
        AppPaths paths = AppPaths.Load();
        if (!TryLargestBuos(paths.BuosWorkbook, out double widthM, out double lengthM, out string buosName))
        {
          throw new InvalidOperationException(
            "Нет каталога БУОС (ширина×длина). Откройте «Настройки» → БУОС.");
        }

        BuosSearchClient.BuosSearchRequest request =
          BuosSearchClient.BuildRequest(net, geo, widthM, lengthM);
        string path = string.IsNullOrWhiteSpace(net.BuosSearchPath)
          ? NetConf.DefaultBuosSearchPath
          : net.BuosSearchPath;
        SetStatus("Расчёт БУОС (async) → " + net.Host + "/" + path
          + " (datasetId=" + request.DatasetId
          + ", " + FormatDim(widthM) + "×" + FormatDim(lengthM) + " м"
          + (string.IsNullOrEmpty(buosName) ? string.Empty : ", «" + buosName + "»")
          + ")…");

        BuosSearchClient.BuosSearchResponse parsed = await BuosSearchClient.SearchJobAsync(
          request,
          onProgress: ApplyJobProgress,
          ct).ConfigureAwait(true);

        int siteCount = parsed.Sites?.Count ?? 0;
        int drawn = CadBuosSitesOverlay.Redraw(parsed.Sites, visible: _showBuos.Checked);

        string diag = BuosSearchClient.FormatDiagnostics(parsed.Diagnostics);
        string status = "БУОС: sites=" + siteCount + ", начерчено=" + drawn
          + " → " + CadBuosSitesOverlay.ResolveLayerName(net);
        if (!string.IsNullOrWhiteSpace(diag))
        {
          status += " | " + diag;
        }

        if (!_showBuos.Checked)
        {
          status += " (слой Off — включите «Показать слой БУОС»)";
        }

        SetStatus(status);
        HideProgress();
      }
      catch (OperationCanceledException)
      {
        HideProgress();
        SetStatus("Расчёт БУОС отменён.");
      }
      catch (Exception ex)
      {
        HideProgress();
        SetStatus("Ошибка расчёта БУОС: " + ex.Message);
      }
      finally
      {
        _busy = false;
        _calcBuos.Enabled = true;
        _btnUpload.Enabled = true;
      }
    }

    private void ApplyJobProgress(BuosSearchClient.BuosJobStatus status)
    {
      if (IsDisposed)
      {
        return;
      }

      void Apply()
      {
        if (status == null)
        {
          return;
        }

        string msg = string.IsNullOrWhiteSpace(status.Message)
          ? (status.Status ?? string.Empty)
          : status.Message.Trim();
        if (status.Total > 0)
        {
          ShowProgress(
            indeterminate: false,
            message: msg,
            total: status.Total,
            completed: status.Completed);
        }
        else
        {
          ShowProgress(indeterminate: true, message: msg);
        }

        if (!string.IsNullOrWhiteSpace(msg))
        {
          SetStatus(msg);
        }
      }

      if (InvokeRequired)
      {
        BeginInvoke(new Action(Apply));
      }
      else
      {
        Apply();
      }
    }

    private void ShowProgress(bool indeterminate, string message, int total = 0, int completed = 0)
    {
      _progressPanel.Visible = true;
      _progressMsg.Text = message ?? string.Empty;
      if (indeterminate || total <= 0)
      {
        if (_progressBar.Style != ProgressBarStyle.Marquee)
        {
          _progressBar.Style = ProgressBarStyle.Marquee;
          _progressBar.MarqueeAnimationSpeed = 30;
        }
      }
      else
      {
        if (_progressBar.Style != ProgressBarStyle.Continuous)
        {
          _progressBar.Style = ProgressBarStyle.Continuous;
        }

        int max = Math.Max(1, total);
        int val = completed;
        if (val < 0)
        {
          val = 0;
        }

        if (val > max)
        {
          val = max;
        }

        if (_progressBar.Maximum != max)
        {
          _progressBar.Maximum = max;
        }

        _progressBar.Value = val;
      }
    }

    private void HideProgress()
    {
      try
      {
        _progressBar.Style = ProgressBarStyle.Blocks;
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Value = 0;
        _progressMsg.Text = string.Empty;
        _progressPanel.Visible = false;
      }
      catch
      {
      }
    }

    private void PersistOffsets()
    {
      GeometryConf.Set(c =>
      {
        ApplyOffsetsTo(c);
        c.ShowBuosLayer = _showBuos.Checked;
      });
    }

    private void PersistUiFlags()
    {
      try
      {
        GeometryConf.Set(c =>
        {
          c.ShowBuosLayer = _showBuos.Checked;
        });
        try { CadBuosSitesOverlay.SetVisible(_showBuos.Checked); }
        catch (Exception ex)
        {
          SetStatus("Слой БУОС: " + ex.Message);
          return;
        }

        SetStatus(_showBuos.Checked
          ? "Слой БУОС On (" + CadBuosSitesOverlay.ResolveLayerName() + ")."
          : "Слой БУОС Off (геометрия сохранена).");
      }
      catch
      {
      }
    }

    private void ApplyOffsetsTo(GeometryConf c)
    {
      c.BuosRiverBufferM = ReadNonNeg(_buosRiver, "БУОС → река");
      c.BuosInfraBufferM = ReadNonNeg(_buosInfra, "БУОС → дороги/трубы");
      c.NodeRiverBufferM = ReadNonNeg(_nodeRiver, "Узлы → река");
    }

    private static double ReadNonNeg(TextBox box, string label)
    {
      if (!double.TryParse((box.Text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
          || !double.IsFinite(value) || value < 0)
      {
        throw new ArgumentException("Отступ «" + label + "» должен быть конечным неотрицательным числом.");
      }

      return value;
    }

    private void UpdateAoiInfo(GeometryConf geo)
    {
      if (geo == null || !geo.HasAoi)
      {
        _aoiInfo.Text = "Область интереса: не задана (общая для БУОС и узлов).";
        return;
      }

      _aoiInfo.Text = "Область интереса: ("
        + FormatDim(geo.MinX) + "; " + FormatDim(geo.MinY) + ") — ("
        + FormatDim(geo.MaxX) + "; " + FormatDim(geo.MaxY) + ")"
        + "  ΔX=" + FormatDim(geo.MaxX - geo.MinX)
        + "  ΔY=" + FormatDim(geo.MaxY - geo.MinY);
    }

    private void SetStatus(string text)
    {
      _status.Text = text ?? string.Empty;
      bool ok = text != null && text.StartsWith(MapUploadClient.StatusOk, StringComparison.Ordinal);
      bool fail = text != null && text.IndexOf(MapUploadClient.StatusFail, StringComparison.Ordinal) >= 0;
      _status.ForeColor = fail ? Color.DarkRed : ok ? Color.DarkGreen : Color.DimGray;
    }

    private static Label MakeLabel(string text)
    {
      return new Label
      {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 4, 0, 2)
      };
    }

    private static TextBox MakeNum(double value)
    {
      return new TextBox
      {
        Width = 160,
        Text = value.ToString("0.###", CultureInfo.CurrentCulture),
        Margin = new Padding(0, 0, 0, 4)
      };
    }

    private static string FormatDim(double value)
    {
      return value.ToString("0.###", CultureInfo.CurrentCulture);
    }
  }
}
