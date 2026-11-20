namespace NcTestRibbon.Cad
{
  using System;
  using System.IO;
  using HostMgd.ApplicationServices;
  using NcTestRibbon.Shell;
  using Teigha.DatabaseServices;
  using CadApp = HostMgd.ApplicationServices.Application;

  /// <summary>
  /// Save active drawing as a DXF sidecar next to the DWG (or under Maps if unsaved).
  /// </summary>
  public static class CadDxfExport
  {
    /// <summary>
    /// Writes {dwgBase}_map.dxf beside the main drawing; overwrites if present.
    /// Returns absolute path.
    /// </summary>
    public static string SaveMapSidecar()
    {
      Document doc = CadApp.DocumentManager.MdiActiveDocument
        ?? throw new InvalidOperationException("Нет активного чертежа nanoCAD.");
      Database db = doc.Database
        ?? throw new InvalidOperationException("Нет базы данных чертежа.");

      string dxfPath = ResolveSidecarPath(doc);
      string dir = Path.GetDirectoryName(dxfPath);
      if (!string.IsNullOrEmpty(dir))
      {
        Directory.CreateDirectory(dir);
      }

      using (doc.LockDocument())
      {
        // Teigha / HostMgd: DxfOut(path, precision, exactVersionMatch)
        db.DxfOut(dxfPath, 16, false);
      }

      if (!File.Exists(dxfPath) || new FileInfo(dxfPath).Length <= 0)
      {
        throw new InvalidOperationException("DXF не записан: " + dxfPath);
      }

      return Path.GetFullPath(dxfPath);
    }

    public static string ResolveSidecarPath(Document doc)
    {
      string dwg = null;
      try { dwg = doc?.Name; }
      catch { }

      if (!string.IsNullOrWhiteSpace(dwg)
          && !dwg.StartsWith("Drawing", StringComparison.OrdinalIgnoreCase)
          && Path.IsPathRooted(dwg))
      {
        string dir = Path.GetDirectoryName(dwg) ?? string.Empty;
        string baseName = Path.GetFileNameWithoutExtension(dwg);
        if (string.IsNullOrWhiteSpace(baseName))
        {
          baseName = "map";
        }

        return Path.Combine(dir, baseName + "_map.dxf");
      }

      // Unsaved / untitled — fall back to Maps under Tracing_cache.
      string maps = TracingCache.MapsDir;
      Directory.CreateDirectory(maps);
      return Path.Combine(maps, "drawing_map.dxf");
    }
  }
}
