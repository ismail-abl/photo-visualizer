# Phase 2 — Exécution sûre (renommage sur place) : plan d'implémentation

> **Pour les agents d'exécution :** SOUS-COMPÉTENCE REQUISE : utiliser superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans pour exécuter ce plan tâche par tâche. Les étapes utilisent des cases à cocher (`- [ ]`).

**Objectif :** renommer pour de bon, sur place, les médias dont le moteur de phase 1 propose un nom (Sûre et Probable), avec un lot figé, un journal SQLite écrit avant chaque action, une reprise après interruption et une annulation qui redonne un état identique (noms, contenus, dates) ; corriger au passage les trois points du moteur qui faussaient des renommages réels.

**Architecture :** `PhotoApp.Core` gagne l'espace de noms pur `Batches` (modèle d'un lot, `IBatchJournal`, noms de sidecars). `PhotoApp.Catalog` gagne la base SQLite `catalog.db` (`CatalogDatabase`, `SqliteBatchJournal`, `SqliteQuarantineLog`). `PhotoApp.Operations` gagne le planificateur, les contrôles préalables, l'exécuteur sur place, la reprise, l'annulation, la quarantaine et une façade `RenameWorkflow` partagée par la CLI et l'interface. `PhotoApp.Cli` gagne `renomme`, `journal`, `annule`, `reprend` ; l'interface gagne deux boutons. L'ancien flux (dossier de sortie, conversion JPEG) reste intact.

**Tech Stack :** .NET 10, Avalonia 12.0.2, MetadataExtractor 2.9.3, Magick.NET-Q8-AnyCPU 14.17.2, System.CommandLine 2.0.13, **Microsoft.Data.Sqlite 10.0.0 (nouveau, projet Catalog)**, xUnit + Moq.

**Spec :** `docs/superpowers/specs/2026-10-19-phase2-execution-sure-design.md` (cadre : `docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md`, §6.2, §7 et §10 ; moteur : `docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md`).

## Contraintes globales

- `PhotoApp.Core` reste **pur** : aucune E/S fichier, aucun paquet NuGet. Les fonctions de chaînes de `Path` sont permises.
- **Un renommage ne modifie que le nom et les dates du fichier** (création et modification). Jamais d'écrasement : toute collision de nom est un échec explicite. Aucune suppression de fichier dans cette phase (la seule, `Quarantine.Purge`, n'est appelée par aucune commande).
- Le journal est écrit **avant** l'action : l'élément passe à *en cours* (transaction validée), puis le fichier est déplacé, puis l'élément passe à *fait*.
- **Aucun nom temporaire** pour un renommage ordinaire (pas de double événement de synchronisation Nextcloud). Seule exception : un changement de casse seul (`IMG-….JPG` → `.jpg`), fait en deux temps.
- Heure de référence = heure murale du lieu de prise de vue, 24 h ; les dates du fichier prennent l'instant `DateCandidate.ToInstant(fuseau)`.
- Nommage inchangé (phase 1) : `IMG-AAAAMMJJ-HHmmss.ext`, `VID-…` pour les vidéos, jour seul `IMG-AAAAMMJJ-000000-NN.ext`.
- Confiances appliquées : **Sûre et Probable**. Incertaine et Aucune ne sont jamais renommées.
- HEIC, HEIF et AVIF sont **renommés tels quels** et comptés « conversion différée » (conversion en phase 3).
- Rien n'est écrit à côté des photos hors renommage : les rapports vont dans `<racine de données>\reports\`, la base dans `<racine de données>\catalog.db` (`AppDataPaths`). Les dossiers de données sont créés par `AppDataPaths.EnsureCreated()` ; `CatalogDatabase` et `Quarantine` **n'en créent aucun** (le dossier cible doit exister).
- Codes de sortie de la CLI : 0 succès ; 1 arguments invalides ; 2 erreur fatale ; **3 lot terminé avec des échecs, ou interrompu**.
- Identifiants de code en anglais, commentaires et textes affichés en français (style du dépôt). Les tests ont des noms en anglais `Sujet_Condition_Résultat` comme dans les tests existants.
- Corpus de test **généré à l'exécution** (`TestImages`, `TestVideos`, dossiers `TempDirectory`) : aucune donnée privée, jamais la bibliothèque réelle avant la tâche 15.
- Windows seul est dans le champ de cette phase (Linux et macOS : hors champ jusqu'à la fin du projet) : ne pas bloquer une tâche sur eux.
- Les commits se terminent par la ligne `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>` (second `-m` dans les commandes ci-dessous). Ne pas réécrire l'historique pour les trailers.
- Ne pas pousser la branche, ne pas fusionner dans `main` sans la validation de l'utilisateur (la fusion locale se propose en fin de phase ; le `git push` est fait par l'utilisateur).

## Écarts assumés par rapport à la spec

- **Verrou d'exécution** (spec §10 : « verrou sur la base ») : un fichier de verrou `catalog.db.lock` ouvert en exclusivité (`FileShare.None`, `DeleteOnClose`) à côté de la base. Il est libéré automatiquement si le processus est tué, ce qu'un verrou stocké dans la base ne garantit pas.
- **Colonne « options » du lot** (spec §6) : supprimée, aucune option n'est à mémoriser en phase 2 (YAGNI). La version de schéma est bien enregistrée.
- **Code de sortie 3** : couvre aussi un lot interrompu (Ctrl+C), pas seulement un lot terminé avec des échecs.
- **Changement de casse seul** : fait en deux temps via un nom `.<guid>.tmp` voisin. Si le processus est tué entre les deux déplacements, la reprise voit « ni ancien nom ni nouveau nom » et marque l'élément en échec ; le fichier `.tmp` est alors à renommer à la main. Cas très rare (nom produit par PhotoManager avec une extension en majuscules).
- **Groupe partiellement exécuté** : le groupe est tout ou rien **au plan et aux contrôles préalables** ; un échec d'exécution tardif (verrou apparu entre-temps) d'un seul fichier laisse les autres membres déjà renommés et est signalé dans le journal.
- **Échec des dates après le déplacement** : le fichier est remis sous son ancien nom (retour arrière au mieux) et l'élément passe en échec, comme le demande la spec §5.3.
- **Brique de quarantaine** (§5.6) : testée et non utilisée en phase 2 ; son dossier est un paramètre.
- **Interface** : confirmation en deux temps dans la fenêtre (bouton « Renommer sur place » puis « Confirmer »), faute de boîte de dialogue standard dans le projet.

---

## Structure des fichiers (cible de la phase 2)

```
PhotoApp.Core/Batches/                       (nouveau, pur)
  BatchModel.cs                              BatchState, ItemState, ItemRole, BatchItemDraft, BatchItem, BatchCounts, BatchInfo
  IBatchJournal.cs                           interface du journal + BatchLockedException
  SidecarNames.cs                            nom d'un sidecar d'après le nouveau nom du média
  QuarantineModel.cs                         QuarantineState, QuarantineEntry, IQuarantineLog
PhotoApp.Core/Models/RawMetadata.cs          + SourceWarnings
PhotoApp.Core/Normalization/DecisionFlags.cs + LivePhotoConflict, SourceIgnored
PhotoApp.Core/Normalization/DecisionTexts.cs + libellés des deux drapeaux
PhotoApp.Core/Normalization/DateArbiter.cs   capture : métadonnée antérieure au nom
PhotoApp.Core/Normalization/MediaNamer.cs    Live Photo : identifiants concordants, dates d'accord
PhotoApp.Core/Normalization/NormalizationEngine.cs  pose les deux nouveaux drapeaux
PhotoApp.Metadata/MetadataReader.cs          garde par source
PhotoApp.Catalog/
  PhotoApp.Catalog.csproj                    + Microsoft.Data.Sqlite
  CatalogDatabase.cs                         ouverture, schéma, version
  SqliteBatchJournal.cs                      IBatchJournal sur SQLite, verrou d'exécution
  SqliteQuarantineLog.cs                     IQuarantineLog sur SQLite
PhotoApp.Operations/
  FileTimestamps.cs                          lecture/écriture des dates de création et de modification
  FileMoves.cs                               déplacement sans écrasement (casse seule en deux temps)
  BatchPreflight.cs                          contrôles préalables, groupes tout ou rien
  BatchPlanner.cs                            MediaResult[] → BatchPlan
  InPlaceRenamer.cs                          exécution d'un lot, progression, arrêt
  BatchRecovery.cs                           résolution des éléments laissés « en cours »
  BatchUndo.cs                               annulation d'un lot
  Quarantine.cs                              brique de quarantaine
  RenameWorkflow.cs                          façade Prepare / Apply partagée par la CLI et l'interface
PhotoApp.Cli/
  PhotoApp.Cli.csproj                        + référence à Operations
  ExitCodes (dans AnalyseCommand.cs)         + BatchIssues = 3
  Confirmation.cs, SyncFolderDetector.cs, BatchConsoleProgress.cs, BatchSummaryPrinter.cs
  Report/PlanCsvWriter.cs
  RenommeCommand.cs, JournalCommand.cs, AnnuleCommand.cs, ReprendCommand.cs
  Program.cs                                 les quatre commandes
PhotoApp/ViewModels/MainWindowViewModel.cs   deux boutons, confirmation en deux temps
PhotoApp/Views/MainWindow.axaml              boutons et état
PhotoApp.Tests/                              un fichier de tests par classe nouvelle (voir les tâches)
```

---

### Task 0: Branche de la phase et ligne de base

**Files:** aucun.

- [ ] **Step 1: Créer la branche**

Run : `git switch -c feat/phase2-execution-sure`
Expected : `Switched to a new branch 'feat/phase2-execution-sure'`

- [ ] **Step 2: Vérifier la ligne de base**

Run : `dotnet test PhotoVisualizer.slnx`
Expected : tous les tests passent (376 d'après la phase 1). Noter le total : les tâches suivantes en ajoutent, aucune ne doit en retirer sans le dire.

---

### Task 1: Garde par source dans `MetadataReader` (point ouvert n° 1)

Un GPS mal formé (`Rational(10, 0)` : `TryGetGpsDate` lève `ArgumentOutOfRangeException`) fait perdre aujourd'hui la date de tout le fichier : l'exception remonte jusqu'à la garde générale de `Read`, qui renvoie `ReadError`. Chaque source est désormais lue dans sa propre garde ; la source fautive est ignorée et signalée.

**Files:**
- Modify: `PhotoApp.Core/Models/RawMetadata.cs`
- Modify: `PhotoApp.Metadata/MetadataReader.cs`
- Modify: `PhotoApp.Core/Normalization/DecisionFlags.cs`
- Modify: `PhotoApp.Core/Normalization/DecisionTexts.cs`
- Modify: `PhotoApp.Core/Normalization/NormalizationEngine.cs`
- Test: `PhotoApp.Tests/MetadataReaderTests.cs`, `PhotoApp.Tests/NormalizationEngineTests.cs`, `PhotoApp.Tests/DecisionTextsTests.cs`, `PhotoApp.Tests/AnalyseCommandTests.cs`

**Interfaces:**
- Produces : `RawMetadata.SourceWarnings : IReadOnlyList<string>` (vide par défaut ; une entrée `"gps : <message>"`, `"exif : …"`, `"xmp : …"`, `"quicktime : …"` par source ignorée) ; `DecisionFlags.SourceIgnored = 128` (libellé `source-ignoree`).

- [ ] **Step 1: Remplacer le test existant, trop permissif**

Dans `PhotoApp.Tests/MetadataReaderTests.cs`, remplacer le test `MalformedGpsTimestamp_DoesNotThrow` (qui accepte aujourd'hui `ReadError` non nul) par :

```csharp
    [Fact]
    public void MalformedGpsTimestamp_OnlyCostsTheGpsSource()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            exif.SetValue(ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ExifTag.GPSTimestamp, [new Rational(10, 0), new Rational(0, 1), new Rational(0, 1)]);
        });

        var metadata = Reader.Read(path);

        Assert.Null(metadata.ReadError);
        Assert.Contains(metadata.Dates, d => d.Origin == CandidateOrigin.ExifOriginal && d.WallClock == new DateTime(2023, 3, 4, 13, 20, 15));
        Assert.DoesNotContain(metadata.Dates, d => d.Origin == CandidateOrigin.Gps);
        Assert.Contains(metadata.SourceWarnings, warning => warning.StartsWith("gps", StringComparison.Ordinal));
    }

    [Fact]
    public void WellFormedFile_HasNoSourceWarnings()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15");

        Assert.Empty(Reader.Read(path).SourceWarnings);
    }
```

Dans `PhotoApp.Tests/DecisionTextsTests.cs`, ajouter à `FlagLabels_ListEverySetFlag` :

```csharp
        Assert.Equal(["source-ignoree", "live-photo-conflit"], DecisionTexts.FlagLabels(DecisionFlags.SourceIgnored | DecisionFlags.LivePhotoConflict));
```

Dans `PhotoApp.Tests/NormalizationEngineTests.cs`, ajouter (le helper `Facts` et `Engine()` existent déjà dans ce fichier) :

```csharp
    [Fact]
    public void IgnoredSource_IsFlagged()
    {
        var exif = new DateCandidate(new DateTime(2023, 3, 4, 13, 20, 15), DatePrecision.Second, CandidateOrigin.ExifOriginal);

        var result = Engine().Normalize([Facts("/p/holiday.jpg", new RawMetadata { Dates = [exif], SourceWarnings = ["gps : valeur hors limites"] })]).Single();

        Assert.True(result.Decision.Flags.HasFlag(DecisionFlags.SourceIgnored));
        Assert.True(result.Decision.IsApplicable);
    }
```

Dans `PhotoApp.Tests/AnalyseCommandTests.cs`, remplacer le test `Run_MalformedFile_DoesNotStopTheAnalysis` par deux tests :

```csharp
    [Fact]
    public void Run_MalformedGps_KeepsTheFileAndFlagsTheIgnoredSource()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        TestImages.WriteJpegWithExif(Path.Combine(photos, "bad.jpg"), exif =>
        {
            exif.SetValue(ImageMagick.ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            exif.SetValue(ImageMagick.ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ImageMagick.ExifTag.GPSTimestamp, [new ImageMagick.Rational(10, 0), new ImageMagick.Rational(0, 1), new ImageMagick.Rational(0, 1)]);
        });
        var report = temp.Combine("rapport.csv");

        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(new AnalyseRequest([photos], report, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = File.ReadAllLines(report);
        Assert.Equal(5, lines.Length);   // en-tête + 4 médias
        var bad = Assert.Single(lines, line => line.Contains("bad.jpg"));
        Assert.DoesNotContain(Reasons.ReadFailed, bad);
        Assert.Contains("source-ignoree", bad);
    }

    [Fact]
    public void Run_UnreadableFile_DoesNotStopTheAnalysis()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        File.WriteAllBytes(Path.Combine(photos, "broken.jpg"), [1, 2, 3, 4]);
        var report = temp.Combine("rapport.csv");

        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(new AnalyseRequest([photos], report, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        var lines = File.ReadAllLines(report);
        Assert.Equal(5, lines.Length);   // en-tête + 4 médias
        Assert.Contains(lines, line => line.Contains("broken.jpg") && line.Contains(Reasons.ReadFailed));
    }
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~MetadataReaderTests|FullyQualifiedName~DecisionTextsTests|FullyQualifiedName~NormalizationEngineTests|FullyQualifiedName~AnalyseCommandTests"`
Expected : erreur de compilation (`SourceWarnings`, `DecisionFlags.SourceIgnored` et `LivePhotoConflict` n'existent pas). C'est l'échec attendu.

- [ ] **Step 3: Ajouter les drapeaux et leurs libellés**

`PhotoApp.Core/Normalization/DecisionFlags.cs` : après `LivePhoto = 32` (ajouter une virgule), ajouter :

```csharp
    LivePhoto = 32,
    LivePhotoConflict = 64,
    SourceIgnored = 128
```

`PhotoApp.Core/Normalization/DecisionTexts.cs`, dans `FlagLabels`, les libellés sortent dans l'ordre des `Add` : placer `source-ignoree` **avant** `live-photo-conflit` pour que l'assertion du test (`["source-ignoree", "live-photo-conflit"]`) tienne. Après `Add(DecisionFlags.LivePhoto, "live-photo");` :

```csharp
        Add(DecisionFlags.LivePhoto, "live-photo");
        Add(DecisionFlags.SourceIgnored, "source-ignoree");
        Add(DecisionFlags.LivePhotoConflict, "live-photo-conflit");
```

- [ ] **Step 4: Ajouter `SourceWarnings` à `RawMetadata`**

`PhotoApp.Core/Models/RawMetadata.cs`, après la propriété `ReadError` :

```csharp

    /// <summary>Sources ignorées parce que mal formées (« gps : message »). Les autres sources restent valables.</summary>
    public IReadOnlyList<string> SourceWarnings { get; init; } = [];
```

- [ ] **Step 5: Garder chaque source dans `MetadataReader`**

Dans `PhotoApp.Metadata/MetadataReader.cs`, remplacer dans `ReadCore` le bloc qui construit `dates` (de `var dates = new List<DateCandidate>();` jusqu'à la fin de la boucle des sidecars) par :

```csharp
        var dates = new List<DateCandidate>();
        var warnings = new List<string>();
        var exif = directories.OfType<ExifSubIfdDirectory>().ToList();
        Guard("exif", warnings, () =>
        {
            AddExifDate(exif, ExifDirectoryBase.TagDateTimeOriginal, TagOffsetTimeOriginal, ExifDirectoryBase.TagSubsecondTimeOriginal, CandidateOrigin.ExifOriginal, dates);
            AddExifDate(exif, ExifDirectoryBase.TagDateTimeDigitized, TagOffsetTimeDigitized, ExifDirectoryBase.TagSubsecondTimeDigitized, CandidateOrigin.ExifDigitized, dates);
        });
        Guard("gps", warnings, () => AddGps(directories, dates));
        Guard("xmp", warnings, () => AddXmp(directories, dates));
        Guard("quicktime", warnings, () => AddQuickTime(directories, apple, dates));
        foreach (var sidecar in sidecars ?? [])
        {
            if (string.Equals(Path.GetExtension(sidecar), ".xmp", StringComparison.OrdinalIgnoreCase))
            {
                AddXmpSidecar(sidecar, dates);
            }
        }
```

Dans le `return new RawMetadata { … }` de `ReadCore`, ajouter après `DetectedExtension = detected` (ajouter une virgule) :

```csharp
            DetectedExtension = detected,
            SourceWarnings = warnings
```

Ajouter la méthode dans la classe, avant `Clean` :

```csharp
    // Une source mal formée (GPS à dénominateur nul, balise du mauvais type…) est ignorée et signalée : elle ne doit jamais
    // faire perdre les dates des autres sources du même fichier.
    private static void Guard(string source, List<string> warnings, Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex)
        {
            warnings.Add($"{source} : {ex.Message}");
        }
    }
```

- [ ] **Step 6: Poser le drapeau dans le moteur**

Dans `PhotoApp.Core/Normalization/NormalizationEngine.cs`, dans la projection finale de `Normalize`, remplacer le calcul de `decision` par :

```csharp
                var decision = names[i].LivePhotoPartner is null
                    ? decisions[i]
                    : decisions[i] with { Flags = decisions[i].Flags | DecisionFlags.LivePhoto };
                if (f.Metadata.SourceWarnings.Count > 0)
                {
                    decision = decision with { Flags = decision.Flags | DecisionFlags.SourceIgnored };
                }

```

(La ligne `return new MediaResult(...)` qui suit reste inchangée.)

- [ ] **Step 7: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~MetadataReaderTests|FullyQualifiedName~DecisionTextsTests|FullyQualifiedName~NormalizationEngineTests|FullyQualifiedName~AnalyseCommandTests"`
Expected : PASS. Le test `DecisionTextsTests` référence `LivePhotoConflict`, ajouté à l'étape 3 : la compilation passe.

Puis la suite entière : `dotnet test PhotoVisualizer.slnx` — Expected : PASS.

- [ ] **Step 8: Commit**

```bash
git add PhotoApp.Core PhotoApp.Metadata PhotoApp.Tests
git commit -m "fix: une source mal formée (GPS) est ignorée et signalée, elle ne fait plus perdre la date du fichier" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Captures d'écran (métadonnée antérieure au nom) et profil epoch nu (points ouverts n° 3 et 4)

Le nom d'une capture est l'heure d'**enregistrement** ; une métadonnée EXIF ou XMP antérieure est l'heure réelle de la capture, comme pour un appareil (`exif-avant-nom`). Cas réel : `Screenshot 2025-05-19 224500.png` avec un XMP à 22:44:46.079 doit devenir `IMG-20250519-224446.png`. Le profil epoch nu n'a aucun fichier dans la bibliothèque de référence : un test fige son comportement.

**Files:**
- Modify: `PhotoApp.Core/Normalization/DateArbiter.cs` (méthodes `SelectPrimary` et `Agrees`)
- Test: `PhotoApp.Tests/DateArbiterTests.cs`

**Interfaces:**
- Consumes : `DateComparison.CompareAt(DateCandidate, DateCandidate, DatePrecision) : int` ; `Reasons.ExifBeforeName` ; les helpers de test `DateArbiterTests.Decide`, `Exif`.
- Produces : aucune API ; nouveau comportement de `DateArbiter.Decide`.

- [ ] **Step 1: Écrire les tests**

Dans `PhotoApp.Tests/DateArbiterTests.cs`, ajouter dans la classe :

```csharp
    private static DateCandidate Xmp(DateTime value) =>
        new(value, DatePrecision.SubSecond, CandidateOrigin.Xmp);

    [Fact]
    public void Screenshot_WithAnEarlierXmp_TakesTheXmp()
    {
        var decision = Decide("Screenshot 2025-05-19 224500.png", dates: Xmp(new DateTime(2025, 5, 19, 22, 44, 46, 79)));

        Assert.Equal(Reasons.ExifBeforeName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(new DateTime(2025, 5, 19, 22, 44, 46, 79), decision.Date?.WallClock);
        Assert.False(decision.Flags.HasFlag(DecisionFlags.Disagreement));
    }

    [Fact]
    public void Screenshot_WithAnEarlierExif_TakesTheExif()
    {
        var decision = Decide("Screenshot 2025-05-19 224500.png", dates: Exif(new DateTime(2025, 5, 19, 22, 40, 0)));

        Assert.Equal(Reasons.ExifBeforeName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(new DateTime(2025, 5, 19, 22, 40, 0), decision.Date?.WallClock);
    }

    [Fact]
    public void Screenshot_WithALaterExif_StaysInDisagreement()
    {
        var decision = Decide("Screenshot 2025-05-19 224500.png", dates: Exif(new DateTime(2025, 5, 20, 8, 0, 0)));

        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.Equal(Confidence.Uncertain, decision.Confidence);
    }

    [Fact]
    public void Screenshot_WithoutMetadata_KeepsTheNameAsTheCaptureTime()
    {
        var decision = Decide("Screenshot 2025-05-19 224500.png");

        Assert.Equal(Reasons.ScreenshotName, decision.Reason);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(new DateTime(2025, 5, 19, 22, 45, 0), decision.Date?.WallClock);
    }

    [Fact]
    public void BareEpochName_IsProbable_AndLocalizedFromUtc()
    {
        // 1700000000 s = 2023-11-14 22:13:20 UTC ; le fuseau de test est UTC+1.
        var decision = Decide("1700000000.jpg");

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(new DateTime(2023, 11, 14, 23, 13, 20), decision.Date?.WallClock);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.AssumedZone));
    }
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~DateArbiterTests"`
Expected : `Screenshot_WithAnEarlierXmp_TakesTheXmp` et `Screenshot_WithAnEarlierExif_TakesTheExif` échouent (raison `capture-nom` au lieu de `exif-avant-nom`, ou `desaccord`). Les trois autres passent déjà : ce sont des tests de caractérisation. **Si `BareEpochName_…` échoue**, ne pas modifier le moteur : relever la valeur réelle, vérifier à la main qu'elle correspond à la définition d'epoch ci-dessus, et corriger l'assertion du test seulement si l'écart vient du test.

- [ ] **Step 3: Implémenter la règle des captures**

Dans `PhotoApp.Core/Normalization/DateArbiter.cs`, méthode `SelectPrimary`, remplacer le bloc

```csharp
        if (family == ProfileFamily.Screenshot && profileName is not null)
        {
            return new Choice(profileName, Confidence.Certain, Reasons.ScreenshotName);
        }
```

par :

```csharp
        if (family == ProfileFamily.Screenshot && profileName is not null)
        {
            // Le nom d'une capture est l'heure d'enregistrement : une métadonnée antérieure est l'heure réelle de la capture
            // (même principe qu'un appareil dont le fichier édité garde l'EXIF d'origine).
            if (profileName.IsUtc is false)
            {
                var earlier = new[] { exif, c.Xmp }.FirstOrDefault(
                    meta => meta is not null && DateComparison.CompareAt(meta, profileName, profileName.Precision) < 0);
                if (earlier is not null)
                {
                    return new Choice(earlier, Confidence.Certain, Reasons.ExifBeforeName);
                }
            }

            return new Choice(profileName, Confidence.Certain, Reasons.ScreenshotName);
        }
```

Dans `Agrees`, la dernière règle (« Enregistrement ou transfert postérieur ») ne connaît que l'EXIF ; y ajouter les deux origines XMP. Remplacer

```csharp
               && primary.Origin is CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized
```

par :

```csharp
               && primary.Origin is CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized or CandidateOrigin.Xmp or CandidateOrigin.XmpSidecar
```

- [ ] **Step 4: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~DateArbiter"`
Expected : PASS (y compris `DateArbiterConfrontationTests` et `DateArbiterTests` existants).

- [ ] **Step 5: Commit**

```bash
git add PhotoApp.Core PhotoApp.Tests
git commit -m "feat: capture d'écran, une métadonnée antérieure au nom fait foi ; test du profil epoch nu" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Live Photo : identifiants concordants et dates d'accord (point ouvert n° 2)

Aujourd'hui la vidéo prend toujours le nom de sa photo, même si leurs dates divergent, et l'appariement par nom de base ignore des `ContentIdentifier` différents. Nouvelle règle : le repli par nom de base exige des identifiants concordants quand les deux fichiers en ont un ; un couple dont la vidéo a sa propre décision applicable **en désaccord** avec la date de la photo est dissous (chacun suit sa décision) et signalé par `live-photo-conflit`.

**Files:**
- Modify: `PhotoApp.Core/Normalization/MediaNamer.cs`
- Modify: `PhotoApp.Core/Normalization/NormalizationEngine.cs`
- Test: `PhotoApp.Tests/MediaNamerTests.cs`, `PhotoApp.Tests/NormalizationEngineTests.cs`

**Interfaces:**
- Consumes : `DateComparison.Concordant(DateCandidate, DateCandidate) : bool`.
- Produces : `NamingResult(string? ProposedName, string? LivePhotoPartner, bool LivePhotoConflict = false)`.

- [ ] **Step 1: Écrire les tests**

Dans `PhotoApp.Tests/MediaNamerTests.cs`, ajouter après `LivePhoto_WithoutAPhotoName_LeavesTheVideoUnnamed` :

```csharp
    [Fact]
    public void LivePhoto_BySameBaseName_WithDifferentContentIdentifiers_IsNotPaired()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "AAA"),
            Input("/a/IMG_0477.mov", Shot, kind: MediaKind.Video, extension: ".mov", contentIdentifier: "BBB"));

        Assert.Equal("IMG-20240105-150412.heic", results[0].ProposedName);
        Assert.Equal("VID-20240105-150412.mov", results[1].ProposedName);
        Assert.Null(results[0].LivePhotoPartner);
        Assert.Null(results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_BySameBaseName_WhenOnlyOneSideHasAnIdentifier_IsStillPaired()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "AAA"),
            Input("/a/IMG_0477.mov", Shot, kind: MediaKind.Video, extension: ".mov"));

        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
        Assert.Equal("IMG_0477.heic", results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_WhenTheVideoDateDisagrees_EachFileFollowsItsOwnDecision_AndTheConflictIsReported()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "ABC"),
            Input("/a/IMG_0478.mov", Shot.AddHours(3), kind: MediaKind.Video, extension: ".mov", contentIdentifier: "ABC"));

        Assert.Equal("IMG-20240105-150412.heic", results[0].ProposedName);
        Assert.Equal("VID-20240105-180412.mov", results[1].ProposedName);
        Assert.Null(results[0].LivePhotoPartner);
        Assert.Null(results[1].LivePhotoPartner);
        Assert.True(results[0].LivePhotoConflict);
        Assert.True(results[1].LivePhotoConflict);
    }

    [Fact]
    public void LivePhoto_WhenTheVideoHasNoApplicableDecision_StillFollowsThePhoto()
    {
        var results = Name(
            Input("/a/IMG_0477.heic", Shot, extension: ".heic", contentIdentifier: "ABC"),
            Input("/a/IMG_0478.mov", Shot.AddHours(3), kind: MediaKind.Video, extension: ".mov", contentIdentifier: "ABC", confidence: Confidence.Uncertain));

        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
        Assert.False(results[1].LivePhotoConflict);
    }
