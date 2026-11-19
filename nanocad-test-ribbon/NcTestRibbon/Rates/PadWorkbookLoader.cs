namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using OfficeOpenXml;

  /// <summary>
  /// Loads KP metadata (+ optional width/length) from workbook sheet «КП».
  /// </summary>
  public static class PadWorkbookLoader
  {
    public sealed class PadRecord
    {
      public string Name { get; set; } = string.Empty;
      public double X { get; set; }
      public double Y { get; set; }
      public double PressureBar { get; set; }
      public string SeparationMode { get; set; } = "none";
      public bool ActiveOil { get; set; } = true;
      public bool ActiveLiquid { get; set; } = true;
      public bool ActiveGas { get; set; } = true;
      public double WidthM { get; set; } = 150;
      public double LengthM { get; set; } = 250;
    }

    static PadWorkbookLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static List<PadRecord> LoadPads(string workbookPath)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл таблиц КП не найден.", workbookPath);
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      ExcelWorksheet sheet = package.Workbook.Worksheets["КП"]
        ?? package.Workbook.Worksheets[0]
        ?? throw new InvalidDataException("В книге нет листа «КП».");

      var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      int cols = Math.Max(sheet.Dimension?.End.Column ?? 0, 1);
      int rows = Math.Max(sheet.Dimension?.End.Row ?? 0, 1);
      for (int c = 1; c <= cols; c++)
      {
        string key = (sheet.Cells[1, c].Text ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(key) && !headers.ContainsKey(key))
        {
          headers[key] = c;
        }
      }

      int Col(params string[] names)
      {
        foreach (string name in names)
        {
          if (headers.TryGetValue(name, out int index))
          {
            return index;
          }
        }

        return -1;
      }

      int nameCol = Col("name", "КП", "Name");
      int xCol = Col("x", "X");
      int yCol = Col("y", "Y");
      if (nameCol < 0 || xCol < 0 || yCol < 0)
      {
        throw new InvalidDataException("Лист «КП» должен содержать столбцы name/x/y (или КП/X/Y).");
      }

      int pressureCol = Col("pressureBar", "pressure");
      int separationCol = Col("separationMode", "separation");
      int oilCol = Col("active_oil", "oil");
      int liquidCol = Col("active_liquid", "liquid");
      int gasCol = Col("active_gas", "gas");
      int widthCol = Col("Ширина", "width", "widthM");
      int lengthCol = Col("Длина", "length", "lengthM");

      var result = new List<PadRecord>();
      for (int r = 2; r <= rows; r++)
      {
        string name = (sheet.Cells[r, nameCol].Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
          continue;
        }

        result.Add(new PadRecord
        {
          Name = name,
          X = ReadDouble(sheet, r, xCol, 0),
          Y = ReadDouble(sheet, r, yCol, 0),
          PressureBar = pressureCol > 0 ? ReadDouble(sheet, r, pressureCol, 0) : 0,
          SeparationMode = separationCol > 0
            ? ((sheet.Cells[r, separationCol].Text ?? "none").Trim() is { Length: > 0 } s ? s : "none")
            : "none",
          ActiveOil = oilCol < 0 || ReadBool(sheet, r, oilCol, true),
          ActiveLiquid = liquidCol < 0 || ReadBool(sheet, r, liquidCol, true),
          ActiveGas = gasCol < 0 || ReadBool(sheet, r, gasCol, true),
          WidthM = widthCol > 0 ? ReadDouble(sheet, r, widthCol, 150) : 150,
          LengthM = lengthCol > 0 ? ReadDouble(sheet, r, lengthCol, 250) : 250
        });
      }

      if (result.Count == 0)
      {
        throw new InvalidDataException("На листе «КП» нет строк с площадками.");
      }

      return result;
    }

    private static double ReadDouble(ExcelWorksheet sheet, int row, int col, double fallback)
    {
      object value = sheet.Cells[row, col].Value;
      if (value == null)
      {
        return fallback;
      }

      if (value is double d)
      {
        return d;
      }

      if (value is float f)
      {
        return f;
      }

      if (value is int i)
      {
        return i;
      }

      string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
      text = text.Trim().Replace('\u00a0', ' ').Replace(" ", string.Empty).Replace(',', '.');
      return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        ? parsed
        : fallback;
    }

    private static bool ReadBool(ExcelWorksheet sheet, int row, int col, bool fallback)
    {
      object value = sheet.Cells[row, col].Value;
      if (value == null)
      {
        return fallback;
      }

      if (value is bool b)
      {
        return b;
      }

      string text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
      if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)
          || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
          || text.Equals("да", StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }

      if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)
          || text.Equals("no", StringComparison.OrdinalIgnoreCase)
          || text.Equals("нет", StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      return fallback;
    }
  }
}
