using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhotoApp.ViewModels;

namespace PhotoApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void ChooseFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        if (!topLevel.StorageProvider.CanPickFolder)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choisir un dossier"
        });

        if (folders.Count == 0)
        {
            return;
        }

        var selectedFolder = folders[0];
        var selectedPath = selectedFolder.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetSelectedFolderAndScan(selectedPath);
        }
    }
}