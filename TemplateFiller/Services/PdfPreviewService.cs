// Services/PdfPreviewService.cs
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TemplateFiller.Services;

public class PdfPreviewService
{
    public async Task<BitmapImage?> RenderPageAsync(string pdfPath, uint pageIndex = 0, double scale = 1.5)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await PdfDocument.LoadFromFileAsync(file);

            if (pageIndex >= pdfDoc.PageCount) return null;

            using var page = pdfDoc.GetPage(pageIndex);
            var pageSize = page.Size;

            using var stream = new InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)(pageSize.Width * scale),
                DestinationHeight = (uint)(pageSize.Height * scale),
            };

            await page.RenderToStreamAsync(stream, options);

            var bitmap = new BitmapImage();
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PdfPreviewService error: {ex.Message}");
            return null;
        }
    }

    public async Task<uint> GetPageCountAsync(string pdfPath)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await PdfDocument.LoadFromFileAsync(file);
        return pdfDoc.PageCount;
    }

    public async Task<Windows.Foundation.Size> GetPageSizeAsync(string pdfPath, uint pageIndex = 0)
    {
        var file = await StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await PdfDocument.LoadFromFileAsync(file);
        using var page = pdfDoc.GetPage(pageIndex);
        return page.Size;
    }
}