```

Dans `PhotoApp.Tests/NormalizationEngineTests.cs`, ajouter :

```csharp
    [Fact]
    public void LivePhotoConflict_IsFlaggedOnBothFiles()
    {
        var photoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(2));
        var videoDate = new DateCandidate(new DateTime(2024, 7, 11, 20, 0, 0), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));

        var results = Engine().Normalize(
        [
            Facts("/p/IMG_0477.HEIC", new RawMetadata { Dates = [photoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "heic" }),
            Facts("/p/IMG_0477.MOV", new RawMetadata { Dates = [videoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "mov" })
        ]);

        Assert.All(results, r =>
        {
            Assert.True(r.Decision.Flags.HasFlag(DecisionFlags.LivePhotoConflict));
            Assert.False(r.Decision.Flags.HasFlag(DecisionFlags.LivePhoto));
            Assert.Null(r.LivePhotoPartner);
        });
        Assert.Equal("VID-20240711-200000.mov", results.Single(r => r.Kind == MediaKind.Video).ProposedName);
    }
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~MediaNamerTests|FullyQualifiedName~NormalizationEngineTests"`
Expected : erreur de compilation (`LivePhotoConflict` n'existe pas sur `NamingResult`). C'est l'échec attendu.

- [ ] **Step 3: Étendre `NamingResult`**

Dans `PhotoApp.Core/Normalization/MediaNamer.cs`, remplacer le record :

```csharp
/// <param name="LivePhotoPartner">Nom de fichier actuel de l'autre moitié de la Live Photo.</param>
/// <param name="LivePhotoConflict">Un couple Live Photo a été dissous : les dates de la photo et de la vidéo divergent.</param>
public sealed record NamingResult(string? ProposedName, string? LivePhotoPartner, bool LivePhotoConflict = false);
```

- [ ] **Step 4: Dissoudre les couples en conflit**

Dans `NameFolder`, juste après `var livePairs = PairLivePhotos(inputs, indexes);`, ajouter :

```csharp
        var conflicts = new HashSet<int>();
        foreach (var (video, photo) in livePairs.ToList())
        {
            if (DatesDisagree(inputs[video], inputs[photo]))
            {
                livePairs.Remove(video);
                conflicts.Add(video);
                conflicts.Add(photo);
            }
        }

```

À la toute fin de `NameFolder` (après la boucle `foreach (var (video, photo) in livePairs)`), ajouter :

```csharp

        foreach (var index in conflicts)
        {
            results[index] = results[index] with { LivePhotoConflict = true };
        }
```

Ajouter la méthode :

```csharp
    // Une vidéo qui a sa propre décision applicable, en désaccord avec la date de la photo : ce n'est pas la même prise.
    // Sans décision applicable, elle suit sa photo (comportement historique).
    private static bool DatesDisagree(NamingInput video, NamingInput photo) =>
        video.Decision is { IsApplicable: true, Date: { } videoDate }
        && photo.Decision.Date is { } photoDate
        && !DateComparison.Concordant(videoDate, photoDate);
```

- [ ] **Step 5: Exiger des identifiants concordants pour le repli par nom de base**

Dans `PairLivePhotos`, remplacer la recherche du second `foreach` :

```csharp
            var photo = photos.FirstOrDefault(
                p => !paired.Contains(p) && string.Equals(Path.GetFileNameWithoutExtension(inputs[p].FullPath), stem, StringComparison.OrdinalIgnoreCase),
                -1);
```

par :

```csharp
            var photo = photos.FirstOrDefault(
                p => !paired.Contains(p)
                     && string.Equals(Path.GetFileNameWithoutExtension(inputs[p].FullPath), stem, StringComparison.OrdinalIgnoreCase)
                     && IdentifiersCompatible(inputs[video].ContentIdentifier, inputs[p].ContentIdentifier),
                -1);
```

et ajouter la méthode :

```csharp
    // Deux identifiants Apple différents désignent deux prises distinctes, même sous un nom de base commun.
    private static bool IdentifiersCompatible(string? first, string? second) =>
        first is null || second is null || string.Equals(first, second, StringComparison.Ordinal);
```

- [ ] **Step 6: Poser le drapeau dans le moteur**

Dans `PhotoApp.Core/Normalization/NormalizationEngine.cs`, après le bloc `SourceWarnings` ajouté à la tâche 1 (avant `return new MediaResult(...)`), ajouter :

```csharp
                if (names[i].LivePhotoConflict)
                {
                    decision = decision with { Flags = decision.Flags | DecisionFlags.LivePhotoConflict };
                }

```

- [ ] **Step 7: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx`
Expected : PASS (les tests Live Photo existants, `LivePhoto_ByContentIdentifier_VideoFollowsThePhoto`, `LivePhoto_BySameBaseName` et `LivePhoto_WithoutAPhotoName_LeavesTheVideoUnnamed`, restent verts : leurs dates concordent à ±2 s).

- [ ] **Step 8: Commit**

```bash
git add PhotoApp.Core PhotoApp.Tests
git commit -m "fix: Live Photo, identifiants concordants et dates d'accord (sinon chaque fichier suit sa décision, conflit signalé)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---
### Task 4: Modèle d'un lot, interface du journal, noms de sidecars (Core)

**Files:**
- Create: `PhotoApp.Core/Batches/BatchModel.cs`
- Create: `PhotoApp.Core/Batches/IBatchJournal.cs`
- Create: `PhotoApp.Core/Batches/SidecarNames.cs`
- Test: `PhotoApp.Tests/SidecarNamesTests.cs`

**Interfaces:**
- Produces (utilisé par toutes les tâches suivantes) : voir le code ci-dessous, à recopier tel quel.

- [ ] **Step 1: Écrire le test des noms de sidecars**

`PhotoApp.Tests/SidecarNamesTests.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

public sealed class SidecarNamesTests
{
    [Theory]
    [InlineData("holiday.jpg", "IMG-20230304-132015.jpg", "holiday.xmp", "IMG-20230304-132015.xmp")]
    [InlineData("holiday.jpg", "IMG-20230304-132015.jpg", "holiday.jpg.xmp", "IMG-20230304-132015.jpg.xmp")]
    [InlineData("IMG_0477.HEIC", "IMG-20240105-150412.heic", "IMG_0477.AAE", "IMG-20240105-150412.AAE")]
    [InlineData("a.jpg", "IMG-1.jpg", "A.XMP", "IMG-1.XMP")]
    public void Rename_FollowsTheConventionOfTheOriginalSidecar(string oldMedia, string newMedia, string sidecar, string expected)
    {
        Assert.Equal(expected, SidecarNames.Rename(oldMedia, newMedia, sidecar));
    }

    [Fact]
    public void Rename_UnrelatedSidecar_ReturnsNull()
    {
        Assert.Null(SidecarNames.Rename("holiday.jpg", "IMG-20230304-132015.jpg", "other.xmp"));
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~SidecarNamesTests"`
Expected : erreur de compilation (`SidecarNames` n'existe pas).

- [ ] **Step 3: Écrire le modèle**

`PhotoApp.Core/Batches/BatchModel.cs` :

```csharp
namespace PhotoApp.Core.Batches;

/// <summary>États d'un lot (spec de phase 2, §5.3).</summary>
public enum BatchState
{
    Planned,
    Running,
    Completed,
    Interrupted,
    Undone
}

/// <summary>États d'un fichier du lot. <see cref="UndoFailed"/> : l'annulation n'a pas pu le restaurer, elle peut être retentée.</summary>
public enum ItemState
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped,
    Undone,
    UndoFailed
}

public enum ItemRole
{
    Media,
    LivePartner,
    Sidecar
}

/// <summary>Un fichier à renommer, tel qu'il est figé au plan. Les dates sont en UTC.</summary>
/// <param name="Rank">Rang dans le lot, à partir de 0 : l'ordre d'exécution.</param>
/// <param name="GroupId">Média, partenaire Live Photo et sidecars d'un même groupe : tout ou rien au plan.</param>
/// <param name="DecidedDate">Instant que prendront les dates de création et de modification.</param>
public sealed record BatchItemDraft(
    int Rank,
    int GroupId,
    ItemRole Role,
    string SourcePath,
    string TargetPath,
    DateTimeOffset DecidedDate,
    long SourceSize,
    DateTime SourceLastWriteUtc,
    DateTime SourceCreationUtc);

/// <param name="Error">Message d'échec, ou avertissement d'un élément fait.</param>
/// <param name="DoneSize">Taille du fichier après renommage : sert à détecter une modification avant l'annulation.</param>
/// <param name="DoneLastWriteUtc">Date de modification après renommage (idem).</param>
public sealed record BatchItem(
    long Id,
    long BatchId,
    int Rank,
    int GroupId,
    ItemRole Role,
    string SourcePath,
    string TargetPath,
    ItemState State,
    string? Error,
    DateTimeOffset DecidedDate,
    long SourceSize,
    DateTime SourceLastWriteUtc,
    DateTime SourceCreationUtc,
    long? DoneSize,
    DateTime? DoneLastWriteUtc);

public sealed record BatchCounts(int Total, int Pending, int Running, int Done, int Failed, int Skipped, int Undone, int UndoFailed);

public sealed record BatchInfo(long Id, DateTime CreatedUtc, IReadOnlyList<string> Folders, BatchState State, BatchCounts Counts);
```

`PhotoApp.Core/Batches/IBatchJournal.cs` :

```csharp
namespace PhotoApp.Core.Batches;

/// <summary>Le journal des lots (spec de phase 2, §6). Chaque changement d'état est durable avant que l'action suivante commence.</summary>
public interface IBatchJournal
{
    /// <summary>Enregistre un lot à l'état <see cref="BatchState.Planned"/>, tous ses éléments <see cref="ItemState.Pending"/>.</summary>
    long CreateBatch(DateTime createdUtc, IReadOnlyList<string> folders, IReadOnlyList<BatchItemDraft> items);

    BatchInfo? GetBatch(long batchId);

    /// <summary>Tous les lots, du plus ancien au plus récent.</summary>
    IReadOnlyList<BatchInfo> ListBatches();

    /// <summary>Le lot le plus récent à l'état <see cref="BatchState.Running"/> ou <see cref="BatchState.Interrupted"/>, s'il existe.</summary>
    BatchInfo? FindUnfinished();

    /// <summary>Le lot terminé le plus récent qui a encore des éléments à restaurer (faits ou en échec d'annulation).</summary>
    BatchInfo? FindLastUndoable();

    /// <summary>Les éléments d'un lot, dans l'ordre d'exécution.</summary>
    IReadOnlyList<BatchItem> GetItems(long batchId);

    void SetBatchState(long batchId, BatchState state);

    void SetItemState(long itemId, ItemState state, string? error = null);

    /// <summary>Marque l'élément fait et enregistre l'état du fichier renommé ; <paramref name="warning"/> : avertissement éventuel.</summary>
    void SetItemDone(long itemId, long doneSize, DateTime doneLastWriteUtc, string? warning = null);

    /// <summary>Un seul exécuteur ou annulateur à la fois. Lève <see cref="BatchLockedException"/> si un autre traitement est en cours.</summary>
    IDisposable AcquireExecutionLock();
}

public sealed class BatchLockedException(string message, Exception? inner = null) : Exception(message, inner);
```

`PhotoApp.Core/Batches/SidecarNames.cs` :

```csharp
namespace PhotoApp.Core.Batches;

/// <summary>Nom d'un sidecar (.xmp, .aae) d'après le nouveau nom de son média, en gardant la convention du sidecar d'origine.</summary>
public static class SidecarNames
{
    /// <summary>
    /// <c>photo.jpg.xmp</c> suit le nom complet du média ; <c>photo.xmp</c> et <c>IMG_0477.AAE</c> suivent son nom de base.
    /// Null si le sidecar n'appartient pas à ce média.
    /// </summary>
    public static string? Rename(string oldMediaName, string newMediaName, string sidecarName)
    {
        if (sidecarName.StartsWith(oldMediaName + ".", StringComparison.OrdinalIgnoreCase))
        {
            return newMediaName + sidecarName[oldMediaName.Length..];
        }

        var oldStem = Path.GetFileNameWithoutExtension(oldMediaName);
        if (string.Equals(Path.GetFileNameWithoutExtension(sidecarName), oldStem, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(newMediaName) + Path.GetExtension(sidecarName);
        }

        return null;
    }
}
```

- [ ] **Step 4: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~SidecarNamesTests"`
Expected : PASS (5 cas).

- [ ] **Step 5: Commit**

```bash
git add PhotoApp.Core PhotoApp.Tests
git commit -m "feat: modèle d'un lot, interface du journal et noms de sidecars" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Base SQLite et journal des lots (Catalog)

**Files:**
- Modify: `PhotoApp.Catalog/PhotoApp.Catalog.csproj`
- Create: `PhotoApp.Catalog/CatalogDatabase.cs`
- Create: `PhotoApp.Catalog/SqliteBatchJournal.cs`
- Test: `PhotoApp.Tests/CatalogDatabaseTests.cs`, `PhotoApp.Tests/SqliteBatchJournalTests.cs`

**Interfaces:**
- Consumes : `IBatchJournal`, `BatchItemDraft`, `BatchItem`, `BatchInfo`, `BatchCounts`, `BatchState`, `ItemState`, `ItemRole`, `BatchLockedException` (tâche 4).
- Produces : `new CatalogDatabase(string databasePath)` (le dossier doit exister) avec `DatabasePath : string` et `Open() : SqliteConnection` ; `new SqliteBatchJournal(CatalogDatabase)` : `IBatchJournal`. La table `quarantine_entry` est créée ici et utilisée à la tâche 10.

- [ ] **Step 1: Ajouter le paquet**

`PhotoApp.Catalog/PhotoApp.Catalog.csproj`, remplacer

```xml
  <ItemGroup>
    <ProjectReference Include="..\PhotoApp.Core\PhotoApp.Core.csproj" />
    <ProjectReference Include="..\PhotoApp.Metadata\PhotoApp.Metadata.csproj" />
  </ItemGroup>
```

par

```xml
  <ItemGroup>
    <ProjectReference Include="..\PhotoApp.Core\PhotoApp.Core.csproj" />
    <ProjectReference Include="..\PhotoApp.Metadata\PhotoApp.Metadata.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  </ItemGroup>
```

Run : `dotnet restore PhotoVisualizer.slnx`
Expected : restauration réussie. Si la version `10.0.0` est introuvable, utiliser `dotnet add PhotoApp.Catalog package Microsoft.Data.Sqlite --version 10.*` et noter la version retenue dans le README (tâche 15).

- [ ] **Step 2: Écrire les tests de la base**

`PhotoApp.Tests/CatalogDatabaseTests.cs` :

```csharp

namespace PhotoApp.Tests;

public sealed class CatalogDatabaseTests
{
    [Fact]
    public void Constructor_CreatesTheSchema_AndRecordsItsVersion()
    {
        using var temp = new TempDirectory();
        var database = new CatalogDatabase(temp.Combine("catalog.db"));

        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_version";
        Assert.Equal(CatalogDatabase.SchemaVersion, Convert.ToInt32(command.ExecuteScalar()));
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('batch', 'batch_item', 'quarantine_entry')";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        Assert.Equal(["batch", "batch_item", "quarantine_entry"], tables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Constructor_OnAnExistingDatabase_KeepsItsContent()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("catalog.db");
        var first = new CatalogDatabase(path);
        using (var connection = first.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO batch (created_utc, folders, state) VALUES ('2026-10-19T10:00:00.0000000Z', '[]', 'Completed')";
            command.ExecuteNonQuery();
        }

        var second = new CatalogDatabase(path);

        using var reopened = second.Open();
        using var count = reopened.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM batch";
        Assert.Equal(1L, count.ExecuteScalar());
    }

    [Fact]
    public void Constructor_MissingFolder_Throws()
    {
        using var temp = new TempDirectory();

        Assert.Throws<DirectoryNotFoundException>(() => new CatalogDatabase(temp.Combine("absent", "catalog.db")));
    }

    [Fact]
    public void Constructor_DatabaseFromAFutureVersion_Throws()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("catalog.db");
        var database = new CatalogDatabase(path);
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"UPDATE schema_version SET version = {CatalogDatabase.SchemaVersion + 1}";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => new CatalogDatabase(path));
    }
}
```

- [ ] **Step 3: Écrire les tests du journal**

`PhotoApp.Tests/SqliteBatchJournalTests.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

public sealed class SqliteBatchJournalTests
{
    private static readonly DateTime Created = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static SqliteBatchJournal NewJournal(TempDirectory temp) => new(new CatalogDatabase(temp.Combine("catalog.db")));

    private static BatchItemDraft Draft(int rank, int group = 1, ItemRole role = ItemRole.Media) => new(
        rank,
        group,
        role,
        $@"C:\photos\a{rank}.jpg",
        $@"C:\photos\IMG-{rank}.jpg",
        new DateTimeOffset(2023, 3, 4, 13, 20, 15, TimeSpan.FromHours(1)),
        100 + rank,
        new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        new DateTime(2024, 1, 1, 3, 4, 5, DateTimeKind.Utc));

    [Fact]
    public void CreateBatch_StoresTheBatchAndItsItems_AsPending()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);

        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0), Draft(1, role: ItemRole.Sidecar)]);

        var batch = journal.GetBatch(id)!;
        Assert.Equal(BatchState.Planned, batch.State);
        Assert.Equal(Created, batch.CreatedUtc);
        Assert.Equal([@"C:\photos"], batch.Folders);
        Assert.Equal(new BatchCounts(2, 2, 0, 0, 0, 0, 0, 0), batch.Counts);

        var items = journal.GetItems(id);
        Assert.Equal([0, 1], items.Select(i => i.Rank));
        Assert.Equal(ItemRole.Sidecar, items[1].Role);
        Assert.All(items, i => Assert.Equal(ItemState.Pending, i.State));
        var first = items[0];
        var draft = Draft(0);
        Assert.Equal(draft.SourcePath, first.SourcePath);
        Assert.Equal(draft.TargetPath, first.TargetPath);
        Assert.Equal(draft.DecidedDate, first.DecidedDate);
        Assert.Equal(draft.DecidedDate.Offset, first.DecidedDate.Offset);
        Assert.Equal(draft.SourceSize, first.SourceSize);
        Assert.Equal(draft.SourceLastWriteUtc, first.SourceLastWriteUtc);
        Assert.Equal(draft.SourceCreationUtc, first.SourceCreationUtc);
        Assert.Null(first.DoneSize);
        Assert.Null(first.DoneLastWriteUtc);
    }

    [Fact]
    public void GetBatch_UnknownId_ReturnsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(NewJournal(temp).GetBatch(42));
    }

    [Fact]
    public void SetItemState_ChangesTheCounts_AndKeepsTheError()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0), Draft(1), Draft(2)]);
        var items = journal.GetItems(id);

        journal.SetItemState(items[0].Id, ItemState.Running);
        journal.SetItemState(items[1].Id, ItemState.Failed, "verrouillé");
        journal.SetItemState(items[2].Id, ItemState.Skipped, "introuvable");

        var after = journal.GetItems(id);
        Assert.Equal("verrouillé", after[1].Error);
        Assert.Equal(new BatchCounts(3, 0, 1, 0, 1, 1, 0, 0), journal.GetBatch(id)!.Counts);
    }

    [Fact]
    public void SetItemDone_RecordsTheOutcome()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        var item = journal.GetItems(id).Single();
        var written = new DateTime(2023, 3, 4, 12, 20, 15, DateTimeKind.Utc);

        journal.SetItemDone(item.Id, 123, written, "dates non appliquées");

        var done = journal.GetItems(id).Single();
        Assert.Equal(ItemState.Done, done.State);
        Assert.Equal(123, done.DoneSize);
        Assert.Equal(written, done.DoneLastWriteUtc);
        Assert.Equal("dates non appliquées", done.Error);
    }

    [Fact]
    public void SetBatchState_IsPersisted_AcrossReopening()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var id = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetBatchState(id, BatchState.Running);

        var reopened = NewJournal(temp);

        Assert.Equal(BatchState.Running, reopened.GetBatch(id)!.State);
        Assert.Equal(id, reopened.FindUnfinished()?.Id);
    }

    [Fact]
    public void FindUnfinished_ReturnsTheMostRecentRunningOrInterruptedBatch()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var completed = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetBatchState(completed, BatchState.Completed);
        var interrupted = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetBatchState(interrupted, BatchState.Interrupted);

        Assert.Equal(interrupted, journal.FindUnfinished()?.Id);

        journal.SetBatchState(interrupted, BatchState.Undone);
        Assert.Null(journal.FindUnfinished());
    }

    [Fact]
    public void FindLastUndoable_SkipsBatchesWithoutDoneItems_AndUndoneBatches()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var first = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetItemDone(journal.GetItems(first).Single().Id, 1, Created);
        journal.SetBatchState(first, BatchState.Completed);
        var emptyOutcome = journal.CreateBatch(Created, [@"C:\photos"], [Draft(0)]);
        journal.SetItemState(journal.GetItems(emptyOutcome).Single().Id, ItemState.Failed, "x");
        journal.SetBatchState(emptyOutcome, BatchState.Completed);

        Assert.Equal(first, journal.FindLastUndoable()?.Id);

        journal.SetBatchState(first, BatchState.Undone);
        Assert.Null(journal.FindLastUndoable());
    }

    [Fact]
    public void ListBatches_AreOrderedFromTheOldestToTheNewest()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);
        var first = journal.CreateBatch(Created, [@"C:\a"], [Draft(0)]);
        var second = journal.CreateBatch(Created, [@"C:\b"], [Draft(0)]);

        Assert.Equal([first, second], journal.ListBatches().Select(b => b.Id));
    }

    [Fact]
    public void ExecutionLock_IsExclusive_UntilReleased()
    {
        using var temp = new TempDirectory();
        var journal = NewJournal(temp);

        var held = journal.AcquireExecutionLock();
        Assert.Throws<BatchLockedException>(() => journal.AcquireExecutionLock());

        held.Dispose();
        using var again = journal.AcquireExecutionLock();
    }
}
```

- [ ] **Step 4: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~CatalogDatabaseTests|FullyQualifiedName~SqliteBatchJournalTests"`
Expected : erreur de compilation (`CatalogDatabase` et `SqliteBatchJournal` n'existent pas).

- [ ] **Step 5: Écrire `CatalogDatabase`**

`PhotoApp.Catalog/CatalogDatabase.cs` :

```csharp
using Microsoft.Data.Sqlite;

namespace PhotoApp.Catalog;

/// <summary>
/// La base SQLite de l'application (<c>catalog.db</c>, spec de phase 2, §6), en mode WAL. Une connexion par opération,
/// sans pool : aucune poignée ne reste ouverte sur le fichier. Le dossier de la base doit exister (<c>AppDataPaths.EnsureCreated</c>).
/// </summary>
public sealed class CatalogDatabase
{
    public const int SchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);

        CREATE TABLE IF NOT EXISTS batch (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_utc TEXT NOT NULL,
            folders TEXT NOT NULL,
            state TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS batch_item (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            batch_id INTEGER NOT NULL REFERENCES batch(id),
            rank INTEGER NOT NULL,
            group_id INTEGER NOT NULL,
            role TEXT NOT NULL,
            source_path TEXT NOT NULL,
            target_path TEXT NOT NULL,
            state TEXT NOT NULL,
            error TEXT NULL,
            decided_date TEXT NOT NULL,
            source_size INTEGER NOT NULL,
            source_last_write_utc TEXT NOT NULL,
            source_creation_utc TEXT NOT NULL,
            done_size INTEGER NULL,
            done_last_write_utc TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_batch_item_batch ON batch_item(batch_id, rank);

        CREATE TABLE IF NOT EXISTS quarantine_entry (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            created_utc TEXT NOT NULL,
            original_path TEXT NOT NULL,
            stored_path TEXT NOT NULL,
            batch_id INTEGER NULL,
            state TEXT NOT NULL
        );
        """;

    private readonly string _connectionString;

    public CatalogDatabase(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(DatabasePath)!;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Le dossier de la base de données n'existe pas : {directory}");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        Initialize();
    }

    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        Execute(connection, "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;");
        return connection;
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void Initialize()
    {
        using var connection = Open();
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, Schema);

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT version FROM schema_version";
        var existing = read.ExecuteScalar();
        if (existing is null)
        {
            Execute(connection, $"INSERT INTO schema_version (version) VALUES ({SchemaVersion})");
        }
        else if (Convert.ToInt32(existing) > SchemaVersion)
        {
            throw new InvalidOperationException("La base de données a été créée par une version plus récente de PhotoManager.");
        }
    }
}
```

- [ ] **Step 6: Écrire `SqliteBatchJournal`**

`PhotoApp.Catalog/SqliteBatchJournal.cs` :

```csharp
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PhotoApp.Core.Batches;

namespace PhotoApp.Catalog;

/// <summary>Le journal des lots sur SQLite. Chaque méthode est une transaction courte : un plantage ne laisse jamais une ligne à moitié écrite.</summary>
public sealed class SqliteBatchJournal(CatalogDatabase database) : IBatchJournal
{
    private const string ItemColumns =
        "id, batch_id, rank, group_id, role, source_path, target_path, state, error, decided_date, " +
        "source_size, source_last_write_utc, source_creation_utc, done_size, done_last_write_utc";

    public long CreateBatch(DateTime createdUtc, IReadOnlyList<string> folders, IReadOnlyList<BatchItemDraft> items)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        long batchId;
        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO batch (created_utc, folders, state) VALUES ($created, $folders, $state); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$created", Text(createdUtc));
            insert.Parameters.AddWithValue("$folders", JsonSerializer.Serialize(folders));
            insert.Parameters.AddWithValue("$state", BatchState.Planned.ToString());
            batchId = (long)insert.ExecuteScalar()!;
        }

        using (var insertItem = connection.CreateCommand())
        {
            insertItem.Transaction = transaction;
            insertItem.CommandText =
                "INSERT INTO batch_item (batch_id, rank, group_id, role, source_path, target_path, state, decided_date, " +
                "source_size, source_last_write_utc, source_creation_utc) " +
                "VALUES ($batch, $rank, $group, $role, $source, $target, $state, $decided, $size, $write, $creation)";
            var batch = insertItem.Parameters.Add("$batch", SqliteType.Integer);
            var rank = insertItem.Parameters.Add("$rank", SqliteType.Integer);
            var group = insertItem.Parameters.Add("$group", SqliteType.Integer);
            var role = insertItem.Parameters.Add("$role", SqliteType.Text);
            var source = insertItem.Parameters.Add("$source", SqliteType.Text);
            var target = insertItem.Parameters.Add("$target", SqliteType.Text);
            var state = insertItem.Parameters.Add("$state", SqliteType.Text);
            var decided = insertItem.Parameters.Add("$decided", SqliteType.Text);
            var size = insertItem.Parameters.Add("$size", SqliteType.Integer);
            var write = insertItem.Parameters.Add("$write", SqliteType.Text);
            var creation = insertItem.Parameters.Add("$creation", SqliteType.Text);

            foreach (var item in items)
            {
                batch.Value = batchId;
                rank.Value = item.Rank;
                group.Value = item.GroupId;
                role.Value = item.Role.ToString();
                source.Value = item.SourcePath;
                target.Value = item.TargetPath;
                state.Value = ItemState.Pending.ToString();
                decided.Value = item.DecidedDate.ToString("O", CultureInfo.InvariantCulture);
                size.Value = item.SourceSize;
                write.Value = Text(item.SourceLastWriteUtc);
                creation.Value = Text(item.SourceCreationUtc);
                insertItem.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return batchId;
    }

    public BatchInfo? GetBatch(long batchId)
    {
        using var connection = database.Open();
        return QueryBatches(connection, "WHERE id = $id", ("$id", batchId)).FirstOrDefault();
    }

    public IReadOnlyList<BatchInfo> ListBatches()
    {
        using var connection = database.Open();
        return QueryBatches(connection, "ORDER BY id");
    }

    public BatchInfo? FindUnfinished()
    {
        using var connection = database.Open();
        return QueryBatches(connection, "WHERE state IN ('Running', 'Interrupted') ORDER BY id DESC LIMIT 1").FirstOrDefault();
    }

    public BatchInfo? FindLastUndoable()
    {
        using var connection = database.Open();
        return QueryBatches(
            connection,
            "WHERE state = 'Completed' AND EXISTS (SELECT 1 FROM batch_item i WHERE i.batch_id = batch.id AND i.state IN ('Done', 'UndoFailed')) " +
            "ORDER BY id DESC LIMIT 1").FirstOrDefault();
    }

    public IReadOnlyList<BatchItem> GetItems(long batchId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {ItemColumns} FROM batch_item WHERE batch_id = $id ORDER BY rank";
        command.Parameters.AddWithValue("$id", batchId);

        var items = new List<BatchItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new BatchItem(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                Enum.Parse<ItemRole>(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                Enum.Parse<ItemState>(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                reader.GetInt64(10),
                ParseUtc(reader.GetString(11)),
                ParseUtc(reader.GetString(12)),
                reader.IsDBNull(13) ? null : reader.GetInt64(13),
                reader.IsDBNull(14) ? null : ParseUtc(reader.GetString(14))));
        }

        return items;
    }

    public void SetBatchState(long batchId, BatchState state) =>
        Execute("UPDATE batch SET state = $state WHERE id = $id", ("$state", state.ToString()), ("$id", batchId));

    public void SetItemState(long itemId, ItemState state, string? error = null) =>
        Execute(
            "UPDATE batch_item SET state = $state, error = $error WHERE id = $id",
            ("$state", state.ToString()),
            ("$error", (object?)error ?? DBNull.Value),
            ("$id", itemId));

    public void SetItemDone(long itemId, long doneSize, DateTime doneLastWriteUtc, string? warning = null) =>
        Execute(
            "UPDATE batch_item SET state = 'Done', error = $error, done_size = $size, done_last_write_utc = $write WHERE id = $id",
            ("$error", (object?)warning ?? DBNull.Value),
            ("$size", doneSize),
            ("$write", Text(doneLastWriteUtc)),
            ("$id", itemId));

    public IDisposable AcquireExecutionLock()
    {
        var path = database.DatabasePath + ".lock";
        try
        {
            // Ouvert sans partage : un second processus échoue, et le système libère le verrou si celui-ci est tué.
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException ex)
        {
            throw new BatchLockedException($"Un autre traitement PhotoManager est en cours (verrou : {path}).", ex);
        }
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static List<BatchInfo> QueryBatches(SqliteConnection connection, string clause, params (string Name, object Value)[] parameters)
    {
        var rows = new List<(long Id, DateTime Created, IReadOnlyList<string> Folders, BatchState State)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, created_utc, folders, state FROM batch " + clause;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetInt64(0),
                    ParseUtc(reader.GetString(1)),
                    JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [],
                    Enum.Parse<BatchState>(reader.GetString(3))));
            }
        }

        return rows.Select(row => new BatchInfo(row.Id, row.Created, row.Folders, row.State, CountsOf(connection, row.Id))).ToList();
    }

    private static BatchCounts CountsOf(SqliteConnection connection, long batchId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, COUNT(*) FROM batch_item WHERE batch_id = $id GROUP BY state";
        command.Parameters.AddWithValue("$id", batchId);

        var byState = new Dictionary<ItemState, int>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            byState[Enum.Parse<ItemState>(reader.GetString(0))] = reader.GetInt32(1);
        }

        int Get(ItemState state) => byState.GetValueOrDefault(state);
        return new BatchCounts(
            byState.Values.Sum(),
            Get(ItemState.Pending),
            Get(ItemState.Running),
            Get(ItemState.Done),
            Get(ItemState.Failed),
            Get(ItemState.Skipped),
            Get(ItemState.Undone),
            Get(ItemState.UndoFailed));
    }

    // Dates stockées en texte ISO 8601 aller-retour : les 100 ns d'un système de fichiers NTFS sont conservées.
    private static string Text(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
```

- [ ] **Step 7: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~CatalogDatabaseTests|FullyQualifiedName~SqliteBatchJournalTests"`
Expected : PASS (13 tests). Si `TempDirectory.Dispose` échoue parce que `catalog.db-wal` est verrouillé, c'est qu'une connexion est restée ouverte : vérifier que chaque `Open()` est dans un `using` et que `Pooling = false`.

- [ ] **Step 8: Commit**

```bash
git add PhotoApp.Catalog PhotoApp.Tests
git commit -m "feat: base SQLite du catalogue et journal des lots (verrou d'exécution, schéma versionné)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Dates du fichier, déplacement sans écrasement, contrôles préalables (Operations)

**Files:**
- Create: `PhotoApp.Operations/FileTimestamps.cs`
- Create: `PhotoApp.Operations/FileMoves.cs`
- Create: `PhotoApp.Operations/BatchPreflight.cs`
- Test: `PhotoApp.Tests/FileTimestampsTests.cs`, `PhotoApp.Tests/FileMovesTests.cs`, `PhotoApp.Tests/BatchPreflightTests.cs`

**Interfaces:**
- Produces : `FileTimes(DateTime CreationUtc, DateTime LastWriteUtc)` ; `FileTimestamps.Read(string) : FileTimes` ; `FileTimestamps.Set(string path, DateTime creationUtc, DateTime lastWriteUtc)` ; `FileMoves.Move(string source, string target)` (lève `IOException` si la cible existe, sauf changement de casse seul) ; `PreflightEntry(int Rank, int GroupId, string SourcePath, string TargetPath, long SourceSize, DateTime SourceLastWriteUtc)` ; `BatchPreflight.Evaluate(IReadOnlyList<PreflightEntry>) : IReadOnlyDictionary<int, string>` (rang → raison de l'écart) ; `BatchPreflight.Check(PreflightEntry) : string?`.

- [ ] **Step 1: Écrire les tests**

`PhotoApp.Tests/FileTimestampsTests.cs` :

```csharp
namespace PhotoApp.Tests;

public sealed class FileTimestampsTests
{
    [Fact]
    public void Set_ThenRead_RoundTripsCreationAndModification()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("a.jpg");
        File.WriteAllText(path, "x");
        var creation = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var write = new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        FileTimestamps.Set(path, creation, write);
        var times = FileTimestamps.Read(path);

        Assert.Equal(creation, times.CreationUtc);
        Assert.Equal(write, times.LastWriteUtc);
    }
}
```

`PhotoApp.Tests/FileMovesTests.cs` :

```csharp
namespace PhotoApp.Tests;

