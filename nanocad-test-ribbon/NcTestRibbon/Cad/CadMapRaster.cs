namespace NcTestRibbon.Cad
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using HostMgd.ApplicationServices;
  using Teigha.DatabaseServices;
  using NcTestRibbon.Shell;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// Cartography rasters on locked layers (KP-style): Forest / Height / Screen_ESRI.
  /// Uncheck → layer IsOff (lightbulb); never move/delete while locked.
  /// </summary>
  public static class CadMapRaster
  {
    public enum Kind
    {
      Forest,
      Height,
      Screen
    }

    public const string LayerForest = "NC_MAP_FOREST";
    public const string LayerHeight = "NC_MAP_HEIGHT";
    public const string LayerScreen = "NC_MAP_SCREEN";

    public static string LayerName(Kind kind)
    {
      switch (kind)
      {
        case Kind.Forest: return LayerForest;
        case Kind.Height: return LayerHeight;
        case Kind.Screen: return LayerScreen;
        default: return CadRasterAttach.LayerName;
      }
    }

    public static string DisplayName(Kind kind)
    {
      switch (kind)
      {
        case Kind.Forest: return "Лес (Forest)";
        case Kind.Height: return "Высоты (Height)";
        case Kind.Screen: return "Космоснимок (Screen_ESRI)";
        default: return kind.ToString();
      }
    }

    /// <summary>Preferred Maps paths (first existing wins).</summary>
    public static IReadOnlyList<string> MapsCandidates(Kind kind)
    {
      string maps = TracingCache.MapsDir;
      switch (kind)
      {
        case Kind.Forest:
          // Only prepared RGB *_geo — raw Forest.tif (0/1) stays black until colorize.
          return new[]
          {
            Path.Combine(maps, "Forest_geo.tif"),
            Path.Combine(maps, "Forest_geo.tiff")
          };
        case Kind.Height:
          return new[]
          {
            Path.Combine(maps, "Height_geo.tif"),
            Path.Combine(maps, "Height_geo.tiff")
          };
        case Kind.Screen:
          return new[]
          {
            Path.Combine(maps, "Screen_ESRI.ecw"),
            Path.Combine(maps, "Screen_ESRI.tif"),
            Path.Combine(maps, "Screen_ESRI.tiff"),
            Path.Combine(maps, "Screen_ESRI_geo.ecw"),
            Path.Combine(maps, "Screen_ESRI_geo.tif"),
            Path.Combine(maps, "Screen_ESRI_geo.tiff")
          };
        default:
          return Array.Empty<string>();
      }
    }

    public static string FindMapsFile(Kind kind)
    {
      TracingCache.EnsureCreated();
      Directory.CreateDirectory(TracingCache.MapsDir);
      foreach (string path in MapsCandidates(kind))
      {
        try
        {
          if (File.Exists(path))
          {
            return Path.GetFullPath(path);
          }
        }
        catch
        {
        }
      }

      return null;
    }

    public static bool HasDrawingImage(Kind kind)
    {
      return !FindFirstImageId(LayerName(kind)).IsNull;
    }

    public static void SetVisible(Kind kind, bool visible)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      string layerName = LayerName(kind);
      using (doc.LockDocument())
      using (Transaction tr = doc.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, doc.Database, layerName, unlockForWrite: true);
        LayerTable layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForWrite);
        layer.IsOff = !visible;
        layer.IsFrozen = false;
        layer.IsLocked = true;
        tr.Commit();
      }

      try { doc.Editor?.Regen(); } catch { }
      try { CadApp.UpdateScreen(); } catch { }
    }

    public static void SetOpacity(Kind kind, int opacityPercent)
    {
      int opacity = ClampOpacity(opacityPercent);
      int fade = 100 - opacity;
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      string layerName = LayerName(kind);
      using (doc.LockDocument())
      using (Transaction tr = doc.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, doc.Database, layerName, unlockForWrite: true);
        foreach (ObjectId id in EnumerateImagesOnLayer(tr, doc.Database, layerName))
        {
          RasterImage image = tr.GetObject(id, OpenMode.ForWrite) as RasterImage;
          if (image == null)
          {
            continue;
          }

          try { image.Fade = (byte)fade; }
          catch { /* older host */ }
          image.ShowImage = true;
        }

        LayerTable layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (layers.Has(layerName))
        {
          LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForWrite);
          layer.IsLocked = true;
        }

        tr.Commit();
      }

      try { doc.Editor?.Regen(); } catch { }
      try { CadApp.UpdateScreen(); } catch { }
    }

    public static bool IsLayerOn(Kind kind)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return false;
      }

      string layerName = LayerName(kind);
      try
      {
        using (doc.LockDocument())
        using (Transaction tr = doc.TransactionManager.StartTransaction())
        {
          LayerTable layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
          if (!layers.Has(layerName))
          {
            return false;
          }

          LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForRead);
          bool on = !layer.IsOff && !layer.IsFrozen;
          tr.Commit();
          return on;
        }
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Attach raster onto locked map layer (replaces prior images on that layer).
    /// </summary>
    public static CadRasterAttach.Result AttachToLayer(string rasterPath, Kind kind, int opacityPercent)
    {
      string layer = LayerName(kind);
      ClearImagesOnLayer(layer);
      CadRasterAttach.Result result = CadRasterAttach.Attach(
        rasterPath,
        writeWorldSidecar: true,
        layerName: layer,
        lockLayer: true,
        opacityPercent: opacityPercent);
      SetVisible(kind, true);
      return result;
    }

    /// <summary>
    /// Ensure kind is on drawing: reuse layer image, else Maps file, else null (caller must pick).
    /// </summary>
    public static CadRasterAttach.Result TryShowExisting(Kind kind, int opacityPercent, out string note)
    {
      note = string.Empty;
      if (HasDrawingImage(kind))
      {
        SetOpacity(kind, opacityPercent);
        SetVisible(kind, true);
        note = RasterGeoTransform.PackageStamp()
          + " " + DisplayName(kind) + " layer On (existing)";
        return new CadRasterAttach.Result
        {
          Path = FindMapsFile(kind) ?? string.Empty,
          ImageId = FindFirstImageId(LayerName(kind))
        };
      }

      string maps = FindMapsFile(kind);
      if (string.IsNullOrEmpty(maps))
      {
        return null;
      }

      CadRasterAttach.Result attached = AttachToLayer(maps, kind, opacityPercent);
      note = RasterGeoTransform.PackageStamp()
        + " " + DisplayName(kind) + " ← Maps\\" + Path.GetFileName(maps);
      return attached;
    }

    /// <summary>
    /// Open/prepare path for kind then attach. Forest/Height colorize; Screen TIF→ECW/Maps.
    /// </summary>
    public static CadRasterAttach.Result OpenAndAttach(Kind kind, string pickedPath, int opacityPercent, out string note)
    {
      if (string.IsNullOrWhiteSpace(pickedPath) || !File.Exists(pickedPath))
      {
        throw new FileNotFoundException("Файл не найден.", pickedPath);
      }

      string full = Path.GetFullPath(pickedPath);
      string prepared = full;
      note = string.Empty;

      if (kind == Kind.Forest)
      {
        if (!LooksLikePreparedGeo(full, "Forest"))
        {
          CadRasterColorize.Result colored = CadRasterColorize.Colorize(full, CadRasterColorize.Mode.Forest);
          prepared = colored.OutputPath;
          note = colored.Note;
        }
      }
      else if (kind == Kind.Height)
      {
        if (!LooksLikePreparedGeo(full, "Height"))
        {
          CadRasterColorize.Result colored = CadRasterColorize.Colorize(full, CadRasterColorize.Mode.Height);
          prepared = colored.OutputPath;
          note = colored.Note;
        }
      }
      else if (kind == Kind.Screen)
      {
        string ext = (Path.GetExtension(full) ?? string.Empty).ToLowerInvariant();
        if (ext == ".tif" || ext == ".tiff")
        {
          CadTifToEcw.ConvertResult converted = CadTifToEcw.Convert(full);
          prepared = !string.IsNullOrWhiteSpace(converted.OutputPath)
            ? converted.OutputPath
            : converted.EcwPath;
          note = converted.Note;
        }
        else
        {
          // ECW/other: copy into Maps\Screen_ESRI.* when not already there.
          prepared = EnsureScreenInMaps(full);
          note = RasterGeoTransform.PackageStamp()
            + " Screen ← " + Path.GetFileName(prepared);
        }
      }

      CadRasterAttach.Result result = AttachToLayer(prepared, kind, opacityPercent);
      if (string.IsNullOrEmpty(note))
      {
        note = RasterGeoTransform.PackageStamp()
          + " " + DisplayName(kind) + " → " + Path.GetFileName(prepared);
      }

      return result;
    }

    private static bool LooksLikePreparedGeo(string path, string stem)
    {
      // Only skip colorize for already-prepared *_geo RGB outputs.
      string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
      return name.Equals(stem + "_geo", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureScreenInMaps(string sourceFull)
    {
      TracingCache.EnsureCreated();
      Directory.CreateDirectory(TracingCache.MapsDir);
      string ext = Path.GetExtension(sourceFull) ?? ".ecw";
      string dest = Path.Combine(TracingCache.MapsDir, "Screen_ESRI" + ext);
      try
      {
        if (string.Equals(Path.GetFullPath(sourceFull), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
          return dest;
        }
      }
      catch
      {
      }

      File.Copy(sourceFull, dest, overwrite: true);
      return dest;
    }

    public static int ClampOpacity(int percent)
    {
      if (percent < 0) return 0;
      if (percent > 100) return 100;
      return percent;
    }

    private static ObjectId FindFirstImageId(string layerName)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return ObjectId.Null;
      }

      try
      {
        using (doc.LockDocument())
        using (Transaction tr = doc.TransactionManager.StartTransaction())
        {
          foreach (ObjectId id in EnumerateImagesOnLayer(tr, doc.Database, layerName))
          {
            tr.Commit();
            return id;
          }

          tr.Commit();
        }
      }
      catch
      {
      }

      return ObjectId.Null;
    }

    private static void ClearImagesOnLayer(string layerName)
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument;
      if (doc == null)
      {
        return;
      }

      using (doc.LockDocument())
      using (Transaction tr = doc.TransactionManager.StartTransaction())
      {
        EnsureLayer(tr, doc.Database, layerName, unlockForWrite: true);
        var erase = new List<ObjectId>();
        foreach (ObjectId id in EnumerateImagesOnLayer(tr, doc.Database, layerName))
        {
          erase.Add(id);
        }

        foreach (ObjectId id in erase)
        {
          DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
          obj?.Erase();
        }

        LayerTable layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
        if (layers.Has(layerName))
        {
          LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layers[layerName], OpenMode.ForWrite);
          layer.IsLocked = true;
          layer.IsOff = false;
        }

        tr.Commit();
      }
    }

    private static IEnumerable<ObjectId> EnumerateImagesOnLayer(Transaction tr, Database db, string layerName)
    {
      BlockTableRecord model = (BlockTableRecord)tr.GetObject(
        SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
      foreach (ObjectId id in model)
      {
        Entity ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
        if (ent == null)
        {
          continue;
        }

        if (!string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        if (ent is RasterImage)
        {
          yield return id;
        }
      }
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
  }
}
