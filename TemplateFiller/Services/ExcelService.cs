// Services/ExcelService.cs
using System.Collections.Generic;
using ClosedXML.Excel;

namespace TemplateFiller.Services;

public class ExcelService
{
    public ExcelData Load(string excelPath)
    {
        using var workbook = new XLWorkbook(excelPath);
        var sheet = workbook.Worksheet(1);
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        var columns = new List<string>();
        for (int c = 1; c <= lastCol; c++)
        {
            var header = sheet.Cell(1, c).GetString().Trim();
            columns.Add(string.IsNullOrEmpty(header) ? $"Columna {c}" : header);
        }

        var rows = new List<List<string>>();
        for (int r = 2; r <= lastRow; r++)
        {
            var row = new List<string>();
            for (int c = 1; c <= lastCol; c++)
                row.Add(sheet.Cell(r, c).GetString());
            rows.Add(row);
        }

        return new ExcelData { Columns = columns, Rows = rows };
    }
}

public class ExcelData
{
    public List<string> Columns { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();

    public string GetValue(int rowIndex, int colIndex)
    {
        if (rowIndex >= Rows.Count || colIndex >= Columns.Count) return "";
        return Rows[rowIndex][colIndex];
    }
}