public sealed class FileMovesTests
{
    [Fact]
    public void Move_RenamesTheFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "contenu");

        FileMoves.Move(temp.Combine("a.jpg"), temp.Combine("b.jpg"));

        Assert.False(File.Exists(temp.Combine("a.jpg")));
        Assert.Equal("contenu", File.ReadAllText(temp.Combine("b.jpg")));
    }

    [Fact]
    public void Move_ToAnExistingFile_Throws_AndKeepsBothFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "A");
        File.WriteAllText(temp.Combine("b.jpg"), "B");

        Assert.Throws<IOException>(() => FileMoves.Move(temp.Combine("a.jpg"), temp.Combine("b.jpg")));

        Assert.Equal("A", File.ReadAllText(temp.Combine("a.jpg")));
        Assert.Equal("B", File.ReadAllText(temp.Combine("b.jpg")));
    }

    [Fact]
    public void Move_CaseOnlyChange_RenamesTheFile_AndLeavesNoTemporaryFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("IMG-1.JPG"), "contenu");

        FileMoves.Move(temp.Combine("IMG-1.JPG"), temp.Combine("IMG-1.jpg"));

        var names = Directory.GetFiles(temp.FullPath).Select(Path.GetFileName).ToList();
        Assert.Equal(["IMG-1.jpg"], names);
    }
}
```

`PhotoApp.Tests/BatchPreflightTests.cs` :

```csharp
namespace PhotoApp.Tests;

public sealed class BatchPreflightTests
{
    private static PreflightEntry EntryFor(string source, string target, int rank = 0, int group = 1)
    {
        var info = new FileInfo(source);
        return new PreflightEntry(rank, group, source, target, info.Length, info.LastWriteTimeUtc);
    }

    [Fact]
    public void Check_UntouchedFileWithAFreeTarget_PassesTheChecks()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");

