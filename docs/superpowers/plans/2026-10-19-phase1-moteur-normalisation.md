# Phase 1 — Moteur de normalisation (lecture seule) : plan d'implémentation

> **Pour les agents d'exécution :** SOUS-COMPÉTENCE REQUISE : utiliser superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans pour exécuter ce plan tâche par tâche. Les étapes utilisent des cases à cocher (`- [ ]`).

**Objectif :** remplacer la sélection de date actuelle (premier candidat trouvé) par un moteur unique qui collecte tous les candidats, reconnaît la source par un profil, confronte les candidats, attribue une confiance expliquée par une raison et propose un nom ; l'exposer dans une CLI `photoapp analyse` en lecture seule et dans l'interface existante.

**Architecture :** `PhotoApp.Core` gagne trois espaces de noms purs : `Profiles` (table des profils de sources, lecture des noms), `Normalization` (comparaison des dates, arbitre à règles nommées, ambiguïtés, nommage, moteur). `PhotoApp.Metadata` produit un `RawMetadata` typé par fichier (EXIF, GPS, XMP, sidecar, QuickTime, vrai format) sans rien décider. `PhotoApp.Catalog` scanne photos, vidéos et sidecars, et orchestre l'analyse (`MediaAnalyzer`). Le nouveau projet `PhotoApp.Cli` écrit un rapport CSV et un résumé ; il ne référence jamais `PhotoApp.Operations`.

**Tech Stack :** .NET 10, Avalonia 12.0.2, MetadataExtractor 2.9.3, Magick.NET-Q8-AnyCPU 14.17.2 (Operations et tests), System.CommandLine 2.0.13 (Cli), xUnit + Moq.

**Spec :** `docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md` (cadre : `docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md`, §5 et §10).

## Contraintes globales

- `PhotoApp.Core` est **pur** : aucune E/S fichier, aucun paquet NuGet. Les fonctions de chaînes de `Path` (`GetFileName`, `GetExtension`…) sont permises.
- `PhotoApp.Cli` ne référence **jamais** `PhotoApp.Operations`. Rien n'est écrit à côté des photos ; le rapport par défaut va dans `<racine de données>/reports/`.
- Heure de référence = heure murale du lieu de prise de vue ; 24 h toujours.
- Nommage : `IMG-AAAAMMJJ-HHmmss.ext` (photos, captures), `VID-…` (vidéos) ; jour seul `IMG-AAAAMMJJ-000000-NN.ext` ; collision dans la même seconde `-01`, `-02`… ; extension en minuscules, `.jpeg` → `.jpg` ; un fichier déjà nommé n'est jamais renuméroté.
- Confiances : **Sûre** et **Probable** sont applicables ; **Incertaine** part dans « À vérifier » ; **Aucune** n'invente jamais de date. Seules Sûre et Probable sont appliquées par l'interface.
- Tolérances (spec §5.2) : concordance ±2 s en précision seconde, même minute, même jour ; écart de fuseau = multiple de 15 min à 2 s près, |écart| ≤ 14 h ; décalage déduit du GPS arrondi au quart d'heure, reste ≤ 2 min.
- Identifiants de code en anglais, commentaires et textes affichés en français (style du dépôt).
- Corpus de test **généré à l'exécution** (Magick.NET, conteneurs MOV/MP4 écrits à la main) : aucune donnée privée.
- Les commits se terminent par la ligne `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (second `-m` dans les commandes ci-dessous).
- Ne pas pousser la branche et ne pas fusionner dans `main` sans demande explicite de l'utilisateur.

## Écarts assumés par rapport à la spec

- **Clés Apple QuickTime** : MetadataExtractor convertit `com.apple.quicktime.creationdate` dans le fuseau du PC et perd le décalage d'origine (vérifié : `17:07:10+09:00` est rendu `10:07:10` « Local »). Un petit lecteur `moov/meta/keys` + `ilst` (`QuickTimeKeysReader`, tâche 10) lit la chaîne telle qu'écrite.
- **Suffixes de copie** (`-min`, `(1)`, ` - Copie`) : le nom est d'abord reconnu tel quel, et les suffixes ne sont retirés qu'en cas d'échec. Les retirer d'emblée casserait des motifs qui contiennent légitimement `(n)` (rafales Samsung `20260917_222944(1)`).
- **Pixel `PXL_`** : l'heure du nom est en UTC (comportement de Google Caméra) ; le profil la déclare UTC, comme les noms epoch.
- **Collisions** : l'unicité porte sur le nom de base, sans l'extension. Deux photos de la même seconde en `.jpg` et `.heic` reçoivent `-01` au lieu de partager un nom de base, ce qui réserve le partage du nom de base aux Live Photo.
- **`RawMetadata`** contient des candidats déjà typés (le parsage des formats de date est fait par `Metadata`) ; tout choix entre candidats reste dans `Core`.
- **Vrai format** : MetadataExtractor annonce une extension « commune » (`heic` pour `.heif`, `tiff` pour `.tif`, `mp4` pour `.m4v`). Ces équivalences ne lèvent pas le drapeau `format-reel`.
- **`MediaAnalyzer` dans `Catalog`** : l'orchestration scan → lecture → moteur, partagée par la CLI et l'interface, vit dans `Catalog`, qui référence désormais `Metadata` (le catalogue de la phase 4 en aura besoin de toute façon).
- **Test de l'interface** (spec §13) : le projet de tests ne référence pas le projet Avalonia. La règle « seules Sûre et Probable sont appliquées » vit dans `ConversionService` et `ImageFileInfo`, testés directement ; le ViewModel se contente de les appeler, et il est vérifié à la main (tâche 16).
- **Mesure de mi-phase** : les tâches 17 et 18 dépendent du rapport réel ; elles décrivent la méthode et le cycle TDD, pas le contenu des profils à ajouter.

---

## Structure des fichiers (cible de la phase 1)

```
PhotoApp.Core/                              (aucun paquet)
  AppDataPaths.cs                           + ReportsFolder
  Models/DateCandidate.cs                   remplace ImageDateCandidate : précision, origine, UTC, Localize
  Models/DatePrecision.cs, CandidateOrigin.cs
  Models/RawMetadata.cs                     métadonnées brutes typées (produites par Metadata)
  Models/ScannedMedia.cs, MediaKind.cs      fichier scanné (taille, date de modification, sidecars)
  Models/ImageFileInfo.cs                   porte une DateDecision (interface, conversion)
  Profiles/ProfileFamily.cs, NameTimeSemantics.cs, DayMonthOrder.cs
  Profiles/SourceProfile.cs                 enregistrement d'un profil
  Profiles/NameReading.cs                   lecture d'un nom (+ AmbiguousName, DayMonthEvidence)
  Profiles/NamePatternReader.cs             groupes nommés → DateCandidate
  Profiles/GenericNameParser.cs             filet pour les noms inconnus
  Profiles/SourceProfiles.cs                LA table des profils
  Profiles/ProfileMatcher.cs                reconnaissance : motif, marque, dossier ; suffixes de copie
  Normalization/DateComparison.cs           concordance, écart de fuseau, décalage GPS
  Normalization/Confidence.cs, DecisionFlags.cs, DateDecision.cs, Reasons.cs, DecisionTexts.cs
  Normalization/NormalizationOptions.cs
  Normalization/DateArbiter.cs              règles §5.4 (étape 1 + confrontation)
  Normalization/AmbiguityResolver.cs        §5.5 étapes 3 et 4
  Normalization/IContentHasher.cs, MediaNamer.cs
  Normalization/MediaFacts.cs, MediaResult.cs, NormalizationEngine.cs
  Services/SupportedMediaExtensions.cs      remplace SupportedImageExtensions (photos, vidéos, sidecars)
  Services/SubSeconds.cs                    « 123 » → 123 ms
  Services/NameAllocator.cs, NormalizedFileNameBuilder.cs   préfixe IMG/VID, unicité du nom de base
  (supprimés : DateSelector, FileNameDateParser, ImageDateSource, ImageDateMetadata)
PhotoApp.Metadata/                          MetadataExtractor
  MetadataReader.cs                         RawMetadata
  QuickTimeKeysReader.cs                    clés Apple telles qu'écrites
  Sha256ContentHasher.cs
  (supprimés : NativeImageMetadataReader, FileSystemDateReader, ImageDateResolver)
PhotoApp.Catalog/                           + référence Metadata
  MediaScanner.cs, ScanOptions.cs, ScanResult.cs   (remplace ImageScanner)
  MediaAnalyzer.cs, AnalysisResult.cs
PhotoApp.Cli/                               nouveau, System.CommandLine ; exécutable « photoapp »
  Program.cs, AnalyseCommand.cs, ConsoleProgress.cs
  Report/CsvReportWriter.cs, Report/AnalysisSummary.cs
PhotoApp/                                   ViewModel sur MediaAnalyzer ; affiche confiance et raison
PhotoApp.Tests/                             + TestVideos.cs, TestZones.cs ; + référence Cli
```

---

### Tâche 1 : `DateCandidate` remplace `ImageDateCandidate` (précision, origine, UTC)

**Fichiers :**
- Créer : `PhotoApp.Core/Models/DatePrecision.cs`, `PhotoApp.Core/Models/CandidateOrigin.cs`, `PhotoApp.Core/Services/SubSeconds.cs`, `PhotoApp.Tests/DateCandidateTests.cs`, `PhotoApp.Tests/TestZones.cs`
- Renommer : `PhotoApp.Core/Models/ImageDateCandidate.cs` → `PhotoApp.Core/Models/DateCandidate.cs`
- Modifier : `PhotoApp.Core/Services/XmpDateParser.cs`, `PhotoApp.Core/Services/FileNameDateParser.cs`, `PhotoApp.Metadata/NativeImageMetadataReader.cs`, `PhotoApp.Metadata/FileSystemDateReader.cs`, et tous les fichiers qui nomment `ImageDateCandidate` (liste à l'étape 5)
- Tests : `PhotoApp.Tests/XmpDateParserTests.cs` et les tests existants listés à l'étape 6

**Interfaces :**
- Produit : `DatePrecision { Day, Minute, Second, SubSecond }` ; `CandidateOrigin { Unknown, ExifOriginal, ExifDigitized, QuickTimeApple, QuickTimeMovieHeader, Xmp, XmpSidecar, Gps, FileName, FileModified }` ; `sealed record DateCandidate(DateTime wallClock, DatePrecision precision, CandidateOrigin origin = Unknown, TimeSpan? offset = null, bool isUtc = false)` avec `WallClock`, `Precision`, `Origin`, `Offset`, `IsUtc`, `HasTime`, `HasKnownInstant`, `ToInstant(TimeZoneInfo)`, `Localize(TimeZoneInfo)` ; `XmpDateParser.Parse(string? text, CandidateOrigin origin = CandidateOrigin.Xmp)` ; `SubSeconds.ToMilliseconds(string?) → int?` ; `TestZones.PlusOne` (fuseau fixe UTC+1, sans heure d'été).

- [ ] **Étape 1 : Commiter le plan, vérifier la branche**

```bash
cd /c/Users/ismail/Nextcloud/dev/photomanager
git branch --show-current     # attendu : feat/phase1-normalisation
git add docs/superpowers/plans/2026-10-19-phase1-moteur-normalisation.md
git commit -m "docs: plan d'implémentation de la phase 1" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Étape 2 : Base de référence**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : `Réussi!  - Échec : 0, Réussite : 133` (ou `Passed! - Failed: 0, Passed: 133`).

- [ ] **Étape 3 : Écrire les tests qui échouent**

`PhotoApp.Tests/TestZones.cs` :

```csharp
namespace PhotoApp.Tests;

/// <summary>Fuseaux fixes, sans heure d'été : les tests ne dépendent ni du PC ni de la date.</summary>
public static class TestZones
{
    public static readonly TimeZoneInfo PlusOne =
        TimeZoneInfo.CreateCustomTimeZone("UTC+01", TimeSpan.FromHours(1), "UTC+01", "UTC+01");
}
```

`PhotoApp.Tests/DateCandidateTests.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class DateCandidateTests
{
    [Fact]
    public void WallClock_IsAlwaysUnspecified()
    {
        var candidate = new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12, DateTimeKind.Utc), DatePrecision.Second);

        Assert.Equal(DateTimeKind.Unspecified, candidate.WallClock.Kind);
    }

    [Fact]
    public void Utc_IgnoresAnyOffset_AndHasAKnownInstant()
    {
        var candidate = new DateCandidate(
            new DateTime(2024, 1, 5, 14, 0, 0), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, TimeSpan.FromHours(3), isUtc: true);

        Assert.Null(candidate.Offset);
        Assert.True(candidate.HasKnownInstant);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 14, 0, 0, TimeSpan.Zero), candidate.ToInstant(TestZones.PlusOne));
    }

    [Fact]
    public void FloatingLocal_HasNoKnownInstant_AndUsesTheFallbackZone()
    {
        var candidate = new DateCandidate(new DateTime(2024, 1, 5, 15, 0, 0), DatePrecision.Second);

        Assert.False(candidate.HasKnownInstant);
        Assert.Equal(new DateTimeOffset(2024, 1, 5, 15, 0, 0, TimeSpan.FromHours(1)), candidate.ToInstant(TestZones.PlusOne));
    }

    [Fact]
    public void Localize_ConvertsUtcToTheZoneWallClock_AndKeepsTheOrigin()
    {
        var utc = new DateCandidate(new DateTime(2024, 1, 5, 14, 0, 0), DatePrecision.Second, CandidateOrigin.Gps, isUtc: true);

        var local = utc.Localize(TestZones.PlusOne);

        Assert.False(local.IsUtc);
        Assert.Equal(new DateTime(2024, 1, 5, 15, 0, 0), local.WallClock);
        Assert.Equal(TimeSpan.FromHours(1), local.Offset);
        Assert.Equal(CandidateOrigin.Gps, local.Origin);
    }

    [Fact]
    public void Localize_LeavesALocalDateUnchanged()
    {
        var local = new DateCandidate(new DateTime(2024, 1, 5, 15, 0, 0), DatePrecision.Second);

        Assert.Same(local, local.Localize(TestZones.PlusOne));
    }

    [Theory]
    [InlineData(DatePrecision.Day, false)]
    [InlineData(DatePrecision.Minute, true)]
    [InlineData(DatePrecision.SubSecond, true)]
    public void HasTime_DependsOnThePrecision(DatePrecision precision, bool expected)
    {
        Assert.Equal(expected, new DateCandidate(new DateTime(2024, 1, 5), precision).HasTime);
    }

    [Theory]
    [InlineData("5", 500)]
    [InlineData("123", 123)]
    [InlineData("123456", 123)]
    [InlineData("07", 70)]
    public void SubSeconds_AreReadAsMilliseconds(string digits, int expected)
    {
        Assert.Equal(expected, PhotoApp.Core.Services.SubSeconds.ToMilliseconds(digits));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12a")]
    public void SubSeconds_RejectNonDigits(string? digits)
    {
        Assert.Null(PhotoApp.Core.Services.SubSeconds.ToMilliseconds(digits));
    }
}
```

Dans `PhotoApp.Tests/XmpDateParserTests.cs`, ajouter `using PhotoApp.Core.Models;` en tête et remplacer le test `Parse_Z_MeansZeroOffset_WithoutConvertingTheClock` par :

```csharp
    [Fact]
    public void Parse_Z_IsUtc_WithoutConvertingTheClock()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15Z");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.True(result.IsUtc);
        Assert.Null(result.Offset);
    }

    [Fact]
    public void Parse_ReportsPrecisionAndOrigin()
    {
        Assert.Equal(DatePrecision.Second, XmpDateParser.Parse("2023-03-04T13:20:15")!.Precision);
        Assert.Equal(DatePrecision.Minute, XmpDateParser.Parse("2023-03-04T13:20")!.Precision);
        Assert.Equal(DatePrecision.Day, XmpDateParser.Parse("2023-03-04")!.Precision);
        Assert.Equal(CandidateOrigin.Xmp, XmpDateParser.Parse("2023-03-04")!.Origin);
        Assert.Equal(CandidateOrigin.QuickTimeApple, XmpDateParser.Parse("2024-07-11T17:07:10+0900", CandidateOrigin.QuickTimeApple)!.Origin);
    }

    [Fact]
    public void Parse_Fraction_IsSubSecond()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15.25+01:00");

        Assert.NotNull(result);
        Assert.Equal(DatePrecision.SubSecond, result.Precision);
        Assert.Equal(250, result.WallClock.Millisecond);
        Assert.Equal(TimeSpan.FromHours(1), result.Offset);
    }

    [Fact]
    public void Parse_AppleOffsetWithoutColon_IsRead()
    {
        var result = XmpDateParser.Parse("2024-07-11T17:07:10+0900");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2024, 7, 11, 17, 7, 10), result.WallClock);
        Assert.Equal(TimeSpan.FromHours(9), result.Offset);
    }
```

- [ ] **Étape 4 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : erreurs de compilation `DateCandidate`, `DatePrecision`, `CandidateOrigin`, `SubSeconds` introuvables.

- [ ] **Étape 5 : Implémenter**

`PhotoApp.Core/Models/DatePrecision.cs` :

```csharp
namespace PhotoApp.Core.Models;

/// <summary>Finesse d'une date : un nom WhatsApp ne donne que le jour, un EXIF la seconde, parfois mieux.</summary>
public enum DatePrecision
{
    Day = 0,
    Minute = 1,
    Second = 2,
    SubSecond = 3
}
```

`PhotoApp.Core/Models/CandidateOrigin.cs` :

```csharp
namespace PhotoApp.Core.Models;

/// <summary>D'où vient une date candidate (spec de phase 1, §5.1).</summary>
public enum CandidateOrigin
{
    Unknown = 0,
    ExifOriginal,
    ExifDigitized,
    QuickTimeApple,
    QuickTimeMovieHeader,
    Xmp,
    XmpSidecar,
    Gps,
    FileName,
    FileModified
}
```

`PhotoApp.Core/Services/SubSeconds.cs` :

```csharp
using System.Globalization;

namespace PhotoApp.Core.Services;

public static class SubSeconds
{
    /// <summary>Fraction de seconde écrite en chiffres : « 5 » → 500 ms, « 123456 » → 123 ms. Null si ce n'est pas une suite de chiffres.</summary>
    public static int? ToMilliseconds(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        return int.Parse(digits.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
    }
}
```

Renommer puis réécrire le modèle :

```bash
git mv PhotoApp.Core/Models/ImageDateCandidate.cs PhotoApp.Core/Models/DateCandidate.cs
```

`PhotoApp.Core/Models/DateCandidate.cs` :

```csharp
namespace PhotoApp.Core.Models;

/// <summary>
/// Date lue dans une source. Hors UTC, elle garde l'heure murale telle qu'écrite (heure locale du lieu de prise de vue),
/// avec un décalage seulement quand la source le fournit. En UTC (vidéo, GPS, epoch), l'heure murale du lieu est inconnue.
/// </summary>
public sealed record DateCandidate
{
    public DateCandidate(
        DateTime wallClock,
        DatePrecision precision,
        CandidateOrigin origin = CandidateOrigin.Unknown,
        TimeSpan? offset = null,
        bool isUtc = false)
    {
        WallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        Precision = precision;
        Origin = origin;
        IsUtc = isUtc;
        Offset = isUtc ? null : offset;
    }

    public DateTime WallClock { get; init; }

    public DatePrecision Precision { get; init; }

    public CandidateOrigin Origin { get; init; }

    /// <summary>Décalage UTC de l'heure murale, quand la source le donne ou qu'il a été déduit. Toujours null en UTC.</summary>
    public TimeSpan? Offset { get; init; }

    public bool IsUtc { get; init; }

    public bool HasTime => Precision != DatePrecision.Day;

    /// <summary>Instant réel connu sans hypothèse de fuseau : UTC, ou heure locale avec décalage.</summary>
    public bool HasKnownInstant => IsUtc || Offset is not null;

    /// <summary>Instant réel : UTC tel quel, sinon le décalage connu, sinon celui du fuseau de repli à cette date.</summary>
    public DateTimeOffset ToInstant(TimeZoneInfo fallbackZone)
    {
        if (IsUtc)
        {
            return new DateTimeOffset(WallClock, TimeSpan.Zero);
        }

        var offset = Offset ?? fallbackZone.GetUtcOffset(WallClock);
        return new DateTimeOffset(WallClock, offset);
    }

    /// <summary>Heure murale dans un fuseau donné, pour une source qui ne connaît que l'UTC. Une heure locale est rendue telle quelle.</summary>
    public DateCandidate Localize(TimeZoneInfo zone)
    {
        if (!IsUtc)
        {
            return this;
        }

        var utc = DateTime.SpecifyKind(WallClock, DateTimeKind.Utc);
        var offset = zone.GetUtcOffset(utc);
        return this with
        {
            WallClock = DateTime.SpecifyKind(utc + offset, DateTimeKind.Unspecified),
            Offset = offset,
            IsUtc = false
        };
    }
}
```

Renommer le type dans tout le dépôt :

```bash
grep -rl ImageDateCandidate --include=*.cs . | grep -v '/bin/\|/obj/' | xargs sed -i 's/ImageDateCandidate/DateCandidate/g'
```

