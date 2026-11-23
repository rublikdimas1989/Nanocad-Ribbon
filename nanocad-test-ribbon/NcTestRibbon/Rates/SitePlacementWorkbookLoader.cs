namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using OfficeOpenXml;

  /// <summary>
  /// Placement Excel for source-data БУОС / Узлы: Name, X, Y, Angle, Width, Length.
  /// Unique «Имя» required (like inserts). Saves pose X/Y/Angle by name.
  /// </summary>
  public static class SitePlacementWorkbookLoader
  {
    public const string BuosSheetName = "Размещение БУОС";
    public const string NodesSheetName = "Размещение Узлы";

    public sealed class PlacementRecord
    {
      public string Name { get; set; } = string.Empty;
      public double X { get; set; }
      public double Y { get; set; }
      public double AngleDeg { get; set; }
      public double WidthM { get; set; }
      public double LengthM { get; set; }
    }

    public sealed class WorkbookData
    {
      public string Path { get; set; } = string.Empty;
      public string SheetName { get; set; } = string.Empty;
      public List<PlacementRecord> Sites { get; set; } = new List<PlacementRecord>();
    }

    static SitePlacementWorkbookLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static WorkbookData LoadWorkbook(string workbookPath, string preferredSheet)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл размещения не найден.", workbookPath);
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = FindSheet(package, preferredSheet)
        ?? throw new InvalidOperationException(
          "В книге нет листа «" + preferredSheet + "» (или известного алиаса).");

      var data = new WorkbookData
      {
        Path = workbookPath,
        SheetName = sheet.Name
      };
      data.Sites = ReadSheet(sheet, preferredSheet);
      return data;
    }

    public static int SavePlacementPose(string workbookPath, string preferredSheet, IReadOnlyList<PlacementRecord> dirty)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл размещения не найден.", workbookPath);
      }

      if (dirty == null || dirty.Count == 0)
      {
        return 0;
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = FindSheet(package, preferredSheet)
        ?? throw new InvalidOperationException("В книге нет листа «" + preferredSheet + "».");

      Dictionary<string, int> headers = MapHeaders(sheet);
      int nameCol = Require(headers, preferredSheet, "имя", "name");
      int xCol = Require(headers, preferredSheet, "x");
      int yCol = Require(headers, preferredSheet, "y");
      int angCol = Find(headers, "угол", "angle", "rotation", "угол °", "угол°");
      int wCol = Find(headers, "ширина", "width", "ширина м", "ширина, м", "ширина площадки", "ширина площадки м");
      int lCol = Find(headers, "длина", "length", "длина м", "длина, м", "длина площадки", "длина площадки м");
      int updated = 0;
      int rows = sheet.Dimension?.End.Row ?? 1;
      var byName = new Dictionary<string, PlacementRecord>(StringComparer.OrdinalIgnoreCase);
      foreach (PlacementRecord rec in dirty)
      {
        if (!string.IsNullOrWhiteSpace(rec?.Name))
        {
          byName[rec.Name.Trim()] = rec;
        }
      }

      var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      for (int r = 2; r <= rows; r++)
      {
        string name = CellText(sheet, r, nameCol);
        if (string.IsNullOrWhiteSpace(name) || !byName.TryGetValue(name.Trim(), out PlacementRecord rec))
        {
          continue;
        }

        WritePoseRow(sheet, r, nameCol, xCol, yCol, angCol, wCol, lCol, rec);
        matched.Add(rec.Name.Trim());
        updated++;
      }

      // Upsert: append CAD instances that are not yet in Excel (e.g. Add Узел/БУОС).
      foreach (KeyValuePair<string, PlacementRecord> pair in byName)
      {
        if (matched.Contains(pair.Key))
        {
          continue;
        }

        int r = (sheet.Dimension?.End.Row ?? 1) + 1;
        WritePoseRow(sheet, r, nameCol, xCol, yCol, angCol, wCol, lCol, pair.Value);
        updated++;
      }

      package.Save();
      return updated;
    }

    private static void WritePoseRow(
      ExcelWorksheet sheet,
      int row,
      int nameCol,
      int xCol,
      int yCol,
      int angCol,
      int wCol,
      int lCol,
      PlacementRecord rec)
    {
      sheet.Cells[row, nameCol].Value = rec.Name;
      sheet.Cells[row, xCol].Value = rec.X;
      sheet.Cells[row, yCol].Value = rec.Y;
      if (angCol > 0)
      {
        sheet.Cells[row, angCol].Value = rec.AngleDeg;
      }

      if (wCol > 0 && rec.WidthM > 0)
      {
        sheet.Cells[row, wCol].Value = rec.WidthM;
      }

      if (lCol > 0 && rec.LengthM > 0)
      {
        sheet.Cells[row, lCol].Value = rec.LengthM;
      }
    }

    public static void CreateSampleWorkbook(string path, string sheetName)
    {
      if (string.IsNullOrWhiteSpace(path))
      {
        throw new ArgumentException("Путь к xlsx пуст.", nameof(path));
      }

      string dir = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(dir))
      {
        Directory.CreateDirectory(dir);
      }

      using var package = new ExcelPackage();
      ExcelWorksheet sheet = package.Workbook.Worksheets.Add(sheetName);
      string[] headers = { "Имя", "X", "Y", "Угол", "Ширина", "Длина" };
      for (int c = 0; c < headers.Length; c++)
      {
        sheet.Cells[1, c + 1].Value = headers[c];
      }

      bool buos = sheetName.IndexOf("БУОС", StringComparison.OrdinalIgnoreCase) >= 0
        || sheetName.IndexOf("Buos", StringComparison.OrdinalIgnoreCase) >= 0;
      object[][] rows = buos
        ? new[]
        {
          new object[] { "БУОС-1", 630100, 6500100, 0, 50, 70 },
          new object[] { "БУОС-2", 630500, 6500500, 30, 60, 80 }
        }
        : new[]
        {
          new object[] { "Узел-1", 630200, 6500200, 0, 20, 20 },
          new object[] { "Узел-2", 630600, 6500600, 45, 25, 30 }
        };

      for (int i = 0; i < rows.Length; i++)
      {
        for (int c = 0; c < rows[i].Length; c++)
        {
          sheet.Cells[i + 2, c + 1].Value = rows[i][c];
        }
      }

      sheet.Cells.AutoFitColumns();
      package.SaveAs(new FileInfo(path));
    }

    private static List<PlacementRecord> ReadSheet(ExcelWorksheet sheet, string preferredSheet)
    {
      Dictionary<string, int> headers = MapHeaders(sheet);
      int nameCol = Require(headers, preferredSheet, "имя", "name");
      int xCol = Require(headers, preferredSheet, "x");
      int yCol = Require(headers, preferredSheet, "y");
      int angCol = Find(headers, "угол", "angle", "rotation", "угол °", "угол°");
      int wCol = Find(headers, "ширина", "width", "ширина м", "ширина, м");
      int lCol = Find(headers, "длина", "length", "длина м", "длина, м");
      if (wCol <= 0)
      {
        wCol = Find(headers, "ширина площадки", "ширина площадки м");
      }

      if (lCol <= 0)
      {
        lCol = Find(headers, "длина площадки", "длина площадки м");
      }

      if (wCol <= 0 || lCol <= 0)
      {
        throw new InvalidOperationException(
          "Лист «" + preferredSheet + "»: нужны столбцы Ширина и Длина.");
      }

      var list = new List<PlacementRecord>();
      var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      var duplicates = new List<string>();
      int rows = sheet.Dimension?.End.Row ?? 1;
      for (int r = 2; r <= rows; r++)
      {
        string name = CellText(sheet, r, nameCol);
        if (string.IsNullOrWhiteSpace(name))
        {
          continue;
        }

        string trimmed = name.Trim();
        if (seen.TryGetValue(trimmed, out int firstRow))
        {
          duplicates.Add("«" + trimmed + "» (строки " + firstRow + " и " + r + ")");
          continue;
        }

        seen[trimmed] = r;
        list.Add(new PlacementRecord
        {
          Name = trimmed,
          X = ParseDouble(sheet, r, xCol),
          Y = ParseDouble(sheet, r, yCol),
          AngleDeg = angCol > 0 ? ParseDouble(sheet, r, angCol) : 0,
          WidthM = ParseDouble(sheet, r, wCol),
          LengthM = ParseDouble(sheet, r, lCol)
        });
      }

      if (duplicates.Count > 0)
      {
        var sb = new StringBuilder();
        sb.Append("Лист «").Append(preferredSheet).Append("»: имена должны быть уникальными. Дубликаты: ");
        sb.Append(string.Join("; ", duplicates.Distinct()));
        sb.Append(". Исправьте Excel и загрузите снова.");
        throw new InvalidOperationException(sb.ToString());
      }

      return list;
    }

    private static ExcelWorksheet FindSheet(ExcelPackage package, string preferredSheet)
    {
      string[] aliases = preferredSheet == BuosSheetName
        ? new[] { BuosSheetName, "БУОС размещение", "BuosPlacement", "BUOS Placement" }
        : new[] { NodesSheetName, "Узлы размещение", "NodesPlacement", "Nodes Placement" };

      foreach (ExcelWorksheet ws in package.Workbook.Worksheets)
      {
        foreach (string alias in aliases)
        {
          if (string.Equals(ws.Name, alias, StringComparison.OrdinalIgnoreCase))
          {
            return ws;
          }
        }
      }

      // Fallback: exact preferred only.
      foreach (ExcelWorksheet ws in package.Workbook.Worksheets)
      {
        if (string.Equals(ws.Name, preferredSheet, StringComparison.OrdinalIgnoreCase))
        {
          return ws;
        }
      }

      return null;
    }

    private static Dictionary<string, int> MapHeaders(ExcelWorksheet sheet)
    {
      var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      int cols = sheet.Dimension?.End.Column ?? 0;
      for (int c = 1; c <= cols; c++)
      {
        string h = NormalizeHeader(CellText(sheet, 1, c));
        if (!string.IsNullOrEmpty(h) && !map.ContainsKey(h))
        {
          map[h] = c;
        }
      }

      return map;
    }

    private static string NormalizeHeader(string text)
    {
      if (string.IsNullOrWhiteSpace(text))
      {
        return string.Empty;
      }

      string t = text.Trim().ToLowerInvariant().Replace('\n', ' ');
      while (t.Contains("  ", StringComparison.Ordinal))
      {
        t = t.Replace("  ", " ", StringComparison.Ordinal);
      }

      return t;
    }

    private static int Require(Dictionary<string, int> headers, string sheet, params string[] keys)
    {
      int col = Find(headers, keys);
      if (col > 0)
      {
        return col;
      }

      throw new InvalidOperationException(
        "Лист «" + sheet + "»: не найдены столбцы " + string.Join("/", keys) + ".");
    }

    private static int Find(Dictionary<string, int> headers, params string[] keys)
    {
      foreach (string key in keys)
      {
        if (headers.TryGetValue(key, out int col) && col > 0)
        {
          return col;
        }
      }

      return 0;
    }

    private static string CellText(ExcelWorksheet sheet, int row, int col)
    {
      object v = sheet.Cells[row, col].Value;
      return v?.ToString() ?? string.Empty;
    }

    private static double ParseDouble(ExcelWorksheet sheet, int row, int col)
    {
      object v = sheet.Cells[row, col].Value;
      if (v is double d)
      {
        return d;
      }

      if (v is float f)
      {
        return f;
      }

      if (v is int i)
      {
        return i;
      }

      double.TryParse((v?.ToString() ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed);
      return parsed;
    }
  }
}
