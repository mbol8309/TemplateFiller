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

    // Escala del preview (pantalla / pdf)
    private double _scaleX = 1.0;
    private double _scaleY = 1.0;
    private double _pdfPageWidth;
    private double _pdfPageHeight;

    // Arrastrar campos
    private Border? _dragging;
    private Point _dragStart;
    private double _dragOrigLeft;
    private double _dragOrigTop;

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
        await RenderPdfPreviewAsync();

        // Restaurar campos guardados
        foreach (var field in _config.Fields)
        {
            _placedFields.Add(field);
            AddFieldOverlay(field);
        }
    }

    private async Task RenderPdfPreviewAsync()
    {
        var pageSize = await _previewService.GetPageSizeAsync(_extractedPdfPath, 0);
        _pdfPageWidth = pageSize.Width;
        _pdfPageHeight = pageSize.Height;

        const double scale = 1.5;
        _scaleX = scale;
        _scaleY = scale;

        PdfCanvasContainer.Width = _pdfPageWidth * scale;
        PdfCanvasContainer.Height = _pdfPageHeight * scale;
        FieldsCanvas.Width = _pdfPageWidth * scale;
        FieldsCanvas.Height = _pdfPageHeight * scale;

        var bitmap = await _previewService.RenderPageAsync(_extractedPdfPath, 0, scale);
        PdfImage.Source = bitmap;
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

        ColumnsList.ItemsSource = _excelData.Columns;
        ColumnsList.Visibility = Visibility.Visible;
        NoExcelText.Visibility = Visibility.Collapsed;
        AddFieldButton.IsEnabled = true;

        StatusText.Text = $"Excel cargado: {_excelData.Columns.Count} columnas, {_excelData.Rows.Count} filas";
        if (_excelData.Rows.Count > 0)
            PreviewRowText.Text = "Preview: fila 1";

        // Actualizar labels de preview en los campos existentes
        RefreshFieldPreviews();
    }

    // ─── Añadir campo al Canvas ────────────────────────────────────────────────

    private void AddFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (ColumnsList.SelectedIndex < 0 || _excelData == null) return;

        var colIdx = ColumnsList.SelectedIndex;
        var colName = _excelData.Columns[colIdx];

        // Posición inicial: centro del canvas
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
    }

    private void AddFieldOverlay(FieldMapping field)
    {
        var (screenX, screenY) = PdfGeneratorService.PdfToScreen(
            field.X, field.Y,
            FieldsCanvas.Width, FieldsCanvas.Height,
            _pdfPageWidth, _pdfPageHeight);

        var previewText = GetPreviewText(field.ColumnIndex);

        var label = new TextBlock
        {
            Text = previewText,
            FontSize = field.FontSize * _scaleX,
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
            Cursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll),
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
        if (_excelData == null || _excelData.Rows.Count == 0)
            return $"{{{_excelData?.Columns.ElementAtOrDefault(colIdx) ?? "?"}}}";
        return _excelData.GetValue(0, colIdx);
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

    // ─── Drag & drop de campos ─────────────────────────────────────────────────

    private void FieldBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = (Border)sender;
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
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        Canvas.SetLeft(_dragging, Math.Max(0, _dragOrigLeft + dx));
        Canvas.SetTop(_dragging, Math.Max(0, _dragOrigTop + dy));
        e.Handled = true;
    }

    private void FieldBorder_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging == null) return;

        var screenX = Canvas.GetLeft(_dragging);
        var screenY = Canvas.GetTop(_dragging);

        if (_dragging.Tag is string id)
        {
            var field = _config.Fields.FirstOrDefault(f => f.Id == id);
            if (field != null)
            {
                var (pdfX, pdfY) = PdfGeneratorService.ScreenToPdf(
                    screenX, screenY,
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
        // Click en zona vacía → deseleccionar
        PlacedFieldsList.SelectedIndex = -1;
    }

    // ─── Lista de campos colocados ─────────────────────────────────────────────

    private void PlacedFieldsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveFieldButton.IsEnabled = PlacedFieldsList.SelectedIndex >= 0;
    }

    private void RemoveFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (PlacedFieldsList.SelectedItem is not FieldMapping field) return;

        // Quitar del canvas
        var overlay = FieldsCanvas.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag is string id && id == field.Id);
        if (overlay != null) FieldsCanvas.Children.Remove(overlay);

        _config.Fields.Remove(field);
        _placedFields.Remove(field);
    }

    // ─── Guardar ───────────────────────────────────────────────────────────────

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        await _tplService.SaveConfigAsync(_tplPath, _config);
        SaveButton.IsEnabled = true;
        StatusText.Text = "✅ Guardado correctamente";
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => Frame.GoBack();
}
