namespace NcTestRibbon.Shell
{
  using System;
  using System.Drawing;
  using System.Globalization;
  using System.IO;
  using System.Linq;
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
    private readonly Label _nodeSize;
    private readonly TextBox _buosRiver;
    private readonly TextBox _buosInfra;
    private readonly TextBox _nodeRiver;
    private readonly Label _aoiInfo;
    private readonly Label _status;
    private readonly CheckBox _showBuos;
    private readonly CheckBox _showNodes;
    private readonly Button _btnUpload;
    private bool _busy;

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
        Margin = new Padding(0, 0, 0, 6),
        Font = new Font(Font, FontStyle.Bold)
      };
      root.Controls.Add(_buosSize);

      _nodeSize = new Label
      {
        AutoSize = true,
        MaximumSize = new Size(600, 0),
        Margin = new Padding(0, 0, 0, 12),
        Font = new Font(Font, FontStyle.Bold)
      };
      root.Controls.Add(_nodeSize);

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

      var calcBuos = new Button
      {
        Text = "Рассчитать слой БУОС",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      calcBuos.Click += (_, __) => SetStatus("Расчёт слоя БУОС: пока не реализовано.");
      calcRow.Controls.Add(calcBuos);

      var calcNodes = new Button
      {
        Text = "Рассчитать слой узлов",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      calcNodes.Click += (_, __) => SetStatus("Расчёт слоя узлов: пока не реализовано.");
      calcRow.Controls.Add(calcNodes);

      var cancelCalc = new Button
      {
        Text = "Отменить расчёт",
        AutoSize = true,
        Margin = new Padding(0, 0, 8, 4)
      };
      cancelCalc.Click += (_, __) => SetStatus("Нет активного расчёта.");
      calcRow.Controls.Add(cancelCalc);

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

      _showNodes = new CheckBox
      {
        Text = "Показать слой узлов",
        AutoSize = true,
        Checked = geo.ShowNodesLayer,
        Margin = new Padding(0, 0, 0, 8)
      };
      _showNodes.CheckedChanged += (_, __) => PersistUiFlags();
      root.Controls.Add(_showNodes);

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
        Text = "Центры перебираются с шагом 50 м. Для БУОС проверяются 0°, 30°, 60°, 90°, 120°, 150°. "
          + "Расчёт слоёв в этой сборке ещё не перенесён — кнопки расчёта показывают статус. "
          + "Общая прямоугольная область интереса сохраняется в geometry.conf и подсвечивается, пока окно открыто. "
          + "«Обновить карту на сервере» пишет DXF рядом с чертежом и загружает DXF+terrain на хост из net.conf."
      });

      MapUploadClient.StatusChanged += OnUploadStatus;
      FormClosed += (_, __) =>
      {
        MapUploadClient.StatusChanged -= OnUploadStatus;
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

      if (!string.IsNullOrEmpty(MapUploadClient.LastStatus))
      {
        SetStatus(MapUploadClient.LastStatus);
      }
      else
      {
        SetStatus("Слои выключены. Выделите область; расчёт слоёв — позже. Можно обновить карту на сервере.");
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
        if (TryLargestBuos(paths.BuosWorkbook, out double bw, out double bl, out string bName))
        {
          _buosSize.Text = "Площадка БУОС ширина х длина - "
            + FormatDim(bw) + " х " + FormatDim(bl)
            + (string.IsNullOrEmpty(bName) ? string.Empty : "  («" + bName + "»)");
          _buosSize.ForeColor = SystemColors.ControlText;
        }
        else
        {
          _buosSize.Text = "Площадка БУОС ширина х длина - — (откройте каталог во «Исходные данные» → БУОС)";
          _buosSize.ForeColor = Color.DarkRed;
        }

        if (TryLargestNode(paths.NodesWorkbook, out double nw, out double nl, out string nName))
        {
          _nodeSize.Text = "Площадка узла ширина х длина - "
            + FormatDim(nw) + " х " + FormatDim(nl)
            + (string.IsNullOrEmpty(nName) ? string.Empty : "  («" + nName + "»)");
          _nodeSize.ForeColor = SystemColors.ControlText;
        }
        else
        {
          _nodeSize.Text = "Площадка узла ширина х длина - — (откройте каталог во «Исходные данные» → Узлы)";
          _nodeSize.ForeColor = Color.DarkRed;
        }
      }
      catch (Exception ex)
      {
        _buosSize.Text = "Площадка БУОС: ошибка каталога — " + ex.Message;
        _nodeSize.Text = "Площадка узла: ошибка каталога — " + ex.Message;
        _buosSize.ForeColor = _nodeSize.ForeColor = Color.DarkRed;
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

    internal static bool TryLargestNode(string workbook, out double widthM, out double lengthM, out string name)
    {
      widthM = lengthM = 0;
      name = null;
      if (string.IsNullOrWhiteSpace(workbook) || !File.Exists(workbook))
      {
        return false;
      }

      var data = NodesWorkbookLoader.LoadWorkbook(workbook);
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
          c.ShowNodesLayer = _showNodes.Checked;
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
          c.ShowNodesLayer = _showNodes.Checked;
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
        if (!MapUploadClient.TryResolveTerrainPath(out string terrain, out string terrErr))
        {
          string fail = MapUploadClient.StatusFail + ": " + terrErr;
          MapUploadClient.PublishStatus(fail);
          SetStatus(fail);
          return;
        }

        SetStatus("Сохранение DXF…");
        string dxf = CadDxfExport.SaveMapSidecar();
        SetStatus("Передача DXF + terrain на " + NetConf.Load().Host + "…");
        string datasetId = await MapUploadClient.UploadMapAsync(dxf, terrain).ConfigureAwait(true);
        string ok = MapUploadClient.StatusOk + " (datasetId=" + datasetId + ")";
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

    private void PersistOffsets()
    {
      GeometryConf.Set(c =>
      {
        ApplyOffsetsTo(c);
        c.ShowBuosLayer = _showBuos.Checked;
        c.ShowNodesLayer = _showNodes.Checked;
      });
    }

    private void PersistUiFlags()
    {
      try
      {
        GeometryConf.Set(c =>
        {
          c.ShowBuosLayer = _showBuos.Checked;
          c.ShowNodesLayer = _showNodes.Checked;
        });
        if (_showBuos.Checked || _showNodes.Checked)
        {
          SetStatus("Показ слоёв запомнен; расчёт слоёв пока не реализован — на чертеже только AOI.");
        }
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
