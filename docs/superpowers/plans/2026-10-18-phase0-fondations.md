# Phase 0 — Fondations : plan d'implémentation

> **Pour les agents d'exécution :** SOUS-COMPÉTENCE REQUISE : utiliser superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans pour exécuter ce plan tâche par tâche. Les étapes utilisent des cases à cocher (`- [ ]`).

**Objectif :** rendre le flux actuel (scan → date → renommage/conversion JPEG) fiable sur JPEG et PNG : corriger les 9 défauts du §3.1 de la spec, remplacer ImageSharp vulnérable par Magick.NET, découper en projets, centraliser les données de l'application, et mettre en place les tests et l'intégration continue sur 3 OS.

> **Révision du 2026-10-18 (décision utilisateur) :** ImageSharp est **remplacé par Magick.NET** au lieu d'être mis à jour. Raisons : ImageSharp 4.x exige une clé de licence pour tout build Release ; la 3.1.12 garde un avis modéré (GHSA-gwg2-r3hj-4w44) ; Magick.NET était déjà retenu par la spec (§2) pour HEIC/AVIF/RAW. Conséquence : HEIC, HEIF et AVIF deviennent convertibles dès la phase 0 (défaut 2 réellement corrigé).

**Architecture :** la solution passe de 3 à 6 projets : `PhotoApp.Core` (pur, sans E/S : modèles, parseur de noms, nommage, planification), `PhotoApp.Metadata` (lecture MetadataExtractor), `PhotoApp.Catalog` (scanner), `PhotoApp.Operations` (conversion, exécution, manifeste), `PhotoApp` (Avalonia), `PhotoApp.Tests`. La date devient une **heure murale** (`DateTime` sans fuseau + décalage optionnel), jamais un faux UTC. Un **manifeste** par dossier de sortie, stocké dans les données de l'application, rend le flux idempotent.

**Tech Stack :** .NET 10, Avalonia 12.0.2, CommunityToolkit.Mvvm, MetadataExtractor 2.9.3, Magick.NET-Q8-AnyCPU 14.17.2 (Apache-2.0, binaires natifs Windows/Linux/macOS inclus), xUnit + Moq, GitHub Actions.

**Spec :** `docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md` (§3.1 défauts, §4 architecture, §5.5 nommage, §9 données, §10 phase 0, §11 tests)

## Contraintes globales

- .NET 10 + Avalonia 12 ; `dotnet build` / `dotnet run` doivent suffire (aucune étape manuelle).
- Heure de référence = heure locale du lieu de prise de vue (heure « murale »). Format 24 h, toujours.
- Nommage : `IMG-AAAAMMJJ-HHmmss` ; collision dans la même seconde : suffixe `-01`, `-02`… ; date au jour seulement : `IMG-AAAAMMJJ-000000-NN` ; extension en minuscules, `.jpeg` → `.jpg` ; un fichier déjà nommé n'est jamais renuméroté.
- `PhotoApp.Core` est **pur** : aucune E/S fichier, aucun package NuGet.
- Tout ce que l'application produit vit sous des racines centralisées résolues par une classe unique (`AppDataPaths`). Rien n'est écrit à côté des photos hors du dossier de sortie `Converted`.
- Une date non fiable n'est jamais inventée ni appliquée automatiquement (la date de modification du fichier est « faible, toujours »).
- Les commits se terminent par la ligne `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (second `-m` dans les commandes ci-dessous).
- Ne pas pousser la branche et ne pas fusionner dans `main` sans demande explicite de l'utilisateur.

## Hors phase 0 (explicite)

Projet `PhotoApp.Cli` (phase 1), profils de sources et confrontation de candidats (phase 1), écriture ExifTool (phase 3), règles fines de conversion — qualité réglable, « ignorer si plus lourd » (sauf HEIC/HEIF/AVIF, toujours convertis pour la compatibilité), profil ICC Display P3 vérifié (phase 5), journal/annulation/quarantaine (phase 2). La suppression de l'original (`DeleteOriginalAfterSuccess`) reste telle quelle jusqu'à la phase 2.

## Écarts assumés par rapport à la spec

- **Corpus de test** : les fichiers d'exemple sont **générés à l'exécution** (Magick.NET, quelques centaines d'octets) plutôt que versionnés : aucune donnée privée possible, et le corpus s'enrichit par ajout de cas de test.
- **Heure sans décalage** : une heure EXIF sans `OffsetTime` est interprétée dans le fuseau du PC au moment d'écrire les dates du fichier. Le décalage horaire par lot (photos du Japon) arrive en phase 3.
- **Empreinte d'idempotence** : taille + date de modification de la source (le SHA-256 arrive avec les doublons, phase 5).

---

## Structure des fichiers (cible de la phase 0)

```
PhotoApp.Core/                        (aucun package)
  AppDataPaths.cs                     racines de données (§9)
  Models/ImageDateCandidate.cs        heure murale + décalage optionnel
  Models/ImageDateMetadata.cs         candidats + retenu + fiabilité
  Models/ImageDateSource.cs
  Models/ImageFileInfo.cs
  Models/PlanInput.cs, PreviousOutput.cs, RenamePlan.cs, RenameResult.cs
  Services/UtcOffsetParser.cs         "+09:00" → TimeSpan
  Services/XmpDateParser.cs           date XMP → candidat
  Services/DatePlausibility.cs        fenêtre [1990, demain], horloges remises à zéro
  Services/DateSelector.cs            choix du candidat + fiabilité
  Services/FileNameDateParser.cs      motifs de noms
  Services/NormalizedFileNameBuilder.cs   nom de base / extension
  Services/NameAllocator.cs           suffixes -01, -02 sans renumérotation
  Services/RenamePlanner.cs           plan déterministe + idempotent
  Services/SupportedImageExtensions.cs
PhotoApp.Metadata/                    MetadataExtractor
  NativeImageMetadataReader.cs, FileSystemDateReader.cs, ImageDateResolver.cs
PhotoApp.Catalog/
  ImageScanner.cs, ScanOptions.cs
PhotoApp.Operations/                  Magick.NET
  IImageConverter.cs, ImageConverter.cs, IFileTimestampApplier.cs, FileTimestampApplier.cs
  BatchRenameExecutor.cs, ProcessedManifest.cs, ConversionService.cs, ConversionSummary.cs
PhotoApp/                             Avalonia (MainWindowViewModel asynchrone)
PhotoApp.Tests/                       + TestImages.cs, TempDirectory.cs
.github/workflows/ci.yml
```

---

### Tâche 1 : Branche de travail, base de référence, remplacement d'ImageSharp par Magick.NET (défaut 8)

**Fichiers :**
- Modifier : `PhotoApp.Core/PhotoApp.Core.csproj` (ImageSharp retiré, Magick.NET ajouté), `PhotoApp.Core/Services/ImageConverter.cs` (même comportement, moteur Magick.NET)

**Interfaces :**
- Produit : `Magick.NET-Q8-AnyCPU 14.17.2`, sans vulnérabilité connue, utilisé par toutes les tâches suivantes (espace de noms `ImageMagick`).

- [ ] **Étape 1 : Créer la branche d'implémentation à partir de la branche docs**

```bash
cd /c/Users/ismail/Nextcloud/dev/photomanager
git status --short            # attendu : vide (le plan est le seul fichier nouveau : le commiter d'abord)
git add docs/superpowers/plans/2026-10-18-phase0-fondations.md
git commit -m "docs: plan d'implémentation de la phase 0" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git switch -c feat/phase0-fondations
```

- [ ] **Étape 2 : Base de référence — les 32 tests existants passent**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : `Réussi!  - Échec : 0, Réussite : 32`

- [ ] **Étape 3 : Constater la vulnérabilité**

Run : `dotnet list PhotoVisualizer.slnx package --vulnerable --include-transitive`
Attendu : une section listant `SixLabors.ImageSharp 3.1.11` avec des avis de gravité élevée.

- [ ] **Étape 4 : Remplacer ImageSharp par Magick.NET**

```bash
dotnet remove PhotoApp.Core package SixLabors.ImageSharp
dotnet add PhotoApp.Core package Magick.NET-Q8-AnyCPU --version 14.17.2
```

Dans `ImageConverter.cs`, remplacer les `using SixLabors…` par `using ImageMagick;` et la branche de conversion par :

```csharp
            using var image = new MagickImage(inputPath);
            image.Quality = (uint)quality;
            image.Write(outputPath, MagickFormat.Jpeg);
```

Puis : `dotnet list PhotoVisualizer.slnx package --vulnerable --include-transitive`
Attendu : aucun package vulnérable pour chaque projet ; `grep -rn ImageSharp --include=*.cs --include=*.csproj .` ne renvoie rien (hors `bin/` et `obj/`).

- [ ] **Étape 5 : Vérifier que tout passe**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : 32 réussis.

- [ ] **Étape 6 : Commit**

```bash
git add PhotoApp.Core docs/superpowers/plans
git commit -m "fix: remplacement d'ImageSharp vulnérable par Magick.NET" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 2 : Découpage en projets (§4)

**Fichiers :**
- Créer : `PhotoApp.Metadata/`, `PhotoApp.Catalog/`, `PhotoApp.Operations/` (csproj)
- Déplacer (git mv) : voir étape 2
- Modifier : `PhotoApp.Core/PhotoApp.Core.csproj`, `PhotoApp.Tests/PhotoApp.Tests.csproj`, `PhotoApp/PhotoApp.csproj`, `PhotoVisualizer.slnx`, `PhotoApp/ViewModels/MainWindowViewModel.cs`

**Interfaces :**
- Produit : espaces de noms `PhotoApp.Metadata` (lecteurs, `ImageDateResolver`), `PhotoApp.Catalog` (`ImageScanner`), `PhotoApp.Operations` (`BatchRenameExecutor`, `IImageConverter`, `ImageConverter`, `IFileTimestampApplier`, `FileTimestampApplier`). `PhotoApp.Core` ne conserve que modèles, `FileNameDateParser`, `NormalizedFileNameBuilder`, `RenamePlanner`, `SupportedImageExtensions`. Aucun changement de comportement.

- [ ] **Étape 1 : Créer les trois projets et les références**

```bash
cd /c/Users/ismail/Nextcloud/dev/photomanager
dotnet new classlib -n PhotoApp.Metadata -f net10.0
dotnet new classlib -n PhotoApp.Catalog -f net10.0
dotnet new classlib -n PhotoApp.Operations -f net10.0
rm PhotoApp.Metadata/Class1.cs PhotoApp.Catalog/Class1.cs PhotoApp.Operations/Class1.cs

dotnet add PhotoApp.Metadata reference PhotoApp.Core/PhotoApp.Core.csproj
dotnet add PhotoApp.Catalog reference PhotoApp.Core/PhotoApp.Core.csproj
dotnet add PhotoApp.Operations reference PhotoApp.Core/PhotoApp.Core.csproj

dotnet add PhotoApp.Metadata package MetadataExtractor --version 2.9.3
dotnet add PhotoApp.Operations package Magick.NET-Q8-AnyCPU --version 14.17.2
dotnet remove PhotoApp.Core package MetadataExtractor
dotnet remove PhotoApp.Core package Magick.NET-Q8-AnyCPU

dotnet sln PhotoVisualizer.slnx add PhotoApp.Metadata PhotoApp.Catalog PhotoApp.Operations

dotnet add PhotoApp reference PhotoApp.Metadata PhotoApp.Catalog PhotoApp.Operations
dotnet add PhotoApp.Tests reference PhotoApp.Metadata PhotoApp.Catalog PhotoApp.Operations
```

Vérifier que `PhotoApp.Operations` référence bien `Magick.NET-Q8-AnyCPU 14.17.2` (`grep Magick PhotoApp.Operations/*.csproj`).

- [ ] **Étape 2 : Déplacer les fichiers et changer les espaces de noms**

```bash
git mv PhotoApp.Core/Services/NativeImageMetadataReader.cs PhotoApp.Core/Services/FileSystemDateReader.cs PhotoApp.Core/Services/ImageDateResolver.cs PhotoApp.Metadata/
git mv PhotoApp.Core/Services/ImageScanner.cs PhotoApp.Catalog/
git mv PhotoApp.Core/Services/BatchRenameExecutor.cs PhotoApp.Core/Services/FileTimestampApplier.cs PhotoApp.Core/Services/IFileTimestampApplier.cs PhotoApp.Core/Services/IImageConverter.cs PhotoApp.Core/Services/ImageConverter.cs PhotoApp.Operations/

sed -i 's/^namespace PhotoApp.Core.Services;/namespace PhotoApp.Metadata;/' PhotoApp.Metadata/*.cs
sed -i 's/^namespace PhotoApp.Core.Services;/namespace PhotoApp.Catalog;/' PhotoApp.Catalog/*.cs
sed -i 's/^namespace PhotoApp.Core.Services;/namespace PhotoApp.Operations;/' PhotoApp.Operations/*.cs

# ImageDateResolver et ImageScanner utilisent encore des types de Core.Services
sed -i 's/^using PhotoApp.Core.Models;/using PhotoApp.Core.Models;\nusing PhotoApp.Core.Services;/' PhotoApp.Metadata/ImageDateResolver.cs PhotoApp.Catalog/ImageScanner.cs
```

- [ ] **Étape 3 : Adapter les tests et l'application**

Dans `PhotoApp.Tests/PhotoApp.Tests.csproj`, remplacer le bloc `<ItemGroup><Using Include="Xunit" /></ItemGroup>` par :

```xml
  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="PhotoApp.Metadata" />
    <Using Include="PhotoApp.Catalog" />
    <Using Include="PhotoApp.Operations" />
  </ItemGroup>
```

Dans `PhotoApp/ViewModels/MainWindowViewModel.cs`, après `using PhotoApp.Core.Models;` ajouter :

```csharp
using PhotoApp.Catalog;
using PhotoApp.Metadata;
using PhotoApp.Operations;
```

et remplacer `new PhotoApp.Core.Services.BatchRenameExecutor()` par `new BatchRenameExecutor()`.

- [ ] **Étape 4 : Compiler et tester**

Run : `dotnet build PhotoVisualizer.slnx && dotnet test PhotoVisualizer.slnx --no-build`
Attendu : build sans erreur ; 32 tests réussis. Si une erreur de nom ambigu apparaît (`Directory`), qualifier avec `System.IO.Directory` / `MetadataExtractor.Directory` comme le fait déjà le code existant.

Vérifier aussi que `PhotoApp.Core.csproj` ne contient plus aucun `PackageReference` (contrainte « Core pur »).

- [ ] **Étape 5 : Commit**

