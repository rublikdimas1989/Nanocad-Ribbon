namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using Teigha.GraphicsInterface;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbPolyline = Teigha.DatabaseServices.Polyline;

  /// <summary>
  /// KP rectangles + labels on locked layer NC_KP_PADS.
  /// Orientation at 0°: Длина along X (wide), Ширина along Y (narrow).
  /// </summary>
  public static class CadPadOverlay
  {
    public const string LayerName = "NC_KP_PADS";

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

    /// <summary>
    /// Erase existing NC_KP_PADS entities, then draw pads (no duplicates on re-check).
    /// </summary>
    public static int ShowPads(IEnumerable<PadGeom> pads)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      PadGeom[] list = (pads ?? Enumerable.Empty<PadGeom>())
        .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
        .ToArray();

      int drawn = 0;
      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        EraseLayerEntities(tr, model);
        foreach (PadGeom pad in list)
        {
          AddRectangle(tr, model, db, pad);
          AddLabel(tr, model, db, pad);
          drawn++;
        }

        LockLayer(tr, db);
      });
      TryRegen(doc);
      TryUpdateScreen(doc);
      return drawn;
    }

    public static void UpdatePad(PadGeom pad)
    {
      if (pad == null || string.IsNullOrWhiteSpace(pad.Name))
      {
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");

      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        EraseNamedPad(tr, model, pad.Name);
        AddRectangle(tr, model, db, pad);
        AddLabel(tr, model, db, pad);
        LockLayer(tr, db);
      });
      TryRegen(doc);
      TryUpdateScreen(doc);
    }

    public static bool TryPlace(PadGeom pad, out double x, out double y)
    {
      x = pad?.X ?? 0;
      y = pad?.Y ?? 0;
      if (pad == null)
      {
        return false;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Editor ed = doc.Editor;

      // Same zoom-to-KP as «Повернуть вручную» before the move jig.
      ZoomToPadOrGeom(ed, doc, pad);

      var jig = new PadPlaceJig(pad, new Point3d(pad.X, pad.Y, 0));
      PromptResult result = ed.Drag(jig);
      if (result.Status != PromptStatus.OK)
      {
        ed.WriteMessage("\nРазмещение КП отменено.");
        return false;
      }

      x = jig.Center.X;
      y = jig.Center.Y;
      ed.WriteMessage("\nКП «" + pad.Name + "»: X=" + x.ToString("G17") + " Y=" + y.ToString("G17"));
      return true;
    }

    /// <summary>
    /// Unlock layer, select pad entities, rotate about the rectangle center (CAD ROTATE base = center).
    /// Returns false if the user cancelled. When true, poseUpdated indicates X/Y/angle were read back.
    /// </summary>
    public static bool TryManualRotate(
      string padName,
      out double x,
      out double y,
      out double rotationDeg,
      out bool poseUpdated)
    {
      x = 0;
      y = 0;
      rotationDeg = 0;
      poseUpdated = false;
      if (string.IsNullOrWhiteSpace(padName))
      {
        throw new InvalidOperationException("Не указано имя КП.");
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Editor ed = doc.Editor;

      ObjectId[] ids = Array.Empty<ObjectId>();
      Point3d center = Point3d.Origin;
      bool haveCenter = false;
      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        ids = FindNamedPadIds(tr, model, padName);
        haveCenter = TryGetRectangleCenter(tr, ids, out center);
        // Leave unlocked so ROTATE can modify entities.
      });

      if (ids.Length == 0)
      {
        throw new InvalidOperationException(
          "КП «" + padName + "» нет на чертеже. Включите «Показывать на чертеже».");
      }

      if (!haveCenter)
      {
        throw new InvalidOperationException(
          "Не удалось определить центр прямоугольника КП «" + padName + "».");
      }

      try
      {
        ed.SetImpliedSelection(ids);
      }
      catch
      {
      }

      ZoomToPad(ed, doc, ids);

      ed.WriteMessage(
        "\nПоворот КП «" + padName + "»: база = центр прямоугольника ("
        + center.X.ToString("0.###") + "; " + center.Y.ToString("0.###") + ").");

      // Always rotate about KP rectangle center (no free base-point pick).
      if (!RotateAboutCenter(ed, doc, ids, center))
      {
        RunWrite(doc, (tr, model, db) => LockLayer(tr, db));
        return false;
      }

      poseUpdated = TryReadPadPose(padName, out x, out y, out rotationDeg);
      RunWrite(doc, (tr, model, db) => LockLayer(tr, db));
      TryRegen(doc);
      TryUpdateScreen(doc);
      return true;
    }

    public static bool TryReadPadPose(string padName, out double x, out double y, out double rotationDeg)
    {
      x = 0;
      y = 0;
      rotationDeg = 0;
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null || string.IsNullOrWhiteSpace(padName))
      {
        return false;
      }

      bool ok = false;
      double cx = 0, cy = 0, rot = 0;
      RunWrite(doc, (tr, model, db) =>
      {
        foreach (ObjectId id in FindNamedPadIds(tr, model, padName))
        {
          DbPolyline pl = tr.GetObject(id, OpenMode.ForRead) as DbPolyline;
          if (pl == null || pl.NumberOfVertices < 2)
          {
            continue;
          }

          Point2d p0 = pl.GetPoint2dAt(0);
          Point2d p1 = pl.GetPoint2dAt(1);
          Point2d p2 = pl.GetPoint2dAt(Math.Min(2, pl.NumberOfVertices - 1));
          cx = (p0.X + p2.X) / 2.0;
          cy = (p0.Y + p2.Y) / 2.0;
          rot = NormalizeDegrees(Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180.0 / Math.PI);
          ok = true;
          break;
        }
      });

      if (!ok)
      {
        return false;
      }

      x = cx;
      y = cy;
      rotationDeg = rot;
      return true;
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

    private static void AddRectangle(Transaction tr, BlockTableRecord model, Database db, PadGeom pad)
    {
      // 0°: Длина → X (горизонталь), Ширина → Y (вертикаль) → широкая КП.
      double halfX = Math.Max(pad.LengthM, 1) / 2.0;
      double halfY = Math.Max(pad.WidthM, 1) / 2.0;
      Point2d[] local =
      {
        new Point2d(-halfX, -halfY),
        new Point2d(+halfX, -halfY),
        new Point2d(+halfX, +halfY),
        new Point2d(-halfX, +halfY)
      };

      var pl = new DbPolyline();
      pl.SetDatabaseDefaults(db);
      pl.Layer = LayerName;
      pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
      pl.Visible = true;
      for (int i = 0; i < 4; i++)
      {
        Point2d w = Rotate(pad.X, pad.Y, local[i].X, local[i].Y, pad.RotationDeg);
        pl.AddVertexAt(i, w, 0, 0, 0);
      }

      pl.Closed = true;
      model.AppendEntity(pl);
      tr.AddNewlyCreatedDBObject(pl, true);
      SetPadName(tr, db, pl, pad.Name);
    }

    private static void AddLabel(Transaction tr, BlockTableRecord model, Database db, PadGeom pad)
    {
      double halfY = Math.Max(pad.WidthM, 1) / 2.0;
      double height = Math.Max(pad.WidthM, 1) / 2.0;
      Point2d anchor = Rotate(pad.X, pad.Y, 0, halfY + height * 0.15, pad.RotationDeg);

      var text = new DBText();
      text.SetDatabaseDefaults(db);
      text.Layer = LayerName;
      text.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
      text.Visible = true;
      text.Height = height;
      text.TextString = pad.Name ?? string.Empty;
      text.Rotation = DegreesToRadians(pad.RotationDeg);
      text.HorizontalMode = TextHorizontalMode.TextCenter;
      text.VerticalMode = TextVerticalMode.TextBottom;
      text.AlignmentPoint = new Point3d(anchor.X, anchor.Y, 0);
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
      SetPadName(tr, db, text, pad.Name);
    }

    private static void SetPadName(Transaction tr, Database db, Entity entity, string name)
    {
      try
      {
        const string app = "NC_KP_PAD";
        RegAppTable apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!apps.Has(app))
        {
          apps.UpgradeOpen();
          var record = new RegAppTableRecord { Name = app };
          apps.Add(record);
          tr.AddNewlyCreatedDBObject(record, true);
        }

        entity.XData = new ResultBuffer(
          new TypedValue((int)DxfCode.ExtendedDataRegAppName, app),
          new TypedValue((int)DxfCode.ExtendedDataAsciiString, name ?? string.Empty));
      }
      catch
      {
        // XData optional — erase-by-layer still works for full refresh.
      }
    }

    private static string ReadPadName(Entity entity)
    {
      try
      {
        ResultBuffer data = entity.GetXDataForApplication("NC_KP_PAD");
        if (data == null)
        {
          return null;
        }

        foreach (TypedValue tv in data)
        {
          if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
          {
            return Convert.ToString(tv.Value);
          }
        }
      }
      catch
      {
      }

      return null;
    }

    private static ObjectId[] FindNamedPadIds(Transaction tr, BlockTableRecord model, string name)
    {
      var ids = new List<ObjectId>();
      foreach (ObjectId id in CollectModelIds(model))
      {
        if (id.IsNull || id.IsErased)
        {
          continue;
        }

        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
        if (ent == null || !string.Equals(ent.Layer, LayerName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        string stored = ReadPadName(ent);
        if (string.Equals(stored, name, StringComparison.OrdinalIgnoreCase))
        {
          ids.Add(id);
          continue;
        }

        // Fallback: label text match (entities without XData).
        if (ent is DBText text
            && string.Equals(text.TextString, name, StringComparison.OrdinalIgnoreCase))
        {
          ids.Add(id);
        }
      }

      return ids.ToArray();
    }

    private static bool TryGetRectangleCenter(Transaction tr, ObjectId[] ids, out Point3d center)
    {
      center = Point3d.Origin;
      foreach (ObjectId id in ids)
      {
        if (id.IsNull || id.IsErased)
        {
          continue;
        }

        DbPolyline pl = tr.GetObject(id, OpenMode.ForRead) as DbPolyline;
        if (pl == null || pl.NumberOfVertices < 3)
        {
          continue;
        }

        Point2d p0 = pl.GetPoint2dAt(0);
        Point2d p2 = pl.GetPoint2dAt(2);
        center = new Point3d((p0.X + p2.X) / 2.0, (p0.Y + p2.Y) / 2.0, 0);
        return true;
      }

      return false;
    }

    /// <summary>
    /// Zoom to drawn KP entities when present; otherwise fit to pad geom (place before first draw).
    /// </summary>
    private static void ZoomToPadOrGeom(Editor ed, Document doc, PadGeom pad)
    {
      if (pad == null || string.IsNullOrWhiteSpace(pad.Name))
      {
        ZoomToGeom(ed, doc, pad);
        return;
      }

      ObjectId[] ids = FindPadIdsReadOnly(doc, pad.Name);
      if (ids.Length > 0)
      {
        ZoomToPad(ed, doc, ids);
        return;
      }

      ZoomToGeom(ed, doc, pad);
    }

    private static ObjectId[] FindPadIdsReadOnly(Document doc, string padName)
    {
      try
      {
        using (doc.LockDocument())
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
          BlockTableRecord model = (BlockTableRecord)tr.GetObject(
            bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
          ObjectId[] ids = FindNamedPadIds(tr, model, padName);
          tr.Commit();
          return ids;
        }
      }
      catch
      {
        return Array.Empty<ObjectId>();
      }
    }

    /// <summary>
    /// Fit the current view around the KP entities (rectangle + label) with margin.
    /// </summary>
    private static void ZoomToPad(Editor ed, Document doc, ObjectId[] ids)
    {
      if (ed == null || doc == null || ids == null || ids.Length == 0)
      {
        return;
      }

      try
      {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        bool any = false;

        using (doc.LockDocument())
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

            if (TryExpandExtents(ent, ref minX, ref minY, ref maxX, ref maxY))
            {
              any = true;
            }
          }

          tr.Commit();
        }

        if (!any || !double.IsFinite(minX) || !double.IsFinite(maxX))
        {
          return;
        }

        ApplyViewExtents(ed, doc, minX, minY, maxX, maxY);
      }
      catch
      {
        // Zoom is best-effort — place/rotate must still work.
      }
    }

    private static void ZoomToGeom(Editor ed, Document doc, PadGeom pad)
    {
      if (ed == null || doc == null || pad == null)
      {
        return;
      }

      try
      {
        double halfX = Math.Max(pad.LengthM, 1) / 2.0;
        double halfY = Math.Max(pad.WidthM, 1) / 2.0;
        Point2d[] local =
        {
          new Point2d(-halfX, -halfY),
          new Point2d(+halfX, -halfY),
          new Point2d(+halfX, +halfY),
          new Point2d(-halfX, +halfY)
        };

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        foreach (Point2d p in local)
        {
          Point2d w = Rotate(pad.X, pad.Y, p.X, p.Y, pad.RotationDeg);
          minX = Math.Min(minX, w.X);
          minY = Math.Min(minY, w.Y);
          maxX = Math.Max(maxX, w.X);
          maxY = Math.Max(maxY, w.Y);
        }

        // Include label above the pad (same offset as AddLabel).
        double height = Math.Max(pad.WidthM, 1) / 2.0;
        Point2d label = Rotate(pad.X, pad.Y, 0, halfY + height * 1.15, pad.RotationDeg);
        minX = Math.Min(minX, label.X - height);
        minY = Math.Min(minY, label.Y - height);
        maxX = Math.Max(maxX, label.X + height);
        maxY = Math.Max(maxY, label.Y + height);

        ApplyViewExtents(ed, doc, minX, minY, maxX, maxY);
      }
      catch
      {
      }
    }

    private static void ApplyViewExtents(
      Editor ed,
      Document doc,
      double minX,
      double minY,
      double maxX,
      double maxY)
    {
      double width = Math.Max(maxX - minX, 1.0);
      double height = Math.Max(maxY - minY, 1.0);
      // ~35% margin so the pad is not edge-clipped while placing/rotating.
      width *= 1.7;
      height *= 1.7;
      double cx = (minX + maxX) / 2.0;
      double cy = (minY + maxY) / 2.0;

      using (ViewTableRecord view = ed.GetCurrentView())
      {
        double aspect = view.Width / Math.Max(view.Height, 1e-9);
        if (width / height > aspect)
        {
          height = width / aspect;
        }
        else
        {
          width = height * aspect;
        }

        view.CenterPoint = new Point2d(cx, cy);
        view.Width = width;
        view.Height = height;
        ed.SetCurrentView(view);
      }

      TryUpdateScreen(doc);
    }

    private static bool TryExpandExtents(
      Entity ent,
      ref double minX,
      ref double minY,
      ref double maxX,
      ref double maxY)
    {
      try
      {
        Extents3d ext = ent.GeometricExtents;
        minX = Math.Min(minX, ext.MinPoint.X);
        minY = Math.Min(minY, ext.MinPoint.Y);
        maxX = Math.Max(maxX, ext.MaxPoint.X);
        maxY = Math.Max(maxY, ext.MaxPoint.Y);
        return true;
      }
      catch
      {
      }

      if (ent is DbPolyline pl)
      {
        bool any = false;
        for (int i = 0; i < pl.NumberOfVertices; i++)
        {
          Point2d p = pl.GetPoint2dAt(i);
          minX = Math.Min(minX, p.X);
          minY = Math.Min(minY, p.Y);
          maxX = Math.Max(maxX, p.X);
          maxY = Math.Max(maxY, p.Y);
          any = true;
        }

        return any;
      }

      if (ent is DBText text)
      {
        Point3d p = text.Position;
        double h = Math.Max(text.Height, 1.0);
        minX = Math.Min(minX, p.X - h);
        minY = Math.Min(minY, p.Y - h);
        maxX = Math.Max(maxX, p.X + h);
        maxY = Math.Max(maxY, p.Y + h);
        return true;
      }

      return false;
    }

    /// <summary>
    /// ROTATE-equivalent: angle prompt only; base is always the KP rectangle center.
    /// </summary>
    private static bool RotateAboutCenter(Editor ed, Document doc, ObjectId[] ids, Point3d center)
    {
      var angOpts = new PromptAngleOptions("\nУгол поворота (база = центр КП): ")
      {
        BasePoint = center,
        UseBasePoint = true,
        AllowNone = false
      };
      PromptDoubleResult ang = ed.GetAngle(angOpts);
      if (ang.Status != PromptStatus.OK)
      {
        ed.WriteMessage("\nПоворот отменён.");
        return false;
      }

      Matrix3d xform = Matrix3d.Rotation(ang.Value, Vector3d.ZAxis, center);
      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        foreach (ObjectId id in ids)
        {
          if (id.IsNull || id.IsErased)
          {
            continue;
          }

          Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
          ent?.TransformBy(xform);
        }
      });
      return true;
    }

    private static void EraseNamedPad(Transaction tr, BlockTableRecord model, string name)
    {
      foreach (ObjectId id in FindNamedPadIds(tr, model, name))
      {
        Entity ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
        ent?.Erase();
      }
    }

    private static void EraseLayerEntities(Transaction tr, BlockTableRecord model)
    {
      foreach (ObjectId id in CollectModelIds(model))
      {
        if (id.IsNull || id.IsErased)
        {
          continue;
        }

        Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
        if (ent == null || !string.Equals(ent.Layer, LayerName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        ent.UpgradeOpen();
        ent.Erase();
      }
    }

    private static List<ObjectId> CollectModelIds(BlockTableRecord model)
    {
      var ids = new List<ObjectId>();
      foreach (ObjectId id in model)
      {
        ids.Add(id);
      }

      return ids;
    }

    private static void EnsureLayer(Transaction tr, Database db, bool unlockForWrite)
    {
      LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      LayerTableRecord layer;
      if (!layers.Has(LayerName))
      {
        layers.UpgradeOpen();
        layer = new LayerTableRecord
        {
          Name = LayerName,
          Color = Color.FromColorIndex(ColorMethod.ByAci, 3)
        };
        layers.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
      }
      else
      {
        layer = (LayerTableRecord)tr.GetObject(layers[LayerName], OpenMode.ForWrite);
      }

      layer.IsOff = false;
      layer.IsFrozen = false;
      if (unlockForWrite)
      {
        layer.IsLocked = false;
      }
    }

    private static void UnlockLayer(Transaction tr, Database db)
    {
      LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      if (!layers.Has(LayerName))
      {
        return;
      }

      LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layers[LayerName], OpenMode.ForWrite);
      layer.IsLocked = false;
      layer.IsOff = false;
      layer.IsFrozen = false;
    }

    private static void LockLayer(Transaction tr, Database db)
    {
      LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      if (!layers.Has(LayerName))
      {
        return;
      }

      LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layers[LayerName], OpenMode.ForWrite);
      layer.IsLocked = true;
      layer.IsOff = false;
      layer.IsFrozen = false;
    }

    private static Point2d Rotate(double cx, double cy, double lx, double ly, double degrees)
    {
      double rad = DegreesToRadians(NormalizeDegrees(degrees));
      double c = Math.Cos(rad);
      double s = Math.Sin(rad);
      return new Point2d(cx + lx * c - ly * s, cy + lx * s + ly * c);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    public static double NormalizeDegrees(double degrees)
    {
      if (!double.IsFinite(degrees))
      {
        return 0;
      }

      double d = degrees % 360.0;
      if (d < 0)
      {
        d += 360.0;
      }

      return d;
    }

    private static void TryRegen(Document doc)
    {
      try
      {
        doc.Editor?.Regen();
      }
      catch
      {
      }
    }

    private static void TryUpdateScreen(Document doc)
    {
      try
      {
        doc.Editor?.UpdateScreen();
      }
      catch
      {
      }
    }

    public sealed class PadGeom
    {
      public string Name { get; set; } = string.Empty;
      public double X { get; set; }
      public double Y { get; set; }
      /// <summary>Short side — vertical at 0°.</summary>
      public double WidthM { get; set; } = 150;
      /// <summary>Long side — horizontal at 0°.</summary>
      public double LengthM { get; set; } = 250;
      /// <summary>Rotation degrees CCW, 0…360.</summary>
      public double RotationDeg { get; set; }
    }

    private sealed class PadPlaceJig : DrawJig
    {
      private readonly PadGeom _pad;
      private Point3d _center;

      public Point3d Center => _center;

      public PadPlaceJig(PadGeom pad, Point3d start)
      {
        _pad = pad;
        _center = start;
      }

      protected override SamplerStatus Sampler(JigPrompts prompts)
      {
        var options = new JigPromptPointOptions(
          "\nУкажите новое положение КП «" + _pad.Name + "» (ЛКМ — принять, Esc — отмена): ")
        {
          UserInputControls = UserInputControls.Accept3dCoordinates
            | UserInputControls.NullResponseAccepted
        };
        PromptPointResult result = prompts.AcquirePoint(options);
        if (result.Status == PromptStatus.Cancel || result.Status == PromptStatus.None)
        {
          return SamplerStatus.Cancel;
        }

        if (result.Status != PromptStatus.OK)
        {
          return SamplerStatus.NoChange;
        }

        if (_center.DistanceTo(result.Value) < 1e-9)
        {
          return SamplerStatus.NoChange;
        }

        _center = result.Value;
        return SamplerStatus.OK;
      }

      protected override bool WorldDraw(WorldDraw draw)
      {
        if (draw?.Geometry == null)
        {
          return false;
        }

        double halfX = Math.Max(_pad.LengthM, 1) / 2.0;
        double halfY = Math.Max(_pad.WidthM, 1) / 2.0;
        Point2d[] local =
        {
          new Point2d(-halfX, -halfY),
          new Point2d(+halfX, -halfY),
          new Point2d(+halfX, +halfY),
          new Point2d(-halfX, +halfY),
          new Point2d(-halfX, -halfY)
        };
        var corners = new Point3dCollection();
        foreach (Point2d p in local)
        {
          Point2d w = Rotate(_center.X, _center.Y, p.X, p.Y, _pad.RotationDeg);
          corners.Add(new Point3d(w.X, w.Y, 0));
        }

        draw.Geometry.Polyline(corners, Vector3d.ZAxis, IntPtr.Zero);

        double height = Math.Max(_pad.WidthM, 1) / 2.0;
        Point2d label = Rotate(_center.X, _center.Y, 0, halfY + height * 0.15, _pad.RotationDeg);
        draw.Geometry.Text(
          new Point3d(label.X, label.Y, 0),
          Vector3d.ZAxis,
          new Vector3d(Math.Cos(DegreesToRadians(_pad.RotationDeg)), Math.Sin(DegreesToRadians(_pad.RotationDeg)), 0),
          height,
          1.0,
          0.0,
          _pad.Name);
        return true;
      }
    }
  }
}
