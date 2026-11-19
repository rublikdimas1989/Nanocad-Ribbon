namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using OfficeOpenXml;

  /// <summary>
  /// Junction / node type catalogue — sheet «Узлы».
  /// Sizes only (no capacity). Cost from Price.xlsx «Элементы топологии» by name.
  /// </summary>
  public static class NodesWorkbookLoader
  {
    public const string SheetName = "Узлы";

    public sealed class NodeVariant
    {
      public string Name { get; set; } = "Узел";
      public double LengthM { get; set; } = 7;
      public double WidthM { get; set; } = 5;
    }

    public sealed class WorkbookData
    {
      public string Path { get; set; } = string.Empty;
      public List<NodeVariant> Variants { get; set; } = new List<NodeVariant>();
    }

    static NodesWorkbookLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static WorkbookData LoadWorkbook(string workbookPath)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл типов узлов не найден.", workbookPath);
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = FindSheet(package)
        ?? throw new InvalidOperationException("В книге нет листа «Узлы» / «Варианты узлов».");

      return new WorkbookData
      {
        Path = workbookPath,
        Variants = ReadSheet(sheet)
      };
    }

    public static void CreateSampleWorkbook(string path)
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
      ExcelWorksheet sheet = package.Workbook.Worksheets.Add(SheetName);
      string[] headers = { "Имя", "Длина площадки м", "Ширина площадки м" };
      for (int c = 0; c < headers.Length; c++)
      {
        sheet.Cells[1, c + 1].Value = headers[c];
      }

      object[][] rows =
      {
        new object[] { "Узел 3 соединения", 7, 5 },
        new object[] { "Узел 4 соединения", 9, 7 }
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

    private static List<NodeVariant> ReadSheet(ExcelWorksheet sheet)
    {
      Dictionary<string, int> headers = MapHeaders(sheet);
      int nameCol = Require(headers, "имя", "name", "тип", "тип узла");
      int lengthCol = Find(headers, "длина площадки м", "длина площадки", "length", "длина");
      int widthCol = Find(headers, "ширина площадки м", "ширина площадки", "width", "ширина");

      var list = new List<NodeVariant>();
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
        list.Add(new NodeVariant
        {
          Name = trimmed,
          LengthM = lengthCol > 0 ? ParseDouble(sheet, r, lengthCol, 7) : 7,
          WidthM = widthCol > 0 ? ParseDouble(sheet, r, widthCol, 5) : 5
        });
      }

      if (duplicates.Count > 0)
      {
        throw new InvalidOperationException(
          "Лист «" + sheet.Name + "»: имена узлов должны быть уникальными. Дубликаты: "
          + string.Join("; ", duplicates.Distinct())
          + ". Исправьте Excel и загрузите снова.");
      }

      if (list.Count == 0)
      {
        throw new InvalidOperationException("Лист «" + sheet.Name + "»: нет строк типов узлов.");
      }

      return list;
    }

    private static ExcelWorksheet FindSheet(ExcelPackage package)
    {
      foreach (ExcelWorksheet ws in package.Workbook.Worksheets)
      {
        string n = ws.Name ?? string.Empty;
        if (string.Equals(n, SheetName, StringComparison.OrdinalIgnoreCase)
          || string.Equals(n, "Nodes", StringComparison.OrdinalIgnoreCase)
          || string.Equals(n, "Варианты узлов", StringComparison.OrdinalIgnoreCase)
          || string.Equals(n, "Узлы соединения", StringComparison.OrdinalIgnoreCase))
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

      string t = text.Trim().ToLowerInvariant().Replace('\n', ' ').Replace('³', '3');
      while (t.Contains("  ", StringComparison.Ordinal))
      {
        t = t.Replace("  ", " ", StringComparison.Ordinal);
      }

      return t;
    }

    private static int Require(Dictionary<string, int> headers, params string[] keys)
    {
      int col = Find(headers, keys);
      if (col > 0)
      {
        return col;
      }

      throw new InvalidOperationException(
        "Лист Узлы: не найдены столбцы " + string.Join("/", keys) + ".");
    }

    private static int Find(Dictionary<string, int> headers, params string[] keys)
    {
      foreach (string key in keys)
      {
        string k = NormalizeHeader(key);
        if (headers.TryGetValue(k, out int col) && col > 0)
        {
          return col;
        }
      }

      foreach (KeyValuePair<string, int> pair in headers)
      {
        foreach (string key in keys)
        {
          string k = NormalizeHeader(key);
          if (k.Length >= 4 && pair.Key.Contains(k, StringComparison.OrdinalIgnoreCase))
          {
            return pair.Value;
          }
        }
      }

      return 0;
    }

    private static string CellText(ExcelWorksheet sheet, int row, int col)
    {
      return sheet.Cells[row, col].Value?.ToString() ?? string.Empty;
    }

    private static double ParseDouble(ExcelWorksheet sheet, int row, int col, double fallback)
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

      if (double.TryParse((v?.ToString() ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
      {
        return parsed;
      }

      return fallback;
    }
  }
}
