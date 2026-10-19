namespace PhotoApp.Cli;

/// <summary>Question oui/non sur la console. Tout ce qui n'est pas un oui explicite, fin de saisie comprise, vaut non.</summary>
public static class Confirmation
{
    private static readonly string[] Yes = ["o", "oui", "y", "yes"];

    public static bool Ask(string question, TextReader input, TextWriter output)
    {
        output.Write(question + " [o/N] ");
        var answer = input.ReadLine()?.Trim();
        return answer is not null && Yes.Contains(answer, StringComparer.OrdinalIgnoreCase);
    }
}