Corriger les constructeurs (l'ancien deuxième argument `bool hasTime` devient une précision ; ajouter `using PhotoApp.Core.Models;` là où il manque) :

| Fichier | Avant | Après |
|---|---|---|
| `PhotoApp.Metadata/FileSystemDateReader.cs` | `new DateCandidate(wallClock, true, TimeZoneInfo.Local.GetUtcOffset(wallClock))` | `new DateCandidate(wallClock, DatePrecision.Second, CandidateOrigin.FileModified, TimeZoneInfo.Local.GetUtcOffset(wallClock))` |
| `PhotoApp.Metadata/NativeImageMetadataReader.cs` (original) | `new DateCandidate(original, true, ReadOffset(exifDirectory, TagOffsetTimeOriginal))` | `new DateCandidate(original, DatePrecision.Second, CandidateOrigin.ExifOriginal, ReadOffset(exifDirectory, TagOffsetTimeOriginal))` |
| `PhotoApp.Metadata/NativeImageMetadataReader.cs` (numérisation) | `new DateCandidate(digitized, true, ReadOffset(exifDirectory, TagOffsetTimeDigitized))` | `new DateCandidate(digitized, DatePrecision.Second, CandidateOrigin.ExifDigitized, ReadOffset(exifDirectory, TagOffsetTimeDigitized))` |
| `PhotoApp.Core/Services/FileNameDateParser.cs` l. 60 | `new DateCandidate(wallClock, hasTime)` | `new DateCandidate(wallClock, hasTime ? DatePrecision.Second : DatePrecision.Day, CandidateOrigin.FileName)` |
| `PhotoApp.Core/Services/FileNameDateParser.cs` l. 74 | `new DateCandidate(dateOnly, false)` | `new DateCandidate(dateOnly, DatePrecision.Day, CandidateOrigin.FileName)` |
| `PhotoApp.Core/Services/FileNameDateParser.cs` l. 102 | `new DateCandidate(instant.UtcDateTime, true, TimeSpan.Zero)` | `new DateCandidate(instant.UtcDateTime, DatePrecision.Second, CandidateOrigin.FileName, isUtc: true)` |
| `PhotoApp.Core/Services/FileNameDateParser.cs` l. 136 | `new DateCandidate(wallClock, true)` | `new DateCandidate(wallClock, DatePrecision.Second, CandidateOrigin.FileName)` |

`PhotoApp.Core/Services/XmpDateParser.cs` (réécrit en entier) :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>
/// Lit une date ISO 8601 (XMP, clés Apple QuickTime) en conservant l'heure écrite : aucune conversion en UTC.
/// « Z » désigne un instant UTC dont l'heure murale du lieu est inconnue.
/// </summary>
public static class XmpDateParser
{
    private static readonly Regex Pattern = new(
        "^(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})(?:[T ](?<hour>\\d{2}):(?<minute>\\d{2})(?::(?<second>\\d{2})(?:[.,](?<fraction>\\d+))?)?)?\\s*(?<zone>Z|[+-]\\d{2}:?\\d{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateCandidate? Parse(string? text, CandidateOrigin origin = CandidateOrigin.Xmp)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Pattern.Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var hasTime = match.Groups["hour"].Success;
        var hasSecond = match.Groups["second"].Success;
        var millisecond = SubSeconds.ToMilliseconds(match.Groups["fraction"].Value) ?? 0;
        var precision = !hasTime ? DatePrecision.Day
            : match.Groups["fraction"].Success ? DatePrecision.SubSecond
            : hasSecond ? DatePrecision.Second
            : DatePrecision.Minute;

        DateTime wallClock;
        try
        {
            wallClock = new DateTime(
                Number(match, "year"),
                Number(match, "month"),
                Number(match, "day"),
                hasTime ? Number(match, "hour") : 0,
                hasTime ? Number(match, "minute") : 0,
                hasSecond ? Number(match, "second") : 0,
                millisecond,
                DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var zone = hasTime && match.Groups["zone"].Success ? match.Groups["zone"].Value : null;
        if (zone is "Z" or "z")
        {
            return new DateCandidate(wallClock, precision, origin, isUtc: true);
        }

        TimeSpan? offset = zone is not null && UtcOffsetParser.TryParse(zone, out var parsed) ? parsed : null;
        return new DateCandidate(wallClock, precision, origin, offset);
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
```

- [ ] **Étape 6 : Adapter les tests existants**

| Fichier | Avant | Après |
|---|---|---|
| `DatePlausibilityTests.cs` (2 fois) | `new DateCandidate(new DateTime(…), true)` | `new DateCandidate(new DateTime(…), DatePrecision.Second)` |
| `DateSelectorTests.cs` `At` | `new(new DateTime(year, month, day, 10, 0, 0), hasTime)` | `new(new DateTime(year, month, day, 10, 0, 0), hasTime ? DatePrecision.Second : DatePrecision.Day)` |
| `DateSelectorTests.cs` l. 29 | `new DateCandidate(new DateTime(2000, 1, 1, 0, 0, 0), true)` | `new DateCandidate(new DateTime(2000, 1, 1, 0, 0, 0), DatePrecision.Second)` |
| `ImageDateResolverTests.cs` (6 fois) | `…, true)` / `…, false)` | `…, DatePrecision.Second)` / `…, DatePrecision.Day)` |
| `NormalizedFileNameBuilderTests.cs` l. 31 | `new DateCandidate(new DateTime(…), hasTime)` | `new DateCandidate(new DateTime(…), hasTime ? DatePrecision.Second : DatePrecision.Day)` |
| `RenamePlannerTests.cs` l. 11 | `new DateCandidate(wallClock, hasTime)` | `new DateCandidate(wallClock, hasTime ? DatePrecision.Second : DatePrecision.Day)` |
| `NameAllocatorTests.cs` | `new(new DateTime(2024, 1, 5, 15, 4, 12), true)` / `new(new DateTime(2024, 1, 5), false)` | `new(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second)` / `new(new DateTime(2024, 1, 5), DatePrecision.Day)` |
| `BatchRenameExecutorTests.cs` l. 9 | `new(new DateTime(2023, 1, 1, 12, 0, 0), true, TimeSpan.Zero)` | `new(new DateTime(2023, 1, 1, 12, 0, 0), DatePrecision.Second, offset: TimeSpan.Zero)` |
| `FileNameDateParserTests.cs` `Parse_EpochNames_AreUtcWithZeroOffset` | `Assert.Equal(TimeSpan.Zero, result.Offset);` | `Assert.True(result.IsUtc);` (renommer le test `Parse_EpochNames_AreUtc`) |

Ajouter `using PhotoApp.Core.Models;` dans chacun de ces fichiers de test s'il manque.

- [ ] **Étape 7 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : 0 échec ; les nouveaux tests `DateCandidateTests` et `XmpDateParserTests` passent. `grep -rn ImageDateCandidate --include=*.cs . | grep -v '/bin/\|/obj/'` ne renvoie rien.

- [ ] **Étape 8 : Commiter**

```bash
git add -A
git commit -m "refactor: DateCandidate avec précision, origine et UTC (remplace ImageDateCandidate)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 2 : Comparaison des dates (concordance, écart de fuseau, décalage GPS)

**Fichiers :**
- Créer : `PhotoApp.Core/Normalization/DateComparison.cs`
- Test : `PhotoApp.Tests/DateComparisonTests.cs`

**Interfaces :**
- Consomme : `DateCandidate`, `DatePrecision` (tâche 1).
- Produit : `DateComparison.Concordant(DateCandidate a, DateCandidate b) → bool` ; `DateComparison.ZoneGap(DateCandidate local, DateCandidate utc) → TimeSpan?` ; `DateComparison.OffsetFromGps(DateCandidate exif, DateCandidate gps) → TimeSpan?` ; `DateComparison.CompareAt(DateCandidate a, DateCandidate b, DatePrecision precision) → int` ; constantes `SecondTolerance` (2 s), `ZoneStep` (15 min), `MaxZoneOffset` (14 h), `GpsTolerance` (2 min).

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/DateComparisonTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class DateComparisonTests
{
    private static DateCandidate Local(int h, int m, int s, TimeSpan? offset = null, DatePrecision precision = DatePrecision.Second, int day = 5) =>
        new(new DateTime(2024, 1, day, h, m, s), precision, CandidateOrigin.ExifOriginal, offset);

    private static DateCandidate Utc(int h, int m, int s, int day = 5) =>
        new(new DateTime(2024, 1, day, h, m, s), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

    [Theory]
    [InlineData(15, 4, 12, true)]
    [InlineData(15, 4, 14, true)]    // 2 s : toléré
    [InlineData(15, 4, 15, false)]   // 3 s : refusé
    public void Concordant_Seconds_WithinTwoSeconds(int h, int m, int s, bool expected)
    {
        Assert.Equal(expected, DateComparison.Concordant(Local(15, 4, 12), Local(h, m, s)));
    }

    [Fact]
    public void Concordant_ComparesAtTheWeakerPrecision()
    {
        var day = Local(0, 0, 0, precision: DatePrecision.Day);

        Assert.True(DateComparison.Concordant(day, Local(23, 59, 59)));
        Assert.False(DateComparison.Concordant(day, Local(10, 0, 0, day: 6)));
        Assert.True(DateComparison.Concordant(Local(15, 4, 0, precision: DatePrecision.Minute), Local(15, 4, 59)));
    }

    [Fact]
    public void Concordant_TwoKnownInstants_AreComparedAsInstants()
    {
        Assert.True(DateComparison.Concordant(Local(10, 0, 0, TimeSpan.FromHours(2)), Local(9, 0, 0, TimeSpan.FromHours(1))));
        Assert.True(DateComparison.Concordant(Local(10, 0, 0, TimeSpan.FromHours(2)), Utc(8, 0, 0)));
    }

    [Fact]
    public void Concordant_FloatingLocalAgainstUtc_IsNotDecidedHere()
    {
        Assert.False(DateComparison.Concordant(Local(10, 0, 0), Utc(10, 0, 0)));
    }

    [Theory]
    [InlineData(22, 29, 44, 2.0)]     // Samsung : nom local, mvhd UTC
    [InlineData(22, 29, 45, 2.0)]     // 1 s d'écart : toléré
    [InlineData(20, 29, 44, 0.0)]     // écart nul : fuseau UTC+0
    [InlineData(1, 59, 44, 5.5)]      // Inde : +05:30 (le lendemain)
    public void ZoneGap_IsAMultipleOfAQuarterHour(int h, int m, int s, double expectedHours)
    {
        var local = new DateCandidate(new DateTime(2026, 9, h < 12 ? 18 : 17, h, m, s), DatePrecision.Second, CandidateOrigin.FileName);
        var utc = new DateCandidate(new DateTime(2026, 9, 17, 20, 29, 44), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

        Assert.Equal(TimeSpan.FromHours(expectedHours), DateComparison.ZoneGap(local, utc));
    }

    [Fact]
    public void ZoneGap_IsRefused_WhenNotAQuarterHour_TooLarge_OrTooCoarse()
    {
        Assert.Null(DateComparison.ZoneGap(Local(22, 36, 44), Utc(20, 29, 44)));            // 7 min de trop
        Assert.Null(DateComparison.ZoneGap(Local(12, 0, 0, day: 6), Utc(20, 0, 0)));        // 16 h
        Assert.Null(DateComparison.ZoneGap(Local(0, 0, 0, precision: DatePrecision.Day), Utc(20, 0, 0)));
        Assert.Null(DateComparison.ZoneGap(Utc(20, 0, 0), Utc(20, 0, 0)));                  // il faut une heure locale
    }

    [Fact]
    public void OffsetFromGps_RoundsToAQuarterHour_WithinTwoMinutes()
    {
        var exif = Local(13, 20, 15);
        var gps = new DateCandidate(new DateTime(2024, 1, 5, 12, 20, 14), DatePrecision.Second, CandidateOrigin.Gps, isUtc: true);
        var staleGps = new DateCandidate(new DateTime(2024, 1, 5, 12, 17, 0), DatePrecision.Second, CandidateOrigin.Gps, isUtc: true);

        Assert.Equal(TimeSpan.FromHours(1), DateComparison.OffsetFromGps(exif, gps));
        Assert.Null(DateComparison.OffsetFromGps(exif, staleGps));   // 3 min 15 s de reste
    }

    [Fact]
    public void CompareAt_TruncatesToThePrecision()
    {
        var morning = Local(8, 0, 0);
        var evening = Local(20, 0, 0);

        Assert.Equal(0, DateComparison.CompareAt(morning, evening, DatePrecision.Day));
        Assert.True(DateComparison.CompareAt(morning, evening, DatePrecision.Second) < 0);
        Assert.True(DateComparison.CompareAt(Local(8, 0, 0, day: 6), evening, DatePrecision.Day) > 0);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `DateComparison` introuvable.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Normalization/DateComparison.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Tolérances de la spec de phase 1 (§5.2), revues après la mesure de mi-phase.</summary>
public static class DateComparison
{
    public static readonly TimeSpan SecondTolerance = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ZoneStep = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxZoneOffset = TimeSpan.FromHours(14);
    public static readonly TimeSpan GpsTolerance = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Deux valeurs concordent à la précision la plus faible des deux. Deux instants connus se comparent comme instants ;
    /// deux heures locales, comme heures murales. Une heure locale flottante face à un UTC relève de <see cref="ZoneGap"/>.
    /// </summary>
    public static bool Concordant(DateCandidate a, DateCandidate b)
    {
        DateTime left;
        DateTime right;
        if (a.HasKnownInstant && b.HasKnownInstant)
        {
            left = UtcOf(a);
            right = UtcOf(b);
        }
        else if (!a.IsUtc && !b.IsUtc)
        {
            left = a.WallClock;
            right = b.WallClock;
        }
        else
        {
            return false;
        }

        var precision = (DatePrecision)Math.Min((int)a.Precision, (int)b.Precision);
        if (precision == DatePrecision.Day && !a.IsUtc && !b.IsUtc)
        {
            // Le jour se lit sur l'heure murale : c'est celui qu'affiche l'appareil.
            return a.WallClock.Date == b.WallClock.Date;
        }

        return precision switch
        {
            DatePrecision.Day => left.Date == right.Date,
            DatePrecision.Minute => Truncate(left, DatePrecision.Minute) == Truncate(right, DatePrecision.Minute),
            _ => (left - right).Duration() <= SecondTolerance
        };
    }

    /// <summary>Écart de fuseau valide entre une heure locale et une valeur UTC : multiple de 15 min à 2 s près, au plus 14 h.</summary>
    public static TimeSpan? ZoneGap(DateCandidate local, DateCandidate utc)
    {
        if (local.IsUtc || !utc.IsUtc || local.Precision < DatePrecision.Second || utc.Precision < DatePrecision.Second)
        {
            return null;
        }

        return RoundToZone(local.WallClock - utc.WallClock, SecondTolerance);
    }

    /// <summary>Décalage déduit du GPS : DateTimeOriginal − GPS, arrondi au quart d'heure, reste d'au plus 2 min.</summary>
    public static TimeSpan? OffsetFromGps(DateCandidate exif, DateCandidate gps)
    {
        if (exif.IsUtc || !gps.IsUtc)
        {
            return null;
        }

        return RoundToZone(exif.WallClock - gps.WallClock, GpsTolerance);
    }

    /// <summary>Compare les heures murales tronquées à la précision donnée : négatif si a est antérieur.</summary>
    public static int CompareAt(DateCandidate a, DateCandidate b, DatePrecision precision) =>
        Truncate(a.WallClock, precision).CompareTo(Truncate(b.WallClock, precision));

    private static TimeSpan? RoundToZone(TimeSpan difference, TimeSpan tolerance)
    {
        var steps = Math.Round(difference.Ticks / (double)ZoneStep.Ticks);
        var rounded = TimeSpan.FromTicks((long)steps * ZoneStep.Ticks);
        if ((difference - rounded).Duration() > tolerance || rounded.Duration() > MaxZoneOffset)
        {
            return null;
        }

        return rounded;
    }

    private static DateTime UtcOf(DateCandidate candidate) =>
        candidate.IsUtc ? candidate.WallClock : candidate.WallClock - candidate.Offset!.Value;

    private static DateTime Truncate(DateTime value, DatePrecision precision) => precision switch
    {
        DatePrecision.Day => value.Date,
        DatePrecision.Minute => new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute),
        DatePrecision.Second => new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond),
        _ => value
    };
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~DateComparisonTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core/Normalization/DateComparison.cs PhotoApp.Tests/DateComparisonTests.cs
git commit -m "feat: comparaison des dates (concordance, écart de fuseau, décalage GPS)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 3 : Modèle des profils, lecture des motifs de nom, parseur générique

**Fichiers :**
- Créer : `PhotoApp.Core/Profiles/ProfileFamily.cs`, `NameTimeSemantics.cs`, `DayMonthOrder.cs`, `SourceProfile.cs`, `NameReading.cs`, `NamePatternReader.cs`, `GenericNameParser.cs`
- Test : `PhotoApp.Tests/GenericNameParserTests.cs`

**Interfaces :**
- Consomme : `DateCandidate`, `DatePrecision`, `CandidateOrigin`, `SubSeconds` (tâche 1).
- Produit :
  - `enum ProfileFamily { Camera, Messaging, Screenshot, Export }` ; `enum NameTimeSemantics { Local, Utc }` ; `enum DayMonthOrder { Unknown, DayFirst, MonthFirst }` ;
  - `sealed record SourceProfile(string Id, string DisplayName, ProfileFamily Family, IReadOnlyList<Regex> NamePatterns, NameTimeSemantics TimeSemantics = Local, DayMonthOrder DayMonthOrder = Unknown, Regex? MakePattern = null, bool NameNeedsMake = false, Regex? FolderPattern = null)` ;
  - `sealed record NameReading(SourceProfile? Profile, DateCandidate? Date, AmbiguousName? Ambiguous, long? Counter, bool FromProfilePattern, bool IsDerivative, DayMonthEvidence? Evidence = null)` avec `NameReading.None` ;
  - `sealed record AmbiguousName(DateCandidate DayFirst, DateCandidate MonthFirst, string PatternKey)` ; `sealed record DayMonthEvidence(string PatternKey, DayMonthOrder Order)` ;
  - `NamePatternReader.Read(Match match, SourceProfile? profile, string patternKey, bool isDerivative) → NameReading` ;
  - `GenericNameParser.Parse(string stem, bool isDerivative = false) → NameReading`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/GenericNameParserTests.cs` (reprend les cas génériques de `FileNameDateParserTests`) :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class GenericNameParserTests
{
    [Theory]
    [InlineData("Persona 5 Royal 15.01.2025 00_54_14", 2025, 1, 15, 0, 54, 14)]   // JJ.MM : 15 > 12
    [InlineData("12.25.2024 10-30-00", 2024, 12, 25, 10, 30, 0)]                  // MM.JJ : 25 > 12
    [InlineData("vacances 2019-05-01 10-11-12", 2019, 5, 1, 10, 11, 12)]
    [InlineData("export_20230304-132015", 2023, 3, 4, 13, 20, 15)]
    [InlineData("scan 2019-05-01 101112", 2019, 5, 1, 10, 11, 12)]
    public void Parse_DatesWithTime(string stem, int year, int month, int day, int hour, int minute, int second)
    {
        var reading = GenericNameParser.Parse(stem);

        Assert.NotNull(reading.Date);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), reading.Date.WallClock);
        Assert.Equal(DatePrecision.Second, reading.Date.Precision);
        Assert.Equal(CandidateOrigin.FileName, reading.Date.Origin);
        Assert.False(reading.FromProfilePattern);
        Assert.Null(reading.Profile);
    }

    [Theory]
    [InlineData("scan 20190501")]
    [InlineData("anniversaire 2019-05-01")]
    public void Parse_DayOnly(string stem)
    {
        var reading = GenericNameParser.Parse(stem);

        Assert.NotNull(reading.Date);
        Assert.Equal(new DateTime(2019, 5, 1), reading.Date.WallClock);
        Assert.Equal(DatePrecision.Day, reading.Date.Precision);
    }

    [Fact]
    public void Parse_AmbiguousDayMonth_KeepsBothReadings()
    {
        var reading = GenericNameParser.Parse("03-04-2025 10-00-00");

        Assert.Null(reading.Date);
        Assert.NotNull(reading.Ambiguous);
        Assert.Equal(new DateTime(2025, 4, 3, 10, 0, 0), reading.Ambiguous.DayFirst.WallClock);
        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0), reading.Ambiguous.MonthFirst.WallClock);
    }

    [Fact]
    public void Parse_UnambiguousDayMonth_GivesEvidenceOfTheOrder()
    {
        var dayFirst = GenericNameParser.Parse("25-12-2024 10-00-00");
        var ambiguous = GenericNameParser.Parse("03-04-2025 10-00-00");

        Assert.Equal(new DayMonthEvidence(ambiguous.Ambiguous!.PatternKey, DayMonthOrder.DayFirst), dayFirst.Evidence);
    }

    [Fact]
    public void Parse_SameDayAndMonth_IsNotAmbiguous()
    {
        var reading = GenericNameParser.Parse("04-04-2025 10-00-00");

        Assert.Equal(new DateTime(2025, 4, 4, 10, 0, 0), reading.Date?.WallClock);
        Assert.Null(reading.Ambiguous);
    }

    [Theory]
    [InlineData("random-file-name")]
    [InlineData("IMG_12345678")]       // 1234-56-78 : pas une date
    [InlineData("")]
    public void Parse_NoDate(string stem)
    {
        var reading = GenericNameParser.Parse(stem);

        Assert.Null(reading.Date);
        Assert.Null(reading.Ambiguous);
    }

    [Fact]
    public void Parse_KeepsImplausibleDates_ForTheArbiterToReject()
    {
        Assert.Equal(new DateTime(2099, 12, 31, 23, 59, 59), GenericNameParser.Parse("x_20991231_235959").Date?.WallClock);
    }

    [Fact]
    public void Parse_CarriesTheDerivativeFlag()
    {
        Assert.True(GenericNameParser.Parse("photo", isDerivative: true).IsDerivative);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : espace de noms `PhotoApp.Core.Profiles` introuvable.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Profiles/ProfileFamily.cs` :

```csharp
namespace PhotoApp.Core.Profiles;

/// <summary>Famille d'une source : elle décide des règles qui s'appliquent (spec de phase 1, §5.4).</summary>
public enum ProfileFamily
{
    Camera,
    Messaging,
    Screenshot,
    Export
}
```

`PhotoApp.Core/Profiles/NameTimeSemantics.cs` :

```csharp
namespace PhotoApp.Core.Profiles;

/// <summary>Sens de l'heure écrite dans le nom : heure locale (cas général) ou UTC (epoch, Pixel).</summary>
public enum NameTimeSemantics
{
    Local,
    Utc
}
```

`PhotoApp.Core/Profiles/DayMonthOrder.cs` :

```csharp
namespace PhotoApp.Core.Profiles;

public enum DayMonthOrder
{
    Unknown,
    DayFirst,
    MonthFirst
}
```

`PhotoApp.Core/Profiles/SourceProfile.cs` :

```csharp
using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>
/// Profil de source, déclaratif (spec de phase 1, §6). Les motifs s'appliquent au nom sans extension et utilisent
/// les groupes nommés year, month, day, hour, minute, second, fraction, ampm, epoch, d1/d2 (jour ou mois) et counter.
/// </summary>
/// <param name="MakePattern">Marque EXIF ou QuickTime : reconnaissance sans motif de nom, ou condition du motif.</param>
/// <param name="NameNeedsMake">Le motif est trop courant pour suffire seul (IMG_1234, IMG_AAAAMMJJ_HHmmss) : la marque doit concorder.</param>
public sealed record SourceProfile(
    string Id,
    string DisplayName,
    ProfileFamily Family,
    IReadOnlyList<Regex> NamePatterns,
    NameTimeSemantics TimeSemantics = NameTimeSemantics.Local,
    DayMonthOrder DayMonthOrder = DayMonthOrder.Unknown,
    Regex? MakePattern = null,
    bool NameNeedsMake = false,
    Regex? FolderPattern = null);
```

`PhotoApp.Core/Profiles/NameReading.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Profiles;

/// <summary>Ce que le nom d'un fichier apprend : profil reconnu, date, ambiguïté JJ/MM, compteur (WAnnnn…).</summary>
/// <param name="FromProfilePattern">La lecture vient d'un motif de profil (et non du parseur générique) : le nom est alors un candidat fort.</param>
/// <param name="IsDerivative">Un suffixe de copie (-min, (1), - Copie) a été retiré pour lire le nom.</param>
/// <param name="Evidence">Ordre JJ/MM prouvé par ce nom (une composante > 12) : sert au consensus des voisins.</param>
public sealed record NameReading(
    SourceProfile? Profile,
    DateCandidate? Date,
    AmbiguousName? Ambiguous,
    long? Counter,
    bool FromProfilePattern,
    bool IsDerivative,
    DayMonthEvidence? Evidence = null)
{
    public static NameReading None { get; } = new(null, null, null, null, false, false);
}

/// <summary>Les deux lectures d'une date JJ/MM ou MM/JJ, et la clé du motif qui l'a produite.</summary>
public sealed record AmbiguousName(DateCandidate DayFirst, DateCandidate MonthFirst, string PatternKey);

public sealed record DayMonthEvidence(string PatternKey, DayMonthOrder Order);
```

`PhotoApp.Core/Profiles/NamePatternReader.cs` :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Profiles;

/// <summary>Construit la lecture d'un nom à partir des groupes nommés d'un motif. Aucune plausibilité ici : l'arbitre s'en charge.</summary>
public static class NamePatternReader
{
    public static NameReading Read(Match match, SourceProfile? profile, string patternKey, bool isDerivative)
    {
        var semantics = profile?.TimeSemantics ?? NameTimeSemantics.Local;
        var reading = new NameReading(profile, null, null, Counter(match), profile is not null, isDerivative);

        if (match.Groups["epoch"].Success)
        {
            return reading with { Date = Epoch(match.Groups["epoch"].Value) };
        }

        if (match.Groups["d1"].Success)
        {
            return ReadDayMonth(match, reading, patternKey, profile?.DayMonthOrder ?? DayMonthOrder.Unknown, semantics);
        }

        if (!match.Groups["year"].Success)
        {
            return reading;
        }

        return reading with { Date = Build(match, Number(match, "month"), Number(match, "day"), semantics) };
    }

    private static NameReading ReadDayMonth(Match match, NameReading reading, string patternKey, DayMonthOrder declared, NameTimeSemantics semantics)
    {
        var first = Number(match, "d1");
        var second = Number(match, "d2");
        var dayFirst = Build(match, month: second, day: first, semantics);
        var monthFirst = Build(match, month: first, day: second, semantics);

        if (first == second)
        {
            return reading with { Date = dayFirst };
        }

        // Une seule lecture valide (composante > 12) : elle prouve l'ordre du motif pour les voisins.
        if (monthFirst is null)
        {
            return dayFirst is null ? reading : reading with { Date = dayFirst, Evidence = new DayMonthEvidence(patternKey, DayMonthOrder.DayFirst) };
        }

        if (dayFirst is null)
        {
            return reading with { Date = monthFirst, Evidence = new DayMonthEvidence(patternKey, DayMonthOrder.MonthFirst) };
        }

        return declared switch
        {
            DayMonthOrder.DayFirst => reading with { Date = dayFirst },
            DayMonthOrder.MonthFirst => reading with { Date = monthFirst },
            _ => reading with { Ambiguous = new AmbiguousName(dayFirst, monthFirst, patternKey) }
        };
    }

    private static DateCandidate? Build(Match match, int month, int day, NameTimeSemantics semantics)
    {
        var year = Number(match, "year");
        if (match.Groups["year"].Length == 2)
        {
            year += 2000;
        }

        var precision = DatePrecision.Day;
        int hour = 0, minute = 0, second = 0, millisecond = 0;
        if (match.Groups["hour"].Success)
        {
            hour = Number(match, "hour");
            minute = Number(match, "minute");
            precision = DatePrecision.Minute;

            if (match.Groups["ampm"].Success)
            {
                if (hour is < 1 or > 12)
                {
                    return null;
                }

                var isPm = char.ToUpperInvariant(match.Groups["ampm"].Value[0]) == 'P';
                hour = hour % 12 + (isPm ? 12 : 0);
            }

            if (match.Groups["second"].Success)
            {
                second = Number(match, "second");
                precision = DatePrecision.Second;
            }

            if (match.Groups["fraction"].Success && SubSeconds.ToMilliseconds(match.Groups["fraction"].Value) is { } ms)
            {
                millisecond = ms;
                precision = DatePrecision.SubSecond;
            }
        }

        try
        {
            var value = new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Unspecified);
            return new DateCandidate(value, precision, CandidateOrigin.FileName, isUtc: semantics == NameTimeSemantics.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // Exports WeChat, Facebook… : le nombre est un instant UTC, en secondes (10 chiffres) ou millisecondes (13).
    private static DateCandidate? Epoch(string text)
    {
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        try
        {
            var milliseconds = text.Length == 13;
            var instant = milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value);
            return new DateCandidate(
                instant.UtcDateTime, milliseconds ? DatePrecision.SubSecond : DatePrecision.Second, CandidateOrigin.FileName, isUtc: true);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static long? Counter(Match match) =>
        match.Groups["counter"].Success && long.TryParse(match.Groups["counter"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var counter)
            ? counter
            : null;

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
```

`PhotoApp.Core/Profiles/GenericNameParser.cs` :

```csharp
using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>Filet pour les noms qu'aucun profil ne reconnaît : une date est cherchée n'importe où dans le nom.</summary>
public static class GenericNameParser
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Du plus précis au moins précis ; le premier motif qui donne une date (ou une ambiguïté) l'emporte.
    private static readonly Regex[] Patterns =
    [
        // 20230304_132015 / 20230304-132015123
        new(@"(?<!\d)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})[_-](?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})(?<fraction>\d{3})?(?!\d)", Options),
        // 2019-05-01 10-11-12 / 2019.05.01_10.11.12 / 2019-05-01T10:11:12
        new(@"(?<!\d)(?<year>\d{4})[-_.](?<month>\d{2})[-_.](?<day>\d{2})[ T_-](?<hour>\d{2})[-_.:](?<minute>\d{2})[-_.:](?<second>\d{2})(?!\d)", Options),
        // 2019-05-01 101112
        new(@"(?<!\d)(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})[ _](?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})(?!\d)", Options),
        // 15.01.2025 00_54_14 (JJ.MM) ou 12.25.2024 10-30-00 (MM.JJ)
        new(@"(?<!\d)(?<d1>\d{2})[.-](?<d2>\d{2})[.-](?<year>\d{4})[ T_-](?<hour>\d{2})[-_.](?<minute>\d{2})[-_.](?<second>\d{2})(?!\d)", Options),
        // 15.01.2025 (jour seul, JJ.MM ou MM.JJ)
        new(@"(?<!\d)(?<d1>\d{2})[.-](?<d2>\d{2})[.-](?<year>\d{4})(?!\d)", Options),
        // 2019-05-01 (jour seul)
        new(@"(?<!\d)(?<year>\d{4})[-_.](?<month>\d{2})[-_.](?<day>\d{2})(?!\d)", Options),
        // 20190501 (jour seul)
        new(@"(?<!\d)(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?!\d)", Options)
    ];

    public static NameReading Parse(string stem, bool isDerivative = false)
    {
        for (var index = 0; index < Patterns.Length; index++)
        {
            foreach (Match match in Patterns[index].Matches(stem))
            {
                var reading = NamePatternReader.Read(match, profile: null, $"generique#{index}", isDerivative);
                if (reading.Date is not null || reading.Ambiguous is not null)
                {
                    return reading;
                }
            }
        }

        return NameReading.None with { IsDerivative = isDerivative };
    }
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~GenericNameParserTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core/Profiles PhotoApp.Tests/GenericNameParserTests.cs
git commit -m "feat: modèle des profils et parseur générique de noms" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 4 : La table des profils et leur reconnaissance

**Fichiers :**
- Créer : `PhotoApp.Core/Profiles/SourceProfiles.cs`, `PhotoApp.Core/Profiles/ProfileMatcher.cs`
- Test : `PhotoApp.Tests/SourceProfilesTests.cs`

**Interfaces :**
- Consomme : `SourceProfile`, `NameReading`, `NamePatternReader`, `GenericNameParser` (tâche 3).
- Produit : `SourceProfiles.All : IReadOnlyList<SourceProfile>` (identifiants : `whatsapp`, `signal`, `telegram`, `capture-android-kde`, `capture-windows`, `capture-windows-impr-ecran`, `capture-gnome`, `capture-macos`, `pixel`, `xiaomi`, `huawei`, `oneplus`, `android`, `samsung`, `nextcloud-ios`, `iphone`, `wechat`, `facebook`, `snapchat`, `miniature-video`, `epoch`) ; `ProfileMatcher.Read(string fileName, string? make, string? folderName, IReadOnlyList<SourceProfile>? profiles = null) → NameReading` ; `ProfileMatcher.StripCopySuffixes(string stem) → string`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/SourceProfilesTests.cs` :

```csharp
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class SourceProfilesTests
{
    private static DateTime ToSecond(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);

    [Theory]
    // Messageries
    [InlineData("IMG-20211029-WA0039.jpg", null, "whatsapp", 2021, 10, 29, 0, 0, 0, DatePrecision.Day)]
    [InlineData("VID-20151114-WA0017.mp4", null, "whatsapp", 2015, 11, 14, 0, 0, 0, DatePrecision.Day)]
    [InlineData("WhatsApp Image 2023-05-01 at 14.03.22.jpeg", null, "whatsapp", 2023, 5, 1, 14, 3, 22, DatePrecision.Second)]
    [InlineData("WhatsApp Image 2023-05-01 at 2.03.22 PM.jpeg", null, "whatsapp", 2023, 5, 1, 14, 3, 22, DatePrecision.Second)]
    [InlineData("signal-2023-12-18-211842_002.jpg", null, "signal", 2023, 12, 18, 21, 18, 42, DatePrecision.Second)]
    [InlineData("signal-2024-02-01-10-11-12-345.jpg", null, "signal", 2024, 2, 1, 10, 11, 12, DatePrecision.SubSecond)]
    [InlineData("photo_2024-01-05_15-04-12.jpg", null, "telegram", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    // Captures d'écran
    [InlineData("Screenshot_20240410_111652.jpg", null, "capture-android-kde", 2024, 4, 10, 11, 16, 52, DatePrecision.Second)]
    [InlineData("Screenshot_20260318_035813_Firefox.jpg", null, "capture-android-kde", 2026, 3, 18, 3, 58, 13, DatePrecision.Second)]
    [InlineData("Screenshot_20241016_172511 1.jpg", null, "capture-android-kde", 2024, 10, 16, 17, 25, 11, DatePrecision.Second)]
    [InlineData("Screenshot_2019-05-01-10-11-12.png", null, "capture-android-kde", 2019, 5, 1, 10, 11, 12, DatePrecision.Second)]
    [InlineData("Screenshot 2025-08-27 211650.jpg", null, "capture-windows", 2025, 8, 27, 21, 16, 50, DatePrecision.Second)]
    [InlineData("Capture d’écran 2025-08-27 211650.png", null, "capture-windows", 2025, 8, 27, 21, 16, 50, DatePrecision.Second)]
    [InlineData("Capture d’écran du 2026-03-27 01-22-28.png", null, "capture-gnome", 2026, 3, 27, 1, 22, 28, DatePrecision.Second)]
    [InlineData("Capture d'écran du 2026-03-27 12-01-17.png", null, "capture-gnome", 2026, 3, 27, 12, 1, 17, DatePrecision.Second)]
    [InlineData("Screenshot from 2023-02-03 04-05-06.png", null, "capture-gnome", 2023, 2, 3, 4, 5, 6, DatePrecision.Second)]
    [InlineData("Screenshot 2024-01-05 at 3.04.12 PM.png", null, "capture-macos", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    [InlineData("Screenshot 2024-01-05 at 12.04.12 AM.png", null, "capture-macos", 2024, 1, 5, 0, 4, 12, DatePrecision.Second)]
    [InlineData("Screenshot 2024-01-05 at 3.04.12 PM.png", null, "capture-macos", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    [InlineData("Capture d’écran 2024-01-05 à 15.04.12.png", null, "capture-macos", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
    // Appareils
    [InlineData("IMG_20230304_132015.jpg", "Xiaomi", "xiaomi", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", "HUAWEI", "huawei", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", "OnePlus", "oneplus", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", null, "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("IMG_20230304_132015.jpg", "Canon", "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("VID_20230304_132015.mp4", null, "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("MVIMG_20230304_132015.jpg", null, "android", 2023, 3, 4, 13, 20, 15, DatePrecision.Second)]
    [InlineData("20260917_222944.heic", "samsung", "samsung", 2026, 9, 17, 22, 29, 44, DatePrecision.Second)]
    [InlineData("20260411_001511.jpg", null, "samsung", 2026, 4, 11, 0, 15, 11, DatePrecision.Second)]
    [InlineData("24-07-11 17-07-10 0477.mov", null, "nextcloud-ios", 2024, 7, 11, 17, 7, 10, DatePrecision.Second)]
    [InlineData("PXL_20230304_122015123.jpg", null, "pixel", 2023, 3, 4, 12, 20, 15, DatePrecision.SubSecond)]
    [InlineData("PXL_20230304_122015123.MP.jpg", null, "pixel", 2023, 3, 4, 12, 20, 15, DatePrecision.SubSecond)]
    public void Read_RecognizesTheProfileAndTheDate(
        string fileName, string? make, string profileId, int year, int month, int day, int hour, int minute, int second, DatePrecision precision)
    {
        var reading = ProfileMatcher.Read(fileName, make, folderName: null);

        Assert.Equal(profileId, reading.Profile?.Id);
        Assert.True(reading.FromProfilePattern);
        Assert.NotNull(reading.Date);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), ToSecond(reading.Date.WallClock));
        Assert.Equal(precision, reading.Date.Precision);
        Assert.Equal(CandidateOrigin.FileName, reading.Date.Origin);
    }

    [Theory]
    [InlineData("mmexport1700000000000.jpg", "wechat")]
    [InlineData("FB_IMG_1700000000.jpg", "facebook")]
    [InlineData("1700000000000.jpg", "epoch")]
    public void Read_EpochNames_AreUtc(string fileName, string profileId)
    {
        var reading = ProfileMatcher.Read(fileName, null, null);

        Assert.Equal(profileId, reading.Profile?.Id);
        Assert.True(reading.Date?.IsUtc);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20), reading.Date!.WallClock);
    }

    [Fact]
    public void Read_PixelNames_AreUtc()
    {
        Assert.True(ProfileMatcher.Read("PXL_20230304_122015123.jpg", null, null).Date?.IsUtc);
    }

    [Theory]
    [InlineData("IMG-20211029-WA0039.jpg", 39)]
    [InlineData("24-07-11 17-07-10 0477.jpg", 477)]
    [InlineData("signal-2023-12-18-211842_002.jpg", 2)]
    [InlineData("20260917_222944(1).jpg", 1)]
    [InlineData("Screenshot (12).png", 12)]
    public void Read_ExtractsTheCounter(string fileName, long counter)
    {
        Assert.Equal(counter, ProfileMatcher.Read(fileName, null, null).Counter);
    }

    [Theory]
    [InlineData("IMG_0477.HEIC", "Apple", "iphone")]
    [InlineData("hqdefault-4B744.jpg", null, "miniature-video")]
    [InlineData("Snapchat-123456789.jpg", null, "snapchat")]
    [InlineData("Screenshot (12).png", null, "capture-windows-impr-ecran")]
    public void Read_ProfilesWithoutADateInTheName(string fileName, string? make, string profileId)
    {
        var reading = ProfileMatcher.Read(fileName, make, null);

        Assert.Equal(profileId, reading.Profile?.Id);
        Assert.Null(reading.Date);
    }

    [Fact]
    public void Read_IPhoneNameWithoutAppleMake_IsNotRecognized()
    {
        Assert.Null(ProfileMatcher.Read("IMG_0477.HEIC", "Canon", null).Profile);
    }

    [Fact]
    public void Read_CopySuffix_IsStrippedOnlyWhenNeeded_AndFlagged()
    {
        var compressed = ProfileMatcher.Read("IMG_20230304_132015-min.jpg", null, null);
        var burst = ProfileMatcher.Read("20260917_222944(1).jpg", null, null);
        var copy = ProfileMatcher.Read("photo (1).jpg", null, null);

        Assert.Equal("android", compressed.Profile?.Id);
        Assert.True(compressed.IsDerivative);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), compressed.Date?.WallClock);
        Assert.False(burst.IsDerivative);
        Assert.True(copy.IsDerivative);
        Assert.Null(copy.Profile);
    }

    [Theory]
    [InlineData("IMG_20230304_132015-min - Copie (2)", "IMG_20230304_132015")]
    [InlineData("photo (1)", "photo")]
    [InlineData("photo", "photo")]
    public void StripCopySuffixes_RemovesEverySuffix(string stem, string expected)
    {
        Assert.Equal(expected, ProfileMatcher.StripCopySuffixes(stem));
    }

    [Fact]
    public void Read_MakeOnly_GivesTheProfile_AndTheGenericParserReadsTheName()
    {
        var album = ProfileMatcher.Read("001.jpg", "Xiaomi", null);
        var renamed = ProfileMatcher.Read("vacances 2019-05-01 10-11-12.jpg", "samsung", null);

        Assert.Equal("xiaomi", album.Profile?.Id);
        Assert.False(album.FromProfilePattern);
        Assert.Null(album.Date);
        Assert.Equal("samsung", renamed.Profile?.Id);
        Assert.False(renamed.FromProfilePattern);
        Assert.Equal(new DateTime(2019, 5, 1, 10, 11, 12), renamed.Date?.WallClock);
    }

    [Fact]
    public void Read_FolderHint_GivesTheProfile()
    {
        Assert.Equal("snapchat", ProfileMatcher.Read("image.jpg", null, "Snapchat").Profile?.Id);
    }

    [Fact]
    public void Read_UnknownName_HasNoProfileAndNoDate()
    {
        var reading = ProfileMatcher.Read("random.jpg", null, null);

        Assert.Null(reading.Profile);
        Assert.Null(reading.Date);
        Assert.False(reading.FromProfilePattern);
    }

    [Fact]
    public void Read_KeepsImplausibleDates_ForTheArbiterToReject()
    {
        Assert.Equal(new DateTime(1980, 1, 1, 0, 0, 1), ProfileMatcher.Read("IMG_19800101_000001.jpg", null, null).Date?.WallClock);
    }

    [Fact]
    public void Read_ProfileDayMonthOrder_SettlesAnAmbiguousName()
    {
        var pattern = new Regex(@"^cam (?<d1>\d{2})\.(?<d2>\d{2})\.(?<year>\d{4})$");
        SourceProfile[] profiles =
        [
            new("cam-fr", "Caméra FR", ProfileFamily.Camera, [pattern], DayMonthOrder: DayMonthOrder.DayFirst),
        ];

        var reading = ProfileMatcher.Read("cam 03.04.2025.jpg", null, null, profiles);

        Assert.Equal(new DateTime(2025, 4, 3), reading.Date?.WallClock);
        Assert.Null(reading.Ambiguous);
    }

    [Fact]
    public void Read_GenericAmbiguousName_StaysAmbiguous()
    {
        var reading = ProfileMatcher.Read("03-04-2025 10-00-00.jpg", null, null);

        Assert.Null(reading.Date);
        Assert.NotNull(reading.Ambiguous);
    }

    [Fact]
    public void All_HasUniqueIds()
    {
        var ids = SourceProfiles.All.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `ProfileMatcher` et `SourceProfiles` introuvables.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Profiles/SourceProfiles.cs` :

```csharp
using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>
/// La table des profils de sources (spec de phase 1, §6). L'ordre compte : le premier motif reconnu l'emporte.
/// Ajouter une source = ajouter un profil ici et ses cas dans SourceProfilesTests.
/// </summary>
public static class SourceProfiles
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private const string Ymd = @"(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})";
    private const string Hms = @"(?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})";
    private const string IsoDate = @"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})";
    private const string Apostrophe = "[’']";
    private const string AndroidCamera = @"^(?:IMG|VID|MVIMG|PANO)_" + Ymd + "_" + Hms + @"(?:_(?<counter>\d+))?(?:_[A-Z]+\d*)*$";

    public static IReadOnlyList<SourceProfile> All { get; } =
    [
        // Messageries : le nom fait foi.
        new("whatsapp", "WhatsApp", ProfileFamily.Messaging,
        [
            P(@"^(?:IMG|VID|AUD|PTT|STK|DOC)-" + Ymd + @"-WA(?<counter>\d+)$"),
            P(@"^WhatsApp (?:Image|Video) " + IsoDate + @" at (?<hour>\d{1,2})\.(?<minute>\d{2})\.(?<second>\d{2})(?:\s?(?<ampm>[AP]M))?$")
        ]),
        new("signal", "Signal", ProfileFamily.Messaging,
        [
            P(@"^signal-" + IsoDate + "-" + Hms + @"(?:[_-](?<counter>\d+))?$"),
            P(@"^signal-" + IsoDate + @"-(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:-(?<fraction>\d{3}))?(?:[_-](?<counter>\d+))?$")
        ]),
        new("telegram", "Telegram", ProfileFamily.Messaging,
        [
            P(@"^(?:photo|video)_" + IsoDate + @"_(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?: \((?<counter>\d+)\))?$")
        ]),

        // Captures d'écran : le nom est l'heure de capture.
        new("capture-android-kde", "Capture Android / KDE Spectacle", ProfileFamily.Screenshot,
        [
            P(@"^Screenshot_" + Ymd + "[_-]" + Hms + @"(?:[_ -].*)?$"),
            P(@"^Screenshot_" + IsoDate + @"-(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:[_ -].*)?$")
        ]),
        new("capture-windows", "Capture Windows (Outil Capture)", ProfileFamily.Screenshot,
        [
            P(@"^(?:Screenshot|Capture d" + Apostrophe + "écran) " + IsoDate + " " + Hms + @"(?: \((?<counter>\d+)\))?$")
        ]),
        new("capture-windows-impr-ecran", "Capture Windows (Impr. écran)", ProfileFamily.Screenshot,
        [
            P(@"^(?:Screenshot|Capture d" + Apostrophe + @"écran) \((?<counter>\d+)\)$")
        ]),
        new("capture-gnome", "Capture GNOME", ProfileFamily.Screenshot,
        [
            P(@"^(?:Capture d" + Apostrophe + "écran du|Screenshot from) " + IsoDate + @" (?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:-(?<counter>\d+))?$")
        ]),
        new("capture-macos", "Capture macOS", ProfileFamily.Screenshot,
        [
            P(@"^(?:Screenshot|Screen Shot|Capture d" + Apostrophe + "écran|Bildschirmfoto|Captura de pantalla) " + IsoDate
              + @" (?:at|à|um|a las) (?<hour>\d{1,2})[.:](?<minute>\d{2})[.:](?<second>\d{2})(?:\s?(?<ampm>[AP]M))?(?: \((?<counter>\d+)\))?$")
        ]),

        // Appareils.
        new("pixel", "Google Pixel", ProfileFamily.Camera,
        [
            P(@"^PXL_" + Ymd + "_" + Hms + @"(?<fraction>\d{3})(?:[~.].*)?$")
        ], TimeSemantics: NameTimeSemantics.Utc, MakePattern: P("^Google$")),
        new("xiaomi", "Xiaomi", ProfileFamily.Camera, [P(AndroidCamera)], MakePattern: P("^(?:Xiaomi|Redmi|POCO)$"), NameNeedsMake: true),
        new("huawei", "Huawei / Honor", ProfileFamily.Camera, [P(AndroidCamera)], MakePattern: P("^(?:HUAWEI|HONOR)$"), NameNeedsMake: true),
        new("oneplus", "OnePlus", ProfileFamily.Camera, [P(AndroidCamera)], MakePattern: P("^OnePlus$"), NameNeedsMake: true),
        new("android", "Appareil Android", ProfileFamily.Camera, [P(AndroidCamera)]),
        new("samsung", "Samsung", ProfileFamily.Camera,
        [
            P("^" + Ymd + "_" + Hms + @"(?:\((?<counter>\d+)\))?$")
        ], MakePattern: P("^samsung$")),
        new("nextcloud-ios", "iPhone (Nextcloud iOS)", ProfileFamily.Camera,
        [
            P(@"^(?<year>\d{2})-(?<month>\d{2})-(?<day>\d{2}) (?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2}) (?<counter>\d{4})$")
        ]),
        new("iphone", "iPhone", ProfileFamily.Camera,
        [
            P(@"^IMG_E?(?<counter>\d{4})$")
        ], MakePattern: P("^Apple$"), NameNeedsMake: true),

        // Exports et téléchargements.
        new("wechat", "WeChat", ProfileFamily.Export, [P(@"^mmexport(?<epoch>\d{13}|\d{10})(?!\d)")], TimeSemantics: NameTimeSemantics.Utc),
        new("facebook", "Facebook", ProfileFamily.Export, [P(@"^FB_IMG_(?<epoch>\d{13}|\d{10})(?!\d)")], TimeSemantics: NameTimeSemantics.Utc),
        new("snapchat", "Snapchat", ProfileFamily.Export, [P(@"^Snapchat-\d+$")], FolderPattern: P("^Snapchat$")),
        new("miniature-video", "Miniature téléchargée", ProfileFamily.Export, [P(@"^(?:hq|mq|sd|maxres)?default(?:-\w+)?$")]),
        new("epoch", "Horodatage epoch", ProfileFamily.Export, [P(@"^(?<epoch>\d{13}|\d{10})$")], TimeSemantics: NameTimeSemantics.Utc)
    ];

    private static Regex P(string pattern) => new(pattern, Options);
}
```

`PhotoApp.Core/Profiles/ProfileMatcher.cs` :

```csharp
using System.Text.RegularExpressions;

namespace PhotoApp.Core.Profiles;

/// <summary>
/// Reconnaît la source d'un fichier, dans cet ordre : motif de nom, puis marque, puis indice de dossier.
/// Le nom est d'abord lu tel quel ; les suffixes de copie ne sont retirés qu'en cas d'échec (drapeau « dérivé »).
/// </summary>
public static class ProfileMatcher
{
    // « -min » (compression), « (1) » / « 1 (2) », « - Copie », « - Copy (2) ».
    private static readonly Regex CopySuffix = new(
        @"(?:-min|\s?\(\d+\)|\s-\s(?:Copie|Copy)(?:\s\(\d+\))?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static NameReading Read(string fileName, string? make, string? folderName, IReadOnlyList<SourceProfile>? profiles = null)
    {
        profiles ??= SourceProfiles.All;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var stripped = StripCopySuffixes(stem);
        var isDerivative = stripped.Length != stem.Length;

        var byName = ReadByPattern(stem, make, profiles, isDerivative: false)
                     ?? (isDerivative ? ReadByPattern(stripped, make, profiles, isDerivative: true) : null);
        if (byName is not null)
        {
            return byName;
        }

        var profile = profiles.FirstOrDefault(p => make is not null && p.MakePattern?.IsMatch(make) == true)
                      ?? profiles.FirstOrDefault(p => folderName is not null && p.FolderPattern?.IsMatch(folderName) == true);
        return GenericNameParser.Parse(stripped, isDerivative) with { Profile = profile };
    }

    public static string StripCopySuffixes(string stem)
    {
        var current = stem;
        while (true)
        {
            var next = CopySuffix.Replace(current, string.Empty);
            if (next.Length == current.Length || next.Length == 0)
            {
                return current;
            }

            current = next;
        }
    }

    private static NameReading? ReadByPattern(string stem, string? make, IReadOnlyList<SourceProfile> profiles, bool isDerivative)
    {
        foreach (var profile in profiles)
        {
            if (profile.NameNeedsMake && (make is null || profile.MakePattern?.IsMatch(make) != true))
            {
                continue;
            }

            for (var index = 0; index < profile.NamePatterns.Count; index++)
            {
                var match = profile.NamePatterns[index].Match(stem);
                if (match.Success)
                {
                    return NamePatternReader.Read(match, profile, $"{profile.Id}#{index}", isDerivative);
                }
            }
        }

        return null;
    }
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~SourceProfilesTests"`
Attendu : tous réussis. Si un cas échoue, corriger le motif du profil concerné, jamais le test (les noms viennent de la bibliothèque réelle, spec cadre §3.2).

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core/Profiles PhotoApp.Tests/SourceProfilesTests.cs
git commit -m "feat: table des profils de sources et reconnaissance (motif, marque, dossier)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 5 : Décision, textes et arbitre (étape 1 : candidat principal)

**Fichiers :**
- Créer : `PhotoApp.Core/Models/RawMetadata.cs`, `PhotoApp.Core/Normalization/Confidence.cs`, `DecisionFlags.cs`, `DateDecision.cs`, `Reasons.cs`, `DecisionTexts.cs`, `NormalizationOptions.cs`, `DateArbiter.cs`
- Test : `PhotoApp.Tests/DateArbiterTests.cs`, `PhotoApp.Tests/DecisionTextsTests.cs`

**Interfaces :**
- Consomme : `DateCandidate` (tâche 1), `DateComparison` (tâche 2), `NameReading`, `ProfileFamily` (tâche 3), `ProfileMatcher` (tâche 4, dans les tests), `DatePlausibility` (existant).
- Produit :
  - `sealed record RawMetadata { IReadOnlyList<DateCandidate> Dates; string? Make; string? Model; string? Software; string? ContentIdentifier; string? DetectedExtension; string? ReadError; static RawMetadata Empty }` (propriétés `init`) ;
  - `enum Confidence { None = 0, Uncertain = 1, Probable = 2, Certain = 3 }` ;
  - `[Flags] enum DecisionFlags { None = 0, Ambiguous = 1, Disagreement = 2, RealFormat = 4, AssumedZone = 8, Derivative = 16, LivePhoto = 32 }` ;
  - `sealed record DateDecision(DateCandidate? Date, Confidence Confidence, string Reason, IReadOnlyList<DateCandidate> Candidates, IReadOnlyList<DateCandidate> Rejected, DecisionFlags Flags = None)` avec `bool IsApplicable` ;
  - `static class Reasons` (constantes du §5.4 de la spec + `Disagreement = "desaccord"`, `ReadFailed = "lecture-impossible"`) ;
  - `DecisionTexts.ConfidenceLabel(Confidence)`, `ReasonLabel(string)`, `PrecisionLabel(DatePrecision)`, `FlagLabels(DecisionFlags) → IReadOnlyList<string>`, `FormatDate(DateCandidate)`, `FormatOffset(TimeSpan?)`, `FormatCandidate(DateCandidate)` ;
  - `sealed record NormalizationOptions(DateTime UtcNow, TimeZoneInfo LocalZone)` avec `static ForNow()` ;
  - `DateArbiter(NormalizationOptions)` ; `DateDecision Decide(NameReading name, RawMetadata metadata, DateTime lastWriteUtc)`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/DateArbiterTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class DateArbiterTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 19, 12, 0, 0);
    private static readonly DateTime CopiedOn = new(2023, 3, 1, 9, 0, 0);   // date de modification : celle de la copie
    private static readonly DateArbiter Arbiter = new(new NormalizationOptions(UtcNow, TestZones.PlusOne));

    internal static DateCandidate Exif(DateTime value, TimeSpan? offset = null) =>
        new(value, DatePrecision.Second, CandidateOrigin.ExifOriginal, offset);

    internal static DateCandidate Utc(DateTime value, CandidateOrigin origin) =>
        new(value, DatePrecision.Second, origin, isUtc: true);

    internal static DateDecision Decide(string fileName, string? make = null, DateTime? lastWriteUtc = null, params DateCandidate[] dates) =>
        Arbiter.Decide(ProfileMatcher.Read(fileName, make, folderName: null), new RawMetadata { Dates = dates, Make = make }, lastWriteUtc ?? CopiedOn);

    [Fact]
    public void WhatsApp_NameIsTheTruth_EvenWhenTheFileWasCopiedLater()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg");

        Assert.Equal(new DateTime(2021, 9, 15), decision.Date?.WallClock);
        Assert.Equal(DatePrecision.Day, decision.Date?.Precision);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.MessagingName, decision.Reason);
        Assert.True(decision.IsApplicable);
    }

    [Fact]
    public void Messaging_OlderExif_IsTheRealCapture()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg", dates: Exif(new DateTime(2019, 5, 1, 10, 0, 0)));

        Assert.Equal(new DateTime(2019, 5, 1, 10, 0, 0), decision.Date?.WallClock);
        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.MessagingExif, decision.Reason);
    }

    [Fact]
    public void Messaging_SameDayExif_GivesTheExactTime()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg", dates: Exif(new DateTime(2021, 9, 15, 8, 30, 0)));

        Assert.Equal(new DateTime(2021, 9, 15, 8, 30, 0), decision.Date?.WallClock);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.MessagingExif, decision.Reason);
    }

    [Fact]
    public void Screenshot_NameIsTheCaptureTime()
    {
        var decision = Decide("Screenshot 2025-08-27 211650.png");

        Assert.Equal(new DateTime(2025, 8, 27, 21, 16, 50), decision.Date?.WallClock);
        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ScreenshotName, decision.Reason);
    }

    [Fact]
    public void ExifWithOffset_IsCertain()
    {
        var decision = Decide("photo.jpg", dates: Exif(new DateTime(2024, 1, 5, 15, 4, 12), TimeSpan.FromHours(9)));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ExifWithOffset, decision.Reason);
        Assert.Equal(TimeSpan.FromHours(9), decision.Date?.Offset);
    }

    [Fact]
    public void AppleQuickTimeDate_IsCertain()
    {
        var apple = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        var movieHeader = Utc(new DateTime(2024, 7, 11, 15, 7, 10), CandidateOrigin.QuickTimeMovieHeader);

        var decision = Decide("IMG_0477.mov", "Apple", dates: [apple, movieHeader]);

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.QuickTimeApple, decision.Reason);
        Assert.Equal(new DateTime(2024, 7, 11, 17, 7, 10), decision.Date?.WallClock);
    }

    [Fact]
    public void ExifWithoutOffset_ConfirmedByTheProfileName_IsCertain()
    {
        var decision = Decide("IMG_20230304_132015.jpg", "Xiaomi", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ExifConcordant, decision.Reason);
    }

    [Fact]
    public void ExifWithoutOffset_ConfirmedByGps_GetsTheDeducedOffset()
    {
        var gps = Utc(new DateTime(2023, 3, 4, 12, 20, 14), CandidateOrigin.Gps);

        var decision = Decide("001.jpg", dates: [Exif(new DateTime(2023, 3, 4, 13, 20, 15)), gps]);

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.ExifConcordant, decision.Reason);
        Assert.Equal(TimeSpan.FromHours(1), decision.Date?.Offset);
    }

    [Fact]
    public void ExifWithoutOffset_ConfirmedByAUtcPixelName_GetsTheDeducedOffset()
    {
        var decision = Decide("PXL_20230304_122015123.jpg", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(TimeSpan.FromHours(1), decision.Date?.Offset);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), decision.Date?.WallClock);
    }

    [Fact]
    public void ExifAlone_IsProbable()
    {
        var decision = Decide("001.jpg", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.ExifOnly, decision.Reason);
    }

    [Fact]
    public void VideoUtcHeader_AndLocalProfileName_GiveTheWallClockAndTheOffset()
    {
        var decision = Decide("20260917_222944.mp4", dates: Utc(new DateTime(2026, 9, 17, 20, 29, 44), CandidateOrigin.QuickTimeMovieHeader));

        Assert.Equal(Confidence.Certain, decision.Confidence);
        Assert.Equal(Reasons.VideoUtcName, decision.Reason);
        Assert.Equal(new DateTime(2026, 9, 17, 22, 29, 44), decision.Date?.WallClock);
        Assert.Equal(TimeSpan.FromHours(2), decision.Date?.Offset);
    }

    [Fact]
    public void DeviceProfileName_WithoutMetadata_IsProbable()
    {
        var decision = Decide("24-07-11 17-07-10 0477.jpg");

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.ProfileName, decision.Reason);
    }

    [Fact]
    public void XmpAlone_IsProbable()
    {
        var xmp = new DateCandidate(new DateTime(2019, 5, 1, 10, 11, 12), DatePrecision.Second, CandidateOrigin.XmpSidecar);

        var decision = Decide("scan.jpg", dates: xmp);

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.XmpOnly, decision.Reason);
    }

    [Fact]
    public void UtcHeaderAlone_IsProbable_InThePcZone()
    {
        var decision = Decide("clip.mp4", dates: Utc(new DateTime(2024, 1, 5, 14, 0, 0), CandidateOrigin.QuickTimeMovieHeader));

        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.Equal(Reasons.UtcOnly, decision.Reason);
        Assert.Equal(new DateTime(2024, 1, 5, 15, 0, 0), decision.Date?.WallClock);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.AssumedZone));
    }

    [Fact]
    public void EpochName_IsConvertedToThePcZone()
    {
        var decision = Decide("mmexport1700000000000.jpg");

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Equal(new DateTime(2023, 11, 14, 23, 13, 20), decision.Date?.WallClock);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.AssumedZone));
    }

    [Fact]
    public void GenericName_WithTime_IsProbable_DayOnly_IsUncertain()
    {
        Assert.Equal(Reasons.GenericName, Decide("vacances 2019-05-01 10-11-12.jpg").Reason);
        Assert.Equal(Confidence.Probable, Decide("vacances 2019-05-01 10-11-12.jpg").Confidence);
        Assert.Equal(Reasons.GenericNameDay, Decide("scan 20190501.jpg").Reason);
        Assert.Equal(Confidence.Uncertain, Decide("scan 20190501.jpg").Confidence);
    }

    [Fact]
    public void AmbiguousName_IsUncertain_WithoutADate()
    {
        var decision = Decide("03-04-2025 10-00-00.jpg");

        Assert.Equal(Confidence.Uncertain, decision.Confidence);
        Assert.Equal(Reasons.Ambiguous, decision.Reason);
        Assert.Null(decision.Date);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.Ambiguous));
    }

    [Fact]
    public void FileDateOnly_IsUncertain_AndNeverApplicable()
    {
        var decision = Decide("random.jpg");

        Assert.Equal(Confidence.Uncertain, decision.Confidence);
        Assert.Equal(Reasons.FileDateOnly, decision.Reason);
        Assert.Equal(new DateTime(2023, 3, 1, 10, 0, 0), decision.Date?.WallClock);
        Assert.False(decision.IsApplicable);
    }

    [Fact]
    public void NothingPlausible_IsNone()
    {
        var decision = Decide("random.jpg", lastWriteUtc: new DateTime(1980, 1, 1));

        Assert.Equal(Confidence.None, decision.Confidence);
        Assert.Equal(Reasons.Nothing, decision.Reason);
        Assert.Null(decision.Date);
    }

    [Fact]
    public void UnreadableFile_IsNone_EvenWithADatedName()
    {
        var decision = Arbiter.Decide(
            ProfileMatcher.Read("IMG-20210915-WA0005.jpg", null, null), new RawMetadata { ReadError = "boom" }, CopiedOn);

        Assert.Equal(Confidence.None, decision.Confidence);
        Assert.Equal(Reasons.ReadFailed, decision.Reason);
    }

    [Fact]
    public void ImplausibleCandidates_AreRejected_ButListed()
    {
        var resetClock = Exif(new DateTime(2000, 1, 1, 0, 0, 0));

        var decision = Decide("IMG_20230304_132015.jpg", dates: resetClock);

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Contains(resetClock, decision.Rejected);
        Assert.DoesNotContain(resetClock, decision.Candidates);
    }

    [Fact]
    public void CopySuffix_SetsTheDerivativeFlag()
    {
        var decision = Decide("IMG_20230304_132015-min.jpg", dates: Exif(new DateTime(2023, 3, 4, 13, 20, 15)));

        Assert.True(decision.Flags.HasFlag(DecisionFlags.Derivative));
    }
}
```

`PhotoApp.Tests/DecisionTextsTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class DecisionTextsTests
{
    [Theory]
    [InlineData(Confidence.Certain, "Sûre")]
    [InlineData(Confidence.Probable, "Probable")]
    [InlineData(Confidence.Uncertain, "Incertaine")]
    [InlineData(Confidence.None, "Aucune")]
    public void ConfidenceLabel_IsFrench(Confidence confidence, string expected)
    {
        Assert.Equal(expected, DecisionTexts.ConfidenceLabel(confidence));
    }

    [Fact]
    public void ReasonLabel_ExistsForEveryReason_AndFallsBackToTheId()
    {
        var reasons = typeof(Reasons).GetFields().Select(f => (string)f.GetValue(null)!);

        Assert.All(reasons, reason => Assert.NotEqual(reason, DecisionTexts.ReasonLabel(reason)));
        Assert.Equal("inconnue", DecisionTexts.ReasonLabel("inconnue"));
    }

    [Fact]
    public void FormatCandidate_ShowsPrecisionOffsetAndUtc()
    {
        Assert.Equal("2024-01-05", DecisionTexts.FormatCandidate(new DateCandidate(new DateTime(2024, 1, 5), DatePrecision.Day)));
        Assert.Equal("2024-01-05 15:04:12+09:00", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second, offset: TimeSpan.FromHours(9))));
        Assert.Equal("2024-01-05 15:04:12-03:30", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second, offset: TimeSpan.FromHours(-3.5))));
        Assert.Equal("2024-01-05 14:00:00 UTC", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 14, 0, 0), DatePrecision.Second, isUtc: true)));
        Assert.Equal("2024-01-05 15:04:12.123", DecisionTexts.FormatCandidate(
            new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12, 123), DatePrecision.SubSecond)));
    }

    [Fact]
    public void FlagLabels_ListEverySetFlag()
    {
        Assert.Equal(["ambigue", "live-photo"], DecisionTexts.FlagLabels(DecisionFlags.Ambiguous | DecisionFlags.LivePhoto));
        Assert.Empty(DecisionTexts.FlagLabels(DecisionFlags.None));
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `RawMetadata`, `DateArbiter`, `Reasons`… introuvables.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Models/RawMetadata.cs` :

```csharp
namespace PhotoApp.Core.Models;

/// <summary>
/// Métadonnées brutes d'un fichier, lues par PhotoApp.Metadata sans rien décider :
/// les dates sont déjà typées (origine, précision, décalage), le choix appartient au moteur.
/// </summary>
public sealed record RawMetadata
{
    public static RawMetadata Empty { get; } = new();

    public IReadOnlyList<DateCandidate> Dates { get; init; } = [];

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string? Software { get; init; }

    /// <summary>Identifiant Apple qui relie la photo et la vidéo d'une Live Photo.</summary>
    public string? ContentIdentifier { get; init; }

    /// <summary>Extension du vrai format détecté (« jpg », « heic », « mov »…), sans point ; null si inconnu.</summary>
    public string? DetectedExtension { get; init; }

    /// <summary>Message d'erreur quand le fichier est illisible : la décision sera « lecture impossible ».</summary>
    public string? ReadError { get; init; }
}
```

`PhotoApp.Core/Normalization/Confidence.cs` :

```csharp
namespace PhotoApp.Core.Normalization;

/// <summary>Sûre et Probable sont appliquées ; Incertaine part dans « À vérifier » ; Aucune n'invente jamais de date.</summary>
public enum Confidence
{
    None = 0,
    Uncertain = 1,
    Probable = 2,
    Certain = 3
}
```

`PhotoApp.Core/Normalization/DecisionFlags.cs` :

```csharp
namespace PhotoApp.Core.Normalization;

[Flags]
public enum DecisionFlags
{
    None = 0,
    Ambiguous = 1,
    Disagreement = 2,
    RealFormat = 4,
    AssumedZone = 8,
    Derivative = 16,
    LivePhoto = 32
}
```

`PhotoApp.Core/Normalization/DateDecision.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Date retenue, confiance et raison (identifiant de la règle), avec tous les candidats pour le rapport.</summary>
public sealed record DateDecision(
    DateCandidate? Date,
    Confidence Confidence,
    string Reason,
    IReadOnlyList<DateCandidate> Candidates,
    IReadOnlyList<DateCandidate> Rejected,
    DecisionFlags Flags = DecisionFlags.None)
{
    public bool IsApplicable => Date is not null && Confidence >= Confidence.Probable;
}
```

`PhotoApp.Core/Normalization/Reasons.cs` :

```csharp
namespace PhotoApp.Core.Normalization;

/// <summary>Identifiants des règles de décision (spec de phase 1, §5.4), repris tels quels dans le rapport CSV.</summary>
public static class Reasons
{
    public const string MessagingExif = "messagerie-exif";
    public const string MessagingName = "messagerie-nom";
    public const string ScreenshotName = "capture-nom";
    public const string ExifWithOffset = "exif-decalage";
    public const string QuickTimeApple = "quicktime-apple";
    public const string ExifConcordant = "exif-concordant";
    public const string ExifOnly = "exif-seul";
    public const string VideoUtcName = "video-utc-nom";
    public const string ProfileName = "nom-profil";
    public const string XmpOnly = "xmp-seul";
    public const string UtcOnly = "utc-seul";
    public const string GenericName = "nom-generique";
    public const string GenericNameDay = "nom-generique-jour";
    public const string Ambiguous = "ambigue";
    public const string FileDateOnly = "date-fichier-seule";
    public const string Nothing = "aucune";
    public const string Disagreement = "desaccord";
    public const string ReadFailed = "lecture-impossible";
}
```

`PhotoApp.Core/Normalization/DecisionTexts.cs` :

```csharp
using System.Globalization;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Textes affichés par l'interface et le rapport : libellés français, dates au format invariant.</summary>
public static class DecisionTexts
{
    private static readonly Dictionary<string, string> ReasonLabels = new(StringComparer.Ordinal)
    {
        [Reasons.MessagingExif] = "EXIF de la photo d'origine (fichier reçu par messagerie)",
        [Reasons.MessagingName] = "Nom donné par la messagerie (fait foi)",
        [Reasons.ScreenshotName] = "Nom de la capture d'écran",
        [Reasons.ExifWithOffset] = "EXIF avec fuseau",
        [Reasons.QuickTimeApple] = "Date Apple de la vidéo",
        [Reasons.ExifConcordant] = "EXIF confirmé par le nom ou le GPS",
        [Reasons.ExifOnly] = "EXIF sans fuseau",
        [Reasons.VideoUtcName] = "Date de la vidéo confirmée par le nom",
        [Reasons.ProfileName] = "Nom donné par l'appareil",
        [Reasons.XmpOnly] = "Date XMP",
        [Reasons.UtcOnly] = "Date UTC de la vidéo, fuseau du PC supposé",
        [Reasons.GenericName] = "Date trouvée dans le nom",
        [Reasons.GenericNameDay] = "Jour trouvé dans le nom, sans heure",
        [Reasons.Ambiguous] = "Date ambiguë (jour et mois inversables)",
        [Reasons.FileDateOnly] = "Seulement la date du fichier (souvent celle de la copie)",
        [Reasons.Nothing] = "Aucune date trouvée",
        [Reasons.Disagreement] = "Sources en désaccord",
        [Reasons.ReadFailed] = "Fichier illisible"
    };

    public static string ConfidenceLabel(Confidence confidence) => confidence switch
    {
        Confidence.Certain => "Sûre",
        Confidence.Probable => "Probable",
        Confidence.Uncertain => "Incertaine",
        _ => "Aucune"
    };

    public static string ReasonLabel(string reason) => ReasonLabels.GetValueOrDefault(reason, reason);

    public static string PrecisionLabel(DatePrecision precision) => precision switch
    {
        DatePrecision.Day => "jour",
        DatePrecision.Minute => "minute",
        DatePrecision.Second => "seconde",
        _ => "sous-seconde"
    };

    public static IReadOnlyList<string> FlagLabels(DecisionFlags flags)
    {
        var labels = new List<string>();
        Add(DecisionFlags.Ambiguous, "ambigue");
        Add(DecisionFlags.Disagreement, "desaccord");
        Add(DecisionFlags.RealFormat, "format-reel");
        Add(DecisionFlags.AssumedZone, "fuseau-suppose");
        Add(DecisionFlags.Derivative, "derive");
        Add(DecisionFlags.LivePhoto, "live-photo");
        return labels;

        void Add(DecisionFlags flag, string label)
        {
            if (flags.HasFlag(flag))
            {
                labels.Add(label);
            }
        }
    }

    public static string FormatDate(DateCandidate date) => date.WallClock.ToString(
        date.Precision switch
        {
            DatePrecision.Day => "yyyy-MM-dd",
            DatePrecision.Minute => "yyyy-MM-dd HH:mm",
            DatePrecision.Second => "yyyy-MM-dd HH:mm:ss",
            _ => "yyyy-MM-dd HH:mm:ss.fff"
        },
        CultureInfo.InvariantCulture);

    public static string FormatOffset(TimeSpan? offset) => offset is { } value
        ? (value < TimeSpan.Zero ? "-" : "+") + value.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)
        : string.Empty;

    public static string FormatCandidate(DateCandidate candidate) =>
        FormatDate(candidate) + (candidate.IsUtc ? " UTC" : FormatOffset(candidate.Offset));
}
```

`PhotoApp.Core/Normalization/NormalizationOptions.cs` :

```csharp
namespace PhotoApp.Core.Normalization;

