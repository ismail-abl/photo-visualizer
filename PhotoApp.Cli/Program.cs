using System.CommandLine;
using System.Text;
using PhotoApp.Cli;
using PhotoApp.Core;

Console.OutputEncoding = Encoding.UTF8;

var folders = new Argument<string[]>("dossiers")
{
    Arity = ArgumentArity.OneOrMore,
    Description = "Dossiers à analyser, récursivement."
};
var output = new Option<string?>("--out")
{
    Description = "Fichier CSV du rapport (par défaut : dossier reports des données de l'application)."
};
var exclude = new Option<string[]>("--exclude")
{
    Description = "Dossier à ignorer (option répétable)."
};

var analyse = new Command("analyse", "Analyse les dates et propose des noms, sans rien modifier.") { folders, output, exclude };
analyse.SetAction(parse =>
{
    try
    {
        return new AnalyseCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error)
            .Run(new AnalyseRequest(parse.GetValue(folders) ?? [], parse.GetValue(output), parse.GetValue(exclude) ?? []));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Erreur fatale : {ex.Message}");
        return ExitCodes.Fatal;
    }
});

var root = new RootCommand("PhotoManager en ligne de commande.") { analyse };
return root.Parse(args).Invoke();
