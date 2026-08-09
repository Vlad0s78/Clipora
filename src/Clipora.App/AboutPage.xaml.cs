using Clipora.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipora.App;

public sealed partial class AboutPage : Page
{
    public AboutPageViewModel ViewModel { get; }

    public AboutPage(AboutPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (App.Window is MainWindow window)
        {
            window.NavigateHome();
        }
    }
}
