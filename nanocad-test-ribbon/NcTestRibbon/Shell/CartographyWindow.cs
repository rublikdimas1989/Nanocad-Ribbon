namespace NcTestRibbon.Shell
{
  using System;
  using System.Drawing;
  using System.IO;
  using System.Windows.Forms;
  using NcTestRibbon.Cad;
  using NcTestRibbon.Configuration;

  /// <summary>
  /// End-style «Картография»: Forest / Height / космоснимок toggles + opacity,
  /// open/colorize buttons; rasters on locked layers (Off when unchecked).
  /// </summary>
  public sealed class CartographyWindow : Form
  {
    private bool _suppress;

    private readonly CheckBox _chkForest;
    private readonly CheckBox _chkHeight;
    private readonly CheckBox _chkScreen;
    private readonly TextBox _txtForestOpacity;
    private readonly TextBox _txtHeightOpacity;
    private readonly TextBox _txtScreenOpacity;
    private readonly Label _status;
    private readonly Label _mapsHint;

    public CartographyWindow()
    {
      Text = "Картография";
      Width = 520;
      Height = 340;
      MinimumSize = new Size(460, 300);
      StartPosition = FormStartPosition.CenterParent;
      FormBorderStyle = FormBorderStyle.Sizable;
      ShowInTaskbar = true;
      Font = new Font("Segoe UI", 9f);

      var root = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 5,
        Padding = new Padding(10)
      };
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
      Controls.Add(root);

      _mapsHint = new Label
      {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Text = "Maps: " + TracingCache.MapsDir,
        AutoEllipsis = true
      };
      root.Controls.Add(_mapsHint, 0, 0);

      BuildLayerRow(
        root,
        1,
        "Лес (Forest)",
        "colorize → Maps\\Forest_geo.tif",
        out _chkForest,
        out _txtForestOpacity,
        () => ToggleKind(CadMapRaster.Kind.Forest, _chkForest.Checked, ReadOpacity(_txtForestOpacity, 50)),
        () => ApplyOpacity(CadMapRaster.Kind.Forest, _txtForestOpacity, 50),
        () => OpenKind(CadMapRaster.Kind.Forest, _txtForestOpacity, 50),
        defaultOpacity: 50);

      BuildLayerRow(
        root,
        2,
        "Высоты (Height)",
        "colorize → Maps\\Height_geo.tif",
        out _chkHeight,
        out _txtHeightOpacity,
        () => ToggleKind(CadMapRaster.Kind.Height, _chkHeight.Checked, ReadOpacity(_txtHeightOpacity, 100)),
        () => ApplyOpacity(CadMapRaster.Kind.Height, _txtHeightOpacity, 100),
        () => OpenKind(CadMapRaster.Kind.Height, _txtHeightOpacity, 100),
        defaultOpacity: 100);

      BuildLayerRow(
        root,
        3,
        "Космоснимок (Screen_ESRI)",
        "TIF→ECW / ECW → Maps\\Screen_ESRI*",
        out _chkScreen,
        out _txtScreenOpacity,
        () => ToggleKind(CadMapRaster.Kind.Screen, _chkScreen.Checked, ReadOpacity(_txtScreenOpacity, 100)),
        () => ApplyOpacity(CadMapRaster.Kind.Screen, _txtScreenOpacity, 100),
        () => OpenKind(CadMapRaster.Kind.Screen, _txtScreenOpacity, 100),
        defaultOpacity: 100);

      _status = new Label
      {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopLeft,
        ForeColor = Color.DimGray,
        Text = RasterGeoTransform.PackageStamp()
      };
      root.Controls.Add(_status, 0, 4);

      Load += (_, __) => RefreshFromDrawingAndPaths();
    }

    private void BuildLayerRow(
      TableLayoutPanel root,
      int row,
      string title,
      string hint,
      out CheckBox chk,
      out TextBox opacity,
      Action onToggle,
      Action onOpacity,
      Action onOpen,
      int defaultOpacity)
    {
      var box = new GroupBox
      {
        Dock = DockStyle.Fill,
        Text = title,
        Padding = new Padding(8, 4, 8, 4)
      };
      root.Controls.Add(box, 0, row);

      var grid = new TableLayoutPanel
      {
        Dock = DockStyle.Fill,
        ColumnCount = 5,
        RowCount = 1
      };
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 28));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
      box.Controls.Add(grid);

      chk = new CheckBox
      {
        Text = "Показать",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Checked = true
      };
      chk.CheckedChanged += (_, __) =>
      {
        if (_suppress) return;
        onToggle();
      };
      grid.Controls.Add(chk, 0, 0);

      var lbl = new Label
      {
        Text = "Прозр.:",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 0, 0)
      };
      grid.Controls.Add(lbl, 1, 0);

      opacity = new TextBox
      {
        Text = defaultOpacity.ToString(),
        Width = 40,
        Anchor = AnchorStyles.Left,
        TextAlign = HorizontalAlignment.Center
      };
      opacity.LostFocus += (_, __) =>
      {
        if (_suppress) return;
        onOpacity();
      };
      opacity.KeyDown += (s, e) =>
      {
        if (e.KeyCode == Keys.Enter)
        {
          e.SuppressKeyPress = true;
          onOpacity();
        }
      };
      grid.Controls.Add(opacity, 2, 0);

      var pct = new Label
      {
        Text = "%",
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 6, 0, 0)
      };
      grid.Controls.Add(pct, 3, 0);

      var open = new Button
      {
        Text = "Открыть…",
        Dock = DockStyle.Fill,
        Height = 28
      };
      open.Click += (_, __) => onOpen();
      grid.Controls.Add(open, 4, 0);

      box.Tag = hint;
    }

    private void RefreshFromDrawingAndPaths()
    {
      AppPaths paths = AppPaths.Load();
      _suppress = true;
      try
      {
        _txtForestOpacity.Text = CadMapRaster.ClampOpacity(paths.ForestOpacity).ToString();
        _txtHeightOpacity.Text = CadMapRaster.ClampOpacity(paths.HeightOpacity).ToString();
        _txtScreenOpacity.Text = CadMapRaster.ClampOpacity(paths.ScreenOpacity).ToString();

        // Reflect drawing state only (Maps presence shown in status).
        _chkForest.Checked = CadMapRaster.HasDrawingImage(CadMapRaster.Kind.Forest)
          && CadMapRaster.IsLayerOn(CadMapRaster.Kind.Forest);
        _chkHeight.Checked = CadMapRaster.HasDrawingImage(CadMapRaster.Kind.Height)
          && CadMapRaster.IsLayerOn(CadMapRaster.Kind.Height);
        _chkScreen.Checked = CadMapRaster.HasDrawingImage(CadMapRaster.Kind.Screen)
          && CadMapRaster.IsLayerOn(CadMapRaster.Kind.Screen);

        _status.Text = RasterGeoTransform.PackageStamp()
          + "\nMaps Forest_geo=" + (CadMapRaster.FindMapsFile(CadMapRaster.Kind.Forest) != null ? "yes" : "no")
          + " Height_geo=" + (CadMapRaster.FindMapsFile(CadMapRaster.Kind.Height) != null ? "yes" : "no")
          + " Screen_ESRI=" + (CadMapRaster.FindMapsFile(CadMapRaster.Kind.Screen) != null ? "yes" : "no");
      }
      catch (Exception ex)
      {
        _status.Text = "Refresh: " + ex.Message;
      }
      finally
      {
        _suppress = false;
      }
    }

    private void ToggleKind(CadMapRaster.Kind kind, bool show, int opacity)
    {
      try
      {
        if (!show)
        {
          if (CadMapRaster.HasDrawingImage(kind) || LayerExistsSafe(kind))
          {
            CadMapRaster.SetVisible(kind, false);
          }

          PersistShow(kind, false);
          SetStatus(CadMapRaster.DisplayName(kind) + ": слой Off (lightbulb)");
          return;
        }

        CadRasterAttach.Result existing = CadMapRaster.TryShowExisting(kind, opacity, out string note);
        if (existing != null)
        {
          PersistShow(kind, true);
          if (!string.IsNullOrEmpty(existing.ImageId.ToString()) && !existing.ImageId.IsNull)
          {
            CadRasterAttach.ZoomToImage(existing.ImageId);
          }

          SetStatus(note);
          return;
        }

        // Maps missing → open file then insert.
        string picked = PromptOpen(kind);
        if (string.IsNullOrEmpty(picked))
        {
          _suppress = true;
          try
          {
            CheckBox box = BoxFor(kind);
            if (box != null) box.Checked = false;
          }
          finally
          {
            _suppress = false;
          }

          PersistShow(kind, false);
          SetStatus(CadMapRaster.DisplayName(kind) + ": отменено (Maps пуст)");
          return;
        }

        CadRasterAttach.Result attached = CadMapRaster.OpenAndAttach(kind, picked, opacity, out string openNote);
        PersistShow(kind, true);
        PersistBackground(attached.Path);
        CadRasterAttach.ZoomToImage(attached.ImageId);
        SetStatus(openNote);
      }
      catch (Exception ex)
      {
        _suppress = true;
        try
        {
          CheckBox box = BoxFor(kind);
          if (box != null) box.Checked = false;
        }
        finally
        {
          _suppress = false;
        }

        PersistShow(kind, false);
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        SetStatus(CadMapRaster.DisplayName(kind) + ": " + ex.Message);
      }
    }

    private void OpenKind(CadMapRaster.Kind kind, TextBox opacityBox, int defaultOpacity)
    {
      try
      {
        string picked = PromptOpen(kind);
        if (string.IsNullOrEmpty(picked))
        {
          SetStatus(CadMapRaster.DisplayName(kind) + ": открытие отменено");
          return;
        }

        int opacity = ReadOpacity(opacityBox, defaultOpacity);
        CadRasterAttach.Result attached = CadMapRaster.OpenAndAttach(kind, picked, opacity, out string note);
        _suppress = true;
        try
        {
          CheckBox box = BoxFor(kind);
          if (box != null) box.Checked = true;
        }
        finally
        {
          _suppress = false;
        }

        PersistShow(kind, true);
        PersistBackground(attached.Path);
        CadRasterAttach.ZoomToImage(attached.ImageId);
        SetStatus(note);
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        SetStatus(CadMapRaster.DisplayName(kind) + ": " + ex.Message);
      }
    }

    private void ApplyOpacity(CadMapRaster.Kind kind, TextBox box, int defaultOpacity)
    {
      try
      {
        int opacity = ReadOpacity(box, defaultOpacity);
        box.Text = opacity.ToString();
        PersistOpacity(kind, opacity);
        if (CadMapRaster.HasDrawingImage(kind))
        {
          CadMapRaster.SetOpacity(kind, opacity);
          SetStatus(CadMapRaster.DisplayName(kind) + ": Transparency "
            + CadRasterAttach.OpacityToTransparencyValue(opacity)
            + " (opacity " + opacity + "%)");
        }
        else
        {
          SetStatus(CadMapRaster.DisplayName(kind) + ": opacity " + opacity + "% (слой ещё не вставлен)");
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        SetStatus(ex.Message);
      }
    }

    private string PromptOpen(CadMapRaster.Kind kind)
    {
      using (OpenFileDialog dlg = new OpenFileDialog())
      {
        if (kind == CadMapRaster.Kind.Forest)
        {
          dlg.Title = "Forest.tif → colorize → Maps\\Forest_geo.tif";
          dlg.Filter = "GeoTIFF Forest (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
        }
        else if (kind == CadMapRaster.Kind.Height)
        {
          dlg.Title = "Height.tif → colorize → Maps\\Height_geo.tif";
          dlg.Filter = "GeoTIFF Height (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
        }
        else
        {
          dlg.Title = "Screen_ESRI / космоснимок (ECW или GeoTIFF)";
          dlg.Filter =
            "Растры (*.ecw;*.tif;*.tiff)|*.ecw;*.tif;*.tiff|ECW (*.ecw)|*.ecw|GeoTIFF (*.tif;*.tiff)|*.tif;*.tiff|Все файлы (*.*)|*.*";
        }

        dlg.CheckFileExists = true;
        dlg.Multiselect = false;
        string maps = TracingCache.MapsDir;
        if (Directory.Exists(maps))
        {
          dlg.InitialDirectory = maps;
        }

        string remembered = AppPaths.Load().Background;
        if (!string.IsNullOrWhiteSpace(remembered) && File.Exists(remembered))
        {
          try
          {
            dlg.InitialDirectory = Path.GetDirectoryName(remembered) ?? maps;
            dlg.FileName = remembered;
          }
          catch
          {
          }
        }

        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
      }
    }

    private CheckBox BoxFor(CadMapRaster.Kind kind)
    {
      switch (kind)
      {
        case CadMapRaster.Kind.Forest: return _chkForest;
        case CadMapRaster.Kind.Height: return _chkHeight;
        case CadMapRaster.Kind.Screen: return _chkScreen;
        default: return null;
      }
    }

    private static bool LayerExistsSafe(CadMapRaster.Kind kind)
    {
      try
      {
        return CadMapRaster.HasDrawingImage(kind);
      }
      catch
      {
        return false;
      }
    }

    private static int ReadOpacity(TextBox box, int fallback)
    {
      if (box != null && int.TryParse(box.Text.Trim(), out int v))
      {
        return CadMapRaster.ClampOpacity(v);
      }

      return CadMapRaster.ClampOpacity(fallback);
    }

    private static void PersistShow(CadMapRaster.Kind kind, bool show)
    {
      AppPaths.Set(p =>
      {
        switch (kind)
        {
          case CadMapRaster.Kind.Forest: p.ShowForestMap = show; break;
          case CadMapRaster.Kind.Height: p.ShowHeightMap = show; break;
          case CadMapRaster.Kind.Screen: p.ShowScreenMap = show; break;
        }
      });
    }

    private static void PersistOpacity(CadMapRaster.Kind kind, int opacity)
    {
      AppPaths.Set(p =>
      {
        switch (kind)
        {
          case CadMapRaster.Kind.Forest: p.ForestOpacity = opacity; break;
          case CadMapRaster.Kind.Height: p.HeightOpacity = opacity; break;
          case CadMapRaster.Kind.Screen: p.ScreenOpacity = opacity; break;
        }
      });
    }

    private static void PersistBackground(string path)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        return;
      }

      AppPaths.Set(p => p.Background = path);
    }

    private void SetStatus(string text)
    {
      _status.Text = (text ?? string.Empty).Trim();
    }
  }
}
