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
  /// End TopologyBuosType catalog (variants) — sheet «БУОС» / «Варианты БУОС».
  /// Identity = unique «Имя». No site coordinates.
  /// </summary>
  public static class BuosWorkbookLoader
  {
    public const string SheetName = "БУОС";

    public sealed class BuosVariant
    {
      public string Name { get; set; } = "БУОС";
      public bool UseInTopology { get; set; } = true;
      /// <summary>Жидкость (нефть + вода), м³/сут.</summary>
      public double CombinedCapacity { get; set; } = 4000;
      /// <summary>ПНГ, ст. м³/сут.</summary>
      public double GasCapacity { get; set; } = 1_500_000;
      /// <summary>Длина площадки одной установки, м.</summary>
      public double LengthM { get; set; } = 700;
      /// <summary>Ширина площадки одной установки, м.</summary>
      public double WidthM { get; set; } = 700;
      public bool UseExcelPrice { get; set; } = true;
      /// <summary>Стоимость одной установки, руб. (если цена Excel выключена).</summary>
      public double CostRub { get; set; }
    }

    public sealed class WorkbookData
    {
      public string Path { get; set; } = string.Empty;
      public List<BuosVariant> Variants { get; set; } = new List<BuosVariant>();
    }

    static BuosWorkbookLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static WorkbookData LoadWorkbook(string workbookPath)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл вариантов БУОС не найден.", workbookPath);
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = FindSheet(package)
        ?? throw new InvalidOperationException("В книге нет листа «БУОС» / «Варианты БУОС».");

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
      string[] headers =
      {
        "Имя",
        "Использовать в подборе",
        "Производительность жидкость м3/сут",
        "Производительность ПНГ ст.м3/сут",
        "Длина площадки м",
        "Ширина площадки м",
        "Цена из Excel",
        "Стоимость руб"
      };
      for (int c = 0; c < headers.Length; c++)
      {
        sheet.Cells[1, c + 1].Value = headers[c];
      }

      object[][] rows =
      {
        new object[] { "БУОС-4000", "Да", 4000, 1500000, 700, 700, "Да", 0 },
        new object[] { "БУОС-2500", "Да", 2500, 900000, 550, 550, "Да", 0 },
        new object[] { "БУОС-резерв", "Нет", 6000, 2000000, 800, 750, "Нет", 85000000 }
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

    private static List<BuosVariant> ReadSheet(ExcelWorksheet sheet)
    {
      Dictionary<string, int> headers = MapHeaders(sheet);
      int nameCol = Require(headers, "имя", "name", "тип", "тип буос");
      int useCol = Find(headers, "использовать в подборе", "useintopology", "в подборе");
      int combinedCol = Find(headers,
        "производительность жидкость м3/сут",
        "производительность: жидкость",
        "combinedcapacity",
        "жидкость м3/сут",
        "жидкость");
      int gasCol = Find(headers,
        "производительность пнг ст.м3/сут",
        "производительность по пнг",
        "gascapacity",
        "пнг ст.м3/сут",
        "пнг");
      int lengthCol = Find(headers, "длина площадки м", "длина площадки", "length", "длина");
      int widthCol = Find(headers, "ширина площадки м", "ширина площадки", "width", "ширина");
      int excelPriceCol = Find(headers, "цена из excel", "брать стоимость", "useexcelprice");
      int costCol = Find(headers, "стоимость руб", "стоимость", "costrub");

      var list = new List<BuosVariant>();
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
        list.Add(new BuosVariant
        {
          Name = trimmed,
          UseInTopology = useCol > 0 ? ParseBool(CellText(sheet, r, useCol), true) : true,
          CombinedCapacity = combinedCol > 0 ? ParseDouble(sheet, r, combinedCol, 4000) : 4000,
          GasCapacity = gasCol > 0 ? ParseDouble(sheet, r, gasCol, 1_500_000) : 1_500_000,
          LengthM = lengthCol > 0 ? ParseDouble(sheet, r, lengthCol, 700) : 700,
          WidthM = widthCol > 0 ? ParseDouble(sheet, r, widthCol, 700) : 700,
          UseExcelPrice = excelPriceCol > 0 ? ParseBool(CellText(sheet, r, excelPriceCol), true) : true,
          CostRub = costCol > 0 ? ParseDouble(sheet, r, costCol, 0) : 0
        });
      }

      if (duplicates.Count > 0)
      {
        throw new InvalidOperationException(
          "Лист «" + sheet.Name + "»: имена типов БУОС должны быть уникальными. Дубликаты: "
          + string.Join("; ", duplicates.Distinct())
          + ". Исправьте Excel и загрузите снова.");
      }

      if (list.Count == 0)
      {
        throw new InvalidOperationException("Лист «" + sheet.Name + "»: нет строк типов БУОС.");
      }

      return list;
    }

    private static ExcelWorksheet FindSheet(ExcelPackage package)
    {
      foreach (ExcelWorksheet ws in package.Workbook.Worksheets)
      {
        string n = ws.Name ?? string.Empty;
        if (string.Equals(n, SheetName, StringComparison.OrdinalIgnoreCase)
          || string.Equals(n, "BUOS", StringComparison.OrdinalIgnoreCase)
          || string.Equals(n, "Варианты БУОС", StringComparison.OrdinalIgnoreCase)
          || string.Equals(n, "BuosTypes", StringComparison.OrdinalIgnoreCase))
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
        "Лист БУОС: не найдены столбцы " + string.Join("/", keys) + ".");
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

      // Fuzzy: header contains key fragment.
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
  }
}
