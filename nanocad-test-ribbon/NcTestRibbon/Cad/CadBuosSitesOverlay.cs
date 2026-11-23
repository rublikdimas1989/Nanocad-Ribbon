namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using HostMgd.ApplicationServices;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbPolyline = Teigha.DatabaseServices.Polyline;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Net;

  /// <summary>
  /// BUOS search footprints on locked layer NC_BUOS_SITES (clear → redraw → lock).
  /// Checkbox «Показать слой БУОС» toggles layer IsOff only.
  /// </summary>
  public static class CadBuosSitesOverlay
  {
    public const string DefaultLayerName = "NC_BUOS_SITES";
    private const string XDataApp = "NC_BUOS_SITE";

    /// <summary>CAD layer for drawn sites (net.conf BuosCadLayer, else NC_BUOS_SITES).</summary>
    public static string ResolveLayerName(NetConf net = null)
    {
      net ??= NetConf.Load();
      string name = (net.BuosCadLayer ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(name))
      {
        name = DefaultLayerName;
      }

      return name;
    }

    /// <summary>
    /// Erase previous sites on the layer, draw footprints + id labels, lock layer.
    /// </summary>
    public static int Redraw(IReadOnlyList<BuosSearchClient.BuosSiteDto> sites, bool visible)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");

      string layerName = ResolveLayerName();
      int drawn = 0;
      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, layerName, unlockForWrite: true);
        EraseLayerEntities(tr, model, layerName);
        if (sites != null)
        {
          foreach (BuosSearchClient.BuosSiteDto site in sites)
          {
            if (TryAddFootprint(tr, model, db, layerName, site))
            {
              drawn++;
              AddLabel(tr, model, db, layerName, site);
            }
          }
        }

        SetLayerOff(tr, db, layerName, off: !visible);
        LockLayer(tr, db, layerName);
      });
      TryRegen(doc);
      return drawn;
    }

    public static void Clear()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      string layerName = ResolveLayerName();
      RunWrite(doc, (tr, model, db) =>
      {
        UnlockLayer(tr, db, layerName);
        EraseLayerEntities(tr, model, layerName);
        LockLayer(tr, db, layerName);
      });
      TryRegen(doc);
    }

    /// <summary>Visibility only (layer IsOff). Geometry stays.</summary>
    public static void SetVisible(bool visible)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      string layerName = ResolveLayerName();
      RunWrite(doc, (tr, model, db) =>
      {
        UnlockLayer(tr, db, layerName);
        if (!LayerExists(tr, db, layerName))
        {
          return;
        }

        SetLayerOff(tr, db, layerName, off: !visible);
        LockLayer(tr, db, layerName);
      });
      TryRegen(doc);
    }

    private static bool TryAddFootprint(
      Transaction tr,
      BlockTableRecord model,
      Database db,
      string layerName,
      BuosSearchClient.BuosSiteDto site)
    {
      if (site?.Footprint == null || site.Footprint.Count < 3)
      {
        return false;
      }

      var pl = new DbPolyline();
      pl.SetDatabaseDefaults(db);
      pl.Layer = layerName;
      pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 3); // green
      pl.Visible = true;
      pl.ConstantWidth = 0;

      int idx = 0;
      foreach (double[] pt in site.Footprint)
      {
        if (pt == null || pt.Length < 2 || !double.IsFinite(pt[0]) || !double.IsFinite(pt[1]))
        {
          continue;
        }

        pl.AddVertexAt(idx++, new Point2d(pt[0], pt[1]), 0, 0, 0);
      }

      if (pl.NumberOfVertices < 3)
      {
        pl.Dispose();
        return false;
      }

      pl.Closed = true;
      model.AppendEntity(pl);
      tr.AddNewlyCreatedDBObject(pl, true);
      SetSiteId(tr, db, pl, site.Id);
      return true;
    }

    private static void AddLabel(
      Transaction tr,
      BlockTableRecord model,
      Database db,
      string layerName,
      BuosSearchClient.BuosSiteDto site)
    {
      string id = (site.Id ?? string.Empty).Trim();
      if (id.Length == 0)
      {
        return;
      }

      double x = site.X;
      double y = site.Y;
      if (!double.IsFinite(x) || !double.IsFinite(y))
      {
        // Fallback: footprint centroid.
        if (!TryCentroid(site.Footprint, out x, out y))
        {
          return;
        }
      }

      double height = 2.5;
      try
      {
        double configured = InterfaceConf.Load().LabelFontHeightM;
        if (configured > 0)
        {
          height = configured;
        }
      }
      catch
      {
      }

      var text = new DBText();
      text.SetDatabaseDefaults(db);
      text.Layer = layerName;
      text.Color = Color.FromColorIndex(ColorMethod.ByAci, 2); // yellow
      text.Visible = true;
      text.Height = height;
      text.TextString = id;
      text.HorizontalMode = TextHorizontalMode.TextCenter;
      text.VerticalMode = TextVerticalMode.TextVerticalMid;
      text.AlignmentPoint = new Point3d(x, y, 0);
      text.Position = text.AlignmentPoint;
      try { text.AdjustAlignment(db); }
      catch { }

      model.AppendEntity(text);
      tr.AddNewlyCreatedDBObject(text, true);
      SetSiteId(tr, db, text, id);
    }

    private static bool TryCentroid(IList<double[]> ring, out double x, out double y)
    {
      x = y = 0;
      if (ring == null || ring.Count == 0)
      {
        return false;
      }

      int n = 0;
      double sx = 0, sy = 0;
      foreach (double[] pt in ring)
      {
        if (pt == null || pt.Length < 2 || !double.IsFinite(pt[0]) || !double.IsFinite(pt[1]))
        {
          continue;
        }

        sx += pt[0];
        sy += pt[1];
        n++;
      }

      if (n == 0)
      {
        return false;
      }

      x = sx / n;
      y = sy / n;
      return true;
    }

    private static void SetSiteId(Transaction tr, Database db, Entity entity, string id)
    {
      try
      {
        RegAppTable apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!apps.Has(XDataApp))
        {
          apps.UpgradeOpen();
          var record = new RegAppTableRecord { Name = XDataApp };
          apps.Add(record);
          tr.AddNewlyCreatedDBObject(record, true);
        }

        entity.XData = new ResultBuffer(
          new TypedValue((int)DxfCode.ExtendedDataRegAppName, XDataApp),
          new TypedValue((int)DxfCode.ExtendedDataAsciiString, id ?? string.Empty));
      }
      catch
      {
        // XData optional.
      }
    }

    private static void RunWrite(Document doc, Action<Transaction, BlockTableRecord, Database> action)
    {
      Database db = doc.Database;
      using (doc.LockDocument())
      using (Transaction tr = db.TransactionManager.StartTransaction())
      {
        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        ObjectId msId = bt[BlockTableRecord.ModelSpace];
        BlockTableRecord model = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
        action(tr, model, db);
        tr.Commit();
      }
    }

    private static bool LayerExists(Transaction tr, Database db, string layerName)
    {
      LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      return lt.Has(layerName);
    }

    private static void EnsureLayer(Transaction tr, Database db, string layerName, bool unlockForWrite)
    {
      LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      ObjectId layerId;
      if (!lt.Has(layerName))
      {
        lt.UpgradeOpen();
        var layer = new LayerTableRecord
        {
          Name = layerName,
          Color = Color.FromColorIndex(ColorMethod.ByAci, 3)
        };
        layerId = lt.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
      }
      else
      {
        layerId = lt[layerName];
      }

      LayerTableRecord rec = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
      rec.IsOff = false;
      if (unlockForWrite)
      {
        rec.IsLocked = false;
      }
    }

    private static void SetLayerOff(Transaction tr, Database db, string layerName, bool off)
    {
      try
      {
        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName))
        {
          return;
        }

        LayerTableRecord rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
        rec.IsOff = off;
      }
      catch
      {
      }
    }

    private static void LockLayer(Transaction tr, Database db, string layerName)
    {
      try
      {
        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName))
        {
          return;
        }

        LayerTableRecord rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
        rec.IsLocked = true;
      }
      catch
      {
      }
    }

    private static void UnlockLayer(Transaction tr, Database db, string layerName)
    {
      try
      {
        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(layerName))
        {
          return;
        }

        LayerTableRecord rec = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
        rec.IsLocked = false;
      }
      catch
      {
      }
    }

    private static void EraseLayerEntities(Transaction tr, BlockTableRecord model, string layerName)
    {
      foreach (ObjectId id in model)
      {
        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
        if (ent == null)
        {
          continue;
        }

        string layer;
        try { layer = ent.Layer; }
        catch { continue; }

        if (!string.Equals(layer, layerName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        ent.UpgradeOpen();
        ent.Erase();
      }
    }

    private static void TryRegen(Document doc)
    {
      try { doc?.Editor?.Regen(); }
      catch { }
    }
  }
}