/// <param name="UtcNow">Borne haute de plausibilité (aujourd'hui + 1 jour).</param>
/// <param name="LocalZone">Fuseau supposé quand une source ne connaît que l'UTC (vidéo, epoch, date du fichier).</param>
public sealed record NormalizationOptions(DateTime UtcNow, TimeZoneInfo LocalZone)
{
    public static NormalizationOptions ForNow() => new(DateTime.UtcNow, TimeZoneInfo.Local);
}
```

`PhotoApp.Core/Normalization/DateArbiter.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Normalization;

/// <summary>
/// Choisit la date d'un média et sa confiance par des règles nommées (spec de phase 1, §5.4) :
/// étape 1, le candidat principal ; étape 2, sa confrontation aux autres candidats forts.
/// </summary>
public sealed class DateArbiter
{
    private readonly NormalizationOptions _options;

    public DateArbiter(NormalizationOptions options)
    {
        _options = options;
    }

    public DateDecision Decide(NameReading name, RawMetadata metadata, DateTime lastWriteUtc)
    {
        var flags = name.IsDerivative ? DecisionFlags.Derivative : DecisionFlags.None;
        if (name.Ambiguous is not null)
        {
            flags |= DecisionFlags.Ambiguous;
        }

        if (metadata.ReadError is not null)
        {
            return new DateDecision(null, Confidence.None, Reasons.ReadFailed, [], [], flags);
        }

        var all = new List<DateCandidate>(metadata.Dates);
        if (name.Date is not null)
        {
            all.Add(name.Date);
        }

        all.Add(new DateCandidate(lastWriteUtc, DatePrecision.Second, CandidateOrigin.FileModified, isUtc: true));

        var plausible = all.Where(c => DatePlausibility.IsPlausible(c, _options.UtcNow)).ToList();
        var rejected = all.Where(c => !DatePlausibility.IsPlausible(c, _options.UtcNow)).ToList();
        var candidates = new Candidates(plausible, name);

        var choice = SelectPrimary(candidates);

        var date = choice.Date;
        if (date is { IsUtc: true })
        {
            date = date.Localize(_options.LocalZone);
            flags |= DecisionFlags.AssumedZone;
        }

        return new DateDecision(date, choice.Confidence, choice.Reason, plausible, rejected, flags | choice.Flags);
    }

