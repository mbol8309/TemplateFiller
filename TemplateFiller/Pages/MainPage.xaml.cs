// Pages/MainPage.xaml.cs
using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TemplateFiller.Models;
using TemplateFiller.Services;
using Windows.Storage.Pickers;

namespace TemplateFiller.Pages;

public sealed partial class MainPage : Page
{
    private readonly TplService _tplService = new();

    public MainPage()
    {
        InitializeComponent();
        LoadTemplates();
    }

    private void LoadTemplates()
    {
        TemplatesPanel.Children.Clear();
        var templates = _tplService.GetAllTemplates();

        if (templates.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No hay plantillas. Crea una con el botón '+ Nueva plantilla'.",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 48, 0, 0),
            };
            TemplatesPanel.Children.Add(empty);
            return;
        }

        foreach (var template in templates)
            TemplatesPanel.Children.Add(CreateTemplateCard(template));
    }

    private Border CreateTemplateCard(TemplateInfo template)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Info
        var info = new StackPanel { Spacing = 4 };
        info.Children.Add(new TextBlock
        {
            Text = template.Name,
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });
        info.Children.Add(new TextBlock
        {
            Text = $"{template.FieldCount} campos configurados · {System.IO.Path.GetFileName(template.FilePath)}",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        // Botones
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        var editBtn = new Button { Content = "Editar" };
        editBtn.Click += (_, _) => NavigateToEditor(template.FilePath);
        buttons.Children.Add(editBtn);

        var generateBtn = new Button { Content = "Generar lote" };
        generateBtn.Click += async (_, _) => await GenerateBatchAsync(template.FilePath);
        buttons.Children.Add(generateBtn);

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            //ToolTipService = { ToolTip = "Eliminar" },
        };
        deleteBtn.Click += (_, _) => DeleteTemplate(template.FilePath);
        buttons.Children.Add(deleteBtn);

        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        card.Child = grid;
        return card;
    }

    private async void AddTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        // Diálogo para nombre
        var dialog = new ContentDialog
        {
            Title = "Nueva plantilla",
            PrimaryButtonText = "Crear",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var nameBox = new TextBox { PlaceholderText = "Nombre de la plantilla", Margin = new Thickness(0, 8, 0, 0) };
        dialog.Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Nombre" },
                nameBox,
                new TextBlock { Text = "A continuación selecciona el PDF base", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] },
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text)) return;

        // Picker de PDF
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".pdf");
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        await _tplService.CreateAsync(nameBox.Text.Trim(), file.Path);
        LoadTemplates();
    }

    private void NavigateToEditor(string tplPath)
    {
        Frame.Navigate(typeof(TemplateEditorPage), tplPath);
    }

    private async System.Threading.Tasks.Task GenerateBatchAsync(string tplPath)
    {
        Frame.Navigate(typeof(BatchGeneratePage), tplPath);
    }

    private async void DeleteTemplate(string tplPath)
    {
        var dialog = new ContentDialog
        {
            Title = "Eliminar plantilla",
            Content = "¿Seguro que quieres eliminar esta plantilla? No se puede deshacer.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _tplService.Delete(tplPath);
            LoadTemplates();
        }
    }
}
