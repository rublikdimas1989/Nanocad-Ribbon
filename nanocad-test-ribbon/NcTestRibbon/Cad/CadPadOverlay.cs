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

    private static void EraseNamedPad(Transaction tr, BlockTableRecord model, string name)
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

        string stored = ReadPadName(ent);
        if (!string.Equals(stored, name, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        ent.UpgradeOpen();
        ent.Erase();
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
