# photo-visualizer (PhotoManager)

Application desktop locale, hors ligne, pour normaliser les dates et les noms de photos personnelles, les parcourir et les convertir en JPEG sans perdre leurs métadonnées. Développée en priorité pour Windows, portable sur Linux et macOS.

- **Vision et feuille de route :** [`docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md`](docs/superpowers/specs/2026-10-14-vision-feuille-de-route-design.md)
- **Phase en cours :** phase 1 (moteur de normalisation, lecture seule) implémentée sur la branche `feat/phase1-normalisation`, en attente de validation : objectif de 94 % Sûre + Probable atteint sur la bibliothèque réelle, vérification visuelle de l'interface à faire — spec : [`docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md`](docs/superpowers/specs/2026-10-19-phase1-moteur-normalisation-design.md), plan : [`docs/superpowers/plans/2026-10-19-phase1-moteur-normalisation.md`](docs/superpowers/plans/2026-10-19-phase1-moteur-normalisation.md). Prochaine étape : phase 2, exécution sûre (renommage sur place, journal, annulation).

## Ce qui marche aujourd'hui

- Sélection d'un dossier, puis scan récursif **en arrière-plan** (l'interface reste réactive) et lecture des dates en parallèle.
- Le scan ignore le dossier de sortie `Converted`, les dossiers et fichiers cachés (caches d'applications comme `.gs`) et les fichiers de 0 octet.
- **Moteur de normalisation unique**, partagé par l'interface et la CLI : il lit tous les candidats (EXIF avec décalage et sous-secondes, GPS, XMP et sidecar `.xmp`, dates Apple QuickTime avec leur décalage, en-tête `mvhd` des vidéos, nom de fichier, date du fichier), reconnaît la source par un **profil** (WhatsApp, Signal, Telegram, Samsung, Xiaomi, Pixel, Huawei, OnePlus, iPhone et Nextcloud iOS (y compris les noms à suffixe hexadécimal), captures Android/KDE, captures de jeu (AMD, Steam…) et images collées Obsidian, Windows, GNOME, macOS, exports WeChat/Facebook/epoch, Snapchat…), confronte les candidats et donne une **confiance** expliquée par une raison : Sûre, Probable (appliquées), Incertaine (« À vérifier »), Aucune.
  - WhatsApp : le nom fait foi ; un EXIF antérieur ou du même jour l'emporte (photo transférée).
  - Dates JJ/MM ambiguës tranchées par les fichiers voisins ou par un autre candidat, sinon signalées.
  - La date du fichier (souvent celle de la copie) n'est jamais appliquée.
- **Commande `photoapp analyse`** en lecture seule : rapport CSV (une ligne par média : profil, date, confiance, raison, nom proposé, tous les candidats) et résumé (répartition, profils, motifs de nom inconnus).
- **Noms proposés** : `IMG-…` pour les photos, `VID-…` pour les vidéos, Live Photo associées (même nom de base), collisions numérotées dans l'ordre réel (sous-secondes, compteur WhatsApp, empreinte), vrai format détecté.
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
- Les outils manuels (décalage horaire par lot, date fixée, choix d'un candidat) arrivent en phase 3 ou 4 ; les sidecars Google Takeout en phase 8.
- L'interface ignore encore les vidéos (renommage sur place et vidéos : phase 2). La CLI les analyse.

## Architecture

```text
PhotoApp/              interface Avalonia (MVVM)
PhotoApp.Core/         pur, sans E/S ni paquet : modèles, profils de sources (Profiles), moteur de normalisation
                       (Normalization : comparaison, arbitre, ambiguïtés, nommage), racines de données (AppDataPaths)
PhotoApp.Metadata/     lecture des métadonnées brutes (MetadataExtractor + clés Apple QuickTime)
PhotoApp.Catalog/      scan des dossiers (photos, vidéos, sidecars) et analyse complète (MediaAnalyzer)
PhotoApp.Cli/          commande « analyse » en lecture seule (ne référence jamais Operations)
PhotoApp.Operations/   conversion (Magick.NET), exécution du lot, manifeste, ConversionService
PhotoApp.Tests/        tests xUnit ; images d'exemple générées à l'exécution (aucune donnée privée)
```

## Données de l'application

Tout ce que l'application produit, hors dossier `Converted`, vit sous des racines centralisées (`PhotoApp.Core/AppDataPaths.cs`) :

| | Windows | Linux | macOS |
|---|---|---|---|
| Données (manifestes, rapports d'analyse, futur catalogue) | `%LOCALAPPDATA%\PhotoManager\` | `$XDG_DATA_HOME/photomanager/` | `~/Library/Application Support/PhotoManager/` |
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

## Analyser une bibliothèque (lecture seule)

```powershell
dotnet run --project PhotoApp.Cli -c Release -- analyse "D:\Photos" "D:\Vidéos" --exclude "D:\Photos\Converted"
```

- Rien n'est écrit à côté des photos. Le rapport va dans `%LOCALAPPDATA%\PhotoManager\reports\analyse-AAAAMMJJ-HHmmss.csv` (ou `--out fichier.csv`).
- Le CSV s'ouvre directement dans Excel ou LibreOffice (UTF-8 avec BOM, séparateur `;`).
- Codes de sortie : 0 analyse faite (même avec des fichiers illisibles, listés dans le rapport), 1 argument invalide, 2 erreur fatale.

## Test manuel recommandé

1. Copier quelques photos variées dans un dossier de test (jamais sur la bibliothèque réelle).
2. Lancer l'application et choisir ce dossier : vérifier dates, sources, noms cibles et les mentions « À vérifier ».
3. Convertir **sans** suppression des originaux, contrôler `Converted`.
4. Convertir une seconde fois : le résumé doit indiquer `0 converti(s)` et tous les fichiers « déjà faits ».

## Décisions actées

Voir la spec (§2) : .NET 10 + Avalonia 12 ; lecture native via MetadataExtractor ; écriture des métadonnées via ExifTool embarqué à partir de la phase 3 (sans installation pour l'utilisateur) ; décodage d'images via Magick.NET ; catalogue SQLite ; exécution graduée (plan → aperçu → application, journal, annulation, quarantaine) ; heure murale, format 24 h ; nommage `IMG-`/`VID-AAAAMMJJ-HHmmss`.
