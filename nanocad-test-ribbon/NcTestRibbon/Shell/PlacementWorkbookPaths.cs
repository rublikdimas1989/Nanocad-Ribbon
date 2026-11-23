namespace NcTestRibbon.Shell
{
  using System;
  using System.IO;
  using System.Windows.Forms;
  using NcTestRibbon.Configuration;
  using NcTestRibbon.Rates;

  /// <summary>
  /// Resolve / create placement Excel for БУОС / Узлы instances
  /// (sheets «Размещение БУОС» / «Размещение Узлы»). Never the catalog sheets «БУОС» / «Узлы».
  /// </summary>
  public static class PlacementWorkbookPaths
  {
    /// <summary>
    /// Returns a usable placement workbook path, prompting create/open when missing.
    /// Ensures the placement sheet exists (creates it if the file only has catalog sheets).
    /// </summary>
    public static string Ensure(IWin32Window owner, bool buos)
    {
      string path = buos
        ? AppPaths.Load().BuosPlacementWorkbook
        : AppPaths.Load().NodesPlacementWorkbook;

      // Placement path empty: do not silently reuse catalog workbook — ask for placement file.
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        string sheet = buos
          ? SitePlacementWorkbookLoader.BuosSheetName
          : SitePlacementWorkbookLoader.NodesSheetName;
        path = EnsurePlacementWorkbookDialog.EnsurePath(
          owner,
          buos ? "БУОС" : "Узлы",
          sheet,
          path,
          p =>
          {
            if (buos)
            {
              AppPaths.Set(x => x.BuosPlacementWorkbook = p);
            }
            else
            {
              AppPaths.Set(x => x.NodesPlacementWorkbook = p);
            }
          });
      }

      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        throw new InvalidOperationException("Файл размещения не выбран.");
      }

      string preferred = buos
        ? SitePlacementWorkbookLoader.BuosSheetName
        : SitePlacementWorkbookLoader.NodesSheetName;
      SitePlacementWorkbookLoader.EnsurePlacementSheet(path, preferred);
      return path;
    }
  }
}
