using System.Globalization;
using PhotoApp.Catalog;
using PhotoApp.Cli.Report;
using PhotoApp.Core;

namespace PhotoApp.Cli;

public sealed record AnalyseRequest(IReadOnlyList<string> Folders, string? OutputPath, IReadOnlyList<string> Excluded);

public static class ExitCodes
{
    public const int Success = 0;
    public const int InvalidArguments = 1;
    public const int Fatal = 2;
}

/// <summary>
/// Commande « analyse » (spec de phase 1, §9) : rapport CSV et résumé. Aucune écriture à côté des photos ;
/// le rapport va par défaut dans le dossier reports des données de l'application.
/// </summary>
public sealed class AnalyseCommand
{
    private readonly AppDataPaths _paths;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<DateTime> _localNow;

    public AnalyseCommand(AppDataPaths paths, TextWriter output, TextWriter error, Func<DateTime>? localNow = null)
    {
        _paths = paths;
        _output = output;
        _error = error;
        _localNow = localNow ?? (() => DateTime.Now);
    }

    public int Run(AnalyseRequest request)
    {
        var folders = request.Folders.Select(folder => Path.GetFullPath(folder)).ToList();
        var missing = folders.FirstOrDefault(folder => !Directory.Exists(folder));
        if (missing is not null)
        {
            _error.WriteLine($"Dossier introuvable : {missing}");
            return ExitCodes.InvalidArguments;
        }

        var excluded = request.Excluded.Select(folder => Path.GetFullPath(folder)).ToList();
        var analysis = new MediaAnalyzer().Analyze(folders, excluded, new ConsoleProgress(_error));
        _error.WriteLine();

        var reportPath = request.OutputPath is { Length: > 0 } output
            ? Path.GetFullPath(output)
            : Path.Combine(_paths.ReportsFolder, "analyse-" + _localNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");

        try
        {
            CsvReportWriter.WriteFile(reportPath, analysis.Results);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _error.WriteLine($"Impossible d'écrire le rapport {reportPath} : {ex.Message}");
            return ExitCodes.Fatal;
        }

        AnalysisSummary.From(analysis).Print(_output);
        _output.WriteLine($"Rapport : {reportPath}");
        return ExitCodes.Success;
    }
}