```bash
git add -A
git commit -m "refactor: découpage en projets Core, Metadata, Catalog, Operations" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 3 : Racines de données centralisées (§9)

**Fichiers :**
- Créer : `PhotoApp.Core/AppDataPaths.cs`
- Test : `PhotoApp.Tests/AppDataPathsTests.cs`

**Interfaces :**
- Produit :
  - `enum PlatformKind { Windows, Linux, MacOS }`
  - `AppDataPaths.Resolve(PlatformKind platform, Func<string, string?> getEnv, string home) : AppDataPaths`
  - `AppDataPaths.ForCurrentUser() : AppDataPaths`
  - propriétés `DataRoot`, `CacheRoot`, `ManifestsFolder`, `LogsFolder`, `DatabasePath`, `ThumbnailsFolder`
  - `string ManifestPathFor(string outputFolder)` : `<ManifestsFolder>/<16 hex du SHA-256 du chemin complet en minuscules>.json`
  - `void EnsureCreated()`

- [ ] **Étape 1 : Écrire les tests qui échouent**

```csharp
using PhotoApp.Core;

namespace PhotoApp.Tests;

public sealed class AppDataPathsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Windows_UsesLocalAppData()
    {
        var env = new Dictionary<string, string> { ["LOCALAPPDATA"] = @"C:\Users\x\AppData\Local" };

        var paths = AppDataPaths.Resolve(PlatformKind.Windows, k => env.GetValueOrDefault(k), @"C:\Users\x");

        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Local", "PhotoManager"), paths.DataRoot);
        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Local", "PhotoManager", "cache"), paths.CacheRoot);
    }

    [Fact]
    public void Linux_UsesXdgWhenSet()
    {
        var env = new Dictionary<string, string> { ["XDG_DATA_HOME"] = "/data", ["XDG_CACHE_HOME"] = "/cache" };

        var paths = AppDataPaths.Resolve(PlatformKind.Linux, k => env.GetValueOrDefault(k), "/home/x");

        Assert.Equal(Path.Combine("/data", "photomanager"), paths.DataRoot);
        Assert.Equal(Path.Combine("/cache", "photomanager"), paths.CacheRoot);
    }

    [Fact]
    public void Linux_FallsBackToHomeDefaults()
    {
        var paths = AppDataPaths.Resolve(PlatformKind.Linux, NoEnv, "/home/x");

        Assert.Equal(Path.Combine("/home/x", ".local", "share", "photomanager"), paths.DataRoot);
        Assert.Equal(Path.Combine("/home/x", ".cache", "photomanager"), paths.CacheRoot);
    }

    [Fact]
    public void MacOS_UsesLibraryFolders()
    {
        var paths = AppDataPaths.Resolve(PlatformKind.MacOS, NoEnv, "/Users/x");

        Assert.Equal(Path.Combine("/Users/x", "Library", "Application Support", "PhotoManager"), paths.DataRoot);
        Assert.Equal(Path.Combine("/Users/x", "Library", "Caches", "PhotoManager"), paths.CacheRoot);
    }

    [Fact]
    public void ManifestPathFor_IsStableAndPerOutputFolder()
    {
        var paths = AppDataPaths.Resolve(PlatformKind.Linux, NoEnv, "/home/x");

        var a1 = paths.ManifestPathFor("/photos/a/Converted");
        var a2 = paths.ManifestPathFor("/photos/a/Converted");
        var b = paths.ManifestPathFor("/photos/b/Converted");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.StartsWith(paths.ManifestsFolder, a1);
        Assert.EndsWith(".json", a1);
    }

    [Fact]
    public void EnsureCreated_CreatesTheFolders()
    {
        using var temp = new TempDirectory();
        var paths = AppDataPaths.Resolve(PlatformKind.Linux, k => k == "XDG_DATA_HOME" ? temp.Combine("d") : k == "XDG_CACHE_HOME" ? temp.Combine("c") : null, "/home/x");

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.ManifestsFolder));
        Assert.True(Directory.Exists(paths.LogsFolder));
        Assert.True(Directory.Exists(paths.ThumbnailsFolder));
    }
}
```

Créer aussi `PhotoApp.Tests/TempDirectory.cs` (utilisé par toutes les tâches suivantes) :

```csharp
namespace PhotoApp.Tests;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "photoapp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([FullPath, .. parts]);

    public void Dispose()
    {
        if (Directory.Exists(FullPath))
        {
            Directory.Delete(FullPath, recursive: true);
        }
    }
}
```

- [ ] **Étape 2 : Vérifier l'échec**

Run : `dotnet test PhotoApp.Tests --filter AppDataPathsTests`
Attendu : ÉCHEC de compilation (`AppDataPaths` n'existe pas).

- [ ] **Étape 3 : Implémenter**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace PhotoApp.Core;

public enum PlatformKind
{
    Windows,
    Linux,
    MacOS
}

/// <summary>Point unique de résolution des dossiers de l'application (spec §9).</summary>
public sealed class AppDataPaths
{
    private AppDataPaths(string dataRoot, string cacheRoot)
    {
        DataRoot = dataRoot;
        CacheRoot = cacheRoot;
    }

    public string DataRoot { get; }

    public string CacheRoot { get; }

    public string ManifestsFolder => Path.Combine(DataRoot, "manifests");

    public string LogsFolder => Path.Combine(DataRoot, "logs");

    public string DatabasePath => Path.Combine(DataRoot, "catalog.db");

    public string ThumbnailsFolder => Path.Combine(CacheRoot, "thumbnails");

    public static AppDataPaths ForCurrentUser()
    {
        var platform = OperatingSystem.IsWindows() ? PlatformKind.Windows
            : OperatingSystem.IsMacOS() ? PlatformKind.MacOS
            : PlatformKind.Linux;

        return Resolve(
            platform,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public static AppDataPaths Resolve(PlatformKind platform, Func<string, string?> getEnv, string home)
    {
        switch (platform)
        {
            case PlatformKind.Windows:
            {
                var local = getEnv("LOCALAPPDATA");
                if (string.IsNullOrWhiteSpace(local))
                {
                    local = Path.Combine(home, "AppData", "Local");
                }

                var root = Path.Combine(local, "PhotoManager");
                return new AppDataPaths(root, Path.Combine(root, "cache"));
            }
            case PlatformKind.MacOS:
                return new AppDataPaths(
                    Path.Combine(home, "Library", "Application Support", "PhotoManager"),
                    Path.Combine(home, "Library", "Caches", "PhotoManager"));
            default:
            {
                var data = getEnv("XDG_DATA_HOME");
                var cache = getEnv("XDG_CACHE_HOME");
                return new AppDataPaths(
                    Path.Combine(string.IsNullOrWhiteSpace(data) ? Path.Combine(home, ".local", "share") : data, "photomanager"),
                    Path.Combine(string.IsNullOrWhiteSpace(cache) ? Path.Combine(home, ".cache") : cache, "photomanager"));
            }
        }
    }

    public string ManifestPathFor(string outputFolder)
    {
        var normalized = Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Path.Combine(ManifestsFolder, Convert.ToHexString(hash, 0, 8).ToLowerInvariant() + ".json");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ManifestsFolder);
        Directory.CreateDirectory(LogsFolder);
        Directory.CreateDirectory(ThumbnailsFolder);
    }
}
```

Note : `Directory.CreateDirectory` est une E/S ; elle reste dans `AppDataPaths` car c'est la classe qui possède les racines. `Core` n'ouvre aucun fichier de photo.

- [ ] **Étape 4 : Vérifier le succès**

Run : `dotnet test PhotoApp.Tests --filter AppDataPathsTests`
Attendu : 6 réussis.

- [ ] **Étape 5 : Commit**

```bash
git add PhotoApp.Core/AppDataPaths.cs PhotoApp.Tests/AppDataPathsTests.cs PhotoApp.Tests/TempDirectory.cs
git commit -m "feat: racines de données centralisées (AppDataPaths)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 4 : Heure murale au lieu d'un faux UTC (défaut 3)

Cette tâche change le modèle de date et porte tout le code existant dessus. Le comportement de choix (natif > nom > système) et le nommage (hash) restent provisoirement ceux d'avant ; les tâches 5 à 7 les remplacent.

**Fichiers :**
- Créer : `PhotoApp.Core/Services/UtcOffsetParser.cs`, `PhotoApp.Core/Services/XmpDateParser.cs`, `PhotoApp.Tests/TestImages.cs`
- Modifier : `PhotoApp.Core/Models/ImageDateCandidate.cs`, `ImageDateMetadata.cs`, `RenamePlan.cs`, `PhotoApp.Core/Services/FileNameDateParser.cs`, `NormalizedFileNameBuilder.cs`, `RenamePlanner.cs`, `PhotoApp.Metadata/NativeImageMetadataReader.cs`, `FileSystemDateReader.cs`, `ImageDateResolver.cs`, `PhotoApp.Operations/BatchRenameExecutor.cs`, `PhotoApp/ViewModels/MainWindowViewModel.cs`
- Test : `UtcOffsetParserTests.cs`, `XmpDateParserTests.cs`, `NativeImageMetadataReaderTests.cs` (nouveaux) ; `FileNameDateParserTests.cs`, `ImageDateResolverTests.cs`, `NormalizedFileNameBuilderTests.cs`, `BatchRenameExecutorTests.cs` (adaptés)

**Interfaces :**
- Produit :
  - `ImageDateCandidate(DateTime wallClock, bool hasTime, TimeSpan? offset = null)` avec `WallClock` (Kind `Unspecified`), `HasTime`, `Offset`, `DateTimeOffset ToInstant(TimeZoneInfo fallbackZone)`
  - `UtcOffsetParser.TryParse(string? text, out TimeSpan offset)` : accepte `+09:00`, `-0330`, `Z`
  - `XmpDateParser.Parse(string? text) : ImageDateCandidate?`
  - `ImageDateMetadata(ImageDateCandidate? native, ImageDateCandidate? fileName, ImageDateCandidate? fileSystem, ImageDateCandidate? retained, ImageDateSource retainedSource, string? normalizedFileNamePreview)` avec `NativeCandidate`, `FileNameCandidate`, `FileSystemCandidate`, `RetainedCandidate`, `RetainedSource`, `NormalizedFileNamePreview`, `RetainedDateHasTime`, `RetainedDateText`, `RetainedSourceText`
  - `RenamePlan(string SourcePath, string TargetName, ImageDateCandidate Date)`
  - `NormalizedFileNameBuilder.Build(ImageDateCandidate date, string extension, string sourceIdentifier, string? collisionToken = null)` et `Build(ImageDateMetadata, …)`
  - `RenamePlanner.PlanRenames(IReadOnlyList<(string sourcePath, ImageDateCandidate date, string extension)>, string outputFolder)`

- [ ] **Étape 1 : Tests des nouveaux utilitaires (échec attendu)**

`PhotoApp.Tests/UtcOffsetParserTests.cs` :

```csharp
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class UtcOffsetParserTests
{
    [Theory]
    [InlineData("+09:00", 9, 0)]
    [InlineData("-03:30", -3, -30)]
    [InlineData("+0200", 2, 0)]
    [InlineData("Z", 0, 0)]
    [InlineData(" +01:00 ", 1, 0)]
    public void TryParse_AcceptsValidOffsets(string text, int hours, int minutes)
    {
        Assert.True(UtcOffsetParser.TryParse(text, out var offset));
        Assert.Equal(new TimeSpan(hours, minutes, 0), offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paris")]
    [InlineData("+25:00")]
    [InlineData("+01:75")]
    public void TryParse_RejectsInvalidOffsets(string? text)
    {
        Assert.False(UtcOffsetParser.TryParse(text, out _));
    }
}
```

`PhotoApp.Tests/XmpDateParserTests.cs` :

```csharp
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class XmpDateParserTests
{
    [Fact]
    public void Parse_KeepsWallClockAndOffset()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15+09:00");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Equal(TimeSpan.FromHours(9), result.Offset);
        Assert.True(result.HasTime);
    }

    [Fact]
    public void Parse_WithoutZone_IsFloating()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Null(result.Offset);
    }

    [Fact]
    public void Parse_Z_MeansZeroOffset_WithoutConvertingTheClock()
    {
        var result = XmpDateParser.Parse("2023-03-04T13:20:15Z");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Equal(TimeSpan.Zero, result.Offset);
    }

    [Fact]
    public void Parse_DateOnly_HasNoTimeAndNoOffset()
    {
        var result = XmpDateParser.Parse("2023-03-04");

        Assert.NotNull(result);
        Assert.False(result.HasTime);
        Assert.Null(result.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a date")]
    [InlineData("2023-13-40T00:00:00")]
    public void Parse_ReturnsNull_ForInvalidText(string? text)
    {
        Assert.Null(XmpDateParser.Parse(text));
    }
}
```

`PhotoApp.Tests/TestImages.cs` :

```csharp
using ImageMagick;

namespace PhotoApp.Tests;

/// <summary>Génère de petites images d'exemple : le corpus de test ne contient aucune donnée privée.</summary>
public static class TestImages
{
    public static void Write(string path, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 8);
        image.Write(path, format);
    }

    public static void WritePng(string path) => Write(path, MagickFormat.Png);

    public static void WriteJpeg(string path, string? exifDateTimeOriginal = null, string? exifOffsetTimeOriginal = null)
    {
        using var image = new MagickImage(MagickColors.Gray, 8, 8);
        if (exifDateTimeOriginal is not null || exifOffsetTimeOriginal is not null)
        {
            var exif = new ExifProfile();
            if (exifDateTimeOriginal is not null)
            {
                exif.SetValue(ExifTag.DateTimeOriginal, exifDateTimeOriginal);
            }

            if (exifOffsetTimeOriginal is not null)
            {
                exif.SetValue(ExifTag.OffsetTimeOriginal, exifOffsetTimeOriginal);
            }

            image.SetProfile(exif);
        }

        image.Write(path, MagickFormat.Jpeg);
    }
}
```

`PhotoApp.Tests/NativeImageMetadataReaderTests.cs` :

```csharp
namespace PhotoApp.Tests;

public sealed class NativeImageMetadataReaderTests
{
    [Fact]
    public void Read_ExifLocalTime_IsKeptAsWallClock_NotShiftedToUtc()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15");

        var result = new NativeImageMetadataReader().Read(path);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), result.WallClock);
        Assert.Null(result.Offset);
        Assert.True(result.HasTime);
    }

    [Fact]
    public void Read_ExifOffsetTime_IsReturned()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.jpg");
        TestImages.WriteJpeg(path, "2023:03:04 13:20:15", "+09:00");

        var result = new NativeImageMetadataReader().Read(path);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(9), result.Offset);
    }

    [Fact]
    public void Read_ImageWithoutMetadata_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("photo.png");
        TestImages.WritePng(path);

        Assert.Null(new NativeImageMetadataReader().Read(path));
    }
}
```

Run : `dotnet build PhotoApp.Tests` — Attendu : ÉCHEC (types absents).

- [ ] **Étape 2 : Implémenter les utilitaires**

`PhotoApp.Core/Services/UtcOffsetParser.cs` :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoApp.Core.Services;

public static class UtcOffsetParser
{
    private static readonly Regex Pattern = new(
        "^(?<sign>[+-])(?<hours>\\d{2}):?(?<minutes>\\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out TimeSpan offset)
    {
        offset = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed is "Z" or "z")
        {
            offset = TimeSpan.Zero;
            return true;
        }

        var match = Pattern.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var hours = int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
        if (hours > 14 || minutes > 59)
        {
            return false;
        }

        var value = new TimeSpan(hours, minutes, 0);
        offset = match.Groups["sign"].Value == "-" ? -value : value;
        return true;
    }
}
```

