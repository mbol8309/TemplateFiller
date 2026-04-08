// MainWindow.xaml.cs
using Microsoft.UI.Xaml;
using TemplateFiller.Pages;

namespace TemplateFiller;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        this.ExtendsContentIntoTitleBar = true;
        RootFrame.Navigate(typeof(MainPage));
    }
}
