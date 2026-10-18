namespace PhotoApp.Operations;

public sealed record ConversionSummary(
    int Converted,
    int AlreadyDone,
    int SkippedUnreliableDate,
    int SkippedUnsupportedFormat,
    int Failed,
    string? FirstError)
{
    public string ToDisplayText()
    {
        var text = $"Terminé : {Converted} converti(s), {AlreadyDone} déjà fait(s), " +
                   $"{SkippedUnreliableDate} ignoré(s) (date non fiable), " +
                   $"{SkippedUnsupportedFormat} ignoré(s) (format non pris en charge), " +
                   $"{Failed} échec(s).";

        return Failed > 0 && FirstError is not null ? $"{text} Première erreur : {FirstError}" : text;
    }
}
