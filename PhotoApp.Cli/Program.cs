using System.CommandLine;
using System.Text;
using PhotoApp.Cli;
using PhotoApp.Core;

Console.OutputEncoding = Encoding.UTF8;

// Ctrl+C : pendant l'exécution d'un lot seulement, le fichier en cours se termine puis le lot s'arrête proprement (état « interrompu »).
// Avant (analyse en lecture seule, question de confirmation), rien n'est encore écrit : le comportement par défaut s'applique
// (le processus se termine) ; l'intercepter ferait ignorer la touche, puis lancerait le lot avec un jeton déjà annulé.
using var cancellation = new CancellationTokenSource();
var gracefulCancel = false;
Console.CancelKeyPress += (_, e) =>
{
    if (Volatile.Read(ref gracefulCancel))
    {
        e.Cancel = true;
        cancellation.Cancel();
    }
};

void EnableGracefulCancel() => Volatile.Write(ref gracefulCancel, true);

static int Guard(Func<int> run)
{
    try
    {
        return run();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Erreur fatale : {ex.Message}");
        return ExitCodes.Fatal;
    }
}

// Identifiant de lot facultatif : absent → null, mal formé → refusé.
static bool TryParseBatchId(string? text, out long? id)
{
    id = null;
    if (text is null)
    {
        return true;
    }

    if (long.TryParse(text, out var parsed) && parsed > 0)
    {
        id = parsed;
        return true;
    }

    Console.Error.WriteLine($"Identifiant de lot invalide : {text}");
    return false;
}

// analyse
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
analyse.SetAction(parse => Guard(() =>
    new AnalyseCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error)
        .Run(new AnalyseRequest(parse.GetValue(folders) ?? [], parse.GetValue(output), parse.GetValue(exclude) ?? []))));

// renomme
var renameFolders = new Argument<string[]>("dossiers")
{
    Arity = ArgumentArity.OneOrMore,
    Description = "Dossiers à renommer sur place, récursivement."
};
var renameExclude = new Option<string[]>("--exclude")
{
    Description = "Dossier à ignorer (option répétable)."
};
var apply = new Option<bool>("--applique")
{
    Description = "Exécute le plan. Sans cette option, la commande n'affiche que le plan et ne modifie aucun fichier."
};
var yes = new Option<bool>("--oui")
{
    Description = "Ne demande pas de confirmation (avec --applique)."
};
var slice = new Option<string?>("--tranche")
{
    Description = "Ne traite que les n premiers groupes du plan (dossiers synchronisés) ; relancer pour la tranche suivante."
};

var renomme = new Command("renomme", "Renomme les médias sur place d'après les dates décidées (aperçu par défaut), journal et annulation possibles.")
{
    renameFolders, renameExclude, apply, yes, slice
};
renomme.SetAction(parse => Guard(() =>
{
    int? sliceValue = null;
    if (parse.GetValue(slice) is { } sliceText)
    {
        if (!int.TryParse(sliceText, out var parsed) || parsed <= 0)
        {
            Console.Error.WriteLine("--tranche doit être un entier positif.");
            return ExitCodes.InvalidArguments;
        }

        sliceValue = parsed;
    }

    return new RenommeCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error, Console.In)
        .Run(
            new RenommeRequest(parse.GetValue(renameFolders) ?? [], parse.GetValue(renameExclude) ?? [], parse.GetValue(apply), parse.GetValue(yes), sliceValue),
            cancellation.Token,
            EnableGracefulCancel);
}));

// journal
var journalLot = new Option<string?>("--lot")
{
    Description = "Identifiant d'un lot : affiche le détail fichier par fichier."
};
var journal = new Command("journal", "Liste les lots de renommage, ou détaille l'un d'eux.") { journalLot };
journal.SetAction(parse => Guard(() =>
    TryParseBatchId(parse.GetValue(journalLot), out var id)
        ? new JournalCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error).Run(id)
        : ExitCodes.InvalidArguments));

// annule
var annuleLot = new Argument<string?>("lot")
{
    Arity = ArgumentArity.ZeroOrOne,
    Description = "Identifiant du lot à annuler (par défaut : le dernier lot terminé)."
};
var annuleYes = new Option<bool>("--oui")
{
    Description = "Ne demande pas de confirmation."
};
var annule = new Command("annule", "Annule un lot : les fichiers retrouvent leur nom et leurs dates d'origine.") { annuleLot, annuleYes };
annule.SetAction(parse => Guard(() =>
{
    if (!TryParseBatchId(parse.GetValue(annuleLot), out var id))
    {
        return ExitCodes.InvalidArguments;
    }

    // L'arrêt propre ne vaut qu'une fois la confirmation donnée : la restauration gère alors l'arrêt fichier par fichier.
    return new AnnuleCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error, Console.In)
        .Run(id, parse.GetValue(annuleYes), cancellation.Token, EnableGracefulCancel);
}));

// reprend
var reprendLot = new Argument<string>("lot")
{
    Description = "Identifiant du lot interrompu à terminer."
};
var reprend = new Command("reprend", "Termine un lot interrompu.") { reprendLot };
reprend.SetAction(parse => Guard(() =>
{
    if (!TryParseBatchId(parse.GetValue(reprendLot), out var id) || id is not { } batchId)
    {
        return ExitCodes.InvalidArguments;
    }

    EnableGracefulCancel();
    return new ReprendCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error).Run(batchId, cancellation.Token);
}));

var root = new RootCommand("PhotoManager en ligne de commande.") { analyse, renomme, journal, annule, reprend };
return root.Parse(args).Invoke();
