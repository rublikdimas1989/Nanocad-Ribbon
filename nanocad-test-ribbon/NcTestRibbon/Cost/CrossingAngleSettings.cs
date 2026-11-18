using System;
using System.Collections.Generic;
using System.Globalization;

namespace NcTestRibbon.Cost;

/// <summary>Single source of crossing-angle defaults and validation for search and reporting.</summary>
public static class CrossingAngleSettings
{
    public const float DefaultMin=60f,DefaultMax=90f;
    public static (float Min,float Max) Read(IReadOnlyDictionary<string,string> rates,string prefix)
    {
        float ReadValue(string suffix,float fallback)
        {
            if(rates==null||!rates.TryGetValue(prefix+suffix,out var text))return fallback;
            if(!float.TryParse(text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||!float.IsFinite(value))
                throw new ArgumentException($"{prefix}{suffix}: введите конечное число градусов.");
            return value;
        }
        float min=ReadValue("_МинУгол",DefaultMin),max=ReadValue("_МаксУгол",DefaultMax);
        if(min<0||max>90||min>max)throw new ArgumentException($"{prefix}: требуется 0 ≤ минимальный угол ≤ максимальный угол ≤ 90°.");
        return(min,max);
    }
}
