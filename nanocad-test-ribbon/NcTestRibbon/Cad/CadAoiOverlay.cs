namespace NcTestRibbon.Cad
{
  using System;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbPolyline = Teigha.DatabaseServices.Polyline;
  using NcTestRibbon.Configuration;

  /// <summary>
  /// Shared rectangular AOI on locked layer NC_AOI (one rectangle for BUOS + nodes).
  /// </summary>
  public static class CadAoiOverlay
  {
    public const string LayerName = "NC_AOI";

    public static void Clear()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      RunWrite(doc, (tr, model, db) =>
      {
        UnlockLayer(tr, db);
        EraseLayerEntities(tr, model);
        LockLayer(tr, db);
      });
      TryRegen(doc);
    }

    public static void Show(GeometryConf aoi)
    {
      if (aoi == null || !aoi.HasAoi)
      {
        Clear();
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");

      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        EraseLayerEntities(tr, model);
        AddRectangle(tr, model, db, aoi.MinX, aoi.MinY, aoi.MaxX, aoi.MaxY);
        LockLayer(tr, db);
      });
      TryRegen(doc);
    }

    /// <summary>
    /// Two-corner pick (GetPoint + GetCorner). Returns false if cancelled.
    /// </summary>
    public static bool TrySelectRectangle(out double minX, out double minY, out double maxX, out double maxY)
    {
      minX = minY = maxX = maxY = 0;
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Editor ed = doc.Editor
        ?? throw new InvalidOperationException("Нет редактора активного чертежа.");

      PromptPointOptions opt1 = new PromptPointOptions("\nУгол 1 области интереса: ")
      {
        AllowNone = false
      };
      PromptPointResult r1 = ed.GetPoint(opt1);
      if (r1.Status != PromptStatus.OK)
      {
        ed.WriteMessage("\nВыделение области отменено.");
        return false;
      }

      PromptCornerOptions opt2 = new PromptCornerOptions("\nПротивоположный угол: ", r1.Value)
      {
        AllowNone = false,
        UseDashedLine = true
      };
      PromptPointResult r2 = ed.GetCorner(opt2);
      if (r2.Status != PromptStatus.OK)
      {
        ed.WriteMessage("\nВыделение области отменено.");
        return false;
      }

      minX = Math.Min(r1.Value.X, r2.Value.X);
      maxX = Math.Max(r1.Value.X, r2.Value.X);
      minY = Math.Min(r1.Value.Y, r2.Value.Y);
      maxY = Math.Max(r1.Value.Y, r2.Value.Y);
      if (!(maxX > minX) || !(maxY > minY))
      {
        ed.WriteMessage("\nОбласть слишком мала.");
        return false;
      }

      ed.WriteMessage(
        "\nAOI: (" + minX.ToString("0.###") + "; " + minY.ToString("0.###")
        + ") — (" + maxX.ToString("0.###") + "; " + maxY.ToString("0.###") + ")");
      return true;
    }

    private static void AddRectangle(
      Transaction tr,
      BlockTableRecord model,
      Database db,
      double minX,
      double minY,
      double maxX,
      double maxY)
    {
      var pl = new DbPolyline();
      pl.SetDatabaseDefaults(db);
      pl.Layer = LayerName;
      pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 1); // red
      pl.Visible = true;
      pl.ConstantWidth = 0;
      pl.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
      pl.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
      pl.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
      pl.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
      pl.Closed = true;
      model.AppendEntity(pl);
      tr.AddNewlyCreatedDBObject(pl, true);
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

    private static void EnsureLayer(Transaction tr, Database db, bool unlockForWrite)
    {
      LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      ObjectId layerId;
      if (!lt.Has(LayerName))
      {
        lt.UpgradeOpen();
        var layer = new LayerTableRecord
        {
          Name = LayerName,
          Color = Color.FromColorIndex(ColorMethod.ByAci, 1)
        };
        layerId = lt.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
      }
      else
      {
        layerId = lt[LayerName];
      }

      LayerTableRecord rec = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
      rec.IsOff = false;
      if (unlockForWrite)
      {
        rec.IsLocked = false;
      }
    }

    private static void LockLayer(Transaction tr, Database db)
    {
      try
      {
        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(LayerName))
        {
          return;
        }

        LayerTableRecord rec = (LayerTableRecord)tr.GetObject(lt[LayerName], OpenMode.ForWrite);
        rec.IsLocked = true;
      }
      catch
      {
      }
    }

    private static void UnlockLayer(Transaction tr, Database db)
    {
      try
      {
        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(LayerName))
        {
          return;
        }

        LayerTableRecord rec = (LayerTableRecord)tr.GetObject(lt[LayerName], OpenMode.ForWrite);
        rec.IsLocked = false;
      }
      catch
      {
      }
    }

    private static void EraseLayerEntities(Transaction tr, BlockTableRecord model)
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

        if (!string.Equals(layer, LayerName, StringComparison.OrdinalIgnoreCase))
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
