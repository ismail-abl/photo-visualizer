using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Operations;

namespace PhotoApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string OutputFolderName = "Converted";
    private const int JpegQuality = 90;

    private readonly MediaAnalyzer _mediaAnalyzer = new();
    private readonly ConversionService _conversionService = new();
    private readonly AppDataPaths _dataPaths = AppDataPaths.ForCurrentUser();

    // Tous les médias de la dernière analyse (photos et vidéos) : le renommage sur place s'applique aux deux.
    private IReadOnlyList<MediaResult> _results = [];
    private PreparedRename? _preparedRename;
    private long? _preparedUndoBatchId;
    private CancellationTokenSource? _renameCancellation;

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

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private bool _isRenameConfirmationPending;

    [ObservableProperty]
    private string _renameStatus = string.Empty;

    public MainWindowViewModel()
    {
        Images = new ObservableCollection<ImageFileInfo>();
    }

    public ObservableCollection<ImageFileInfo> Images { get; }

    public bool HasNoImages => ImageCount == 0;

    public bool CanConvertNow => !IsScanning && !IsConverting && !IsRenaming && !IsRenameConfirmationPending
                                 && Images.Count > 0 && !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public bool CanRenameNow => !IsScanning && !IsConverting && !IsRenaming && !IsRenameConfirmationPending
                                && _results.Count > 0 && !string.IsNullOrWhiteSpace(SelectedFolderPath);

    public bool CanUndoNow => !IsScanning && !IsConverting && !IsRenaming && !IsRenameConfirmationPending;

    // Pas de changement de dossier pendant une conversion, un renommage ou une confirmation en attente.
    public bool CanChooseFolder => !IsConverting && !IsRenaming && !IsRenameConfirmationPending;

    // Le bouton d'arrêt n'apparaît qu'une fois le lot lancé (pas pendant la préparation du plan).
    public bool CanStopBatch => IsRenaming && _renameCancellation is not null;

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
            var (resolved, results) = await Task.Run(() => ScanAndResolve(root));

            Images.Clear();
            foreach (var image in resolved)
            {
                Images.Add(image);
            }

            _results = results;
            ImageCount = Images.Count;
            RefreshCommandState();
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

    private (List<ImageFileInfo> Images, IReadOnlyList<MediaResult> Results) ScanAndResolve(string root)
    {
        var outputFolder = Path.Combine(root, OutputFolderName);

        // Le moteur voit tout le dossier (voisins, Live Photo) ; la liste et la conversion ne traitent que les photos,
        // le renommage sur place (spec de phase 2) s'applique à tous les résultats, vidéos comprises.
        var results = _mediaAnalyzer.Analyze([root], [outputFolder]).Results;
        var images = results
            .Where(result => result.Kind == MediaKind.Photo)
            .Select(ImageFileInfo.FromResult)
            .OrderBy(image => image.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (images, results);
    }

    partial void OnSelectedFolderPathChanged(string? value) => RefreshCommandState();

    partial void OnIsScanningChanged(bool value) => RefreshCommandState();

    partial void OnIsConvertingChanged(bool value) => RefreshCommandState();

    partial void OnIsRenamingChanged(bool value) => RefreshCommandState();

    partial void OnIsRenameConfirmationPendingChanged(bool value) => RefreshCommandState();

    partial void OnImageCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasNoImages));
        RefreshCommandState();
    }

    private void RefreshCommandState()
    {
        OnPropertyChanged(nameof(CanConvertNow));
        OnPropertyChanged(nameof(CanRenameNow));
        OnPropertyChanged(nameof(CanUndoNow));
        OnPropertyChanged(nameof(CanChooseFolder));
        ConvertCommand.NotifyCanExecuteChanged();
        PrepareRenameCommand.NotifyCanExecuteChanged();
        PrepareUndoCommand.NotifyCanExecuteChanged();
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

    private SqliteBatchJournal OpenJournal()
    {
        _dataPaths.EnsureCreated();
        return new SqliteBatchJournal(new CatalogDatabase(_dataPaths.DatabasePath));
    }

    // Premier temps : le plan est préparé et résumé, rien n'est modifié.
    [RelayCommand(CanExecute = nameof(CanRenameNow))]
    private async Task PrepareRenameAsync()
    {
        var root = SelectedFolderPath!;
        var results = _results;
        IsRenaming = true;
        RenameStatus = "Préparation du plan…";
        try
        {
            var journal = OpenJournal();
            if (journal.FindUnfinished() is { } unfinished)
            {
                RenameStatus = $"Le lot {unfinished.Id} est interrompu : terminez-le avec « photoapp reprend {unfinished.Id} » ou annulez-le avec « photoapp annule {unfinished.Id} » avant d'en lancer un autre.";
                return;
            }

            var prepared = await Task.Run(() => new RenameWorkflow(journal).Prepare(results, [root]));
            if (prepared.Renamable == 0)
            {
                RenameStatus = "Rien à renommer.";
                return;
            }

            _preparedRename = prepared;
            _preparedUndoBatchId = null;
            RenameStatus =
                $"{prepared.Renamable} fichier(s) à renommer sur place ({prepared.Plan.Summary.AlreadyConforming} déjà conforme(s), " +
                $"{prepared.Skipped.Count} écarté(s), {prepared.Plan.Summary.DeferredConversion} en conversion différée). " +
                "Le lot est annulable. Confirmer ?";
            IsRenameConfirmationPending = true;
        }
        catch (Exception ex)
        {
            RenameStatus = $"Erreur pendant la préparation : {ex.Message}";
        }
        finally
        {
            IsRenaming = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoNow))]
    private void PrepareUndo()
    {
        try
        {
            var journal = OpenJournal();
            if (journal.FindUnfinished() is { } unfinished)
            {
                RenameStatus = $"Le lot {unfinished.Id} est interrompu : indiquez-le avec « photoapp annule {unfinished.Id} » ou terminez-le avec « photoapp reprend {unfinished.Id} ».";
                return;
            }

            if (journal.FindLastUndoable() is not { } batch)
            {
                RenameStatus = "Aucun lot à annuler.";
                return;
            }

            _preparedUndoBatchId = batch.Id;
            _preparedRename = null;
            RenameStatus = $"Annuler le lot {batch.Id} ({batch.Counts.Done + batch.Counts.UndoFailed} fichier(s) à restaurer) ? Confirmer ?";
            IsRenameConfirmationPending = true;
        }
        catch (Exception ex)
        {
            RenameStatus = $"Erreur : {ex.Message}";
        }
    }

    // Second temps : exécute ce qui a été préparé (renommage ou annulation).
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var prepared = _preparedRename;
        var undoBatchId = _preparedUndoBatchId;
        if (prepared is null && undoBatchId is null)
        {
            return;
        }

        IsRenameConfirmationPending = false;
        IsRenaming = true;
        _renameCancellation = new CancellationTokenSource();
        var token = _renameCancellation.Token;
        OnPropertyChanged(nameof(CanStopBatch));
        var renaming = new Progress<BatchProgress>(p => RenameStatus = $"{(prepared is null ? "Annulation" : "Renommage")} : {p.Processed} / {p.Total}");
        try
        {
            var journal = OpenJournal();
            if (prepared is not null)
            {
                var summary = await Task.Run(() => new RenameWorkflow(journal).Apply(prepared, token, renaming));
                RenameStatus = summary.Cancelled
                    ? $"Lot {summary.BatchId} interrompu ({summary.Counts.Done} renommé(s)). « photoapp reprend {summary.BatchId} » le termine, « photoapp annule {summary.BatchId} » l'annule."
                    : $"Lot {summary.BatchId} terminé : {summary.Counts.Done} renommé(s), {summary.Counts.Failed} en échec, {summary.Counts.Skipped} écarté(s). Annulable avec « Annuler le dernier lot ».";
            }
            else
            {
                var batchId = undoBatchId!.Value;
                var summary = await Task.Run(() => new BatchUndo(journal).Undo(batchId, token, renaming));
                RenameStatus = summary.Completed
                    ? $"Lot {batchId} annulé : {summary.Restored} fichier(s) restauré(s)."
                    : token.IsCancellationRequested
                        ? $"Annulation du lot {batchId} interrompue : {summary.Restored} fichier(s) restauré(s), {summary.Failed} en échec ; les autres restent à restaurer, relancez « Annuler le dernier lot »."
                        : $"Lot {batchId} partiellement annulé : {summary.Restored} restauré(s), {summary.Failed} laissé(s) en place (voir « photoapp journal --lot {batchId} »).";
            }
        }
        catch (Exception ex)
        {
            RenameStatus = $"Erreur : {ex.Message}";
        }
        finally
        {
            _renameCancellation?.Dispose();
            _renameCancellation = null;
            _preparedRename = null;
            _preparedUndoBatchId = null;
            _results = [];
            OnPropertyChanged(nameof(CanStopBatch));
            IsRenaming = false;
        }

        await ScanSelectedFolderAsync();
    }

    // Abandonne la confirmation en attente, ou demande l'arrêt du lot en cours (le fichier en cours se termine).
    [RelayCommand]
    private void Abort()
    {
        if (IsRenaming)
        {
            if (_renameCancellation is null)
            {
                return;
            }

            _renameCancellation.Cancel();
            RenameStatus = "Arrêt demandé : le fichier en cours se termine.";
            return;
        }

        _preparedRename = null;
        _preparedUndoBatchId = null;
        IsRenameConfirmationPending = false;
        RenameStatus = "Abandonné, aucun fichier modifié.";
    }
}
