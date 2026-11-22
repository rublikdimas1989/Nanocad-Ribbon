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
  /// Узел rectangles + labels on locked layer NC_NODES_PLACED.
  /// Placement overlay for «Узел». Orientation at 0°: Length along X, Width along Y.
  /// </summary>
  public static class CadNodePlacementOverlay
  {
    public const string LayerName = "NC_NODES_PLACED";

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
    /// Erase existing NC_NODES_PLACED entities, then draw pads (no duplicates on re-check).
    /// </summary>
    public static int ShowPads(IEnumerable<SiteGeom> pads)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      SiteGeom[] list = (pads ?? Enumerable.Empty<SiteGeom>())
        .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
        .ToArray();

      int drawn = 0;
      RunWrite(doc, (tr, model, db) =>
      {
        EnsureLayer(tr, db, unlockForWrite: true);
        EraseLayerEntities(tr, model);
        foreach (SiteGeom pad in list)
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

    public static void UpdatePad(SiteGeom pad)
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

    public static bool TryPlace(SiteGeom pad, out double x, out double y)
    {
      x = pad?.X ?? 0;
      y = pad?.Y ?? 0;
      if (pad == null)
      {
        return false;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Editor ed = doc.Editor
        ?? throw new InvalidOperationException("Нет редактора активного чертежа.");

      // Same zoom-to-KP as «Повернуть вручную» before the move jig (never throws).
      SafeZoomToPadOrGeom(ed, doc, pad);

      var jig = new PadPlaceJig(pad, new Point3d(pad.X, pad.Y, 0));
      PromptResult result = ed.Drag(jig);
      if (result.Status != PromptStatus.OK)
      {
        ed.WriteMessage("\nРазмещение Узел отменено.");
        return false;
      }

      x = jig.Center.X;
      y = jig.Center.Y;
      ed.WriteMessage("\nУзел «" + pad.Name + "»: X=" + x.ToString("G17") + " Y=" + y.ToString("G17"));
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
        throw new InvalidOperationException("Не указано имя Узел.");
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
          "Узел «" + padName + "» нет на чертеже. Включите «Показывать на чертеже».");
      }

      if (!haveCenter)
      {
        throw new InvalidOperationException(
          "Не удалось определить центр прямоугольника Узел «" + padName + "».");
      }

      try
      {
        ed.SetImpliedSelection(ids);
      }
      catch
      {
      }

      SafeZoomToPad(ed, doc, ids);

      ed.WriteMessage(
        "\nПоворот Узел «" + padName + "»: база = центр прямоугольника ("
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

    private static void AddRectangle(Transaction tr, BlockTableRecord model, Database db, SiteGeom pad)
    {
      // 0°: Длина → X (горизонталь), Ширина → Y (вертикаль) → широкая площадка Узел.
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

    private static void AddLabel(Transaction tr, BlockTableRecord model, Database db, SiteGeom pad)
    {
      double halfY = Math.Max(pad.WidthM, 1) / 2.0;
      // Shared with inserts via Tracing_cache\Settings\interface.conf (LabelFontHeightM).
      double configured = 0;
      try
      {
        configured = NcTestRibbon.Configuration.InterfaceConf.Load().LabelFontHeightM;
      }
      catch
      {
      }

      double height = configured > 0 ? configured : Math.Max(pad.WidthM, 1) / 2.0;
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
        const string app = "NC_NODE_PLACED";
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
        ResultBuffer data = entity.GetXDataForApplication("NC_NODE_PLACED");
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
    /// Best-effort zoom before place/move. Must never throw into the unmanaged host.
    /// </summary>
    private static void SafeZoomToPadOrGeom(Editor ed, Document doc, SiteGeom pad)
    {
      try
      {
        if (ed == null || doc == null || pad == null)
        {
          return;
        }

        ObjectId[] ids = Array.Empty<ObjectId>();
        if (!string.IsNullOrWhiteSpace(pad.Name))
        {
          ids = FindPadIdsReadOnly(doc, pad.Name);
        }

        if (ids.Length > 0)
        {
          SafeZoomToPad(ed, doc, ids);
          return;
        }

        SafeZoomToGeom(ed, pad);
      }
      catch (Exception)
      {
        // Swallow — zoom must not crash nCad.exe.
      }
      catch
      {
      }
    }

    private static void SafeZoomToPad(Editor ed, Document doc, ObjectId[] ids)
    {
      try
      {
        if (ed == null || doc == null || ids == null || ids.Length == 0)
        {
          return;
        }

        if (!TryGetEntityExtents(doc, ids, out double minX, out double minY, out double maxX, out double maxY))
        {
          return;
        }

        ApplyViewExtentsSafe(ed, minX, minY, maxX, maxY);
      }
      catch (Exception)
      {
      }
      catch
      {
      }
    }

    private static void SafeZoomToGeom(Editor ed, SiteGeom pad)
    {
      try
      {
        if (ed == null || pad == null)
        {
          return;
        }

        if (!TryGetGeomExtents(pad, out double minX, out double minY, out double maxX, out double maxY))
        {
          return;
        }

        ApplyViewExtentsSafe(ed, minX, minY, maxX, maxY);
      }
      catch (Exception)
      {
      }
      catch
      {
      }
    }

    /// <summary>
    /// Read pad ObjectIds without nested write locks (avoids lock/view races before jig).
    /// </summary>
    private static ObjectId[] FindPadIdsReadOnly(Document doc, string padName)
    {
      try
      {
        if (doc?.Database == null || string.IsNullOrWhiteSpace(padName))
        {
          return Array.Empty<ObjectId>();
        }

        // Prefer transaction without LockDocument — zoom is read-only and may run
        // while the UI already holds a document context.
        using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
        {
          BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
          BlockTableRecord model = (BlockTableRecord)tr.GetObject(
            bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
          ObjectId[] ids = FindNamedPadIds(tr, model, padName);
          tr.Commit();
          return ids ?? Array.Empty<ObjectId>();
        }
      }
      catch (Exception)
      {
        return Array.Empty<ObjectId>();
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

            if (TryExpandExtents(ent, ref minX, ref minY, ref maxX, ref maxY))
            {
              any = true;
            }
          }

          tr.Commit();
        }
      }
      catch (Exception)
      {
        return false;
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

    private static bool TryGetGeomExtents(
      SiteGeom pad,
      out double minX,
      out double minY,
      out double maxX,
      out double maxY)
    {
      minX = minY = maxX = maxY = 0;
      if (pad == null)
      {
        return false;
      }

      if (!double.IsFinite(pad.X) || !double.IsFinite(pad.Y)
          || !double.IsFinite(pad.LengthM) || !double.IsFinite(pad.WidthM)
          || !double.IsFinite(pad.RotationDeg))
      {
        return false;
      }

      double halfX = Math.Max(pad.LengthM, 1) / 2.0;
      double halfY = Math.Max(pad.WidthM, 1) / 2.0;
      Point2d[] local =
      {
        new Point2d(-halfX, -halfY),
        new Point2d(+halfX, -halfY),
        new Point2d(+halfX, +halfY),
        new Point2d(-halfX, +halfY)
      };

      minX = double.PositiveInfinity;
      minY = double.PositiveInfinity;
      maxX = double.NegativeInfinity;
      maxY = double.NegativeInfinity;
      foreach (Point2d p in local)
      {
        Point2d w = Rotate(pad.X, pad.Y, p.X, p.Y, pad.RotationDeg);
        minX = Math.Min(minX, w.X);
        minY = Math.Min(minY, w.Y);
        maxX = Math.Max(maxX, w.X);
        maxY = Math.Max(maxY, w.Y);
      }

      double labelH = Math.Max(pad.WidthM, 1) / 2.0;
      Point2d label = Rotate(pad.X, pad.Y, 0, halfY + labelH * 1.15, pad.RotationDeg);
      minX = Math.Min(minX, label.X - labelH);
      minY = Math.Min(minY, label.Y - labelH);
      maxX = Math.Max(maxX, label.X + labelH);
      maxY = Math.Max(maxY, label.Y + labelH);

      return double.IsFinite(minX) && double.IsFinite(maxX) && maxX >= minX && maxY >= minY;
    }

    /// <summary>
    /// Set current view to extents. Avoids disposing ViewTableRecord after SetCurrentView
    /// (Teigha/HostMgd can AV / raise CLR 0xE0434352 on Dispose of that wrapper).
    /// </summary>
    private static void ApplyViewExtentsSafe(
      Editor ed,
      double minX,
      double minY,
      double maxX,
      double maxY)
    {
      if (ed == null)
      {
        return;
      }

      if (!double.IsFinite(minX) || !double.IsFinite(minY)
          || !double.IsFinite(maxX) || !double.IsFinite(maxY)
          || maxX < minX || maxY < minY)
      {
        return;
      }

      double width = Math.Max(maxX - minX, 1.0) * 1.7;
      double height = Math.Max(maxY - minY, 1.0) * 1.7;
      double cx = (minX + maxX) / 2.0;
      double cy = (minY + maxY) / 2.0;
      if (!double.IsFinite(width) || !double.IsFinite(height)
          || !double.IsFinite(cx) || !double.IsFinite(cy)
          || width <= 0 || height <= 0)
      {
        return;
      }

      ViewTableRecord view = null;
      try
      {
        view = ed.GetCurrentView();
        if (view == null)
        {
          return;
        }

        double viewH = view.Height;
        double viewW = view.Width;
        if (!double.IsFinite(viewH) || viewH <= 1e-9)
        {
          viewH = 1.0;
        }

        if (!double.IsFinite(viewW) || viewW <= 1e-9)
        {
          viewW = 1.0;
        }

        double aspect = viewW / viewH;
        if (!double.IsFinite(aspect) || aspect <= 0)
        {
          aspect = 1.0;
        }

        if (width / height > aspect)
        {
          height = width / aspect;
        }
        else
        {
          width = height * aspect;
        }

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
          return;
        }

        view.CenterPoint = new Point2d(cx, cy);
        view.Width = width;
        view.Height = height;
        ed.SetCurrentView(view);
      }
      catch (Exception)
      {
      }
      catch
      {
      }
      finally
      {
        // Do NOT Dispose ViewTableRecord after SetCurrentView on HostMgd/Teigha —
        // Dispose of the temporary view wrapper has been observed to crash nCad
        // (CLR 0xE0434352 / KernelBase). Leave for GC.
        view = null;
      }
    }

    private static bool TryExpandExtents(
      Entity ent,
      ref double minX,
      ref double minY,
      ref double maxX,
      ref double maxY)
    {
      // Prefer polyline/text verts — GeometricExtents can throw native SEH on bad entities.
      if (ent is DbPolyline pl)
      {
        bool any = false;
        try
        {
          for (int i = 0; i < pl.NumberOfVertices; i++)
          {
            Point2d p = pl.GetPoint2dAt(i);
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y))
            {
              continue;
            }

            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
            any = true;
          }
        }
        catch (Exception)
        {
          return any;
        }
        catch
        {
          return any;
        }

        return any;
      }

      if (ent is DBText text)
      {
        try
        {
          Point3d p = text.Position;
          double h = Math.Max(text.Height, 1.0);
          if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(h))
          {
            return false;
          }

          minX = Math.Min(minX, p.X - h);
          minY = Math.Min(minY, p.Y - h);
          maxX = Math.Max(maxX, p.X + h);
          maxY = Math.Max(maxY, p.Y + h);
          return true;
        }
        catch (Exception)
        {
          return false;
        }
        catch
        {
          return false;
        }
      }

      try
      {
        Extents3d ext = ent.GeometricExtents;
        if (!double.IsFinite(ext.MinPoint.X) || !double.IsFinite(ext.MaxPoint.X))
        {
          return false;
        }

        minX = Math.Min(minX, ext.MinPoint.X);
        minY = Math.Min(minY, ext.MinPoint.Y);
        maxX = Math.Max(maxX, ext.MaxPoint.X);
        maxY = Math.Max(maxY, ext.MaxPoint.Y);
        return true;
      }
      catch (Exception)
      {
        return false;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// ROTATE-equivalent: angle prompt only; base is always the KP rectangle center.
    /// </summary>
    private static bool RotateAboutCenter(Editor ed, Document doc, ObjectId[] ids, Point3d center)
    {
      var angOpts = new PromptAngleOptions("\nУгол поворота (база = центр Узел): ")
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

    public sealed class SiteGeom
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
      private readonly SiteGeom _pad;
      private Point3d _center;

      public Point3d Center => _center;

      public PadPlaceJig(SiteGeom pad, Point3d start)
      {
        _pad = pad;
        _center = start;
      }

      protected override SamplerStatus Sampler(JigPrompts prompts)
      {
        var options = new JigPromptPointOptions(
          "\nУкажите новое положение Узел «" + _pad.Name + "» (ЛКМ — принять, Esc — отмена): ")
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