        Assert.Null(BatchPreflight.Check(EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"))));
    }

    [Fact]
    public void Check_MissingSource_IsRejected()
    {
        using var temp = new TempDirectory();
        var entry = new PreflightEntry(0, 1, temp.Combine("absent.jpg"), temp.Combine("IMG-1.jpg"), 1, DateTime.UtcNow);

        Assert.Equal("Fichier introuvable", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Check_SourceChangedSinceThePlan_IsRejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        var entry = EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"));

        File.WriteAllText(temp.Combine("a.jpg"), "contenu plus long");

        Assert.Equal("Fichier modifié depuis le plan", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Check_TargetAlreadyTaken_IsRejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        File.WriteAllText(temp.Combine("IMG-1.jpg"), "autre");

        Assert.Equal("Le nom cible existe déjà", BatchPreflight.Check(EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"))));
    }

    [Fact]
    public void Check_CaseOnlyChange_IsNotATargetCollision()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("IMG-1.JPG"), "x");

        Assert.Null(BatchPreflight.Check(EntryFor(temp.Combine("IMG-1.JPG"), temp.Combine("IMG-1.jpg"))));
    }

    [Fact]
    public void Check_LockedFile_IsRejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        var entry = EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"));

        using var held = new FileStream(temp.Combine("a.jpg"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal("Fichier verrouillé", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Check_TooLongTargetPath_IsRejectedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        var entry = EntryFor(temp.Combine("a.jpg"), temp.Combine(new string('n', 260) + ".jpg"));

        Assert.Equal("Chemin trop long", BatchPreflight.Check(entry));
    }

    [Fact]
    public void Evaluate_AGroupIsAllOrNothing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("a.jpg"), "x");
        File.WriteAllText(temp.Combine("a.xmp"), "x");
        File.WriteAllText(temp.Combine("b.jpg"), "x");
        File.WriteAllText(temp.Combine("IMG-1.xmp"), "déjà là");

        var reasons = BatchPreflight.Evaluate(
        [
            EntryFor(temp.Combine("a.jpg"), temp.Combine("IMG-1.jpg"), rank: 0, group: 1),
            EntryFor(temp.Combine("a.xmp"), temp.Combine("IMG-1.xmp"), rank: 1, group: 1),
            EntryFor(temp.Combine("b.jpg"), temp.Combine("IMG-2.jpg"), rank: 2, group: 2)
        ]);

        Assert.Equal([0, 1], reasons.Keys.Order());
        Assert.Equal("Le nom cible existe déjà", reasons[1]);
        Assert.StartsWith("Groupe écarté : a.xmp", reasons[0]);
        Assert.False(reasons.ContainsKey(2));
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~FileTimestampsTests|FullyQualifiedName~FileMovesTests|FullyQualifiedName~BatchPreflightTests"`
Expected : erreur de compilation (`FileTimestamps`, `FileMoves`, `BatchPreflight` n'existent pas).

- [ ] **Step 3: Écrire `FileTimestamps` et `FileMoves`**

`PhotoApp.Operations/FileTimestamps.cs` :

```csharp
namespace PhotoApp.Operations;

public readonly record struct FileTimes(DateTime CreationUtc, DateTime LastWriteUtc);

/// <summary>Dates de création et de modification d'un fichier, lues et écrites ensemble (le renommage sur place ne touche pas à la date d'accès).</summary>
public static class FileTimestamps
{
    public static FileTimes Read(string path)
    {
        var info = new FileInfo(path);
        return new FileTimes(info.CreationTimeUtc, info.LastWriteTimeUtc);
    }

    public static void Set(string path, DateTime creationUtc, DateTime lastWriteUtc)
    {
        File.SetCreationTimeUtc(path, creationUtc);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
```

`PhotoApp.Operations/FileMoves.cs` :

```csharp
namespace PhotoApp.Operations;

public static class FileMoves
{
    /// <summary>
    /// Renomme un fichier sans jamais écraser : lève <see cref="IOException"/> si la cible existe. Un changement de casse seul
    /// (<c>IMG-1.JPG</c> → <c>IMG-1.jpg</c>) passe par un nom voisin, pour ne dépendre d'aucun comportement propre au système de fichiers.
    /// </summary>
    public static void Move(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, target, StringComparison.Ordinal))
        {
            var temporary = source + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.Move(source, temporary, overwrite: false);
            File.Move(temporary, target, overwrite: false);
            return;
        }

        File.Move(source, target, overwrite: false);
    }
}
```

- [ ] **Step 4: Écrire `BatchPreflight`**

`PhotoApp.Operations/BatchPreflight.cs` :

```csharp
namespace PhotoApp.Operations;

/// <param name="Rank">Rang de l'élément dans le lot : la clé du résultat.</param>
public sealed record PreflightEntry(int Rank, int GroupId, string SourcePath, string TargetPath, long SourceSize, DateTime SourceLastWriteUtc);

/// <summary>Contrôles préalables à l'exécution (spec de phase 2, §5.2). Un fichier qui échoue est écarté avec sa raison ; un groupe est tout ou rien.</summary>
public static class BatchPreflight
{
    // MAX_PATH moins le terminateur ; la limite ne s'applique qu'à Windows.
    private const int WindowsMaxPath = 259;

    /// <summary>Les rangs à écarter, avec leur raison. Les autres membres d'un groupe en échec sont écartés avec « Groupe écarté : … ».</summary>
    public static IReadOnlyDictionary<int, string> Evaluate(IReadOnlyList<PreflightEntry> entries)
    {
        var reasons = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            if (Check(entry) is { } reason)
            {
                reasons[entry.Rank] = reason;
            }
        }

        foreach (var group in entries.GroupBy(entry => entry.GroupId))
        {
            var failing = group.FirstOrDefault(entry => reasons.ContainsKey(entry.Rank));
            if (failing is null)
            {
                continue;
            }

            foreach (var other in group.Where(entry => !reasons.ContainsKey(entry.Rank)))
            {
                reasons[other.Rank] = $"Groupe écarté : {Path.GetFileName(failing.SourcePath)} — {reasons[failing.Rank]}";
            }
        }

        return reasons;
    }

    /// <summary>La raison de l'écart, ou null si le fichier passe tous les contrôles.</summary>
    public static string? Check(PreflightEntry entry)
    {
        var info = new FileInfo(entry.SourcePath);
        if (!info.Exists)
        {
            return "Fichier introuvable";
        }

        if (info.Length != entry.SourceSize || info.LastWriteTimeUtc != entry.SourceLastWriteUtc)
        {
            return "Fichier modifié depuis le plan";
        }

        if (OperatingSystem.IsWindows() && entry.TargetPath.Length > WindowsMaxPath)
        {
            return "Chemin trop long";
        }

        if (TargetTaken(entry))
        {
            return "Le nom cible existe déjà";
        }

        try
        {
            // Une ouverture exclusive échoue si un autre processus tient le fichier.
            using var probe = new FileStream(entry.SourcePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException)
        {
            return "Fichier verrouillé";
        }
        catch (UnauthorizedAccessException)
        {
            return "Accès refusé";
        }

        return null;
    }

    private static bool TargetTaken(PreflightEntry entry)
    {
        if (!File.Exists(entry.TargetPath) && !Directory.Exists(entry.TargetPath))
        {
            return false;
        }

        // Changement de casse seul sur un volume insensible à la casse : la cible « existante » est la source elle-même.
        var caseOnly = string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(entry.SourcePath, entry.TargetPath, StringComparison.Ordinal);
        return !caseOnly;
    }
}
```

- [ ] **Step 5: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~FileTimestampsTests|FullyQualifiedName~FileMovesTests|FullyQualifiedName~BatchPreflightTests"`
Expected : PASS (11 tests).

- [ ] **Step 6: Commit**

```bash
git add PhotoApp.Operations PhotoApp.Tests
git commit -m "feat: dates du fichier, déplacement sans écrasement et contrôles préalables (groupes tout ou rien)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---
### Task 7: `BatchPlanner` : du résultat d'analyse au plan figé (Operations)

Le planificateur retient les médias Sûre et Probable qui ont un nom proposé et dont le nom actuel diffère, forme les groupes (Live Photo, sidecars), fige la date que prendront les fichiers et note l'état d'origine (taille, dates) pour contrôler et annuler.

**Files:**
- Create: `PhotoApp.Operations/BatchPlanner.cs`
- Test: `PhotoApp.Tests/BatchPlannerTests.cs`

**Interfaces:**
- Consumes : `MediaResult` (phase 1), `BatchItemDraft`, `ItemRole`, `SidecarNames.Rename`.
- Produces : `PlanSummary(int AlreadyConforming, int Files, int Groups, int DeferredConversion)` ; `BatchPlan(IReadOnlyList<BatchItemDraft> Items, PlanSummary Summary)` ; `new BatchPlanner(TimeZoneInfo? zone = null)` avec `Plan(IReadOnlyList<MediaResult> results, int? maxGroups = null) : BatchPlan`. Les rangs des éléments vont de 0 à n−1 ; les identifiants de groupe, de 1 à g.

- [ ] **Step 1: Écrire les tests**

`PhotoApp.Tests/BatchPlannerTests.cs` :

```csharp
using PhotoApp.Core.Batches;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class BatchPlannerTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 19, 12, 0, 0);
    private static readonly DateTime LastWrite = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private sealed class NoHasher : IContentHasher
    {
        public string Hash(string fullPath) => fullPath;
    }

    private static IReadOnlyList<MediaResult> Analyze(params MediaFacts[] facts) =>
        new NormalizationEngine(new NormalizationOptions(UtcNow, TestZones.PlusOne), new NoHasher()).Normalize(facts);

    private static MediaFacts Facts(string path, RawMetadata? metadata = null, IReadOnlyList<string>? sidecars = null) =>
        new(new ScannedMedia(path, 100, LastWrite, sidecars ?? []), metadata ?? RawMetadata.Empty);

    private static RawMetadata Exif(DateTime wallClock, string? extension = null, TimeSpan? offset = null) => new()
    {
        Dates = [new DateCandidate(wallClock, DatePrecision.Second, CandidateOrigin.ExifOriginal, offset)],
        DetectedExtension = extension
    };

    private static BatchPlan Plan(IReadOnlyList<MediaResult> results, int? maxGroups = null) =>
        new BatchPlanner(TestZones.PlusOne).Plan(results, maxGroups);

    [Fact]
    public void Plan_KeepsSureAndProbable_AndLeavesTheRest()
    {
        using var temp = new TempDirectory();
        var results = Analyze(
            Facts(temp.Combine("holiday.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15))),              // EXIF seul : Probable
            Facts(temp.Combine("IMG-20210915-WA0005.png")),                                               // WhatsApp : Sûre
            Facts(temp.Combine("blank.jpg")));                                                            // rien : non applicable

        var plan = Plan(results);

        var targets = plan.Items.ToDictionary(item => Path.GetFileName(item.SourcePath), item => Path.GetFileName(item.TargetPath));
        Assert.Equal(2, targets.Count);
        Assert.Equal("IMG-20230304-132015.jpg", targets["holiday.jpg"]);
        Assert.Equal("IMG-20210915-000000-01.png", targets["IMG-20210915-WA0005.png"]);
        Assert.Equal(new PlanSummary(0, 2, 2, 0), plan.Summary);
    }

    [Fact]
    public void Plan_AlreadyConformingFiles_ProduceNoItem()
    {
        using var temp = new TempDirectory();
        var results = Analyze(
            Facts(temp.Combine("IMG-20240102-030405.jpg")),
            Facts(temp.Combine("holiday.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15))));

        var plan = Plan(results);

        Assert.Equal(["holiday.jpg"], plan.Items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(1, plan.Summary.AlreadyConforming);
    }

    [Fact]
    public void Plan_RecordsTheDecidedInstantAndTheOriginalState()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("holiday.jpg");
        File.WriteAllText(path, "contenu");
        var creation = new FileInfo(path).CreationTimeUtc;
        var results = Analyze(new MediaFacts(new ScannedMedia(path, 7, LastWrite, []), Exif(new DateTime(2023, 3, 4, 13, 20, 15))));

        var item = Plan(results).Items.Single();

        Assert.Equal(new DateTimeOffset(2023, 3, 4, 13, 20, 15, TimeSpan.FromHours(1)), item.DecidedDate);
        Assert.Equal(7, item.SourceSize);
        Assert.Equal(LastWrite, item.SourceLastWriteUtc);
        Assert.Equal(creation, item.SourceCreationUtc);
        Assert.Equal(0, item.Rank);
        Assert.Equal(ItemRole.Media, item.Role);
    }

    [Fact]
    public void Plan_SidecarsFollowTheirMedia_InTheSameGroup()
    {
        using var temp = new TempDirectory();
        var sidecar = temp.Combine("holiday.xmp");
        File.WriteAllText(sidecar, "<x/>");
        var results = Analyze(Facts(temp.Combine("holiday.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15)), [sidecar]));

        var items = Plan(results).Items;

        Assert.Equal(["holiday.jpg", "holiday.xmp"], items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(["IMG-20230304-132015.jpg", "IMG-20230304-132015.xmp"], items.Select(item => Path.GetFileName(item.TargetPath)));
        Assert.Equal([ItemRole.Media, ItemRole.Sidecar], items.Select(item => item.Role));
        Assert.Single(items.Select(item => item.GroupId).Distinct());
        Assert.Equal([0, 1], items.Select(item => item.Rank));
    }

    [Fact]
    public void Plan_ASidecarSharedByTheTwoHalvesOfALivePhoto_IsPlannedOnce()
    {
        using var temp = new TempDirectory();
        var aae = temp.Combine("IMG_0477.AAE");
        File.WriteAllText(aae, "x");
        var photoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(2));
        var videoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        var results = Analyze(
            Facts(temp.Combine("IMG_0477.HEIC"), new RawMetadata { Dates = [photoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "heic" }, [aae]),
            Facts(temp.Combine("IMG_0477.MOV"), new RawMetadata { Dates = [videoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "mov" }, [aae]));

        var plan = Plan(results);

        // La photo, son sidecar (réclamé par la photo), puis la vidéo.
        Assert.Equal(["IMG_0477.HEIC", "IMG_0477.AAE", "IMG_0477.MOV"], plan.Items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(["IMG-20240711-170710.heic", "IMG-20240711-170710.AAE", "IMG-20240711-170710.mov"], plan.Items.Select(item => Path.GetFileName(item.TargetPath)));
        Assert.Equal([ItemRole.Media, ItemRole.Sidecar, ItemRole.LivePartner], plan.Items.Select(item => item.Role));
        Assert.Single(plan.Items.Select(item => item.GroupId).Distinct());
        Assert.Equal(new PlanSummary(0, 3, 1, 1), plan.Summary);   // la HEIC est comptée « conversion différée »
    }

    [Fact]
    public void Plan_MaxGroups_KeepsTheFirstGroupsOnly()
    {
        using var temp = new TempDirectory();
        var results = Analyze(
            Facts(temp.Combine("a.jpg"), Exif(new DateTime(2023, 3, 4, 13, 20, 15))),
            Facts(temp.Combine("b.jpg"), Exif(new DateTime(2023, 3, 5, 13, 20, 15))),
            Facts(temp.Combine("c.jpg"), Exif(new DateTime(2023, 3, 6, 13, 20, 15))));

        var plan = Plan(results, maxGroups: 2);

        Assert.Equal(["a.jpg", "b.jpg"], plan.Items.Select(item => Path.GetFileName(item.SourcePath)));
        Assert.Equal(2, plan.Summary.Groups);
    }

    [Fact]
    public void Plan_Nothing_ToRename_IsAnEmptyPlan()
    {
        var plan = Plan(Analyze());

        Assert.Empty(plan.Items);
        Assert.Equal(new PlanSummary(0, 0, 0, 0), plan.Summary);
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~BatchPlannerTests"`
Expected : erreur de compilation (`BatchPlanner`, `PlanSummary` n'existent pas).

- [ ] **Step 3: Écrire le planificateur**

`PhotoApp.Operations/BatchPlanner.cs` :

```csharp
using PhotoApp.Core.Batches;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Operations;

/// <param name="AlreadyConforming">Médias dont le nom actuel est déjà le nom proposé : rien à faire.</param>
/// <param name="Files">Fichiers du plan (médias, partenaires Live Photo et sidecars).</param>
/// <param name="DeferredConversion">Fichiers HEIC, HEIF ou AVIF : renommés tels quels, conversion en phase 3.</param>
public sealed record PlanSummary(int AlreadyConforming, int Files, int Groups, int DeferredConversion);

public sealed record BatchPlan(IReadOnlyList<BatchItemDraft> Items, PlanSummary Summary);

/// <summary>
/// Construit le plan figé d'un lot (spec de phase 2, §5.1) à partir de l'analyse de phase 1 : seuls les médias Sûre et Probable
/// qui ont un nom proposé différent du nom actuel. Un média, son partenaire Live Photo et ses sidecars forment un groupe.
/// </summary>
public sealed class BatchPlanner(TimeZoneInfo? zone = null)
{
    private static readonly HashSet<string> DeferredExtensions = new(StringComparer.OrdinalIgnoreCase) { ".heic", ".heif", ".avif" };

    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

    public BatchPlan Plan(IReadOnlyList<MediaResult> results, int? maxGroups = null)
    {
        var alreadyConforming = 0;
        var candidates = new List<MediaResult>();
        foreach (var result in results)
        {
            if (result.Decision is not { IsApplicable: true, Date: not null } || result.ProposedName is null)
            {
                continue;
            }

            if (string.Equals(Path.GetFileName(result.Facts.File.FullPath), result.ProposedName, StringComparison.Ordinal))
            {
                alreadyConforming++;
            }
            else
            {
                candidates.Add(result);
            }
        }

        var groups = BuildGroups(candidates);
        if (maxGroups is { } limit)
        {
            groups = groups.Take(limit).ToList();
        }

        var items = new List<BatchItemDraft>();
        var claimedSidecars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deferred = 0;
        for (var index = 0; index < groups.Count; index++)
        {
            var members = groups[index].OrderBy(member => member.Kind).ToList();   // la photo avant sa vidéo
            foreach (var member in members)
            {
                var file = member.Facts.File;
                var folder = Path.GetDirectoryName(file.FullPath) ?? string.Empty;
                var decided = member.Decision.Date!.ToInstant(_zone);
                var role = members.Count > 1 && member.Kind == MediaKind.Video ? ItemRole.LivePartner : ItemRole.Media;

                items.Add(new BatchItemDraft(
                    items.Count, index + 1, role, file.FullPath, Path.Combine(folder, member.ProposedName!), decided,
                    file.SizeBytes, file.LastWriteUtc, CreationOf(file.FullPath, file.LastWriteUtc)));
                if (DeferredExtensions.Contains(Path.GetExtension(member.ProposedName!)))
                {
                    deferred++;
                }

                foreach (var sidecar in file.Sidecars)
                {
                    // Un sidecar .aae vaut pour les deux moitiés d'une Live Photo : le premier média qui le réclame le garde.
                    if (!claimedSidecars.Add(sidecar))
                    {
                        continue;
                    }

                    var newName = SidecarNames.Rename(file.FileName, member.ProposedName!, Path.GetFileName(sidecar));
                    var info = new FileInfo(sidecar);
                    if (newName is null || !info.Exists)
                    {
                        continue;
                    }

                    items.Add(new BatchItemDraft(
                        items.Count, index + 1, ItemRole.Sidecar, sidecar, Path.Combine(Path.GetDirectoryName(sidecar) ?? folder, newName), decided,
                        info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc));
                }
            }
        }

        return new BatchPlan(items, new PlanSummary(alreadyConforming, items.Count, groups.Count, deferred));
    }

    // Ordre stable (chemin), un groupe par média, les deux moitiés d'une Live Photo ensemble.
    private static List<List<MediaResult>> BuildGroups(List<MediaResult> candidates)
    {
        var ordered = candidates.OrderBy(result => result.Facts.File.FullPath, StringComparer.Ordinal).ToList();
        var byPath = ordered.ToDictionary(result => result.Facts.File.FullPath, StringComparer.Ordinal);
        var grouped = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<List<MediaResult>>();

        foreach (var result in ordered)
        {
            var path = result.Facts.File.FullPath;
            if (!grouped.Add(path))
            {
                continue;
            }

            var group = new List<MediaResult> { result };
            if (result.LivePhotoPartner is { } partner)
            {
                var partnerPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, partner);
                if (byPath.TryGetValue(partnerPath, out var mate) && grouped.Add(partnerPath))
                {
                    group.Add(mate);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static DateTime CreationOf(string path, DateTime fallbackUtc)
    {
        var info = new FileInfo(path);
        return info.Exists ? info.CreationTimeUtc : fallbackUtc;
    }
}
```

- [ ] **Step 4: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~BatchPlannerTests"`
Expected : PASS (7 tests). Si `Plan_ASidecarShared…` échoue sur un nom, relever le nom réellement produit par le moteur (`ProposedName`) : le sidecar doit suivre celui du média, c'est le comportement à conserver.

- [ ] **Step 5: Commit**

```bash
git add PhotoApp.Operations PhotoApp.Tests
git commit -m "feat: planificateur de lot (Sûre et Probable, groupes Live Photo et sidecars, tranches)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Exécuteur sur place et reprise (Operations)

**Files:**
- Create: `PhotoApp.Operations/InPlaceRenamer.cs`
- Create: `PhotoApp.Operations/BatchRecovery.cs`
- Create: `PhotoApp.Tests/BatchTestSupport.cs`
- Test: `PhotoApp.Tests/InPlaceRenamerTests.cs`, `PhotoApp.Tests/BatchRecoveryTests.cs`

**Interfaces:**
- Consumes : `IBatchJournal`, `BatchPreflight.Evaluate`, `PreflightEntry`, `FileMoves.Move`, `FileTimestamps.Set`.
- Produces : `BatchProgress(int Processed, int Total)` ; `BatchRunSummary(long BatchId, BatchCounts Counts, bool Cancelled)` ; `new InPlaceRenamer(IBatchJournal)` avec `Run(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null) : BatchRunSummary` (exécute un lot planifié ou interrompu : reprend d'abord les éléments « en cours », écarte ce qui échoue aux contrôles, renomme le reste) ; `RecoveryReport(int Completed, int RolledBack, int Anomalies)` ; `new BatchRecovery(IBatchJournal)` avec `Resolve(long batchId) : RecoveryReport`.

- [ ] **Step 1: Écrire l'aide de test**

`PhotoApp.Tests/BatchTestSupport.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

/// <summary>Aides communes aux tests d'exécution, de reprise et d'annulation.</summary>
internal static class BatchTestSupport
{
    public static readonly DateTimeOffset Decided = new(2023, 3, 4, 13, 20, 15, TimeSpan.FromHours(1));

    public static SqliteBatchJournal NewJournal(TempDirectory temp) => new(new CatalogDatabase(temp.Combine("catalog.db")));

    public static string Photos(TempDirectory temp)
    {
        var folder = temp.Combine("photos");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string WriteFile(string folder, string name, string content = "contenu")
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    public static BatchItemDraft DraftFor(string source, string target, int rank, int group = 1, ItemRole role = ItemRole.Media)
    {
        var info = new FileInfo(source);
        return new BatchItemDraft(rank, group, role, source, target, Decided, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
    }

    public static long CreateBatch(IBatchJournal journal, params BatchItemDraft[] drafts) =>
        journal.CreateBatch(DateTime.UtcNow, [Path.GetDirectoryName(drafts[0].SourcePath)!], drafts);

    /// <summary>Taille, contenu, dates de création et de modification de chaque fichier : la preuve d'un état identique.</summary>
    public static Dictionary<string, (long Size, string Hash, DateTime Creation, DateTime LastWrite)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(folder, path),
            path => (new FileInfo(path).Length,
                     Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                     File.GetCreationTimeUtc(path),
                     File.GetLastWriteTimeUtc(path)));

    /// <summary>Un <see cref="IProgress{T}"/> synchrone (contrairement à <see cref="Progress{T}"/>, qui poste le rappel).</summary>
    public sealed class SyncProgress(Action<BatchProgress> onReport) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => onReport(value);
    }
}
```

- [ ] **Step 2: Écrire les tests de l'exécuteur**

`PhotoApp.Tests/InPlaceRenamerTests.cs` :

```csharp
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class InPlaceRenamerTests
{
    [Fact]
    public void Run_RenamesTheFile_SetsTheDecidedDates_AndKeepsTheContent()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "holiday.jpg", "pixels");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-20230304-132015.jpg"), 0));

        var summary = new InPlaceRenamer(journal).Run(batch);

        var target = Path.Combine(photos, "IMG-20230304-132015.jpg");
        Assert.False(File.Exists(source));
        Assert.Equal("pixels", File.ReadAllText(target));
        Assert.Equal(Decided.UtcDateTime, File.GetLastWriteTimeUtc(target));
        Assert.Equal(Decided.UtcDateTime, File.GetCreationTimeUtc(target));
        Assert.False(summary.Cancelled);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Done, item.State);
        Assert.Equal(new FileInfo(target).Length, item.DoneSize);
        Assert.Equal(Decided.UtcDateTime, item.DoneLastWriteUtc);
    }

    [Fact]
    public void Run_CaseOnlyChange_RenamesTheFile()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "IMG-20230304-132015.JPG");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-20230304-132015.jpg"), 0));

        new InPlaceRenamer(journal).Run(batch);

        Assert.Equal(["IMG-20230304-132015.jpg"], Directory.GetFiles(photos).Select(Path.GetFileName));
        Assert.Equal(ItemState.Done, journal.GetItems(batch).Single().State);
    }

    [Fact]
    public void Run_FileChangedSinceThePlan_IsSkipped_AndLeftUntouched()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "holiday.jpg", "v1");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));
        File.WriteAllText(source, "version modifiée");

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.Combine(photos, "IMG-1.jpg")));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Skipped, item.State);
        Assert.Equal("Fichier modifié depuis le plan", item.Error);
        Assert.Equal(1, summary.Counts.Skipped);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Run_TargetTaken_SkipsTheWholeGroup_AndNeverOverwrites()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var media = WriteFile(photos, "holiday.jpg", "photo");
        var sidecar = WriteFile(photos, "holiday.xmp", "sidecar");
        WriteFile(photos, "IMG-1.xmp", "propriétaire du nom");
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(media, Path.Combine(photos, "IMG-1.jpg"), 0, group: 1),
            DraftFor(sidecar, Path.Combine(photos, "IMG-1.xmp"), 1, group: 1, role: ItemRole.Sidecar));

        new InPlaceRenamer(journal).Run(batch);

        Assert.True(File.Exists(media));
        Assert.True(File.Exists(sidecar));
        Assert.Equal("propriétaire du nom", File.ReadAllText(Path.Combine(photos, "IMG-1.xmp")));
        Assert.All(journal.GetItems(batch), item => Assert.Equal(ItemState.Skipped, item.State));
    }

    [Fact]
    public void Run_AFailureOnOneFile_DoesNotStopTheBatch()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var failing = WriteFile(photos, "a.jpg");
        var fine = WriteFile(photos, "b.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(failing, Path.Combine(photos, "absent", "IMG-1.jpg"), 0, group: 1),   // le dossier cible n'existe pas
            DraftFor(fine, Path.Combine(photos, "IMG-2.jpg"), 1, group: 2));

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.True(File.Exists(failing));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-2.jpg")));
        var items = journal.GetItems(batch);
        Assert.Equal(ItemState.Failed, items[0].State);
        Assert.False(string.IsNullOrEmpty(items[0].Error));
        Assert.Equal(ItemState.Done, items[1].State);
        Assert.Equal(1, summary.Counts.Failed);
        Assert.Equal(1, summary.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Run_Cancelled_FinishesTheCurrentFile_ThenStops_AndCanBeResumed()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var drafts = Enumerable.Range(0, 3)
            .Select(i => DraftFor(WriteFile(photos, $"a{i}.jpg"), Path.Combine(photos, $"IMG-{i}.jpg"), i, group: i + 1))
            .ToArray();
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, drafts);
        using var cancellation = new CancellationTokenSource();

        var first = new InPlaceRenamer(journal).Run(batch, cancellation.Token, new SyncProgress(_ => cancellation.Cancel()));

        Assert.True(first.Cancelled);
        Assert.Equal(1, first.Counts.Done);
        Assert.Equal(2, first.Counts.Pending);
        Assert.Equal(BatchState.Interrupted, journal.GetBatch(batch)!.State);
        Assert.Equal(batch, journal.FindUnfinished()?.Id);

        var second = new InPlaceRenamer(journal).Run(batch);

        Assert.False(second.Cancelled);
        Assert.Equal(3, second.Counts.Done);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        Assert.Null(journal.FindUnfinished());
    }

    [Fact]
    public void Run_ACompletedBatch_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));
        new InPlaceRenamer(journal).Run(batch);

        Assert.Throws<InvalidOperationException>(() => new InPlaceRenamer(journal).Run(batch));
    }

    [Fact]
    public void Run_WhileAnotherTreatmentHoldsTheLock_Throws_AndTouchesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));

        using var held = journal.AcquireExecutionLock();

        Assert.Throws<BatchLockedException>(() => new InPlaceRenamer(journal).Run(batch));
        Assert.True(File.Exists(source));
        Assert.Equal(BatchState.Planned, journal.GetBatch(batch)!.State);
    }
}
```

- [ ] **Step 3: Écrire les tests de la reprise**

`PhotoApp.Tests/BatchRecoveryTests.cs` :

```csharp
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class BatchRecoveryTests
{
    // Un lot tué au milieu d'un fichier : élément « en cours », lot « en cours ».
    private static (SqliteBatchJournal Journal, long Batch, BatchItem Item) Crashed(string source, string target, TempDirectory temp)
    {
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        journal.SetBatchState(batch, BatchState.Running);
        var item = journal.GetItems(batch).Single();
        journal.SetItemState(item.Id, ItemState.Running);
        return (journal, batch, journal.GetItems(batch).Single());
    }

    [Fact]
    public void Resolve_OnlyTheNewNameExists_TheItemIsDone_AndTheDatesAreReapplied()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var (journal, batch, _) = Crashed(source, target, temp);
        File.Move(source, target);

        var report = new BatchRecovery(journal).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Done, item.State);
        Assert.Equal(Decided.UtcDateTime, File.GetLastWriteTimeUtc(target));
        Assert.Equal(Decided.UtcDateTime, item.DoneLastWriteUtc);
        Assert.Equal(new FileInfo(target).Length, item.DoneSize);
        Assert.Equal(new RecoveryReport(1, 0, 0), report);
    }

    [Fact]
    public void Resolve_OnlyTheOldNameExists_TheItemGoesBackToPending()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var (journal, batch, _) = Crashed(source, Path.Combine(photos, "IMG-1.jpg"), temp);

        var report = new BatchRecovery(journal).Resolve(batch);

        Assert.Equal(ItemState.Pending, journal.GetItems(batch).Single().State);
        Assert.True(File.Exists(source));
        Assert.Equal(new RecoveryReport(0, 1, 0), report);
    }

    [Fact]
    public void Resolve_BothNamesExist_IsAnAnomaly_AndNothingIsOverwritten()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "A");
        var target = WriteFile(photos, "IMG-1.jpg", "B");
        var (journal, batch, _) = Crashed(source, target, temp);

        var report = new BatchRecovery(journal).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.StartsWith("Les deux noms existent", item.Error);
        Assert.Equal("A", File.ReadAllText(source));
        Assert.Equal("B", File.ReadAllText(target));
        Assert.Equal(new RecoveryReport(0, 0, 1), report);
    }

    [Fact]
    public void Resolve_NeitherNameExists_IsAnAnomaly()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var (journal, batch, _) = Crashed(source, Path.Combine(photos, "IMG-1.jpg"), temp);
        File.Delete(source);

        var report = new BatchRecovery(journal).Resolve(batch);

        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.Failed, item.State);
        Assert.Equal("Fichier introuvable", item.Error);
        Assert.Equal(1, report.Anomalies);
    }

    [Fact]
    public void Resolve_CaseOnlyRename_ReadsTheRealCaseOnDisk()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "IMG-1.JPG");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var (journal, batch, _) = Crashed(source, target, temp);

        // Le nom porte encore l'ancienne casse : le renommage n'a pas eu lieu.
        new BatchRecovery(journal).Resolve(batch);
        Assert.Equal(ItemState.Pending, journal.GetItems(batch).Single().State);

        // Le nom porte la nouvelle casse : il a eu lieu.
        journal.SetItemState(journal.GetItems(batch).Single().Id, ItemState.Running);
        File.Move(source, source + ".tmp");
        File.Move(source + ".tmp", target);
        new BatchRecovery(journal).Resolve(batch);
        Assert.Equal(ItemState.Done, journal.GetItems(batch).Single().State);
    }

    [Fact]
    public void Resolve_LeavesOtherItemsUntouched()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));
        journal.SetBatchState(batch, BatchState.Interrupted);

        var report = new BatchRecovery(journal).Resolve(batch);

        Assert.Equal(ItemState.Pending, journal.GetItems(batch).Single().State);
        Assert.Equal(new RecoveryReport(0, 0, 0), report);
    }

    [Fact]
    public void Run_AfterACrash_ResolvesThenFinishesTheBatch()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var movedBeforeTheCrash = WriteFile(photos, "a.jpg");
        var notReached = WriteFile(photos, "b.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(movedBeforeTheCrash, Path.Combine(photos, "IMG-1.jpg"), 0, group: 1),
            DraftFor(notReached, Path.Combine(photos, "IMG-2.jpg"), 1, group: 2));
        journal.SetBatchState(batch, BatchState.Running);
        journal.SetItemState(journal.GetItems(batch)[0].Id, ItemState.Running);
        File.Move(movedBeforeTheCrash, Path.Combine(photos, "IMG-1.jpg"));

        var summary = new InPlaceRenamer(journal).Run(batch);

        Assert.Equal(2, summary.Counts.Done);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-1.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-2.jpg")));
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
    }
}
```

- [ ] **Step 4: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~InPlaceRenamerTests|FullyQualifiedName~BatchRecoveryTests"`
Expected : erreur de compilation (`InPlaceRenamer`, `BatchRecovery`, `RecoveryReport`, `BatchProgress` n'existent pas).

- [ ] **Step 5: Écrire la reprise**

`PhotoApp.Operations/BatchRecovery.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <param name="Completed">Éléments dont le renommage avait eu lieu : marqués faits.</param>
/// <param name="RolledBack">Éléments dont le renommage n'avait pas eu lieu : remis en attente.</param>
/// <param name="Anomalies">Éléments incohérents (deux noms présents, ou aucun) : en échec, rien n'est écrasé.</param>
public sealed record RecoveryReport(int Completed, int RolledBack, int Anomalies);

/// <summary>
/// Résout les éléments laissés « en cours » par un lot interrompu brutalement, d'après l'état réel du disque (spec de phase 2, §5.4).
/// </summary>
public sealed class BatchRecovery(IBatchJournal journal)
{
    public RecoveryReport Resolve(long batchId)
    {
        int completed = 0, rolledBack = 0, anomalies = 0;
        foreach (var item in journal.GetItems(batchId).Where(item => item.State == ItemState.Running))
        {
            var (sourcePresent, targetPresent) = Presence(item);
            if (!sourcePresent && targetPresent)
            {
                // Le renommage avait eu lieu : on réapplique les dates (idempotent) et on note l'état du fichier.
                var instant = item.DecidedDate.UtcDateTime;
                FileTimestamps.Set(item.TargetPath, instant, instant);
                var info = new FileInfo(item.TargetPath);
                journal.SetItemDone(item.Id, info.Length, info.LastWriteTimeUtc);
                completed++;
            }
            else if (sourcePresent && !targetPresent)
            {
                journal.SetItemState(item.Id, ItemState.Pending);
                rolledBack++;
            }
            else if (sourcePresent)
            {
                journal.SetItemState(item.Id, ItemState.Failed, $"Les deux noms existent : {Path.GetFileName(item.SourcePath)} et {Path.GetFileName(item.TargetPath)}");
                anomalies++;
            }
            else
            {
                journal.SetItemState(item.Id, ItemState.Failed, "Fichier introuvable");
                anomalies++;
            }
        }

        return new RecoveryReport(completed, rolledBack, anomalies);
    }

    private static (bool Source, bool Target) Presence(BatchItem item)
    {
        if (string.Equals(item.SourcePath, item.TargetPath, StringComparison.OrdinalIgnoreCase)
            && ActualName(item.TargetPath) is { } actual)
        {
            // Changement de casse seul : un seul fichier existe, c'est la casse réelle de son nom qui dit où en est le renommage.
            return string.Equals(actual, Path.GetFileName(item.TargetPath), StringComparison.Ordinal) ? (false, true) : (true, false);
        }

        return (File.Exists(item.SourcePath), File.Exists(item.TargetPath));
    }

    // Nom tel qu'il est écrit dans le dossier (casse comprise), ou null si aucun fichier ne correspond.
    private static string? ActualName(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFileSystemEntries(folder, Path.GetFileName(path)).Select(Path.GetFileName).FirstOrDefault();
    }
}
```

- [ ] **Step 6: Écrire l'exécuteur**

`PhotoApp.Operations/InPlaceRenamer.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

public sealed record BatchProgress(int Processed, int Total);

public sealed record BatchRunSummary(long BatchId, BatchCounts Counts, bool Cancelled);

/// <summary>
/// Exécute un lot : renommage sur place, un seul déplacement par fichier, journal écrit avant chaque action
/// (spec de phase 2, §5.3). Un échec sur un fichier n'arrête pas le lot ; l'arrêt demandé termine le fichier en cours.
/// </summary>
public sealed class InPlaceRenamer(IBatchJournal journal)
{
    public BatchRunSummary Run(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        using var executionLock = journal.AcquireExecutionLock();

        var batch = journal.GetBatch(batchId) ?? throw new InvalidOperationException($"Lot {batchId} introuvable.");
        if (batch.State is BatchState.Completed or BatchState.Undone)
        {
            throw new InvalidOperationException($"Le lot {batchId} est déjà terminé.");
        }

        journal.SetBatchState(batchId, BatchState.Running);
        new BatchRecovery(journal).Resolve(batchId);
        SkipFailingEntries(batchId);

        var items = journal.GetItems(batchId);
        var processed = items.Count(item => item.State != ItemState.Pending);
        var cancelled = false;
        foreach (var item in items.Where(item => item.State == ItemState.Pending))
        {
            if (cancellation.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            Execute(item);
            progress?.Report(new BatchProgress(++processed, items.Count));
        }

        journal.SetBatchState(batchId, cancelled ? BatchState.Interrupted : BatchState.Completed);
        return new BatchRunSummary(batchId, journal.GetBatch(batchId)!.Counts, cancelled);
    }

    private void SkipFailingEntries(long batchId)
    {
        var pending = journal.GetItems(batchId).Where(item => item.State == ItemState.Pending).ToList();
        var reasons = BatchPreflight.Evaluate(pending
            .Select(item => new PreflightEntry(item.Rank, item.GroupId, item.SourcePath, item.TargetPath, item.SourceSize, item.SourceLastWriteUtc))
            .ToList());

        foreach (var item in pending.Where(item => reasons.ContainsKey(item.Rank)))
        {
            journal.SetItemState(item.Id, ItemState.Skipped, reasons[item.Rank]);
        }
    }

    private void Execute(BatchItem item)
    {
        journal.SetItemState(item.Id, ItemState.Running);   // validé avant le déplacement
        var moved = false;
        try
        {
            FileMoves.Move(item.SourcePath, item.TargetPath);
            moved = true;
            var instant = item.DecidedDate.UtcDateTime;
            FileTimestamps.Set(item.TargetPath, instant, instant);
            var info = new FileInfo(item.TargetPath);
            journal.SetItemDone(item.Id, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var message = moved ? ex.Message + RollBack(item) : ex.Message;
            journal.SetItemState(item.Id, ItemState.Failed, message);
        }
    }

    // Retour arrière au mieux quand les dates n'ont pas pu être appliquées : le fichier retrouve son nom et ses dates d'origine.
    private static string RollBack(BatchItem item)
    {
        try
        {
            FileMoves.Move(item.TargetPath, item.SourcePath);
            FileTimestamps.Set(item.SourcePath, item.SourceCreationUtc, item.SourceLastWriteUtc);
            return " (renommage annulé)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $" (retour arrière impossible, le fichier est sous {item.TargetPath} : {ex.Message})";
        }
    }
}
```

- [ ] **Step 7: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~InPlaceRenamerTests|FullyQualifiedName~BatchRecoveryTests"`
Expected : PASS (15 tests).

- [ ] **Step 8: Commit**

```bash
git add PhotoApp.Operations PhotoApp.Tests
git commit -m "feat: exécuteur de lot sur place (journal avant action, arrêt propre) et reprise après interruption" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Annulation d'un lot (Operations)

**Files:**
- Create: `PhotoApp.Operations/BatchUndo.cs`
- Test: `PhotoApp.Tests/BatchUndoTests.cs`

**Interfaces:**
- Consumes : `IBatchJournal`, `BatchRecovery`, `FileMoves.Move`, `FileTimestamps.Set`, `BatchProgress`.
- Produces : `UndoSummary(long BatchId, int Restored, int Failed, bool Completed)` ; `new BatchUndo(IBatchJournal)` avec `Undo(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null) : UndoSummary`. `Completed` est vrai quand plus aucun élément n'est à restaurer (le lot passe alors à *annulé*). Un lot partiellement annulé peut l'être de nouveau par le même appel.

- [ ] **Step 1: Écrire les tests**

`PhotoApp.Tests/BatchUndoTests.cs` :

```csharp
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class BatchUndoTests
{
    [Fact]
    public void Undo_RestoresNamesContentsAndDates()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var first = WriteFile(photos, "a.jpg", "A");
        var second = WriteFile(photos, "b.jpg", "BB");
        File.SetLastWriteTimeUtc(second, new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        File.SetCreationTimeUtc(second, new DateTime(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var batch = CreateBatch(
            journal,
            DraftFor(first, Path.Combine(photos, "IMG-1.jpg"), 0, group: 1),
            DraftFor(second, Path.Combine(photos, "IMG-2.jpg"), 1, group: 2));
        new InPlaceRenamer(journal).Run(batch);
        Assert.NotEqual(before.Keys.Order(), Snapshot(photos).Keys.Order());

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(new UndoSummary(batch, 2, 0, true), summary);
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
        Assert.All(journal.GetItems(batch), item => Assert.Equal(ItemState.Undone, item.State));
    }

    [Fact]
    public void Undo_ALaterEditedFile_IsLeftInPlace_AndReported()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "v1");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        new InPlaceRenamer(journal).Run(batch);
        File.WriteAllText(target, "retouchée depuis le lot");

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.Equal("retouchée depuis le lot", File.ReadAllText(target));
        Assert.False(File.Exists(source));
        var item = journal.GetItems(batch).Single();
        Assert.Equal(ItemState.UndoFailed, item.State);
        Assert.StartsWith("Fichier modifié depuis le lot", item.Error);
        Assert.Equal(new UndoSummary(batch, 0, 1, false), summary);
        Assert.Equal(BatchState.Completed, journal.GetBatch(batch)!.State);
        Assert.Equal(batch, journal.FindLastUndoable()?.Id);
    }

    [Fact]
    public void Undo_OriginalNameTaken_IsReported_ThenRetriedOnceFree()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg", "A");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        new InPlaceRenamer(journal).Run(batch);
        WriteFile(photos, "a.jpg", "un autre fichier");

        var blocked = new BatchUndo(journal).Undo(batch);

        Assert.False(blocked.Completed);
        Assert.Equal("un autre fichier", File.ReadAllText(source));
        Assert.True(File.Exists(target));
        Assert.StartsWith("Le nom d'origine est occupé", journal.GetItems(batch).Single().Error);

        File.Delete(source);
        var retried = new BatchUndo(journal).Undo(batch);

        Assert.True(retried.Completed);
        Assert.Equal("A", File.ReadAllText(source));
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
    }

    [Fact]
    public void Undo_AnAlreadyUndoneBatch_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));
        new InPlaceRenamer(journal).Run(batch);
        new BatchUndo(journal).Undo(batch);

        Assert.Throws<InvalidOperationException>(() => new BatchUndo(journal).Undo(batch));
    }

    [Fact]
    public void Undo_ABatchThatNeverRan_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(WriteFile(photos, "a.jpg"), Path.Combine(photos, "IMG-1.jpg"), 0));

        Assert.Throws<InvalidOperationException>(() => new BatchUndo(journal).Undo(batch));
    }

    [Fact]
    public void Undo_AnInterruptedBatch_RestoresWhatWasDone_AndSkipsTheRest()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var drafts = Enumerable.Range(0, 3)
            .Select(i => DraftFor(WriteFile(photos, $"a{i}.jpg"), Path.Combine(photos, $"IMG-{i}.jpg"), i, group: i + 1))
            .ToArray();
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, drafts);
        using var cancellation = new CancellationTokenSource();
        new InPlaceRenamer(journal).Run(batch, cancellation.Token, new SyncProgress(_ => cancellation.Cancel()));

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.True(summary.Completed);
        Assert.Equal(1, summary.Restored);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(BatchState.Undone, journal.GetBatch(batch)!.State);
        Assert.Equal(2, journal.GetBatch(batch)!.Counts.Skipped);
        Assert.Null(journal.FindUnfinished());
    }

    [Fact]
    public void Undo_ACrashedBatch_ResolvesTheRunningItemFirst()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var source = WriteFile(photos, "a.jpg");
        var target = Path.Combine(photos, "IMG-1.jpg");
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var batch = CreateBatch(journal, DraftFor(source, target, 0));
        journal.SetBatchState(batch, BatchState.Running);
        journal.SetItemState(journal.GetItems(batch).Single().Id, ItemState.Running);
        File.Move(source, target);

        var summary = new BatchUndo(journal).Undo(batch);

        Assert.True(summary.Completed);
        Assert.Equal(before, Snapshot(photos));
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~BatchUndoTests"`
Expected : erreur de compilation (`BatchUndo`, `UndoSummary` n'existent pas).

- [ ] **Step 3: Écrire l'annulation**

`PhotoApp.Operations/BatchUndo.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <param name="Completed">Plus aucun élément à restaurer : le lot est annulé.</param>
public sealed record UndoSummary(long BatchId, int Restored, int Failed, bool Completed);

/// <summary>
/// Annule un lot (spec de phase 2, §5.5) : chaque fichier fait retrouve son nom et ses dates d'origine, dans l'ordre inverse.
/// Un fichier modifié depuis le lot, ou dont le nom d'origine est occupé, est laissé en place et signalé ; jamais écrasé.
/// </summary>
public sealed class BatchUndo(IBatchJournal journal)
{
    public UndoSummary Undo(long batchId, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        using var executionLock = journal.AcquireExecutionLock();

        var batch = journal.GetBatch(batchId) ?? throw new InvalidOperationException($"Lot {batchId} introuvable.");
        if (batch.State == BatchState.Undone)
        {
            throw new InvalidOperationException($"Le lot {batchId} est déjà annulé.");
        }

        if (batch.State == BatchState.Planned)
        {
            throw new InvalidOperationException($"Le lot {batchId} n'a pas été exécuté.");
        }

        // Un lot tué brutalement a peut-être un fichier déjà renommé mais encore « en cours ».
        new BatchRecovery(journal).Resolve(batchId);

        var todo = journal.GetItems(batchId)
            .Where(item => item.State is ItemState.Done or ItemState.UndoFailed)
            .OrderByDescending(item => item.Rank)
            .ToList();

        int restored = 0, failed = 0, processed = 0;
        foreach (var item in todo)
        {
            if (cancellation.IsCancellationRequested)
            {
                break;
            }

            if (Restore(item) is { } error)
            {
                journal.SetItemState(item.Id, ItemState.UndoFailed, error);
                failed++;
            }
            else
            {
                journal.SetItemState(item.Id, ItemState.Undone);
                restored++;
            }

            progress?.Report(new BatchProgress(++processed, todo.Count));
        }

        var remaining = journal.GetItems(batchId).Any(item => item.State is ItemState.Done or ItemState.UndoFailed);
        if (!remaining)
        {
            foreach (var pending in journal.GetItems(batchId).Where(item => item.State == ItemState.Pending))
            {
                journal.SetItemState(pending.Id, ItemState.Skipped, "Lot annulé avant exécution");
            }

            journal.SetBatchState(batchId, BatchState.Undone);
        }

        return new UndoSummary(batchId, restored, failed, !remaining);
    }

    // Null si le fichier est restauré, sinon la raison pour laquelle il est laissé en place.
    private static string? Restore(BatchItem item)
    {
        var target = new FileInfo(item.TargetPath);
        if (!target.Exists)
        {
            return "Fichier introuvable sous son nouveau nom";
        }

        if (target.Length != item.DoneSize || target.LastWriteTimeUtc != item.DoneLastWriteUtc)
        {
            return "Fichier modifié depuis le lot : laissé en place";
        }

        var caseOnly = string.Equals(item.SourcePath, item.TargetPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(item.SourcePath) || Directory.Exists(item.SourcePath)))
        {
            return "Le nom d'origine est occupé : laissé en place";
        }

        try
        {
            FileMoves.Move(item.TargetPath, item.SourcePath);
            FileTimestamps.Set(item.SourcePath, item.SourceCreationUtc, item.SourceLastWriteUtc);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
```

- [ ] **Step 4: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~BatchUndoTests"`
Expected : PASS (7 tests). Si `Undo_RestoresNamesContentsAndDates` échoue sur une date de création, vérifier que `FileTimestamps.Set` est bien appelé **après** le déplacement dans `Restore`.

- [ ] **Step 5: Commit**

```bash
git add PhotoApp.Operations PhotoApp.Tests
git commit -m "feat: annulation d'un lot (noms et dates d'origine restaurés, fichier modifié ou nom occupé laissés en place)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Brique de quarantaine (Core, Catalog, Operations)

Non utilisée par la phase 2 (premier usage : la conversion sur place de la phase 3). `Purge` est la seule suppression de fichier de la phase et aucune commande ne l'appelle encore. Le dossier de quarantaine est un paramètre qui **doit exister** ; son emplacement par défaut sera choisi en phase 3.

**Files:**
- Create: `PhotoApp.Core/Batches/QuarantineModel.cs`
- Create: `PhotoApp.Catalog/SqliteQuarantineLog.cs`
- Create: `PhotoApp.Operations/Quarantine.cs`
- Test: `PhotoApp.Tests/SqliteQuarantineLogTests.cs`, `PhotoApp.Tests/QuarantineTests.cs`

**Interfaces:**
- Produces : `QuarantineState { Held, Restored, Purged }` ; `QuarantineEntry(long Id, DateTime CreatedUtc, string OriginalPath, string StoredPath, long? BatchId, QuarantineState State)` ; `IQuarantineLog` (`Add`, `Get`, `List`, `SetState`) ; `new SqliteQuarantineLog(CatalogDatabase)` ; `new Quarantine(string rootFolder, IQuarantineLog log, Func<DateTime>? utcNow = null)` avec `Hold(string path, long? batchId = null) : QuarantineEntry`, `Restore(long id)`, `Purge(long id)`, `PurgeOlderThan(TimeSpan age) : int`.

- [ ] **Step 1: Écrire les tests**

`PhotoApp.Tests/SqliteQuarantineLogTests.cs` :

```csharp
using PhotoApp.Core.Batches;

namespace PhotoApp.Tests;

public sealed class SqliteQuarantineLogTests
{
    private static readonly DateTime Created = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Add_ThenGet_RoundTripsTheEntry()
    {
        using var temp = new TempDirectory();
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));

        var id = log.Add(Created, @"C:\photos\a.jpg", @"D:\q\a.jpg", batchId: 7);

        Assert.Equal(new QuarantineEntry(id, Created, @"C:\photos\a.jpg", @"D:\q\a.jpg", 7, QuarantineState.Held), log.Get(id));
    }

    [Fact]
    public void Get_Unknown_ReturnsNull()
    {
        using var temp = new TempDirectory();

        Assert.Null(new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db"))).Get(1));
    }

    [Fact]
    public void SetState_AndListByState()
    {
        using var temp = new TempDirectory();
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));
        var first = log.Add(Created, "a", "qa", null);
        var second = log.Add(Created, "b", "qb", null);

        log.SetState(first, QuarantineState.Restored);

        Assert.Equal([second], log.List(QuarantineState.Held).Select(entry => entry.Id));
        Assert.Equal([first, second], log.List().Select(entry => entry.Id));
        Assert.Null(log.Get(second)!.BatchId);
    }
}
```

`PhotoApp.Tests/QuarantineTests.cs` :

```csharp
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class QuarantineTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static (Quarantine Quarantine, SqliteQuarantineLog Log, string Root, string Photos) Setup(TempDirectory temp, Func<DateTime>? clock = null)
    {
        var root = temp.Combine("quarantine");
        Directory.CreateDirectory(root);
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));
        return (new Quarantine(root, log, clock ?? (() => Now)), log, root, Photos(temp));
    }

    [Fact]
    public void Hold_MovesTheFileUnderTheRoot_AndRecordsWhereItCameFrom()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, root, photos) = Setup(temp);
        var path = WriteFile(photos, "a.jpg", "A");

        var entry = quarantine.Hold(path, batchId: 3);

        Assert.False(File.Exists(path));
        Assert.StartsWith(root, entry.StoredPath);
        Assert.Equal("A", File.ReadAllText(entry.StoredPath));
        Assert.Equal(path, entry.OriginalPath);
        Assert.Equal(3, entry.BatchId);
        Assert.Equal(QuarantineState.Held, entry.State);
        Assert.Equal(entry, log.Get(entry.Id));
    }

    [Fact]
    public void Hold_TwoFilesWithTheSameName_DoNotCollide()
    {
        using var temp = new TempDirectory();
        var (quarantine, _, _, photos) = Setup(temp);
        var first = quarantine.Hold(WriteFile(photos, "a.jpg", "1"));
        var second = quarantine.Hold(WriteFile(photos, "a.jpg", "2"));

        Assert.NotEqual(first.StoredPath, second.StoredPath);
        Assert.Equal("1", File.ReadAllText(first.StoredPath));
        Assert.Equal("2", File.ReadAllText(second.StoredPath));
    }

    [Fact]
    public void Hold_MissingRoot_Throws()
    {
        using var temp = new TempDirectory();
        var log = new SqliteQuarantineLog(new CatalogDatabase(temp.Combine("catalog.db")));
        var quarantine = new Quarantine(temp.Combine("absent"), log);
        var path = WriteFile(Photos(temp), "a.jpg");

        Assert.Throws<DirectoryNotFoundException>(() => quarantine.Hold(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Hold_MissingFile_Throws()
    {
        using var temp = new TempDirectory();
        var (quarantine, _, _, photos) = Setup(temp);

        Assert.Throws<FileNotFoundException>(() => quarantine.Hold(Path.Combine(photos, "absent.jpg")));
    }

    [Fact]
    public void Restore_PutsTheFileBack_AndMarksTheEntry()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, _, photos) = Setup(temp);
        var path = WriteFile(photos, "a.jpg", "A");
        var entry = quarantine.Hold(path);

        quarantine.Restore(entry.Id);

        Assert.Equal("A", File.ReadAllText(path));
        Assert.False(File.Exists(entry.StoredPath));
        Assert.Equal(QuarantineState.Restored, log.Get(entry.Id)!.State);
    }

    [Fact]
    public void Restore_WhenTheOriginalNameIsTaken_Throws_AndKeepsBothFiles()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, _, photos) = Setup(temp);
        var path = WriteFile(photos, "a.jpg", "A");
        var entry = quarantine.Hold(path);
        WriteFile(photos, "a.jpg", "autre");

        Assert.Throws<IOException>(() => quarantine.Restore(entry.Id));

        Assert.Equal("autre", File.ReadAllText(path));
        Assert.Equal("A", File.ReadAllText(entry.StoredPath));
        Assert.Equal(QuarantineState.Held, log.Get(entry.Id)!.State);
    }

    [Fact]
    public void Restore_AnEntryThatIsNotHeld_Throws()
    {
        using var temp = new TempDirectory();
        var (quarantine, _, _, photos) = Setup(temp);
        var entry = quarantine.Hold(WriteFile(photos, "a.jpg"));
        quarantine.Restore(entry.Id);

        Assert.Throws<InvalidOperationException>(() => quarantine.Restore(entry.Id));
    }

    [Fact]
    public void Purge_DeletesTheStoredFile_AndMarksTheEntry()
    {
        using var temp = new TempDirectory();
        var (quarantine, log, _, photos) = Setup(temp);
        var entry = quarantine.Hold(WriteFile(photos, "a.jpg"));

        quarantine.Purge(entry.Id);

        Assert.False(File.Exists(entry.StoredPath));
        Assert.Equal(QuarantineState.Purged, log.Get(entry.Id)!.State);
    }

    [Fact]
    public void PurgeOlderThan_OnlyPurgesHeldEntriesPastTheAge()
    {
        using var temp = new TempDirectory();
        var clock = Now;
        var (quarantine, log, _, photos) = Setup(temp, () => clock);
        var old = quarantine.Hold(WriteFile(photos, "old.jpg"));
        clock = Now.AddDays(20);
        var recent = quarantine.Hold(WriteFile(photos, "recent.jpg"));
        clock = Now.AddDays(31);

        var purged = quarantine.PurgeOlderThan(TimeSpan.FromDays(30));

        Assert.Equal(1, purged);
        Assert.Equal(QuarantineState.Purged, log.Get(old.Id)!.State);
        Assert.Equal(QuarantineState.Held, log.Get(recent.Id)!.State);
        Assert.True(File.Exists(recent.StoredPath));
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~SqliteQuarantineLogTests|FullyQualifiedName~QuarantineTests"`
Expected : erreur de compilation (`SqliteQuarantineLog`, `Quarantine`, `QuarantineEntry` n'existent pas).

- [ ] **Step 3: Écrire le modèle et le journal de quarantaine**

`PhotoApp.Core/Batches/QuarantineModel.cs` :

```csharp
namespace PhotoApp.Core.Batches;

public enum QuarantineState
{
    Held,
    Restored,
    Purged
}

public sealed record QuarantineEntry(long Id, DateTime CreatedUtc, string OriginalPath, string StoredPath, long? BatchId, QuarantineState State);

/// <summary>Où est chaque fichier mis en quarantaine (cadre §7 : « chaque emplacement est enregistré dans la base »).</summary>
public interface IQuarantineLog
{
    long Add(DateTime createdUtc, string originalPath, string storedPath, long? batchId);

    QuarantineEntry? Get(long id);

    /// <summary>Les entrées, de la plus ancienne à la plus récente ; toutes si <paramref name="state"/> est nul.</summary>
    IReadOnlyList<QuarantineEntry> List(QuarantineState? state = null);

    void SetState(long id, QuarantineState state);
}
```

`PhotoApp.Catalog/SqliteQuarantineLog.cs` :

```csharp
using System.Globalization;
using PhotoApp.Core.Batches;

namespace PhotoApp.Catalog;

public sealed class SqliteQuarantineLog(CatalogDatabase database) : IQuarantineLog
{
    private const string Columns = "id, created_utc, original_path, stored_path, batch_id, state";

    public long Add(DateTime createdUtc, string originalPath, string storedPath, long? batchId)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO quarantine_entry (created_utc, original_path, stored_path, batch_id, state) " +
            "VALUES ($created, $original, $stored, $batch, $state); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$created", createdUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$original", originalPath);
        command.Parameters.AddWithValue("$stored", storedPath);
        command.Parameters.AddWithValue("$batch", (object?)batchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", QuarantineState.Held.ToString());
        return (long)command.ExecuteScalar()!;
    }

    public QuarantineEntry? Get(long id)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM quarantine_entry WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<QuarantineEntry> List(QuarantineState? state = null)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM quarantine_entry " + (state is null ? string.Empty : "WHERE state = $state ") + "ORDER BY id";
        if (state is { } value)
        {
            command.Parameters.AddWithValue("$state", value.ToString());
        }

        var entries = new List<QuarantineEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(Read(reader));
        }

        return entries;
    }

    public void SetState(long id, QuarantineState state)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE quarantine_entry SET state = $state WHERE id = $id";
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static QuarantineEntry Read(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt64(0),
        DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetInt64(4),
        Enum.Parse<QuarantineState>(reader.GetString(5)));
}
```

- [ ] **Step 4: Écrire la quarantaine**

`PhotoApp.Operations/Quarantine.cs` :

```csharp
using System.Globalization;
using PhotoApp.Core.Batches;

namespace PhotoApp.Operations;

/// <summary>
/// Quarantaine (cadre §7) : déplace un fichier sous un dossier donné, enregistre son emplacement d'origine, le restaure ou le purge.
/// Le dossier de quarantaine doit exister. Le fichier est déplacé avant d'être enregistré : un plantage entre les deux laisse un
/// fichier sous la racine, jamais un enregistrement sans fichier.
/// </summary>
public sealed class Quarantine(string rootFolder, IQuarantineLog log, Func<DateTime>? utcNow = null)
{
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public QuarantineEntry Hold(string path, long? batchId = null)
    {
        if (!Directory.Exists(rootFolder))
        {
            throw new DirectoryNotFoundException($"Le dossier de quarantaine n'existe pas : {rootFolder}");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fichier introuvable.", path);
        }

        var now = _utcNow();
        var stored = Path.Combine(rootFolder, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N"), Path.GetFileName(path));
        Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
        File.Move(path, stored, overwrite: false);
        return log.Get(log.Add(now, path, stored, batchId))!;
    }

    public void Restore(long id)
    {
        var entry = RequireHeld(id);
        if (File.Exists(entry.OriginalPath))
        {
            throw new IOException($"Le nom d'origine est occupé : {entry.OriginalPath}");
        }

        var folder = Path.GetDirectoryName(entry.OriginalPath)!;
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Le dossier d'origine n'existe plus : {folder}");
        }

        File.Move(entry.StoredPath, entry.OriginalPath, overwrite: false);
        RemoveEmptyFolder(entry.StoredPath);
        log.SetState(id, QuarantineState.Restored);
    }

    public void Purge(long id)
    {
        var entry = RequireHeld(id);
        File.Delete(entry.StoredPath);
        RemoveEmptyFolder(entry.StoredPath);
        log.SetState(id, QuarantineState.Purged);
    }

    /// <summary>Purge les entrées encore en quarantaine depuis plus que <paramref name="age"/> ; renvoie leur nombre.</summary>
    public int PurgeOlderThan(TimeSpan age)
    {
        var limit = _utcNow() - age;
        var purged = 0;
        foreach (var entry in log.List(QuarantineState.Held).Where(entry => entry.CreatedUtc < limit))
        {
            Purge(entry.Id);
            purged++;
        }

        return purged;
    }

    private QuarantineEntry RequireHeld(long id)
    {
        var entry = log.Get(id) ?? throw new InvalidOperationException($"Entrée de quarantaine {id} introuvable.");
        return entry.State == QuarantineState.Held
            ? entry
            : throw new InvalidOperationException($"L'entrée {id} n'est plus en quarantaine ({entry.State}).");
    }

    private static void RemoveEmptyFolder(string storedPath)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(storedPath)!);   // sans récursivité : échoue s'il reste quelque chose
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nettoyage au mieux.
        }
    }
}
```

- [ ] **Step 5: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~SqliteQuarantineLogTests|FullyQualifiedName~QuarantineTests"`
Expected : PASS (12 tests).

- [ ] **Step 6: Commit**

```bash
git add PhotoApp.Core PhotoApp.Catalog PhotoApp.Operations PhotoApp.Tests
git commit -m "feat: brique de quarantaine (mise en quarantaine, restauration, purge, emplacements enregistrés)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---
### Task 11: Façade `RenameWorkflow` et test de bout en bout (Operations)

La CLI et l'interface préparent et appliquent un lot de la même façon : analyse → plan → contrôles → journal → exécution. Cette façade évite de l'écrire deux fois. Le test d'intégration de cette tâche vérifie le **critère de fin de la phase** : renommer puis annuler redonne un état identique (noms, contenus, dates).

**Files:**
- Create: `PhotoApp.Operations/RenameWorkflow.cs`
- Test: `PhotoApp.Tests/RenameWorkflowTests.cs`

**Interfaces:**
- Consumes : `BatchPlanner`, `BatchPreflight`, `PreflightEntry`, `InPlaceRenamer`, `BatchUndo`, `IBatchJournal`, `MediaResult`.
- Produces : `PreparedRename(BatchPlan Plan, IReadOnlyList<string> Folders, IReadOnlyDictionary<int, string> Skipped)` avec `Renamable : int` (éléments du plan moins les écartés) ; `new RenameWorkflow(IBatchJournal journal, TimeZoneInfo? zone = null)` avec `Prepare(IReadOnlyList<MediaResult> results, IReadOnlyList<string> folders, int? maxGroups = null) : PreparedRename` (aucune écriture) et `Apply(PreparedRename prepared, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null) : BatchRunSummary` (enregistre le lot puis l'exécute).

- [ ] **Step 1: Écrire les tests**

`PhotoApp.Tests/RenameWorkflowTests.cs` :

```csharp
using PhotoApp.Core.Batches;
using PhotoApp.Core.Normalization;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class RenameWorkflowTests
{
    private static string Library(TempDirectory temp)
    {
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");   // EXIF seul : Probable
        File.WriteAllText(Path.Combine(photos, "holiday.xmp"), "<x/>");                      // son sidecar
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));                // WhatsApp : Sûre
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));                             // aucune date : jamais renommé
        TestVideos.Write(Path.Combine(photos, "20260917_222944.mp4"), new DateTime(2026, 9, 17, 20, 29, 44), brand: "isom");
        return photos;
    }

    private static IReadOnlyList<MediaResult> Analyze(string photos) => new MediaAnalyzer().Analyze([photos], []).Results;

    [Fact]
    public void ApplyThenUndo_GivesBackAnIdenticalState()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);

        var summary = workflow.Apply(workflow.Prepare(Analyze(photos), [photos]));

        Assert.Equal(0, summary.Counts.Failed);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.xmp")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
        Assert.True(File.Exists(Path.Combine(photos, "blank.jpg")));
        Assert.False(File.Exists(Path.Combine(photos, "holiday.jpg")));
        Assert.Equal(new DateTime(2023, 3, 4, 12, 20, 15, DateTimeKind.Utc), File.GetLastWriteTimeUtc(Path.Combine(photos, "IMG-20230304-132015.jpg")));

        var undo = new BatchUndo(journal).Undo(summary.BatchId);

        Assert.True(undo.Completed);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(BatchState.Undone, journal.GetBatch(summary.BatchId)!.State);
    }

    [Fact]
    public void Apply_ThenPrepareAgain_FindsNothingLeftToRename()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var workflow = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne);
        workflow.Apply(workflow.Prepare(Analyze(photos), [photos]));

        var second = workflow.Prepare(Analyze(photos), [photos]);

        Assert.Empty(second.Plan.Items);
        Assert.True(second.Plan.Summary.AlreadyConforming >= 2);
    }

    [Fact]
    public void Prepare_ATargetTakenByAFolder_IsSkipped()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        Directory.CreateDirectory(Path.Combine(photos, "IMG-20230304-132015.jpg"));

        var prepared = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne).Prepare(Analyze(photos), [photos]);

        Assert.Equal(2, prepared.Plan.Items.Count);
        Assert.Equal(1, prepared.Skipped.Count);
        Assert.Equal(1, prepared.Renamable);
        Assert.Equal("Le nom cible existe déjà", prepared.Skipped.Values.Single());
    }

    [Fact]
    public void Prepare_WritesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);

        new RenameWorkflow(journal, TestZones.PlusOne).Prepare(Analyze(photos), [photos]);

        Assert.Equal(before, Snapshot(photos));
        Assert.Empty(journal.ListBatches());
    }

    [Fact]
    public void Apply_AnEmptyPlan_Throws()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        var workflow = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne);

        Assert.Throws<InvalidOperationException>(() => workflow.Apply(workflow.Prepare(Analyze(photos), [photos])));
    }

    [Fact]
    public void Apply_WhileABatchIsUnfinished_Throws_AndTouchesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var journal = NewJournal(temp);
        var stuck = journal.CreateBatch(DateTime.UtcNow, [photos], []);
        journal.SetBatchState(stuck, BatchState.Interrupted);
        var workflow = new RenameWorkflow(journal, TestZones.PlusOne);
        var prepared = workflow.Prepare(Analyze(photos), [photos]);

        Assert.Throws<InvalidOperationException>(() => workflow.Apply(prepared));

        Assert.Equal(before, Snapshot(photos));
        Assert.Single(journal.ListBatches());
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~RenameWorkflowTests"`
Expected : erreur de compilation (`RenameWorkflow` n'existe pas).

- [ ] **Step 3: Écrire la façade**

`PhotoApp.Operations/RenameWorkflow.cs` :

```csharp
using PhotoApp.Core.Batches;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Operations;

/// <param name="Skipped">Rangs du plan que les contrôles préalables écartent, avec la raison.</param>
public sealed record PreparedRename(BatchPlan Plan, IReadOnlyList<string> Folders, IReadOnlyDictionary<int, string> Skipped)
{
    public int Renamable => Plan.Items.Count - Skipped.Count;
}

/// <summary>
/// Préparer puis appliquer un lot de renommage sur place : le même enchaînement pour la CLI et l'interface.
/// <see cref="Prepare"/> n'écrit rien ; <see cref="Apply"/> enregistre le lot dans le journal puis l'exécute.
/// </summary>
public sealed class RenameWorkflow(IBatchJournal journal, TimeZoneInfo? zone = null)
{
    public PreparedRename Prepare(IReadOnlyList<MediaResult> results, IReadOnlyList<string> folders, int? maxGroups = null)
    {
        var plan = new BatchPlanner(zone).Plan(results, maxGroups);
        var skipped = BatchPreflight.Evaluate(plan.Items
            .Select(item => new PreflightEntry(item.Rank, item.GroupId, item.SourcePath, item.TargetPath, item.SourceSize, item.SourceLastWriteUtc))
            .ToList());
        return new PreparedRename(plan, folders, skipped);
    }

    public BatchRunSummary Apply(PreparedRename prepared, CancellationToken cancellation = default, IProgress<BatchProgress>? progress = null)
    {
        if (prepared.Plan.Items.Count == 0)
        {
            throw new InvalidOperationException("Le plan est vide : rien à renommer.");
        }

        if (journal.FindUnfinished() is { } unfinished)
        {
            throw new InvalidOperationException(
                $"Le lot {unfinished.Id} est interrompu : terminez-le (reprend) ou annulez-le (annule) avant d'en lancer un autre.");
        }

        var batchId = journal.CreateBatch(DateTime.UtcNow, prepared.Folders, prepared.Plan.Items);
        return new InPlaceRenamer(journal).Run(batchId, cancellation, progress);
    }
}
```

- [ ] **Step 4: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~RenameWorkflowTests"`
Expected : PASS (6 tests). Si `ApplyThenUndo_…` échoue sur une comparaison d'état, regarder **quel fichier** diffère (nom, hachage, date de création ou de modification) avant de toucher au code : c'est exactement le défaut que ce test existe pour attraper. Si `Apply_ThenPrepareAgain_…` trouve des éléments, afficher leurs `SourcePath` : un fichier renommé qui reste « à renommer » est un défaut du moteur de phase 1 (le nom produit n'est pas reconnu comme déjà conforme), à corriger à la source.

- [ ] **Step 5: Commit**

```bash
git add PhotoApp.Operations PhotoApp.Tests
git commit -m "feat: façade RenameWorkflow (Prepare / Apply) et test de bout en bout renommer puis annuler" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Commande `photoapp renomme` (Cli)

**Files:**
- Modify: `PhotoApp.Cli/PhotoApp.Cli.csproj`
- Modify: `PhotoApp.Cli/AnalyseCommand.cs` (`ExitCodes`)
- Modify: `PhotoApp.Cli/Report/CsvReportWriter.cs` (`Escape` devient `internal`)
- Create: `PhotoApp.Cli/Confirmation.cs`, `PhotoApp.Cli/SyncFolderDetector.cs`, `PhotoApp.Cli/BatchConsoleProgress.cs`, `PhotoApp.Cli/BatchSummaryPrinter.cs`, `PhotoApp.Cli/Report/PlanCsvWriter.cs`, `PhotoApp.Cli/RenommeCommand.cs`
- Test: `PhotoApp.Tests/CliTestSupport.cs`, `PhotoApp.Tests/ConfirmationTests.cs`, `PhotoApp.Tests/SyncFolderDetectorTests.cs`, `PhotoApp.Tests/PlanCsvWriterTests.cs`, `PhotoApp.Tests/RenommeCommandTests.cs`

**Interfaces:**
- Consumes : `RenameWorkflow`, `PreparedRename`, `BatchRunSummary`, `BatchProgress`, `SqliteBatchJournal`, `CatalogDatabase`, `MediaAnalyzer`, `AppDataPaths`.
- Produces : `ExitCodes.BatchIssues = 3` ; `Confirmation.Ask(string question, TextReader input, TextWriter output) : bool` ; `SyncFolderDetector.IsSynced(string folder) : bool` ; `new BatchConsoleProgress(TextWriter writer, string label)` (`IProgress<BatchProgress>`) ; `BatchSummaryPrinter.PrintRun(TextWriter, IBatchJournal, BatchRunSummary)`, `PrintProblems(TextWriter, IBatchJournal, long batchId, params ItemState[] states)`, `StateLabel(ItemState)`, `BatchStateLabel(BatchState)` ; `PlanCsvWriter.WriteFile(string path, PreparedRename)` ; `RenommeRequest(IReadOnlyList<string> Folders, IReadOnlyList<string> Excluded, bool Apply, bool AssumeYes, int? Slice)` ; `new RenommeCommand(AppDataPaths paths, TextWriter output, TextWriter error, TextReader input, Func<DateTime>? localNow = null)` avec `Run(RenommeRequest, CancellationToken = default) : int`.

- [ ] **Step 1: La CLI référence maintenant `Operations`**

La phase 1 interdisait cette référence parce que la CLI était en lecture seule ; `renomme` écrit (renommage sur place) et passe exclusivement par `RenameWorkflow`. Dans `PhotoApp.Cli/PhotoApp.Cli.csproj`, remplacer

```xml
  <!-- Jamais PhotoApp.Operations : la CLI est en lecture seule (spec de phase 1, §9). -->
  <ItemGroup>
    <ProjectReference Include="..\PhotoApp.Core\PhotoApp.Core.csproj" />
    <ProjectReference Include="..\PhotoApp.Metadata\PhotoApp.Metadata.csproj" />
    <ProjectReference Include="..\PhotoApp.Catalog\PhotoApp.Catalog.csproj" />
  </ItemGroup>
```

par

```xml
  <!-- Operations seulement pour le renommage sur place (RenameWorkflow, BatchUndo, InPlaceRenamer) ; la commande analyse reste en lecture seule. -->
  <ItemGroup>
    <ProjectReference Include="..\PhotoApp.Core\PhotoApp.Core.csproj" />
    <ProjectReference Include="..\PhotoApp.Metadata\PhotoApp.Metadata.csproj" />
    <ProjectReference Include="..\PhotoApp.Catalog\PhotoApp.Catalog.csproj" />
    <ProjectReference Include="..\PhotoApp.Operations\PhotoApp.Operations.csproj" />
  </ItemGroup>
```

Dans `PhotoApp.Cli/AnalyseCommand.cs`, classe `ExitCodes`, ajouter après `Fatal = 2` :

```csharp
    public const int BatchIssues = 3;   // lot terminé avec des échecs, ou interrompu
```

Dans `PhotoApp.Cli/Report/CsvReportWriter.cs`, remplacer `private static string Escape(string value) =>` par `internal static string Escape(string value) =>`.

- [ ] **Step 2: Écrire l'aide de test et les tests des petites briques**

`PhotoApp.Tests/CliTestSupport.cs` :

```csharp
using PhotoApp.Core;

namespace PhotoApp.Tests;

/// <summary>Aides communes aux tests des commandes de la CLI.</summary>
internal static class CliTestSupport
{
    public static readonly DateTime Clock = new(2026, 10, 19, 10, 15, 0);

    public static AppDataPaths PathsUnder(string root) =>
        AppDataPaths.Resolve(PlatformKind.Windows, name => name == "LOCALAPPDATA" ? root : null, root);

    public static SqliteBatchJournal OpenJournal(AppDataPaths paths)
    {
        paths.EnsureCreated();
        return new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
    }

    /// <summary>Deux fichiers à renommer (holiday.jpg : Probable, IMG-20210915-WA0005.png : Sûre) et un fichier sans date.</summary>
    public static string Library(TempDirectory temp)
    {
        var photos = BatchTestSupport.Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));
        return photos;
    }
}
```

`PhotoApp.Tests/ConfirmationTests.cs` :

```csharp
using PhotoApp.Cli;

