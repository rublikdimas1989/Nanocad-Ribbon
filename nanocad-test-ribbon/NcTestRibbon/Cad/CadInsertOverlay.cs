namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using NcTestRibbon.Configuration;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbCircle = Teigha.DatabaseServices.Circle;

  /// <summary>
  /// Insert-point (врезка) circles + labels on locked layer NC_INSERTS.
  /// Size from Tracing_cache\Settings\interface.conf.
  /// </summary>
  public static class CadInsertOverlay
  {
    public const string LayerName = "NC_INSERTS";
    private const short ColorIndex = 6; // magenta-ish, like End DarkMagenta

    public sealed class InsertGeom
    {
      public string Name { get; set; } = string.Empty;
      public double X { get; set; }
      public double Y { get; set; }
      public bool Enabled { get; set; } = true;
    }

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

    public static int ShowInserts(IEnumerable<InsertGeom> inserts)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      InsertGeom[] list = (inserts ?? Enumerable.Empty<InsertGeom>())
        .Where(p => p != null && p.Enabled && !string.IsNullOrWhiteSpace(p.Name)
          && double.IsFinite(p.X) && double.IsFinite(p.Y))
        .ToArray();

      InterfaceConf ui = InterfaceConf.Load();
      double diameter = Math.Max(ui.InsertCircleDiameterM, 0.1);
      double font = Math.Max(ui.LabelFontHeightM, 0.1);

      int drawn = 0;
      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        EraseLayerEntities(tr, model);
        foreach (InsertGeom point in list)
        {
          AddCircle(tr, model, db, point, diameter);
          AddLabel(tr, model, db, point, diameter, font);
          drawn++;
        }

        LockLayer(tr, db);
      });
      TryRegen(doc);
      return drawn;
    }

    public static void UpdateInsert(InsertGeom point)
    {
      if (point == null || string.IsNullOrWhiteSpace(point.Name))
      {
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      InterfaceConf ui = InterfaceConf.Load();
      double diameter = Math.Max(ui.InsertCircleDiameterM, 0.1);
      double font = Math.Max(ui.LabelFontHeightM, 0.1);

      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        EraseNamed(tr, model, point.Name);
        if (point.Enabled)
        {
          AddCircle(tr, model, db, point, diameter);
          AddLabel(tr, model, db, point, diameter, font);
        }

        LockLayer(tr, db);
      });
      TryRegen(doc);
    }

    public static bool TryPlace(InsertGeom point, out double x, out double y)
    {
      x = point?.X ?? 0;
      y = point?.Y ?? 0;
      if (point == null)
      {
        return false;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Editor ed = doc.Editor
        ?? throw new InvalidOperationException("Нет редактора активного чертежа.");

      try
      {
        ed.WriteMessage("\nУкажите новое положение врезки «" + point.Name + "»…");
      }
      catch
      {
      }

      var opts = new PromptPointOptions("\nНовое положение врезки «" + point.Name + "»")
      {
        AllowNone = false,
        UseBasePoint = true,
        BasePoint = new Point3d(point.X, point.Y, 0)
      };
      PromptPointResult res = ed.GetPoint(opts);
      if (res.Status != PromptStatus.OK)
      {
        ed.WriteMessage("\nРазмещение врезки отменено.");
        return false;
      }

      x = res.Value.X;
      y = res.Value.Y;
      ed.WriteMessage("\nВрезка «" + point.Name + "»: X=" + x.ToString("G17") + " Y=" + y.ToString("G17"));
      return true;
    }

    private static void AddCircle(Transaction tr, BlockTableRecord model, Database db, InsertGeom point, double diameter)
    {
      var circle = new DbCircle(new Point3d(point.X, point.Y, 0), Vector3d.ZAxis, Math.Max(diameter, 0.1) / 2.0);
      circle.SetDatabaseDefaults(db);
      circle.Layer = LayerName;
      circle.Color = Color.FromColorIndex(ColorMethod.ByAci, ColorIndex);
      model.AppendEntity(circle);
      tr.AddNewlyCreatedDBObject(circle, true);
      SetName(tr, db, circle, point.Name);
    }

    private static void AddLabel(Transaction tr, BlockTableRecord model, Database db, InsertGeom point, double diameter, double font)
    {
      double offset = Math.Max(diameter, 0.1) / 2.0 + Math.Max(font, 0.1) * 0.2;
      var text = new DBText();
      text.SetDatabaseDefaults(db);
      text.Layer = LayerName;
      text.Color = Color.FromColorIndex(ColorMethod.ByAci, ColorIndex);
      text.Height = Math.Max(font, 0.1);
      text.TextString = point.Name ?? string.Empty;
      text.HorizontalMode = TextHorizontalMode.TextLeft;
      text.VerticalMode = TextVerticalMode.TextBottom;
      text.AlignmentPoint = new Point3d(point.X + offset, point.Y + offset * 0.3, 0);
      text.Position = text.AlignmentPoint;
      try
      {
        text.AdjustAlignment(db);
      }
      catch
      {
      }

      model.AppendEntity(text);
      tr.AddNewlyCreatedDBObject(text, true);
      SetName(tr, db, text, point.Name);
    }

    private static void SetName(Transaction tr, Database db, Entity entity, string name)
    {
      try
      {
        if (!entity.ExtensionDictionary.IsNull)
        {
          // keep existing
        }

        entity.CreateExtensionDictionary();
        var dict = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
        var rec = new Xrecord();
        rec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, name ?? string.Empty));
        if (dict.Contains("NC_INSERT_NAME"))
        {
          dict.Remove("NC_INSERT_NAME");
        }

        dict.SetAt("NC_INSERT_NAME", rec);
        tr.AddNewlyCreatedDBObject(rec, true);
      }
      catch
      {
      }
    }

    private static string ReadName(Transaction tr, Entity entity)
    {
      try
      {
        if (entity.ExtensionDictionary.IsNull)
        {
          return null;
        }

        var dict = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead);
        if (!dict.Contains("NC_INSERT_NAME"))
        {
          return null;
        }

        var rec = (Xrecord)tr.GetObject(dict.GetAt("NC_INSERT_NAME"), OpenMode.ForRead);
        foreach (TypedValue tv in rec.Data)
        {
          if (tv.TypeCode == (int)DxfCode.Text)
          {
            return tv.Value as string;
          }
        }
      }
      catch
      {
      }

      return null;
    }

    private static void EraseNamed(Transaction tr, BlockTableRecord model, string name)
    {
      var erase = new List<ObjectId>();
      foreach (ObjectId id in model)
      {
        if (!id.IsValid || id.IsErased)
        {
          continue;
        }

        Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
        if (ent == null || !string.Equals(ent.Layer, LayerName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        if (string.Equals(ReadName(tr, ent), name, StringComparison.OrdinalIgnoreCase))
        {
          erase.Add(id);
        }
      }

      foreach (ObjectId id in erase)
      {
        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite, false);
        ent.Erase();
      }
    }

    private static void EraseLayerEntities(Transaction tr, BlockTableRecord model)
    {
      var erase = new List<ObjectId>();
      foreach (ObjectId id in model)
      {
        if (!id.IsValid || id.IsErased)
        {
          continue;
        }

        Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
        if (ent != null && string.Equals(ent.Layer, LayerName, StringComparison.OrdinalIgnoreCase))
        {
          erase.Add(id);
        }
      }

      foreach (ObjectId id in erase)
      {
        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite, false);
        ent.Erase();
      }
    }

    private static void EnsureLayer(Transaction tr, Database db, bool unlockForWrite)
    {
      var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      ObjectId layerId;
      if (!lt.Has(LayerName))
      {
        lt.UpgradeOpen();
        var layer = new LayerTableRecord
        {
          Name = LayerName,
          Color = Color.FromColorIndex(ColorMethod.ByAci, ColorIndex)
        };
        layerId = lt.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
      }
      else
      {
        layerId = lt[LayerName];
      }

      var record = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
      if (unlockForWrite)
      {
        record.IsLocked = false;
      }
    }

    private static void LockLayer(Transaction tr, Database db)
    {
      var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      if (!lt.Has(LayerName))
      {
        return;
      }

      var record = (LayerTableRecord)tr.GetObject(lt[LayerName], OpenMode.ForWrite);
      record.IsLocked = true;
    }

    private static void UnlockLayer(Transaction tr, Database db)
    {
      var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      if (!lt.Has(LayerName))
      {
        return;
      }

      var record = (LayerTableRecord)tr.GetObject(lt[LayerName], OpenMode.ForWrite);
      record.IsLocked = false;
    }

    private static void RunWrite(Document doc, Action<Transaction, BlockTableRecord, Database> work)
    {
      Database db = doc.Database;
      using DocumentLock docLock = doc.LockDocument();
      using Transaction tr = db.TransactionManager.StartTransaction();
      var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
      var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
      work(tr, model, db);
      tr.Commit();
    }

    private static void TryRegen(Document doc)
    {
      try
      {
        doc?.Editor?.Regen();
      }
      catch
      {
      }
    }
  }
}