    // Étape 1 : la première règle qui s'applique décide.
    private static Choice SelectPrimary(Candidates c)
    {
        var family = c.Reading.Profile?.Family;
        var exif = c.Exif;
        var profileName = c.ProfileName;

        if (family == ProfileFamily.Messaging && exif is not null && profileName is not null
            && DateComparison.CompareAt(exif, profileName, profileName.Precision) <= 0)
        {
            return FromExif(exif, c, Reasons.MessagingExif);
        }

        if (family == ProfileFamily.Messaging && profileName is not null)
        {
            return new Choice(profileName, Confidence.Certain, Reasons.MessagingName);
        }

        if (family == ProfileFamily.Screenshot && profileName is not null)
        {
            return new Choice(profileName, Confidence.Certain, Reasons.ScreenshotName);
        }

        if (exif is { Offset: not null })
        {
            return new Choice(exif, Confidence.Certain, Reasons.ExifWithOffset);
        }

        if (c.Apple is not null)
        {
            return new Choice(c.Apple, Confidence.Certain, Reasons.QuickTimeApple);
        }

        if (exif is not null)
        {
            return FromExif(exif, c, reason: null);
        }

        if (c.MovieHeader is not null && profileName is not null && DateComparison.ZoneGap(profileName, c.MovieHeader) is { } gap)
        {
            return new Choice(profileName with { Offset = gap }, Confidence.Certain, Reasons.VideoUtcName);
        }

        if (profileName is not null)
        {
            return new Choice(profileName, Confidence.Probable, Reasons.ProfileName);
        }

        if (c.Xmp is not null)
        {
            return new Choice(c.Xmp, Confidence.Probable, Reasons.XmpOnly);
        }

        if (c.MovieHeader is not null)
        {
            return new Choice(c.MovieHeader, Confidence.Probable, Reasons.UtcOnly);
        }

        if (c.Name is not null)
        {
            return c.Name.HasTime
                ? new Choice(c.Name, Confidence.Probable, Reasons.GenericName)
                : new Choice(c.Name, Confidence.Uncertain, Reasons.GenericNameDay);
        }

        if (c.Reading.Ambiguous is not null)
        {
            return new Choice(null, Confidence.Uncertain, Reasons.Ambiguous, DecisionFlags.Ambiguous);
        }

        if (c.FileModified is not null)
        {
            return new Choice(c.FileModified, Confidence.Uncertain, Reasons.FileDateOnly);
        }

        return new Choice(null, Confidence.None, Reasons.Nothing);
    }

    // EXIF sans décalage : Sûr s'il concorde avec le nom d'un profil ou si le GPS en déduit le décalage, Probable sinon.
    private static Choice FromExif(DateCandidate exif, Candidates c, string? reason)
    {
        if (exif.Offset is not null)
        {
            return new Choice(exif, Confidence.Certain, reason ?? Reasons.ExifWithOffset);
        }

        if (c.ProfileName is { } name)
        {
            if (!name.IsUtc && DateComparison.Concordant(exif, name))
            {
                return new Choice(exif, Confidence.Certain, reason ?? Reasons.ExifConcordant);
            }

            if (name.IsUtc && DateComparison.ZoneGap(exif, name) is { } nameGap)
            {
                return new Choice(exif with { Offset = nameGap }, Confidence.Certain, reason ?? Reasons.ExifConcordant);
            }
        }

        if (c.Gps is not null && DateComparison.OffsetFromGps(exif, c.Gps) is { } gpsOffset)
        {
            return new Choice(exif with { Offset = gpsOffset }, Confidence.Certain, reason ?? Reasons.ExifConcordant);
        }

        return new Choice(exif, Confidence.Probable, reason ?? Reasons.ExifOnly);
    }

    private sealed record Choice(DateCandidate? Date, Confidence Confidence, string Reason, DecisionFlags Flags = DecisionFlags.None);

    private sealed record Candidates(IReadOnlyList<DateCandidate> Plausible, NameReading Reading)
    {
        public DateCandidate? Exif => Get(CandidateOrigin.ExifOriginal) ?? Get(CandidateOrigin.ExifDigitized);

        public DateCandidate? Apple => Get(CandidateOrigin.QuickTimeApple);

        public DateCandidate? MovieHeader => Get(CandidateOrigin.QuickTimeMovieHeader);

        public DateCandidate? Xmp => Get(CandidateOrigin.Xmp) ?? Get(CandidateOrigin.XmpSidecar);

        public DateCandidate? Gps => Get(CandidateOrigin.Gps);

        public DateCandidate? Name => Get(CandidateOrigin.FileName);

        /// <summary>Le nom n'est un candidat fort que lu par un motif de profil.</summary>
        public DateCandidate? ProfileName => Reading.FromProfilePattern ? Name : null;

        public DateCandidate? FileModified => Get(CandidateOrigin.FileModified);

        public DateCandidate? Get(CandidateOrigin origin) => Plausible.FirstOrDefault(c => c.Origin == origin);
    }
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~DateArbiterTests|FullyQualifiedName~DecisionTextsTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core PhotoApp.Tests/DateArbiterTests.cs PhotoApp.Tests/DecisionTextsTests.cs
git commit -m "feat: arbitre des dates à règles nommées (candidat principal et confiance)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 6 : Arbitre, étape 2 : confrontation aux autres candidats forts

**Fichiers :**
- Modifier : `PhotoApp.Core/Normalization/DateArbiter.cs`
- Test : `PhotoApp.Tests/DateArbiterConfrontationTests.cs`

**Interfaces :**
- Consomme : `DateArbiter` (tâche 5), `DateComparison` (tâche 2).
- Produit : même API ; une décision dont le candidat principal contredit sans explication un autre candidat fort devient `Uncertain`, raison `Reasons.Disagreement`, drapeau `DecisionFlags.Disagreement`, la date principale restant affichée.

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/DateArbiterConfrontationTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using static PhotoApp.Tests.DateArbiterTests;

namespace PhotoApp.Tests;

public sealed class DateArbiterConfrontationTests
{
    [Fact]
    public void ExifAgainstAnotherDayInXmp_IsAnUnexplainedDisagreement()
    {
        var xmp = new DateCandidate(new DateTime(2022, 6, 1, 10, 0, 0), DatePrecision.Second, CandidateOrigin.Xmp);

        var decision = Decide("photo.jpg", dates: [Exif(new DateTime(2024, 1, 5, 15, 4, 12), TimeSpan.FromHours(9)), xmp]);

        Assert.Equal(Confidence.Uncertain, decision.Confidence);
        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.True(decision.Flags.HasFlag(DecisionFlags.Disagreement));
        Assert.Equal(new DateTime(2024, 1, 5, 15, 4, 12), decision.Date?.WallClock);
        Assert.False(decision.IsApplicable);
    }

    [Fact]
    public void WhatsApp_WithAnExifLaterThanTheName_IsADisagreement()
    {
        var decision = Decide("IMG-20210915-WA0005.jpg", dates: Exif(new DateTime(2022, 1, 1, 10, 0, 0)));

        Assert.Equal(Reasons.Disagreement, decision.Reason);
        Assert.Equal(Confidence.Uncertain, decision.Confidence);
    }

    [Fact]
    public void Download_WithAnOlderExif_IsAnExplainedTransfer()
    {
        var decision = Decide("mmexport1700000000000.jpg", dates: Exif(new DateTime(2020, 1, 1, 10, 0, 0)));

        Assert.Equal(Reasons.ExifOnly, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
        Assert.False(decision.Flags.HasFlag(DecisionFlags.Disagreement));
    }

    [Fact]
    public void DeviceName_FarFromTheExif_IsADisagreement()
    {
        var decision = Decide("IMG_20230304_132015.jpg", dates: Exif(new DateTime(2021, 1, 1, 10, 0, 0)));

        Assert.Equal(Reasons.Disagreement, decision.Reason);
    }

    [Fact]
    public void AgreeingSources_StayCertain()
    {
        var apple = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));
        var movieHeader = Utc(new DateTime(2024, 7, 11, 15, 7, 11), CandidateOrigin.QuickTimeMovieHeader);
        var samsung = Exif(new DateTime(2026, 9, 17, 22, 29, 44), TimeSpan.FromHours(2));

        var video = Decide("24-07-11 17-07-10 0477.mov", dates: [apple, movieHeader]);
        var photo = Decide("20260917_222944.heic", "samsung", dates: samsung);

        Assert.Equal(Confidence.Certain, video.Confidence);
        Assert.Equal(Reasons.QuickTimeApple, video.Reason);
        Assert.Equal(Confidence.Certain, photo.Confidence);
        Assert.Equal(Reasons.ExifWithOffset, photo.Reason);
    }