namespace PhotoApp.Tests;

public sealed class ConfirmationTests
{
    [Theory]
    [InlineData("o", true)]
    [InlineData("OUI", true)]
    [InlineData("y", true)]
    [InlineData("Yes", true)]
    [InlineData(" o ", true)]
    [InlineData("n", false)]
    [InlineData("non", false)]
    [InlineData("", false)]
    [InlineData("peut-être", false)]
    public void Ask_AcceptsOnlyAnExplicitYes(string answer, bool expected)
    {
        var output = new StringWriter();

        Assert.Equal(expected, Confirmation.Ask("Continuer ?", new StringReader(answer + "\n"), output));
        Assert.Contains("Continuer ? [o/N]", output.ToString());
    }

    [Fact]
    public void Ask_EndOfInput_IsNo()
    {
        Assert.False(Confirmation.Ask("Continuer ?", new StringReader(string.Empty), new StringWriter()));
    }
}
```

`PhotoApp.Tests/SyncFolderDetectorTests.cs` :

```csharp
using PhotoApp.Cli;

namespace PhotoApp.Tests;

public sealed class SyncFolderDetectorTests
{
    [Fact]
    public void IsSynced_AFolderUnderANextcloudRoot_IsDetected()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("Nextcloud");
        var sub = Path.Combine(root, "Pictures", "2024");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(root, ".sync_1a2b3c.db"), string.Empty);

        Assert.True(SyncFolderDetector.IsSynced(sub));
        Assert.True(SyncFolderDetector.IsSynced(root));
    }

    [Fact]
    public void IsSynced_TheOlderMarkerNames_AreDetected()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("Cloud");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".nextcloudsync.log"), string.Empty);

        Assert.True(SyncFolderDetector.IsSynced(root));
    }

    [Fact]
    public void IsSynced_AnOrdinaryFolder_IsNot()
    {
        using var temp = new TempDirectory();
        var folder = temp.Combine("photos");
        Directory.CreateDirectory(folder);

        Assert.False(SyncFolderDetector.IsSynced(folder));
    }
}
```

`PhotoApp.Tests/PlanCsvWriterTests.cs` :

```csharp
using PhotoApp.Cli.Report;
using static PhotoApp.Tests.BatchTestSupport;

