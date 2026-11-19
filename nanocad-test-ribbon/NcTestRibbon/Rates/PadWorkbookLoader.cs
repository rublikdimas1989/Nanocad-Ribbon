namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text.Json;
  using System.Text.RegularExpressions;
  using OfficeOpenXml;

  /// <summary>
  /// Loads KP metadata, monthly profiles («Профили»), and optional BUOS capacities from KP/Top_tree xlsx.
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
      /// <summary>pads[].sourMedium — сернистая среда.</summary>
      public bool SourMedium { get; set; }
      public double[] Oil { get; set; } = Array.Empty<double>();
      public double[] Liquid { get; set; } = Array.Empty<double>();
      public double[] Gas { get; set; } = Array.Empty<double>();
    }

    public sealed class WorkbookData
    {
      public string Path { get; set; } = string.Empty;
      public List<PadRecord> Pads { get; set; } = new List<PadRecord>();
      public int Months { get; set; }
      public string OilUnit { get; set; } = "m3_day";
      public string LiquidUnit { get; set; } = "m3_day";
      public string GasUnit { get; set; } = "m3_day";
      public string UnitsNote { get; set; } = string.Empty;
      /// <summary>True when oil is mass (t/day) and liquid is total liquid — End TotalLiquidOilMass.</summary>
      public bool OilIsTonnesPerDay => OilUnit.IndexOf("t_day", StringComparison.OrdinalIgnoreCase) >= 0
        || OilUnit.IndexOf("т/сут", StringComparison.OrdinalIgnoreCase) >= 0;
      public double CombinedCapacity { get; set; } = 4000;
      public double GasCapacity { get; set; } = 1_500_000;
      public bool CapacitiesFromBuosSheet { get; set; }
      /// <summary>True when sheet «КП» already had a sourMedium column.</summary>
      public bool SourMediumFromSheet { get; set; }
    }

    static PadWorkbookLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static List<PadRecord> LoadPads(string workbookPath)
    {
      return LoadWorkbook(workbookPath).Pads;
    }

    public static WorkbookData LoadWorkbook(string workbookPath)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл таблиц КП не найден.", workbookPath);
      }

      using var package = new ExcelPackage(new FileInfo(workbookPath));
      var data = new WorkbookData { Path = workbookPath };
      data.Pads = ReadPadSheet(package, out bool sourFromSheet);
      data.SourMediumFromSheet = sourFromSheet;
      ReadProfilesSheet(package, data);
      ReadBuosCapacities(package, data);
      return data;
    }

    /// <summary>
    /// Writes updated X/Y and sourMedium for named pads into sheet «КП».
    /// </summary>
    public static int SavePadCoordinates(string workbookPath, IEnumerable<PadRecord> pads)
    {
      if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
      {
        throw new FileNotFoundException("Файл таблиц КП не найден.", workbookPath);
      }

      if (pads == null)
      {
        return 0;
      }

      var byName = new Dictionary<string, PadRecord>(StringComparer.OrdinalIgnoreCase);
      foreach (PadRecord pad in pads)
      {
        if (pad != null && !string.IsNullOrWhiteSpace(pad.Name))
        {
          byName[pad.Name] = pad;
        }
      }

      if (byName.Count == 0)
      {
        return 0;
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
        throw new InvalidDataException("Лист «КП» должен содержать столбцы name/x/y.");
      }

      int sourCol = Col("sourMedium", "сернистая среда", "сернистая", "SourMedium");
      if (sourCol < 0)
      {
        sourCol = cols + 1;
        sheet.Cells[1, sourCol].Value = "sourMedium";
        headers["sourMedium"] = sourCol;
        cols = sourCol;
      }

      int updated = 0;
      for (int r = 2; r <= rows; r++)
      {
        string name = (sheet.Cells[r, nameCol].Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name) || !byName.TryGetValue(name, out PadRecord pad))
        {
          continue;
        }

        sheet.Cells[r, xCol].Value = pad.X;
        sheet.Cells[r, yCol].Value = pad.Y;
        sheet.Cells[r, sourCol].Value = pad.SourMedium;
        updated++;
      }

      package.Save();
      return updated;
    }

    /// <summary>
    /// Overlay pads[].sourMedium from Top_tree / KP JSON (ui.pads) onto workbook pads by name.
    /// Looks at explicit path, sibling .json next to the xlsx, then common names.
    /// </summary>
    public static int ApplySourMediumFromJson(WorkbookData data, string preferredJsonPath = null)
    {
      if (data?.Pads == null || data.Pads.Count == 0)
      {
        return 0;
      }

      var candidates = new List<string>();
      void Add(string path)
      {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && !candidates.Contains(path))
        {
          candidates.Add(path);
        }
      }

      Add(preferredJsonPath);
      if (!string.IsNullOrWhiteSpace(data.Path))
      {
        string dir = Path.GetDirectoryName(data.Path) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(data.Path);
        Add(Path.Combine(dir, stem + ".json"));
        Add(Path.Combine(dir, "Top_tree_config.json"));
      }

      foreach (string jsonPath in candidates)
      {
        try
        {
          using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
          if (!TryGetPadsArray(doc.RootElement, out JsonElement pads))
          {
            continue;
          }

          var byName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
          foreach (JsonElement pad in pads.EnumerateArray())
          {
            if (!pad.TryGetProperty("name", out JsonElement nameEl))
            {
              continue;
            }

            string name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
              continue;
            }

            if (pad.TryGetProperty("sourMedium", out JsonElement sourEl))
            {
              byName[name] = ReadJsonBool(sourEl, false);
            }
          }

          if (byName.Count == 0)
          {
            continue;
          }

          int applied = 0;
          foreach (PadRecord pad in data.Pads)
          {
            if (byName.TryGetValue(pad.Name, out bool sour))
            {
              pad.SourMedium = sour;
              applied++;
            }
          }

          if (applied > 0)
          {
            return applied;
          }
        }
        catch
        {
          // try next candidate
        }
      }

      return 0;
    }

    private static bool TryGetPadsArray(JsonElement root, out JsonElement pads)
    {
      pads = default;
      if (root.ValueKind == JsonValueKind.Object)
      {
        if (root.TryGetProperty("ui", out JsonElement ui)
            && ui.ValueKind == JsonValueKind.Object
            && ui.TryGetProperty("pads", out pads)
            && pads.ValueKind == JsonValueKind.Array)
        {
          return true;
        }

        if (root.TryGetProperty("pads", out pads) && pads.ValueKind == JsonValueKind.Array)
        {
          return true;
        }
      }

      return false;
    }

    private static bool ReadJsonBool(JsonElement el, bool fallback)
    {
      return el.ValueKind switch
      {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => el.TryGetInt32(out int n) ? n != 0 : fallback,
        JsonValueKind.String => ParseBoolText(el.GetString(), fallback),
        _ => fallback
      };
    }

    private static bool ParseBoolText(string text, bool fallback)
    {
      if (string.IsNullOrWhiteSpace(text))
      {
        return fallback;
      }

      text = text.Trim();
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

    private static List<PadRecord> ReadPadSheet(ExcelPackage package, out bool sourMediumColumnPresent)
    {
      sourMediumColumnPresent = false;
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
      int sourCol = Col("sourMedium", "сернистая среда", "сернистая", "SourMedium");
      sourMediumColumnPresent = sourCol > 0;

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
          LengthM = lengthCol > 0 ? ReadDouble(sheet, r, lengthCol, 250) : 250,
          SourMedium = sourCol > 0 && ReadBool(sheet, r, sourCol, false)
        });
      }

      if (result.Count == 0)
      {
        throw new InvalidDataException("На листе «КП» нет строк с площадками.");
      }

      return result;
    }

    private static void ReadProfilesSheet(ExcelPackage package, WorkbookData data)
    {
      ExcelWorksheet sheet = package.Workbook.Worksheets["Профили"];
      if (sheet?.Dimension == null)
      {
        foreach (PadRecord pad in data.Pads)
        {
          pad.Oil = Array.Empty<double>();
          pad.Liquid = Array.Empty<double>();
          pad.Gas = Array.Empty<double>();
        }

        data.Months = 0;
        return;
      }

      int cols = sheet.Dimension.End.Column;
      int rows = sheet.Dimension.End.Row;
      string note = (sheet.Cells[1, 1].Text ?? string.Empty).Trim();
      data.UnitsNote = note;
      ParseUnitsNote(note, data);

      int headerRow = 1;
      string h11 = (sheet.Cells[1, 1].Text ?? string.Empty).Trim();
      string h21 = rows >= 2 ? (sheet.Cells[2, 1].Text ?? string.Empty).Trim() : string.Empty;
      if (h21.Equals("Месяц", StringComparison.OrdinalIgnoreCase)
          || h21.Equals("Month", StringComparison.OrdinalIgnoreCase))
      {
        headerRow = 2;
      }
      else if (h11.Equals("Месяц", StringComparison.OrdinalIgnoreCase))
      {
        headerRow = 1;
        data.UnitsNote = string.Empty;
      }

      var oilCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      var liquidCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      var gasCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      for (int c = 2; c <= cols; c++)
      {
        string header = (sheet.Cells[headerRow, c].Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(header))
        {
          continue;
        }

        Match match = Regex.Match(header,
          @"^(?<name>.+?)\s+(?<fluid>нефть|жидкость|газ|oil|liquid|gas)\s*$",
          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
          continue;
        }

        string padName = match.Groups["name"].Value.Trim();
        string fluid = match.Groups["fluid"].Value.ToLowerInvariant();
        if (fluid is "нефть" or "oil")
        {
          oilCols[padName] = c;
        }
        else if (fluid is "жидкость" or "liquid")
        {
          liquidCols[padName] = c;
        }
        else
        {
          gasCols[padName] = c;
        }
      }

      int dataStart = headerRow + 1;
      int monthCount = Math.Max(0, rows - headerRow);
      data.Months = monthCount;

      foreach (PadRecord pad in data.Pads)
      {
        pad.Oil = new double[monthCount];
        pad.Liquid = new double[monthCount];
        pad.Gas = new double[monthCount];
        oilCols.TryGetValue(pad.Name, out int oilCol);
        liquidCols.TryGetValue(pad.Name, out int liquidCol);
        gasCols.TryGetValue(pad.Name, out int gasCol);
        for (int i = 0; i < monthCount; i++)
        {
          int r = dataStart + i;
          if (oilCol > 0)
          {
            pad.Oil[i] = ReadDouble(sheet, r, oilCol, 0);
          }

          if (liquidCol > 0)
          {
            pad.Liquid[i] = ReadDouble(sheet, r, liquidCol, 0);
          }

          if (gasCol > 0)
          {
            pad.Gas[i] = ReadDouble(sheet, r, gasCol, 0);
          }
        }
      }
    }

    private static void ParseUnitsNote(string note, WorkbookData data)
    {
      if (string.IsNullOrWhiteSpace(note))
      {
        return;
      }

      Match oil = Regex.Match(note, @"oil\s*=\s*([a-z0-9_]+)", RegexOptions.IgnoreCase);
      Match liquid = Regex.Match(note, @"liquid\s*=\s*([a-z0-9_]+)", RegexOptions.IgnoreCase);
      Match gas = Regex.Match(note, @"gas\s*=\s*([a-z0-9_]+)", RegexOptions.IgnoreCase);
      if (oil.Success)
      {
        data.OilUnit = oil.Groups[1].Value;
      }

      if (liquid.Success)
      {
        data.LiquidUnit = liquid.Groups[1].Value;
      }

      if (gas.Success)
      {
        data.GasUnit = gas.Groups[1].Value;
      }

      if (!oil.Success && !liquid.Success && note.IndexOf("m3_day", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        data.OilUnit = data.LiquidUnit = data.GasUnit = "m3_day";
      }
    }

    private static void ReadBuosCapacities(ExcelPackage package, WorkbookData data)
    {
      ExcelWorksheet sheet = package.Workbook.Worksheets["БУОС"]
        ?? package.Workbook.Worksheets["BUOS"];
      if (sheet?.Dimension == null)
      {
        return;
      }

      var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      int cols = sheet.Dimension.End.Column;
      for (int c = 1; c <= cols; c++)
      {
        string key = (sheet.Cells[1, c].Text ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(key) && !headers.ContainsKey(key))
        {
          headers[key] = c;
        }
      }

      int combinedCol = -1;
      int gasCol = -1;
      foreach (KeyValuePair<string, int> pair in headers)
      {
        string key = pair.Key;
        if (combinedCol < 0 && (key.Contains("Combined", StringComparison.OrdinalIgnoreCase)
            || key.Contains("жидк", StringComparison.OrdinalIgnoreCase)
            || key.Equals("CollectorCombinedCapacity", StringComparison.OrdinalIgnoreCase)))
        {
          combinedCol = pair.Value;
        }

        if (gasCol < 0 && (key.Contains("GasCapacity", StringComparison.OrdinalIgnoreCase)
            || key.Contains("газ", StringComparison.OrdinalIgnoreCase)
            || key.Equals("CollectorGasCapacity", StringComparison.OrdinalIgnoreCase)))
        {
          gasCol = pair.Value;
        }
      }

      if (combinedCol < 0 && gasCol < 0)
      {
        return;
      }

      double maxCombined = 0;
      double maxGas = 0;
      int rows = sheet.Dimension.End.Row;
      for (int r = 2; r <= rows; r++)
      {
        if (combinedCol > 0)
        {
          maxCombined = Math.Max(maxCombined, ReadDouble(sheet, r, combinedCol, 0));
        }

        if (gasCol > 0)
        {
          maxGas = Math.Max(maxGas, ReadDouble(sheet, r, gasCol, 0));
        }
      }

      if (maxCombined > 0)
      {
        data.CombinedCapacity = maxCombined;
        data.CapacitiesFromBuosSheet = true;
      }

      if (maxGas > 0)
      {
        data.GasCapacity = maxGas;
        data.CapacitiesFromBuosSheet = true;
      }
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