    [Fact]
    public void GenericName_IsNotStrong_AndCannotDisagree()
    {
        var decision = Decide("vacances 2019-05-01 10-11-12.jpg", dates: Exif(new DateTime(2018, 7, 1, 10, 0, 0)));

        Assert.Equal(Reasons.ExifOnly, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
    }

    [Fact]
    public void FileDate_NeverDisagrees()
    {
        var decision = Decide("IMG_20230304_132015.jpg", lastWriteUtc: new DateTime(2025, 12, 24, 10, 0, 0));

        Assert.Equal(Reasons.ProfileName, decision.Reason);
        Assert.Equal(Confidence.Probable, decision.Confidence);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~DateArbiterConfrontationTests"`
Attendu : `ExifAgainstAnotherDayInXmp…`, `WhatsApp_WithAnExifLaterThanTheName…` et `DeviceName_FarFromTheExif…` échouent (pas encore de confrontation) ; les autres passent.

- [ ] **Étape 3 : Implémenter**

Dans `DateArbiter.Decide`, remplacer :

```csharp
        var choice = SelectPrimary(candidates);
```

par :

```csharp
        var choice = Confront(SelectPrimary(candidates), candidates);
```

Puis ajouter, juste avant `private static Choice FromExif(` :

```csharp
    // Étape 2 : le candidat principal face à chaque autre candidat fort. Un désaccord inexpliqué fait tomber la confiance.
    private static Choice Confront(Choice choice, Candidates c)
    {
        if (choice.Date is null)
        {
            return choice;
        }

        foreach (var other in c.Strong)
        {
            if (other.Origin == choice.Date.Origin || Agrees(choice.Date, other, c.Reading))
            {
                continue;
            }

            return choice with
            {
                Confidence = Confidence.Uncertain,
                Reason = Reasons.Disagreement,
                Flags = choice.Flags | DecisionFlags.Disagreement
            };
        }

        return choice;
    }

    private static bool Agrees(DateCandidate primary, DateCandidate other, NameReading reading)
    {
        if (DateComparison.Concordant(primary, other))
        {
            return true;
        }

        // Une heure locale et une valeur UTC séparées par un fuseau valide.
        if ((!primary.IsUtc && other.IsUtc && DateComparison.ZoneGap(primary, other) is not null)
            || (primary.IsUtc && !other.IsUtc && DateComparison.ZoneGap(other, primary) is not null))
        {
            return true;
        }

        // Transfert : l'EXIF d'un fichier reçu ou téléchargé est plus ancien que son nom.
        return other.Origin == CandidateOrigin.FileName
               && reading.Profile?.Family is ProfileFamily.Messaging or ProfileFamily.Export
               && primary.Origin is CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized
               && DateComparison.CompareAt(primary, other, other.Precision) <= 0;
    }
```

Et, dans le record `Candidates`, ajouter après `FileModified` :

```csharp
        /// <summary>Candidats forts : métadonnées internes, et le nom s'il est lu par un profil. Ni GPS ni date du fichier.</summary>
        public IEnumerable<DateCandidate> Strong => Plausible.Where(candidate => candidate.Origin switch
        {
            CandidateOrigin.ExifOriginal or CandidateOrigin.ExifDigitized or CandidateOrigin.QuickTimeApple
                or CandidateOrigin.QuickTimeMovieHeader or CandidateOrigin.Xmp or CandidateOrigin.XmpSidecar => true,
            CandidateOrigin.FileName => Reading.FromProfilePattern,
            _ => false
        });
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~DateArbiter"`
Attendu : `DateArbiterTests` et `DateArbiterConfrontationTests` réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core/Normalization/DateArbiter.cs PhotoApp.Tests/DateArbiterConfrontationTests.cs
git commit -m "feat: confrontation du candidat principal aux autres candidats forts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 7 : Dates ambiguës JJ/MM : consensus des voisins et autres candidats

**Fichiers :**
- Créer : `PhotoApp.Core/Normalization/AmbiguityResolver.cs`
- Test : `PhotoApp.Tests/AmbiguityResolverTests.cs`

**Interfaces :**
- Consomme : `NameReading`, `AmbiguousName`, `DayMonthEvidence`, `DayMonthOrder` (tâche 3), `ProfileMatcher` (tâche 4, dans les tests).
- Produit : `sealed record AmbiguityInput(string Folder, NameReading Reading, IReadOnlyList<DateCandidate> OtherDates)` ; `AmbiguityResolver.Resolve(IReadOnlyList<AmbiguityInput>) → IReadOnlyList<NameReading>` (même ordre ; une lecture tranchée a `Date` renseignée et `Ambiguous == null`).

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/AmbiguityResolverTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Tests;

public sealed class AmbiguityResolverTests
{
    private static AmbiguityInput Input(string folder, string fileName, params DateCandidate[] others) =>
        new(folder, ProfileMatcher.Read(fileName, null, null), others);

    private static DateCandidate FileDate(int year, int month, int day) =>
        new(new DateTime(year, month, day, 10, 0, 0), DatePrecision.Second, CandidateOrigin.FileModified);

    [Fact]
    public void NeighbourReadBySameMotif_SettlesTheOrder()
    {
        var result = AmbiguityResolver.Resolve(
        [
            Input("/a", "25-12-2024 10-00-00.jpg"),    // 25 > 12 : jour en premier
            Input("/a", "03-04-2025 10-00-00.jpg")
        ]);

        Assert.Equal(new DateTime(2025, 4, 3, 10, 0, 0), result[1].Date?.WallClock);
        Assert.Null(result[1].Ambiguous);
    }

    [Fact]
    public void NeighboursInAnotherFolder_DoNotCount()
    {
        var result = AmbiguityResolver.Resolve([Input("/a", "25-12-2024 10-00-00.jpg"), Input("/b", "03-04-2025 10-00-00.jpg")]);

        Assert.NotNull(result[1].Ambiguous);
        Assert.Null(result[1].Date);
    }

    [Fact]
    public void ConflictingNeighbours_LeaveItAmbiguous()
    {
        var result = AmbiguityResolver.Resolve(
        [
            Input("/a", "25-12-2024 10-00-00.jpg"),    // jour en premier
            Input("/a", "12-25-2024 10-00-00.jpg"),    // mois en premier
            Input("/a", "03-04-2025 10-00-00.jpg")
        ]);

        Assert.NotNull(result[2].Ambiguous);
    }

    [Fact]
    public void AnotherCandidateOnTheSameDay_SettlesIt()
    {
        var result = AmbiguityResolver.Resolve([Input("/a", "03-04-2025 10-00-00.jpg", FileDate(2025, 3, 4))]);

        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0), result[0].Date?.WallClock);
    }

    [Fact]
    public void OtherCandidatesMatchingNeitherReading_LeaveItAmbiguous()
    {
        var result = AmbiguityResolver.Resolve([Input("/a", "03-04-2025 10-00-00.jpg", FileDate(2026, 1, 1))]);

        Assert.NotNull(result[0].Ambiguous);
    }

    [Fact]
    public void UnambiguousReadings_AreUntouched()
    {
        var input = Input("/a", "IMG-20211029-WA0039.jpg");

        Assert.Same(input.Reading, AmbiguityResolver.Resolve([input])[0]);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `AmbiguityResolver` et `AmbiguityInput` introuvables.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Normalization/AmbiguityResolver.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Core.Normalization;

/// <param name="Folder">Dossier du fichier : le consensus ne vaut qu'entre voisins.</param>
/// <param name="OtherDates">Autres candidats du fichier (métadonnées, date du fichier), en heure murale locale.</param>
public sealed record AmbiguityInput(string Folder, NameReading Reading, IReadOnlyList<DateCandidate> OtherDates);

/// <summary>
/// Tranche les dates JJ/MM ambiguës d'un lot (spec de phase 1, §5.5) : étape 3, consensus des voisins lus par le même motif ;
/// étape 4, accord avec un autre candidat du fichier. Les étapes 1 et 2 sont faites à la lecture du nom.
/// </summary>
public static class AmbiguityResolver
{
    public static IReadOnlyList<NameReading> Resolve(IReadOnlyList<AmbiguityInput> inputs)
    {
        var evidence = inputs
            .Where(input => input.Reading.Evidence is not null)
            .GroupBy(input => (input.Folder, input.Reading.Evidence!.PatternKey))
            .ToDictionary(group => group.Key, group => group.Select(input => input.Reading.Evidence!.Order).Distinct().ToList());

        return inputs.Select(input => ResolveOne(input, evidence)).ToList();
    }

    private static NameReading ResolveOne(AmbiguityInput input, Dictionary<(string Folder, string PatternKey), List<DayMonthOrder>> evidence)
    {
        if (input.Reading.Ambiguous is not { } ambiguous)
        {
            return input.Reading;
        }

        if (evidence.TryGetValue((input.Folder, ambiguous.PatternKey), out var orders) && orders.Count == 1)
        {
            return Settle(input.Reading, orders[0] == DayMonthOrder.DayFirst ? ambiguous.DayFirst : ambiguous.MonthFirst);
        }

        var dayFirstMatches = input.OtherDates.Any(date => date.WallClock.Date == ambiguous.DayFirst.WallClock.Date);
        var monthFirstMatches = input.OtherDates.Any(date => date.WallClock.Date == ambiguous.MonthFirst.WallClock.Date);
        if (dayFirstMatches != monthFirstMatches)
        {
            return Settle(input.Reading, dayFirstMatches ? ambiguous.DayFirst : ambiguous.MonthFirst);
        }

        return input.Reading;
    }

    private static NameReading Settle(NameReading reading, DateCandidate date) => reading with { Date = date, Ambiguous = null };
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~AmbiguityResolverTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core/Normalization/AmbiguityResolver.cs PhotoApp.Tests/AmbiguityResolverTests.cs
git commit -m "feat: dates JJ/MM tranchées par les voisins et les autres candidats" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 8 : Nommage proposé (IMG/VID, Live Photo, collisions ordonnées, idempotence)

**Fichiers :**
- Créer : `PhotoApp.Core/Models/MediaKind.cs`, `PhotoApp.Core/Services/SupportedMediaExtensions.cs`, `PhotoApp.Core/Normalization/IContentHasher.cs`, `PhotoApp.Core/Normalization/MediaNamer.cs`
- Modifier : `PhotoApp.Core/Services/NormalizedFileNameBuilder.cs`, `PhotoApp.Core/Services/NameAllocator.cs`
- Test : `PhotoApp.Tests/MediaNamerTests.cs`, `PhotoApp.Tests/NameAllocatorTests.cs`, `PhotoApp.Tests/SupportedMediaExtensionsTests.cs`

**Interfaces :**
- Consomme : `DateCandidate` (tâche 1), `DateDecision`, `Confidence` (tâche 5).
- Produit :
  - `enum MediaKind { Photo, Video }` ;
  - `SupportedMediaExtensions.IsPhoto/IsVideo/IsSidecar/IsSupported(string? extension)`, `SameFormat(string first, string second)` (extensions avec point) ;
  - `NormalizedFileNameBuilder.BaseName(DateCandidate date, string prefix = "IMG")` ;
  - `NameAllocator.Allocate(DateCandidate date, string extension, string prefix = "IMG")`, `NameAllocator.TryReserve(string fileName) → bool` ; l'unicité porte sur le nom de base, sans l'extension ;
  - `interface IContentHasher { string Hash(string fullPath); }` ;
  - `sealed record NamingInput(string FullPath, MediaKind Kind, string TargetExtension, DateDecision Decision, long? Counter, string? ContentIdentifier)` ; `sealed record NamingResult(string? ProposedName, string? LivePhotoPartner)` (`LivePhotoPartner` = nom de fichier actuel du partenaire) ;
  - `MediaNamer(IContentHasher)` ; `IReadOnlyList<NamingResult> Name(IReadOnlyList<NamingInput>)` (même ordre).

- [ ] **Étape 1 : Écrire les tests qui échouent**

Ajouter à `PhotoApp.Tests/NameAllocatorTests.cs` :

```csharp
    [Fact]
    public void Allocate_VideoPrefix()
    {
        Assert.Equal("VID-20240105-150412.mp4", new NameAllocator().Allocate(Shot(), ".mp4", "VID"));
    }

    [Fact]
    public void Allocate_SameSecondWithAnotherExtension_StillGetsASuffix()
    {
        var allocator = new NameAllocator();

        Assert.Equal("IMG-20240105-150412.jpg", allocator.Allocate(Shot(), ".jpg"));
        Assert.Equal("IMG-20240105-150412-01.heic", allocator.Allocate(Shot(), ".heic"));
    }

    [Fact]
    public void TryReserve_TakesTheBaseName_Once()
    {
        var allocator = new NameAllocator();

        Assert.True(allocator.TryReserve("IMG-20240105-150412.heic"));
        Assert.False(allocator.TryReserve("img-20240105-150412.jpg"));
        Assert.Equal("IMG-20240105-150412-01.jpg", allocator.Allocate(Shot(), ".jpg"));
    }
```

`PhotoApp.Tests/SupportedMediaExtensionsTests.cs` :

```csharp
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class SupportedMediaExtensionsTests
{
    [Theory]
    [InlineData(".JPG", true, false)]
    [InlineData(".heic", true, false)]
    [InlineData(".gif", true, false)]
    [InlineData(".MOV", false, true)]
    [InlineData(".mp4", false, true)]
    [InlineData(".xmp", false, false)]
    [InlineData(".txt", false, false)]
    [InlineData(null, false, false)]
    public void Classifies(string? extension, bool photo, bool video)
    {
        Assert.Equal(photo, SupportedMediaExtensions.IsPhoto(extension));
        Assert.Equal(video, SupportedMediaExtensions.IsVideo(extension));
        Assert.Equal(photo || video, SupportedMediaExtensions.IsSupported(extension));
    }

    [Theory]
    [InlineData(".xmp")]
    [InlineData(".AAE")]
    public void Sidecars(string extension)
    {
        Assert.True(SupportedMediaExtensions.IsSidecar(extension));
    }

    [Theory]
    [InlineData(".jpg", ".JPEG", true)]
    [InlineData(".heif", ".heic", true)]
    [InlineData(".tif", ".tiff", true)]
    [InlineData(".m4v", ".mp4", true)]
    [InlineData(".jpg", ".png", false)]
    [InlineData(".mov", ".mp4", false)]
    public void SameFormat(string first, string second, bool expected)
    {
        Assert.Equal(expected, SupportedMediaExtensions.SameFormat(first, second));
    }
}
```

`PhotoApp.Tests/MediaNamerTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class MediaNamerTests
{
    private sealed class FakeHasher(IReadOnlyDictionary<string, string>? hashes = null) : IContentHasher
    {
        public List<string> Calls { get; } = [];

        public string Hash(string fullPath)
        {
            Calls.Add(fullPath);
            return hashes?.GetValueOrDefault(fullPath) ?? fullPath;
        }
    }

    private static NamingInput Input(
        string path,
        DateTime? date,
        DatePrecision precision = DatePrecision.Second,
        MediaKind kind = MediaKind.Photo,
        string? extension = null,
        Confidence confidence = Confidence.Certain,
        long? counter = null,
        string? contentIdentifier = null)
    {
        var candidate = date is null ? null : new DateCandidate(date.Value, precision, CandidateOrigin.ExifOriginal);
        var decision = new DateDecision(candidate, confidence, Reasons.ExifOnly, [], []);
        return new NamingInput(path, kind, extension ?? Path.GetExtension(path).ToLowerInvariant(), decision, counter, contentIdentifier);
    }

    private static readonly DateTime Shot = new(2024, 1, 5, 15, 4, 12);

    private static IReadOnlyList<NamingResult> Name(params NamingInput[] inputs) => new MediaNamer(new FakeHasher()).Name(inputs);

    [Fact]
    public void PhotosAndVideos_GetTheirPrefix()
    {
        var results = Name(Input("/a/x.jpg", Shot), Input("/a/y.mp4", Shot.AddHours(1), kind: MediaKind.Video));

        Assert.Equal("IMG-20240105-150412.jpg", results[0].ProposedName);
        Assert.Equal("VID-20240105-160412.mp4", results[1].ProposedName);
    }

    [Fact]
    public void SameSecond_IsOrderedBySubSeconds()
    {
        var results = Name(Input("/a/late.jpg", Shot.AddMilliseconds(500), DatePrecision.SubSecond), Input("/a/early.jpg", Shot.AddMilliseconds(100), DatePrecision.SubSecond));

        Assert.Equal("IMG-20240105-150412-01.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
    }

    [Fact]
    public void SameDay_IsOrderedByTheNameCounter()
    {
        var day = new DateTime(2021, 9, 15);
        var results = Name(
            Input("/a/IMG-20210915-WA0002.jpg", day, DatePrecision.Day, counter: 2),
            Input("/a/IMG-20210915-WA0001.jpg", day, DatePrecision.Day, counter: 1));

        Assert.Equal("IMG-20210915-000000-02.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20210915-000000-01.jpg", results[1].ProposedName);
    }

    [Fact]
    public void RemainingTies_AreOrderedByContentHash_ComputedOnlyForThem()
    {
        var hasher = new FakeHasher(new Dictionary<string, string> { ["/a/b.jpg"] = "0001", ["/a/a.jpg"] = "0002" });

        var results = new MediaNamer(hasher).Name([Input("/a/a.jpg", Shot), Input("/a/b.jpg", Shot), Input("/a/c.jpg", Shot.AddMinutes(1))]);

        Assert.Equal("IMG-20240105-150412-01.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
        Assert.Equal(["/a/a.jpg", "/a/b.jpg"], hasher.Calls.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OtherFolders_DoNotCollide()
    {
        var results = Name(Input("/a/x.jpg", Shot), Input("/b/x.jpg", Shot));

        Assert.Equal("IMG-20240105-150412.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
    }

    [Fact]
    public void AlreadyNormalizedName_IsKept_AndNeverRenumbered()
    {
        var results = Name(Input("/a/IMG-20240105-150412-01.jpg", Shot), Input("/a/new.jpg", Shot));

        Assert.Equal("IMG-20240105-150412-01.jpg", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.jpg", results[1].ProposedName);
    }

    [Theory]
    [InlineData("/a/IMG-20240105-150413.jpg")]      // autre date
    [InlineData("/a/IMG-20240105-150412.JPG")]      // extension non normalisée
    [InlineData("/a/VID-20240105-150412.jpg")]      // mauvais préfixe
    public void NonConformingNames_AreRenamed(string path)
    {
        Assert.Equal("IMG-20240105-150412.jpg", Name(Input(path, Shot, extension: ".jpg"))[0].ProposedName);
    }

    [Fact]
    public void DayOnlyNameWithoutSuffix_IsRenamed()
    {
        Assert.Equal("IMG-20210915-000000-01.jpg", Name(Input("/a/IMG-20210915-000000.jpg", new DateTime(2021, 9, 15), DatePrecision.Day))[0].ProposedName);
    }

    [Theory]
    [InlineData(Confidence.Uncertain)]
    [InlineData(Confidence.None)]
    public void NotApplicable_GetsNoName(Confidence confidence)
    {
        Assert.Null(Name(Input("/a/x.jpg", Shot, confidence: confidence))[0].ProposedName);
    }

    [Fact]
    public void LivePhoto_ByContentIdentifier_VideoFollowsThePhoto()
    {
        var results = Name(
            Input("/a/IMG_0477.HEIC", Shot, extension: ".heic", contentIdentifier: "ABC"),
            Input("/a/IMG_0478.MOV", Shot.AddSeconds(1), kind: MediaKind.Video, extension: ".mov", contentIdentifier: "ABC"));

        Assert.Equal("IMG-20240105-150412.heic", results[0].ProposedName);
        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
        Assert.Equal("IMG_0478.MOV", results[0].LivePhotoPartner);
        Assert.Equal("IMG_0477.HEIC", results[1].LivePhotoPartner);
    }

    [Fact]
    public void LivePhoto_BySameBaseName()
    {
        var results = Name(
            Input("/a/24-07-11 17-07-10 0477.jpg", Shot),
            Input("/a/24-07-11 17-07-10 0477.mov", Shot, kind: MediaKind.Video));

        Assert.Equal("IMG-20240105-150412.mov", results[1].ProposedName);
    }

    [Fact]
    public void LivePhoto_WithoutAPhotoName_LeavesTheVideoUnnamed()
    {
        var results = Name(
            Input("/a/IMG_0477.jpg", Shot, confidence: Confidence.Uncertain),
            Input("/a/IMG_0477.mov", Shot, kind: MediaKind.Video));

        Assert.Null(results[1].ProposedName);
        Assert.Equal("IMG_0477.jpg", results[1].LivePhotoPartner);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `MediaNamer`, `MediaKind`, `SupportedMediaExtensions`, `TryReserve`… introuvables.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Models/MediaKind.cs` :

```csharp
namespace PhotoApp.Core.Models;

public enum MediaKind
{
    Photo,
    Video
}
```

`PhotoApp.Core/Services/SupportedMediaExtensions.cs` :

```csharp
namespace PhotoApp.Core.Services;

/// <summary>Extensions prises en charge (avec le point), et équivalences d'un même format.</summary>
public static class SupportedMediaExtensions
{
    private static readonly HashSet<string> Photos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".tif", ".tiff", ".avif"
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mov", ".mp4" };

    private static readonly HashSet<string> Sidecars = new(StringComparer.OrdinalIgnoreCase) { ".xmp", ".aae" };

    // MetadataExtractor annonce l'extension la plus courante d'un format : heic pour .heif, tiff pour .tif, mp4 pour .m4v.
    private static readonly string[][] SameFormatGroups =
    [
        [".jpg", ".jpeg", ".jpe"],
        [".heic", ".heif"],
        [".tif", ".tiff"],
        [".mp4", ".m4v"]
    ];

    public static bool IsPhoto(string? extension) => extension is not null && Photos.Contains(extension);

    public static bool IsVideo(string? extension) => extension is not null && Videos.Contains(extension);

    public static bool IsSidecar(string? extension) => extension is not null && Sidecars.Contains(extension);

    public static bool IsSupported(string? extension) => IsPhoto(extension) || IsVideo(extension);

    public static bool SameFormat(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase)
        || SameFormatGroups.Any(group =>
            group.Contains(first, StringComparer.OrdinalIgnoreCase) && group.Contains(second, StringComparer.OrdinalIgnoreCase));
}
```

Dans `PhotoApp.Core/Services/NormalizedFileNameBuilder.cs`, remplacer `BaseName` par :

```csharp
    /// <summary>Nom sans suffixe ni extension. Jour seul : l'heure 000000 sert uniquement au tri (spec §5.5).</summary>
    public static string BaseName(DateCandidate date, string prefix = "IMG") => date.HasTime
        ? prefix + "-" + date.WallClock.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        : prefix + "-" + date.WallClock.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-000000";
```

`PhotoApp.Core/Services/NameAllocator.cs` (réécrit en entier) :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>
/// Attribue des noms uniques par nom de base, sans l'extension : deux fichiers de la même seconde ne partagent jamais
/// un nom de base (seule une Live Photo le fait, hors de cet allocateur). Les noms déjà pris ne sont jamais renumérotés :
/// un nouveau venu reçoit le prochain numéro libre.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _takenStems = new(StringComparer.OrdinalIgnoreCase);

    public NameAllocator(IEnumerable<string>? alreadyTaken = null)
    {
        foreach (var name in alreadyTaken ?? [])
        {
            _takenStems.Add(Path.GetFileNameWithoutExtension(name));
        }
    }

    /// <summary>Réserve un nom existant ; faux si son nom de base est déjà pris.</summary>
    public bool TryReserve(string fileName) => _takenStems.Add(Path.GetFileNameWithoutExtension(fileName));

    public string Allocate(DateCandidate date, string extension, string prefix = "IMG")
    {
        var baseName = NormalizedFileNameBuilder.BaseName(date, prefix);
        var normalizedExtension = NormalizedFileNameBuilder.NormalizeExtension(extension);

        // Jour seul : le suffixe est toujours présent ; sinon le premier de la seconde n'en a pas.
        for (var number = date.HasTime ? 0 : 1; ; number++)
        {
            var stem = number == 0 ? baseName : $"{baseName}-{number:00}";
            if (_takenStems.Add(stem))
            {
                return stem + normalizedExtension;
            }
        }
    }
}
```

`PhotoApp.Core/Normalization/IContentHasher.cs` :

```csharp
namespace PhotoApp.Core.Normalization;

/// <summary>Empreinte du contenu d'un fichier : dernier critère d'ordre des collisions. Implémentée hors de Core (E/S).</summary>
public interface IContentHasher
{
    string Hash(string fullPath);
}
```

`PhotoApp.Core/Normalization/MediaNamer.cs` :

```csharp
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Normalization;

/// <param name="TargetExtension">Extension normalisée du vrai format, avec le point.</param>
/// <param name="Counter">Compteur lu dans le nom (WAnnnn…) : ordre des fichiers d'une même seconde ou d'un même jour.</param>
public sealed record NamingInput(string FullPath, MediaKind Kind, string TargetExtension, DateDecision Decision, long? Counter, string? ContentIdentifier);

/// <param name="LivePhotoPartner">Nom de fichier actuel de l'autre moitié de la Live Photo.</param>
public sealed record NamingResult(string? ProposedName, string? LivePhotoPartner);

/// <summary>
/// Propose un nom par média Sûr ou Probable, dossier par dossier (spec de phase 1, §7). Rien n'est renommé ici.
/// </summary>
public sealed class MediaNamer
{
    private static readonly Regex NormalizedStem = new(
        @"^(?<base>(?:IMG|VID)-\d{8}-\d{6})(?:-(?<number>\d{2}))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IContentHasher _hasher;

    public MediaNamer(IContentHasher hasher)
    {
        _hasher = hasher;
    }

    public IReadOnlyList<NamingResult> Name(IReadOnlyList<NamingInput> inputs)
    {
        var results = Enumerable.Repeat(new NamingResult(null, null), inputs.Count).ToArray();
        var folders = Enumerable.Range(0, inputs.Count)
            .GroupBy(index => Path.GetDirectoryName(inputs[index].FullPath) ?? string.Empty, StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            NameFolder(inputs, folder.ToList(), results);
        }

        return results;
    }

    private void NameFolder(IReadOnlyList<NamingInput> inputs, List<int> indexes, NamingResult[] results)
    {
        var livePairs = PairLivePhotos(inputs, indexes);
        var allocator = new NameAllocator();
        var toAllocate = new List<int>();

        // Les noms déjà conformes sont réservés d'abord : ils ne sont jamais renumérotés.
        foreach (var index in indexes)
        {
            if (livePairs.ContainsKey(index) || inputs[index].Decision is not { IsApplicable: true, Date: { } date })
            {
                continue;
            }

            var current = Path.GetFileName(inputs[index].FullPath);
            if (IsAlreadyNormalized(inputs[index], date) && allocator.TryReserve(current))
            {
                results[index] = new NamingResult(current, null);
            }
            else
            {
                toAllocate.Add(index);
            }
        }

        foreach (var index in InRealOrder(inputs, toAllocate))
        {
            var input = inputs[index];
            results[index] = new NamingResult(allocator.Allocate(input.Decision.Date!, input.TargetExtension, Prefix(input)), null);
        }

        // Live Photo : la vidéo prend le nom de base de sa photo, avec le préfixe IMG.
        foreach (var (video, photo) in livePairs)
        {
            var photoName = results[photo].ProposedName;
            var videoName = photoName is null ? null : Path.GetFileNameWithoutExtension(photoName) + inputs[video].TargetExtension;
            results[video] = new NamingResult(videoName, Path.GetFileName(inputs[photo].FullPath));
            results[photo] = results[photo] with { LivePhotoPartner = Path.GetFileName(inputs[video].FullPath) };
        }
    }

    // Ordre réel : heure (sous-secondes comprises), compteur du nom, puis empreinte du contenu pour les égalités restantes.
    private IEnumerable<int> InRealOrder(IReadOnlyList<NamingInput> inputs, List<int> indexes)
    {
        var groups = indexes
            .GroupBy(index => (inputs[index].Decision.Date!.WallClock, inputs[index].Counter))
            .OrderBy(group => group.Key.WallClock)
            .ThenBy(group => group.Key.Counter ?? long.MaxValue);

        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                yield return members[0];
                continue;
            }

            var hashes = members.ToDictionary(index => index, index => _hasher.Hash(inputs[index].FullPath));
            foreach (var index in members
                         .OrderBy(index => hashes[index], StringComparer.Ordinal)
                         .ThenBy(index => inputs[index].FullPath, StringComparer.Ordinal))
            {
                yield return index;
            }
        }
    }

    private static bool IsAlreadyNormalized(NamingInput input, DateCandidate date)
    {
        var fileName = Path.GetFileName(input.FullPath);
        if (!string.Equals(Path.GetExtension(fileName), input.TargetExtension, StringComparison.Ordinal))
        {
            return false;
        }

        var match = NormalizedStem.Match(Path.GetFileNameWithoutExtension(fileName));
        if (!match.Success || (!date.HasTime && !match.Groups["number"].Success))
        {
            return false;
        }

        return match.Groups["base"].Value == NormalizedFileNameBuilder.BaseName(date, Prefix(input));
    }

    // Vidéo → photo, par ContentIdentifier Apple, sinon par le même nom de base, dans le même dossier.
    private static Dictionary<int, int> PairLivePhotos(IReadOnlyList<NamingInput> inputs, List<int> indexes)
    {
        var pairs = new Dictionary<int, int>();
        var photos = indexes.Where(index => inputs[index].Kind == MediaKind.Photo).ToList();
        var videos = indexes.Where(index => inputs[index].Kind == MediaKind.Video && inputs[index].TargetExtension == ".mov").ToList();
        var paired = new HashSet<int>();

        foreach (var video in videos)
        {
            var identifier = inputs[video].ContentIdentifier;
            var photo = identifier is null
                ? -1
                : photos.FirstOrDefault(p => !paired.Contains(p) && inputs[p].ContentIdentifier == identifier, -1);
            if (photo >= 0)
            {
                pairs[video] = photo;
                paired.Add(photo);
            }
        }

        foreach (var video in videos.Where(v => !pairs.ContainsKey(v)))
        {
            var stem = Path.GetFileNameWithoutExtension(inputs[video].FullPath);
            var photo = photos.FirstOrDefault(
                p => !paired.Contains(p) && string.Equals(Path.GetFileNameWithoutExtension(inputs[p].FullPath), stem, StringComparison.OrdinalIgnoreCase),
                -1);
            if (photo >= 0)
            {
                pairs[video] = photo;
                paired.Add(photo);
            }
        }

        return pairs;
    }

    private static string Prefix(NamingInput input) => input.Kind == MediaKind.Video ? "VID" : "IMG";
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : 0 échec (les tests existants de `RenamePlanner` et `ConversionService` restent verts : la sortie est en `.jpg` uniquement).

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core PhotoApp.Tests
git commit -m "feat: nommage proposé (IMG/VID, Live Photo, collisions ordonnées, idempotence)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 9 : Le moteur de normalisation (lot complet)

**Fichiers :**
- Créer : `PhotoApp.Core/Models/ScannedMedia.cs`, `PhotoApp.Core/Normalization/MediaFacts.cs`, `PhotoApp.Core/Normalization/MediaResult.cs`, `PhotoApp.Core/Normalization/NormalizationEngine.cs`
- Test : `PhotoApp.Tests/NormalizationEngineTests.cs`

**Interfaces :**
- Consomme : tâches 1 à 8.
- Produit :
  - `sealed record ScannedMedia(string FullPath, long SizeBytes, DateTime LastWriteUtc, IReadOnlyList<string> Sidecars)` avec `FileName`, `Extension` ;
  - `sealed record MediaFacts(ScannedMedia File, RawMetadata Metadata)` ;
  - `sealed record MediaResult(MediaFacts Facts, MediaKind Kind, string TargetExtension, NameReading Reading, DateDecision Decision, string? ProposedName, string? LivePhotoPartner)` avec `SourceProfile? Profile` ;
  - `NormalizationEngine(NormalizationOptions options, IContentHasher hasher)` ; `IReadOnlyList<MediaResult> Normalize(IReadOnlyList<MediaFacts> batch)` (trié par chemin, ordinal ; indépendant de l'ordre d'entrée).

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/NormalizationEngineTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class NormalizationEngineTests
{
    private sealed class PathHasher : IContentHasher
    {
        public string Hash(string fullPath) => fullPath;
    }

    private static readonly DateTime CopiedOn = new(2025, 6, 1, 10, 0, 0);

    internal static NormalizationEngine Engine() =>
        new(new NormalizationOptions(new DateTime(2026, 10, 19, 12, 0, 0), TestZones.PlusOne), new PathHasher());

    internal static MediaFacts Facts(string path, RawMetadata? metadata = null) =>
        new(new ScannedMedia(path, 100, CopiedOn, []), metadata ?? RawMetadata.Empty);

    [Fact]
    public void WhatsAppDay_IsNumberedByTheCounter_RegardlessOfInputOrder()
    {
        var batch = new[]
        {
            Facts("/p/IMG-20210915-WA0010.jpg"),
            Facts("/p/IMG-20210915-WA0002.jpg"),
            Facts("/p/IMG-20210915-WA0005.jpg")
        };

        var forward = Engine().Normalize(batch);
        var backward = Engine().Normalize(batch.Reverse().ToList());

        Assert.Equal(
            ["IMG-20210915-000000-01.jpg", "IMG-20210915-000000-02.jpg", "IMG-20210915-000000-03.jpg"],
            forward.Select(r => r.ProposedName));
        Assert.Equal(["/p/IMG-20210915-WA0002.jpg", "/p/IMG-20210915-WA0005.jpg", "/p/IMG-20210915-WA0010.jpg"], forward.Select(r => r.Facts.File.FullPath));
        Assert.Equal(forward.Select(r => r.ProposedName), backward.Select(r => r.ProposedName));
        Assert.All(forward, r => Assert.Equal("whatsapp", r.Profile?.Id));
    }

    [Fact]
    public void LivePhoto_IsFlaggedOnBothHalves_AndTheVideoKeepsTheImgPrefix()
    {
        var photoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(2));
        var videoDate = new DateCandidate(new DateTime(2024, 7, 11, 17, 7, 10), DatePrecision.Second, CandidateOrigin.QuickTimeApple, TimeSpan.FromHours(2));

        var results = Engine().Normalize(
        [
            Facts("/p/IMG_0477.HEIC", new RawMetadata { Dates = [photoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "heic" }),
            Facts("/p/IMG_0477.MOV", new RawMetadata { Dates = [videoDate], Make = "Apple", ContentIdentifier = "ID-1", DetectedExtension = "mov" })
        ]);

        Assert.Equal(["IMG-20240711-170710.heic", "IMG-20240711-170710.mov"], results.Select(r => r.ProposedName));
        Assert.All(results, r => Assert.True(r.Decision.Flags.HasFlag(DecisionFlags.LivePhoto)));
        Assert.Equal([MediaKind.Photo, MediaKind.Video], results.Select(r => r.Kind));
    }

    [Fact]
    public void RealFormat_DifferentFromTheExtension_IsFlagged_AndUsedForTheName()
    {
        var exif = new DateCandidate(new DateTime(2024, 1, 5, 15, 4, 12), DatePrecision.Second, CandidateOrigin.ExifOriginal, TimeSpan.FromHours(1));

        var result = Engine().Normalize([Facts("/p/photo.jpg", new RawMetadata { Dates = [exif], DetectedExtension = "png" })]).Single();

        Assert.Equal(".png", result.TargetExtension);
        Assert.True(result.Decision.Flags.HasFlag(DecisionFlags.RealFormat));
        Assert.Equal("IMG-20240105-150412.png", result.ProposedName);
    }

    [Theory]
    [InlineData("/p/photo.heif", "heic", ".heif")]
    [InlineData("/p/photo.JPEG", "jpg", ".jpg")]
    [InlineData("/p/photo.tif", "tiff", ".tif")]
    public void SameFormatUnderAnotherExtension_IsNotFlagged(string path, string detected, string target)
    {
        var result = Engine().Normalize([Facts(path, new RawMetadata { DetectedExtension = detected })]).Single();

        Assert.Equal(target, result.TargetExtension);
        Assert.False(result.Decision.Flags.HasFlag(DecisionFlags.RealFormat));
    }

    [Fact]
    public void AmbiguousNames_AreSettledAcrossTheBatch()
    {
        var results = Engine().Normalize([Facts("/p/25-12-2024 10-00-00.jpg"), Facts("/p/03-04-2025 10-00-00.jpg")]);

        var settled = results.Single(r => r.Facts.File.FileName.StartsWith("03"));
        Assert.Equal(new DateTime(2025, 4, 3, 10, 0, 0), settled.Decision.Date?.WallClock);
        Assert.Equal(Reasons.GenericName, settled.Decision.Reason);
    }

    [Fact]
    public void FileDate_CanSettleAnAmbiguousName()
    {
        // Date de modification 2025-03-04 09:00 UTC = 10:00 à UTC+1 : la lecture MM-JJ l'emporte.
        var facts = new MediaFacts(new ScannedMedia("/p/03-04-2025 10-00-00.jpg", 100, new DateTime(2025, 3, 4, 9, 0, 0), []), RawMetadata.Empty);

        var result = Engine().Normalize([facts]).Single();

        Assert.Equal(new DateTime(2025, 3, 4, 10, 0, 0), result.Decision.Date?.WallClock);
    }

    [Fact]
    public void Video_GetsTheVidPrefix()
    {
        var header = new DateCandidate(new DateTime(2026, 9, 17, 20, 29, 44), DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true);

        var result = Engine().Normalize([Facts("/p/20260917_222944.mp4", new RawMetadata { Dates = [header], DetectedExtension = "mp4" })]).Single();

        Assert.Equal(MediaKind.Video, result.Kind);
        Assert.Equal("VID-20260917-222944.mp4", result.ProposedName);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `NormalizationEngine`, `ScannedMedia`, `MediaFacts` introuvables.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Core/Models/ScannedMedia.cs` :

```csharp
namespace PhotoApp.Core.Models;

/// <summary>Un média trouvé par le scan, avec ses sidecars (.xmp, .aae) du même nom de base.</summary>
public sealed record ScannedMedia(string FullPath, long SizeBytes, DateTime LastWriteUtc, IReadOnlyList<string> Sidecars)
{
    public string FileName => Path.GetFileName(FullPath);

    public string Extension => Path.GetExtension(FullPath);
}
```

`PhotoApp.Core/Normalization/MediaFacts.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Normalization;

/// <summary>Ce que le moteur sait d'un fichier : le scan et les métadonnées brutes.</summary>
public sealed record MediaFacts(ScannedMedia File, RawMetadata Metadata);
```

`PhotoApp.Core/Normalization/MediaResult.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;

namespace PhotoApp.Core.Normalization;

/// <param name="TargetExtension">Extension normalisée du vrai format, avec le point.</param>
/// <param name="ProposedName">Nom proposé (Sûre ou Probable), sinon null. Rien n'est renommé en phase 1.</param>
public sealed record MediaResult(
    MediaFacts Facts,
    MediaKind Kind,
    string TargetExtension,
    NameReading Reading,
    DateDecision Decision,
    string? ProposedName,
    string? LivePhotoPartner)
{
    public SourceProfile? Profile => Reading.Profile;
}
```

`PhotoApp.Core/Normalization/NormalizationEngine.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Profiles;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Normalization;

/// <summary>
/// Point d'entrée du moteur (spec de phase 1, §4) : profils et noms, ambiguïtés du lot, décisions, vrai format, noms proposés.
/// Pur et déterministe : un même lot donne toujours le même résultat, quel que soit l'ordre d'entrée.
/// </summary>
public sealed class NormalizationEngine
{
    private readonly NormalizationOptions _options;
    private readonly DateArbiter _arbiter;
    private readonly MediaNamer _namer;

    public NormalizationEngine(NormalizationOptions options, IContentHasher hasher)
    {
        _options = options;
        _arbiter = new DateArbiter(options);
        _namer = new MediaNamer(hasher);
    }

    public IReadOnlyList<MediaResult> Normalize(IReadOnlyList<MediaFacts> batch)
    {
        var facts = batch.OrderBy(f => f.File.FullPath, StringComparer.Ordinal).ToList();

        var readings = AmbiguityResolver.Resolve(facts
            .Select(f => new AmbiguityInput(
                FolderOf(f.File.FullPath),
                ProfileMatcher.Read(f.File.FileName, f.Metadata.Make, Path.GetFileName(FolderOf(f.File.FullPath))),
                LocalDates(f)))
            .ToList());

        var targets = facts.Select(TargetExtension).ToList();
        var kinds = targets.Select(extension => SupportedMediaExtensions.IsVideo(extension) ? MediaKind.Video : MediaKind.Photo).ToList();
        var decisions = facts
            .Select((f, i) =>
            {
                var decision = _arbiter.Decide(readings[i], f.Metadata, f.File.LastWriteUtc);
                return SupportedMediaExtensions.SameFormat(targets[i], f.File.Extension)
                    ? decision
                    : decision with { Flags = decision.Flags | DecisionFlags.RealFormat };
            })
            .ToList();

        var names = _namer.Name(facts
            .Select((f, i) => new NamingInput(f.File.FullPath, kinds[i], targets[i], decisions[i], readings[i].Counter, f.Metadata.ContentIdentifier))
            .ToList());

        return facts
            .Select((f, i) =>
            {
                var decision = names[i].LivePhotoPartner is null
                    ? decisions[i]
                    : decisions[i] with { Flags = decisions[i].Flags | DecisionFlags.LivePhoto };
                return new MediaResult(f, kinds[i], targets[i], readings[i], decision, names[i].ProposedName, names[i].LivePhotoPartner);
            })
            .ToList();
    }

    // Autres candidats du fichier en heure murale locale, pour trancher une date JJ/MM (§5.5, étape 4).
    private IReadOnlyList<DateCandidate> LocalDates(MediaFacts facts) =>
        facts.Metadata.Dates
            .Append(new DateCandidate(facts.File.LastWriteUtc, DatePrecision.Second, CandidateOrigin.FileModified, isUtc: true))
            .Select(date => date.Localize(_options.LocalZone))
            .ToList();

    // L'extension du vrai format, sauf si l'extension actuelle désigne déjà ce format (.heif / heic, .tif / tiff…).
    private static string TargetExtension(MediaFacts facts)
    {
        var own = NormalizedFileNameBuilder.NormalizeExtension(facts.File.Extension);
        if (facts.Metadata.DetectedExtension is not { Length: > 0 } detected)
        {
            return own;
        }

        var real = NormalizedFileNameBuilder.NormalizeExtension(detected);
        return SupportedMediaExtensions.SameFormat(own, real) ? own : real;
    }

    private static string FolderOf(string path) => Path.GetDirectoryName(path) ?? string.Empty;
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~NormalizationEngineTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core PhotoApp.Tests/NormalizationEngineTests.cs
git commit -m "feat: moteur de normalisation sur un lot (profils, ambiguïtés, décisions, noms)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 10 : Lecteur des clés Apple QuickTime et conteneurs vidéo de test

**Fichiers :**
- Créer : `PhotoApp.Metadata/QuickTimeKeysReader.cs`, `PhotoApp.Tests/TestVideos.cs`
- Test : `PhotoApp.Tests/QuickTimeKeysReaderTests.cs`

**Interfaces :**
- Produit : `QuickTimeKeysReader.Read(string path) → IReadOnlyDictionary<string, string>` et `Read(Stream)` (jamais d'exception : vide si absent ou mal formé) ; constantes `CreationDateKey`, `ContentIdentifierKey`, `MakeKey`, `ModelKey`, `SoftwareKey` ; aide de test `TestVideos.Write(string path, DateTime movieHeaderUtc, IReadOnlyDictionary<string, string>? appleKeys = null, string brand = "qt  ", bool isoMeta = false, int mediaDataBytes = 0)`.

> Pourquoi ce lecteur : MetadataExtractor convertit `com.apple.quicktime.creationdate` dans le fuseau du PC et perd le décalage d'origine (vérifié : `2024-07-11T17:07:10+0900` est rendu `10:07:10` « Local » sur un PC à UTC+2). Seule la chaîne brute garde l'heure murale du lieu de prise de vue.

- [ ] **Étape 1 : Écrire l'aide de test et les tests qui échouent**

`PhotoApp.Tests/TestVideos.cs` :

```csharp
using System.Buffers.Binary;
using System.Text;

namespace PhotoApp.Tests;

/// <summary>
/// Écrit de petits conteneurs MOV/MP4 que MetadataExtractor sait lire : ftyp, mdat facultatif, puis moov avec mvhd
/// et, en option, les clés Apple (meta : hdlr + keys + ilst). Aucune donnée privée.
/// </summary>
public static class TestVideos
{
    private static readonly DateTime QuickTimeEpoch = new(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Write(
        string path,
        DateTime movieHeaderUtc,
        IReadOnlyDictionary<string, string>? appleKeys = null,
        string brand = "qt  ",
        bool isoMeta = false,
        int mediaDataBytes = 0)
    {
        var ftyp = Box("ftyp", Ascii(brand), U32(0), Ascii(brand));
        var mdat = mediaDataBytes > 0 ? Box("mdat", new byte[mediaDataBytes]) : [];
        var moovChildren = new List<byte[]> { MovieHeader(movieHeaderUtc) };
        if (appleKeys is { Count: > 0 })
        {
            moovChildren.Add(AppleMeta(appleKeys, isoMeta));
        }

        File.WriteAllBytes(path, [.. ftyp, .. mdat, .. Box("moov", [.. moovChildren])]);
    }

    private static byte[] MovieHeader(DateTime utc)
    {
        var seconds = (uint)(DateTime.SpecifyKind(utc, DateTimeKind.Utc) - QuickTimeEpoch).TotalSeconds;
        var matrix = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(16), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(matrix.AsSpan(32), 0x40000000);
        // version/drapeaux, création, modification, échelle, durée, vitesse, volume, réservé, matrice, prévisualisation…, piste suivante
        return Box("mvhd", U32(0), U32(seconds), U32(seconds), U32(600), U32(0), U32(0x00010000), U16(0x0100), new byte[10], matrix, new byte[24], U32(2));
    }

    private static byte[] AppleMeta(IReadOnlyDictionary<string, string> keys, bool isoMeta)
    {
        var items = keys.ToList();
        var hdlr = Box("hdlr", U32(0), U32(0), Ascii("mdta"), new byte[12], new byte[1]);
        var keyEntries = items.Select(item =>
        {
            var name = Encoding.UTF8.GetBytes(item.Key);
            byte[] entry = [.. U32((uint)(8 + name.Length)), .. Ascii("mdta"), .. name];
            return entry;
        });
        var keysBox = Box("keys", [U32(0), U32((uint)items.Count), .. keyEntries]);
        var entries = items.Select((item, index) =>
            Box("\0\0\0" + (char)(index + 1), Box("data", U32(1), U32(0), Encoding.UTF8.GetBytes(item.Value))));
        var ilst = Box("ilst", [.. entries]);

        // QuickTime : « meta » contient directement ses boîtes ; ISO (MP4) : 4 octets de version et drapeaux d'abord.
        return isoMeta ? Box("meta", U32(0), hdlr, keysBox, ilst) : Box("meta", hdlr, keysBox, ilst);
    }

    private static byte[] Box(string type, params byte[][] content)
    {
        var body = content.SelectMany(part => part).ToArray();
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] U16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
```

`PhotoApp.Tests/QuickTimeKeysReaderTests.cs` :

```csharp
namespace PhotoApp.Tests;

public sealed class QuickTimeKeysReaderTests
{
    private static readonly DateTime Header = new(2024, 7, 11, 8, 7, 10);

    private static readonly Dictionary<string, string> AppleKeys = new()
    {
        [QuickTimeKeysReader.CreationDateKey] = "2024-07-11T17:07:10+0900",
        [QuickTimeKeysReader.ContentIdentifierKey] = "ID-1",
        [QuickTimeKeysReader.MakeKey] = "Apple"
    };

    [Fact]
    public void Read_ReturnsTheAppleKeysAsWritten()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys);

        var keys = QuickTimeKeysReader.Read(path);

        Assert.Equal("2024-07-11T17:07:10+0900", keys[QuickTimeKeysReader.CreationDateKey]);
        Assert.Equal("ID-1", keys[QuickTimeKeysReader.ContentIdentifierKey]);
        Assert.Equal("Apple", keys[QuickTimeKeysReader.MakeKey]);
    }

    [Fact]
    public void Read_IsoMetaFullBox_IsSupported()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mp4");
        TestVideos.Write(path, Header, AppleKeys, brand: "isom", isoMeta: true);

        Assert.Equal("ID-1", QuickTimeKeysReader.Read(path)[QuickTimeKeysReader.ContentIdentifierKey]);
    }

    [Fact]
    public void Read_FindsMoovAfterTheMediaData()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys, mediaDataBytes: 4096);

        Assert.Equal("Apple", QuickTimeKeysReader.Read(path)[QuickTimeKeysReader.MakeKey]);
    }

    [Fact]
    public void Read_WithoutAppleKeys_IsEmpty()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mp4");
        TestVideos.Write(path, Header, brand: "isom");

        Assert.Empty(QuickTimeKeysReader.Read(path));
    }

    [Fact]
    public void Read_NotAQuickTimeFile_IsEmpty()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.png");
        TestImages.WritePng(path);

        Assert.Empty(QuickTimeKeysReader.Read(path));
    }

    [Fact]
    public void Read_TruncatedFile_IsEmpty_WithoutThrowing()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("clip.mov");
        TestVideos.Write(path, Header, AppleKeys);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length - 20)]);

        Assert.Empty(QuickTimeKeysReader.Read(path));
    }

    [Fact]
    public void Read_MissingFile_IsEmpty()
    {
        Assert.Empty(QuickTimeKeysReader.Read(Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid().ToString("N") + ".mov")));
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `QuickTimeKeysReader` introuvable.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Metadata/QuickTimeKeysReader.cs` :

```csharp
using System.Buffers.Binary;
using System.Text;

namespace PhotoApp.Metadata;

/// <summary>
/// Lit les clés Apple d'un MOV/MP4 (moov/meta : keys + ilst) telles qu'écrites.
/// MetadataExtractor convertit com.apple.quicktime.creationdate dans le fuseau du PC et perd le décalage d'origine.
/// </summary>
public static class QuickTimeKeysReader
{
    public const string CreationDateKey = "com.apple.quicktime.creationdate";
    public const string ContentIdentifierKey = "com.apple.quicktime.content.identifier";
    public const string MakeKey = "com.apple.quicktime.make";
    public const string ModelKey = "com.apple.quicktime.model";
    public const string SoftwareKey = "com.apple.quicktime.software";

    private const uint Utf8Type = 1;
    private const long MaxListBytes = 1 << 20;   // keys et ilst font quelques Ko en pratique

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public static IReadOnlyDictionary<string, string> Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static IReadOnlyDictionary<string, string> Read(Stream stream)
    {
        try
        {
            var moov = FindBox(stream, 0, stream.Length, "moov");
            var meta = moov is null ? null : FindBox(stream, moov.Value.Start, moov.Value.End, "meta");
            if (meta is null)
            {
                return Empty;
            }

            // QuickTime : « meta » contient directement ses boîtes ; ISO (MP4) : c'est une FullBox (4 octets de version et drapeaux).
            var children = meta.Value.Start;
            if (!IsBoxAt(stream, children, meta.Value.End))
            {
                children += 4;
            }

            var keys = FindBox(stream, children, meta.Value.End, "keys");
            var ilst = FindBox(stream, children, meta.Value.End, "ilst");
            if (keys is null || ilst is null)
            {
                return Empty;
            }

            return ReadValues(ReadBytes(stream, ilst.Value), ReadKeyNames(ReadBytes(stream, keys.Value)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return Empty;
        }
    }

    /// <summary>Contenu d'une boîte, en-tête exclu : [Start, End[.</summary>
    private readonly record struct Box(long Start, long End);

    private static Box? FindBox(Stream stream, long from, long to, string type)
    {
        var header = new byte[16];
        var position = from;
        while (position + 8 <= to)
        {
            stream.Position = position;
            stream.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var name = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8L;

            if (size == 1)
            {
                stream.ReadExactly(header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = to - position;   // jusqu'à la fin du parent
            }

            if (size < headerSize || position + size > to)
            {
                return null;
            }

            if (name == type)
            {
                return new Box(position + headerSize, position + size);
            }

            position += size;
        }

        return null;
    }

    // Une boîte plausible commence ici : taille cohérente et type en lettres ASCII.
    private static bool IsBoxAt(Stream stream, long position, long end)
    {
        if (position + 8 > end)
        {
            return false;
        }

        var header = new byte[8];
        stream.Position = position;
        stream.ReadExactly(header, 0, 8);
        var size = BinaryPrimitives.ReadUInt32BigEndian(header);
        return size >= 8 && position + size <= end && header.AsSpan(4, 4).ToArray().All(b => b is >= 0x20 and < 0x7F);
    }

    private static byte[] ReadBytes(Stream stream, Box box)
    {
        var length = box.End - box.Start;
        if (length > MaxListBytes)
        {
            throw new InvalidDataException("Liste de métadonnées QuickTime trop grande.");
        }

        var bytes = new byte[length];
        stream.Position = box.Start;
        stream.ReadExactly(bytes);
        return bytes;
    }

    // keys : version et drapeaux (4), nombre de clés (4), puis pour chaque clé : taille (4), espace de noms (4), nom.
    private static List<string> ReadKeyNames(byte[] bytes)
    {
        var names = new List<string>();
        if (bytes.Length < 8)
        {
            return names;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
        var position = 8;
        for (var i = 0; i < count && position + 8 <= bytes.Length; i++)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            if (size < 8 || position + size > bytes.Length)
            {
                break;
            }

            names.Add(Encoding.UTF8.GetString(bytes, position + 8, size - 8));
            position += size;
        }

        return names;
    }

    // ilst : une boîte par valeur, dont le type est l'index de la clé (à partir de 1), contenant une boîte « data » :
    // type (4, 1 = UTF-8), locale (4), valeur.
    private static Dictionary<string, string> ReadValues(byte[] bytes, List<string> names)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        while (position + 8 <= bytes.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            if (size < 8 || position + size > bytes.Length)
            {
                break;
            }

            var index = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 4));
            var data = position + 8;
            if (index >= 1 && index <= names.Count && data + 16 <= position + size
                && Encoding.ASCII.GetString(bytes, data + 4, 4) == "data")
            {
                var dataSize = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(data));
                var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(data + 8)) & 0x00FFFFFF;
                if (type == Utf8Type && dataSize >= 16 && data + dataSize <= position + size)
                {
                    values[names[index - 1]] = Encoding.UTF8.GetString(bytes, data + 16, dataSize - 16);
                }
            }

            position += size;
        }

        return values;
    }
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~QuickTimeKeysReaderTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Metadata/QuickTimeKeysReader.cs PhotoApp.Tests/TestVideos.cs PhotoApp.Tests/QuickTimeKeysReaderTests.cs
git commit -m "feat: lecture des clés Apple QuickTime telles qu'écrites (décalage conservé)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 11 : `MetadataReader` (métadonnées brutes typées) et empreinte SHA-256

**Fichiers :**
- Créer : `PhotoApp.Metadata/MetadataReader.cs`, `PhotoApp.Metadata/Sha256ContentHasher.cs`
- Modifier : `PhotoApp.Tests/TestImages.cs` (ajout de `WriteJpegWithExif`)
- Test : `PhotoApp.Tests/MetadataReaderTests.cs`

**Interfaces :**
- Consomme : `RawMetadata` (tâche 5), `XmpDateParser`, `SubSeconds`, `UtcOffsetParser` (tâche 1), `QuickTimeKeysReader` (tâche 10), `IContentHasher` (tâche 8).
- Produit : `MetadataReader.Read(string path, IReadOnlyList<string>? sidecars = null) → RawMetadata` (sans exception : `ReadError` renseigné si le fichier est illisible) ; `Sha256ContentHasher : IContentHasher` ; `TestImages.WriteJpegWithExif(string path, Action<ExifProfile> configure)`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

Ajouter à `PhotoApp.Tests/TestImages.cs` :

```csharp
    public static void WriteJpegWithExif(string path, Action<ExifProfile> configure)
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 8);
        var exif = new ExifProfile();
        configure(exif);
        image.SetProfile(exif);
        image.Write(path, MagickFormat.Jpeg);
    }
```

`PhotoApp.Tests/MetadataReaderTests.cs` :

```csharp
using ImageMagick;
using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class MetadataReaderTests
{
    private static readonly MetadataReader Reader = new();

    [Fact]
    public void ExifOriginal_KeepsWallClock_SubSecondsAndOffset()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            exif.SetValue(ExifTag.SubsecTimeOriginal, "123");
            exif.SetValue(ExifTag.OffsetTimeOriginal, "+09:00");
        });

        var date = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.ExifOriginal);

        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15, 123), date.WallClock);
        Assert.Equal(DatePrecision.SubSecond, date.Precision);
        Assert.Equal(TimeSpan.FromHours(9), date.Offset);
    }

    [Fact]
    public void ExifWithoutOffset_IsFloating()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15");

        var date = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.ExifOriginal);

        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), date.WallClock);
        Assert.Equal(DatePrecision.Second, date.Precision);
        Assert.Null(date.Offset);
        Assert.False(date.IsUtc);
    }

    [Fact]
    public void ExifDigitized_MakeModelAndSoftware_AreRead()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.DateTimeDigitized, "2023:03:04 13:20:16");
            exif.SetValue(ExifTag.Make, "Xiaomi");
            exif.SetValue(ExifTag.Model, "M2012K11AG");
            exif.SetValue(ExifTag.Software, "MIUI");
        });

        var metadata = Reader.Read(path);

        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 16), metadata.Dates.Single(d => d.Origin == CandidateOrigin.ExifDigitized).WallClock);
        Assert.Equal("Xiaomi", metadata.Make);
        Assert.Equal("M2012K11AG", metadata.Model);
        Assert.Equal("MIUI", metadata.Software);
        Assert.Equal("jpg", metadata.DetectedExtension);
    }

    [Fact]
    public void Gps_IsUtc()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpegWithExif(path, exif =>
        {
            exif.SetValue(ExifTag.GPSDateStamp, "2023:03:04");
            exif.SetValue(ExifTag.GPSTimestamp, [new Rational(12, 1), new Rational(20, 1), new Rational(14, 1)]);
        });

        var gps = Reader.Read(path).Dates.Single(d => d.Origin == CandidateOrigin.Gps);

        Assert.True(gps.IsUtc);
        Assert.Equal(new DateTime(2023, 3, 4, 12, 20, 14), gps.WallClock);
    }

    [Fact]
    public void XmpSidecar_IsRead()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("scan.png");
        var sidecar = temp.Combine("scan.xmp");
        TestImages.WritePng(path);
        File.WriteAllText(sidecar, """
            <x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
            <rdf:Description rdf:about="" xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/" photoshop:DateCreated="2019-05-01T10:11:12+02:00"/>
            </rdf:RDF></x:xmpmeta>
            """);

        var date = Reader.Read(path, [sidecar]).Dates.Single(d => d.Origin == CandidateOrigin.XmpSidecar);

        Assert.Equal(new DateTime(2019, 5, 1, 10, 11, 12), date.WallClock);
        Assert.Equal(TimeSpan.FromHours(2), date.Offset);
    }

    [Fact]
    public void MalformedSidecar_IsIgnored()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("scan.png");
        var sidecar = temp.Combine("scan.xmp");
        TestImages.WritePng(path);
        File.WriteAllText(sidecar, "pas du XML");

        var metadata = Reader.Read(path, [sidecar]);

        Assert.Null(metadata.ReadError);
        Assert.DoesNotContain(metadata.Dates, d => d.Origin == CandidateOrigin.XmpSidecar);
    }

    [Fact]
    public void DetectedExtension_RevealsTheRealFormat()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WritePng(path);

        Assert.Equal("png", Reader.Read(path).DetectedExtension);
    }

    [Fact]
    public void AppleMov_GivesTheAppleDateWithItsOffset_AndTheUtcHeader()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("IMG_0477.MOV");
        TestVideos.Write(path, new DateTime(2024, 7, 11, 8, 7, 10), new Dictionary<string, string>
        {
            [QuickTimeKeysReader.CreationDateKey] = "2024-07-11T17:07:10+0900",
            [QuickTimeKeysReader.ContentIdentifierKey] = "ID-1",
            [QuickTimeKeysReader.MakeKey] = "Apple"
        });

        var metadata = Reader.Read(path);

        var apple = metadata.Dates.Single(d => d.Origin == CandidateOrigin.QuickTimeApple);
        var header = metadata.Dates.Single(d => d.Origin == CandidateOrigin.QuickTimeMovieHeader);
        Assert.Equal(new DateTime(2024, 7, 11, 17, 7, 10), apple.WallClock);
        Assert.Equal(TimeSpan.FromHours(9), apple.Offset);
        Assert.True(header.IsUtc);
        Assert.Equal(new DateTime(2024, 7, 11, 8, 7, 10), header.WallClock);
        Assert.Equal("ID-1", metadata.ContentIdentifier);
        Assert.Equal("Apple", metadata.Make);
        Assert.Equal("mov", metadata.DetectedExtension);
    }

    [Fact]
    public void Mp4_GivesTheUtcHeaderOnly()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("20260917_222944.mp4");
        TestVideos.Write(path, new DateTime(2026, 9, 17, 20, 29, 44), brand: "isom");

        var metadata = Reader.Read(path);

        Assert.Equal(new DateTime(2026, 9, 17, 20, 29, 44), metadata.Dates.Single().WallClock);
        Assert.Equal(CandidateOrigin.QuickTimeMovieHeader, metadata.Dates.Single().Origin);
        Assert.Equal("mp4", metadata.DetectedExtension);
    }

    [Fact]
    public void UnreadableFile_ReportsAnError()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("IMG_20240101_101010.png");
        File.WriteAllBytes(path, [1, 2, 3]);

        var metadata = Reader.Read(path);

        Assert.NotNull(metadata.ReadError);
        Assert.Empty(metadata.Dates);
    }

    [Fact]
    public void PngWithoutMetadata_HasNoDates()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.png");
        TestImages.WritePng(path);

        Assert.Empty(Reader.Read(path).Dates);
    }

    [Fact]
    public void Sha256ContentHasher_DependsOnTheContentOnly()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("a.bin"), [1, 2, 3]);
        File.WriteAllBytes(temp.Combine("b.bin"), [1, 2, 3]);
        File.WriteAllBytes(temp.Combine("c.bin"), [4, 5, 6]);
        var hasher = new Sha256ContentHasher();

        Assert.Equal(64, hasher.Hash(temp.Combine("a.bin")).Length);
        Assert.Equal(hasher.Hash(temp.Combine("a.bin")), hasher.Hash(temp.Combine("b.bin")));
        Assert.NotEqual(hasher.Hash(temp.Combine("a.bin")), hasher.Hash(temp.Combine("c.bin")));
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `MetadataReader` et `Sha256ContentHasher` introuvables.

