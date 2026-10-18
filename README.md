# photo-visualizer (PhotoManager)

Application desktop locale, hors ligne, pour normaliser les dates et les noms de photos personnelles, les parcourir et les convertir en JPEG sans perdre leurs métadonnées. Développée en priorité pour Windows, portable sur Linux et macOS.

- **Vision et feuille de route :** [`docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md`](docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md)
- **Phase en cours :** phase 0 (Fondations) terminée — plan : [`docs/superpowers/plans/2026-10-18-phase0-fondations.md`](docs/superpowers/plans/2026-10-18-phase0-fondations.md). Prochaine étape : phase 1, moteur de normalisation en lecture seule et commande `analyse`.

## Ce qui marche aujourd'hui

- Sélection d'un dossier, puis scan récursif **en arrière-plan** (l'interface reste réactive) et lecture des dates en parallèle.
- Le scan ignore le dossier de sortie `Converted`, les dossiers et fichiers cachés (caches d'applications comme `.gs`) et les fichiers de 0 octet.
- Date retenue par image, dans cet ordre : métadonnées (EXIF `DateTimeOriginal` + `OffsetTimeOriginal`, puis XMP de prise de vue), puis nom de fichier, puis date du fichier.
  - La date est une **heure murale** (heure locale du lieu de prise de vue) avec son décalage UTC quand il est connu. Elle n'est plus convertie en faux UTC.
  - Les dates implausibles (avant 1990, futur, horloges remises à zéro `2000-01-01` / `1970-01-01`) sont écartées.
  - La date du fichier (souvent la date de copie) n'est **jamais** appliquée : le fichier est signalé « À vérifier ».
- Noms reconnus : appareils Android (`IMG_20230304_132015`, `20260917_222944`, Pixel `PXL_…`), WhatsApp (`IMG-20151114-WA0017`, jour seul ; `WhatsApp Image … at …`), captures Android, Windows (`Screenshot 2025-08-27 211650`), GNOME (`Capture d'écran du …`), macOS en 12 h et en français, Nextcloud iOS (`24-07-11 17-07-10 0477`), exports epoch WeChat/Facebook. Une date `JJ-MM-AAAA` / `MM-JJ-AAAA` indécidable n'est pas devinée.
- Nommage : `IMG-AAAAMMJJ-HHmmss.jpg` ; même seconde : `-01`, `-02`… dans l'ordre chronologique puis alphabétique ; jour seul : `IMG-AAAAMMJJ-000000-NN.jpg`. Un nom déjà attribué n'est jamais renuméroté.
- Conversion JPEG via Magick.NET : JPEG copié tel quel ; PNG, WebP, TIFF, BMP, **HEIC, HEIF, AVIF** décodés, orientés, transparence sur fond blanc, profils EXIF/XMP/ICC conservés. GIF non converti (animation).
- **Passage idempotent** : un manifeste par dossier de sortie mémorise ce qui a déjà été converti ; relancer sur le même dossier ne produit ni erreur ni doublon. Un fichier de sortie supprimé est reconverti sous le même nom.
- Exécution par fichier sans fichier orphelin : copie de travail `.work-…`, conversion, dates du fichier, déplacement final ; un échec n'arrête pas le lot. Résumé détaillé (convertis, déjà faits, ignorés, échecs).
- Intégration continue : build, tests et audit des paquets vulnérables sur Windows, Linux et macOS.

## Limites connues

- Pas encore d'écriture des métadonnées internes (phase 3, ExifTool embarqué) : la date est appliquée aux dates du fichier de sortie.
- Une heure EXIF sans décalage est interprétée dans le fuseau du PC ; le décalage horaire par lot (ex. photos de voyage) arrive en phase 3.
- L'empreinte d'idempotence est la taille + la date de modification de la source ; le SHA-256 arrive avec les doublons (phase 5).
- Une conversion HEIC ou AVIF → JPEG produit souvent un fichier plus lourd : c'est voulu, la compatibilité prime. La règle « ignorer si plus lourd » (phase 5) ne s’appliquera pas à HEIC/HEIF/AVIF.
- Pas encore de galerie, de journal, d'annulation ni de quarantaine (phases 2 et 4). L'option « Effacer l'original après succès » supprime réellement l'original : la laisser décochée pour tout essai.

## Architecture

```text
PhotoApp/              interface Avalonia (MVVM)
PhotoApp.Core/         pur, sans E/S de photo ni paquet : modèles, parseur de noms, choix de la date,
                       nommage, planification, racines de données (AppDataPaths)
PhotoApp.Metadata/     lecture des métadonnées (MetadataExtractor)
PhotoApp.Catalog/      scan des dossiers
PhotoApp.Operations/   conversion (Magick.NET), exécution du lot, manifeste, ConversionService
PhotoApp.Tests/        tests xUnit ; images d'exemple générées à l'exécution (aucune donnée privée)
```

## Données de l'application

Tout ce que l'application produit, hors dossier `Converted`, vit sous des racines centralisées (`PhotoApp.Core/AppDataPaths.cs`) :

| | Windows | Linux | macOS |
|---|---|---|---|
| Données (manifestes, futur catalogue) | `%LOCALAPPDATA%\PhotoManager\` | `$XDG_DATA_HOME/photomanager/` | `~/Library/Application Support/PhotoManager/` |
| Cache | `%LOCALAPPDATA%\PhotoManager\cache\` | `$XDG_CACHE_HOME/photomanager/` | `~/Library/Caches/PhotoManager/` |

Les manifestes sont dans `manifests/`, un fichier JSON par dossier de sortie.

## Développement

Prérequis : .NET 10 SDK. Aucune autre installation (les binaires natifs de Magick.NET sont fournis par NuGet).

```powershell
dotnet build PhotoVisualizer.slnx
dotnet test PhotoVisualizer.slnx
dotnet run --project .\PhotoApp\PhotoApp.csproj
```

Publication Windows autonome :

```powershell
dotnet publish .\PhotoApp\PhotoApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# ou, publication + lancement :
.\run-published.ps1
```

## Test manuel recommandé

1. Copier quelques photos variées dans un dossier de test (jamais sur la bibliothèque réelle).
2. Lancer l'application et choisir ce dossier : vérifier dates, sources, noms cibles et les mentions « À vérifier ».
3. Convertir **sans** suppression des originaux, contrôler `Converted`.
4. Convertir une seconde fois : le résumé doit indiquer `0 converti(s)` et tous les fichiers « déjà faits ».

## Décisions actées

Voir la spec (§2) : .NET 10 + Avalonia 12 ; lecture native via MetadataExtractor ; écriture des métadonnées via ExifTool embarqué à partir de la phase 3 (sans installation pour l'utilisateur) ; décodage d'images via Magick.NET ; catalogue SQLite ; exécution graduée (plan → aperçu → application, journal, annulation, quarantaine) ; heure murale, format 24 h ; nommage `IMG-`/`VID-AAAAMMJJ-HHmmss`.
