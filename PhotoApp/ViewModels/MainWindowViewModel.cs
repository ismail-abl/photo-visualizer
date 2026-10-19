using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Models;
using PhotoApp.Operations;

namespace PhotoApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string OutputFolderName = "Converted";
    private const int JpegQuality = 90;

    private readonly MediaAnalyzer _mediaAnalyzer = new();
    private readonly ConversionService _conversionService = new();
    private readonly AppDataPaths _dataPaths = AppDataPaths.ForCurrentUser();

    [ObservableProperty]
    private string? _selectedFolderPath;

    [ObservableProperty]
    private int _imageCount;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isConverting;

    [ObservableProperty]
    private bool _deleteOriginalAfterSuccess;

    [ObservableProperty]
    private string _conversionStatus = string.Empty;

    public MainWindowViewModel()
    {
        Images = new ObservableCollection<ImageFileInfo>();
    }

    public ObservableCollection<ImageFileInfo> Images { get; }

    public bool HasNoImages => ImageCount == 0;

    public bool CanConvertNow => !IsScanning && !IsConverting && Images.Count > 0 && !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public async Task SetSelectedFolderAndScanAsync(string selectedFolderPath)
    {
        SelectedFolderPath = selectedFolderPath;
        await ScanSelectedFolderAsync();
    }

    private async Task ScanSelectedFolderAsync()
    {
        var root = SelectedFolderPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        IsScanning = true;
        ConversionStatus = string.Empty;
        try
        {
            // Scan et lecture des métadonnées hors du thread de l'interface.
            var resolved = await Task.Run(() => ScanAndResolve(root));

            Images.Clear();
            foreach (var image in resolved)
            {
                Images.Add(image);
            }

            ImageCount = Images.Count;
        }
        catch (Exception ex)
        {
            ConversionStatus = $"Erreur pendant l'analyse : {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private List<ImageFileInfo> ScanAndResolve(string root)
    {
        var outputFolder = Path.Combine(root, OutputFolderName);

        // Le moteur voit tout le dossier (voisins, Live Photo) ; l'interface ne traite que les photos en phase 1.
        return _mediaAnalyzer.Analyze([root], [outputFolder]).Results
            .Where(result => result.Kind == MediaKind.Photo)
            .Select(ImageFileInfo.FromResult)
            .OrderBy(image => image.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    partial void OnSelectedFolderPathChanged(string? value) => RefreshCommandState();

    partial void OnIsScanningChanged(bool value) => RefreshCommandState();

    partial void OnIsConvertingChanged(bool value) => RefreshCommandState();

    partial void OnImageCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasNoImages));
        RefreshCommandState();
    }

    private void RefreshCommandState()
    {
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConvertNow))]
    private async Task ConvertAsync()
    {
        if (Images.Count == 0 || string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            return;
        }

        var outputFolder = Path.Combine(SelectedFolderPath, OutputFolderName);
        var manifestPath = _dataPaths.ManifestPathFor(outputFolder);
        var snapshot = Images.ToList();
        var deleteOriginals = DeleteOriginalAfterSuccess;

        IsConverting = true;
        ConversionStatus = "Conversion en cours...";

        try
        {
            var summary = await Task.Run(() =>
                _conversionService.Run(snapshot, outputFolder, manifestPath, JpegQuality, deleteOriginals));
            ConversionStatus = summary.ToDisplayText();
        }
        catch (Exception ex)
        {
            ConversionStatus = $"Erreur critique: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
        }
    }
}
