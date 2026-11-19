namespace NcTestRibbon.Cad
{
  using System;
  using System.IO;
  using HostMgd.ApplicationServices;
  using HostMgd.EditorInput;
  using Teigha.Colors;
  using Teigha.DatabaseServices;
  using Teigha.Geometry;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// Insert georeferenced raster (ECW/TIFF/…) with End-style world coords + scale
  /// (not plain IMAGEATTACH at scale 1).
  /// </summary>
  public static class CadRasterAttach
  {
    public const string LayerName = "NC_RASTER";

    public sealed class Result
    {
      public string Path { get; set; } = string.Empty;
      public string GeoSource { get; set; } = string.Empty;
      public double OriginX { get; set; }
      public double OriginY { get; set; }
      public double PixelSizeX { get; set; }
      public double PixelSizeY { get; set; }
      public int WidthPx { get; set; }
      public int HeightPx { get; set; }
      public ObjectId ImageId { get; set; } = ObjectId.Null;
      public string WorldFileWritten { get; set; } = string.Empty;
    }

    public static Result Attach(string rasterPath, bool writeWorldSidecar = true)
    {
      return Attach(rasterPath, writeWorldSidecar, LayerName, lockLayer: false, opacityPercent: 100);
    }

    public static Result Attach(
      string rasterPath,
      bool writeWorldSidecar,
      string layerName,
      bool lockLayer,
      int opacityPercent)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      if (string.IsNullOrWhiteSpace(rasterPath) || !File.Exists(rasterPath))
      {
        throw new FileNotFoundException("Растр не найден.", rasterPath);
      }

      string layer = string.IsNullOrWhiteSpace(layerName) ? LayerName : layerName.Trim();
      int opacity = ClampOpacity(opacityPercent);

      RasterGeoTransform geo = RasterGeoTransform.Resolve(rasterPath, out string geoNote);
      string worldWritten = string.Empty;
      if (writeWorldSidecar)
      {
        try
        {
          worldWritten = geo.WriteEsriWorldFile(rasterPath);
        }
        catch
        {
          worldWritten = string.Empty;
        }
      }

      Database db = doc.Database;
      ObjectId imageId = ObjectId.Null;
      int widthPx = geo.WidthPx;
      int heightPx = geo.HeightPx;

      using (doc.LockDocument())
      using (Transaction tr = db.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, db, layer, unlockForWrite: true);

        RasterImageDef def = new RasterImageDef();
        ObjectId defId = AddImageDef(tr, db, def, Path.GetFileNameWithoutExtension(rasterPath));
        def.SourceFileName = rasterPath;
        def.Load();

        if (widthPx <= 0 || heightPx <= 0)
        {
          try
          {
            widthPx = (int)Math.Round(def.Size.X);
            heightPx = (int)Math.Round(def.Size.Y);
          }
          catch
          {
          }
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
          throw new InvalidOperationException("Не удалось определить размер растра в пикселях.");
        }

        geo.WidthPx = widthPx;
        geo.HeightPx = heightPx;

        // Lower-left / UL / orientation from End/GDAL pixel→world.
        var ll = geo.PixelToWorld(0, heightPx);
        var lr = geo.PixelToWorld(widthPx, heightPx);
        var ul = geo.PixelToWorld(0, 0);
        Point3d origin = new Point3d(ll.X, ll.Y, 0);
        Vector3d uAxis = new Vector3d(lr.X - ll.X, lr.Y - ll.Y, 0);
        Vector3d vAxis = new Vector3d(ul.X - ll.X, ul.Y - ll.Y, 0);
        if (uAxis.Length < 1e-9 || vAxis.Length < 1e-9)
        {
          throw new InvalidOperationException("Вырожденная геопривязка (нулевой масштаб).");
        }

        RasterImage image = new RasterImage();
        image.SetDatabaseDefaults(db);
        image.ImageDefId = defId;
        image.ShowImage = true;
        image.Orientation = new CoordinateSystem3d(origin, uAxis, vAxis);
        image.Layer = layer;
        // Explicit entity Transparency (not ByLayer / Fade). Layer unlocked during create.
        ApplyEntityTransparency(image, opacity);

        BlockTableRecord model = (BlockTableRecord)tr.GetObject(
          SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
        imageId = model.AppendEntity(image);
        tr.AddNewlyCreatedDBObject(image, true);

        if (lockLayer)
        {
          LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
          LayerTableRecord record = (LayerTableRecord)tr.GetObject(layers[layer], OpenMode.ForWrite);
          record.IsLocked = true;
          record.IsOff = false;
          record.IsFrozen = false;
        }

        tr.Commit();
      }

      try { doc.Editor?.Regen(); } catch { }
      try { CadApp.UpdateScreen(); } catch { }

      return new Result
      {
        Path = rasterPath,
        GeoSource = geoNote + (string.IsNullOrEmpty(geo.Source) ? string.Empty : " / " + geo.Source),
        OriginX = geo.OriginX,
        OriginY = geo.OriginY,
        PixelSizeX = geo.PixelSizeX,
        PixelSizeY = geo.PixelSizeY,
        WidthPx = widthPx,
        HeightPx = heightPx,
        ImageId = imageId,
        WorldFileWritten = worldWritten
      };
    }

    private static ObjectId AddImageDef(Transaction tr, Database db, RasterImageDef def, string key)
    {
      DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
      DBDictionary imageDict;
      const string dictName = "ACAD_IMAGE_DICT";
      if (nod.Contains(dictName))
      {
        imageDict = (DBDictionary)tr.GetObject(nod.GetAt(dictName), OpenMode.ForWrite);
      }
      else
      {
        nod.UpgradeOpen();
        imageDict = new DBDictionary();
        nod.SetAt(dictName, imageDict);
        tr.AddNewlyCreatedDBObject(imageDict, true);
      }

      string name = string.IsNullOrWhiteSpace(key) ? "RASTER" : key;
      string unique = name;
      int n = 1;
      while (imageDict.Contains(unique))
      {
        unique = name + "_" + n;
        n++;
      }

      ObjectId id = imageDict.SetAt(unique, def);
      tr.AddNewlyCreatedDBObject(def, true);
      return id;
    }

    /// <summary>
    /// Opacity % (100=opaque) → entity Transparency (0..90). Never ByLayer / Fade.
    /// </summary>
    public static int ClampOpacity(int percent)
    {
      if (percent < 0) return 0;
      if (percent > 100) return 100;
      return percent;
    }

    public static byte OpacityToTransparencyValue(int opacityPercent)
    {
      int opacity = ClampOpacity(opacityPercent);
      int transparent = 100 - opacity;
      if (transparent < 0) transparent = 0;
      if (transparent > 90) transparent = 90; // CAD alpha transparency max
      return (byte)transparent;
    }

    public static void ApplyEntityTransparency(Entity entity, int opacityPercent)
    {
      if (entity == null)
      {
        return;
      }

      byte value = OpacityToTransparencyValue(opacityPercent);
      // Explicit ByAlpha 0–90 — Properties must show the number, not ByLayer.
      entity.Transparency = new Transparency(value);
      try
      {
        // Reset Fade so Properties/display aren't driven by Fade instead.
        if (entity is RasterImage raster)
        {
          raster.Fade = (byte)0;
        }
      }
      catch
      {
      }
    }

    /// <summary>
    /// Unlock layer, set RasterImage.Transparency (0–90), optionally re-lock.
    /// Locked layers often swallow Transparency writes without throwing (stays ByLayer).
    /// </summary>
    public static void SetOpacityOnLayer(string layerName, int opacityPercent, bool relockAfter = false)
    {
      if (string.IsNullOrWhiteSpace(layerName))
      {
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      int opacity = ClampOpacity(opacityPercent);
      byte expected = OpacityToTransparencyValue(opacity);

      using (doc.LockDocument())
      using (Transaction tr = doc.TransactionManager.StartTransaction())
      {
        LayerTable layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (!layers.Has(layerName))
        {
          tr.Commit();
          return;
        }

        // Always unlock first — required for Transparency to stick in Properties.
        LayerTableRecord layerRec = (LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForWrite);
        layerRec.IsLocked = false;

        BlockTableRecord model = (BlockTableRecord)tr.GetObject(
          SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);

        int applied = 0;
        foreach (ObjectId id in model)
        {
          Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
          if (!(ent is RasterImage)
            || !string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
          {
            continue;
          }

          RasterImage image = (RasterImage)tr.GetObject(id, OpenMode.ForWrite, false);
          if (image == null)
          {
            continue;
          }

          ApplyEntityTransparency(image, opacity);
          image.ShowImage = true;
          applied++;

          // Force ByAlpha again so Properties shows the number, not ByLayer.
          image.Transparency = new Transparency(expected);
        }

        if (relockAfter)
        {
          layerRec.IsLocked = true;
        }

        tr.Commit();

        if (applied == 0)
        {
          // No images — still leave lock state as requested.
        }
      }

      try { doc.Editor?.Regen(); } catch { }
      try { CadApp.UpdateScreen(); } catch { }
    }

    private static void EnsureLayer(Transaction tr, Database db)
    {
      EnsureLayer(tr, db, LayerName, unlockForWrite: true);
    }

    private static void EnsureLayer(Transaction tr, Database db, string layerName, bool unlockForWrite)
    {
      LayerTable layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
      LayerTableRecord layer;
      if (!layers.Has(layerName))
      {
        layers.UpgradeOpen();
        layer = new LayerTableRecord { Name = layerName };
        layers.Add(layer);
        tr.AddNewlyCreatedDBObject(layer, true);
      }
      else
      {
        layer = (LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForWrite);
      }

      layer.IsFrozen = false;
      if (unlockForWrite)
      {
        layer.IsLocked = false;
      }
    }

    public static void ZoomToImage(ObjectId imageId)
    {
      if (imageId.IsNull)
      {
        return;
      }

      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      try
      {
        using (doc.LockDocument())
        using (Transaction tr = doc.TransactionManager.StartTransaction())
        {
          Entity ent = tr.GetObject(imageId, OpenMode.ForRead) as Entity;
          if (ent == null)
          {
            return;
          }

          Extents3d ext = ent.GeometricExtents;
          ViewTableRecord view = doc.Editor.GetCurrentView();
          double w = Math.Max(1, ext.MaxPoint.X - ext.MinPoint.X) * 1.05;
          double h = Math.Max(1, ext.MaxPoint.Y - ext.MinPoint.Y) * 1.05;
          view.Width = w;
          view.Height = h;
          view.CenterPoint = new Point2d(
            (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
            (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0);
          doc.Editor.SetCurrentView(view);
          tr.Commit();
        }
      }
      catch
      {
      }
    }
  }
}