namespace PhotoApp.Tests;

public sealed class PlanCsvWriterTests
{
    [Fact]
    public void Write_ListsEveryItem_WithItsStateAndReason()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        Directory.CreateDirectory(Path.Combine(photos, "IMG-20230304-132015.jpg"));
        var results = new MediaAnalyzer().Analyze([photos], []).Results;
        var prepared = new RenameWorkflow(NewJournal(temp), TestZones.PlusOne).Prepare(results, [photos]);
        var writer = new StringWriter();

        PlanCsvWriter.Write(writer, prepared);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("groupe;role;chemin;cible;date;etat;motif", lines[0]);
        Assert.Equal(3, lines.Length);   // en-tête + 2 éléments
        Assert.Contains(lines, line => line.Contains("IMG-20210915-WA0005.png") && line.Contains(";a-renommer;"));
        Assert.Contains(lines, line => line.Contains("holiday.jpg") && line.Contains(";ecarte;Le nom cible existe déjà"));
    }
}
```

- [ ] **Step 3: Écrire les tests de la commande**

`PhotoApp.Tests/RenommeCommandTests.cs` :

```csharp
using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class RenommeCommandTests
{
    private static RenommeRequest Request(string photos, bool apply = false, bool yes = false, int? slice = null) =>
        new([photos], [], apply, yes, slice);

    private static RenommeCommand Command(AppDataPaths paths, StringWriter output, StringWriter error, string answers = "") =>
        new(paths, output, error, new StringReader(answers), () => Clock);

    [Fact]
    public void DryRun_WritesThePlan_AndTouchesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains("À renommer : 2 fichier(s)", output.ToString());
        Assert.Contains("Aucune modification", output.ToString());
        Assert.True(File.Exists(Path.Combine(paths.ReportsFolder, "renomme-20261019-101500.csv")));
        Assert.Empty(OpenJournal(paths).ListBatches());
    }

    [Fact]
    public void Apply_WithYes_RenamesInPlace_AndRecordsTheBatch()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos, apply: true, yes: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
        Assert.True(File.Exists(Path.Combine(photos, "blank.jpg")));
        Assert.Contains("Lot 1 : 2 renommé(s), 0 en échec, 0 écarté(s).", output.ToString());
        var batch = Assert.Single(OpenJournal(paths).ListBatches());
        Assert.Equal(BatchState.Completed, batch.State);
        Assert.Equal(2, batch.Counts.Done);
    }

    [Fact]
    public void Apply_AnsweringYes_RenamesInPlace()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var output = new StringWriter();

        var exitCode = Command(PathsUnder(temp.Combine("data")), output, new StringWriter(), "o\n").Run(Request(photos, apply: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.Contains("Renommer 2 fichier(s) sur place ? [o/N]", output.ToString());
    }

    [Fact]
    public void Apply_AnsweringNo_ChangesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter(), "n\n").Run(Request(photos, apply: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains("Abandon", output.ToString());
        Assert.Empty(OpenJournal(paths).ListBatches());
    }

    [Fact]
    public void Apply_WithAnUnfinishedBatch_IsRefused()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var journal = OpenJournal(paths);
        var stuck = journal.CreateBatch(DateTime.UtcNow, [photos], []);
        journal.SetBatchState(stuck, BatchState.Interrupted);
        var before = Snapshot(photos);
        var error = new StringWriter();

        var exitCode = Command(paths, new StringWriter(), error).Run(Request(photos, apply: true, yes: true));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains($"photoapp reprend {stuck}", error.ToString());
    }

    [Fact]
    public void Apply_Cancelled_LeavesAnInterruptedBatch_AndReturnsCode3()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos, apply: true, yes: true), cancellation.Token);

        Assert.Equal(ExitCodes.BatchIssues, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(BatchState.Interrupted, Assert.Single(OpenJournal(paths).ListBatches()).State);
        Assert.Contains("photoapp reprend 1", output.ToString());
    }

    [Fact]
    public void Slice_KeepsTheFirstGroupsOnly()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var output = new StringWriter();

        Command(PathsUnder(temp.Combine("data")), output, new StringWriter()).Run(Request(photos, apply: true, yes: true, slice: 1));

        Assert.Contains("À renommer : 1 fichier(s)", output.ToString());
        Assert.True(File.Exists(Path.Combine(photos, "holiday.jpg")));   // le second groupe attend la tranche suivante
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
    }

    [Fact]
    public void NothingToRename_SaysSo_AndRecordsNoBatch()
    {
        using var temp = new TempDirectory();
        var photos = Photos(temp);
        TestImages.WriteJpeg(Path.Combine(photos, "blank.jpg"));
        var paths = PathsUnder(temp.Combine("data"));
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(Request(photos, apply: true, yes: true));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Rien à renommer.", output.ToString());
        Assert.Empty(OpenJournal(paths).ListBatches());
    }

    [Fact]
    public void MissingFolder_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = Command(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(Request(temp.Combine("absent")));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Dossier introuvable", error.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveSlice_IsAnInvalidArgument(int slice)
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);

        var exitCode = Command(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter()).Run(Request(photos, slice: slice));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
    }

    [Fact]
    public void ThePlanReport_IsNeverWrittenInsideAnAnalysedFolder()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var before = Snapshot(photos);
        var error = new StringWriter();

        // Les données de l'application sont (par erreur) sous le dossier analysé : le rapport y irait.
        var exitCode = Command(PathsUnder(photos), new StringWriter(), error).Run(Request(photos));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("dossier analysé", error.ToString());
        Assert.Equal(before, Snapshot(photos));
    }
}
```

- [ ] **Step 4: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~ConfirmationTests|FullyQualifiedName~SyncFolderDetectorTests|FullyQualifiedName~PlanCsvWriterTests|FullyQualifiedName~RenommeCommandTests"`
Expected : erreur de compilation (`Confirmation`, `SyncFolderDetector`, `PlanCsvWriter`, `RenommeCommand`, `RenommeRequest` n'existent pas).

- [ ] **Step 5: Écrire les petites briques**

`PhotoApp.Cli/Confirmation.cs` :

```csharp
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
```

`PhotoApp.Cli/SyncFolderDetector.cs` :

```csharp
namespace PhotoApp.Cli;

/// <summary>Reconnaît un dossier synchronisé par Nextcloud (cadre §7) : un fichier de base de synchronisation dans le dossier ou l'un de ses parents.</summary>
public static class SyncFolderDetector
{
    private static readonly string[] Markers = [".nextcloudsync.log", ".csync_journal.db", ".sync_journal.db"];

    public static bool IsSynced(string folder)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(folder)); current is not null; current = current.Parent)
        {
            try
            {
                if (Markers.Any(marker => File.Exists(Path.Combine(current.FullName, marker)))
                    || current.EnumerateFiles(".sync_*.db").Any())
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Dossier parent illisible : on continue vers le haut.
            }
        }

        return false;
    }
}
```

`PhotoApp.Cli/BatchConsoleProgress.cs` :

```csharp
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Progression sur la sortie d'erreur, réécrite sur la même ligne tous les 100 fichiers et à la fin.</summary>
public sealed class BatchConsoleProgress(TextWriter writer, string label) : IProgress<BatchProgress>
{
    public void Report(BatchProgress value)
    {
        if (value.Processed % 100 != 0 && value.Processed != value.Total)
        {
            return;
        }

        writer.Write($"\r{label} : {value.Processed} / {value.Total}");
    }
}
```

`PhotoApp.Cli/BatchSummaryPrinter.cs` :

```csharp
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Textes de fin de lot et libellés d'états, partagés par les commandes.</summary>
public static class BatchSummaryPrinter
{
    private const int MaxListed = 20;

    public static void PrintRun(TextWriter output, IBatchJournal journal, BatchRunSummary summary)
    {
        var counts = summary.Counts;
        output.WriteLine($"Lot {summary.BatchId} : {counts.Done} renommé(s), {counts.Failed} en échec, {counts.Skipped} écarté(s).");
        PrintProblems(output, journal, summary.BatchId, ItemState.Failed, ItemState.Skipped);
        if (summary.Cancelled)
        {
            output.WriteLine($"Lot interrompu : « photoapp reprend {summary.BatchId} » pour le terminer, ou « photoapp annule {summary.BatchId} » pour l'annuler.");
        }
        else
        {
            output.WriteLine($"Détail : photoapp journal --lot {summary.BatchId} ; pour revenir en arrière : photoapp annule {summary.BatchId}");
        }
    }

    /// <summary>Les fichiers du lot dans les états demandés, avec leur message, au plus 20 lignes.</summary>
    public static void PrintProblems(TextWriter output, IBatchJournal journal, long batchId, params ItemState[] states)
    {
        var problems = journal.GetItems(batchId).Where(item => states.Contains(item.State)).ToList();
        foreach (var item in problems.Take(MaxListed))
        {
            output.WriteLine($"  {StateLabel(item.State)} : {item.SourcePath} — {item.Error}");
        }

        if (problems.Count > MaxListed)
        {
            output.WriteLine($"  … et {problems.Count - MaxListed} autre(s) : photoapp journal --lot {batchId}");
        }
    }

    public static string StateLabel(ItemState state) => state switch
    {
        ItemState.Pending => "en attente",
        ItemState.Running => "en cours",
        ItemState.Done => "fait",
        ItemState.Failed => "échec",
        ItemState.Skipped => "écarté",
        ItemState.Undone => "annulé",
        ItemState.UndoFailed => "annulation impossible",
        _ => state.ToString()
    };

    public static string BatchStateLabel(BatchState state) => state switch
    {
        BatchState.Planned => "Planifié",
        BatchState.Running => "En cours",
        BatchState.Completed => "Terminé",
        BatchState.Interrupted => "Interrompu",
        BatchState.Undone => "Annulé",
        _ => state.ToString()
    };
}
```

`PhotoApp.Cli/Report/PlanCsvWriter.cs` :

```csharp
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
```

La classe `CsvReportWriter` est dans l'espace de noms `PhotoApp.Cli.Report`, le même que `PlanCsvWriter` : aucun `using` supplémentaire.

- [ ] **Step 6: Écrire la commande**

`PhotoApp.Cli/RenommeCommand.cs` :

```csharp
using System.Globalization;
using PhotoApp.Catalog;
using PhotoApp.Cli.Report;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <param name="Apply">Sans lui, la commande n'est qu'un aperçu : aucun fichier n'est modifié.</param>
/// <param name="AssumeYes">Pas de confirmation interactive (sans effet sans <paramref name="Apply"/>).</param>
/// <param name="Slice">Ne traite que les n premiers groupes du plan (dossiers synchronisés).</param>
public sealed record RenommeRequest(IReadOnlyList<string> Folders, IReadOnlyList<string> Excluded, bool Apply, bool AssumeYes, int? Slice);

/// <summary>
/// Commande « renomme » (spec de phase 2, §7) : analyse, plan, et, avec --applique, renommage sur place journalisé et annulable.
/// Le plan détaillé va dans le dossier reports des données de l'application, jamais dans un dossier analysé.
/// </summary>
public sealed class RenommeCommand(AppDataPaths paths, TextWriter output, TextWriter error, TextReader input, Func<DateTime>? localNow = null)
{
    /// <summary>Au-delà, un dossier synchronisé déclenche l'avertissement sur les gros lots.</summary>
    public const int SyncWarningThreshold = 500;

    private readonly Func<DateTime> _localNow = localNow ?? (() => DateTime.Now);

    public int Run(RenommeRequest request, CancellationToken cancellation = default)
    {
        if (request.Slice is <= 0)
        {
            error.WriteLine("--tranche doit être un entier positif.");
            return ExitCodes.InvalidArguments;
        }

        var folders = request.Folders.Select(folder => Path.GetFullPath(folder)).ToList();
        var missing = folders.FirstOrDefault(folder => !Directory.Exists(folder));
        if (missing is not null)
        {
            error.WriteLine($"Dossier introuvable : {missing}");
            return ExitCodes.InvalidArguments;
        }

        // La garde passe avant toute création de dossier : rien n'est écrit dans un dossier analysé.
        var reportPath = Path.Combine(paths.ReportsFolder, "renomme-" + _localNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
        if (folders.Any(folder => AnalyseCommand.IsInsideAnalysedFolder(reportPath, folder)))
        {
            error.WriteLine($"Le plan ne doit pas être écrit dans un dossier analysé : {reportPath}");
            return ExitCodes.InvalidArguments;
        }

        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
        if (journal.FindUnfinished() is { } unfinished)
        {
            error.WriteLine($"Le lot {unfinished.Id} est interrompu : « photoapp reprend {unfinished.Id} » pour le terminer, ou « photoapp annule {unfinished.Id} » pour l'annuler.");
            if (request.Apply)
            {
                return ExitCodes.InvalidArguments;
            }
        }

        var excluded = request.Excluded.Select(folder => Path.GetFullPath(folder)).ToList();
        var analysis = new MediaAnalyzer().Analyze(folders, excluded, new ConsoleProgress(error));
        error.WriteLine();

        var workflow = new RenameWorkflow(journal);
        var prepared = workflow.Prepare(analysis.Results, folders, request.Slice);
        try
        {
            PlanCsvWriter.WriteFile(reportPath, prepared);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"Impossible d'écrire le plan {reportPath} : {ex.Message}");
            return ExitCodes.Fatal;
        }

        PrintPlan(prepared, reportPath);
        if (prepared.Renamable == 0)
        {
            output.WriteLine("Rien à renommer.");
            return ExitCodes.Success;
        }

        if (!request.Apply)
        {
            output.WriteLine("Aucune modification : ajoutez --applique pour exécuter ce plan.");
            return ExitCodes.Success;
        }

        if (prepared.Renamable >= SyncWarningThreshold && folders.Any(SyncFolderDetector.IsSynced))
        {
            error.WriteLine(
                $"Attention : dossier synchronisé (Nextcloud) et lot de {prepared.Renamable} fichiers. " +
                "Pour ménager la synchronisation, --tranche <n> traite le plan par tranches de n groupes.");
        }

        if (!request.AssumeYes && !Confirmation.Ask($"Renommer {prepared.Renamable} fichier(s) sur place ?", input, output))
        {
            output.WriteLine("Abandon, aucun fichier modifié.");
            return ExitCodes.Success;
        }

        BatchRunSummary summary;
        try
        {
            summary = workflow.Apply(prepared, cancellation, new BatchConsoleProgress(error, "Renommage"));
        }
        catch (BatchLockedException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Fatal;
        }

        error.WriteLine();
        BatchSummaryPrinter.PrintRun(output, journal, summary);
        return summary.Cancelled || summary.Counts.Failed > 0 ? ExitCodes.BatchIssues : ExitCodes.Success;
    }

    private void PrintPlan(PreparedRename prepared, string reportPath)
    {
        output.WriteLine($"À renommer : {prepared.Renamable} fichier(s) ({prepared.Plan.Summary.Groups} groupe(s))");
        output.WriteLine($"Déjà conformes : {prepared.Plan.Summary.AlreadyConforming}");
        output.WriteLine($"Écartés par les contrôles : {prepared.Skipped.Count}");
        output.WriteLine($"Conversion différée (HEIC, HEIF, AVIF) : {prepared.Plan.Summary.DeferredConversion}");
        output.WriteLine($"Plan détaillé : {reportPath}");
    }
}
```

- [ ] **Step 7: Lancer les tests**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~ConfirmationTests|FullyQualifiedName~SyncFolderDetectorTests|FullyQualifiedName~PlanCsvWriterTests|FullyQualifiedName~RenommeCommandTests"`
Expected : PASS. Si `Slice_KeepsTheFirstGroupsOnly` échoue sur l'ordre des groupes, vérifier le tri du planificateur (ordre ordinal du chemin : `IMG-20210915-WA0005.png` avant `holiday.jpg`, car `I` < `h` en ordre ordinal).

Puis la suite entière : `dotnet test PhotoVisualizer.slnx` — Expected : PASS (les tests de la phase 1 sur la CLI restent verts).

- [ ] **Step 8: Commit**

```bash
git add PhotoApp.Cli PhotoApp.Tests
git commit -m "feat: commande renomme (aperçu par défaut, --applique, --oui, --tranche, plan CSV, avertissement Nextcloud)" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 13: Commandes `journal`, `annule`, `reprend` et branchement dans `Program.cs` (Cli)

**Files:**
- Create: `PhotoApp.Cli/JournalCommand.cs`, `PhotoApp.Cli/AnnuleCommand.cs`, `PhotoApp.Cli/ReprendCommand.cs`
- Modify: `PhotoApp.Cli/Program.cs`
- Test: `PhotoApp.Tests/JournalCommandTests.cs`, `PhotoApp.Tests/AnnuleCommandTests.cs`, `PhotoApp.Tests/ReprendCommandTests.cs`

**Interfaces:**
- Consumes : `BatchSummaryPrinter`, `Confirmation`, `BatchConsoleProgress`, `BatchUndo`, `InPlaceRenamer`, `SqliteBatchJournal`, `CatalogDatabase`.
- Produces : `new JournalCommand(AppDataPaths, TextWriter output, TextWriter error)` avec `Run(long? batchId) : int` ; `new AnnuleCommand(AppDataPaths, TextWriter output, TextWriter error, TextReader input)` avec `Run(long? batchId, bool assumeYes, CancellationToken = default) : int` ; `new ReprendCommand(AppDataPaths, TextWriter output, TextWriter error)` avec `Run(long batchId, CancellationToken = default) : int`. Codes : 0 succès, 1 argument invalide, 2 erreur fatale (verrou), 3 échecs ou lot non terminé.

- [ ] **Step 1: Écrire les tests**

`PhotoApp.Tests/JournalCommandTests.cs` :

```csharp
using PhotoApp.Cli;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class JournalCommandTests
{
    [Fact]
    public void Run_WithoutBatches_SaysSo()
    {
        using var temp = new TempDirectory();
        var output = new StringWriter();

        var exitCode = new JournalCommand(PathsUnder(temp.Combine("data")), output, new StringWriter()).Run(null);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Aucun lot", output.ToString());
    }

    [Fact]
    public void Run_ListsTheBatches_WithTheirCounts()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        var output = new StringWriter();

        var exitCode = new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("Terminé", output.ToString());
        Assert.Contains("2 fait(s), 0 échec(s), 0 écarté(s)", output.ToString());
    }

    [Fact]
    public void Run_WithALot_ShowsEachFileWithItsStateAndError()
    {
        using var temp = new TempDirectory();
        var paths = PathsUnder(temp.Combine("data"));
        var photos = Photos(temp);
        var journal = OpenJournal(paths);
        var source = WriteFile(photos, "a.jpg");
        var batch = CreateBatch(journal, DraftFor(source, Path.Combine(photos, "IMG-1.jpg"), 0));
        File.WriteAllText(source, "modifié");   // le contrôle préalable l'écartera
        new InPlaceRenamer(journal).Run(batch);
        var output = new StringWriter();

        var exitCode = new JournalCommand(paths, output, new StringWriter()).Run(batch);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("a.jpg", output.ToString());
        Assert.Contains("IMG-1.jpg", output.ToString());
        Assert.Contains("écarté", output.ToString());
        Assert.Contains("Fichier modifié depuis le plan", output.ToString());
    }

    [Fact]
    public void Run_UnknownLot_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = new JournalCommand(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(42);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Lot 42 introuvable", error.ToString());
    }

    [Fact]
    public void Run_MentionsAnUnfinishedBatch()
    {
        using var temp = new TempDirectory();
        var paths = PathsUnder(temp.Combine("data"));
        var journal = OpenJournal(paths);
        var stuck = journal.CreateBatch(DateTime.UtcNow, [@"C:\photos"], []);
        journal.SetBatchState(stuck, BatchState.Interrupted);
        var output = new StringWriter();

        new JournalCommand(paths, output, new StringWriter()).Run(null);

        Assert.Contains($"photoapp reprend {stuck}", output.ToString());
    }
}
```

`PhotoApp.Tests/AnnuleCommandTests.cs` :

```csharp
using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class AnnuleCommandTests
{
    private static void Rename(AppDataPaths paths, string photos, CancellationToken cancellation = default) =>
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null), cancellation);

    private static AnnuleCommand Command(AppDataPaths paths, StringWriter output, StringWriter error, string answers = "") =>
        new(paths, output, error, new StringReader(answers));

    [Fact]
    public void Run_UndoesTheLastBatch_AndGivesBackTheOriginalState()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        Rename(paths, photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Contains("Lot 1 : 2 restauré(s), 0 non restauré(s).", output.ToString());
        Assert.Contains("Lot annulé.", output.ToString());
        Assert.Equal(BatchState.Undone, OpenJournal(paths).GetBatch(1)!.State);
    }

    [Fact]
    public void Run_AsksForConfirmation_AndAnsweringNoChangesNothing()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        var renamed = Snapshot(photos);
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter(), "n\n").Run(null, assumeYes: false);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(renamed, Snapshot(photos));
        Assert.Contains("Annuler le lot 1 (2 fichier(s) à restaurer) ? [o/N]", output.ToString());
        Assert.Contains("Abandon", output.ToString());
    }

    [Fact]
    public void Run_AnsweringYes_Undoes()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        var before = Snapshot(photos);
        Rename(paths, photos);

        var exitCode = Command(paths, new StringWriter(), new StringWriter(), "oui\n").Run(1, assumeYes: false);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
    }

    [Fact]
    public void Run_WithNothingToUndo_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = Command(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Aucun lot à annuler", error.ToString());
    }

    [Fact]
    public void Run_UnknownOrAlreadyUndoneLot_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        Command(paths, new StringWriter(), new StringWriter()).Run(1, assumeYes: true);
        var error = new StringWriter();

        Assert.Equal(ExitCodes.InvalidArguments, Command(paths, new StringWriter(), error).Run(1, assumeYes: true));
        Assert.Contains("déjà annulé", error.ToString());
        Assert.Equal(ExitCodes.InvalidArguments, Command(paths, new StringWriter(), error).Run(99, assumeYes: true));
        Assert.Contains("Lot 99 introuvable", error.ToString());
    }

    [Fact]
    public void Run_WithoutAnId_WhileABatchIsUnfinished_AsksForAnExplicitChoice()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Rename(paths, photos, cancellation.Token);   // lot 1 interrompu, rien de renommé
        var error = new StringWriter();

        var exitCode = Command(paths, new StringWriter(), error).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("photoapp annule 1", error.ToString());
        Assert.Contains("photoapp reprend 1", error.ToString());
    }

    [Fact]
    public void Run_AFileEditedSinceTheBatch_IsLeftInPlace_AndReturnsCode3()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        Rename(paths, photos);
        File.WriteAllText(Path.Combine(photos, "IMG-20230304-132015.jpg"), "retouchée");
        var output = new StringWriter();

        var exitCode = Command(paths, output, new StringWriter()).Run(null, assumeYes: true);

        Assert.Equal(ExitCodes.BatchIssues, exitCode);
        Assert.Equal("retouchée", File.ReadAllText(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-WA0005.png")));   // l'autre fichier est bien restauré
        Assert.Contains("Fichier modifié depuis le lot", output.ToString());
        Assert.Contains("partiellement annulé", output.ToString());
    }
}
```

`PhotoApp.Tests/ReprendCommandTests.cs` :

```csharp
using PhotoApp.Cli;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using static PhotoApp.Tests.BatchTestSupport;
using static PhotoApp.Tests.CliTestSupport;

