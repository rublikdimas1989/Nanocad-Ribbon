namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;
  using DbPolyline = Teigha.DatabaseServices.Polyline;

  /// <summary>
  /// Stub corridor drawing: route line / road polylines on dedicated layers.
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
    /// Interactive polyline: pick ≥2 points, Enter to finish. Drawn on NC_ROUTE_LINE or NC_ROAD.
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

      while (true)
      {
        var opts = new PromptPointOptions(
          pts.Count == 0
            ? "\n" + noun + ": укажите первую точку (Enter — отмена)"
            : "\n" + noun + ": следующая точка (Enter — завершить, ≥2 точек)")
        {
          AllowNone = true
        };
        if (pts.Count > 0)
        {
          opts.UseBasePoint = true;
          opts.BasePoint = pts[pts.Count - 1];
        }

        PromptPointResult res = ed.GetPoint(opts);
        if (res.Status == PromptStatus.None || res.Status == PromptStatus.Cancel)
        {
          break;
        }

        if (res.Status != PromptStatus.OK)
        {
          break;
        }

        pts.Add(res.Value);
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
  }
}
