namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Collections.ObjectModel;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using NcTestRibbon.Configuration;
  using OfficeOpenXml;

  /// <summary>
  /// Local Price.xlsx loader for the tracing settings tab
  /// (subset of End InitialData.LoadRatesFromExcel + PipeMedium transitions).
  /// </summary>
  public static class TracingRatesLoader
  {
    public sealed class Request
    {
      public string RatesFilePath { get; set; }
      public string EnvironmentLabel { get; set; }
      public string DiameterText { get; set; }
      public string ThicknessText { get; set; }
      public string SteelGrade { get; set; }
      public string PipeType { get; set; }
      public bool RoadOpen { get; set; }
      public bool PipeOpen { get; set; }
      public bool PneumoTest { get; set; }
      public bool HydroTest { get; set; }
      public bool PpuInsulation { get; set; }
      public bool Ehs { get; set; }
      public bool DiagnosticOther { get; set; }
      public bool InLineInspection { get; set; }
      public bool GeoDef { get; set; }
      public bool GeoRep { get; set; }
      public bool GeoTerm { get; set; }
      public IReadOnlyDictionary<string, string> MediumProfile { get; set; }
    }

    public sealed class Result
    {
      public ObservableCollection<RateItem> Rates { get; } = new ObservableCollection<RateItem>();
      public List<PipeCatalogRow> Catalog { get; set; } = new List<PipeCatalogRow>();
      public string Status { get; set; } = string.Empty;
      public string SelectedCasing { get; set; } = string.Empty;
    }

    static TracingRatesLoader()
    {
      ExcelPackage.License.SetNonCommercialPersonal("NcTestRibbon");
    }

    public static Result Load(Request request)
    {
      if (request == null)
      {
        throw new ArgumentNullException(nameof(request));
      }

      if (string.IsNullOrWhiteSpace(request.RatesFilePath) || !File.Exists(request.RatesFilePath))
      {
        throw new FileNotFoundException(
          "Файл расценок не найден. Положите Price.xlsx в Tracing_cache\\Settings\\.",
          request.RatesFilePath);
      }

      var result = new Result();
      using var package = new ExcelPackage(new FileInfo(request.RatesFilePath));
      ExcelWorkbook book = package.Workbook;
      ExcelWorksheet tube = book.Worksheets["ТрубаЛч"]
        ?? throw new InvalidDataException("Нет листа ТрубаЛч в Price.xlsx.");

      result.Catalog = PipeCatalog.Read(tube);
      if (result.Catalog.Count == 0)
      {
        result.Status = "В ТрубаЛч нет пригодных строк.";
        return result;
      }

      ExcelWorksheet earth = Require(book, "Земляные_работы");
      ExcelWorksheet wood = Require(book, "Валка_леса");
      ExcelWorksheet reclamation = Require(book, "Рекультивация");
      ExcelWorksheet lezhnevka = Require(book, "Лежневка");
      ExcelWorksheet beach = Require(book, "Берег");
      ExcelWorksheet diagn = Require(book, "Диагностика");
      ExcelWorksheet geo = book.Worksheets["Геотехнический мониторинг"];
      ExcelWorksheet ehz = book.Worksheets["Устройство ЭХЗ, км"];
      ExcelWorksheet uza = book.Worksheets["Узел контроля коррозии"];
      ExcelWorksheet roads = book.Worksheets["Дороги"];
      ExcelWorksheet weld = book.Worksheets["Контроль качества сварных"];
      ExcelWorksheet overlap = book.Worksheets["Захлест"];
      ExcelWorksheet bends = book.Worksheets["Отводы"];

      Add(result, "Стоимость прокладки проезда", "км", Cell(roads, "B2", "0"));
      Add(result, "Валка леса", "га", Cell(wood, "R4"));
      Add(result, "Техническая рекультивация", "м3", Cell(reclamation, "S7"));
      Add(result, "Биологическая рекультивация", "га", Cell(reclamation, "S8"));
      Add(result, "Лежнёвка", "км", Cell(lezhnevka, "S4"));
      Add(result, "Берегоукрепление габионы", "м2", Cell(beach, "R5"));
      Add(result, "Берегоукрепление георешетка", "м2", Cell(beach, "R6"));
      Add(result, "Берегоукрепление рено матрацы", "м2", Cell(beach, "R7"));

      if (request.DiagnosticOther)
      {
        Add(result, "Диагностика предпусковая свыше 4 км", "шт", Cell(diagn, "Q7"));
      }

      if (request.InLineInspection)
      {
        Add(result, "Диагностика внутритрубная свыше 4 км", "шт", Cell(diagn, "Q8"));
      }

      if (request.GeoDef)
      {
        Add(result, "Дефмарки", "шт", Cell(geo, "R5"));
      }

      if (request.GeoRep)
      {
        Add(result, "Репер", "шт", Cell(geo, "R6"));
      }

      if (request.GeoTerm)
      {
        Add(result, "Термоскважина", "шт", Cell(geo, "R7"));
      }

      if (uza != null)
      {
        for (int r = 6; !string.IsNullOrWhiteSpace(uza.Cells["B" + r].Text); r++)
        {
          if (uza.Cells["B" + r].Text.Trim().Contains(request.DiameterText ?? string.Empty, StringComparison.Ordinal))
          {
            Add(result, "Узел контроля коррозии", "шт", uza.Cells["T" + r].Text.Trim());
            break;
          }
        }
      }

      if (bends != null)
      {
        // Best-effort bend prices from sheet header area if present.
        string bend = Cell(bends, "R5", string.Empty);
        string nonNorm = Cell(bends, "R6", string.Empty);
        if (!string.IsNullOrWhiteSpace(bend))
        {
          Add(result, "Стоимость отвода", "шт", bend);
        }

        if (!string.IsNullOrWhiteSpace(nonNorm))
        {
          Add(result, "Стоимость ненормативного отвода", "шт", nonNorm);
        }
      }

      PipeCatalogRow selected = result.Catalog.FirstOrDefault(p =>
        SameNumber(p.Diameter.ToString(CultureInfo.InvariantCulture), request.DiameterText)
        && SameNumber(FormatWall(p.Wall), request.ThicknessText)
        && string.Equals(p.Steel, request.SteelGrade?.Trim(), StringComparison.Ordinal)
        && string.Equals(p.Type, request.PipeType?.Trim(), StringComparison.Ordinal));

      if (selected == null)
      {
        // Fall back to first catalog row matching diameter only, then any first.
        selected = result.Catalog.FirstOrDefault(p =>
            SameNumber(p.Diameter.ToString(CultureInfo.InvariantCulture), request.DiameterText))
          ?? result.Catalog[0];
      }

      result.SelectedCasing = FormatWall(selected.Casing);
      int row = selected.Row;

      Add(result, "Труба", "км", tube.Cells["M" + row].Text.Trim());
      Add(result, "Укладка в траншею", "км", tube.Cells["O" + row].Text.Trim());
      Add(result, "Сварка в горизонтали", "км", tube.Cells["P" + row].Text.Trim());
      Add(result, "Сварка угол от 15 до 20", "км", tube.Cells["Q" + row].Text.Trim());
      Add(result, "Сварка угол от 20 до 28", "км", tube.Cells["R" + row].Text.Trim());
      Add(result, "Сварка в болоте", "км", tube.Cells["S" + row].Text.Trim());

      string gnb = tube.Cells["T" + row].Text.Trim();
      string open = tube.Cells["U" + row].Text.Trim();
      // Rivers: keep both transition prices (End behaviour).
      Add(result, "Переход реки открытый", "шт", open);
      Add(result, "Переход реки ГНБ", "шт", gnb);
      Add(result, request.RoadOpen ? "Переход дороги открытый" : "Переход дороги ГНБ", "шт",
        request.RoadOpen ? open : gnb);
      Add(result, request.PipeOpen ? "Переход трубы открытый" : "Переход трубы ГНБ", "шт",
        request.PipeOpen ? open : gnb);

      Add(result, "Футеровка", "км", tube.Cells["V" + row].Text.Trim());
      Add(result, "Балластировка на болотах", "км", tube.Cells["AA" + row].Text.Trim());
      Add(result, "Узел запорной арматуры", "шт", tube.Cells["AD" + row].Text.Trim());
      Add(result, "СОД", "шт", tube.Cells["AE" + row].Text.Trim());
      Add(result, "Пневмоиспытания предварительные", "км", tube.Cells["AG" + row].Text.Trim());
      Add(result, "Гидроиспытания предварительные", "км", tube.Cells["AH" + row].Text.Trim());

      if (request.PpuInsulation)
      {
        Add(result, "Дополнительная изоляция ППУ", "км", tube.Cells["AB" + row].Text.Trim());
      }

      if (request.Ehs)
      {
        Add(result, "Устройство ЭХЗ", "км", Cell(ehz, "R5"));
      }

      if (request.PneumoTest)
      {
        Add(result, "Пневмоиспытания основные", "км", tube.Cells["W" + row].Text.Trim());
        Add(result, "Монтаж узла для пневмоиспытаний", "шт", tube.Cells["X" + row].Text.Trim());
      }

      if (request.HydroTest)
      {
        Add(result, "Гидроиспытания основные", "км", tube.Cells["Y" + row].Text.Trim());
        Add(result, "Монтаж узла для гидроиспытаний", "шт", tube.Cells["Z" + row].Text.Trim());
      }

      if (weld != null)
      {
        for (int r = 7; !string.IsNullOrWhiteSpace(weld.Cells["B" + r].Text); r++)
        {
          string c = weld.Cells["C" + r].Text.Trim();
          string b = weld.Cells["B" + r].Text.Trim();
          if (c.Contains(request.DiameterText ?? string.Empty, StringComparison.Ordinal)
              && c.Contains(request.ThicknessText ?? string.Empty, StringComparison.Ordinal)
              && b.Contains(request.EnvironmentLabel ?? string.Empty, StringComparison.Ordinal))
          {
            Add(result, "Контроль сварных соединений", "шт", weld.Cells["T" + r].Text.Trim());
            break;
          }
        }
      }

      if (overlap != null)
      {
        for (int r = 6; !string.IsNullOrWhiteSpace(overlap.Cells["B" + r].Text); r++)
        {
          string b = overlap.Cells["B" + r].Text.Trim();
          string a = overlap.Cells["A" + r].Text.Trim();
          if (b.Contains(request.DiameterText ?? string.Empty, StringComparison.Ordinal)
              && b.Contains(request.ThicknessText ?? string.Empty, StringComparison.Ordinal)
              && a.Contains(request.EnvironmentLabel ?? string.Empty, StringComparison.Ordinal))
          {
            Add(result, "Захлёст", "шт", overlap.Cells["S" + r].Text.Trim());
            break;
          }
        }
      }

      // Depth earthwork tariffs (End: 24 rows).
      int buried = 0;
      for (int r = 7; r <= (earth.Dimension?.End.Row ?? 0); r++)
      {
        if (earth.Cells[r, 1].Text.Trim() == string.Empty
            && earth.Cells[r, 2].Text.Contains("700", StringComparison.Ordinal)
            && earth.Cells[r, 7].Text.Trim() != string.Empty)
        {
          Add(result, "Заглубление " + earth.Cells[r, 7].Text.Trim(), "м3", earth.Cells[r, 22].Text.Trim());
          buried++;
        }
      }

      // Apply medium transition markers into a side dictionary (for conf consumers).
      if (request.MediumProfile != null)
      {
        var ratesMap = result.Rates.ToDictionary(x => x.Name, x => x.Cost);
        PipeMediumSettings.Apply(ratesMap, request.MediumProfile);
      }

      result.Status = "Загружено расценок: " + result.Rates.Count
        + "; каталог труб: " + result.Catalog.Count
        + "; земляные тарифы: " + buried
        + ". Файл: " + request.RatesFilePath;
      return result;
    }

    private static ExcelWorksheet Require(ExcelWorkbook book, string name)
    {
      return book.Worksheets[name]
        ?? throw new InvalidDataException("Нет листа «" + name + "» в Price.xlsx.");
    }

    private static void Add(Result result, string name, string unit, string cost)
    {
      result.Rates.Add(new RateItem { Name = name, Unit = unit, Cost = cost ?? string.Empty });
    }

    private static string Cell(ExcelWorksheet sheet, string address, string fallback = "")
    {
      if (sheet == null)
      {
        return fallback;
      }

      string text = Convert.ToString(sheet.Cells[address].Value, CultureInfo.InvariantCulture)?.Trim();
      return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    private static bool SameNumber(string a, string b)
    {
      return double.TryParse((a ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
        && double.TryParse((b ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
        && Math.Abs(x - y) < 1e-8;
    }

    private static string FormatWall(double value)
    {
      return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
  }
}
