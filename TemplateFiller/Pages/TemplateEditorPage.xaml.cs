// Pages/TemplateEditorPage.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TemplateFiller.Models;
using TemplateFiller.Services;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;

namespace TemplateFiller.Pages;

public sealed partial class TemplateEditorPage : Page
{
    private readonly TplService _tplService = new();
    private readonly ExcelService _excelService = new();
    private readonly PdfPreviewService _previewService = new();

    private string _tplPath = "";
    private string _extractedPdfPath = "";
    private TemplateConfig _config = new();
    private ExcelData? _excelData;

    // Zoom y tamaño de página PDF (en puntos)
    private double _currentScale = 1.5;
    private double _pdfPageWidth;
    private double _pdfPageHeight;

    // Arrastrar campos
    private Border? _dragging;
    private Point _dragStart;
    private double _dragOrigLeft;
    private double _dragOrigTop;

    // Control de propiedades
    private bool _updatingProps = false;

    private readonly ObservableCollection<FieldMapping> _placedFields = new();

    public TemplateEditorPage()
    {
        InitializeComponent();
        PlacedFieldsList.ItemsSource = _placedFields;
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _tplPath = (string)e.Parameter;
        _config = _tplService.ReadConfig(_tplPath);
        TemplateNameText.Text = _config.Name;

        _extractedPdfPath = await _tplService.ExtractPdfAsync(_tplPath);
        await ApplyZoomAsync();

        // Restaurar columnas guardadas para que el preview funcione sin recargar Excel
        if (_config.Columns.Count > 0)
        {
            ColumnsList.ItemsSource = _config.Columns;
            ColumnsList.Visibility = Visibility.Visible;
            NoExcelText.Visibility = Visibility.Collapsed;
            AddFieldButton.IsEnabled = true;
            ExcelFileNameText.Text = "Columnas guardadas en la plantilla";
        }

        foreach (var field in _config.Fields)
        {
            _placedFields.Add(field);
            AddFieldOverlay(field);
        }
    }

    // ─── Zoom ──────────────────────────────────────────────────────────────────

