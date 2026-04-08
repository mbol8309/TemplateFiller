// Pages/BatchGeneratePage.xaml.cs
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TemplateFiller.Services;
using Windows.Storage.Pickers;

namespace TemplateFiller.Pages;

public sealed partial class BatchGeneratePage : Page
{
    private readonly TplService _tplService = new();
    private readonly ExcelService _excelService = new();
    private readonly PdfGeneratorService _generatorService = new();

    private string _tplPath = "";
    private ExcelData? _excelData;
    private string _outputFolder = "";

    public BatchGeneratePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _tplPath = (string)e.Parameter;
        var config = _tplService.ReadConfig(_tplPath);
        TemplateNameText.Text = config.Name;

        // Carpeta de salida por defecto: Documentos/TemplateFiller/<nombre>
        _outputFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TemplateFiller",
            config.Name);
        OutputFolderBox.Text = _outputFolder;

        UpdateGenerateButton();
    }

    // ─── Examinar Excel ────────────────────────────────────────────────────────

    private async void BrowseExcelButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xls");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        ExcelPathBox.Text = file.Path;
        _excelData = _excelService.Load(file.Path);
        ExcelInfoText.Text = $"{_excelData.Columns.Count} columnas · {_excelData.Rows.Count} filas encontradas";
        UpdateGenerateButton();
    }

    // ─── Examinar carpeta de salida ────────────────────────────────────────────

    private async void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        _outputFolder = folder.Path;
        OutputFolderBox.Text = _outputFolder;
        UpdateGenerateButton();
    }

    // ─── Generar lote ──────────────────────────────────────────────────────────

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_excelData == null || string.IsNullOrEmpty(_outputFolder)) return;

        GenerateButton.IsEnabled = false;
        GenerateProgress.Visibility = Visibility.Visible;
        GenerateProgress.Maximum = _excelData.Rows.Count;
        GenerateProgress.Value = 0;

        var config = _tplService.ReadConfig(_tplPath);
        var pdfPath = await _tplService.ExtractPdfAsync(_tplPath);
        var pattern = string.IsNullOrWhiteSpace(FilePatternBox.Text) ? "documento_{row}" : FilePatternBox.Text.Trim();

        ProgressText.Text = "Generando PDFs...";

        try
        {
            var excelData = _excelData;
            var outputFolder = _outputFolder;
            int generated = 0;

            await Task.Run(() =>
            {
                Directory.CreateDirectory(outputFolder);
                for (int i = 0; i < excelData.Rows.Count; i++)
                {
                    var row = excelData.Rows[i];
                    var fileName = pattern.Replace("{row}", (i + 1).ToString()) + ".pdf";
                    var outputPath = Path.Combine(outputFolder, fileName);
                    _generatorService.GenerateSingle(pdfPath, config, row, outputPath);
                    generated++;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        GenerateProgress.Value = generated;
                        ProgressText.Text = $"Generando... {generated}/{excelData.Rows.Count}";
                    });
                }
            });

            ProgressText.Text = $"✅ {generated} PDFs generados en:\n{_outputFolder}";

            // Ofrecer abrir la carpeta
            var dialog = new ContentDialog
            {
                Title = "¡Generación completada!",
                Content = $"{generated} PDFs generados correctamente.",
                PrimaryButtonText = "Abrir carpeta",
                CloseButtonText = "Cerrar",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                System.Diagnostics.Process.Start("explorer.exe", _outputFolder);
        }
        catch (Exception ex)
        {
            ProgressText.Text = $"❌ Error: {ex.Message}";
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void UpdateGenerateButton()
    {
        GenerateButton.IsEnabled = _excelData != null && !string.IsNullOrEmpty(_outputFolder);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();
}