namespace PhotoApp.Tests;

public sealed class ReprendCommandTests
{
    private static void InterruptedRename(AppDataPaths paths, string photos)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null), cancellation.Token);
    }

    [Fact]
    public void Run_FinishesAnInterruptedBatch()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        InterruptedRename(paths, photos);
        var output = new StringWriter();

        var exitCode = new ReprendCommand(paths, output, new StringWriter()).Run(1);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20230304-132015.jpg")));
        Assert.True(File.Exists(Path.Combine(photos, "IMG-20210915-000000-01.png")));
        Assert.Contains("Lot 1 : 2 renommé(s)", output.ToString());
        Assert.Equal(BatchState.Completed, OpenJournal(paths).GetBatch(1)!.State);
    }

    [Fact]
    public void Run_OnACompletedBatch_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var photos = Library(temp);
        var paths = PathsUnder(temp.Combine("data"));
        new RenommeCommand(paths, new StringWriter(), new StringWriter(), new StringReader(string.Empty))
            .Run(new RenommeRequest([photos], [], true, true, null));
        var error = new StringWriter();

        var exitCode = new ReprendCommand(paths, new StringWriter(), error).Run(1);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("déjà terminé", error.ToString());
    }

    [Fact]
    public void Run_UnknownLot_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = new ReprendCommand(PathsUnder(temp.Combine("data")), new StringWriter(), error).Run(7);

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Lot 7 introuvable", error.ToString());
    }
}
```

- [ ] **Step 2: Lancer les tests pour vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~JournalCommandTests|FullyQualifiedName~AnnuleCommandTests|FullyQualifiedName~ReprendCommandTests"`
Expected : erreur de compilation (`JournalCommand`, `AnnuleCommand`, `ReprendCommand` n'existent pas).

- [ ] **Step 3: Écrire `journal`**

`PhotoApp.Cli/JournalCommand.cs` :

```csharp
using System.Globalization;
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Batches;

namespace PhotoApp.Cli;

/// <summary>Commande « journal » : la liste des lots, ou le détail fichier par fichier d'un lot.</summary>
public sealed class JournalCommand(AppDataPaths paths, TextWriter output, TextWriter error)
{
    public int Run(long? batchId)
    {
        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
        return batchId is { } id ? PrintDetail(journal, id) : PrintList(journal);
    }

    private int PrintList(IBatchJournal journal)
    {
        var batches = journal.ListBatches();
        if (batches.Count == 0)
        {
            output.WriteLine("Aucun lot dans le journal.");
            return ExitCodes.Success;
        }

        foreach (var batch in batches)
        {
            var counts = batch.Counts;
            output.WriteLine(
                $"{batch.Id,4}  {Local(batch.CreatedUtc)}  {BatchSummaryPrinter.BatchStateLabel(batch.State),-10}  " +
                $"{counts.Done} fait(s), {counts.Failed} échec(s), {counts.Skipped} écarté(s)  {string.Join(" ; ", batch.Folders)}");
        }

        if (journal.FindUnfinished() is { } unfinished)
        {
            output.WriteLine($"Lot {unfinished.Id} interrompu : « photoapp reprend {unfinished.Id} » pour le terminer, ou « photoapp annule {unfinished.Id} » pour l'annuler.");
        }

        return ExitCodes.Success;
    }

    private int PrintDetail(IBatchJournal journal, long batchId)
    {
        var batch = journal.GetBatch(batchId);
        if (batch is null)
        {
            error.WriteLine($"Lot {batchId} introuvable.");
            return ExitCodes.InvalidArguments;
        }

        output.WriteLine($"Lot {batch.Id} — {BatchSummaryPrinter.BatchStateLabel(batch.State)} — {Local(batch.CreatedUtc)}");
        output.WriteLine($"Dossiers : {string.Join(" ; ", batch.Folders)}");
        foreach (var item in journal.GetItems(batchId))
        {
            output.WriteLine($"{item.Rank,5}  {BatchSummaryPrinter.StateLabel(item.State),-22}  {item.SourcePath} → {item.TargetPath}");
            if (!string.IsNullOrEmpty(item.Error))
            {
                output.WriteLine($"       {item.Error}");
            }
        }

        return ExitCodes.Success;
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Écrire `annule`**

`PhotoApp.Cli/AnnuleCommand.cs` :

```csharp
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Commande « annule » : restaure les noms et les dates d'un lot (le dernier lot terminé si aucun n'est indiqué).</summary>
public sealed class AnnuleCommand(AppDataPaths paths, TextWriter output, TextWriter error, TextReader input)
{
    public int Run(long? batchId, bool assumeYes, CancellationToken cancellation = default)
    {
        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));

        // Sans identifiant, ne pas annuler un lot ancien derrière un lot interrompu : le choix doit être explicite.
        if (batchId is null && journal.FindUnfinished() is { } unfinished)
        {
            error.WriteLine(
                $"Le lot {unfinished.Id} est interrompu : indiquez le lot à annuler (« photoapp annule {unfinished.Id} »), " +
                $"ou terminez-le (« photoapp reprend {unfinished.Id} »).");
            return ExitCodes.InvalidArguments;
        }

        var batch = batchId is { } id ? journal.GetBatch(id) : journal.FindLastUndoable();
        if (batch is null)
        {
            error.WriteLine(batchId is { } missing ? $"Lot {missing} introuvable." : "Aucun lot à annuler.");
            return ExitCodes.InvalidArguments;
        }

        if (batch.State == BatchState.Undone)
        {
            error.WriteLine($"Le lot {batch.Id} est déjà annulé.");
            return ExitCodes.InvalidArguments;
        }

        if (batch.State == BatchState.Planned)
        {
            error.WriteLine($"Le lot {batch.Id} n'a pas été exécuté.");
            return ExitCodes.InvalidArguments;
        }

        var toRestore = batch.Counts.Done + batch.Counts.UndoFailed + batch.Counts.Running;
        if (!assumeYes && !Confirmation.Ask($"Annuler le lot {batch.Id} ({toRestore} fichier(s) à restaurer) ?", input, output))
        {
            output.WriteLine("Abandon, aucun fichier modifié.");
            return ExitCodes.Success;
        }

        UndoSummary summary;
        try
        {
            summary = new BatchUndo(journal).Undo(batch.Id, cancellation, new BatchConsoleProgress(error, "Annulation"));
        }
        catch (BatchLockedException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Fatal;
        }

        error.WriteLine();
        output.WriteLine($"Lot {batch.Id} : {summary.Restored} restauré(s), {summary.Failed} non restauré(s).");
        BatchSummaryPrinter.PrintProblems(output, journal, batch.Id, ItemState.UndoFailed);
        output.WriteLine(summary.Completed
            ? "Lot annulé."
            : $"Lot partiellement annulé : relancez « photoapp annule {batch.Id} » une fois les cas ci-dessus réglés.");
        return summary.Completed ? ExitCodes.Success : ExitCodes.BatchIssues;
    }
}
```

- [ ] **Step 5: Écrire `reprend`**

`PhotoApp.Cli/ReprendCommand.cs` :

```csharp
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Batches;
using PhotoApp.Operations;

namespace PhotoApp.Cli;

