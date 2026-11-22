namespace NcTestRibbon.Rates
{
  using System;
  using System.Collections.Generic;
  using System.Linq;

  /// <summary>
  /// Port of End <c>TopologyPadLoadPreview</c>: one KP vs one BUOS, per-month limiting fluid.
  /// </summary>
  public static class PadLoadPreview
  {
    public const double DefaultCombinedCapacity = 4000;
    public const double DefaultGasCapacity = 1_500_000;
    public const double DefaultLiquidTolerance = 0.2;
    public const double DefaultGasTolerance = 0.2;
    public const double DefaultOilDensityKgM3 = 850;

    public sealed class MonthLoad
    {
      public int Month { get; set; }
      public double LiquidDisplay { get; set; }
      public double OilDisplay { get; set; }
      public double GasDisplay { get; set; }
      public double OilVolumeM3Day { get; set; }
      public double? WaterCutPercent { get; set; }
      public double[] Percent { get; set; } = new double[3];
      public string LimitingFluid { get; set; } = "Нет нагрузки";
      public bool Overloaded => Percent.Any(p => p > 100 + 1e-9);
      public string Status => LimitingFluid + (Overloaded ? " — превышение" : string.Empty);
    }

    public sealed class Result
    {
      public IReadOnlyList<MonthLoad> Months { get; set; } = Array.Empty<MonthLoad>();
      public double CombinedCapacity { get; set; }
      public double GasCapacity { get; set; }
      public double OilDensityKgM3 { get; set; }
      public string DensityNote { get; set; } = string.Empty;
    }

    public static Result Build(
      PadWorkbookLoader.PadRecord pad,
      PadWorkbookLoader.WorkbookData book,
      double oilDensityKgM3 = double.NaN,
      double combinedCapacity = double.NaN,
      double gasCapacity = double.NaN,
      double liquidTolerance = DefaultLiquidTolerance,
      double gasTolerance = DefaultGasTolerance,
      double profileReserve = 1,
      double futureReserve = 1)
    {
      if (pad == null)
      {
        throw new ArgumentNullException(nameof(pad));
      }

      int months = Math.Max(
        Math.Max(pad.Oil?.Length ?? 0, pad.Liquid?.Length ?? 0),
        pad.Gas?.Length ?? 0);
      if (months < 1)
      {
        return new Result
        {
          CombinedCapacity = FiniteOr(combinedCapacity, book?.CombinedCapacity ?? DefaultCombinedCapacity),
          GasCapacity = FiniteOr(gasCapacity, book?.GasCapacity ?? DefaultGasCapacity)
        };
      }

      double reserve = profileReserve * futureReserve;
      if (!double.IsFinite(reserve) || reserve < 1)
      {
        reserve = 1;
      }

      double combined = FiniteOr(combinedCapacity, book?.CombinedCapacity ?? DefaultCombinedCapacity);
      double gasCap = FiniteOr(gasCapacity, book?.GasCapacity ?? DefaultGasCapacity);
      if (combined <= 0 || gasCap <= 0)
      {
        throw new ArgumentException("Проверьте мощности БУОС.");
      }

      bool oilMass = book?.OilIsTonnesPerDay == true;
      double density = double.IsFinite(oilDensityKgM3) && oilDensityKgM3 > 0
        ? oilDensityKgM3
        : DefaultOilDensityKgM3;
      string densityNote = oilMass
        ? (double.IsFinite(oilDensityKgM3) && oilDensityKgM3 > 0
          ? $"нефть т/сут → м³/сут при ρ={density:0.###} кг/м³"
          : $"нефть т/сут → м³/сут при ρ={density:0.###} кг/м³ (fallback)")
        : "нефть уже в м³/сут";

      double[] caps = { combined, combined, gasCap };
      var rows = new List<MonthLoad>(months);
      for (int m = 0; m < months; m++)
      {
        double oilRaw = pad.ActiveOil ? At(pad.Oil, m) : 0;
        double liquidRaw = pad.ActiveLiquid ? At(pad.Liquid, m) : 0;
        double gasRaw = pad.ActiveGas ? At(pad.Gas, m) : 0;

        double oilVol = oilMass ? OilVolume(oilRaw, density) : oilRaw;
        double water;
        if (oilMass)
        {
          // End TotalLiquidOilMass: liquid column is total liquid; water = total − oil.
          double tolerance = 1e-9 * Math.Max(1, Math.Max(liquidRaw, oilVol));
          if (oilVol > liquidRaw + tolerance)
          {
            water = 0;
          }
          else
          {
            water = Math.Max(0, liquidRaw - oilVol);
          }
        }
        else
        {
          // Component volumes: liquid column is water (or free water).
          water = liquidRaw;
        }

        double[] profile = { water, oilVol, gasRaw };
        bool[] active = { pad.ActiveLiquid, pad.ActiveOil, pad.ActiveGas };
        double[] design = Enumerable.Range(0, 3)
          .Select(f => active[f] ? profile[f] * reserve : 0)
          .ToArray();
        double[] percent = Enumerable.Range(0, 3)
          .Select(f => Required(design[f] / reserve, f == 2 ? gasTolerance : liquidTolerance) / caps[f] * 100)
          .ToArray();
        percent[0] = Required((design[0] + design[1]) / reserve, liquidTolerance) / caps[0] * 100;

        double maximum = percent.Max();
        string limiting = maximum == 0
          ? "Нет нагрузки"
          : string.Join(" / ", Enumerable.Range(0, 3)
            .Where(f => Math.Abs(percent[f] - maximum) <= 1e-9)
            .Select(f => f == 0 ? "Общая жидкость (нефть + вода)" : FluidName(f))
            .Distinct());

        double totalVol = water + oilVol;
        rows.Add(new MonthLoad
        {
          Month = m + 1,
          LiquidDisplay = liquidRaw,
          OilDisplay = oilRaw,
          GasDisplay = gasRaw,
          OilVolumeM3Day = oilVol,
          WaterCutPercent = totalVol > 0 ? 100 * water / totalVol : null,
          Percent = percent,
          LimitingFluid = limiting
        });
      }

      return new Result
      {
        Months = rows,
        CombinedCapacity = combined,
        GasCapacity = gasCap,
        OilDensityKgM3 = density,
        DensityNote = densityNote
      };
    }

    private static double Required(double profileLoad, double tolerance)
    {
      if (!double.IsFinite(profileLoad) || profileLoad < 0)
      {
        return 0;
      }

      if (!double.IsFinite(tolerance) || tolerance < 0)
      {
        tolerance = 0;
      }

      return Math.Max(0, profileLoad - tolerance);
    }

    private static double OilVolume(double tonnesPerDay, double densityKgM3)
    {
      if (!double.IsFinite(tonnesPerDay) || tonnesPerDay < 0 || !double.IsFinite(densityKgM3) || densityKgM3 <= 0)
      {
        return 0;
      }

      return tonnesPerDay * 1000.0 / densityKgM3;
    }

    private static double At(double[] values, int index)
    {
      if (values == null || index < 0 || index >= values.Length)
      {
        return 0;
      }

      double v = values[index];
      return double.IsFinite(v) ? Math.Max(0, v) : 0;
    }

    private static double FiniteOr(double value, double fallback)
    {
      return double.IsFinite(value) && value > 0 ? value : fallback;
    }

    private static string FluidName(int index)
    {
      return index switch
      {
        1 => "Нефть",
        2 => "ПНГ",
        _ => "Жидкость"
      };
    }
  }
}
