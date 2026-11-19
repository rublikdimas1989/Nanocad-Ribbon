namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using OfficeOpenXml;

  /// <summary>
  /// Loads / saves sheet «Врезки» (End TopologyTieIn fields) from a dedicated inserts xlsx.
  /// </summary>
  public static class InsertWorkbookLoader
  {
    public const string SheetName = "Врезки";

    public sealed class InsertRecord
    {
      public string Id { get; set; } = Guid.NewGuid().ToString("N");
      public string Name { get; set; } = "Врезка";
      public bool Enabled { get; set; } = true;
      public double X { get; set; }
      public double Y { get; set; }
      /// <summary>oil | liquid | gas</summary>
      public string Fluid { get; set; } = "oil";
      public double BackPressureBar { get; set; }
      /// <summary>Null = unlimited.</summary>
      public double? MaxOilRateM3Day { get; set; }
    }

    public sealed class WorkbookData
    {
      public string Path { get; set; } = string.Empty;
      public List<InsertRecord> Inserts { get; set; } = new List<InsertRecord>();
    }

    static InsertWorkbookLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static WorkbookData LoadWorkbook(string workbookPath)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл точек врезки не найден.", workbookPath);
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = FindSheet(package)
        ?? throw new InvalidOperationException("В книге нет листа «" + SheetName + "».");

      var data = new WorkbookData { Path = workbookPath };
      data.Inserts = ReadSheet(sheet);
      return data;
    }

    public static int SaveInsertCoordinates(string workbookPath, IReadOnlyList<InsertRecord> dirty)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл точек врезки не найден.", workbookPath);
      }

      if (dirty == null || dirty.Count == 0)
      {
        return 0;
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = FindSheet(package)
        ?? throw new InvalidOperationException("В книге нет листа «" + SheetName + "».");

      Dictionary<string, int> headers = MapHeaders(sheet);
      int nameCol = Require(headers, "имя", "name", "врезка");
      int xCol = Require(headers, "x");
      int yCol = Require(headers, "y");
      headers.TryGetValue("id", out int idCol);

      int updated = 0;
      int rows = sheet.Dimension?.End.Row ?? 1;
      var byName = new Dictionary<string, InsertRecord>(StringComparer.OrdinalIgnoreCase);
      foreach (InsertRecord rec in dirty)
      {
        if (!string.IsNullOrWhiteSpace(rec?.Name))
        {
          byName[rec.Name.Trim()] = rec;
        }
      }

      for (int r = 2; r <= rows; r++)
      {
        string name = CellText(sheet, r, nameCol);
        if (string.IsNullOrWhiteSpace(name) || !byName.TryGetValue(name.Trim(), out InsertRecord rec))
        {
          continue;
        }

        sheet.Cells[r, xCol].Value = rec.X;
        sheet.Cells[r, yCol].Value = rec.Y;
        if (idCol > 0 && string.IsNullOrWhiteSpace(CellText(sheet, r, idCol)))
        {
          sheet.Cells[r, idCol].Value = rec.Id;
        }

        updated++;
      }

      package.Save();
      return updated;
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
      string[] headers =
      {
        "Id", "Имя", "Включена", "X", "Y", "Среда", "P противодавления бар", "Макс приём нефти м3/сут"
      };
      for (int c = 0; c < headers.Length; c++)
      {
        sheet.Cells[1, c + 1].Value = headers[c];
      }

      object[][] rows =
      {
        new object[] { Guid.NewGuid().ToString("N"), "Врезка-1", "Да", 618495.37, 6486631.77, "Нефть", 15, "" },
        new object[] { Guid.NewGuid().ToString("N"), "Врезка-2", "Да", 614825.645, 6499254.297, "Нефть", 11, 1200 },
        new object[] { Guid.NewGuid().ToString("N"), "Врезка-газ", "Нет", 618357.937, 6486506.552, "ПНГ", 5, "" }
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

    private static List<InsertRecord> ReadSheet(ExcelWorksheet sheet)
    {
      Dictionary<string, int> headers = MapHeaders(sheet);
      int nameCol = Require(headers, "имя", "name", "врезка");
      int xCol = Require(headers, "x");
      int yCol = Require(headers, "y");
      headers.TryGetValue("id", out int idCol);
      headers.TryGetValue("включена", out int enCol);
      if (enCol <= 0)
      {
        headers.TryGetValue("enabled", out enCol);
      }

      headers.TryGetValue("среда", out int fluidCol);
      if (fluidCol <= 0)
      {
        headers.TryGetValue("fluid", out fluidCol);
      }

      headers.TryGetValue("p противодавления бар", out int pCol);
      if (pCol <= 0)
      {
        headers.TryGetValue("backpressurebar", out pCol);
      }

      headers.TryGetValue("макс приём нефти м3/сут", out int maxCol);
      if (maxCol <= 0)
      {
        headers.TryGetValue("maxoilratem3day", out maxCol);
      }

      var list = new List<InsertRecord>();
      int rows = sheet.Dimension?.End.Row ?? 1;
      for (int r = 2; r <= rows; r++)
      {
        string name = CellText(sheet, r, nameCol);
        if (string.IsNullOrWhiteSpace(name))
        {
          continue;
        }

        var rec = new InsertRecord
        {
          Name = name.Trim(),
          Id = idCol > 0 ? (CellText(sheet, r, idCol) ?? string.Empty).Trim() : Guid.NewGuid().ToString("N"),
          Enabled = enCol > 0 ? ParseBool(CellText(sheet, r, enCol), defaultValue: true) : true,
          X = ParseDouble(sheet, r, xCol),
          Y = ParseDouble(sheet, r, yCol),
          Fluid = NormalizeFluid(fluidCol > 0 ? CellText(sheet, r, fluidCol) : "oil"),
          BackPressureBar = pCol > 0 ? ParseDouble(sheet, r, pCol) : 0
        };
        if (string.IsNullOrWhiteSpace(rec.Id))
        {
          rec.Id = Guid.NewGuid().ToString("N");
        }

        if (maxCol > 0)
        {
          string maxText = CellText(sheet, r, maxCol);
          if (!string.IsNullOrWhiteSpace(maxText))
          {
            rec.MaxOilRateM3Day = ParseDouble(sheet, r, maxCol);
          }
        }

        list.Add(rec);
      }

      return list;
    }

    private static ExcelWorksheet FindSheet(ExcelPackage package)
    {
      foreach (ExcelWorksheet ws in package.Workbook.Worksheets)
      {
        if (string.Equals(ws.Name, SheetName, StringComparison.OrdinalIgnoreCase)
          || string.Equals(ws.Name, "TieIns", StringComparison.OrdinalIgnoreCase)
          || string.Equals(ws.Name, "Inserts", StringComparison.OrdinalIgnoreCase))
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

    private static int Require(Dictionary<string, int> headers, params string[] keys)
    {
      foreach (string key in keys)
      {
        if (headers.TryGetValue(key, out int col) && col > 0)
        {
          return col;
        }
      }

      throw new InvalidOperationException(
        "Лист «" + SheetName + "»: не найдены столбцы " + string.Join("/", keys) + ".");
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

    private static bool ParseBool(string text, bool defaultValue)
    {
      if (string.IsNullOrWhiteSpace(text))
      {
        return defaultValue;
      }

      string t = text.Trim();
      if (t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase)
        || t.Equals("да", StringComparison.OrdinalIgnoreCase)
        || t.Equals("yes", StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      if (t == "0" || t.Equals("false", StringComparison.OrdinalIgnoreCase)
        || t.Equals("нет", StringComparison.OrdinalIgnoreCase)
        || t.Equals("no", StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      return defaultValue;
    }

    public static string NormalizeFluid(string text)
    {
      string t = (text ?? string.Empty).Trim().ToLowerInvariant();
      if (t.Contains("gas") || t.Contains("пнг") || t.Contains("газ"))
      {
        return "gas";
      }

      if (t.Contains("liquid") || t.Contains("жидк") || t.Contains("вода"))
      {
        return "liquid";
      }

      return "oil";
    }

    public static string FluidDisplayName(string fluid)
    {
      return NormalizeFluid(fluid) switch
      {
        "gas" => "ПНГ",
        "liquid" => "Жидкость",
        _ => "Нефть"
      };
    }
  }
}