/// <summary>Commande « reprend » : termine un lot interrompu (résout d'abord les fichiers laissés « en cours » d'après le disque).</summary>
public sealed class ReprendCommand(AppDataPaths paths, TextWriter output, TextWriter error)
{
    public int Run(long batchId, CancellationToken cancellation = default)
    {
        paths.EnsureCreated();
        var journal = new SqliteBatchJournal(new CatalogDatabase(paths.DatabasePath));
        var batch = journal.GetBatch(batchId);
        if (batch is null)
        {
            error.WriteLine($"Lot {batchId} introuvable.");
            return ExitCodes.InvalidArguments;
        }

        if (batch.State is BatchState.Completed or BatchState.Undone)
        {
            error.WriteLine($"Le lot {batchId} est déjà terminé ({BatchSummaryPrinter.BatchStateLabel(batch.State)}).");
            return ExitCodes.InvalidArguments;
        }

        BatchRunSummary summary;
        try
        {
            summary = new InPlaceRenamer(journal).Run(batchId, cancellation, new BatchConsoleProgress(error, "Renommage"));
        }
        catch (BatchLockedException ex)
        {
            error.WriteLine(ex.Message);
            return ExitCodes.Fatal;
        }

        error.WriteLine();
        BatchSummaryPrinter.PrintRun(output, journal, summary);
        return summary.Cancelled || summary.Counts.Failed > 0 ? ExitCodes.BatchIssues : ExitCodes.Success;
    }
}
```

- [ ] **Step 6: Brancher les commandes dans `Program.cs`**

Remplacer **tout** le contenu de `PhotoApp.Cli/Program.cs` par :

```csharp
using System.CommandLine;
using System.Text;
using PhotoApp.Cli;
using PhotoApp.Core;

Console.OutputEncoding = Encoding.UTF8;

// Ctrl+C : le fichier en cours se termine, puis le lot s'arrête proprement (état « interrompu »).
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

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
            cancellation.Token);
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
    TryParseBatchId(parse.GetValue(annuleLot), out var id)
        ? new AnnuleCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error, Console.In).Run(id, parse.GetValue(annuleYes), cancellation.Token)
        : ExitCodes.InvalidArguments));

// reprend
var reprendLot = new Argument<string>("lot")
{
    Description = "Identifiant du lot interrompu à terminer."
};
var reprend = new Command("reprend", "Termine un lot interrompu.") { reprendLot };
reprend.SetAction(parse => Guard(() =>
    TryParseBatchId(parse.GetValue(reprendLot), out var id) && id is { } batchId
        ? new ReprendCommand(AppDataPaths.ForCurrentUser(), Console.Out, Console.Error).Run(batchId, cancellation.Token)
        : ExitCodes.InvalidArguments));

var root = new RootCommand("PhotoManager en ligne de commande.") { analyse, renomme, journal, annule, reprend };
return root.Parse(args).Invoke();
```

- [ ] **Step 7: Lancer les tests, puis vérifier la ligne de commande réelle**

Run : `dotnet test PhotoVisualizer.slnx`
Expected : PASS.

Run : `dotnet run --project PhotoApp.Cli -c Release -- --help`
Expected : l'aide liste `analyse`, `renomme`, `journal`, `annule`, `reprend`.

Run : `dotnet run --project PhotoApp.Cli -c Release -- renomme --help`
Expected : les options `--exclude`, `--applique`, `--oui`, `--tranche` apparaissent avec leur description. **Si System.CommandLine 2.0.13 refuse un type ou un nom** (message de configuration au démarrage), corriger la déclaration de l'option concernée plutôt que de contourner : les types utilisés ici (`Option<bool>`, `Option<string?>`, `Option<string[]>`, `Argument<string?>` avec `ArgumentArity.ZeroOrOne`) sont les mêmes que ceux de la commande `analyse` de la phase 1, à l'exception de `Option<bool>` et de l'argument facultatif.

Run : `dotnet run --project PhotoApp.Cli -c Release -- annule abc`
Expected : « Identifiant de lot invalide : abc » sur la sortie d'erreur, code de sortie 1 (`echo $LASTEXITCODE` sous PowerShell).

- [ ] **Step 8: Commit**

```bash
git add PhotoApp.Cli PhotoApp.Tests
git commit -m "feat: commandes journal, annule et reprend, et branchement des quatre commandes dans la CLI" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 14: Interface : « Renommer sur place » et « Annuler le dernier lot »

Deux boutons qui appellent la même façade que la CLI, avec une confirmation en deux temps (bouton, résumé, puis « Confirmer »). Le flux existant (dossier de sortie, conversion JPEG) reste intact. Le projet de tests ne référence pas le projet Avalonia : cette tâche se vérifie par la compilation et à la main (tâche 15).

**Files:**
- Modify: `PhotoApp/ViewModels/MainWindowViewModel.cs` (remplacé en entier)
- Modify: `PhotoApp/Views/MainWindow.axaml`

**Interfaces:**
- Consumes : `RenameWorkflow`, `PreparedRename`, `BatchProgress`, `BatchUndo`, `SqliteBatchJournal`, `CatalogDatabase`, `MediaResult`.
- Produces : commandes `PrepareRenameCommand`, `PrepareUndoCommand`, `ConfirmCommand`, `AbortCommand` et propriétés `RenameStatus`, `IsRenaming`, `IsRenameConfirmationPending`.

- [ ] **Step 1: Remplacer le ViewModel**

`PhotoApp/ViewModels/MainWindowViewModel.cs` (remplacer tout le fichier ; les parties conversion et analyse sont reprises à l'identique) :

```csharp
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
            _renameCancellation?.Cancel();
            RenameStatus = "Arrêt demandé : le fichier en cours se termine.";
            return;
        }

        _preparedRename = null;
        _preparedUndoBatchId = null;
        IsRenameConfirmationPending = false;
        RenameStatus = "Abandonné, aucun fichier modifié.";
    }
}
```

- [ ] **Step 2: Ajouter les boutons et l'état à la fenêtre**

`PhotoApp/Views/MainWindow.axaml` : dans le premier `StackPanel` (ligne `<Button Content="Convertir" … />`), ajouter après le bouton « Convertir » :

```xml
            <Button Content="Renommer sur place" Command="{Binding PrepareRenameCommand}" />
            <Button Content="Annuler le dernier lot" Command="{Binding PrepareUndoCommand}" />
```

Dans le `StackPanel Grid.Row="2"`, après le `TextBlock` de `ConversionStatus`, ajouter :

```xml
            <TextBlock Text="{Binding RenameStatus}" Foreground="DarkBlue" FontWeight="SemiBold" TextWrapping="Wrap"
                       IsVisible="{Binding RenameStatus, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
            <StackPanel Orientation="Horizontal" Spacing="8" IsVisible="{Binding IsRenameConfirmationPending}">
                <Button Content="Confirmer" Command="{Binding ConfirmCommand}" />
                <Button Content="Abandonner" Command="{Binding AbortCommand}" />
            </StackPanel>
            <Button Content="Arrêter le lot en cours" Command="{Binding AbortCommand}" IsVisible="{Binding IsRenaming}" />
```

Toujours dans le premier `StackPanel`, remplacer le `IsEnabled` du bouton « Choisir un dossier » : `IsEnabled="{Binding !IsConverting}"` devient `IsEnabled="{Binding CanChooseFolder}"` (pas de changement de dossier pendant un renommage ou une confirmation en attente).

- [ ] **Step 3: Compiler**

Run : `dotnet build PhotoVisualizer.slnx`
Expected : succès, sans nouvel avertissement dans les fichiers modifiés. Si `PrepareRenameCommand` ou les autres commandes générées sont introuvables, vérifier que la classe reste `partial` et que les méthodes sont bien `private` avec l'attribut `[RelayCommand]` (le générateur du toolkit retire le suffixe `Async`).

Run : `dotnet test PhotoVisualizer.slnx`
Expected : PASS (aucun test ne dépend de l'interface).

- [ ] **Step 4: Commit**

```bash
git add PhotoApp
git commit -m "feat: interface, boutons « Renommer sur place » et « Annuler le dernier lot » avec confirmation en deux temps" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

---

### Task 15: Vérification sur la bibliothèque réelle, documentation, clôture de la phase

Cette tâche fait la preuve que les trois correctifs du moteur ne déplacent que les fichiers attendus, vérifie le cycle complet sur une **copie**, puis met la documentation à jour. Les étapes de la partie B se font **à la main avec l'utilisateur** pour l'interface.

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-10-19-phase2-execution-sure-design.md` (statut)

#### A. Ré-analyse de la bibliothèque de référence (lecture seule)

- [ ] **Step 1: Relancer l'analyse sur les mêmes dossiers que le rapport de référence**

Le rapport de référence de la phase 1 est `%LOCALAPPDATA%\PhotoManager\reports\analyse-20261019-phase1-finale.csv` (9 453 médias). Dans PowerShell, depuis la racine du dépôt :

```powershell
$ref = Join-Path $env:LOCALAPPDATA 'PhotoManager\reports\analyse-20261019-phase1-finale.csv'
if (-not (Test-Path $ref)) { throw "Rapport de référence introuvable : $ref" }
$pictures = [Environment]::GetFolderPath('MyPictures')
$videos = [Environment]::GetFolderPath('MyVideos')
foreach ($folder in $pictures, $videos) { if (-not (Test-Path $folder)) { throw "Dossier introuvable : $folder" } }
$new = Join-Path $env:LOCALAPPDATA 'PhotoManager\reports\analyse-phase2.csv'
dotnet run --project .\PhotoApp.Cli -c Release -- analyse $pictures $videos --out $new
```

Expected : le résumé s'affiche, `Rapport : …\analyse-phase2.csv`, code de sortie 0. Le nombre de médias doit être proche de 9 453 (il a pu varier si des fichiers ont été ajoutés ou retirés depuis ; si l'écart est grand, relever les dossiers racines du rapport de référence avec `Import-Csv $ref -Delimiter ';' | ForEach-Object { Split-Path $_.chemin } | Sort-Object -Unique | Select-Object -First 20` et relancer sur ceux-là).

- [ ] **Step 2: Comparer au rapport de référence**

```powershell
$old = Import-Csv -Delimiter ';' -Encoding UTF8 $ref
$cur = Import-Csv -Delimiter ';' -Encoding UTF8 $new
$byPath = @{}
foreach ($row in $old) { $byPath[$row.chemin] = $row }
$changes = foreach ($row in $cur) {
    $before = $byPath[$row.chemin]
    if ($null -eq $before) { [pscustomobject]@{ chemin = $row.chemin; avant = '(absent)'; apres = "$($row.confiance) / $($row.raison) / $($row.nom_propose)" } }
    elseif ($before.confiance -ne $row.confiance -or $before.raison -ne $row.raison -or $before.nom_propose -ne $row.nom_propose) {
        [pscustomobject]@{ chemin = $row.chemin; avant = "$($before.confiance) / $($before.raison) / $($before.nom_propose)"; apres = "$($row.confiance) / $($row.raison) / $($row.nom_propose)" }
    }
}
"{0} ligne(s) changée(s)" -f @($changes).Count
$changes | Format-Table -AutoSize -Wrap
"live-photo-conflit : {0}" -f @($cur | Where-Object { $_.drapeaux -match 'live-photo-conflit' }).Count
"source-ignoree : {0}" -f @($cur | Where-Object { $_.drapeaux -match 'source-ignoree' }).Count
```

Expected : seules ces catégories changent, et chacune se justifie par un des trois correctifs :
1. `Screenshot 2025-05-19 224500.png` passe de `Incertaine / desaccord` à `Sûre / exif-avant-nom`, nom proposé `IMG-20250519-224446.png` ;
2. des vidéos Live Photo dont la date diverge de la photo : drapeau `live-photo-conflit`, nom `VID-…` au lieu du nom de la photo ;
3. des fichiers à GPS mal formé, auparavant en `lecture-impossible`, désormais datés par leurs autres sources avec le drapeau `source-ignoree` ;
4. éventuellement des fichiers ajoutés ou retirés de la bibliothèque depuis la phase 1 (`(absent)`).

**Toute autre différence est un défaut à comprendre, pas à ignorer** : relever le fichier, la règle concernée, et s'arrêter pour en discuter avec l'utilisateur avant de continuer. Si le nombre de `live-photo-conflit` est élevé (plus de quelques dizaines sur ~266 fichiers liés), s'arrêter aussi : la tolérance de concordance entre photo et vidéo est peut-être trop stricte pour certains appareils, et cela se règle dans `MediaNamer.DatesDisagree`, avec l'utilisateur, pas ici.

#### B. Cycle complet sur une copie (jamais sur la bibliothèque)

- [ ] **Step 3: Préparer une copie d'essai**

```powershell
$source = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'Captures'
if (-not (Test-Path $source)) { throw "Dossier introuvable : $source" }
$essai = Join-Path $env:TEMP 'pm-essai'
if (Test-Path $essai) { throw "$essai existe déjà : le supprimer ou choisir un autre nom" }
New-Item -ItemType Directory -Path $essai | Out-Null
Get-ChildItem $source -File | Select-Object -First 40 | Copy-Item -Destination $essai
function Get-State($folder) {
    Get-ChildItem $folder -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Nom = $_.Name; Taille = $_.Length; Hash = (Get-FileHash $_.FullName).Hash; Creation = $_.CreationTimeUtc.Ticks; Modification = $_.LastWriteTimeUtc.Ticks }
    }
}
$avant = Get-State $essai
"{0} fichier(s) copié(s) dans {1}" -f @($avant).Count, $essai
```

Expected : 40 fichiers (ou moins si le dossier en contient moins), copiés dans `%TEMP%\pm-essai`.

- [ ] **Step 4: Aperçu, application, journal, annulation**

```powershell
# 1. Aperçu : aucun fichier ne doit changer.
dotnet run --project .\PhotoApp.Cli -c Release -- renomme $essai
Compare-Object $avant (Get-State $essai) -Property Nom, Taille, Hash, Creation, Modification | Measure-Object | Select-Object -ExpandProperty Count
# Expected : 0

# 2. Application (répondre o à la question).
dotnet run --project .\PhotoApp.Cli -c Release -- renomme $essai --applique
# Expected : « Lot 1 : N renommé(s), 0 en échec, … » ; code de sortie 0 ($LASTEXITCODE).

# 3. Journal.
dotnet run --project .\PhotoApp.Cli -c Release -- journal
dotnet run --project .\PhotoApp.Cli -c Release -- journal --lot 1
# Expected : le lot apparaît « Terminé » ; le détail liste chaque fichier avec son ancien et son nouveau nom.

# 4. Second passage : rien à renommer.
dotnet run --project .\PhotoApp.Cli -c Release -- renomme $essai
# Expected : « Rien à renommer. »

# 5. Annulation, puis comparaison avec l'état initial.
dotnet run --project .\PhotoApp.Cli -c Release -- annule --oui
Compare-Object $avant (Get-State $essai) -Property Nom, Taille, Hash, Creation, Modification | Measure-Object | Select-Object -ExpandProperty Count
# Expected : « Lot annulé. » puis 0 différence : c'est le critère de fin de la phase.
```

Le numéro de lot est `1` si le journal est neuf ; sinon utiliser celui que `journal` affiche.

- [ ] **Step 5 (avec l'utilisateur) : interrompre un lot avec Ctrl+C**

Sur la copie d'essai, avec davantage de fichiers pour avoir le temps d'interrompre (la copie est de nouveau dans son état d'origine après l'étape 4) :

```powershell
Get-ChildItem $source -File | Select-Object -Skip 40 -First 2000 | Copy-Item -Destination $essai
$avant = Get-State $essai
dotnet run --project .\PhotoApp.Cli -c Release -- renomme $essai --applique --oui
```

Appuyer sur Ctrl+C dès que la ligne `Renommage : …` s'affiche. Expected : « Lot N interrompu : « photoapp reprend N » … » et code de sortie 3. Puis, en remplaçant `N` par l'identifiant affiché :

```powershell
dotnet run --project .\PhotoApp.Cli -c Release -- journal            # le lot N est « Interrompu »
dotnet run --project .\PhotoApp.Cli -c Release -- reprend N          # « Lot N : … renommé(s) », code 0
dotnet run --project .\PhotoApp.Cli -c Release -- annule N --oui     # « Lot annulé. »
Compare-Object $avant (Get-State $essai) -Property Nom, Taille, Hash, Creation, Modification | Measure-Object | Select-Object -ExpandProperty Count
```

Expected : `0` à la fin. Si Ctrl+C tue le processus au lieu d'interrompre proprement (pas de message « Lot interrompu »), `Console.CancelKeyPress` est court-circuité par System.CommandLine : chercher dans l'API de `System.CommandLine` 2.0.13 le moyen de désactiver sa propre gestion (`InvocationConfiguration.ProcessTerminationTimeout`) et passer cette configuration à `Invoke`. Le lot tué brutalement reste de toute façon récupérable (`reprend`, `annule`), ce que les tests d'intégration couvrent.

- [ ] **Step 6 (avec l'utilisateur) : l'interface**

Lancer `dotnet run --project .\PhotoApp\PhotoApp.csproj`, puis :

1. Cliquer **Choisir un dossier** et sélectionner `%TEMP%\pm-essai` (le menu du sélecteur de dossier n'a pas de raccourci pour `%TEMP%` : coller le chemin affiché par `$essai` dans la barre d'adresse). Attendre la fin de l'analyse : la liste affiche les photos.
2. Cliquer **Renommer sur place**. Attendu : un texte bleu « N fichier(s) à renommer sur place (… ) Le lot est annulable. Confirmer ? » et deux boutons **Confirmer** et **Abandonner** ; aucun fichier n'a changé (vérifier dans l'Explorateur).
3. Cliquer **Abandonner**. Attendu : « Abandonné, aucun fichier modifié. »
4. Recliquer **Renommer sur place**, puis **Confirmer**. Attendu : le texte passe à « Renommage : n / N », puis « Lot N terminé : … renommé(s) » ; la liste se rafraîchit avec les nouveaux noms `IMG-…`.
5. Cliquer **Annuler le dernier lot**. Attendu : « Annuler le lot N (… fichier(s) à restaurer) ? Confirmer ? » ; cliquer **Confirmer**. Attendu : « Lot N annulé : … fichier(s) restauré(s). » et la liste retrouve les noms d'origine.
6. Vérifier avec PowerShell : `Compare-Object $avant (Get-State $essai) -Property Nom, Taille, Hash, Creation, Modification | Measure-Object | Select-Object -ExpandProperty Count` → `0`.
7. Nettoyer : `Remove-Item $essai -Recurse -Force`.

Les boutons **Convertir** et **Effacer l'original après succès** n'ont pas changé : ne pas les utiliser pour cet essai.

#### C. Documentation

- [ ] **Step 7: Mettre à jour le README**

Dans `README.md` :

1. Remplacer la ligne `- **Phase en cours :** …` par :

```markdown
- **Phase en cours :** phase 2 (exécution sûre : renommage sur place, journal, reprise, annulation) implémentée sur la branche `feat/phase2-execution-sure`, en attente de validation — spec : [`docs/superpowers/specs/2026-10-19-phase2-execution-sure-design.md`](docs/superpowers/specs/2026-10-19-phase2-execution-sure-design.md), plan : [`docs/superpowers/plans/2026-10-19-phase2-execution-sure.md`](docs/superpowers/plans/2026-10-19-phase2-execution-sure.md). Phase 1 (moteur de normalisation) terminée. Prochaine étape : phase 3, écriture des métadonnées internes et conversion sur place.
```

2. Dans « Ce qui marche aujourd'hui », ajouter après la puce « **Commande `photoapp analyse`** … » :

```markdown
- **Renommage sur place** (`photoapp renomme`) : applique les noms proposés (Sûre et Probable) aux photos **et aux vidéos**, avec leurs Live Photo et sidecars, et fixe les dates du fichier (création et modification) sur la date décidée. Aperçu par défaut ; `--applique` exécute. Le lot est figé puis journalisé (SQLite, `catalog.db`) avant chaque action ; un fichier modifié depuis le plan, un nom cible déjà pris ou un fichier verrouillé est écarté avec sa raison, jamais écrasé.
- **Journal, reprise, annulation** : `photoapp journal` liste les lots, `photoapp annule` restaure noms, contenus et dates d'origine (un fichier retouché depuis le lot est laissé en place et signalé), `photoapp reprend` termine un lot interrompu (Ctrl+C ou arrêt brutal).
- **Interface** : boutons « Renommer sur place » et « Annuler le dernier lot », avec confirmation.
```

3. Dans « Limites connues », remplacer la puce « Pas encore de galerie, de journal, d'annulation ni de quarantaine (phases 2 et 4). … » par :

```markdown
- Pas encore de galerie ni d'écrans Aperçu et Journal dans l'interface (phase 4) ; la quarantaine existe comme brique testée mais n'est pas encore utilisée (conversion sur place, phase 3). L'option « Effacer l'original après succès » du flux de conversion supprime réellement l'original : la laisser décochée pour tout essai.
- Les fichiers HEIC, HEIF et AVIF sont renommés tels quels (conversion sur place en phase 3).
```

et supprimer la puce « L'interface ignore encore les vidéos (renommage sur place et vidéos : phase 2). La CLI les analyse. » en la remplaçant par :

```markdown
- La liste de l'interface et la conversion ne montrent que les photos ; le renommage sur place traite aussi les vidéos.
```

4. Dans le bloc « Architecture », remplacer les trois lignes

```text
PhotoApp.Catalog/      scan des dossiers (photos, vidéos, sidecars) et analyse complète (MediaAnalyzer)
PhotoApp.Cli/          commande « analyse » en lecture seule (ne référence jamais Operations)
PhotoApp.Operations/   conversion (Magick.NET), exécution du lot, manifeste, ConversionService
```

par :

```text
PhotoApp.Catalog/      scan des dossiers (photos, vidéos, sidecars), analyse complète (MediaAnalyzer), base SQLite catalog.db (journal des lots, quarantaine)
PhotoApp.Cli/          commandes analyse (lecture seule), renomme, journal, annule, reprend
PhotoApp.Operations/   renommage sur place (planificateur, exécuteur, reprise, annulation, quarantaine), conversion (Magick.NET), manifeste
```

5. Dans « Données de l'application », remplacer « (manifestes, rapports d'analyse, futur catalogue) » par « (manifestes, rapports, base `catalog.db`) ».

6. Après la section « Analyser une bibliothèque (lecture seule) », ajouter :

````markdown
## Renommer sur place

```powershell
# Aperçu : affiche le plan et écrit un CSV dans reports\, ne modifie aucun fichier.
dotnet run --project PhotoApp.Cli -c Release -- renomme "D:\Photos"

# Exécution (demande confirmation ; --oui pour s'en passer).
dotnet run --project PhotoApp.Cli -c Release -- renomme "D:\Photos" --applique

# Traiter par tranches de 500 groupes (dossier synchronisé Nextcloud).
dotnet run --project PhotoApp.Cli -c Release -- renomme "D:\Photos" --applique --tranche 500

dotnet run --project PhotoApp.Cli -c Release -- journal            # liste des lots
dotnet run --project PhotoApp.Cli -c Release -- journal --lot 1    # détail d'un lot
dotnet run --project PhotoApp.Cli -c Release -- annule             # annule le dernier lot terminé
dotnet run --project PhotoApp.Cli -c Release -- reprend 1          # termine un lot interrompu
```

- Seules les confiances **Sûre** et **Probable** sont renommées. Rien n'est jamais écrasé ni supprimé : seuls le nom et les dates du fichier changent.
- Codes de sortie : 0 succès, 1 argument invalide, 2 erreur fatale, 3 lot terminé avec des échecs ou interrompu.
- Toujours essayer sur une **copie** d'un petit dossier avant la bibliothèque réelle.
````

7. Dans « Test manuel recommandé », ajouter l'étape :

```markdown
5. Renommer sur place : `renomme` (aperçu), `renomme --applique`, `journal`, puis `annule` : la comparaison des noms, contenus et dates avec l'état initial doit être identique.
```

- [ ] **Step 8: Mettre à jour le statut de la spec**

Dans `docs/superpowers/specs/2026-10-19-phase2-execution-sure-design.md`, remplacer `- **Statut :** validé en brainstorming, en attente de relecture` par `- **Statut :** implémentée sur la branche feat/phase2-execution-sure, en attente de validation`. Ajouter à la fin de la section 12 (« Décisions de la session ») la ligne de tableau :

```markdown
| Écarts d'implémentation | Verrou d'exécution par fichier `catalog.db.lock` ; colonne « options » du lot supprimée ; code de sortie 3 aussi pour un lot interrompu ; changement de casse seul en deux temps (voir le plan, « Écarts assumés ») |
```

- [ ] **Step 9: Vérification finale**

Run : `dotnet build PhotoVisualizer.slnx -c Release`
Expected : succès.

Run : `dotnet test PhotoVisualizer.slnx`
Expected : PASS, avec nettement plus de tests qu'à la tâche 0 (aucun retiré).

Run : `git status --short`
Expected : seuls `README.md` et la spec sont modifiés avant le commit ci-dessous.

- [ ] **Step 10: Commit**

```bash
git add README.md docs
git commit -m "docs: README et statut de la spec pour la phase 2" -m "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 11: Clôture de la phase (avec l'utilisateur)**

Ne pas fusionner de soi-même. Présenter à l'utilisateur : le nombre de tests, le résultat de la comparaison de la partie A, le résultat de la partie B (0 différence après annulation), les écarts assumés. Sur sa validation, **proposer** la fusion locale dans `main` (`git switch main` puis `git merge --no-ff feat/phase2-execution-sure`) ; le `git push` reste à l'utilisateur (le shell de Claude n'a pas de clé SSH). Puis mettre à jour la mémoire du projet (phase 2 terminée, prochaine phase 3).