`PhotoApp.Core/Services/XmpDateParser.cs` :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>Lit une date XMP en conservant l'heure écrite : aucune conversion en UTC.</summary>
public static class XmpDateParser
{
    private static readonly Regex Pattern = new(
        "^(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})(?:[T ](?<hour>\\d{2}):(?<minute>\\d{2})(?::(?<second>\\d{2}))?(?:[.,]\\d+)?)?\\s*(?<zone>Z|[+-]\\d{2}:?\\d{2})?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ImageDateCandidate? Parse(string? text)
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
        try
        {
            var wallClock = new DateTime(
                Number(match, "year"),
                Number(match, "month"),
                Number(match, "day"),
                hasTime ? Number(match, "hour") : 0,
                hasTime ? Number(match, "minute") : 0,
                hasTime && match.Groups["second"].Success ? Number(match, "second") : 0,
                DateTimeKind.Unspecified);

            TimeSpan? offset = null;
            if (hasTime && match.Groups["zone"].Success && UtcOffsetParser.TryParse(match.Groups["zone"].Value, out var parsed))
            {
                offset = parsed;
            }

            return new ImageDateCandidate(wallClock, hasTime, offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
```

- [ ] **Étape 3 : Nouveau modèle de candidat et de métadonnées**

`PhotoApp.Core/Models/ImageDateCandidate.cs` (remplacer tout) :

```csharp
namespace PhotoApp.Core.Models;

/// <summary>
/// Date lue dans une source. Elle garde l'heure murale telle qu'écrite (heure locale du lieu de prise de vue),
/// avec un décalage UTC seulement quand la source le fournit.
/// </summary>
public sealed class ImageDateCandidate
{
    public ImageDateCandidate(DateTime wallClock, bool hasTime, TimeSpan? offset = null)
    {
        WallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        HasTime = hasTime;
        Offset = offset;
    }

    public DateTime WallClock { get; }

    public bool HasTime { get; }

    public TimeSpan? Offset { get; }

    /// <summary>Instant réel : le décalage connu, sinon celui du fuseau de repli à cette date.</summary>
    public DateTimeOffset ToInstant(TimeZoneInfo fallbackZone)
    {
        var offset = Offset ?? fallbackZone.GetUtcOffset(WallClock);
        return new DateTimeOffset(WallClock, offset);
    }
}
```

`PhotoApp.Core/Models/ImageDateMetadata.cs` (remplacer tout) :

```csharp
namespace PhotoApp.Core.Models;

public sealed class ImageDateMetadata
{
    public ImageDateMetadata(
        ImageDateCandidate? native,
        ImageDateCandidate? fileName,
        ImageDateCandidate? fileSystem,
        ImageDateCandidate? retained,
        ImageDateSource retainedSource,
        string? normalizedFileNamePreview)
    {
        NativeCandidate = native;
        FileNameCandidate = fileName;
        FileSystemCandidate = fileSystem;
        RetainedCandidate = retained;
        RetainedSource = retainedSource;
        NormalizedFileNamePreview = normalizedFileNamePreview;
    }

    public ImageDateCandidate? NativeCandidate { get; }

    public ImageDateCandidate? FileNameCandidate { get; }

    public ImageDateCandidate? FileSystemCandidate { get; }

    public ImageDateCandidate? RetainedCandidate { get; }

    public ImageDateSource RetainedSource { get; }

    public bool RetainedDateHasTime => RetainedCandidate?.HasTime ?? false;

    public string? NormalizedFileNamePreview { get; }

    public string RetainedDateText => RetainedCandidate is { } retained
        ? retained.WallClock.ToString(retained.HasTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        : "—";

    public string RetainedSourceText => RetainedSource switch
    {
        ImageDateSource.NativeMetadata => "Métadonnées natives",
        ImageDateSource.FileName => "Nom de fichier",
        ImageDateSource.FileSystem => "Système de fichiers",
        _ => "Aucune"
    };
}
```

`PhotoApp.Core/Models/RenamePlan.cs` (remplacer tout) :

```csharp
namespace PhotoApp.Core.Models;

public sealed record RenamePlan(
    string SourcePath,
    string TargetName,
    ImageDateCandidate Date);
```

- [ ] **Étape 4 : Porter le code existant**

`PhotoApp.Metadata/NativeImageMetadataReader.cs` — remplacer `ReadExifDate`, `ReadXmpDate`, `TryReadXmpProperty`, `ToUtcOffset` par (garder `Read` et les `using`, supprimer `using System.Globalization;`) :

```csharp
    // OffsetTimeOriginal / OffsetTimeDigitized (EXIF 2.31), lus en texte : "+09:00".
    private const int TagOffsetTimeOriginal = 0x9011;
    private const int TagOffsetTimeDigitized = 0x9012;

    // Dates de prise de vue uniquement : xmp:ModifyDate et xmp:MetadataDate sont des dates d'édition.
    private static readonly string[] XmpDateKeys = ["exif:DateTimeOriginal", "photoshop:DateCreated", "xmp:CreateDate"];

    private static ImageDateCandidate? ReadExifDate(IEnumerable<MetadataExtractor.Directory> directories)
    {
        foreach (var exifDirectory in directories.OfType<ExifSubIfdDirectory>())
        {
            if (exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
            {
                return new ImageDateCandidate(original, true, ReadOffset(exifDirectory, TagOffsetTimeOriginal));
            }

            if (exifDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
            {
                return new ImageDateCandidate(digitized, true, ReadOffset(exifDirectory, TagOffsetTimeDigitized));
            }
        }

        return null;
    }

    private static TimeSpan? ReadOffset(ExifSubIfdDirectory directory, int tag) =>
        UtcOffsetParser.TryParse(directory.GetString(tag), out var offset) ? offset : null;

    private static ImageDateCandidate? ReadXmpDate(IEnumerable<MetadataExtractor.Directory> directories)
    {
        foreach (var xmpDirectory in directories.OfType<XmpDirectory>())
        {
            var properties = xmpDirectory.GetXmpProperties();
            foreach (var key in XmpDateKeys)
            {
                if (properties.TryGetValue(key, out var value) && XmpDateParser.Parse(value) is { } candidate)
                {
                    return candidate;
                }
            }
        }

        return null;
    }
```

Ajouter `using PhotoApp.Core.Services;` en tête.

`PhotoApp.Metadata/FileSystemDateReader.cs` — remplacer la ligne du `return new ImageDateCandidate(...)` par :

```csharp
            var wallClock = fileInfo.LastWriteTime;
            return new ImageDateCandidate(wallClock, true, TimeZoneInfo.Local.GetUtcOffset(wallClock));
```

`PhotoApp.Metadata/ImageDateResolver.cs` — remplacer le corps de `Resolve` par :

```csharp
        var native = _nativeImageMetadataReader.Read(filePath);
        var fromName = _fileNameDateParser.Parse(Path.GetFileNameWithoutExtension(filePath));
        var fromFileSystem = _fileSystemDateReader.Read(filePath);

        var retained = native ?? fromName ?? fromFileSystem;
        var source = native is not null ? ImageDateSource.NativeMetadata
            : fromName is not null ? ImageDateSource.FileName
            : fromFileSystem is not null ? ImageDateSource.FileSystem
            : ImageDateSource.None;

        var preview = retained is null
            ? null
            : NormalizedFileNameBuilder.Build(retained, Path.GetExtension(filePath), filePath);

        return new ImageDateMetadata(native, fromName, fromFileSystem, retained, source, preview);
```

`PhotoApp.Core/Services/FileNameDateParser.cs` — remplacer les trois endroits qui fabriquent des dates :
- dans `Parse` : `return new ImageDateCandidate(DateTime.SpecifyKind(dateOnly, DateTimeKind.Unspecified), false);`
- dans `TryParseTimePatterns` : `TryBuildDate(match, out var dateTimeOffset)` devient `TryBuildDate(match, out var wallClock)` et le retour `new ImageDateCandidate(wallClock, true)`
- `TryBuildDate` : signature `out DateTime wallClock`, `wallClock = default;` dans les échecs, et dans le `try` : `wallClock = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified); return true;`

`PhotoApp.Core/Services/NormalizedFileNameBuilder.cs` — première surcharge :

```csharp
    public static string Build(ImageDateCandidate date, string extension, string sourceIdentifier, string? collisionToken = null)
    {
        var extensionValue = NormalizeExtension(extension);
        var baseName = date.HasTime
            ? $"IMG-{date.WallClock:yyyyMMdd-HHmmss}"
            : $"IMG-{date.WallClock:yyyyMMdd}";

        if (!date.HasTime || !string.IsNullOrWhiteSpace(collisionToken))
        {
            var token = collisionToken ?? GenerateShortHash(sourceIdentifier);
            baseName += $"-{token}";
        }

        return baseName + extensionValue;
    }

    public static string? Build(ImageDateMetadata metadata, string extension, string sourceIdentifier, string? collisionToken = null)
    {
        return metadata.RetainedCandidate is null
            ? null
            : Build(metadata.RetainedCandidate, extension, sourceIdentifier, collisionToken);
    }
```

`PhotoApp.Core/Services/RenamePlanner.cs` — signature `IReadOnlyList<(string sourcePath, ImageDateCandidate date, string extension)> imageData` ; dans la boucle : `foreach (var (sourcePath, date, extension) in imageData)`, `NormalizedFileNameBuilder.Build(date, extension, sourcePath)`, `new RenamePlan(sourcePath, targetName, date)` ; dans le bloc de collision remplacer `plan.HasTime` par `plan.Date.HasTime` et `plan.RetainedDate.DateTime` par `plan.Date.WallClock`.

`PhotoApp.Operations/BatchRenameExecutor.cs` — la ligne `_timestampApplier.ApplyTimestamps(jpegPath, plan.RetainedDate);` devient :

```csharp
            _timestampApplier.ApplyTimestamps(jpegPath, plan.Date.ToInstant(TimeZoneInfo.Local));
```

`PhotoApp/ViewModels/MainWindowViewModel.cs` — dans `ConvertAsync`, remplacer la construction de `imageData` par :

```csharp
            var imageData = new List<(string sourcePath, ImageDateCandidate date, string extension)>();
            foreach (var img in Images)
            {
                if (img.DateMetadata?.RetainedCandidate is { } date)
                {
                    imageData.Add((img.FullPath, date, img.Extension));
                }
            }
```

- [ ] **Étape 5 : Adapter les tests existants**

`FileNameDateParserTests.cs` : remplacer l'assertion finale par
`Assert.Equal(new DateTime(year, month, day, hour, minute, second), result.WallClock);`

`NormalizedFileNameBuilderTests.cs` : construire `var date = new ImageDateCandidate(new DateTime(year, month, day, hour, minute, second), hasTime);` et appeler `NormalizedFileNameBuilder.Build(date, Path.GetExtension(filename), filename)` (ajouter `using PhotoApp.Core.Models;`).

`ImageDateResolverTests.cs` : remplacer chaque `new ImageDateCandidate(new DateTimeOffset(new DateTime(Y, M, D, h, m, s, DateTimeKind.Utc)), b)` par `new ImageDateCandidate(new DateTime(Y, M, D, h, m, s), b)`, et les assertions `result.OriginalDate?.UtcDateTime` / `CreateDate` / `ModifyDate` / `RetainedDate` / `RetainedDateHasTime` par `result.NativeCandidate?.WallClock` / `FileNameCandidate?.WallClock` / `FileSystemCandidate?.WallClock` / `RetainedCandidate?.WallClock` / `RetainedDateHasTime` (valeurs attendues en `DateTime` sans `Kind`).

`BatchRenameExecutorTests.cs` : définir en tête de classe
`private static readonly ImageDateCandidate Noon = new(new DateTime(2023, 1, 1, 12, 0, 0), true, TimeSpan.Zero);`
remplacer chaque `new RenamePlan(src, tgt, <date>, true)` par `new RenamePlan(src, tgt, Noon)` (ajouter `using PhotoApp.Core.Models;` déjà présent) et la vérification par
`mockApplier.Verify(a => a.ApplyTimestamps(It.IsAny<string>(), new DateTimeOffset(2023, 1, 1, 12, 0, 0, TimeSpan.Zero)), Times.Once);`

- [ ] **Étape 6 : Tout vérifier**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : tous les tests passent (32 adaptés + nouveaux). Si `ExifTag.OffsetTimeOriginal` n'existe pas dans Magick.NET, chercher le nom exact dans `ImageMagick.ExifTag` (balise 0x9011) et noter l'écart.

- [ ] **Étape 7 : Commit**

```bash
git add -A
git commit -m "fix: heure murale + décalage au lieu d'un faux UTC (EXIF, XMP, nom, fichier)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 5 : Dates non fiables jamais appliquées (défaut 4)

**Fichiers :**
- Créer : `PhotoApp.Core/Services/DatePlausibility.cs`, `PhotoApp.Core/Services/DateSelector.cs`
- Modifier : `PhotoApp.Core/Models/ImageDateMetadata.cs`, `ImageFileInfo.cs`, `PhotoApp.Metadata/ImageDateResolver.cs`, `PhotoApp/Views/MainWindow.axaml`
- Test : `DatePlausibilityTests.cs`, `DateSelectorTests.cs`

**Interfaces :**
- Consomme : `ImageDateCandidate`, `ImageDateSource` (tâche 4).
- Produit :
  - `DatePlausibility.IsPlausible(ImageDateCandidate candidate, DateTime utcNow) : bool` — faux hors de [1990-01-01, utcNow + 1 jour] et pour les horloges remises à zéro (`2000-01-01 00:00:00`, `1970-01-01 00:00:00`)
  - `DateSelector.Select(ImageDateCandidate? native, ImageDateCandidate? fileName, ImageDateCandidate? fileSystem, DateTime utcNow) : (ImageDateCandidate? Candidate, ImageDateSource Source)` — natif > nom > système, en ignorant les candidats implausibles
  - `DateSelector.IsReliable(ImageDateSource source) : bool` — vrai pour `NativeMetadata` et `FileName` seulement
  - `ImageDateMetadata.IsReliable`, `ImageDateMetadata.StatusText`, `ImageFileInfo.StatusText`

- [ ] **Étape 1 : Tests qui échouent**

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class DatePlausibilityTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    [Theory]
    [InlineData(2023, 3, 4, true)]
    [InlineData(1990, 1, 1, true)]
    [InlineData(1989, 12, 31, false)]
    [InlineData(2026, 10, 19, true)]   // demain : toléré
    [InlineData(2026, 10, 20, false)]  // après-demain : refusé
    public void IsPlausible_AppliesTheWindow(int year, int month, int day, bool expected)
    {
        var candidate = new ImageDateCandidate(new DateTime(year, month, day, 10, 0, 0), true);

        Assert.Equal(expected, DatePlausibility.IsPlausible(candidate, Now));
    }

    [Theory]
    [InlineData(2000, 1, 1)]
    [InlineData(1970, 1, 1)]
    public void IsPlausible_RejectsResetClocks(int year, int month, int day)
    {
        var candidate = new ImageDateCandidate(new DateTime(year, month, day, 0, 0, 0), true);

        Assert.False(DatePlausibility.IsPlausible(candidate, Now));
    }
}
```

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class DateSelectorTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0);

    private static ImageDateCandidate At(int year, int month, int day, bool hasTime = true) =>
        new(new DateTime(year, month, day, 10, 0, 0), hasTime);

    [Fact]
    public void Select_PrefersNative_ThenFileName_ThenFileSystem()
    {
        var native = At(2024, 1, 2);
        var name = At(2025, 2, 3);
        var fs = At(2026, 3, 4);

        Assert.Equal((native, ImageDateSource.NativeMetadata), DateSelector.Select(native, name, fs, Now));
        Assert.Equal((name, ImageDateSource.FileName), DateSelector.Select(null, name, fs, Now));
        Assert.Equal((fs, ImageDateSource.FileSystem), DateSelector.Select(null, null, fs, Now));
        Assert.Equal((null, ImageDateSource.None), DateSelector.Select(null, null, null, Now));
    }

    [Fact]
    public void Select_SkipsImplausibleCandidates()
    {
        var resetClock = new ImageDateCandidate(new DateTime(2000, 1, 1, 0, 0, 0), true);
        var name = At(2025, 2, 3);

        var (candidate, source) = DateSelector.Select(resetClock, name, null, Now);

        Assert.Same(name, candidate);
        Assert.Equal(ImageDateSource.FileName, source);
    }

    [Theory]
    [InlineData(ImageDateSource.NativeMetadata, true)]
    [InlineData(ImageDateSource.FileName, true)]
    [InlineData(ImageDateSource.FileSystem, false)]
    [InlineData(ImageDateSource.None, false)]
    public void IsReliable_ExcludesTheFileSystemDate(ImageDateSource source, bool expected)
    {
        Assert.Equal(expected, DateSelector.IsReliable(source));
    }
}
```

Run : `dotnet build PhotoApp.Tests` — Attendu : ÉCHEC (types absents).

- [ ] **Étape 2 : Implémenter**

`PhotoApp.Core/Services/DatePlausibility.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class DatePlausibility
{
    private static readonly DateTime WindowStart = new(1990, 1, 1);

    private static readonly DateTime[] ResetClocks =
    [
        new(2000, 1, 1, 0, 0, 0),
        new(1970, 1, 1, 0, 0, 0)
    ];

    public static bool IsPlausible(ImageDateCandidate candidate, DateTime utcNow)
    {
        var date = candidate.WallClock;
        if (date < WindowStart || date > utcNow.AddDays(1))
        {
            return false;
        }

        return !ResetClocks.Contains(date);
    }
}
```

`PhotoApp.Core/Services/DateSelector.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class DateSelector
{
    public static (ImageDateCandidate? Candidate, ImageDateSource Source) Select(
        ImageDateCandidate? native,
        ImageDateCandidate? fileName,
        ImageDateCandidate? fileSystem,
        DateTime utcNow)
    {
        if (IsUsable(native, utcNow))
        {
            return (native, ImageDateSource.NativeMetadata);
        }

        if (IsUsable(fileName, utcNow))
        {
            return (fileName, ImageDateSource.FileName);
        }

        if (IsUsable(fileSystem, utcNow))
        {
            return (fileSystem, ImageDateSource.FileSystem);
        }

        return (null, ImageDateSource.None);
    }

    /// <summary>La date de modification du fichier est la date de copie : jamais fiable.</summary>
    public static bool IsReliable(ImageDateSource source) =>
        source is ImageDateSource.NativeMetadata or ImageDateSource.FileName;

    private static bool IsUsable(ImageDateCandidate? candidate, DateTime utcNow) =>
        candidate is not null && DatePlausibility.IsPlausible(candidate, utcNow);
}
```

`ImageDateMetadata` — ajouter :

```csharp
    public bool IsReliable => DateSelector.IsReliable(RetainedSource);

    public string StatusText => RetainedSource switch
    {
        ImageDateSource.None => "À vérifier : aucune date trouvée",
        ImageDateSource.FileSystem => "À vérifier : date non fiable (date du fichier), non appliquée",
        _ => string.Empty
    };
```
(ajouter `using PhotoApp.Core.Services;` ; `NormalizedFileNamePreview` reste tel quel dans le constructeur.)

`ImageFileInfo` — ajouter `public string StatusText => DateMetadata?.StatusText ?? string.Empty;`

`ImageDateResolver.Resolve` — remplacer le choix par :

```csharp
        var (retained, source) = DateSelector.Select(native, fromName, fromFileSystem, DateTime.UtcNow);

        var preview = retained is null || !DateSelector.IsReliable(source)
            ? null
            : NormalizedFileNameBuilder.Build(retained, Path.GetExtension(filePath), filePath);
```

`MainWindow.axaml` — sous la ligne `Nom cible: {0}` ajouter :

```xml
                            <TextBlock Text="{Binding StatusText}" IsVisible="{Binding StatusText, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" Foreground="DarkOrange" FontSize="12" />
```

- [ ] **Étape 3 : Adapter le test de résolution**

Dans `ImageDateResolverTests.Resolve_FallsBackToFilesystem_WhenEarlierSourcesAreMissing`, remplacer l'assertion du nom cible par `Assert.Null(result.NormalizedFileNamePreview);` et ajouter `Assert.False(result.IsReliable);`.

- [ ] **Étape 4 : Vérifier**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : tout passe. `dotnet build PhotoApp` compile l'AXAML (liaisons compilées).

- [ ] **Étape 5 : Commit**

```bash
git add -A
git commit -m "fix: la date du système de fichiers n'est plus présentée comme fiable" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 6 : Trous du parseur de noms (défaut 6)

**Fichiers :**
- Modifier : `PhotoApp.Core/Services/FileNameDateParser.cs` (réécriture complète)
- Test : `PhotoApp.Tests/FileNameDateParserTests.cs`

**Interfaces :**
- Consomme : `ImageDateCandidate`, `DatePlausibility` (tâches 4-5).
- Produit : `FileNameDateParser.Parse(string fileName) : ImageDateCandidate?` (virtuelle, signature inchangée) reconnaissant en plus : captures Windows `Screenshot 2025-08-27 211650`, macOS/WhatsApp `2024-01-05 at 3.04.12 PM` / `à 15.04.12`, epoch `mmexport…`/`FB_IMG_…` (10 ou 13 chiffres, décalage `TimeSpan.Zero`), Pixel `PXL_AAAAMMJJ_HHmmssSSS`. Les dates `JJ-MM-AAAA` / `MM-JJ-AAAA` ambiguës (les deux composantes ≤ 12 et différentes) renvoient `null`.

- [ ] **Étape 1 : Ajouter les cas de test (échec attendu)**

Dans `FileNameDateParserTests.cs`, ajouter ces lignes à la `[Theory]` existante :

```csharp
    [InlineData("Screenshot 2025-08-27 211650.jpg", 2025, 8, 27, 21, 16, 50, true)]
    [InlineData("Screenshot 2024-01-05 at 3.04.12 PM.png", 2024, 1, 5, 15, 4, 12, true)]
    [InlineData("Screenshot 2024-01-05 at 12.04.12 AM.png", 2024, 1, 5, 0, 4, 12, true)]
    [InlineData("Capture d’écran 2024-01-05 à 15.04.12.png", 2024, 1, 5, 15, 4, 12, true)]
    [InlineData("WhatsApp Image 2023-05-01 at 14.03.22.jpeg", 2023, 5, 1, 14, 3, 22, true)]
    [InlineData("PXL_20230304_132015123.jpg", 2023, 3, 4, 13, 20, 15, true)]
    [InlineData("signal-2023-12-18-211842_002.jpg", 2023, 12, 18, 21, 18, 42, false)]
    [InlineData("12.25.2024 10-30-00.jpg", 2024, 12, 25, 10, 30, 0, true)]
```

Attention : la ligne `signal-…` est un cas de **non-régression documenté** (le parseur actuel lit `2023-12-18` en date seule puisque `-211842` n'a pas de séparateurs de l'heure) — la ligne doit rester `false` ici ; la reconnaissance de l'heure Signal viendra avec les profils (phase 1). Les autres lignes sont attendues en échec avant l'implémentation.

Ajouter ces tests :

```csharp
    [Theory]
    [InlineData("mmexport1700000000000.jpg", 2023, 11, 14, 22, 13, 20)]
    [InlineData("FB_IMG_1700000000.jpg", 2023, 11, 14, 22, 13, 20)]
    public void Parse_EpochNames_AreUtcWithZeroOffset(string fileName, int year, int month, int day, int hour, int minute, int second)
    {
        var result = new FileNameDateParser().Parse(fileName);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), result.WallClock);
        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.True(result.HasTime);
    }

    [Theory]
    [InlineData("03-04-2025 10-00-00.jpg")]   // JJ-MM ou MM-JJ : indécidable sans autre indice
    [InlineData("random-file-name.jpg")]
    [InlineData("IMG_20991231_235959.jpg")]   // dans le futur
    [InlineData("IMG_19800101_000001.jpg")]   // avant 1990
    public void Parse_ReturnsNull_ForAmbiguousOrImplausibleNames(string fileName)
    {
        Assert.Null(new FileNameDateParser().Parse(fileName));
    }
```

Run : `dotnet test PhotoApp.Tests --filter FileNameDateParserTests`
Attendu : échecs sur les nouveaux cas (Windows, macOS, epoch, Pixel, MM-JJ, ambiguïté, futur).

- [ ] **Étape 2 : Réécrire le parseur**

Remplacer tout `PhotoApp.Core/Services/FileNameDateParser.cs` :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public class FileNameDateParser
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Exports WeChat / Facebook : le nombre est un instant UTC (secondes ou millisecondes).
    private static readonly Regex EpochPattern = new(
        "(?:mmexport|FB_IMG[_-])(?<epoch>\\d{13}|\\d{10})(?!\\d)",
        Options | RegexOptions.IgnoreCase);

    // Motifs avec année, mois, jour et heure explicites, du plus précis au moins précis.
    private static readonly Regex[] TimePatterns =
    [
        // 20230304_132015 / 20230304-132015 / Pixel 20230304_132015123
        new("(?<!\\d)(?<year>\\d{4})(?<month>\\d{2})(?<day>\\d{2})[_-](?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})(?:\\d{3})?(?!\\d)", Options),
        // 2025-08-27 21-16-50 / 2025.08.27_21.16.50
        new("(?<!\\d)(?<year>\\d{4})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", Options),
        // 2024-01-05 at 3.04.12 PM / 2024-01-05 à 15.04.12
        new("(?<!\\d)(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2}) (?:at|à) (?<hour>\\d{1,2})[.:](?<minute>\\d{2})[.:](?<second>\\d{2})(?:\\s?(?<ampm>[AaPp][Mm]))?(?!\\d)", Options),
        // Capture Windows : Screenshot 2025-08-27 211650
        new("(?<!\\d)(?<year>\\d{4})-(?<month>\\d{2})-(?<day>\\d{2})[ _](?<hour>\\d{2})(?<minute>\\d{2})(?<second>\\d{2})(?!\\d)", Options),
        // Nextcloud iOS : 24-03-02 13-53-44
        new("(?<!\\d)(?<year>\\d{2})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2})(?!\\d)", Options),
        // Date ISO suivie d'une heure facultative
        new("(?<!\\d)(?<year>\\d{4})[-_.](?<month>\\d{2})[-_.](?<day>\\d{2})(?:[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<second>\\d{2}))?(?!\\d)", Options)
    ];

    // 15.01.2025 00_54_14 (JJ-MM-AAAA) ou 12.25.2024 10-30-00 (MM-JJ-AAAA)
    private static readonly Regex DayMonthYearPattern = new(
        "(?<!\\d)(?<first>\\d{2})[.-](?<second>\\d{2})[.-](?<year>\\d{4})[ T_-](?<hour>\\d{2})[-_.](?<minute>\\d{2})[-_.](?<secondPart>\\d{2})(?!\\d)",
        Options);

    private static readonly Regex DateOnlyPattern = new("(?<!\\d)(?<date>\\d{8})(?!\\d)", Options);

    public virtual ImageDateCandidate? Parse(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var now = DateTime.UtcNow;

        var epoch = TryParseEpoch(fileName);
        if (epoch is not null)
        {
            return Plausible(epoch, now);
        }

        foreach (var pattern in TimePatterns)
        {
            var match = pattern.Match(fileName);
            if (match.Success && TryBuildFromGroups(match, out var wallClock, out var hasTime))
            {
                return Plausible(new ImageDateCandidate(wallClock, hasTime), now);
            }
        }

        var dayMonth = DayMonthYearPattern.Match(fileName);
        if (dayMonth.Success)
        {
            return TryParseDayMonthYear(dayMonth, now);
        }

        var dateOnlyMatch = DateOnlyPattern.Match(fileName);
        if (dateOnlyMatch.Success &&
            DateTime.TryParseExact(dateOnlyMatch.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return Plausible(new ImageDateCandidate(dateOnly, false), now);
        }

        return null;
    }

    private static ImageDateCandidate? Plausible(ImageDateCandidate candidate, DateTime now) =>
        DatePlausibility.IsPlausible(candidate, now) ? candidate : null;

    private static ImageDateCandidate? TryParseEpoch(string fileName)
    {
        var match = EpochPattern.Match(fileName);
        if (!match.Success)
        {
            return null;
        }

        var text = match.Groups["epoch"].Value;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        try
        {
            var instant = text.Length == 13
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
            return new ImageDateCandidate(instant.UtcDateTime, true, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static ImageDateCandidate? TryParseDayMonthYear(Match match, DateTime now)
    {
        var first = Number(match, "first");
        var second = Number(match, "second");

        int day;
        int month;
        if (first > 12)
        {
            (day, month) = (first, second);
        }
        else if (second > 12)
        {
            (day, month) = (second, first);
        }
        else if (first == second)
        {
            (day, month) = (first, second);
        }
        else
        {
            // JJ-MM et MM-JJ sont tous deux valides : on ne devine pas (résolu en phase 1 par profil/voisinage).
            return null;
        }

        return TryBuild(Number(match, "year"), month, day, Number(match, "hour"), Number(match, "minute"), Number(match, "secondPart"), out var wallClock)
            ? Plausible(new ImageDateCandidate(wallClock, true), now)
            : null;
    }

    private static bool TryBuildFromGroups(Match match, out DateTime wallClock, out bool hasTime)
    {
        hasTime = match.Groups["hour"].Success;
        var year = Number(match, "year");
        if (match.Groups["year"].Value.Length == 2)
        {
            year += 2000;
        }

        var hour = hasTime ? Number(match, "hour") : 0;
        if (match.Groups["ampm"].Success)
        {
            if (hour is < 1 or > 12)
            {
                wallClock = default;
                return false;
            }

            var isPm = char.ToUpperInvariant(match.Groups["ampm"].Value[0]) == 'P';
            hour = (hour % 12) + (isPm ? 12 : 0);
        }

        return TryBuild(
            year,
            Number(match, "month"),
            Number(match, "day"),
            hour,
            hasTime ? Number(match, "minute") : 0,
            hasTime ? Number(match, "second") : 0,
            out wallClock);
    }

    private static bool TryBuild(int year, int month, int day, int hour, int minute, int second, out DateTime wallClock)
    {
        try
        {
            wallClock = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            wallClock = default;
            return false;
        }
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
```

- [ ] **Étape 3 : Vérifier**

Run : `dotnet test PhotoApp.Tests --filter FileNameDateParserTests`
Attendu : tout passe, y compris les 10 cas d'origine. Si `signal-2023-12-18-211842_002.jpg` renvoie une heure (motif 2 : `2023-12-18-21`+`18`… non, il exige `[-_.]` entre les groupes), ajuster uniquement l'attendu pour refléter le comportement réel et le consigner en commentaire — ne pas élargir les motifs.

Puis : `dotnet test PhotoVisualizer.slnx` — Attendu : tout passe.

- [ ] **Étape 4 : Commit**

```bash
git add -A
git commit -m "fix: parseur de noms (captures Windows/macOS, epoch, Pixel, ambiguïtés, plausibilité)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 7 : Nommage chronologique stable, sans hash de chemin (défaut 9)

**Fichiers :**
- Créer : `PhotoApp.Core/Services/NameAllocator.cs`, `PhotoApp.Core/Models/PlanInput.cs`, `PhotoApp.Core/Models/PreviousOutput.cs`
- Modifier : `PhotoApp.Core/Services/NormalizedFileNameBuilder.cs` (réécriture), `RenamePlanner.cs` (réécriture), `PhotoApp.Core/Models/RenamePlan.cs`, `RenameResult.cs`, `PhotoApp.Metadata/ImageDateResolver.cs`, `PhotoApp.Operations/BatchRenameExecutor.cs`, `PhotoApp/ViewModels/MainWindowViewModel.cs`
- Test : `NameAllocatorTests.cs`, `RenamePlannerTests.cs` (nouveaux), `NormalizedFileNameBuilderTests.cs`, `ImageDateResolverTests.cs`, `BatchRenameExecutorTests.cs` (adaptés)

**Interfaces :**
- Consomme : `ImageDateCandidate`.
- Produit :
  - `NormalizedFileNameBuilder.BaseName(ImageDateCandidate) : string` (`IMG-20240105-150412` ou `IMG-20240105-000000` si jour seul)
  - `NormalizedFileNameBuilder.NormalizeExtension(string) : string` (minuscules, `.jpeg` → `.jpg`)
  - `NormalizedFileNameBuilder.Preview(ImageDateCandidate) : string` (premier nom possible, extension `.jpg`)
  - `NameAllocator(IEnumerable<string>? alreadyTaken = null)` et `Allocate(ImageDateCandidate date, string extension) : string`
  - `record PlanInput(string SourcePath, ImageDateCandidate Date, long SizeBytes, long LastWriteUtcTicks)`
  - `record PreviousOutput(long SizeBytes, long LastWriteUtcTicks, string TargetName)`
  - `record RenamePlan(string SourcePath, string TargetName, ImageDateCandidate Date, long SizeBytes, long LastWriteUtcTicks, bool AlreadyDone = false)`
  - `record RenameResult(string SourcePath, string TargetPath, bool Success, string? ErrorMessage = null, bool Skipped = false)`
  - `RenamePlanner.PlanRenames(IReadOnlyList<PlanInput> inputs, IReadOnlyDictionary<string, PreviousOutput> previous, IEnumerable<string> existingTargetNames, string outputExtension = ".jpg") : IReadOnlyList<RenamePlan>`

- [ ] **Étape 1 : Tests qui échouent**

`PhotoApp.Tests/NameAllocatorTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class NameAllocatorTests
{
    private static ImageDateCandidate Shot() => new(new DateTime(2024, 1, 5, 15, 4, 12), true);

    private static ImageDateCandidate DayOnly() => new(new DateTime(2024, 1, 5), false);

    [Fact]
    public void Allocate_FirstFileOfASecond_HasNoSuffix()
    {
        Assert.Equal("IMG-20240105-150412.jpg", new NameAllocator().Allocate(Shot(), ".jpg"));
    }

    [Fact]
    public void Allocate_SameSecond_GetsIncreasingSuffixes()
    {
        var allocator = new NameAllocator();

        Assert.Equal("IMG-20240105-150412.jpg", allocator.Allocate(Shot(), ".jpg"));
        Assert.Equal("IMG-20240105-150412-01.jpg", allocator.Allocate(Shot(), ".jpg"));
        Assert.Equal("IMG-20240105-150412-02.jpg", allocator.Allocate(Shot(), ".jpg"));
    }

    [Fact]
    public void Allocate_DayOnly_AlwaysHasASuffixStartingAtOne()
    {
        var allocator = new NameAllocator();

        Assert.Equal("IMG-20240105-000000-01.jpg", allocator.Allocate(DayOnly(), ".jpg"));
        Assert.Equal("IMG-20240105-000000-02.jpg", allocator.Allocate(DayOnly(), ".jpg"));
    }

    [Fact]
    public void Allocate_NeverReusesAlreadyTakenNames_CaseInsensitive()
    {
        var allocator = new NameAllocator(["img-20240105-150412.JPG"]);

        Assert.Equal("IMG-20240105-150412-01.jpg", allocator.Allocate(Shot(), ".jpg"));
    }

    [Theory]
    [InlineData(".JPEG", ".jpg")]
    [InlineData("jpg", ".jpg")]
    [InlineData(".PNG", ".png")]
    public void NormalizeExtension_LowercasesAndMapsJpeg(string input, string expected)
    {
        Assert.Equal(expected, NormalizedFileNameBuilder.NormalizeExtension(input));
    }
}
```

`PhotoApp.Tests/RenamePlannerTests.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Tests;

public sealed class RenamePlannerTests
{
    private static readonly IReadOnlyDictionary<string, PreviousOutput> NoPrevious = new Dictionary<string, PreviousOutput>();

    private static PlanInput Input(string path, DateTime wallClock, bool hasTime = true, long size = 10, long ticks = 1) =>
        new(path, new ImageDateCandidate(wallClock, hasTime), size, ticks);

    [Fact]
    public void PlanRenames_SameSecond_IsOrderedByFileName_NotByInputOrder()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var inputs = new[] { Input("/p/b.png", when), Input("/p/a.png", when) };

        var plans = new RenamePlanner().PlanRenames(inputs, NoPrevious, []);

        Assert.Equal("IMG-20240105-150412.jpg", plans.Single(p => p.SourcePath == "/p/a.png").TargetName);
        Assert.Equal("IMG-20240105-150412-01.jpg", plans.Single(p => p.SourcePath == "/p/b.png").TargetName);
    }

    [Fact]
    public void PlanRenames_IsDeterministic_RegardlessOfInputOrder()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var a = new[] { Input("/p/a.png", when), Input("/p/b.png", when), Input("/p/c.png", when) };
        var b = a.Reverse().ToArray();
        var planner = new RenamePlanner();

        var first = planner.PlanRenames(a, NoPrevious, []).ToDictionary(p => p.SourcePath, p => p.TargetName);
        var second = planner.PlanRenames(b, NoPrevious, []).ToDictionary(p => p.SourcePath, p => p.TargetName);

        Assert.Equal(first, second);
    }

    [Fact]
    public void PlanRenames_AlreadyDoneSource_KeepsItsNameAndIsNotRenumbered()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var previous = new Dictionary<string, PreviousOutput> { ["/p/b.png"] = new(10, 1, "IMG-20240105-150412.jpg") };
        var inputs = new[] { Input("/p/a.png", when), Input("/p/b.png", when) };

        var plans = new RenamePlanner().PlanRenames(inputs, previous, ["IMG-20240105-150412.jpg"]);

        var b = plans.Single(p => p.SourcePath == "/p/b.png");
        Assert.True(b.AlreadyDone);
        Assert.Equal("IMG-20240105-150412.jpg", b.TargetName);
        var a = plans.Single(p => p.SourcePath == "/p/a.png");
        Assert.False(a.AlreadyDone);
        Assert.Equal("IMG-20240105-150412-01.jpg", a.TargetName);
    }