    private async void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        _currentScale = Math.Min(3.0, Math.Round(_currentScale + 0.25, 2));
        await ApplyZoomAsync();
    }

    private async void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        _currentScale = Math.Max(0.25, Math.Round(_currentScale - 0.25, 2));
        await ApplyZoomAsync();
    }

    private async void ZoomFitButton_Click(object sender, RoutedEventArgs e)
    {
        var available = PdfEditorContainer.ActualWidth - 40;
        if (_pdfPageWidth > 0 && available > 0)
            _currentScale = Math.Round(Math.Max(0.25, Math.Min(3.0, available / _pdfPageWidth)), 2);
        else
            _currentScale = 1.0;
        await ApplyZoomAsync();
    }

    private async void Zoom100Button_Click(object sender, RoutedEventArgs e)
    {
        _currentScale = 1.0;
        await ApplyZoomAsync();
    }

    private async Task ApplyZoomAsync()
    {
        if (string.IsNullOrEmpty(_extractedPdfPath)) return;

        var pageSize = await _previewService.GetPageSizeAsync(_extractedPdfPath, 0);
        _pdfPageWidth = pageSize.Width;
        _pdfPageHeight = pageSize.Height;

        var w = _pdfPageWidth * _currentScale;
        var h = _pdfPageHeight * _currentScale;

        PdfCanvasContainer.Width = w;
        PdfCanvasContainer.Height = h;
        FieldsCanvas.Width = w;
        FieldsCanvas.Height = h;

        var bitmap = await _previewService.RenderPageAsync(_extractedPdfPath, 0, _currentScale);
        PdfImage.Source = bitmap;

        // Reposicionar todos los overlays existentes
        foreach (var child in FieldsCanvas.Children.OfType<Border>())
        {
            if (child.Tag is not string id) continue;
            var field = _config.Fields.FirstOrDefault(f => f.Id == id);
            if (field == null) continue;

            var (sx, sy) = PdfGeneratorService.PdfToScreen(field.X, field.Y, w, h, _pdfPageWidth, _pdfPageHeight);
            Canvas.SetLeft(child, sx);
            Canvas.SetTop(child, sy);

            if (child.Child is TextBlock tb)
                tb.FontSize = field.FontSize * _currentScale;
        }

        ZoomLevelText.Text = $"{(int)(_currentScale * 100)}%";
        ZoomOutButton.IsEnabled = _currentScale > 0.25;
        ZoomInButton.IsEnabled = _currentScale < 3.0;
    }

    // ─── Cargar Excel ──────────────────────────────────────────────────────────

    private async void LoadExcelButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".xlsx");
        picker.FileTypeFilter.Add(".xls");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        _excelData = _excelService.Load(file.Path);

        // Persistir columnas y filas de muestra en el config
        _config.Columns = new System.Collections.Generic.List<string>(_excelData.Columns);
        _config.SampleRows = _excelData.Rows
            .Take(3)
            .Select(r => new System.Collections.Generic.List<string>(r))
            .ToList();
        try { await _tplService.SaveConfigAsync(_tplPath, _config); } catch { }

        ExcelFileNameText.Text = $"📄 {System.IO.Path.GetFileName(file.Path)}";
        ColumnsList.ItemsSource = _excelData.Columns;
        ColumnsList.Visibility = Visibility.Visible;
        NoExcelText.Visibility = Visibility.Collapsed;
        AddFieldButton.IsEnabled = true;

        StatusText.Text = $"Excel cargado: {_excelData.Columns.Count} columnas, {_excelData.Rows.Count} filas";
        if (_excelData.Rows.Count > 0)
            PreviewRowText.Text = "Preview: fila 1";

        RefreshFieldPreviews();
    }

    // ─── Añadir campo al Canvas ────────────────────────────────────────────────

    private void AddFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (ColumnsList.SelectedIndex < 0 || _excelData == null) return;

        var colIdx = ColumnsList.SelectedIndex;
        var colName = _excelData.Columns[colIdx];

        var screenX = FieldsCanvas.Width / 2 - 60;
        var screenY = FieldsCanvas.Height / 2;

        var (pdfX, pdfY) = PdfGeneratorService.ScreenToPdf(
            screenX, screenY,
            FieldsCanvas.Width, FieldsCanvas.Height,
            _pdfPageWidth, _pdfPageHeight);

        var field = new FieldMapping
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            ColumnIndex = colIdx,
            ColumnName = colName,
            X = pdfX,
            Y = pdfY,
            Page = 0,
            FontSize = 12f,
            FontColor = "#000000",
        };

        _config.Fields.Add(field);
        _placedFields.Add(field);
        AddFieldOverlay(field);

        // Seleccionar el campo recién añadido
        PlacedFieldsList.SelectedItem = field;
    }

    private void AddFieldOverlay(FieldMapping field)
    {
        var (screenX, screenY) = PdfGeneratorService.PdfToScreen(
            field.X, field.Y,
            FieldsCanvas.Width, FieldsCanvas.Height,
            _pdfPageWidth, _pdfPageHeight);

        var label = new TextBlock
        {
            Text = GetPreviewText(field.ColumnIndex),
            FontSize = field.FontSize * _currentScale,
            Foreground = new SolidColorBrush(Colors.DodgerBlue),
            Tag = field.Id,
        };

        var border = new Border
        {
            Child = label,
            Background = new SolidColorBrush(Color.FromArgb(60, 30, 144, 255)),
            BorderBrush = new SolidColorBrush(Colors.DodgerBlue),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Tag = field.Id,
        };

        Canvas.SetLeft(border, screenX);
        Canvas.SetTop(border, screenY);

        border.PointerPressed += FieldBorder_PointerPressed;
        border.PointerMoved += FieldBorder_PointerMoved;
        border.PointerReleased += FieldBorder_PointerReleased;

        FieldsCanvas.Children.Add(border);
    }

    private string GetPreviewText(int colIdx)
    {
        // 1. Datos en vivo del Excel cargado
        if (_excelData != null && _excelData.Rows.Count > 0)
            return _excelData.GetValue(0, colIdx);
        if (_excelData != null && colIdx < _excelData.Columns.Count)
            return $"[{_excelData.Columns[colIdx]}]";

        // 2. Datos de muestra guardados en la plantilla
        if (_config.SampleRows.Count > 0 && colIdx < (_config.SampleRows[0]?.Count ?? 0))
            return _config.SampleRows[0][colIdx];
        if (colIdx < _config.Columns.Count)
            return $"[{_config.Columns[colIdx]}]";

        // 3. Sin datos
        return "?";
    }

    private void RefreshFieldPreviews()
    {
        foreach (var child in FieldsCanvas.Children.OfType<Border>())
        {
            if (child.Tag is string id && child.Child is TextBlock tb)
            {
                var field = _config.Fields.FirstOrDefault(f => f.Id == id);
                if (field != null)
                    tb.Text = GetPreviewText(field.ColumnIndex);
            }
        }
    }

    // ─── Selección y resaltado ─────────────────────────────────────────────────

    private void HighlightFieldOverlay(string? selectedId)
    {
        foreach (var child in FieldsCanvas.Children.OfType<Border>())
        {
            var isSelected = selectedId != null && child.Tag is string id && id == selectedId;
            child.BorderBrush = new SolidColorBrush(isSelected ? Colors.OrangeRed : Colors.DodgerBlue);
            child.BorderThickness = new Thickness(isSelected ? 2.5 : 1);
            child.Background = new SolidColorBrush(isSelected
                ? Color.FromArgb(70, 255, 80, 0)
                : Color.FromArgb(60, 30, 144, 255));
        }
    }

    // ─── Drag & drop de campos ─────────────────────────────────────────────────

    private void FieldBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = (Border)sender;

        // Seleccionar en la lista al hacer clic en el overlay
        if (_dragging.Tag is string id)
        {
            var field = _placedFields.FirstOrDefault(f => f.Id == id);
            if (field != null) PlacedFieldsList.SelectedItem = field;
        }

        _dragStart = e.GetCurrentPoint(FieldsCanvas).Position;
        _dragOrigLeft = Canvas.GetLeft(_dragging);
        _dragOrigTop = Canvas.GetTop(_dragging);
        _dragging.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void FieldBorder_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging == null) return;
        var pos = e.GetCurrentPoint(FieldsCanvas).Position;
        Canvas.SetLeft(_dragging, Math.Max(0, _dragOrigLeft + pos.X - _dragStart.X));
        Canvas.SetTop(_dragging, Math.Max(0, _dragOrigTop + pos.Y - _dragStart.Y));
        e.Handled = true;
    }

    private void FieldBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging == null) return;

        if (_dragging.Tag is string id)
        {
            var field = _config.Fields.FirstOrDefault(f => f.Id == id);
            if (field != null)
            {
                var (pdfX, pdfY) = PdfGeneratorService.ScreenToPdf(
                    Canvas.GetLeft(_dragging), Canvas.GetTop(_dragging),
                    FieldsCanvas.Width, FieldsCanvas.Height,
                    _pdfPageWidth, _pdfPageHeight);
                field.X = pdfX;
                field.Y = pdfY;
            }
        }

        _dragging.ReleasePointerCapture(e.Pointer);
        _dragging = null;
        e.Handled = true;
    }

    private void FieldsCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PlacedFieldsList.SelectedIndex = -1;
    }

    // ─── Lista de campos colocados ─────────────────────────────────────────────

    private void PlacedFieldsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = PlacedFieldsList.SelectedItem as FieldMapping;
        RemoveFieldButton.IsEnabled = selected != null;

        HighlightFieldOverlay(selected?.Id);

        if (selected != null)
        {
            FieldPropsPanel.Visibility = Visibility.Visible;
            _updatingProps = true;
            FontSizeBox.Value = selected.FontSize;
            _updatingProps = false;
        }
        else
        {
            FieldPropsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RemoveFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlacedFieldsList.SelectedItem is not FieldMapping field) return;

        var overlay = FieldsCanvas.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag is string id && id == field.Id);
        if (overlay != null) FieldsCanvas.Children.Remove(overlay);

        _config.Fields.Remove(field);
        _placedFields.Remove(field);
    }

    // ─── Propiedades del campo ─────────────────────────────────────────────────

    private void FontSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updatingProps || double.IsNaN(args.NewValue)) return;
        if (PlacedFieldsList.SelectedItem is not FieldMapping field) return;

        field.FontSize = (float)args.NewValue;

        var overlay = FieldsCanvas.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag is string id && id == field.Id);
        if (overlay?.Child is TextBlock tb)
            tb.FontSize = field.FontSize * _currentScale;
    }

    // ─── Guardar ───────────────────────────────────────────────────────────────

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            await _tplService.SaveConfigAsync(_tplPath, _config);
            StatusText.Text = "✅ Guardado correctamente";
        }
        catch (Exception ex)
        {
            StatusText.Text = "❌ Error al guardar";
            var dlg = new ContentDialog
            {
                Title = "Error al guardar",
                Content = ex.Message,
                CloseButtonText = "Cerrar",
                XamlRoot = XamlRoot,
            };
            await dlg.ShowAsync();
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();
}
