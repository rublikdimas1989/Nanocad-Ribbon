using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
namespace NcTestRibbon.Rates;
public sealed record PipeCatalogRow(int Row,int Diameter,double Wall,string Steel,string Type,double Casing);
/// <summary>Общая номенклатура окна исходных данных и топологии. AF принадлежит выбранной строке.</summary>
public static class PipeCatalog
{
    public static List<PipeCatalogRow> Read(ExcelWorksheet sheet,Action<string>? skipped=null)
    {
        if(sheet==null)throw new InvalidDataException("Нет листа ТрубаЛч.");
        var rows=new List<PipeCatalogRow>();
        bool Number(int row,int col,out double n)=>double.TryParse(Convert.ToString(sheet.Cells[row,col].Value,CultureInfo.InvariantCulture)?.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&double.IsFinite(n);
        for(int r=2;r<=(sheet.Dimension?.End.Row??0);r++)
        {
            if(!Number(r,3,out double d)||!Number(r,4,out double t)||d!=Math.Round(d)||d<=0||t<=0||2*t>=d)continue;
            if(!Number(r,32,out double casing)||casing<0){skipped?.Invoke($"{sheet.Name}!AF{r}: труба {d}×{t} исключена — некорректный диаметр футляра.");continue;}
            rows.Add(new(r,(int)d,t,sheet.Cells[r,5].Text.Trim(),sheet.Cells[r,6].Text.Trim(),casing));
        }
        return rows;
    }
}