    [Fact]
    public void PlanRenames_SourceChangedSinceLastRun_IsPlannedAgainUnderANewName()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var previous = new Dictionary<string, PreviousOutput> { ["/p/a.png"] = new(10, 1, "IMG-20240105-150412.jpg") };
        var inputs = new[] { Input("/p/a.png", when, size: 99) };

        var plan = new RenamePlanner().PlanRenames(inputs, previous, ["IMG-20240105-150412.jpg"]).Single();

        Assert.False(plan.AlreadyDone);
        Assert.Equal("IMG-20240105-150412-01.jpg", plan.TargetName);
    }

    [Fact]
    public void PlanRenames_SecondRunWithSameInputs_IsEntirelyAlreadyDone()
    {
        var when = new DateTime(2024, 1, 5, 15, 4, 12);
        var inputs = new[] { Input("/p/a.png", when), Input("/p/b.png", when), Input("/p/c.png", new DateTime(2024, 1, 5), hasTime: false) };
        var planner = new RenamePlanner();
        var firstRun = planner.PlanRenames(inputs, NoPrevious, []);
        var previous = firstRun.ToDictionary(p => p.SourcePath, p => new PreviousOutput(p.SizeBytes, p.LastWriteUtcTicks, p.TargetName));

        var secondRun = planner.PlanRenames(inputs, previous, firstRun.Select(p => p.TargetName));

        Assert.All(secondRun, p => Assert.True(p.AlreadyDone));
        Assert.Equal(
            firstRun.ToDictionary(p => p.SourcePath, p => p.TargetName),
            secondRun.ToDictionary(p => p.SourcePath, p => p.TargetName));
    }

    [Fact]
    public void PlanRenames_AlwaysProducesTheOutputExtension()
    {
        var plan = new RenamePlanner().PlanRenames([Input("/p/photo.PNG", new DateTime(2024, 1, 5, 15, 4, 12))], NoPrevious, []).Single();

        Assert.EndsWith(".jpg", plan.TargetName);
    }
}
```

Run : `dotnet build PhotoApp.Tests` — Attendu : ÉCHEC.

- [ ] **Étape 2 : Implémenter les modèles**

`PlanInput.cs` :

```csharp
namespace PhotoApp.Core.Models;

