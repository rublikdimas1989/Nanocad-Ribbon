namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using Teigha.GraphicsInterface;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbPolyline = Teigha.DatabaseServices.Polyline;

  /// <summary>
  /// Draws KP width×length rectangles + labels on a dedicated layer; interactive place jig.
  /// </summary>
  public static class CadPadOverlay
  {
    public const string LayerName = "NC_KP_PADS";
    private static readonly Dictionary<string, ObjectId> RectIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ObjectId> LabelIds = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);

    public static void Clear()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        RectIds.Clear();
        LabelIds.Clear();
        return;
      }

      Database db = doc.Database;
      using (doc.LockDocument())
      using (Transaction tr = db.TransactionManager.StartTransaction())
      {
        EraseIds(tr, RectIds.Values);
        EraseIds(tr, LabelIds.Values);
        tr.Commit();
      }

      RectIds.Clear();
      LabelIds.Clear();
      try
      {
        doc.Editor?.Regen();
      }
      catch
      {
      }
    }

    public static void ShowPads(IEnumerable<PadGeom> pads)
    {
      if (pads == null)
      {
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Database db = doc.Database;
      using (doc.LockDocument())
      using (Transaction tr = db.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, db);
        BlockTableRecord model = (BlockTableRecord)tr.GetObject(
          SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        EraseIds(tr, RectIds.Values);
        EraseIds(tr, LabelIds.Values);
        RectIds.Clear();
        LabelIds.Clear();

        foreach (PadGeom pad in pads)
        {
          if (pad == null || string.IsNullOrWhiteSpace(pad.Name))
          {
            continue;
          }

          ObjectId rectId = AddRectangle(tr, model, pad);
          ObjectId labelId = AddLabel(tr, model, pad);
          RectIds[pad.Name] = rectId;
          LabelIds[pad.Name] = labelId;
        }

        tr.Commit();
      }

      doc.Editor?.Regen();
    }

    public static void UpdatePad(PadGeom pad)
    {
      if (pad == null || string.IsNullOrWhiteSpace(pad.Name))
      {
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      Database db = doc.Database;
      using (doc.LockDocument())
      using (Transaction tr = db.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, db);
        BlockTableRecord model = (BlockTableRecord)tr.GetObject(
          SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

        if (RectIds.TryGetValue(pad.Name, out ObjectId oldRect) && !oldRect.IsNull)
        {
          EraseOne(tr, oldRect);
        }

        if (LabelIds.TryGetValue(pad.Name, out ObjectId oldLabel) && !oldLabel.IsNull)
        {
          EraseOne(tr, oldLabel);
        }

        RectIds[pad.Name] = AddRectangle(tr, model, pad);
        LabelIds[pad.Name] = AddLabel(tr, model, pad);
        tr.Commit();
      }

      doc.Editor?.Regen();
    }

    /// <summary>
    /// Interactive place: rectangle+label follow cursor (DrawJig); click commits center X/Y.
    /// </summary>
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
      var jig = new PadPlaceJig(pad.Name, pad.WidthM, pad.LengthM, new Point3d(pad.X, pad.Y, 0));
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

    private static ObjectId AddRectangle(Transaction tr, BlockTableRecord model, PadGeom pad)
    {
      double halfW = Math.Max(pad.WidthM, 1) / 2.0;
      double halfL = Math.Max(pad.LengthM, 1) / 2.0;
      var pl = new DbPolyline();
      pl.SetDatabaseDefaults();
      pl.Layer = LayerName;
      pl.Color = Color.FromColorIndex(ColorMethod.ByAci, 3);
      pl.AddVertexAt(0, new Point2d(pad.X - halfW, pad.Y - halfL), 0, 0, 0);
      pl.AddVertexAt(1, new Point2d(pad.X + halfW, pad.Y - halfL), 0, 0, 0);
      pl.AddVertexAt(2, new Point2d(pad.X + halfW, pad.Y + halfL), 0, 0, 0);
      pl.AddVertexAt(3, new Point2d(pad.X - halfW, pad.Y + halfL), 0, 0, 0);
      pl.Closed = true;
      ObjectId id = model.AppendEntity(pl);
      tr.AddNewlyCreatedDBObject(pl, true);
      return id;
    }

    private static ObjectId AddLabel(Transaction tr, BlockTableRecord model, PadGeom pad)
    {
      double height = Math.Max(pad.WidthM, 1) / 2.0;
      double halfL = Math.Max(pad.LengthM, 1) / 2.0;
      var text = new DBText();
      text.SetDatabaseDefaults();
      text.Layer = LayerName;
      text.Color = Color.FromColorIndex(ColorMethod.ByAci, 2);
      text.Height = height;
      text.TextString = pad.Name ?? string.Empty;
      text.HorizontalMode = TextHorizontalMode.TextCenter;
      text.VerticalMode = TextVerticalMode.TextBottom;
      text.AlignmentPoint = new Point3d(pad.X, pad.Y + halfL + height * 0.15, 0);
      text.Position = text.AlignmentPoint;
      ObjectId id = model.AppendEntity(text);
      tr.AddNewlyCreatedDBObject(text, true);
      return id;
    }

    private static void EnsureLayer(Transaction tr, Database db)
    {
      LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      if (layers.Has(LayerName))
      {
        return;
      }

      layers.UpgradeOpen();
      var layer = new LayerTableRecord
      {
        Name = LayerName,
        Color = Color.FromColorIndex(ColorMethod.ByAci, 3)
      };
      layers.Add(layer);
      tr.AddNewlyCreatedDBObject(layer, true);
    }

    private static void EraseIds(Transaction tr, IEnumerable<ObjectId> ids)
    {
      foreach (ObjectId id in ids)
      {
        EraseOne(tr, id);
      }
    }

    private static void EraseOne(Transaction tr, ObjectId id)
    {
      if (id.IsNull || id.IsErased)
      {
        return;
      }

      try
      {
        DBObject obj = tr.GetObject(id, OpenMode.ForWrite, openErased: true);
        obj?.Erase();
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
      public double WidthM { get; set; } = 150;
      public double LengthM { get; set; } = 250;
    }

    private sealed class PadPlaceJig : DrawJig
    {
      private readonly string _name;
      private readonly double _width;
      private readonly double _length;
      private Point3d _center;

      public Point3d Center => _center;

      public PadPlaceJig(string name, double width, double length, Point3d start)
      {
        _name = name ?? string.Empty;
        _width = Math.Max(width, 1);
        _length = Math.Max(length, 1);
        _center = start;
      }

      protected override SamplerStatus Sampler(JigPrompts prompts)
      {
        var options = new JigPromptPointOptions("\nУкажите новое положение КП «" + _name + "» (ЛКМ — принять, Esc — отмена): ")
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

        double halfW = _width / 2.0;
        double halfL = _length / 2.0;
        var corners = new Point3dCollection
        {
          new Point3d(_center.X - halfW, _center.Y - halfL, 0),
          new Point3d(_center.X + halfW, _center.Y - halfL, 0),
          new Point3d(_center.X + halfW, _center.Y + halfL, 0),
          new Point3d(_center.X - halfW, _center.Y + halfL, 0),
          new Point3d(_center.X - halfW, _center.Y - halfL, 0)
        };
        // Teigha: Geometry.Polyline(Point3dCollection, Vector3d normal, IntPtr) — not (…, int, int).
        draw.Geometry.Polyline(corners, Vector3d.ZAxis, IntPtr.Zero);

        double height = _width / 2.0;
        Point3d labelAt = new Point3d(_center.X, _center.Y + halfL + height * 0.15, 0);
        draw.Geometry.Text(labelAt, Vector3d.ZAxis, Vector3d.XAxis, height, 1.0, 0.0, _name);
        return true;
      }
    }
  }
}
