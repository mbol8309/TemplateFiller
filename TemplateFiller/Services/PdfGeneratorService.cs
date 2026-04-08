// Services/PdfGeneratorService.cs
using System;
using System.Collections.Generic;
using System.IO;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using TemplateFiller.Models;

namespace TemplateFiller.Services;

public class PdfGeneratorService
{
    /// <summary>
    /// Genera un PDF por cada fila del Excel, insertando los campos mapeados.
    /// </summary>
    public int GenerateBatch(
        string templatePdfPath,
        TemplateConfig config,
        ExcelData excelData,
        string outputFolder,
        string fileNamePattern = "output_{row}")
    {
        Directory.CreateDirectory(outputFolder);
        int generated = 0;

        for (int rowIdx = 0; rowIdx < excelData.Rows.Count; rowIdx++)
        {
            var row = excelData.Rows[rowIdx];
            var outputPath = Path.Combine(outputFolder,
                fileNamePattern.Replace("{row}", (rowIdx + 1).ToString()) + ".pdf");

            try
            {
                GenerateSingle(templatePdfPath, config, row, outputPath);
                generated++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fila {rowIdx}: {ex.Message}");
            }
        }

        return generated;
    }

    public void GenerateSingle(string templatePdfPath, TemplateConfig config, List<string> rowValues, string outputPath)
    {
        using var reader = new PdfReader(templatePdfPath);
        using var writer = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(reader, writer);

        var font = PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA);

        foreach (var field in config.Fields)
        {
            if (field.ColumnIndex >= rowValues.Count) continue;
            var text = rowValues[field.ColumnIndex];
            if (string.IsNullOrEmpty(text)) continue;

            var pageNum = field.Page + 1; // iText es 1-based
            if (pageNum > pdfDoc.GetNumberOfPages()) continue;

            var page = pdfDoc.GetPage(pageNum);
            var canvas = new PdfCanvas(page);

            // Convertir color hex → iText Color
            var color = HexToColor(field.FontColor);

            canvas.BeginText()
                  .SetFontAndSize(font, field.FontSize)
                  .SetColor(color, true)
                  .MoveText(field.X, field.Y)
                  .ShowText(text)
                  .EndText();
        }
    }

    /// <summary>
    /// Convierte coordenadas de pantalla (Canvas WinUI) a coordenadas PDF (iText, origen abajo-izquierda).
    /// </summary>
    public static (double pdfX, double pdfY) ScreenToPdf(
        double screenX, double screenY,
        double canvasWidth, double canvasHeight,
        double pdfPageWidth, double pdfPageHeight)
    {
        double scaleX = pdfPageWidth / canvasWidth;
        double scaleY = pdfPageHeight / canvasHeight;
        double pdfX = screenX * scaleX;
        // En PDF Y=0 es abajo, en pantalla Y=0 es arriba → invertir
        double pdfY = pdfPageHeight - (screenY * scaleY);
        return (pdfX, pdfY);
    }

    /// <summary>
    /// Convierte coordenadas PDF a coordenadas de pantalla.
    /// </summary>
    public static (double screenX, double screenY) PdfToScreen(
        double pdfX, double pdfY,
        double canvasWidth, double canvasHeight,
        double pdfPageWidth, double pdfPageHeight)
    {
        double scaleX = canvasWidth / pdfPageWidth;
        double scaleY = canvasHeight / pdfPageHeight;
        double screenX = pdfX * scaleX;
        double screenY = (pdfPageHeight - pdfY) * scaleY;
        return (screenX, screenY);
    }

    private static DeviceRgb HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return new DeviceRgb(0, 0, 0);
        int r = Convert.ToInt32(hex[..2], 16);
        int g = Convert.ToInt32(hex[2..4], 16);
        int b = Convert.ToInt32(hex[4..6], 16);
        return new DeviceRgb(r, g, b);
    }
}
