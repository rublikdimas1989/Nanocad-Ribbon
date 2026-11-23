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
  /// Insert-point (врезка) circles + labels on unlocked layer NC_INSERTS.
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

      });
      TryRegen(doc);
      return drawn;
    }

    public static void UpdateInsert(InsertGeom point, bool refreshView = true)
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
      });
      if (refreshView)
      {
        TryRegen(doc);
      }
    }

    /// <summary>Draw insert at stored X/Y — no point pick jig.</summary>
    public static void InsertAtStored(InsertGeom point, bool refreshView = true)
    {
      UpdateInsert(point, refreshView);
    }

    /// <summary>Batch insert in one write + one regen (anti-flicker).</summary>
    public static int InsertManyAtStored(IEnumerable<InsertGeom> inserts)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      InsertGeom[] list = (inserts ?? Enumerable.Empty<InsertGeom>())
        .Where(p => p != null && p.Enabled && !string.IsNullOrWhiteSpace(p.Name))
        .ToArray();
      if (list.Length == 0)
      {
        return 0;
      }

      InterfaceConf ui = InterfaceConf.Load();
      double diameter = Math.Max(ui.InsertCircleDiameterM, 0.1);
      double font = Math.Max(ui.LabelFontHeightM, 0.1);

      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        foreach (InsertGeom point in list)
        {
          EraseNamed(tr, model, point.Name);
          AddCircle(tr, model, db, point, diameter);
          AddLabel(tr, model, db, point, diameter, font);
        }
      });
      TryRegen(doc);
      return list.Length;
    }

    /// <summary>Erase named врезка entities from the drawing.</summary>
    public static void RemoveByNames(IEnumerable<string> names)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      string[] list = (names ?? Enumerable.Empty<string>())
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
      if (list.Length == 0)
      {
        return;
      }

      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        foreach (string name in list)
        {
          EraseNamed(tr, model, name);
        }
      });
      TryRegen(doc);
    }

    /// <summary>Inserts currently on the drawing. Empty if none.</summary>
    public static List<InsertGeom> ListPlacedInserts()
    {
      var result = new List<InsertGeom>();
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return result;
      }

      var byName = new Dictionary<string, InsertGeom>(StringComparer.OrdinalIgnoreCase);
      try
      {
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
          var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
          foreach (ObjectId id in model)
          {
            if (id.IsNull || id.IsErased)
            {
              continue;
            }

            Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
            if (ent == null || !string.Equals(ent.Layer, LayerName, StringComparison.OrdinalIgnoreCase))
            {
              continue;
            }

            string name = ReadName(tr, ent);
            if (string.IsNullOrWhiteSpace(name) && ent is DBText text)
            {
              name = text.TextString;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
              continue;
            }

            name = name.Trim();
            double x = 0, y = 0;
            if (ent is DbCircle circle)
            {
              x = circle.Center.X;
              y = circle.Center.Y;
            }
            else if (ent is DBText label)
            {
              x = label.Position.X;
              y = label.Position.Y;
            }
            else
            {
              continue;
            }

            if (!byName.ContainsKey(name))
            {
              byName[name] = new InsertGeom { Name = name, X = x, Y = y };
            }
            else if (ent is DbCircle)
            {
              byName[name].X = x;
              byName[name].Y = y;
            }
          }

          tr.Commit();
        }
      }
      catch
      {
        return result;
      }

      result.AddRange(byName.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
      return result;
    }

    /// <summary>Zoom to one or many inserts (union extents). Never throws.</summary>
    public static void ZoomToInserts(IEnumerable<InsertGeom> points)
    {
      try
      {
        Document doc = CadApp.DocumentManager.MdiActiveDocument;
        Editor ed = doc?.Editor;
        if (ed == null || doc == null)
        {
          return;
        }

        InsertGeom[] list = (points ?? Enumerable.Empty<InsertGeom>())
          .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
          .ToArray();
        if (list.Length == 0)
        {
          return;
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        bool any = false;
        var ids = new List<ObjectId>();
        foreach (InsertGeom p in list)
        {
          ids.AddRange(FindInsertIdsReadOnly(doc, p.Name));
        }

        if (ids.Count > 0
          && TryGetEntityExtents(doc, ids.ToArray(), out minX, out minY, out maxX, out maxY))
        {
          ApplyViewExtentsSafe(ed, minX, minY, maxX, maxY);
          return;
        }

        foreach (InsertGeom p in list)
        {
          double pad = 25;
          minX = Math.Min(minX, p.X - pad);
          minY = Math.Min(minY, p.Y - pad);
          maxX = Math.Max(maxX, p.X + pad);
          maxY = Math.Max(maxY, p.Y + pad);
          any = true;
        }

        if (any)
        {
          ApplyViewExtentsSafe(ed, minX, minY, maxX, maxY);
        }
      }
      catch (Exception)
      {
      }
      catch
      {
      }
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

      // Always zoom to the insert (or its planned position) before place — same rule as KP pads.
      SafeZoomToInsertOrPoint(ed, doc, point);

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

    /// <summary>
    /// Best-effort zoom before place. Must never throw into the unmanaged host.
    /// </summary>
    private static void SafeZoomToInsertOrPoint(Editor ed, Document doc, InsertGeom point)
    {
      try
      {
        if (ed == null || doc == null || point == null)
        {
          return;
        }

        ObjectId[] ids = FindInsertIdsReadOnly(doc, point.Name);
        if (ids.Length > 0
          && TryGetEntityExtents(doc, ids, out double minX, out double minY, out double maxX, out double maxY))
        {
          ApplyViewExtentsSafe(ed, minX, minY, maxX, maxY);
          return;
        }

        InterfaceConf ui = InterfaceConf.Load();
        double radius = Math.Max(ui.InsertCircleDiameterM, 1.0) / 2.0;
        double pad = Math.Max(ui.LabelFontHeightM, radius) * 2.0;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
          return;
        }

        ApplyViewExtentsSafe(
          ed,
          point.X - radius - pad,
          point.Y - radius - pad,
          point.X + radius + pad,
          point.Y + radius + pad);
      }
      catch (Exception)
      {
      }
      catch
      {
      }
    }

    private static ObjectId[] FindInsertIdsReadOnly(Document doc, string name)
    {
      try
      {
        if (doc?.Database == null || string.IsNullOrWhiteSpace(name))
        {
          return Array.Empty<ObjectId>();
        }

        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
          var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
          var found = new List<ObjectId>();
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
              found.Add(id);
            }
          }

          tr.Commit();
          return found.ToArray();
        }
      }
      catch
      {
        return Array.Empty<ObjectId>();
      }
    }

    private static bool TryGetEntityExtents(
      Document doc,
      ObjectId[] ids,
      out double minX,
      out double minY,
      out double maxX,
      out double maxY)
    {
      minX = double.PositiveInfinity;
      minY = double.PositiveInfinity;
      maxX = double.NegativeInfinity;
      maxY = double.NegativeInfinity;
      bool any = false;
      try
      {
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          foreach (ObjectId id in ids)
          {
            if (id.IsNull || id.IsErased)
            {
              continue;
            }

            Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
            if (ent == null)
            {
              continue;
            }

            try
            {
              Extents3d ext = ent.GeometricExtents;
              minX = Math.Min(minX, ext.MinPoint.X);
              minY = Math.Min(minY, ext.MinPoint.Y);
              maxX = Math.Max(maxX, ext.MaxPoint.X);
              maxY = Math.Max(maxY, ext.MaxPoint.Y);
              any = true;
            }
            catch
            {
            }
          }

          tr.Commit();
        }
      }
      catch
      {
        return false;
      }

      return any
        && double.IsFinite(minX) && double.IsFinite(minY)
        && double.IsFinite(maxX) && double.IsFinite(maxY)
        && maxX >= minX && maxY >= minY;
    }

    private static void ApplyViewExtentsSafe(
      Editor ed,
      double minX,
      double minY,
      double maxX,
      double maxY)
    {
      if (ed == null
        || !double.IsFinite(minX) || !double.IsFinite(minY)
        || !double.IsFinite(maxX) || !double.IsFinite(maxY)
        || maxX < minX || maxY < minY)
      {
        return;
      }

      double width = Math.Max(maxX - minX, 1.0) * 1.7;
      double height = Math.Max(maxY - minY, 1.0) * 1.7;
      double cx = (minX + maxX) / 2.0;
      double cy = (minY + maxY) / 2.0;
      ViewTableRecord view = null;
      try
      {
        view = ed.GetCurrentView();
        if (view == null)
        {
          return;
        }

        view.CenterPoint = new Point2d(cx, cy);
        view.Height = height;
        view.Width = width;
        ed.SetCurrentView(view);
      }
      catch
      {
      }
      // Do not Dispose ViewTableRecord after SetCurrentView (HostMgd/Teigha can AV).
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
      // Never lock placement layers — grips/MOVE/ROTATE must work.
      record.IsLocked = false;
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