public sealed record PlanInput(string SourcePath, ImageDateCandidate Date, long SizeBytes, long LastWriteUtcTicks);
```

`PreviousOutput.cs` :

```csharp
namespace PhotoApp.Core.Models;

public sealed record PreviousOutput(long SizeBytes, long LastWriteUtcTicks, string TargetName);
```

`RenamePlan.cs` :

```csharp
namespace PhotoApp.Core.Models;

public sealed record RenamePlan(
    string SourcePath,
    string TargetName,
    ImageDateCandidate Date,
    long SizeBytes,
    long LastWriteUtcTicks,
    bool AlreadyDone = false);
```

`RenameResult.cs` :

```csharp
namespace PhotoApp.Core.Models;

public sealed record RenameResult(
    string SourcePath,
    string TargetPath,
    bool Success,
    string? ErrorMessage = null,
    bool Skipped = false);
```

- [ ] **Étape 3 : Implémenter le nommage**

`NormalizedFileNameBuilder.cs` (remplacer tout) :

```csharp
using System.Globalization;
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public static class NormalizedFileNameBuilder
{
    /// <summary>Nom sans suffixe ni extension. Jour seul : l'heure 000000 sert uniquement au tri (spec §5.5).</summary>
    public static string BaseName(ImageDateCandidate date) => date.HasTime
        ? "IMG-" + date.WallClock.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
        : "IMG-" + date.WallClock.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-000000";

    public static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var value = (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
        return value == ".jpeg" ? ".jpg" : value;
    }

    /// <summary>Premier nom possible pour cette date (l'attribution réelle dépend des collisions).</summary>
    public static string Preview(ImageDateCandidate date) =>
        date.HasTime ? BaseName(date) + ".jpg" : BaseName(date) + "-01.jpg";
}
```

`NameAllocator.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

