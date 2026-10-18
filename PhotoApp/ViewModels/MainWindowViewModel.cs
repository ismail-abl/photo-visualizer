using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoApp.Catalog;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;
using PhotoApp.Metadata;
using PhotoApp.Operations;

namespace PhotoApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ImageScanner _imageScanner = new();
    private readonly ImageDateResolver _imageDateResolver = new();

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

    private void ScanSelectedFolder()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            return;
        }

        IsScanning = true;
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
        ConversionStatus = string.Empty;
        try
        {
            var scannedImages = _imageScanner.Scan(SelectedFolderPath);
            var sortedImages = new List<ImageFileInfo>(scannedImages);
            sortedImages.Sort(static (left, right) =>
                string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));

            Images.Clear();
            for (var i = 0; i < sortedImages.Count; i++)
            {
                var image = sortedImages[i];
                var metadata = _imageDateResolver.Resolve(image.FullPath);

                Images.Add(new ImageFileInfo(
                    image.FullPath,
                    image.FileName,
                    image.Extension,
                    image.FileSizeBytes,
                    metadata));
            }

            ImageCount = Images.Count;
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanConvertNow));
            ConvertCommand.NotifyCanExecuteChanged();
        }
    }

    public void SetSelectedFolderAndScan(string selectedFolderPath)
    {
        SelectedFolderPath = selectedFolderPath;
        ScanSelectedFolder();
    }

    partial void OnSelectedFolderPathChanged(string? value)
    {
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    partial void OnImageCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasNoImages));
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConvertingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConvertNow))]
    private async System.Threading.Tasks.Task ConvertAsync()
    {
        if (Images.Count == 0 || string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            return;
        }

        IsConverting = true;
        ConversionStatus = "Conversion en cours...";

        try
        {
            var imageData = new List<(string sourcePath, ImageDateCandidate date, string extension)>();
            foreach (var img in Images)
            {
                if (img.DateMetadata?.RetainedCandidate is { } date)
                {
                    imageData.Add((img.FullPath, date, img.Extension));
                }
            }

            if (imageData.Count == 0)
            {
                ConversionStatus = "Aucune image avec date valide à convertir.";
                return;
            }

            string outputFolder = System.IO.Path.Combine(SelectedFolderPath, "Converted");
            
            var planner = new PhotoApp.Core.Services.RenamePlanner();
            var plans = planner.PlanRenames(imageData, outputFolder);

            var executor = new BatchRenameExecutor();
            
            // Execute in background
            var results = await System.Threading.Tasks.Task.Run(() => 
                executor.Execute(plans, outputFolder, 90, DeleteOriginalAfterSuccess));

            int successCount = 0;
            int failCount = 0;
            string? firstError = null;

            foreach (var r in results)
            {
                if (r.Success) successCount++;
                else
                {
                    failCount++;
                    firstError ??= r.ErrorMessage;
                }
            }

            if (failCount == 0)
            {
                ConversionStatus = $"Succès : {successCount} fichier(s) converti(s).";
            }
            else
            {
                ConversionStatus = $"Terminé avec erreurs. Succès: {successCount}, Échecs: {failCount}. Erreur: {firstError}";
            }
        }
        catch (System.Exception ex)
        {
            ConversionStatus = $"Erreur critique: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
        }
    }
}
