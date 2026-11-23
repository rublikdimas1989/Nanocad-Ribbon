namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text.RegularExpressions;
  using OfficeOpenXml;

  /// <summary>
  /// End TopologyNamedExcelPrices — sheet «Элементы топологии», name in A, price in B.
  /// </summary>
  public static class TopologyElementPrices
  {
    public const string SheetName = "Элементы топологии";
    public const string MissingHint = "введите в Price стоимость";

    static TopologyElementPrices()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static string Normalize(string name)
    {
      return Regex.Replace((name ?? string.Empty).Replace('\u00a0', ' ').Trim(), @"\s+", " ");
    }

    /// <summary>Load A→B named prices. Duplicate names are skipped.</summary>
    public static Dictionary<string, double> Load(string ratesFilePath)
    {
      var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
      if (string.IsNullOrWhiteSpace(ratesFilePath) || !File.Exists(ratesFilePath))
      {
        return map;
      }

      using var package = new ExcelPackage(new FileInfo(ratesFilePath));
      ExcelWorksheet sheet = package.Workbook.Worksheets[SheetName];
      if (sheet == null)
      {
        return map;
      }

      int rows = sheet.Dimension?.End.Row ?? 0;
      var groups = new Dictionary<string, List<(int Row, object Value)>>(StringComparer.OrdinalIgnoreCase);
      for (int r = 1; r <= rows; r++)
      {
        string name = Normalize(sheet.Cells[r, 1].Text);
        if (string.IsNullOrWhiteSpace(name)
          || name.Equals("Наименование", StringComparison.OrdinalIgnoreCase)
          || name.Equals("Название", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        if (!groups.TryGetValue(name, out List<(int Row, object Value)> list))
        {
          list = new List<(int, object)>();
          groups[name] = list;
        }

        list.Add((r, sheet.Cells[r, 2].Value));
      }

      foreach (KeyValuePair<string, List<(int Row, object Value)>> group in groups)
      {
        if (group.Value.Count != 1)
        {
          continue;
        }

        if (TryNumber(group.Value[0].Value, out double price) && price >= 0 && double.IsFinite(price))
        {
          map[group.Key] = price;
        }
      }

      return map;
    }

    public static bool TryGet(IReadOnlyDictionary<string, double> prices, string name, out double price)
    {
      return prices.TryGetValue(Normalize(name), out price);
    }

    private static bool TryNumber(object value, out double result)
    {
      result = 0;
      if (value == null)
      {
        return false;
      }

      if (value is double d)
      {
        result = d;
        return double.IsFinite(d);
      }

      if (value is float or decimal or int or long or short or byte)
      {
        result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return double.IsFinite(result);
      }

      string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
      s = Regex.Replace(s, @"\s+", string.Empty).Replace(',', '.');
      return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        && double.IsFinite(result);
    }
  }
}