/// <summary>
/// Attribue des noms uniques. Les noms déjà pris ne sont jamais renumérotés :
/// un nouveau venu reçoit le prochain numéro libre.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _taken = new(StringComparer.OrdinalIgnoreCase);

    public NameAllocator(IEnumerable<string>? alreadyTaken = null)
    {
        if (alreadyTaken is null)
        {
            return;
        }

        foreach (var name in alreadyTaken)
        {
            _taken.Add(name);
        }
    }

    public string Allocate(ImageDateCandidate date, string extension)
    {
        var baseName = NormalizedFileNameBuilder.BaseName(date);
        var normalizedExtension = NormalizedFileNameBuilder.NormalizeExtension(extension);

        // Jour seul : le suffixe est toujours présent ; sinon le premier du lot n'en a pas.
        for (var number = date.HasTime ? 0 : 1; ; number++)
        {
            var name = number == 0
                ? baseName + normalizedExtension
                : $"{baseName}-{number:00}{normalizedExtension}";

            if (_taken.Add(name))
            {
                return name;
            }
        }
    }
}
```

`RenamePlanner.cs` (remplacer tout) :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Core.Services;

public sealed class RenamePlanner
{
    public IReadOnlyList<RenamePlan> PlanRenames(
        IReadOnlyList<PlanInput> inputs,
        IReadOnlyDictionary<string, PreviousOutput> previous,
        IEnumerable<string> existingTargetNames,
        string outputExtension = ".jpg")
    {
        var allocator = new NameAllocator(existingTargetNames.Concat(previous.Values.Select(p => p.TargetName)));
        var plans = new List<RenamePlan>(inputs.Count);

        // Ordre chronologique puis nom de fichier (le compteur WhatsApp WAnnnn en fait partie) : stable d'un passage à l'autre.
        var ordered = inputs
            .OrderBy(i => i.Date.WallClock)
            .ThenBy(i => i.Date.HasTime)
            .ThenBy(i => Path.GetFileName(i.SourcePath), StringComparer.Ordinal)
            .ThenBy(i => i.SourcePath, StringComparer.Ordinal);

        foreach (var input in ordered)
        {
            if (previous.TryGetValue(input.SourcePath, out var done) &&
                done.SizeBytes == input.SizeBytes &&
                done.LastWriteUtcTicks == input.LastWriteUtcTicks)
            {
                plans.Add(new RenamePlan(input.SourcePath, done.TargetName, input.Date, input.SizeBytes, input.LastWriteUtcTicks, AlreadyDone: true));
                continue;
            }

            plans.Add(new RenamePlan(
                input.SourcePath,
                allocator.Allocate(input.Date, outputExtension),
                input.Date,
                input.SizeBytes,
                input.LastWriteUtcTicks));
        }

        return plans;
    }
}
```

- [ ] **Étape 4 : Porter les appelants et les anciens tests**

`ImageDateResolver.Resolve` : remplacer le calcul du `preview` par
`var preview = retained is null || !DateSelector.IsReliable(source) ? null : NormalizedFileNameBuilder.Preview(retained);`

`BatchRenameExecutor.DryRun` : dans la boucle, remplacer le test d'existence par
`if (!plan.AlreadyDone && File.Exists(targetPath))` (le message d'erreur reste identique).

`MainWindowViewModel.ConvertAsync` — adapter provisoirement (la tâche 11 réécrit ce fichier) :

```csharp
            var imageData = new List<PlanInput>();
            foreach (var img in Images)
            {
                if (img.DateMetadata is { IsReliable: true, RetainedCandidate: { } date })
                {
                    imageData.Add(new PlanInput(img.FullPath, date, img.FileSizeBytes, System.IO.File.GetLastWriteTimeUtc(img.FullPath).Ticks));
                }
            }
```
et `var plans = planner.PlanRenames(imageData, new Dictionary<string, PreviousOutput>(), []);`

Tests : `NormalizedFileNameBuilderTests.cs` — remplacer par un test de `Preview` : conserver la `Theory` mais avec les attentes en `.jpg` pour tous les cas horodatés (ex. `"Capture d’écran du 2026-03-27 12-01-17.png"` → `IMG-20260327-120117.jpg`) et `"IMG-20211029-WA0039.jpg"` (jour seul) → `IMG-20211029-000000-01.jpg` ; appeler `NormalizedFileNameBuilder.Preview(date)` et supprimer les paramètres `filename`/extension inutiles à l'appel.
`ImageDateResolverTests.cs` — attendus : `IMG-20240102-030405.jpg` (inchangé) et, pour le jour seul, `IMG-20211029-000000-01.jpg` à la place de `IMG-20211029-58ebe3.jpg`.
`BatchRenameExecutorTests.cs` — chaque `new RenamePlan(src, tgt, Noon)` devient `new RenamePlan(src, tgt, Noon, 0, 0)`.

- [ ] **Étape 5 : Vérifier**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : tout passe.

- [ ] **Étape 6 : Commit**

```bash
git add -A
git commit -m "feat: nommage chronologique stable avec suffixes -NN et plan idempotent" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 8 : Le scan n'inclut plus la sortie ni les dossiers cachés (défaut 1)

**Fichiers :**
- Créer : `PhotoApp.Catalog/ScanOptions.cs`
- Modifier : `PhotoApp.Catalog/ImageScanner.cs`
- Test : `PhotoApp.Tests/ImageScannerTests.cs`

**Interfaces :**
- Produit : `record ScanOptions(IReadOnlyCollection<string> ExcludedFolders)` ; `ImageScanner.Scan(string rootPath, ScanOptions? options = null) : IReadOnlyList<ImageFileInfo>`. Le scanner ignore : les dossiers exclus (et leurs descendants), les dossiers dont le nom commence par `.` ou portant l'attribut Hidden, les fichiers de 0 octet, les fichiers cachés.

- [ ] **Étape 1 : Tests qui échouent** (ajouter dans `ImageScannerTests`)

```csharp
    [Fact]
    public void Scan_SkipsExcludedFolderAndItsDescendants()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine("Converted", "sub"));
        File.WriteAllBytes(temp.Combine("keep.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "out.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("Converted", "sub", "deep.jpg"), [1]);

        var result = new ImageScanner().Scan(temp.FullPath, new ScanOptions([temp.Combine("Converted")]));

        Assert.Equal(["keep.jpg"], result.Select(i => i.FileName));
    }

    [Fact]
    public void Scan_SkipsDotFoldersAndZeroByteFiles()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Combine(".gs"));
        File.WriteAllBytes(temp.Combine(".gs", "cache.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("empty.jpg"), []);
        File.WriteAllBytes(temp.Combine("ok.jpg"), [1]);

        var result = new ImageScanner().Scan(temp.FullPath);

        Assert.Equal(["ok.jpg"], result.Select(i => i.FileName));
    }

    [Fact]
    public void Scan_SkipsTemporaryWorkFiles()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine(".work-abc.jpg"), [1]);
        File.WriteAllBytes(temp.Combine("ok.jpg"), [1]);

        var result = new ImageScanner().Scan(temp.FullPath);

        Assert.Equal(["ok.jpg"], result.Select(i => i.FileName));
    }
```

Run : `dotnet test PhotoApp.Tests --filter ImageScannerTests` — Attendu : ÉCHEC de compilation (`ScanOptions`).

- [ ] **Étape 2 : Implémenter**

`ScanOptions.cs` :

```csharp
namespace PhotoApp.Catalog;

public sealed record ScanOptions(IReadOnlyCollection<string> ExcludedFolders);
```

`ImageScanner.cs` — modifier `Scan` et ajouter les helpers (le reste du fichier — `EnumerateDirectoriesSafe`, `EnumerateFilesSafe`, `CreateImageFileInfo` — est inchangé) :

```csharp
    public IReadOnlyList<ImageFileInfo> Scan(string rootPath, ScanOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path must not be empty.", nameof(rootPath));
        }

        var images = new List<ImageFileInfo>();

        if (!Directory.Exists(rootPath))
        {
            return images;
        }

        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var excluded = new HashSet<string>(
            (options?.ExcludedFolders ?? []).Select(NormalizeFolder),
            comparer);

        var directoriesToScan = new Stack<string>();
        directoriesToScan.Push(rootPath);

        while (directoriesToScan.Count > 0)
        {
            var currentDirectory = directoriesToScan.Pop();

            foreach (var subDirectory in EnumerateDirectoriesSafe(currentDirectory))
            {
                if (excluded.Contains(NormalizeFolder(subDirectory)) || IsHidden(subDirectory))
                {
                    continue;
                }

                directoriesToScan.Push(subDirectory);
            }

            foreach (var filePath in EnumerateFilesSafe(currentDirectory))
            {
                var extension = Path.GetExtension(filePath);
                if (!SupportedImageExtensions.IsSupported(extension) || IsHidden(filePath))
                {
                    continue;
                }

                var imageInfo = CreateImageFileInfo(filePath);
                if (imageInfo is { FileSizeBytes: > 0 })
                {
                    images.Add(imageInfo);
                }
            }
        }

        return images;
    }

    private static string NormalizeFolder(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
```

Le test `Scan_SkipsTemporaryWorkFiles` passe grâce à la règle « nom commençant par `.` » : les fichiers de travail de l'exécuteur (tâche 10) s'appellent `.work-<guid>…`.

- [ ] **Étape 3 : Vérifier**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : tout passe (dont `Scan_ReturnsOnlySupportedImages_FromNestedDirectories`).

- [ ] **Étape 4 : Commit**

```bash
git add -A
git commit -m "fix: le scan exclut le dossier de sortie, les dossiers cachés et les fichiers vides" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 9 : Formats réellement convertibles (défaut 2)

Révisée après le passage à Magick.NET : HEIC, HEIF et AVIF sont **convertis** (décodeur libheif embarqué). Magick.NET 14.17.2 lit le HEIC mais ne sait pas l'écrire (pas d'encodeur x265) : les tests utilisent AVIF, qui passe par le même décodeur ; un vrai HEIC est vérifié à la main en tâche 11. GIF n'est **pas** converti (une animation serait réduite à sa première image). `.avif` est ajouté à `SupportedImageExtensions`.

**Fichiers :**
- Modifier : `PhotoApp.Operations/ImageConverter.cs`, `PhotoApp.Core/Services/SupportedImageExtensions.cs` (`.avif`)
- Test : `PhotoApp.Tests/ImageConverterTests.cs`

**Interfaces :**
- Produit : `ImageConverter.CanConvert(string? extension) : bool` (statique) — vrai pour `.jpg .jpeg .png .bmp .tif .tiff .webp .heic .heif .avif`. `ConvertToJpeg` : JPEG copié octet pour octet ; autres formats décodés, orientés (`AutoOrient`), transparence remplacée par du blanc, profils EXIF/XMP/ICC conservés.

- [ ] **Étape 1 : Tests qui échouent**

```csharp
using ImageMagick;

namespace PhotoApp.Tests;

public sealed class ImageConverterTests
{
    [Theory]
    [InlineData(".jpg", true)]
    [InlineData(".JPEG", true)]
    [InlineData(".png", true)]
    [InlineData(".webp", true)]
    [InlineData(".tif", true)]
    [InlineData(".heic", true)]
    [InlineData(".heif", true)]
    [InlineData(".avif", true)]
    [InlineData(".gif", false)]   // animation perdue : non converti
    [InlineData(".txt", false)]
    [InlineData(null, false)]
    public void CanConvert_ReflectsWhatConvertToJpegSupports(string? extension, bool expected)
    {
        Assert.Equal(expected, ImageConverter.CanConvert(extension));
    }

    [Theory]
    [InlineData("in.png", MagickFormat.Png)]
    [InlineData("in.webp", MagickFormat.WebP)]
    [InlineData("in.tif", MagickFormat.Tiff)]
    [InlineData("in.avif", MagickFormat.Avif)]   // même décodeur (libheif) que HEIC, que Magick.NET ne sait pas écrire
    public void ConvertToJpeg_ProducesAReadableJpeg(string inputName, MagickFormat inputFormat)
    {
        using var temp = new TempDirectory();
        var input = temp.Combine(inputName);
        var output = temp.Combine("out.jpg");
        TestImages.Write(input, inputFormat);

        new ImageConverter().ConvertToJpeg(input, output, 90);

        var info = new MagickImageInfo(output);
        Assert.Equal(MagickFormat.Jpeg, info.Format);
        Assert.Equal(8u, info.Width);
    }

    [Fact]
    public void ConvertToJpeg_KeepsTheExifDate()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.webp");
        var output = temp.Combine("out.jpg");
        using (var image = new MagickImage(MagickColors.Gray, 8, 8))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, "2023:03:04 13:20:15");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.WebP);
        }

        new ImageConverter().ConvertToJpeg(input, output, 90);

        var date = new NativeImageMetadataReader().Read(output);
        Assert.NotNull(date);
        Assert.Equal(new DateTime(2023, 3, 4, 13, 20, 15), date.WallClock);
    }

    [Fact]
    public void ConvertToJpeg_TransparentPng_GetsAWhiteBackground()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.png");
        var output = temp.Combine("out.jpg");
        using (var image = new MagickImage(MagickColors.Transparent, 8, 8))
        {
            image.Write(input, MagickFormat.Png);
        }

        new ImageConverter().ConvertToJpeg(input, output, 90);

        using var result = new MagickImage(output);
        var pixel = result.GetPixels().GetPixel(4, 4).ToColor()!;
        Assert.True(pixel.R > 240 && pixel.G > 240 && pixel.B > 240, $"pixel attendu blanc, obtenu {pixel}");
    }

    [Fact]
    public void ConvertToJpeg_JpegSource_IsCopiedByteForByte()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.jpg");
        var output = temp.Combine("out.jpg");
        TestImages.WriteJpeg(input, "2023:03:04 13:20:15");

        new ImageConverter().ConvertToJpeg(input, output, 50);

        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
    }

    [Fact]
    public void ConvertToJpeg_UnsupportedExtension_ThrowsNotSupported()
    {
        using var temp = new TempDirectory();
        var input = temp.Combine("in.gif");
        File.WriteAllBytes(input, [1, 2, 3]);

        Assert.Throws<NotSupportedException>(() => new ImageConverter().ConvertToJpeg(input, temp.Combine("out.jpg"), 90));
    }
}
```

Run : `dotnet build PhotoApp.Tests` — Attendu : ÉCHEC (`CanConvert` absent).

- [ ] **Étape 2 : Implémenter** — remplacer `ImageConverter.cs` :

