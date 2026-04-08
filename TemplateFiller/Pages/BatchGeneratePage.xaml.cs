// Pages/BatchGeneratePage.xaml.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _tplPath = (string)e.Parameter;
        var config = _tplService.ReadConfig(_tplPath);
        TemplateNameText.Text = config.Name;

        _outputFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TemplateFiller",
            config.Name);
        OutputFolderBox.Text = _outputFolder;

        UpdateGenerateButton();
    }

    // ─── Excel ─────────────────────────────────────────────────────────────────

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
        ExcelInfoText.Text = $"{_excelData.Columns.Count} columnas · {_excelData.Rows.Count} filas";

        // Actualizar límites de los NumberBox de rango
        RangeEndBox.Maximum = _excelData.Rows.Count;
        RangeEndBox.Value = _excelData.Rows.Count;
        RangeStartBox.Maximum = _excelData.Rows.Count;
        RowCountBox.Maximum = _excelData.Rows.Count;

        // Construir chips de columnas para el nombre
        BuildColumnChips();
        UpdateRowSelectionInfo();
        UpdateFilenamePreview();
        UpdateGenerateButton();
    }

    private void BuildColumnChips()
    {
        if (_excelData == null) return;
        ColumnChipsContainer.Children.Clear();

        foreach (var col in _excelData.Columns)
        {
            var colName = col;
            var btn = new Button
            {
                Content = $"{{{colName}}}",
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12,
            };
            btn.Click += (_, _) =>
            {
                var pos = FilePatternBox.SelectionStart;
                var text = FilePatternBox.Text;
                FilePatternBox.Text = text.Insert(pos, $"{{{colName}}}");
                FilePatternBox.SelectionStart = pos + colName.Length + 2;
            };
            ColumnChipsContainer.Children.Add(btn);
        }

        ColumnChipsPanel.Visibility = Visibility.Visible;
    }

    // ─── Carpeta de salida ──────────────────────────────────────────────────────

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

    // ─── Selección de filas ─────────────────────────────────────────────────────

    private void RowMode_Changed(object sender, RoutedEventArgs e)
    {
        if (RangePanel == null) return; // Guard para inicialización XAML
        RangePanel.Visibility = RangeRowsRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CountPanel.Visibility = FirstNRowsRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateRowSelectionInfo();
        UpdateFilenamePreview();
        UpdateGenerateButton();
    }

    private void RowRange_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        UpdateRowSelectionInfo();
        UpdateGenerateButton();
    }

    private void UpdateRowSelectionInfo()
    {
        if (RowSelectionInfoText == null) return; // Guard para inicialización XAML
        if (_excelData == null) { RowSelectionInfoText.Text = ""; return; }

        var (start, end) = GetSelectedRowRange();
        var count = Math.Max(0, end - start + 1);

        if (AllRowsRadio.IsChecked == true)
            RowSelectionInfoText.Text = $"Se procesarán {count} filas";
        else if (RangeRowsRadio.IsChecked == true)
            RowSelectionInfoText.Text = $"Se procesarán {count} filas (fila {start + 1} a {end + 1})";
        else
            RowSelectionInfoText.Text = $"Se procesarán las primeras {count} filas";

        GenerateButtonText.Text = $"Generar {count} PDF{(count != 1 ? "s" : "")}";
    }

    /// <summary>Devuelve (startIndex, endIndex) basado en la selección del usuario (0-based).</summary>
    private (int start, int end) GetSelectedRowRange()
    {
        if (_excelData == null) return (0, -1);
        int total = _excelData.Rows.Count;

        if (RangeRowsRadio.IsChecked == true)
        {
            int s = Math.Max(0, (int)(RangeStartBox.Value) - 1);
            int en = Math.Min(total - 1, (int)(RangeEndBox.Value) - 1);
            return (s, en);
        }
        if (FirstNRowsRadio.IsChecked == true)
        {
            int n = Math.Min(total, (int)(RowCountBox.Value));
            return (0, n - 1);
        }
        return (0, total - 1); // Todas
    }

    // ─── Patrón de nombre ──────────────────────────────────────────────────────

    private void FilePatternBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFilenamePreview();
    }

    private void UpdateFilenamePreview()
    {
        if (_excelData == null || _excelData.Rows.Count == 0)
        {
            FileNamePreviewText.Text = "";
            return;
        }

        var pattern = string.IsNullOrWhiteSpace(FilePatternBox.Text) ? "documento_{row}" : FilePatternBox.Text;
        var (start, _) = GetSelectedRowRange();
        var example = BuildFileName(pattern, start + 1, _excelData.Rows[start]);
        FileNamePreviewText.Text = $"Ejemplo: {example}.pdf";
    }

    private string BuildFileName(string pattern, int rowNum, List<string> rowValues)
    {
        var name = pattern.Replace("{row}", rowNum.ToString());

        if (_excelData != null)
        {
            for (int i = 0; i < _excelData.Columns.Count; i++)
            {
                var val = i < rowValues.Count ? rowValues[i] : "";
                name = name.Replace($"{{{_excelData.Columns[i]}}}", val);
            }
        }

        // Sanitizar caracteres inválidos en nombre de archivo
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }

    // ─── Generar lote ──────────────────────────────────────────────────────────

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_excelData == null || string.IsNullOrEmpty(_outputFolder)) return;

        var (startIdx, endIdx) = GetSelectedRowRange();
        int total = endIdx - startIdx + 1;
        if (total <= 0) return;

        GenerateButton.IsEnabled = false;
        GenerateProgress.Visibility = Visibility.Visible;
        GenerateProgress.Maximum = total;
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

                for (int i = startIdx; i <= endIdx; i++)
                {
                    var row = excelData.Rows[i];
                    var fileName = BuildFileName(pattern, i + 1, row) + ".pdf";
                    var outputPath = Path.Combine(outputFolder, fileName);
                    _generatorService.GenerateSingle(pdfPath, config, row, outputPath);
                    generated++;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        GenerateProgress.Value = generated;
                        ProgressText.Text = $"Generando... {generated}/{total}";
                    });
                }
            });

            ProgressText.Text = $"✅ {generated} PDFs generados en:\n{_outputFolder}";

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
        if (GenerateButton == null) return;
        var (start, end) = _excelData != null ? GetSelectedRowRange() : (0, -1);
        GenerateButton.IsEnabled = _excelData != null
            && !string.IsNullOrEmpty(_outputFolder)
            && end >= start;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();
}