- [ ] **Étape 3 : Implémenter**

`PhotoApp.Metadata/MetadataReader.cs` :

```csharp
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor.Formats.Xmp;
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Metadata;

/// <summary>
/// Lit les métadonnées brutes d'un média (EXIF, GPS, XMP et sidecar .xmp, QuickTime, vrai format) sans rien décider :
/// le choix de la date appartient au moteur de PhotoApp.Core.
/// </summary>
public sealed class MetadataReader
{
    // OffsetTimeOriginal / OffsetTimeDigitized (EXIF 2.31), lus en texte : "+09:00".
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagOffsetTimeDigitized = 0x9012;

    // Dates de prise de vue uniquement : xmp:ModifyDate et xmp:MetadataDate sont des dates d'édition.
    private static readonly string[] XmpDateKeys = ["exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate"];

    private static readonly DateTime QuickTimeEpoch = new(1904, 1, 1);

    public RawMetadata Read(string path, IReadOnlyList<string>? sidecars = null)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception ex)
        {
            // Format inconnu, fichier tronqué ou verrouillé : la décision sera « lecture impossible ».
            return new RawMetadata { ReadError = ex.Message };
        }

        var detected = Clean(directories.OfType<FileTypeDirectory>().FirstOrDefault()?.GetString(FileTypeDirectory.TagExpectedFileNameExtension))
            ?.ToLowerInvariant();
        IReadOnlyDictionary<string, string> apple = detected is "mov" or "mp4"
            ? QuickTimeKeysReader.Read(path)
            : new Dictionary<string, string>();

        var dates = new List<DateCandidate>();
        var exif = directories.OfType<ExifSubIfdDirectory>().ToList();
        AddExifDate(exif, ExifDirectoryBase.TagDateTimeOriginal, TagOffsetTimeOriginal, ExifDirectoryBase.TagSubsecondTimeOriginal, CandidateOrigin.ExifOriginal, dates);
        AddExifDate(exif, ExifDirectoryBase.TagDateTimeDigitized, TagOffsetTimeDigitized, ExifDirectoryBase.TagSubsecondTimeDigitized, CandidateOrigin.ExifDigitized, dates);
        AddGps(directories, dates);
        AddXmp(directories, dates);
        AddQuickTime(directories, apple, dates);
        foreach (var sidecar in sidecars ?? [])
        {
            if (string.Equals(Path.GetExtension(sidecar), ".xmp", StringComparison.OrdinalIgnoreCase))
            {
                AddXmpSidecar(sidecar, dates);
            }
        }

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        return new RawMetadata
        {
            Dates = dates,
            Make = Clean(ifd0?.GetString(ExifDirectoryBase.TagMake)) ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.MakeKey)),
            Model = Clean(ifd0?.GetString(ExifDirectoryBase.TagModel)) ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.ModelKey)),
            Software = Clean(ifd0?.GetString(ExifDirectoryBase.TagSoftware)) ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.SoftwareKey)),
            ContentIdentifier = Clean(directories.OfType<AppleMakernoteDirectory>().FirstOrDefault()?.GetString(AppleMakernoteDirectory.TagContentIdentifier))
                                ?? Clean(apple.GetValueOrDefault(QuickTimeKeysReader.ContentIdentifierKey)),
            DetectedExtension = detected
        };
    }

    private static void AddExifDate(
        List<ExifSubIfdDirectory> directories, int dateTag, int offsetTag, int subSecondTag, CandidateOrigin origin, List<DateCandidate> dates)
    {
        foreach (var directory in directories)
        {
            if (!directory.TryGetDateTime(dateTag, out var value))
            {
                continue;
            }

            // MetadataExtractor ne reporte pas les sous-secondes dans la date : elles ont leur propre balise.
            var wallClock = new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond);
            var precision = DatePrecision.Second;
            if (SubSeconds.ToMilliseconds(directory.GetString(subSecondTag)?.Trim()) is { } milliseconds)
            {
                wallClock = wallClock.AddMilliseconds(milliseconds);
                precision = DatePrecision.SubSecond;
            }

            TimeSpan? offset = UtcOffsetParser.TryParse(directory.GetString(offsetTag), out var parsed) ? parsed : null;
            dates.Add(new DateCandidate(wallClock, precision, origin, offset));
            return;
        }
    }

    private static void AddGps(IEnumerable<MetadataExtractor.Directory> directories, List<DateCandidate> dates)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps is not null && gps.TryGetGpsDate(out var utc))
        {
            dates.Add(new DateCandidate(utc, DatePrecision.Second, CandidateOrigin.Gps, isUtc: true));
        }
    }

    private static void AddXmp(IEnumerable<MetadataExtractor.Directory> directories, List<DateCandidate> dates)
    {
        foreach (var xmp in directories.OfType<XmpDirectory>())
        {
            if (FirstXmpDate(xmp, CandidateOrigin.Xmp) is { } date)
            {
                dates.Add(date);
                return;
            }
        }
    }

    private static void AddXmpSidecar(string path, List<DateCandidate> dates)
    {
        try
        {
            var xmp = new XmpReader().Extract(File.ReadAllBytes(path));
            if (FirstXmpDate(xmp, CandidateOrigin.XmpSidecar) is { } date)
            {
                dates.Add(date);
            }
        }
        catch (Exception)
        {
            // Sidecar illisible ou mal formé : ignoré, les autres sources restent valables (spec de phase 1, §11).
        }
    }

    private static DateCandidate? FirstXmpDate(XmpDirectory xmp, CandidateOrigin origin)
    {
        var properties = xmp.GetXmpProperties();
        foreach (var key in XmpDateKeys)
        {
            if (properties.TryGetValue(key, out var value) && XmpDateParser.Parse(value, origin) is { } date)
            {
                return date;
            }
        }

        return null;
    }

    private static void AddQuickTime(
        IEnumerable<MetadataExtractor.Directory> directories, IReadOnlyDictionary<string, string> apple, List<DateCandidate> dates)
    {
        if (apple.TryGetValue(QuickTimeKeysReader.CreationDateKey, out var creation)
            && XmpDateParser.Parse(creation, CandidateOrigin.QuickTimeApple) is { } appleDate)
        {
            dates.Add(appleDate);
        }

        // mvhd : secondes depuis 1904, en UTC ; 0 (non renseigné) donne 1904-01-01.
        var header = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
        if (header is not null && header.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var created) && created > QuickTimeEpoch)
        {
            dates.Add(new DateCandidate(created, DatePrecision.Second, CandidateOrigin.QuickTimeMovieHeader, isUtc: true));
        }
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim().TrimEnd('\0').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
```

`PhotoApp.Metadata/Sha256ContentHasher.cs` :

```csharp
using System.Security.Cryptography;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Metadata;

/// <summary>Empreinte SHA-256 du contenu : dernier critère d'ordre des collisions de noms.</summary>
public sealed class Sha256ContentHasher : IContentHasher
{
    public string Hash(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fichier devenu illisible : l'ordre retombe sur le chemin, l'analyse continue.
            return string.Empty;
        }
    }
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~MetadataReaderTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Metadata PhotoApp.Tests/TestImages.cs PhotoApp.Tests/MetadataReaderTests.cs
git commit -m "feat: lecteur de métadonnées brutes (EXIF, GPS, XMP, sidecar, QuickTime, vrai format)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 12 : `MediaScanner` : photos, vidéos, sidecars et statistiques d'exclusion

**Fichiers :**
- Renommer : `PhotoApp.Catalog/ImageScanner.cs` → `PhotoApp.Catalog/MediaScanner.cs` ; `PhotoApp.Tests/ImageScannerTests.cs` → `PhotoApp.Tests/MediaScannerTests.cs`
- Créer : `PhotoApp.Catalog/ScanResult.cs`
- Supprimer : `PhotoApp.Core/Services/SupportedImageExtensions.cs`
- Modifier : `PhotoApp/ViewModels/MainWindowViewModel.cs`, `PhotoApp.Tests/ConversionServiceTests.cs` (appel du scanner)

**Interfaces :**
- Consomme : `ScannedMedia` (tâche 9), `SupportedMediaExtensions` (tâche 8).
- Produit : `MediaScanner.Scan(string rootPath, ScanOptions? options = null) → ScanResult` ; `sealed record ScanResult(IReadOnlyList<ScannedMedia> Media, ScanStatistics Statistics)` ; `sealed record ScanStatistics(int HiddenFiles, int HiddenFolders, int EmptyFiles, int InaccessibleFolders)` avec `None` et `Add(ScanStatistics)`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

```bash
git mv PhotoApp.Tests/ImageScannerTests.cs PhotoApp.Tests/MediaScannerTests.cs
```

Remplacer tout le contenu de `PhotoApp.Tests/MediaScannerTests.cs` par :

```csharp
namespace PhotoApp.Tests;