```csharp
using ImageMagick;

namespace PhotoApp.Operations;

public sealed class ImageConverter : IImageConverter
{
    private static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };

    // GIF est exclu : la conversion ne garderait que la première image d'une animation.
    private static readonly HashSet<string> DecodableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif"
    };

    public static bool CanConvert(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) &&
        (JpegExtensions.Contains(extension) || DecodableExtensions.Contains(extension));

    public void ConvertToJpeg(string inputPath, string outputPath, int quality)
    {
        var extension = Path.GetExtension(inputPath);
        if (JpegExtensions.Contains(extension))
        {
            // Déjà du JPEG : copie telle quelle pour conserver tous les octets et métadonnées.
            File.Copy(inputPath, outputPath, overwrite: true);
            return;
        }

        if (!DecodableExtensions.Contains(extension))
        {
            throw new NotSupportedException($"Conversion from {extension} to JPEG is not supported.");
        }

        // Magick.NET conserve les profils EXIF, XMP et ICC à l'écriture.
        using var image = new MagickImage(inputPath);
        image.AutoOrient();
        if (image.HasAlpha)
        {
            // Le JPEG n'a pas de transparence : fond blanc plutôt que noir.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }

        image.Quality = (uint)quality;
        image.Write(outputPath, MagickFormat.Jpeg);
    }
}
```

Ajouter `".avif"` à la liste de `SupportedImageExtensions`.

- [ ] **Étape 3 : Vérifier**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : tout passe.

- [ ] **Étape 4 : Commit**

