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
  /// Stub corridor drawing: route line / road polylines on dedicated layers.
  /// Live DrawJig preview (committed segments + rubber-band to cursor) while picking.
  /// </summary>
  public static class CadCorridorOverlay
  {
    public const string RouteLayerName = "NC_ROUTE_LINE";
    public const string RoadLayerName = "NC_ROAD";

    public enum Kind
    {
      RouteLine,
      Road
    }

    public static string LayerName(Kind kind)
    {
      return kind == Kind.Road ? RoadLayerName : RouteLayerName;
    }

    /// <summary>
    /// Interactive polyline with live jig preview. Pick ≥2 points, Enter to finish.
    /// </summary>
    public static int DrawPolyline(Kind kind)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа.");
      Editor ed = doc.Editor
        ?? throw new InvalidOperationException("Нет Editor.");

      string layer = LayerName(kind);
      string noun = kind == Kind.Road ? "Дорога" : "Линия трассы";
      var pts = new List<Point3d>();

      // First vertex — no rubber band yet.
      {
        var firstOpts = new PromptPointOptions(
          "\n" + noun + ": укажите первую точку (Enter — отмена)")
        {
          AllowNone = true
        };
        PromptPointResult first = ed.GetPoint(firstOpts);
        if (first.Status != PromptStatus.OK)
        {
          ed.WriteMessage("\n" + noun + ": отменено.");
          return 0;
        }

        pts.Add(first.Value);
      }

      // Subsequent vertices — DrawJig shows fixed path + rubber-band to cursor.
      while (true)
      {
        string prompt = pts.Count == 1
          ? "\n" + noun + ": следующая точка (Enter — нужно ≥2 точек)"
          : "\n" + noun + ": следующая точка (Enter — завершить, " + pts.Count + " вершин)";

        var jig = new CorridorPolylineJig(pts, prompt);
        PromptResult drag = ed.Drag(jig);
        if (drag.Status == PromptStatus.OK)
        {
          pts.Add(jig.Current);
          continue;
        }

        // Enter / Esc / None → finish (or cancel if < 2).
        break;
      }

      if (pts.Count < 2)
      {
        ed.WriteMessage("\n" + noun + ": нужно ≥2 точек — отменено.");
        return 0;
      }

      using (doc.LockDocument())
      using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, doc.Database, layer, kind == Kind.Road ? (short)30 : (short)150);
        BlockTable bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
          bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

        var pl = new DbPolyline();
        for (int i = 0; i < pts.Count; i++)
        {
          pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
        }

        pl.Closed = false;
        pl.Layer = layer;
        pl.Color = Color.FromColorIndex(ColorMethod.ByLayer, 256);
        ms.AppendEntity(pl);
        tr.AddNewlyCreatedDBObject(pl, true);
        tr.Commit();
      }

      try { ed.Regen(); }
      catch { }

      ed.WriteMessage("\n" + noun + ": полилиния (" + pts.Count + " вершин) → слой " + layer + ".");
      return pts.Count;
    }

    private static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex)
    {
      LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      if (layers.Has(name))
      {
        LayerTableRecord existing = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForWrite);
        existing.IsLocked = false;
        existing.IsOff = false;
        return;
      }

      layers.UpgradeOpen();
      var layer = new LayerTableRecord
      {
        Name = name,
        Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
      };
      layers.Add(layer);
      tr.AddNewlyCreatedDBObject(layer, true);
    }

    /// <summary>
    /// Live preview: committed vertices + rubber-band from last vertex to cursor.
    /// </summary>
    private sealed class CorridorPolylineJig : DrawJig
    {
      private readonly List<Point3d> _fixed;
      private readonly string _prompt;
      private Point3d _current;

      public Point3d Current => _current;

      public CorridorPolylineJig(List<Point3d> fixedPoints, string prompt)
      {
        _fixed = fixedPoints ?? throw new ArgumentNullException(nameof(fixedPoints));
        if (_fixed.Count == 0)
        {
          throw new ArgumentException("Need at least one fixed vertex.", nameof(fixedPoints));
        }

        _prompt = prompt ?? "\nУкажите точку: ";
        _current = _fixed[_fixed.Count - 1];
      }

      protected override SamplerStatus Sampler(JigPrompts prompts)
      {
        var options = new JigPromptPointOptions(_prompt)
        {
          UserInputControls = UserInputControls.Accept3dCoordinates
            | UserInputControls.NullResponseAccepted
            | UserInputControls.GovernedByOrthoMode,
          UseBasePoint = true,
          BasePoint = _fixed[_fixed.Count - 1]
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

        if (_current.DistanceTo(result.Value) < 1e-9)
        {
          return SamplerStatus.NoChange;
        }

        _current = result.Value;
        return SamplerStatus.OK;
      }

      protected override bool WorldDraw(WorldDraw draw)
      {
        if (draw?.Geometry == null)
        {
          return false;
        }

        var path = new Point3dCollection();
        foreach (Point3d p in _fixed)
        {
          path.Add(p);
        }

        path.Add(_current);
        if (path.Count >= 2)
        {
          draw.Geometry.Polyline(path, Vector3d.ZAxis, IntPtr.Zero);
        }

        return true;
      }
    }
  }
}