public sealed class MediaScannerTests
{
    private static string[] Names(ScanResult result) =>
        result.Media.Select(m => m.FileName).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void Scan_ReturnsPhotosAndVideos_FromNestedDirectories()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("nested"));
        File.WriteAllBytes(temp.Combine("photo1.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("nested", "photo2.png"), [1]);
        File.WriteAllBytes(temp.Combine("nested", "photo3.heic"), [1]);
        File.WriteAllBytes(temp.Combine("nested", "clip.MOV"), [1]);
        File.WriteAllBytes(temp.Combine("video.mp4"), [1]);
        File.WriteAllText(temp.Combine("notes.txt"), "pas un média");

        var result = new MediaScanner().Scan(temp.FullPath);

        Assert.Equal(["clip.MOV", "photo1.jpg", "photo2.png", "photo3.heic", "video.mp4"], Names(result));
    }

    [Fact]
    public void Scan_RecordsSizeAndUtcModificationDate()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        File.WriteAllBytes(path, [1, 2, 3]);
        var written = new DateTime(2023, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, written);

        var media = new MediaScanner().Scan(temp.FullPath).Media.Single();

        Assert.Equal(3, media.SizeBytes);
        Assert.Equal(written, media.LastWriteUtc);
    }

    [Fact]
    public void Scan_AttachesSidecarsWithTheSameBaseName()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("IMG_0477.HEIC"), [1]);
        File.WriteAllBytes(temp.Combine("IMG_0477.MOV"), [1]);
        File.WriteAllText(temp.Combine("IMG_0477.AAE"), "aae");
        File.WriteAllBytes(temp.Combine("scan.jpg"), [1]);
        File.WriteAllText(temp.Combine("scan.jpg.xmp"), "xmp");
        File.WriteAllText(temp.Combine("orphan.xmp"), "xmp");

        var media = new MediaScanner().Scan(temp.FullPath).Media.ToDictionary(m => m.FileName);

        Assert.Equal(["IMG_0477.AAE"], media["IMG_0477.HEIC"].Sidecars.Select(s => Path.GetFileName(s)));
        Assert.Equal(["IMG_0477.AAE"], media["IMG_0477.MOV"].Sidecars.Select(s => Path.GetFileName(s)));
        Assert.Equal(["scan.jpg.xmp"], media["scan.jpg"].Sidecars.Select(s => Path.GetFileName(s)));
        Assert.Equal(3, media.Count);
    }

    [Fact]
    public void Scan_SkipsExcludedFolderAndItsDescendants()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("Converted", "sub"));
        File.WriteAllBytes(temp.Combine("keep.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "out.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "sub", "deep.jpg"), [1]);

        var result = new MediaScanner().Scan(temp.FullPath, new ScanOptions([temp.Combine("Converted")]));

        Assert.Equal(["keep.jpg"], Names(result));
    }

    [Fact]
    public void Scan_CountsHiddenAndEmptyEntries()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine(".gs"));
        File.WriteAllBytes(temp.Combine(".gs", "cache.jpg"), [1]);
        File.WriteAllBytes(temp.Combine(".work-abc.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("empty.jpg"), []);
        File.WriteAllBytes(temp.Combine("ok.jpg"), [1]);

        var result = new MediaScanner().Scan(temp.FullPath);

        Assert.Equal(["ok.jpg"], Names(result));
        Assert.Equal(new ScanStatistics(HiddenFiles: 1, HiddenFolders: 1, EmptyFiles: 1, InaccessibleFolders: 0), result.Statistics);
    }

    [Fact]
    public void Scan_MissingRoot_IsEmpty()
    {
        var result = new MediaScanner().Scan(Path.Combine(Path.GetTempPath(), "absent-" + Guid.NewGuid().ToString("N")));

        Assert.Empty(result.Media);
    }

    [Fact]
    public void ScanStatistics_Add()
    {
        Assert.Equal(new ScanStatistics(2, 4, 6, 8), new ScanStatistics(1, 2, 3, 4).Add(new ScanStatistics(1, 2, 3, 4)));
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `MediaScanner`, `ScanResult`, `ScanStatistics` introuvables.

- [ ] **Étape 3 : Implémenter**

```bash
git mv PhotoApp.Catalog/ImageScanner.cs PhotoApp.Catalog/MediaScanner.cs
git rm PhotoApp.Core/Services/SupportedImageExtensions.cs
```

`PhotoApp.Catalog/ScanResult.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Catalog;

/// <summary>Ce que le scan a écarté, pour le résumé : fichiers et dossiers cachés, fichiers vides, dossiers inaccessibles.</summary>
public sealed record ScanStatistics(int HiddenFiles, int HiddenFolders, int EmptyFiles, int InaccessibleFolders)
{
    public static ScanStatistics None { get; } = new(0, 0, 0, 0);

    public ScanStatistics Add(ScanStatistics other) => new(
        HiddenFiles + other.HiddenFiles,
        HiddenFolders + other.HiddenFolders,
        EmptyFiles + other.EmptyFiles,
        InaccessibleFolders + other.InaccessibleFolders);
}

public sealed record ScanResult(IReadOnlyList<ScannedMedia> Media, ScanStatistics Statistics);
```

`PhotoApp.Catalog/MediaScanner.cs` (réécrit en entier) :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Catalog;

/// <summary>
/// Parcourt un dossier récursivement : photos, vidéos et leurs sidecars (.xmp, .aae) du même nom de base.
/// Écarte les dossiers exclus, les fichiers et dossiers cachés et les fichiers vides, et les compte.
/// </summary>
public sealed class MediaScanner
{
    public ScanResult Scan(string rootPath, ScanOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path must not be empty.", nameof(rootPath));
        }

        var media = new List<ScannedMedia>();
        if (!Directory.Exists(rootPath))
        {
            return new ScanResult(media, ScanStatistics.None);
        }

        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var excluded = new HashSet<string>((options?.ExcludedFolders ?? []).Select(NormalizeFolder), comparer);
        int hiddenFiles = 0, hiddenFolders = 0, emptyFiles = 0, inaccessibleFolders = 0;

        var directoriesToScan = new Stack<string>();
        directoriesToScan.Push(rootPath);

        while (directoriesToScan.Count > 0)
        {
            var currentDirectory = directoriesToScan.Pop();
            var subDirectories = ListSafe(() => Directory.EnumerateDirectories(currentDirectory));
            var files = ListSafe(() => Directory.EnumerateFiles(currentDirectory));
            if (subDirectories is null || files is null)
            {
                inaccessibleFolders++;
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                if (excluded.Contains(NormalizeFolder(subDirectory)))
                {
                    continue;
                }

                if (IsHidden(subDirectory))
                {
                    hiddenFolders++;
                    continue;
                }

                directoriesToScan.Push(subDirectory);
            }

            var sidecars = files.Where(file => SupportedMediaExtensions.IsSidecar(Path.GetExtension(file)) && !IsHidden(file)).ToList();
            foreach (var file in files)
            {
                if (!SupportedMediaExtensions.IsSupported(Path.GetExtension(file)))
                {
                    continue;
                }

                if (IsHidden(file))
                {
                    hiddenFiles++;
                    continue;
                }

                var info = TryGetInfo(file);
                if (info is null)
                {
                    continue;
                }

                if (info.Length == 0)
                {
                    emptyFiles++;
                    continue;
                }

                media.Add(new ScannedMedia(info.FullName, info.Length, info.LastWriteTimeUtc, SidecarsOf(info.FullName, sidecars)));
            }
        }

        return new ScanResult(media, new ScanStatistics(hiddenFiles, hiddenFolders, emptyFiles, inaccessibleFolders));
    }

    // photo.xmp ou photo.jpg.xmp (Lightroom, darktable) ; IMG_0477.AAE vaut pour la photo et la vidéo d'une Live Photo.
    private static IReadOnlyList<string> SidecarsOf(string mediaPath, List<string> sidecars)
    {
        var stem = Path.GetFileNameWithoutExtension(mediaPath);
        var name = Path.GetFileName(mediaPath);
        return sidecars
            .Where(sidecar =>
            {
                var sidecarStem = Path.GetFileNameWithoutExtension(sidecar);
                return string.Equals(sidecarStem, stem, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(sidecarStem, name, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static string NormalizeFolder(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // Dossiers et fichiers cachés : caches d'applications (.gs), fichiers de travail (.work-…), corbeilles système.
    private static bool IsHidden(string path)
    {
        if (Path.GetFileName(path).StartsWith('.'))
        {
            return true;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // L'énumération est matérialisée ici : une erreur d'accès survient pendant le parcours, pas à l'appel.
    private static List<string>? ListSafe(Func<IEnumerable<string>> enumerate)
    {
        try
        {
            return enumerate().ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }

    private static FileInfo? TryGetInfo(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            _ = info.Length;   // force la lecture : une erreur d'accès est levée ici
            return info;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }
}
```

Dans `PhotoApp/ViewModels/MainWindowViewModel.cs` : ajouter `using PhotoApp.Core.Services;`, remplacer le champ `private readonly ImageScanner _imageScanner = new();` par `private readonly MediaScanner _mediaScanner = new();`, et remplacer la méthode `ScanAndResolve` entière par :

```csharp
    private List<ImageFileInfo> ScanAndResolve(string root)
    {
        var outputFolder = Path.Combine(root, OutputFolderName);

        // L'interface ne traite que les photos en phase 1 ; les vidéos arrivent en phase 2.
        var scanned = _mediaScanner.Scan(root, new ScanOptions([outputFolder])).Media
            .Where(media => SupportedMediaExtensions.IsPhoto(media.Extension))
            .ToList();
        var resolved = new ImageFileInfo[scanned.Count];

        Parallel.For(0, scanned.Count, index =>
        {
            var media = scanned[index];
            resolved[index] = new ImageFileInfo(
                media.FullPath,
                media.FileName,
                media.Extension,
                media.SizeBytes,
                _imageDateResolver.Resolve(media.FullPath));
        });

        return resolved.OrderBy(image => image.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }
```

Dans `PhotoApp.Tests/ConversionServiceTests.cs`, remplacer le helper `ScanAndResolve` par :

```csharp
    private static IReadOnlyList<ImageFileInfo> ScanAndResolve(string root, string outputFolder)
    {
        var resolver = new ImageDateResolver();
        return new MediaScanner()
            .Scan(root, new ScanOptions([outputFolder])).Media
            .Select(m => new ImageFileInfo(m.FullPath, m.FileName, m.Extension, m.SizeBytes, resolver.Resolve(m.FullPath)))
            .ToList();
    }
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet build PhotoVisualizer.slnx && dotnet test PhotoVisualizer.slnx`
Attendu : build sans erreur (interface comprise), 0 échec. `grep -rn "ImageScanner\|SupportedImageExtensions" --include=*.cs . | grep -v '/bin/\|/obj/'` ne renvoie rien.

- [ ] **Étape 5 : Commiter**

```bash
git add -A
git commit -m "feat: MediaScanner (photos, vidéos, sidecars, statistiques d'exclusion)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 13 : `MediaAnalyzer` : scan, lecture parallèle, moteur

**Fichiers :**
- Modifier : `PhotoApp.Catalog/PhotoApp.Catalog.csproj` (référence `PhotoApp.Metadata`)
- Créer : `PhotoApp.Catalog/MediaAnalyzer.cs`
- Test : `PhotoApp.Tests/MediaAnalyzerTests.cs`

**Interfaces :**
- Consomme : `MediaScanner`, `ScanStatistics` (tâche 12), `MetadataReader`, `Sha256ContentHasher` (tâche 11), `NormalizationEngine`, `MediaResult`, `NormalizationOptions` (tâches 5 et 9).
- Produit : `readonly record struct AnalysisProgress(int Done, int Total)` ; `sealed record AnalysisResult(IReadOnlyList<MediaResult> Results, ScanStatistics Statistics, TimeSpan Duration)` ; `MediaAnalyzer.Analyze(IReadOnlyList<string> roots, IReadOnlyCollection<string> excludedFolders, IProgress<AnalysisProgress>? progress = null, NormalizationOptions? options = null) → AnalysisResult`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

`PhotoApp.Tests/MediaAnalyzerTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class MediaAnalyzerTests
{
    private sealed class Collector : IProgress<AnalysisProgress>
    {
        private readonly object _gate = new();

        public List<AnalysisProgress> Reports { get; } = [];

        public void Report(AnalysisProgress value)
        {
            lock (_gate)
            {
                Reports.Add(value);
            }
        }
    }

    private static readonly NormalizationOptions Options = new(new DateTime(2026, 10, 19, 12, 0, 0), TestZones.PlusOne);

    [Fact]
    public void Analyze_DecidesEveryMedia_AndCountsWhatWasSkipped()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        Directory.CreateDirectory(Path.Combine(root, ".cache"));
        Directory.CreateDirectory(Path.Combine(root, "Converted"));
        TestImages.WritePng(Path.Combine(root, "IMG-20210915-WA0005.png"));
        TestImages.WritePng(Path.Combine(root, "Screenshot_20240410_111652.png"));
        TestImages.WriteJpeg(Path.Combine(root, "holiday.jpg"), "2023:03:04 13:20:15");
        TestVideos.Write(Path.Combine(root, "IMG_0477.MOV"), new DateTime(2024, 7, 11, 15, 7, 10), new Dictionary<string, string>
        {
            [QuickTimeKeysReader.CreationDateKey] = "2024-07-11T17:07:10+0200",
            [QuickTimeKeysReader.MakeKey] = "Apple"
        });
        TestImages.WritePng(Path.Combine(root, ".cache", "thumb.png"));
        TestImages.WritePng(Path.Combine(root, "Converted", "IMG-20210915-000000-01.jpg"));
        File.WriteAllBytes(Path.Combine(root, "empty.jpg"), []);
        var progress = new Collector();

        var analysis = new MediaAnalyzer().Analyze([root], [Path.Combine(root, "Converted")], progress, Options);

        var byName = analysis.Results.ToDictionary(r => r.Facts.File.FileName);
        Assert.Equal(4, byName.Count);
        Assert.Equal(Reasons.MessagingName, byName["IMG-20210915-WA0005.png"].Decision.Reason);
        Assert.Equal(Reasons.ScreenshotName, byName["Screenshot_20240410_111652.png"].Decision.Reason);
        Assert.Equal(Reasons.ExifOnly, byName["holiday.jpg"].Decision.Reason);
        Assert.Equal(Reasons.QuickTimeApple, byName["IMG_0477.MOV"].Decision.Reason);
        Assert.Equal(MediaKind.Video, byName["IMG_0477.MOV"].Kind);
        Assert.Equal("VID-20240711-170710.mov", byName["IMG_0477.MOV"].ProposedName);
        Assert.Equal(1, analysis.Statistics.HiddenFolders);
        Assert.Equal(1, analysis.Statistics.EmptyFiles);
        Assert.Equal(4, progress.Reports.Count);
        Assert.Contains(new AnalysisProgress(4, 4), progress.Reports);
    }

    [Fact]
    public void Analyze_OverlappingRoots_DoNotDuplicateFiles()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("sub"));
        TestImages.WritePng(temp.Combine("sub", "IMG_20230304_132015.png"));

        var analysis = new MediaAnalyzer().Analyze([temp.FullPath, temp.Combine("sub")], [], options: Options);

        Assert.Single(analysis.Results);
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `MediaAnalyzer`, `AnalysisProgress` introuvables.

- [ ] **Étape 3 : Implémenter**

Dans `PhotoApp.Catalog/PhotoApp.Catalog.csproj`, ajouter dans l'`ItemGroup` des références de projets :

```xml
    <ProjectReference Include="..\PhotoApp.Metadata\PhotoApp.Metadata.csproj" />
```

`PhotoApp.Catalog/MediaAnalyzer.cs` :

```csharp
using System.Diagnostics;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;
using PhotoApp.Metadata;

namespace PhotoApp.Catalog;

public readonly record struct AnalysisProgress(int Done, int Total);

public sealed record AnalysisResult(IReadOnlyList<MediaResult> Results, ScanStatistics Statistics, TimeSpan Duration);

/// <summary>
/// L'analyse complète, sans aucune écriture : scan des dossiers, lecture des métadonnées en parallèle, puis moteur.
/// Partagée par la CLI et l'interface.
/// </summary>
public sealed class MediaAnalyzer
{
    private readonly MediaScanner _scanner = new();
    private readonly MetadataReader _reader = new();

    public AnalysisResult Analyze(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> excludedFolders,
        IProgress<AnalysisProgress>? progress = null,
        NormalizationOptions? options = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var media = new List<ScannedMedia>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var statistics = ScanStatistics.None;

        foreach (var root in roots)
        {
            var scan = _scanner.Scan(root, new ScanOptions(excludedFolders));
            statistics = statistics.Add(scan.Statistics);
            media.AddRange(scan.Media.Where(item => seen.Add(item.FullPath)));
        }

        var facts = new MediaFacts[media.Count];
        var done = 0;
        Parallel.For(0, media.Count, index =>
        {
            facts[index] = new MediaFacts(media[index], _reader.Read(media[index].FullPath, media[index].Sidecars));
            progress?.Report(new AnalysisProgress(Interlocked.Increment(ref done), media.Count));
        });

        var engine = new NormalizationEngine(options ?? NormalizationOptions.ForNow(), new Sha256ContentHasher());
        return new AnalysisResult(engine.Normalize(facts), statistics, stopwatch.Elapsed);
    }
}
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~MediaAnalyzerTests"`
Attendu : tous réussis.

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Catalog PhotoApp.Tests/MediaAnalyzerTests.cs
git commit -m "feat: MediaAnalyzer (scan, lecture parallèle des métadonnées, moteur)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 14 : Projet `PhotoApp.Cli` : rapport CSV et résumé

**Fichiers :**
- Créer : `PhotoApp.Cli/PhotoApp.Cli.csproj`, `PhotoApp.Cli/Program.cs`, `PhotoApp.Cli/Report/CsvReportWriter.cs`, `PhotoApp.Cli/Report/AnalysisSummary.cs`
- Modifier : `PhotoVisualizer.slnx`, `PhotoApp.Tests/PhotoApp.Tests.csproj`
- Test : `PhotoApp.Tests/CsvReportWriterTests.cs`, `PhotoApp.Tests/AnalysisSummaryTests.cs`

**Interfaces :**
- Consomme : `MediaResult`, `DecisionTexts`, `Confidence` (tâches 5 et 9), `AnalysisResult`, `ScanStatistics` (tâches 12 et 13) ; dans les tests, `NormalizationEngineTests.Engine()` et `NormalizationEngineTests.Facts(...)` (tâche 9).
- Produit : `CsvReportWriter.Columns`, `CsvReportWriter.Write(TextWriter, IEnumerable<MediaResult>)`, `CsvReportWriter.WriteFile(string path, IEnumerable<MediaResult>)` ; `sealed record AnalysisSummary(int Total, IReadOnlyDictionary<Confidence, int> ByConfidence, IReadOnlyList<ProfileLine> Profiles, IReadOnlyList<PatternLine> UnknownPatterns, ScanStatistics Statistics, int ReadErrors, TimeSpan Duration)` avec `Applicable`, `static From(AnalysisResult)`, `static Pattern(string fileName)`, `static Percent(int, int)`, `Print(TextWriter)` ; `ProfileLine(string Profile, int Count, int Applicable)` ; `PatternLine(string Pattern, int Count)`.

- [ ] **Étape 1 : Créer le projet**

`PhotoApp.Cli/PhotoApp.Cli.csproj` :

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>photoapp</AssemblyName>
    <RootNamespace>PhotoApp.Cli</RootNamespace>
  </PropertyGroup>

  <!-- Jamais PhotoApp.Operations : la CLI est en lecture seule (spec de phase 1, §9). -->
  <ItemGroup>
    <ProjectReference Include="..\PhotoApp.Core\PhotoApp.Core.csproj" />
    <ProjectReference Include="..\PhotoApp.Metadata\PhotoApp.Metadata.csproj" />
    <ProjectReference Include="..\PhotoApp.Catalog\PhotoApp.Catalog.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.13" />
  </ItemGroup>

</Project>
```

`PhotoApp.Cli/Program.cs` (la commande `analyse` arrive à la tâche 15) :

```csharp
using System.CommandLine;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var root = new RootCommand("PhotoManager en ligne de commande.");
return root.Parse(args).Invoke();
```

```bash
dotnet sln PhotoVisualizer.slnx add PhotoApp.Cli/PhotoApp.Cli.csproj
```

Dans `PhotoApp.Tests/PhotoApp.Tests.csproj`, ajouter à l'`ItemGroup` des références de projets :

```xml
    <ProjectReference Include="..\PhotoApp.Cli\PhotoApp.Cli.csproj" />
```

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : build réussi.

- [ ] **Étape 2 : Écrire les tests qui échouent**

`PhotoApp.Tests/CsvReportWriterTests.cs` :

```csharp
using System.Text;
using PhotoApp.Cli.Report;
using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class CsvReportWriterTests
{
    private static string[] Lines(params MediaFacts[] facts)
    {
        var writer = new StringWriter();
        CsvReportWriter.Write(writer, NormalizationEngineTests.Engine().Normalize(facts));
        return writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static Dictionary<string, string> Row(string line) =>
        CsvReportWriter.Columns.Zip(line.Split(';')).ToDictionary(pair => pair.First, pair => pair.Second);

    [Fact]
    public void Header_ListsTheColumnsInOrder()
    {
        Assert.Equal(
            "chemin;type;format_reel;profil;date;precision;decalage;confiance;raison;nom_propose;exif;quicktime;gps;xmp;nom;date_fichier;fichiers_lies;drapeaux;erreur",
            Lines()[0]);
    }

    [Fact]
    public void Row_DescribesTheDecision()
    {
        var row = Row(Lines(NormalizationEngineTests.Facts("/p/IMG-20210915-WA0005.jpg"))[1]);

        Assert.Equal("/p/IMG-20210915-WA0005.jpg", row["chemin"]);
        Assert.Equal("photo", row["type"]);
        Assert.Equal("jpg", row["format_reel"]);
        Assert.Equal("whatsapp", row["profil"]);
        Assert.Equal("2021-09-15", row["date"]);
        Assert.Equal("jour", row["precision"]);
        Assert.Equal("", row["decalage"]);
        Assert.Equal("Sûre", row["confiance"]);
        Assert.Equal("messagerie-nom", row["raison"]);
        Assert.Equal("IMG-20210915-000000-01.jpg", row["nom_propose"]);
        Assert.Equal("2021-09-15", row["nom"]);
        Assert.Equal("2025-06-01 10:00:00 UTC", row["date_fichier"]);
        Assert.Equal("", row["erreur"]);
    }

    [Fact]
    public void Row_ShowsRejectedCandidates_LinkedFiles_AndFlags()
    {
        var reset = new DateCandidate(new DateTime(2000, 1, 1, 0, 0, 0), DatePrecision.Second, CandidateOrigin.ExifOriginal);
        var facts = new MediaFacts(
            new ScannedMedia("/p/IMG_20230304_132015-min.jpg", 100, new DateTime(2025, 6, 1, 10, 0, 0), ["/p/IMG_20230304_132015-min.xmp"]),
            new RawMetadata { Dates = [reset] });

        var row = Row(Lines(facts)[1]);

        Assert.Equal("2000-01-01 00:00:00 (écartée)", row["exif"]);
        Assert.Equal("IMG_20230304_132015-min.xmp", row["fichiers_lies"]);
        Assert.Equal("derive", row["drapeaux"]);
    }

    [Fact]
    public void Values_WithSeparatorsOrQuotes_AreQuoted()
    {
        var line = Lines(NormalizationEngineTests.Facts("/p/a;\"b\".jpg"))[1];

        Assert.StartsWith("\"/p/a;\"\"b\"\".jpg\";", line);
    }

    [Fact]
    public void WriteFile_CreatesTheFolder_AndWritesUtf8WithBom()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("reports", "rapport.csv");

        CsvReportWriter.WriteFile(path, NormalizationEngineTests.Engine().Normalize([NormalizationEngineTests.Facts("/p/Screenshot 2025-08-27 211650.png")]));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("capture-windows", Encoding.UTF8.GetString(bytes));
    }
}
```

`PhotoApp.Tests/AnalysisSummaryTests.cs` :

```csharp
using PhotoApp.Cli.Report;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class AnalysisSummaryTests
{
    private static AnalysisSummary Summarize(params string[] paths) =>
        AnalysisSummary.From(new AnalysisResult(
            NormalizationEngineTests.Engine().Normalize(paths.Select(path => NormalizationEngineTests.Facts(path)).ToList()),
            new ScanStatistics(1, 2, 3, 4),
            TimeSpan.FromSeconds(75)));

    [Fact]
    public void From_CountsConfidencesProfilesAndUnknownPatterns()
    {
        var summary = Summarize("/p/IMG-20210915-WA0005.jpg", "/p/IMG_20230304_132015.jpg", "/p/random.jpg", "/p/hqdefault-4B744.jpg");

        Assert.Equal(4, summary.Total);
        Assert.Equal(1, summary.ByConfidence[Confidence.Certain]);
        Assert.Equal(1, summary.ByConfidence[Confidence.Probable]);
        Assert.Equal(2, summary.ByConfidence[Confidence.Uncertain]);
        Assert.Equal(2, summary.Applicable);
        Assert.Contains(new ProfileLine("whatsapp", 1, 1), summary.Profiles);
        Assert.Contains(new ProfileLine("inconnu", 1, 0), summary.Profiles);
        Assert.Equal([new PatternLine("random.jpg", 1)], summary.UnknownPatterns);
    }

    [Fact]
    public void UnknownPatterns_AreLimitedToTwenty()
    {
        var summary = Summarize(Enumerable.Range(0, 25).Select(i => $"/p/{(char)('a' + i)}.jpg").ToArray());

        Assert.Equal(AnalysisSummary.UnknownPatternCount, summary.UnknownPatterns.Count);
    }

    [Theory]
    [InlineData("hqdefault-4B744.jpg", "hqdefault-#B###.jpg")]
    [InlineData("IMG_1234.JPG", "IMG_####.jpg")]
    public void Pattern_ReplacesDigits_AndLowercasesTheExtension(string fileName, string expected)
    {
        Assert.Equal(expected, AnalysisSummary.Pattern(fileName));
    }

    [Fact]
    public void Print_ShowsTheKeyFigures()
    {
        var writer = new StringWriter();

        Summarize("/p/IMG-20210915-WA0005.jpg", "/p/random.jpg").Print(writer);

        var text = writer.ToString();
        Assert.Contains("Analyse terminée : 2 médias en 1 min 15 s.", text);
        Assert.Contains("Sûre + Probable : 50.0 %", text);
        Assert.Contains("Motifs de nom inconnus les plus fréquents", text);
        Assert.Contains("1 fichier(s) caché(s), 2 dossier(s) caché(s), 3 fichier(s) vide(s), 4 dossier(s) inaccessible(s)", text);
    }

    [Fact]
    public void Percent_OfNothing_IsZero()
    {
        Assert.Equal("0.0 %", AnalysisSummary.Percent(0, 0));
    }
}
```

- [ ] **Étape 3 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `CsvReportWriter` et `AnalysisSummary` introuvables.

- [ ] **Étape 4 : Implémenter**

`PhotoApp.Cli/Report/CsvReportWriter.cs` :

```csharp
using System.Text;
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Cli.Report;

/// <summary>Rapport CSV de l'analyse (spec de phase 1, §9) : UTF-8 avec BOM, séparateur « ; », une ligne par média.</summary>
public static class CsvReportWriter
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "chemin", "type", "format_reel", "profil", "date", "precision", "decalage", "confiance", "raison", "nom_propose",
        "exif", "quicktime", "gps", "xmp", "nom", "date_fichier", "fichiers_lies", "drapeaux", "erreur"
    ];

    public static void WriteFile(string path, IEnumerable<MediaResult> results)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Write(writer, results);
    }

    public static void Write(TextWriter writer, IEnumerable<MediaResult> results)
    {
        writer.WriteLine(string.Join(';', Columns));
        foreach (var result in results)
        {
            writer.WriteLine(string.Join(';', Row(result).Select(Escape)));
        }
    }

    private static IEnumerable<string> Row(MediaResult result)
    {
        var decision = result.Decision;
        var date = decision.Date;
        var linked = result.Facts.File.Sidecars.Select(path => Path.GetFileName(path)).ToList();
        if (result.LivePhotoPartner is not null)
        {
            linked.Add(result.LivePhotoPartner);
        }

        return
        [
            result.Facts.File.FullPath,
            result.Kind == MediaKind.Video ? "video" : "photo",
            result.TargetExtension.TrimStart('.'),
            result.Profile?.Id ?? "inconnu",
            date is null ? string.Empty : DecisionTexts.FormatDate(date),
            date is null ? string.Empty : DecisionTexts.PrecisionLabel(date.Precision),
            date is null ? string.Empty : DecisionTexts.FormatOffset(date.Offset),
            DecisionTexts.ConfidenceLabel(decision.Confidence),
            decision.Reason,
            result.ProposedName ?? string.Empty,
            Candidates(decision, CandidateOrigin.ExifOriginal, CandidateOrigin.ExifDigitized),
            Candidates(decision, CandidateOrigin.QuickTimeApple, CandidateOrigin.QuickTimeMovieHeader),
            Candidates(decision, CandidateOrigin.Gps),
            Candidates(decision, CandidateOrigin.Xmp, CandidateOrigin.XmpSidecar),
            Candidates(decision, CandidateOrigin.FileName),
            Candidates(decision, CandidateOrigin.FileModified),
            string.Join('|', linked),
            string.Join('|', DecisionTexts.FlagLabels(decision.Flags)),
            result.Facts.Metadata.ReadError ?? string.Empty
        ];
    }

    // Candidats des origines demandées ; les valeurs écartées (implausibles) restent visibles, marquées.
    private static string Candidates(DateDecision decision, params CandidateOrigin[] origins) =>
        string.Join(" | ", decision.Candidates
            .Where(candidate => origins.Contains(candidate.Origin))
            .Select(DecisionTexts.FormatCandidate)
            .Concat(decision.Rejected
                .Where(candidate => origins.Contains(candidate.Origin))
                .Select(candidate => DecisionTexts.FormatCandidate(candidate) + " (écartée)")));

    private static string Escape(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
```

`PhotoApp.Cli/Report/AnalysisSummary.cs` :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Catalog;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Cli.Report;

public sealed record ProfileLine(string Profile, int Count, int Applicable);

public sealed record PatternLine(string Pattern, int Count);

/// <summary>Résumé affiché à la fin de l'analyse (spec de phase 1, §9). Les motifs inconnus désignent les profils manquants.</summary>
public sealed record AnalysisSummary(
    int Total,
    IReadOnlyDictionary<Confidence, int> ByConfidence,
    IReadOnlyList<ProfileLine> Profiles,
    IReadOnlyList<PatternLine> UnknownPatterns,
    ScanStatistics Statistics,
    int ReadErrors,
    TimeSpan Duration)
{
    public const int UnknownPatternCount = 20;

    private static readonly Regex Digit = new(@"\d", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int Applicable => ByConfidence.GetValueOrDefault(Confidence.Certain) + ByConfidence.GetValueOrDefault(Confidence.Probable);

    public static AnalysisSummary From(AnalysisResult analysis)
    {
        var results = analysis.Results;
        var byConfidence = Enum.GetValues<Confidence>().ToDictionary(c => c, c => results.Count(r => r.Decision.Confidence == c));
        var profiles = results
            .GroupBy(r => r.Profile?.Id ?? "inconnu")
            .Select(g => new ProfileLine(g.Key, g.Count(), g.Count(r => r.Decision.IsApplicable)))
            .OrderByDescending(line => line.Count)
            .ThenBy(line => line.Profile, StringComparer.Ordinal)
            .ToList();
        var unknown = results
            .Where(r => r.Profile is null)
            .GroupBy(r => Pattern(r.Facts.File.FileName))
            .Select(g => new PatternLine(g.Key, g.Count()))
            .OrderByDescending(line => line.Count)
            .ThenBy(line => line.Pattern, StringComparer.Ordinal)
            .Take(UnknownPatternCount)
            .ToList();

        return new AnalysisSummary(
            results.Count,
            byConfidence,
            profiles,
            unknown,
            analysis.Statistics,
            results.Count(r => r.Facts.Metadata.ReadError is not null),
            analysis.Duration);
    }

    /// <summary>Motif d'un nom : chiffres remplacés par #, extension en minuscules.</summary>
    public static string Pattern(string fileName) =>
        Digit.Replace(Path.GetFileNameWithoutExtension(fileName), "#") + Path.GetExtension(fileName).ToLowerInvariant();

    public static string Percent(int part, int total) =>
        (total == 0 ? 0 : 100.0 * part / total).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    public void Print(TextWriter writer)
    {
        writer.WriteLine($"Analyse terminée : {Total} médias en {FormatDuration(Duration)}.");
        writer.WriteLine();
        writer.WriteLine("Confiance");
        foreach (var confidence in new[] { Confidence.Certain, Confidence.Probable, Confidence.Uncertain, Confidence.None })
        {
            var count = ByConfidence.GetValueOrDefault(confidence);
            writer.WriteLine($"  {DecisionTexts.ConfidenceLabel(confidence),-12}{count,8}  {Percent(count, Total),8}");
        }

        writer.WriteLine($"  Sûre + Probable : {Percent(Applicable, Total)}");
        writer.WriteLine();
        writer.WriteLine("Par profil (nombre, % Sûre + Probable)");
        foreach (var line in Profiles)
        {
            writer.WriteLine($"  {line.Profile,-28}{line.Count,8}  {Percent(line.Applicable, line.Count),8}");
        }

        if (UnknownPatterns.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Motifs de nom inconnus les plus fréquents");
            foreach (var line in UnknownPatterns)
            {
                writer.WriteLine($"  {line.Pattern,-40}{line.Count,8}");
            }
        }

        writer.WriteLine();
        writer.WriteLine(
            $"Exclus : {Statistics.HiddenFiles} fichier(s) caché(s), {Statistics.HiddenFolders} dossier(s) caché(s), " +
            $"{Statistics.EmptyFiles} fichier(s) vide(s), {Statistics.InaccessibleFolders} dossier(s) inaccessible(s).");
        writer.WriteLine($"Fichiers illisibles : {ReadErrors}.");
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalMinutes >= 1
        ? $"{(int)duration.TotalMinutes} min {duration.Seconds} s"
        : duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
}
```

- [ ] **Étape 5 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~CsvReportWriterTests|FullyQualifiedName~AnalysisSummaryTests"`
Attendu : tous réussis.

- [ ] **Étape 6 : Commiter**

```bash
git add PhotoApp.Cli PhotoVisualizer.slnx PhotoApp.Tests
git commit -m "feat: projet PhotoApp.Cli, rapport CSV et résumé d'analyse" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 15 : Commande `analyse` (lecture seule garantie, codes de sortie)

**Fichiers :**
- Modifier : `PhotoApp.Core/AppDataPaths.cs` (+ `ReportsFolder`), `PhotoApp.Cli/Program.cs`
- Créer : `PhotoApp.Cli/AnalyseCommand.cs`, `PhotoApp.Cli/ConsoleProgress.cs`
- Test : `PhotoApp.Tests/AnalyseCommandTests.cs`, `PhotoApp.Tests/AppDataPathsTests.cs`

**Interfaces :**
- Consomme : `MediaAnalyzer`, `AnalysisProgress` (tâche 13), `CsvReportWriter`, `AnalysisSummary` (tâche 14), `AppDataPaths` (existant).
- Produit : `AppDataPaths.ReportsFolder` (`<DataRoot>/reports`) ; `sealed record AnalyseRequest(IReadOnlyList<string> Folders, string? OutputPath, IReadOnlyList<string> Excluded)` ; `static class ExitCodes { Success = 0, InvalidArguments = 1, Fatal = 2 }` ; `AnalyseCommand(AppDataPaths paths, TextWriter output, TextWriter error, Func<DateTime>? localNow = null)` avec `int Run(AnalyseRequest)` ; `ConsoleProgress(TextWriter) : IProgress<AnalysisProgress>`.

- [ ] **Étape 1 : Écrire les tests qui échouent**

Ajouter à `PhotoApp.Tests/AppDataPathsTests.cs` :

```csharp
    [Fact]
    public void ReportsFolder_IsUnderTheDataRoot()
    {
        var env = new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\x\AppData\Local" };

        var paths = AppDataPaths.Resolve(PlatformKind.Windows, k => env.GetValueOrDefault(k), @"C:\Users\x");

        Assert.Equal(Path.Combine(paths.DataRoot, "reports"), paths.ReportsFolder);
    }
```

`PhotoApp.Tests/AnalyseCommandTests.cs` :

```csharp
using System.Security.Cryptography;
using PhotoApp.Cli;
using PhotoApp.Core;

namespace PhotoApp.Tests;

public sealed class AnalyseCommandTests
{
    private static AppDataPaths PathsUnder(string root) =>
        AppDataPaths.Resolve(PlatformKind.Windows, name => name == "LOCALAPPDATA" ? root : null, root);

    // Taille, date de modification et SHA-256 de chaque fichier : la preuve que rien n'a été touché.
    private static Dictionary<string, (long, DateTime, string)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            path => path,
            path => (new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    private static string CreateLibrary(TempDirectory temp)
    {
        var photos = temp.Combine("photos");
        Directory.CreateDirectory(photos);
        TestImages.WritePng(Path.Combine(photos, "IMG-20210915-WA0005.png"));
        TestImages.WriteJpeg(Path.Combine(photos, "holiday.jpg"), "2023:03:04 13:20:15");
        TestVideos.Write(Path.Combine(photos, "20260917_222944.mp4"), new DateTime(2026, 9, 17, 20, 29, 44), brand: "isom");
        return photos;
    }

    [Fact]
    public void Run_WritesTheReport_AndNeverTouchesThePhotos()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        var before = Snapshot(photos);
        var report = temp.Combine("out", "rapport.csv");
        var output = new StringWriter();

        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), output, new StringWriter())
            .Run(new AnalyseRequest([photos], report, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, Snapshot(photos));
        Assert.Equal(4, File.ReadAllLines(report).Length);   // en-tête + 3 médias
        Assert.Contains("Sûre + Probable", output.ToString());
        Assert.Contains("Rapport : " + report, output.ToString());
    }

    [Fact]
    public void Run_WithoutOut_WritesIntoTheReportsFolder()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        var paths = PathsUnder(temp.Combine("data"));

        var exitCode = new AnalyseCommand(paths, new StringWriter(), new StringWriter(), () => new DateTime(2026, 10, 19, 10, 15, 0))
            .Run(new AnalyseRequest([photos], null, []));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(Path.Combine(paths.ReportsFolder, "analyse-20261019-101500.csv")));
    }

    [Fact]
    public void Run_ExcludedFolder_IsSkipped()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        Directory.CreateDirectory(Path.Combine(photos, "skip"));
        TestImages.WritePng(Path.Combine(photos, "skip", "IMG-20210916-WA0001.png"));
        var report = temp.Combine("rapport.csv");

        new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), new StringWriter())
            .Run(new AnalyseRequest([photos], report, [Path.Combine(photos, "skip")]));

        Assert.DoesNotContain("IMG-20210916-WA0001", File.ReadAllText(report));
    }

    [Fact]
    public void Run_MissingFolder_IsAnInvalidArgument()
    {
        using var temp = new TempDirectory();
        var error = new StringWriter();

        var exitCode = new AnalyseCommand(PathsUnder(temp.FullPath), new StringWriter(), error)
            .Run(new AnalyseRequest([temp.Combine("absent")], null, []));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains("Dossier introuvable", error.ToString());
    }

    [Fact]
    public void Run_UnwritableReport_IsFatal()
    {
        using var temp = new TempDirectory();
        var photos = CreateLibrary(temp);
        var error = new StringWriter();

        // Un dossier existant à la place du fichier de rapport.
        var exitCode = new AnalyseCommand(PathsUnder(temp.Combine("data")), new StringWriter(), error)
            .Run(new AnalyseRequest([photos], photos, []));

        Assert.Equal(ExitCodes.Fatal, exitCode);
        Assert.Contains("Impossible d'écrire le rapport", error.ToString());
    }

    [Fact]
    public void ReadOnlyByConstruction_NoProjectOfTheCliReferencesOperations()
    {
        var assemblies = new[] { typeof(AnalyseCommand).Assembly, typeof(MediaAnalyzer).Assembly, typeof(MetadataReader).Assembly };

        Assert.All(assemblies, assembly =>
            Assert.DoesNotContain("PhotoApp.Operations", assembly.GetReferencedAssemblies().Select(name => name.Name)));
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `ReportsFolder`, `AnalyseCommand`, `AnalyseRequest`, `ExitCodes` introuvables.

- [ ] **Étape 3 : Implémenter**

Dans `PhotoApp.Core/AppDataPaths.cs`, après `LogsFolder` :

```csharp
    public string ReportsFolder => Path.Combine(DataRoot, "reports");
```

`PhotoApp.Cli/ConsoleProgress.cs` :

```csharp
using PhotoApp.Catalog;

namespace PhotoApp.Cli;

/// <summary>Progression sur la sortie d'erreur, réécrite sur la même ligne tous les 100 fichiers et à la fin.</summary>
public sealed class ConsoleProgress(TextWriter writer) : IProgress<AnalysisProgress>
{
    private readonly object _gate = new();

    public void Report(AnalysisProgress value)
    {
        if (value.Done % 100 != 0 && value.Done != value.Total)
        {
            return;
        }

        lock (_gate)
        {
            writer.Write($"\rLecture des métadonnées : {value.Done} / {value.Total}");
        }
    }
}
```

`PhotoApp.Cli/AnalyseCommand.cs` :

```csharp
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
```

`PhotoApp.Cli/Program.cs` (réécrit en entier) :

```csharp
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
```

- [ ] **Étape 4 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~AnalyseCommandTests|FullyQualifiedName~AppDataPathsTests"`
Attendu : tous réussis.

Vérification manuelle des codes de sortie (PowerShell) :

```powershell
dotnet run --project PhotoApp.Cli -- analyse; "exit=$LASTEXITCODE"            # attendu : message d'argument manquant, exit=1
dotnet run --project PhotoApp.Cli -- analyse --help                           # attendu : aide en français, options --out et --exclude
```

- [ ] **Étape 5 : Commiter**

```bash
git add PhotoApp.Core/AppDataPaths.cs PhotoApp.Cli PhotoApp.Tests
git commit -m "feat: commande analyse (rapport par défaut dans reports, codes de sortie, lecture seule)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 16 : L'interface passe au moteur unique ; suppression de l'ancien sélecteur

**Fichiers :**
- Modifier : `PhotoApp.Core/Models/ImageFileInfo.cs`, `PhotoApp.Operations/ConversionService.cs`, `PhotoApp/ViewModels/MainWindowViewModel.cs`, `PhotoApp/Views/MainWindow.axaml`, `PhotoApp.Tests/ConversionServiceTests.cs`
- Supprimer : `PhotoApp.Core/Services/DateSelector.cs`, `PhotoApp.Core/Services/FileNameDateParser.cs`, `PhotoApp.Core/Models/ImageDateSource.cs`, `PhotoApp.Core/Models/ImageDateMetadata.cs`, `PhotoApp.Metadata/ImageDateResolver.cs`, `PhotoApp.Metadata/NativeImageMetadataReader.cs`, `PhotoApp.Metadata/FileSystemDateReader.cs`, et leurs tests `DateSelectorTests.cs`, `FileNameDateParserTests.cs`, `ImageDateResolverTests.cs`, `NativeImageMetadataReaderTests.cs`
- Test : `PhotoApp.Tests/ImageFileInfoTests.cs`, `PhotoApp.Tests/ConversionServiceTests.cs`

**Interfaces :**
- Consomme : `MediaAnalyzer` (tâche 13), `DateDecision`, `DecisionTexts`, `MediaResult` (tâches 5 et 9).
- Produit : `ImageFileInfo(string fullPath, string fileName, string extension, long fileSizeBytes, DateDecision? decision = null)` avec `Decision`, `RetainedDateText`, `DecisionText`, `NormalizedFileNamePreview`, `StatusText`, `static FromResult(MediaResult)` ; `ConversionService` ne convertit que les décisions `IsApplicable`.

- [ ] **Étape 1 : Vérifier que les cas de l'ancien parseur sont repris**

Chaque cas de `PhotoApp.Tests/FileNameDateParserTests.cs` doit exister dans `SourceProfilesTests` (tâche 4) ou `GenericNameParserTests` (tâche 3) avant la suppression :

| Ancien cas | Repris dans |
|---|---|
| `Persona 5 Royal 15.01.2025 00_54_14`, `12.25.2024 10-30-00`, `03-04-2025 10-00-00`, `random-file-name` | `GenericNameParserTests` |
| `IMG_20991231_235959`, `IMG_19800101_000001` (implausibles) | `SourceProfilesTests.Read_KeepsImplausibleDates…` (lecture) et `DateArbiterTests.ImplausibleCandidates…` (rejet) |
| Tous les autres (captures, WhatsApp, Pixel, Signal, Nextcloud, epoch…) | `SourceProfilesTests` |

Run : `grep -c "InlineData" PhotoApp.Tests/FileNameDateParserTests.cs PhotoApp.Tests/SourceProfilesTests.cs PhotoApp.Tests/GenericNameParserTests.cs`
Attendu : les nouveaux fichiers en contiennent davantage que l'ancien.

- [ ] **Étape 2 : Écrire les tests qui échouent**

`PhotoApp.Tests/ImageFileInfoTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Normalization;

namespace PhotoApp.Tests;

public sealed class ImageFileInfoTests
{
    private static ImageFileInfo With(Confidence confidence, string reason, DateCandidate? date) =>
        new("/p/x.png", "x.png", ".png", 10, new DateDecision(date, confidence, reason, [], []));

    private static readonly DateCandidate WhatsAppDay = new(new DateTime(2021, 9, 15), DatePrecision.Day, CandidateOrigin.FileName);

    [Fact]
    public void Applicable_ShowsConfidenceReasonAndPreview()
    {
        var image = With(Confidence.Certain, Reasons.MessagingName, WhatsAppDay);

        Assert.Equal("2021-09-15", image.RetainedDateText);
        Assert.Equal("Sûre — Nom donné par la messagerie (fait foi)", image.DecisionText);
        Assert.Equal("IMG-20210915-000000-01.jpg", image.NormalizedFileNamePreview);
        Assert.Equal(string.Empty, image.StatusText);
    }

    [Fact]
    public void Uncertain_IsToReview_WithoutPreview()
    {
        var fileDate = new DateCandidate(new DateTime(2023, 3, 1, 10, 0, 0), DatePrecision.Second, CandidateOrigin.FileModified);
        var image = With(Confidence.Uncertain, Reasons.FileDateOnly, fileDate);

        Assert.Null(image.NormalizedFileNamePreview);
        Assert.Equal("À vérifier : Seulement la date du fichier (souvent celle de la copie)", image.StatusText);
    }

    [Fact]
    public void None_SaysWhy()
    {
        var image = With(Confidence.None, Reasons.ReadFailed, null);

        Assert.Equal("—", image.RetainedDateText);
        Assert.Equal("Aucune date fiable : Fichier illisible", image.StatusText);
    }
}
```

Dans `PhotoApp.Tests/ConversionServiceTests.cs` :

1. Remplacer le helper `ScanAndResolve` par :

```csharp
    private static IReadOnlyList<ImageFileInfo> ScanAndResolve(string root, string outputFolder) =>
        new MediaAnalyzer().Analyze([root], [outputFolder]).Results
            .Where(result => result.Kind == MediaKind.Photo)
            .Select(ImageFileInfo.FromResult)
            .ToList();
```

2. Remplacer le test `CorruptFile_FailsAlone_AndLeavesNoOrphan` par (un fichier illisible est désormais « lecture impossible » : il n'est jamais converti ; l'échec de conversion reste couvert par `BatchRenameExecutorTests`) :

```csharp
    [Fact]
    public void UnreadableFile_IsNotConverted_AndTheOthersAre()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "IMG_20240101_101010.png"), [1, 2, 3]);   // pas une vraie image
        TestImages.WritePng(Path.Combine(root, "IMG_20240410_111652.png"));

        var summary = new ConversionService().Run(ScanAndResolve(root, output), output, manifestPath, 90, false);

        Assert.Equal(1, summary.Converted);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, summary.SkippedUnreliableDate);
        Assert.Equal(["IMG-20240410-111652.jpg"], OutputNames(output));
    }
```

3. Ajouter :

```csharp
    [Fact]
    public void UncertainDates_AreNeverApplied()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);
        TestImages.WritePng(Path.Combine(root, "03-04-2025 10-00-00.png"));   // JJ/MM ambigu
        TestImages.WritePng(Path.Combine(root, "scan 20190501.png"));         // jour seul, nom générique
        TestImages.WritePng(Path.Combine(root, "random.png"));                // seulement la date du fichier

        var summary = new ConversionService().Run(ScanAndResolve(root, output), output, manifestPath, 90, false);

        Assert.Equal(0, summary.Converted);
        Assert.Equal(3, summary.SkippedUnreliableDate);
        Assert.Empty(OutputNames(output));
    }