```bash
git add -A
git commit -m "fix: HEIC/HEIF/AVIF/WebP convertis via Magick.NET, métadonnées conservées" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 10 : Exécution sans fichiers orphelins, manifeste et passage idempotent (défaut 5)

**Fichiers :**
- Créer : `PhotoApp.Operations/ProcessedManifest.cs`, `ConversionService.cs`, `ConversionSummary.cs`
- Modifier : `PhotoApp.Operations/BatchRenameExecutor.cs` (réécriture)
- Test : `ProcessedManifestTests.cs`, `ConversionServiceTests.cs` (nouveaux) ; `BatchRenameExecutorTests.cs` (ajouts)

**Interfaces :**
- Consomme : `RenamePlan`, `RenameResult`, `PlanInput`, `PreviousOutput`, `RenamePlanner`, `ImageConverter.CanConvert`, `ImageFileInfo`, `ImageDateMetadata.IsReliable` / `RetainedCandidate`.
- Produit :
  - `record ManifestEntry(string SourcePath, long SizeBytes, long LastWriteUtcTicks, string TargetName)`
  - `ProcessedManifest` : `static Empty()`, `static Load(string path)` (lève `InvalidDataException` si le JSON est corrompu), `Save(string path)` (atomique, crée le dossier), `Record(ManifestEntry)`, `PruneMissingTargets(string outputFolder)`, `ToPrevious() : IReadOnlyDictionary<string, PreviousOutput>`, `Count`
  - `BatchRenameExecutor.Execute(IReadOnlyList<RenamePlan> plans, string outputFolder, int jpegQuality, bool deleteOriginals, ProcessedManifest? manifest = null)`
  - `record ConversionSummary(int Converted, int AlreadyDone, int SkippedUnreliableDate, int SkippedUnsupportedFormat, int Failed, string? FirstError)` avec `ToDisplayText()`
  - `ConversionService.Run(IReadOnlyList<ImageFileInfo> images, string outputFolder, string manifestPath, int jpegQuality, bool deleteOriginals) : ConversionSummary`

- [ ] **Étape 1 : Tests qui échouent**

`ProcessedManifestTests.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class ProcessedManifestTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("sub", "m.json");
        var manifest = ProcessedManifest.Empty();
        manifest.Record(new ManifestEntry("/p/a.png", 10, 20, "IMG-20240105-150412.jpg"));

        manifest.Save(path);
        var loaded = ProcessedManifest.Load(path);

        Assert.Equal(1, loaded.Count);
        Assert.Equal(new PreviousOutput(10, 20, "IMG-20240105-150412.jpg"), loaded.ToPrevious()["/p/a.png"]);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        using var temp = new TempDirectory();

        Assert.Equal(0, ProcessedManifest.Load(temp.Combine("none.json")).Count);
    }

    [Fact]
    public void Load_CorruptFile_Throws_InsteadOfSilentlyReprocessingEverything()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("m.json");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidDataException>(() => ProcessedManifest.Load(path));
    }

    [Fact]
    public void PruneMissingTargets_DropsEntriesWhoseOutputFileIsGone()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("kept.jpg"), [1]);
        var manifest = ProcessedManifest.Empty();
        manifest.Record(new ManifestEntry("/p/a.png", 1, 1, "kept.jpg"));
        manifest.Record(new ManifestEntry("/p/b.png", 1, 1, "gone.jpg"));

        manifest.PruneMissingTargets(temp.FullPath);

        Assert.Equal(["/p/a.png"], manifest.ToPrevious().Keys);
    }
}
```

Ajouter à `BatchRenameExecutorTests` :

```csharp
    [Fact]
    public void Execute_WhenConversionFailsAfterWritingOutput_LeavesNoOrphanFiles()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source.png");
        File.WriteAllText(source, "x");
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>();
        converter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((_, target, _) =>
            {
                File.WriteAllText(target, "partial");
                throw new IOException("boom");
            });
        var executor = new BatchRenameExecutor(converter.Object, Mock.Of<IFileTimestampApplier>());

        var results = executor.Execute([new RenamePlan(source, "t.jpg", Noon, 1, 1)], output, 80, false);

        Assert.False(results[0].Success);
        Assert.Empty(Directory.GetFiles(output));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Execute_WhenTimestampApplierFails_LeavesNoOrphanFiles()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source.jpg");
        File.WriteAllText(source, "x");
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>();
        converter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((input, target, _) => File.Copy(input, target));
        var applier = new Mock<IFileTimestampApplier>();
        applier.Setup(a => a.ApplyTimestamps(It.IsAny<string>(), It.IsAny<DateTimeOffset>())).Throws(new IOException("locked"));
        var executor = new BatchRenameExecutor(converter.Object, applier.Object);

        var results = executor.Execute([new RenamePlan(source, "t.jpg", Noon, 1, 1)], output, 80, false);

        Assert.False(results[0].Success);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public void Execute_AlreadyDonePlan_IsSkippedWithoutTouchingAnything()
    {
        using var temp = new TempDirectory();
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>(MockBehavior.Strict);
        var executor = new BatchRenameExecutor(converter.Object, Mock.Of<IFileTimestampApplier>());

        var results = executor.Execute([new RenamePlan("/p/a.png", "t.jpg", Noon, 1, 1, AlreadyDone: true)], output, 80, true);

        Assert.True(results[0].Success);
        Assert.True(results[0].Skipped);
    }

    [Fact]
    public void Execute_Success_RecordsTheSourceInTheManifest()
    {
        using var temp = new TempDirectory();
        var source = temp.Combine("source.jpg");
        File.WriteAllText(source, "x");
        var output = temp.Combine("output");
        var converter = new Mock<IImageConverter>();
        converter.Setup(c => c.ConvertToJpeg(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Callback<string, string, int>((input, target, _) => File.Copy(input, target));
        var manifest = ProcessedManifest.Empty();
        var executor = new BatchRenameExecutor(converter.Object, Mock.Of<IFileTimestampApplier>());

        executor.Execute([new RenamePlan(source, "t.jpg", Noon, 7, 9)], output, 80, false, manifest);

        Assert.Equal(new PreviousOutput(7, 9, "t.jpg"), manifest.ToPrevious()[source]);
    }
```

`ConversionServiceTests.cs` (test d'intégration : le critère de fin de la phase 0) :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Tests;

public sealed class ConversionServiceTests
{
    private static IReadOnlyList<ImageFileInfo> ScanAndResolve(string root, string outputFolder)
    {
        var resolver = new ImageDateResolver();
        return new ImageScanner()
            .Scan(root, new ScanOptions([outputFolder]))
            .Select(i => new ImageFileInfo(i.FullPath, i.FileName, i.Extension, i.FileSizeBytes, resolver.Resolve(i.FullPath)))
            .ToList();
    }

    [Fact]
    public void SecondPassOnTheSameFolder_ProducesNoErrorAndNoDuplicate()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);

        TestImages.WritePng(Path.Combine(root, "Screenshot_20240410_111652.png"));
        TestImages.WritePng(Path.Combine(root, "IMG_20240410_111652.png"));                 // même seconde
        TestImages.WriteJpeg(Path.Combine(root, "holiday.jpg"), "2023:03:04 13:20:15");     // date EXIF
        TestImages.WritePng(Path.Combine(root, "no-date-at-all.png"));                       // date non fiable

        var service = new ConversionService();

        var first = service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);
        var filesAfterFirst = Directory.GetFiles(output).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();

        var second = service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);
        var filesAfterSecond = Directory.GetFiles(output).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(3, first.Converted);
        Assert.Equal(0, first.Failed);
        Assert.Equal(1, first.SkippedUnreliableDate);
        Assert.Equal(0, first.SkippedUnsupportedFormat);
        Assert.Equal(
            ["IMG-20230304-132015.jpg", "IMG-20240410-111652-01.jpg", "IMG-20240410-111652.jpg"],
            filesAfterFirst);

        Assert.Equal(0, second.Converted);
        Assert.Equal(3, second.AlreadyDone);
        Assert.Equal(0, second.Failed);
        Assert.Equal(filesAfterFirst, filesAfterSecond);
    }

    [Fact]
    public void DeletingAnOutputFile_MakesItBeConvertedAgainUnderTheSameName()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("photos");
        var output = Path.Combine(root, "Converted");
        var manifestPath = temp.Combine("data", "manifest.json");
        Directory.CreateDirectory(root);
        TestImages.WritePng(Path.Combine(root, "IMG_20240410_111652.png"));
        var service = new ConversionService();
        service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);
        File.Delete(Path.Combine(output, "IMG-20240410-111652.jpg"));

        var again = service.Run(ScanAndResolve(root, output), output, manifestPath, 90, false);

        Assert.Equal(1, again.Converted);
        Assert.True(File.Exists(Path.Combine(output, "IMG-20240410-111652.jpg")));
    }

    [Fact]
    public void ToDisplayText_SummarisesEveryCounter()
    {
        var text = new ConversionSummary(3, 2, 1, 4, 1, "boom").ToDisplayText();

        Assert.Contains("3 converti", text);
        Assert.Contains("2 déjà", text);
        Assert.Contains("1 ignoré(s) (date non fiable)", text);
        Assert.Contains("4 ignoré(s) (format non pris en charge)", text);
        Assert.Contains("1 échec", text);
        Assert.Contains("boom", text);
    }
}
```

Run : `dotnet build PhotoApp.Tests` — Attendu : ÉCHEC.

- [ ] **Étape 2 : Manifeste**

`PhotoApp.Operations/ProcessedManifest.cs` :

```csharp
using System.Text.Json;
using PhotoApp.Core.Models;

namespace PhotoApp.Operations;

public sealed record ManifestEntry(string SourcePath, long SizeBytes, long LastWriteUtcTicks, string TargetName);

/// <summary>Mémoire des fichiers déjà convertis pour un dossier de sortie. Stockée dans les données de l'application.</summary>
public sealed class ProcessedManifest
{
    private readonly Dictionary<string, ManifestEntry> _entries;

    private ProcessedManifest(Dictionary<string, ManifestEntry> entries)
    {
        _entries = entries;
    }

    public int Count => _entries.Count;

    public static ProcessedManifest Empty() => new(new Dictionary<string, ManifestEntry>(StringComparer.Ordinal));

    public static ProcessedManifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return Empty();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path)) ?? [];
            var entries = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
            foreach (var entry in list)
            {
                entries[entry.SourcePath] = entry;
            }

            return new ProcessedManifest(entries);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Le manifeste est illisible : {path}", ex);
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_entries.Values.OrderBy(e => e.SourcePath, StringComparer.Ordinal)));
        File.Move(temporary, path, overwrite: true);
    }

    public void Record(ManifestEntry entry) => _entries[entry.SourcePath] = entry;

    public void PruneMissingTargets(string outputFolder)
    {
        foreach (var entry in _entries.Values.ToList())
        {
            if (!File.Exists(Path.Combine(outputFolder, entry.TargetName)))
            {
                _entries.Remove(entry.SourcePath);
            }
        }
    }

    public IReadOnlyDictionary<string, PreviousOutput> ToPrevious() =>
        _entries.ToDictionary(
            pair => pair.Key,
            pair => new PreviousOutput(pair.Value.SizeBytes, pair.Value.LastWriteUtcTicks, pair.Value.TargetName),
            StringComparer.Ordinal);
}
```

- [ ] **Étape 3 : Exécuteur** — remplacer `BatchRenameExecutor.cs` :

```csharp
using PhotoApp.Core.Models;

namespace PhotoApp.Operations;

public sealed class BatchRenameExecutor
{
    private readonly IImageConverter _imageConverter;
    private readonly IFileTimestampApplier _timestampApplier;

    public BatchRenameExecutor()
        : this(new ImageConverter(), new FileTimestampApplier())
    {
    }

    public BatchRenameExecutor(IImageConverter imageConverter, IFileTimestampApplier timestampApplier)
    {
        _imageConverter = imageConverter;
        _timestampApplier = timestampApplier;
    }

    public IReadOnlyList<RenameResult> DryRun(IReadOnlyList<RenamePlan> plans, string outputFolder)
    {
        var results = new List<RenameResult>();
        Directory.CreateDirectory(outputFolder);

        foreach (var plan in plans)
        {
            var targetPath = Path.Combine(outputFolder, plan.TargetName);
            if (!plan.AlreadyDone && File.Exists(targetPath))
            {
                results.Add(new RenameResult(plan.SourcePath, targetPath, false, "Target file already exists"));
            }
            else
            {
                results.Add(new RenameResult(plan.SourcePath, targetPath, true, Skipped: plan.AlreadyDone));
            }
        }

        return results;
    }

    public IReadOnlyList<RenameResult> Execute(
        IReadOnlyList<RenamePlan> plans,
        string outputFolder,
        int jpegQuality,
        bool deleteOriginals,
        ProcessedManifest? manifest = null)
    {
        var results = new List<RenameResult>();
        Directory.CreateDirectory(outputFolder);

        foreach (var plan in plans)
        {
            results.Add(ExecuteSingle(plan, outputFolder, jpegQuality, deleteOriginals, manifest));
        }

        return results;
    }

    private RenameResult ExecuteSingle(RenamePlan plan, string outputFolder, int jpegQuality, bool deleteOriginal, ProcessedManifest? manifest)
    {
        var targetPath = Path.Combine(outputFolder, plan.TargetName);
        if (plan.AlreadyDone)
        {
            return new RenameResult(plan.SourcePath, targetPath, true, Skipped: true);
        }

        // Les chemins de travail sont calculés une seule fois : le nettoyage vise exactement ce qui a été créé.
        // Le préfixe ".work-" les rend invisibles au scanner.
        var workStem = Path.Combine(outputFolder, $".work-{Guid.NewGuid():N}");
        var workSource = workStem + ".source" + Path.GetExtension(plan.SourcePath);   // distinct de workJpeg même pour une source .jpg
        var workJpeg = workStem + ".jpg";

        try
        {
            File.Copy(plan.SourcePath, workSource, overwrite: false);
            _imageConverter.ConvertToJpeg(workSource, workJpeg, jpegQuality);
            _timestampApplier.ApplyTimestamps(workJpeg, plan.Date.ToInstant(TimeZoneInfo.Local));
            File.Move(workJpeg, targetPath, overwrite: false);
        }
        catch (Exception ex)
        {
            return new RenameResult(plan.SourcePath, targetPath, false, ex.Message);
        }
        finally
        {
            DeleteIfExists(workSource);
            DeleteIfExists(workJpeg);
        }

        manifest?.Record(new ManifestEntry(plan.SourcePath, plan.SizeBytes, plan.LastWriteUtcTicks, plan.TargetName));

        if (!deleteOriginal)
        {
            return new RenameResult(plan.SourcePath, targetPath, true);
        }

        try
        {
            File.Delete(plan.SourcePath);
            return new RenameResult(plan.SourcePath, targetPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RenameResult(plan.SourcePath, targetPath, true, $"Original non supprimé : {ex.Message}");
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nettoyage au mieux : ne doit jamais masquer l'erreur d'origine.
        }
    }
}
```

- [ ] **Étape 4 : Service de conversion**

`ConversionSummary.cs` :

```csharp
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
```

`ConversionService.cs` :

```csharp
using PhotoApp.Core.Models;
using PhotoApp.Core.Services;

namespace PhotoApp.Operations;

public sealed class ConversionService
{
    private readonly BatchRenameExecutor _executor;

    public ConversionService()
        : this(new BatchRenameExecutor())
    {
    }

    public ConversionService(BatchRenameExecutor executor)
    {
        _executor = executor;
    }

    public ConversionSummary Run(
        IReadOnlyList<ImageFileInfo> images,
        string outputFolder,
        string manifestPath,
        int jpegQuality,
        bool deleteOriginals)
    {
        Directory.CreateDirectory(outputFolder);

        var manifest = ProcessedManifest.Load(manifestPath);
        manifest.PruneMissingTargets(outputFolder);

        var inputs = new List<PlanInput>();
        var skippedUnreliable = 0;
        var skippedUnsupported = 0;

        foreach (var image in images)
        {
            if (image.DateMetadata is not { IsReliable: true, RetainedCandidate: { } date })
            {
                skippedUnreliable++;
                continue;
            }

            if (!ImageConverter.CanConvert(image.Extension))
            {
                skippedUnsupported++;
                continue;
            }

            inputs.Add(new PlanInput(image.FullPath, date, image.FileSizeBytes, File.GetLastWriteTimeUtc(image.FullPath).Ticks));
        }

        var existingNames = Directory.EnumerateFiles(outputFolder).Select(path => Path.GetFileName(path)!).ToList();
        var plans = new RenamePlanner().PlanRenames(inputs, manifest.ToPrevious(), existingNames);

        IReadOnlyList<RenameResult> results;
        try
        {
            results = _executor.Execute(plans, outputFolder, jpegQuality, deleteOriginals, manifest);
        }
        finally
        {
            manifest.Save(manifestPath);
        }

        return new ConversionSummary(
            Converted: results.Count(r => r.Success && !r.Skipped),
            AlreadyDone: results.Count(r => r.Skipped),
            SkippedUnreliableDate: skippedUnreliable,
            SkippedUnsupportedFormat: skippedUnsupported,
            Failed: results.Count(r => !r.Success),
            FirstError: results.FirstOrDefault(r => !r.Success)?.ErrorMessage);
    }
}
```

Note : `Execute` journalise le manifeste en mémoire ; l'écriture disque se fait dans le `finally`, donc même une exception d'un fichier n'efface pas ce qui a réussi.

- [ ] **Étape 5 : Vérifier**

Run : `dotnet test PhotoVisualizer.slnx`
Attendu : tout passe, en particulier `SecondPassOnTheSameFolder_ProducesNoErrorAndNoDuplicate`.

Si le test d'intégration échoue sur `first.Converted`/noms : vérifier d'abord que `TestImages.WriteJpeg(..., "2023:03:04 13:20:15")` est bien relu par `NativeImageMetadataReader` (test de la tâche 4), puis que le fichier `no-date-at-all.png` n'obtient pas de date de nom (son nom ne contient pas de date) et retombe bien sur `FileSystem` (non fiable).

- [ ] **Étape 6 : Commit**

```bash
git add -A
git commit -m "fix: exécution sans orphelins, manifeste de sortie, second passage idempotent" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 11 : Interface non bloquante et branchement final (défaut 7)

**Fichiers :**
- Modifier : `PhotoApp/ViewModels/MainWindowViewModel.cs` (réécriture), `PhotoApp/Views/MainWindow.axaml.cs`, `PhotoApp/App.axaml.cs` (si nécessaire pour `EnsureCreated`)

**Interfaces :**
- Consomme : `ImageScanner.Scan(root, ScanOptions)`, `ImageDateResolver.Resolve`, `ConversionService.Run`, `AppDataPaths.ForCurrentUser()` / `ManifestPathFor` / `EnsureCreated`.
- Produit : `MainWindowViewModel.SetSelectedFolderAndScanAsync(string path) : Task` (remplace `SetSelectedFolderAndScan`). Les propriétés liées en AXAML gardent leurs noms.

Le ViewModel n'a pas de test unitaire (il dépend d'Avalonia/CommunityToolkit et toute la logique métier est déjà testée dans `ConversionServiceTests`) ; la vérification se fait par compilation et exécution (étape 3).

- [ ] **Étape 1 : Réécrire le ViewModel**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoApp.Catalog;
using PhotoApp.Core;
using PhotoApp.Core.Models;
using PhotoApp.Metadata;
using PhotoApp.Operations;

namespace PhotoApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const string OutputFolderName = "Converted";
    private const int JpegQuality = 90;

    private readonly ImageScanner _imageScanner = new();
    private readonly ImageDateResolver _imageDateResolver = new();
    private readonly ConversionService _conversionService = new();
    private readonly AppDataPaths _dataPaths = AppDataPaths.ForCurrentUser();

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
            var resolved = await Task.Run(() => ScanAndResolve(root));

            Images.Clear();
            foreach (var image in resolved)
            {
                Images.Add(image);
            }

            ImageCount = Images.Count;
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

    private List<ImageFileInfo> ScanAndResolve(string root)
    {
        var outputFolder = Path.Combine(root, OutputFolderName);
        var scanned = _imageScanner.Scan(root, new ScanOptions([outputFolder]));
        var resolved = new ImageFileInfo[scanned.Count];

        System.Threading.Tasks.Parallel.For(0, scanned.Count, index =>
        {
            var image = scanned[index];
            resolved[index] = new ImageFileInfo(
                image.FullPath,
                image.FileName,
                image.Extension,
                image.FileSizeBytes,
                _imageDateResolver.Resolve(image.FullPath));
        });

        return resolved.OrderBy(image => image.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    partial void OnSelectedFolderPathChanged(string? value) => RefreshCommandState();

    partial void OnIsScanningChanged(bool value) => RefreshCommandState();

    partial void OnIsConvertingChanged(bool value) => RefreshCommandState();

    partial void OnImageCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasNoImages));
        RefreshCommandState();
    }

    private void RefreshCommandState()
    {
        OnPropertyChanged(nameof(CanConvertNow));
        ConvertCommand.NotifyCanExecuteChanged();
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
}
```

- [ ] **Étape 2 : Adapter la vue**

`MainWindow.axaml.cs` : remplacer `viewModel.SetSelectedFolderAndScan(selectedPath);` par `await viewModel.SetSelectedFolderAndScanAsync(selectedPath);` (la méthode `ChooseFolderButton_OnClick` est déjà `async void`).

- [ ] **Étape 3 : Compiler, tester, lancer**

Run : `dotnet build PhotoVisualizer.slnx && dotnet test PhotoVisualizer.slnx --no-build`
Attendu : build sans erreur ni avertissement de sécurité ; tous les tests passent.

Vérification manuelle (invoquer le skill `run` pour lancer l'application) sur **une copie** de quelques photos — jamais sur la bibliothèque réelle. Résoudre le dossier Images réel par Known Folder (`pwsh -NoProfile -c "[Environment]::GetFolderPath('MyPictures')"`), copier une vingtaine de fichiers variés (JPEG, PNG, une capture Windows) dans un dossier du scratchpad, puis :
1. Choisir ce dossier : l'interface reste réactive pendant le scan, la liste s'affiche, les fichiers sans date fiable montrent « À vérifier ».
2. Cliquer sur Convertir : le résumé affiche les compteurs.
3. Cliquer une seconde fois sur Convertir (ou rechoisir le dossier puis convertir) : le résumé affiche `0 converti(s)` et `N déjà fait(s)`, aucun nouveau fichier dans `Converted`.
4. Vérifier que `%LOCALAPPDATA%\PhotoManager\manifests\` contient un fichier `.json` et qu'aucun fichier n'a été écrit à côté des photos hors de `Converted`.

- [ ] **Étape 4 : Commit**

```bash
git add -A
git commit -m "fix: scan et résolution des dates hors du thread de l'interface; conversion via ConversionService" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Tâche 12 : Intégration continue sur 3 OS et documentation

**Fichiers :**
- Créer : `.github/workflows/ci.yml`
- Modifier : `README.md` (sections « Ce qui marche », « Limites », architecture)

**Interfaces :** aucune.

- [ ] **Étape 1 : Workflow**

`.github/workflows/ci.yml` :

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-test:
    name: build + test (${{ matrix.os }})
    runs-on: ${{ matrix.os }}
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest, macos-latest]
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Build
        run: dotnet build PhotoVisualizer.slnx --configuration Release

      - name: Test
        run: dotnet test PhotoVisualizer.slnx --configuration Release --no-build

      - name: Vulnerable packages
        shell: bash
        run: |
          dotnet list PhotoVisualizer.slnx package --vulnerable --include-transitive | tee vulnerable.txt
          if grep -q "has the following vulnerable packages" vulnerable.txt; then
            echo "Vulnerable packages found"; exit 1
          fi
```

- [ ] **Étape 2 : Vérification locale du workflow**

Run : `dotnet build PhotoVisualizer.slnx --configuration Release && dotnet test PhotoVisualizer.slnx --configuration Release --no-build && dotnet list PhotoVisualizer.slnx package --vulnerable --include-transitive`
Attendu : tout vert, « aucun package vulnérable ». (L'exécution Linux/macOS ne peut être confirmée qu'une fois la branche poussée : voir étape 4.)

- [ ] **Étape 3 : README**

Dans `README.md` : mettre à jour la liste « Ce qui marche aujourd'hui » (heure murale, nommage `-NN` stable, passage idempotent, scan non bloquant, dossier de sortie exclu, formats HEIC explicitement ignorés) ; remplacer la mention « sans ExifTool au runtime » par la décision de la spec (ExifTool embarqué à partir de la phase 3) ; ajouter un paragraphe « Données de l'application » (emplacement du manifeste via `AppDataPaths`) et la liste des 6 projets ; remplacer la limite « Gestion déterministe des collisions via suffixe court stable » par le nouveau schéma `IMG-AAAAMMJJ-HHmmss[-NN].jpg`.

- [ ] **Étape 4 : Commit, puis demander avant de pousser**

```bash
git add -A
git commit -m "ci: build et tests sur Windows, Linux et macOS; documentation de la phase 0" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Ne pas pousser. Demander à l'utilisateur s'il veut pousser `feat/phase0-fondations` (et `docs/vision-feuille-de-route`) vers le remote `photomanager` afin de valider le critère « tests verts sur les 3 OS », puis utiliser le skill `superpowers:finishing-a-development-branch`.

---

## Auto-revue contre la spec (§10 phase 0)

| Exigence de la spec | Tâche |
|---|---|
| Défaut 1 : scan inclut `Converted/` | 8 (+ test d'intégration 10) |
| Défaut 2 : HEIC/HEIF/WebP rejetés | 9 (Magick.NET : HEIC, HEIF, AVIF, WebP convertis ; GIF exclu) |
| Défaut 3 : heure EXIF marquée UTC, XMP converti en UTC | 4 |
| Défaut 4 : `LastWriteTime` présenté comme fiable | 5 |
| Défaut 5 : nettoyage avec nouveau `Guid` | 10 |
| Défaut 6 : trous du parseur (captures Windows, 12 h, epoch, ambiguïté JJ/MM, Nextcloud iOS) | 6 (Nextcloud iOS déjà reconnu, test conservé) |
| Défaut 7 : scan sur le thread UI | 11 |
| Défaut 8 : ImageSharp vulnérable | 1 (remplacé par Magick.NET) |
| Défaut 9 : hash de collision sur le chemin | 7 |
| Découpage en projets (§4), hors `Cli` | 2 (Cli → phase 1, indiqué plus haut) |
| Racines de données centralisées (§9) | 3, utilisée en 11 |
| Corpus de test initial | 4 (`TestImages`), 6, 10 — généré à l'exécution (écart assumé) |
| CI sur 3 OS | 12 |
| Critère : flux fiable sur JPEG et PNG | 10 (`SecondPassOnTheSameFolder…`), 11 (vérification manuelle) |
| Critère : 2ᵉ passage sans erreur ni doublon | 10 |
| Critère : tests verts sur 3 OS | 12, confirmé seulement après push |

**Cohérence des types :** `ImageDateCandidate(DateTime, bool, TimeSpan?)` (4) est utilisé tel quel en 5, 6, 7, 10 ; `RenamePlan` change en 4 (3 champs) puis en 7 (6 champs) — les tests d'exécuteur sont mis à jour en 7 (`Noon, 0, 0`) puis 10 (`Noon, 1, 1`) ; `NormalizedFileNameBuilder.Build(...)` (4) est supprimé en 7 et remplacé par `Preview/BaseName/NormalizeExtension`, ses derniers appelants (`ImageDateResolver`, tests) sont portés en 7.

**Risques connus :** (a) `ExifTag.OffsetTimeOriginal` peut avoir un autre nom selon la version d'ImageSharp (solution de repli indiquée tâche 4) ; (b) le comportement de `MetadataExtractor` sur les PNG sans EXIF doit renvoyer `null` (couvert par un test, tâche 4) ; (c) les chemins de test en `/p/a.png` ne touchent pas le disque (planificateur pur).
