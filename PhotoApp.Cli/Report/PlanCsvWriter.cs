using System.Globalization;
using System.Text;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli.Report;

/// <summary>Plan d'un lot en CSV (UTF-8 avec BOM, séparateur « ; ») : une ligne par fichier, avec son état prévu et le motif d'un écart.</summary>
public static class PlanCsvWriter
{
    public static readonly IReadOnlyList<string> Columns = ["groupe", "role", "chemin", "cible", "date", "etat", "motif"];

    public static void WriteFile(string path, PreparedRename prepared)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Write(writer, prepared);
    }

    public static void Write(TextWriter writer, PreparedRename prepared)
    {
        writer.WriteLine(string.Join(';', Columns));
        foreach (var item in prepared.Plan.Items)
        {
            var skipped = prepared.Skipped.TryGetValue(item.Rank, out var reason);
            string[] row =
            [
                item.GroupId.ToString(CultureInfo.InvariantCulture),
                RoleLabel(item.Role),
                item.SourcePath,
                item.TargetPath,
                item.DecidedDate.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
                skipped ? "ecarte" : "a-renommer",
                skipped ? reason! : string.Empty
            ];
            writer.WriteLine(string.Join(';', row.Select(CsvReportWriter.Escape)));
        }
    }

    private static string RoleLabel(ItemRole role) => role switch
    {
        ItemRole.LivePartner => "partenaire-live",
        ItemRole.Sidecar => "sidecar",
        _ => "media"
    };
}