```

- [ ] **Étape 3 : Vérifier l'échec**

Run : `dotnet build PhotoVisualizer.slnx`
Attendu : `ImageFileInfo.FromResult`, `DecisionText`… introuvables.

- [ ] **Étape 4 : Implémenter**

`PhotoApp.Core/Models/ImageFileInfo.cs` (réécrit en entier) :

```csharp
using PhotoApp.Core.Normalization;
using PhotoApp.Core.Services;

namespace PhotoApp.Core.Models;

/// <summary>Une photo de l'interface, avec la décision du moteur. Seules Sûre et Probable sont appliquées.</summary>
public sealed class ImageFileInfo
{
    public ImageFileInfo(
        string fullPath,
        string fileName,
        string extension,
        long fileSizeBytes,
        DateDecision? decision = null)
    {
        FullPath = fullPath;
        FileName = fileName;
        Extension = extension;
        FileSizeBytes = fileSizeBytes;
        Decision = decision;
    }

    public string FullPath { get; }

    public string FileName { get; }

    public string Extension { get; }

    public long FileSizeBytes { get; }

    public DateDecision? Decision { get; }

    public string RetainedDateText => Decision?.Date is { } date ? DecisionTexts.FormatDate(date) : "—";

    public string DecisionText => Decision is null
        ? DecisionTexts.ConfidenceLabel(Confidence.None)
        : $"{DecisionTexts.ConfidenceLabel(Decision.Confidence)} — {DecisionTexts.ReasonLabel(Decision.Reason)}";

    public string? NormalizedFileNamePreview =>
        Decision is { IsApplicable: true, Date: { } date } ? NormalizedFileNameBuilder.Preview(date) : null;

    public string StatusText => Decision switch
    {
        null or { IsApplicable: true } => string.Empty,
        { Confidence: Confidence.Uncertain } => "À vérifier : " + DecisionTexts.ReasonLabel(Decision.Reason),
        _ => "Aucune date fiable : " + DecisionTexts.ReasonLabel(Decision.Reason)
    };

    public static ImageFileInfo FromResult(MediaResult result) => new(
        result.Facts.File.FullPath,
        result.Facts.File.FileName,
        result.Facts.File.Extension,
        result.Facts.File.SizeBytes,
        result.Decision);
}
```

Dans `PhotoApp.Operations/ConversionService.cs`, remplacer :

```csharp
            if (image.DateMetadata is not { IsReliable: true, RetainedCandidate: { } date })
```

par :

```csharp
            // Seules les dates Sûre et Probable sont appliquées ; Incertaine et Aucune restent « À vérifier ».
            if (image.Decision is not { IsApplicable: true, Date: { } date })
```

Dans `PhotoApp/ViewModels/MainWindowViewModel.cs` :
- retirer `using PhotoApp.Metadata;` et `using PhotoApp.Core.Services;` ;
- remplacer les champs `_mediaScanner` et `_imageDateResolver` par `private readonly MediaAnalyzer _mediaAnalyzer = new();` ;
- remplacer la méthode `ScanAndResolve` par :

```csharp
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
```

Dans `PhotoApp/Views/MainWindow.axaml`, remplacer la ligne :

```xml
                            <TextBlock Text="{Binding RetainedDateSourceText, StringFormat='Source: {0}'}" Opacity="0.8" FontSize="12" />
```

par :

```xml
                            <TextBlock Text="{Binding DecisionText, StringFormat='Confiance: {0}'}" Opacity="0.8" FontSize="12" />
```

Supprimer l'ancien sélecteur et ses tests :

```bash
git rm PhotoApp.Core/Services/DateSelector.cs PhotoApp.Core/Services/FileNameDateParser.cs \
       PhotoApp.Core/Models/ImageDateSource.cs PhotoApp.Core/Models/ImageDateMetadata.cs \
       PhotoApp.Metadata/ImageDateResolver.cs PhotoApp.Metadata/NativeImageMetadataReader.cs PhotoApp.Metadata/FileSystemDateReader.cs \
       PhotoApp.Tests/DateSelectorTests.cs PhotoApp.Tests/FileNameDateParserTests.cs \
       PhotoApp.Tests/ImageDateResolverTests.cs PhotoApp.Tests/NativeImageMetadataReaderTests.cs
```

- [ ] **Étape 5 : Vérifier que tout passe**

Run : `dotnet build PhotoVisualizer.slnx && dotnet test PhotoVisualizer.slnx`
Attendu : build sans erreur (interface comprise), 0 échec. `grep -rn "DateSelector\|ImageDateMetadata\|ImageDateSource\|ImageDateResolver\|NativeImageMetadataReader\|FileSystemDateReader\|FileNameDateParser" --include=*.cs --include=*.axaml . | grep -v '/bin/\|/obj/'` ne renvoie rien.

- [ ] **Étape 6 : Vérifier l'interface à la main**

Run : `dotnet run --project PhotoApp`
Choisir un dossier de test (une copie, jamais la bibliothèque réelle) contenant au moins une photo WhatsApp, une capture et un fichier sans date. Attendu : chaque ligne affiche « Confiance: Sûre — … » ou « Probable — … » ; le fichier sans date affiche « À vérifier : … » en orange et n'a pas de « Nom cible » ; **Convertir** ne produit que les fichiers Sûrs ou Probables.

- [ ] **Étape 7 : Commiter**

```bash
git add -A
git commit -m "feat: l'interface utilise le moteur unique ; suppression de l'ancien sélecteur de date" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 17 : Mesure à mi-phase sur la bibliothèque réelle (lecture seule) et objectif chiffré

**Fichiers :**
- Modifier : `docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md` (§12)

**Interfaces :**
- Consomme : la CLI complète (tâches 14 et 15).
- Produit : un rapport réel dans `%LOCALAPPDATA%\PhotoManager\reports\` ; l'objectif chiffré inscrit au §12 de la spec.

> **Point d'arrêt utilisateur.** Cette tâche s'arrête pour que l'utilisateur fixe l'objectif. Ne rien inventer à sa place.

- [ ] **Étape 1 : Résoudre les dossiers réels (jamais de chemin écrit en dur)**

```bash
pwsh -NoProfile -c "[Environment]::GetFolderPath('MyPictures'); [Environment]::GetFolderPath('MyVideos')"
```

Attendu : deux chemins sous `C:\Users\ismail\Nextcloud\…`. Vérifier qu'ils existent (`Test-Path`). S'ils n'existent pas ou semblent faux, demander à l'utilisateur.

- [ ] **Étape 2 : Empreinte avant analyse**

Script `scratchpad/snapshot.ps1` (dans le dossier temporaire de la session, pas dans le dépôt) :

```powershell
param([string[]] $Folders)
$files = Get-ChildItem -LiteralPath $Folders -Recurse -File -Force -ErrorAction SilentlyContinue
[pscustomobject]@{
    Count       = $files.Count
    TotalBytes  = ($files | Measure-Object Length -Sum).Sum
    LatestWrite = ($files | Measure-Object LastWriteTimeUtc -Maximum).Maximum.ToString('o')
} | ConvertTo-Json
```

```bash
pwsh -NoProfile -File <scratchpad>/snapshot.ps1 -Folders "<Images>","<Vidéos>" > <scratchpad>/before.json
```

- [ ] **Étape 3 : Analyser**

```bash
dotnet run --project PhotoApp.Cli -c Release -- analyse "<Images>" "<Vidéos>"
```

Attendu : code 0, résumé affiché, chemin du rapport sous `%LOCALAPPDATA%\PhotoManager\reports\`. Noter la durée.

- [ ] **Étape 4 : Prouver que rien n'a été écrit**

```bash
pwsh -NoProfile -File <scratchpad>/snapshot.ps1 -Folders "<Images>","<Vidéos>" > <scratchpad>/after.json
diff <scratchpad>/before.json <scratchpad>/after.json && echo "aucune écriture"
```

Attendu : `aucune écriture`. Sinon, arrêter et signaler (Nextcloud peut synchroniser pendant l'analyse : relancer la comparaison pour trancher).

- [ ] **Étape 5 : Dépouiller le rapport**

```powershell
$rows = Import-Csv -Delimiter ';' -Encoding utf8 '<rapport.csv>'
$rows | Group-Object confiance | Select-Object Name, Count
$rows | Group-Object profil | Sort-Object Count -Descending | Select-Object Name, Count
$rows | Where-Object raison -eq 'desaccord' | Group-Object profil | Sort-Object Count -Descending | Select-Object Name, Count
$rows | Where-Object confiance -in 'Incertaine','Aucune' | Group-Object raison | Sort-Object Count -Descending | Select-Object Name, Count
```

Présenter à l'utilisateur : répartition par confiance, sources du cadre §3.2 reconnues ou non (WhatsApp → `whatsapp`, Xiaomi → `xiaomi`, Samsung → `samsung`, iPhone Nextcloud → `nextcloud-ios`, captures Android → `capture-android-kde`, Windows → `capture-windows`, GNOME → `capture-gnome`, Signal → `signal`, Snapchat → `snapchat`, téléchargements → `miniature-video`, copies `-min` → drapeau `derive`, albums renommés → profil de leur marque EXIF), principaux motifs inconnus, principaux désaccords, durée.

- [ ] **Étape 6 : L'utilisateur fixe l'objectif**

Demander (AskUserQuestion) l'objectif de « Sûre + Probable » en proposant trois valeurs calées sur la mesure (mesuré, mesuré + gain attendu des profils manquants, plus ambitieux) ; l'utilisateur peut en donner une autre.

- [ ] **Étape 7 : Inscrire l'objectif dans la spec et commiter**

Dans le §12 de la spec, remplacer la ligne `**Objectif chiffré :** à fixer à la mesure de mi-phase (décision de l'utilisateur du 2026-10-19).` par :

```markdown
**Objectif chiffré :** ≥ <X> % de « Sûre + Probable » sur la bibliothèque réelle (Images + Vidéos), fixé le <date> par l'utilisateur après une première mesure de <Y> % (<N> médias, durée <D>).
```

```bash
git add docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md
git commit -m "docs: objectif chiffré de la phase 1 fixé après la mesure réelle" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 18 : Compléter profils et règles d'après le rapport réel

**Fichiers :**
- Modifier : `PhotoApp.Core/Profiles/SourceProfiles.cs`, éventuellement `PhotoApp.Core/Normalization/DateComparison.cs` ou `DateArbiter.cs`
- Test : `PhotoApp.Tests/SourceProfilesTests.cs`, `PhotoApp.Tests/DateArbiterConfrontationTests.cs`, `PhotoApp.Tests/DateComparisonTests.cs`

**Interfaces :**
- Consomme : le rapport de la tâche 17.
- Produit : des profils et des règles qui atteignent l'objectif chiffré et reconnaissent chaque source du cadre §3.2.

Le contenu exact dépend du rapport. Chaque correction suit le même cycle TDD ; on traite les éléments par nombre de fichiers concernés, du plus grand au plus petit.

- [ ] **Étape 1 : Un profil manquant (motif inconnu fréquent, ≥ 10 fichiers, ou source du §3.2 non reconnue)**

1. Prendre 2 à 3 noms réels du motif dans le rapport (sans donnée privée dans le nom ; sinon, les anonymiser en gardant la forme).
2. Si la famille ou le sens de l'heure (locale ou UTC) n'est pas évident, demander à l'utilisateur.
3. Ajouter les cas à la théorie `Read_RecognizesTheProfileAndTheDate` de `SourceProfilesTests` (ou à `Read_ProfilesWithoutADateInTheName`), par exemple :

```csharp
    [InlineData("<nom réel ou anonymisé>.jpg", null, "<id-du-profil>", 2024, 1, 5, 15, 4, 12, DatePrecision.Second)]
```

4. `dotnet test PhotoVisualizer.slnx --filter "FullyQualifiedName~SourceProfilesTests"` → le nouveau cas échoue.
5. Ajouter le profil dans `SourceProfiles.All`, **avant** tout profil plus général qui le masquerait, sur le modèle :

```csharp
        new("<id-du-profil>", "<Nom affiché>", ProfileFamily.<Camera|Messaging|Screenshot|Export>,
        [
            P(@"^<motif avec groupes nommés year, month, day, hour, minute, second…>$")
        ]),
```

6. Relancer les tests → tous verts (les anciens cas compris).
7. `git commit -m "feat: profil <id-du-profil>" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"`.

- [ ] **Étape 2 : Un groupe de désaccords (raison `desaccord`)**

1. Filtrer le rapport sur `raison = desaccord`, regrouper par profil, examiner 5 lignes représentatives (colonnes `exif`, `quicktime`, `xmp`, `nom`).
2. Classer l'écart :
   - **tolérance trop stricte** (écart régulier de quelques secondes) → ajuster la constante dans `DateComparison` et ajouter le cas à `DateComparisonTests` ;
   - **explication manquante** (comportement connu d'une source) → ajouter le cas à `DateArbiterConfrontationTests`, puis la branche correspondante dans `DateArbiter.Agrees`, commentée par la source qui le justifie ;
   - **vrai conflit** → ne rien changer : la revue « À vérifier » est la bonne issue.
3. Pour toute modification de tolérance ou de règle, mettre à jour le §5.2 ou le §5.4 de la spec dans le même commit.
4. Tests verts, puis commit.

- [ ] **Étape 3 : Mesurer à nouveau**

Refaire les étapes 2 à 4 de la tâche 17 (empreinte, analyse, absence d'écriture). Recommencer les étapes 1 et 2 de cette tâche jusqu'à ce que :
- le pourcentage « Sûre + Probable » atteigne l'objectif du §12 ;
- chaque source du cadre §3.2 apparaisse sous son profil.

Si l'objectif paraît inatteignable sans inventer de dates, s'arrêter et en parler à l'utilisateur plutôt que d'assouplir les règles.

---

### Tâche 19 : Documentation et vérification finale

**Fichiers :**
- Modifier : `README.md`

- [ ] **Étape 1 : Mettre à jour le README**

Dans `README.md` :

1. Ligne « Phase en cours » :

```markdown
- **Phase en cours :** phase 1 (moteur de normalisation, lecture seule) terminée — spec : [`docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md`](docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md), plan : [`docs/superpowers/plans/2026-10-19-phase1-moteur-normalisation.md`](docs/superpowers/plans/2026-10-19-phase1-moteur-normalisation.md). Prochaine étape : phase 2, exécution sûre (renommage sur place, journal, annulation).
```

2. Dans « Ce qui marche aujourd'hui », remplacer les puces « Date retenue par image… » et « Noms reconnus… » par :

```markdown
- **Moteur de normalisation unique**, partagé par l'interface et la CLI : il lit tous les candidats (EXIF avec décalage et sous-secondes, GPS, XMP et sidecar `.xmp`, dates Apple QuickTime avec leur décalage, en-tête `mvhd` des vidéos, nom de fichier, date du fichier), reconnaît la source par un **profil** (WhatsApp, Signal, Telegram, Samsung, Xiaomi, Pixel, Huawei, OnePlus, iPhone et Nextcloud iOS, captures Android/KDE, Windows, GNOME, macOS, exports WeChat/Facebook/epoch, Snapchat…), confronte les candidats et donne une **confiance** expliquée par une raison : Sûre, Probable (appliquées), Incertaine (« À vérifier »), Aucune.
  - WhatsApp : le nom fait foi ; un EXIF antérieur ou du même jour l'emporte (photo transférée).
  - Dates JJ/MM ambiguës tranchées par les fichiers voisins ou par un autre candidat, sinon signalées.
  - La date du fichier (souvent celle de la copie) n'est jamais appliquée.
- **Commande `photoapp analyse`** en lecture seule : rapport CSV (une ligne par média : profil, date, confiance, raison, nom proposé, tous les candidats) et résumé (répartition, profils, motifs de nom inconnus).
- **Noms proposés** : `IMG-…` pour les photos, `VID-…` pour les vidéos, Live Photo associées (même nom de base), collisions numérotées dans l'ordre réel (sous-secondes, compteur WhatsApp, empreinte), vrai format détecté.
```

3. Après la section « Développement », ajouter :

````markdown
## Analyser une bibliothèque (lecture seule)

```powershell
dotnet run --project PhotoApp.Cli -c Release -- analyse "D:\Photos" "D:\Vidéos" --exclude "D:\Photos\Converted"
```

- Rien n'est écrit à côté des photos. Le rapport va dans `%LOCALAPPDATA%\PhotoManager\reports\analyse-AAAAMMJJ-HHmmss.csv` (ou `--out fichier.csv`).
- Le CSV s'ouvre directement dans Excel ou LibreOffice (UTF-8 avec BOM, séparateur `;`).
- Codes de sortie : 0 analyse faite (même avec des fichiers illisibles, listés dans le rapport), 1 argument invalide, 2 erreur fatale.
````

4. Dans « Limites connues », ajouter :

```markdown
- Les outils manuels (décalage horaire par lot, date fixée, choix d'un candidat) arrivent en phase 3 ou 4 ; les sidecars Google Takeout en phase 8.
- L'interface ignore encore les vidéos (renommage sur place et vidéos : phase 2). La CLI les analyse.
```

5. Dans « Architecture », ajouter la ligne `PhotoApp.Cli/` et préciser Core :

```text
PhotoApp.Core/         pur, sans E/S ni paquet : modèles, profils de sources (Profiles), moteur de normalisation
                       (Normalization : comparaison, arbitre, ambiguïtés, nommage), racines de données (AppDataPaths)
PhotoApp.Metadata/     lecture des métadonnées brutes (MetadataExtractor + clés Apple QuickTime)
PhotoApp.Catalog/      scan des dossiers (photos, vidéos, sidecars) et analyse complète (MediaAnalyzer)
PhotoApp.Cli/          commande « analyse » en lecture seule (ne référence jamais Operations)
```

6. Dans le tableau « Données de l'application », ligne Données : `(manifestes, rapports d'analyse, futur catalogue)`.

- [ ] **Étape 2 : Vérification complète**

```bash
dotnet build PhotoVisualizer.slnx --configuration Release
dotnet test PhotoVisualizer.slnx --configuration Release
dotnet list PhotoVisualizer.slnx package --vulnerable --include-transitive
```

Attendu : build sans erreur ni nouvel avertissement, 0 échec, aucun paquet vulnérable.

- [ ] **Étape 3 : Commiter**

```bash
git add README.md
git commit -m "docs: README de la phase 1 (moteur, commande analyse)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Étape 4 : Intégration**

Utiliser superpowers:finishing-a-development-branch. Rappeler à l'utilisateur que la CI Linux et macOS ne tourne qu'après un push, et que le push se fait depuis son terminal (pas de clé SSH dans la session). Ne rien pousser ni fusionner sans sa demande.
