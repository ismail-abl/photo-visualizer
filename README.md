# photo-visualizer

Application desktop locale pour scanner, analyser, convertir et renommer des photos personnelles en lot, avec priorité à la sécurité des données, au traitement hors ligne et à un exécutable Windows autonome.

## État actuel du projet

Le projet a dépassé le stade de prototype technique. La chaîne principale fonctionne sur Windows avec un build `self-contained` `single-file` validé en conditions réelles : sélection d’un dossier, scan automatique, résolution de date, prévisualisation du nom cible, conversion/renommage depuis l’interface, et suppression optionnelle de l’original après succès complet.

### Ce qui marche aujourd’hui

- Sélection d’un dossier depuis l’interface graphique Avalonia.
- Scan récursif automatique du dossier choisi et de ses sous-dossiers.
- Détection des images prises en charge par le scanner.
- Résolution d’une date retenue par image avec priorité explicite :
  1. métadonnées natives lisibles ;
  2. date parsée depuis le nom de fichier ;
  3. date du système de fichiers en fallback.
- Affichage dans l’UI de :
  - nom source ;
  - chemin complet ;
  - taille ;
  - date retenue ;
  - source de la date ;
  - nom cible proposé.
- Génération d’un nom de sortie normalisé au format `IMG-YYYYMMDD-HHmmss.jpg` ou `IMG-YYYYMMDD-xxxxxx.jpg` selon les données disponibles.
- Gestion déterministe des collisions de noms via suffixe court stable.
- Pipeline de sortie sécurisé :
  1. copie de travail ;
  2. conversion JPEG ;
  3. application des timestamps filesystem ;
  4. déplacement vers le nom final ;
  5. suppression optionnelle de l’original uniquement après succès complet.
- Exécution de la conversion depuis l’UI avec résumé de succès/échecs.
- Build Windows publié en `Release`, `win-x64`, `self-contained`, `single-file` testé et validé.
- Suite de tests unitaires en place sur les briques critiques.

### Ce qui a été validé manuellement

- Le choix d’un dossier suffit à lancer le scan ; le bouton de scan manuel a été retiré.
- Le bouton de conversion est désormais piloté par un état calculé cohérent côté ViewModel.
- Un test réel sur un dossier préparé exprès a été exécuté avec succès via l’exécutable publié.
- Le bug du fichier temporaire `.tmp` a été corrigé en conservant l’extension réelle de la source pendant la phase intermédiaire.
- Le package `SixLabors.ImageSharp` a été mis à jour pour supprimer les avertissements de sécurité observés pendant la publication.

## Ce qui ne marche pas encore ou reste limité

Le projet n’est pas “terminé”, même si la v1 utile est désormais atteinte.

### Limites actuelles

- L’écriture des métadonnées internes EXIF/XMP/IPTC n’est **pas** encore implémentée.
  - La date retenue est aujourd’hui réappliquée au **fichier de sortie côté filesystem**.
  - La métadonnée intégrée dans l’image n’est pas encore réécrite.
- Le support HEIC/HEIF n’est **pas garanti** pour la conversion JPEG dans l’état actuel.
  - La lecture de date peut fonctionner selon les cas.
  - La conversion réelle dépendra d’une brique complémentaire si nécessaire.
- Il n’y a pas encore de galerie avancée, de miniatures persistées, ni de filtrage riche.
- Il n’y a pas encore de mode d’annulation d’un batch en cours.
- Il n’y a pas encore de journal d’exécution détaillé exportable.
- Il n’y a pas encore de gestion avancée des doublons par contenu.
- Le panneau debug affiché dans l’UI est temporaire et devra être remplacé par un affichage d’état plus propre.

## Objectif produit actuel

L’objectif immédiat n’est plus de prouver que l’application peut démarrer, mais de stabiliser une v1 locale utile, sûre et maintenable pour traiter de vrais dossiers d’images personnelles.

En pratique, la version actuelle permet déjà de :
- sélectionner un dossier ;
- scanner les images ;
- calculer une date retenue ;
- proposer un nom normalisé ;
- produire une copie convertie/renommée en JPEG ;
- conserver ou supprimer l’original selon l’option choisie.

## Stack technique retenue

### Application

- **.NET 10**
- **Avalonia UI** pour l’interface desktop
- **CommunityToolkit.Mvvm** pour le ViewModel et les commandes

### Cœur métier

- `PhotoApp.Core` pour la logique de scan, de résolution de date, de planification de renommage et d’exécution batch
- `PhotoApp.Tests` pour les tests unitaires

### Lecture de métadonnées

- Lecture native en .NET, sans dépendance runtime externe à ExifTool

### Conversion d’image

- Conversion JPEG basée sur `SixLabors.ImageSharp`
- Copie directe pour les sources déjà JPEG afin d’éviter une recompression inutile

## Architecture du dépôt

```text
photo-visualizer/
├── PhotoApp/
│   ├── Views/
│   ├── ViewModels/
│   ├── Converters/
│   ├── Assets/
│   └── PhotoApp.csproj
├── PhotoApp.Core/
│   ├── Models/
│   ├── Services/
│   └── PhotoApp.Core.csproj
├── PhotoApp.Tests/
│   └── PhotoApp.Tests.csproj
├── run-published.ps1
└── README.md
```

## Fonctionnement actuel

### Résolution de date

Pour chaque image, l’application tente de déterminer une date de référence avec cette priorité :

1. métadonnées natives lisibles ;
2. date détectée dans le nom de fichier ;
3. date filesystem ;
4. aucune date si rien n’est exploitable.

La source retenue est affichée dans l’interface pour rendre la décision visible.

### Nommage cible

La convention actuelle de sortie est la suivante :

- `IMG-YYYYMMDD-HHmmss.jpg` si la date et l’heure sont connues ;
- `IMG-YYYYMMDD-HHmmss-xxxxxx.jpg` si collision ;
- `IMG-YYYYMMDD-xxxxxx.jpg` si seule la date est connue.

Le suffixe court est déterministe afin de garder un comportement stable entre deux traitements identiques.

### Pipeline de conversion

Pour chaque fichier traité :

1. création d’une copie de travail ;
2. conservation de l’extension source sur le fichier temporaire ;
3. conversion vers JPEG si nécessaire ;
4. application des timestamps filesystem ;
5. déplacement vers le nom final dans le dossier de sortie ;
6. suppression optionnelle de l’original après validation complète.

Par défaut, les sorties sont écrites dans un sous-dossier `Converted` du dossier sélectionné.

## Ce qu’il faudrait encore implémenter

### Priorité haute

- Remplacer le panneau debug par un vrai panneau d’état utilisateur.
- Ajouter un résumé d’exécution plus propre, éventuellement avec liste des erreurs.
- Ajouter un vrai journal local des opérations batch.
- Clarifier le comportement des formats non supportés dans l’UI.
- Ajouter des tests supplémentaires sur des jeux réels de noms de fichiers.

### Priorité moyenne

- Support plus explicite de HEIC/HEIF, soit par intégration, soit par exclusion assumée.
- Écriture des métadonnées internes EXIF/XMP si cela peut être fait sans casser la contrainte de packaging.
- Gestion des miniatures et d’un aperçu plus visuel.
- Filtres par source de date, extension, dossier, état de conversion.
- Détection de doublons.

### Priorité basse / plus tard

- Cache local SQLite.
- Historique des batches.
- Annulation d’une opération longue.
- Portage et validation Linux/macOS.

## Sécurité des données

Le projet applique actuellement quelques règles simples mais importantes :

- ne jamais modifier directement le fichier original ;
- travailler sur une copie ;
- ne supprimer l’original qu’après succès complet ;
- afficher les noms cibles avant traitement ;
- produire les sorties dans un sous-dossier dédié.

Pour tout test sur de vraies photos, il est recommandé de commencer avec l’option **Effacer l’original après succès** décochée.

## Développement

### Prérequis

- Windows 10/11
- .NET 10 SDK installé
- PowerShell pour le script de publication

### Build de développement

```powershell
dotnet build .\PhotoApp\PhotoApp.csproj
```

### Lancer l’application en développement

```powershell
dotnet run --project .\PhotoApp\PhotoApp.csproj
```

### Exécuter les tests

```powershell
dotnet test .\PhotoApp.Tests\PhotoApp.Tests.csproj
```

### Publier l’exécutable Windows autonome

```powershell
dotnet publish .\PhotoApp\PhotoApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

L’exécutable publié se trouve ici :

```text
PhotoApp\bin\Release\net10.0\win-x64\publish\PhotoApp.exe
```

### Script pratique de publication + lancement

À la racine du dépôt :

```powershell
.\run-published.ps1
```

Ce script publie l’application en `Release win-x64 self-contained single-file`, puis lance l’exécutable publié.

## Test manuel recommandé

Pour valider l’application sans risque :

1. créer un dossier de test avec des copies d’images ;
2. lancer l’exécutable publié ;
3. choisir le dossier ;
4. vérifier les dates retenues et les noms cibles ;
5. lancer une première conversion **sans suppression des originaux** ;
6. vérifier le contenu du dossier `Converted` ;
7. refaire un second test avec suppression activée seulement après validation du résultat.

## Décisions de conception actées

- pas de backend, pas de cloud ;
- application desktop locale uniquement ;
- priorité Windows pour la v1 ;
- exécutable autonome Windows comme livrable principal ;
- pas d’ExifTool au runtime ;
- séparation simple entre UI, core métier et tests ;
- priorité à la lisibilité et à la sécurité des fichiers.

## Règles de travail avec une IA de code

Chaque prompt destiné à une IA de code doit rappeler :

1. le contexte du projet ;
2. l’état actuel réel de l’application ;
3. la contrainte de packaging autonome ;
4. la priorité à la sécurité des données ;
5. l’objectif précis de la tâche demandée.

Bloc recommandé à réutiliser dans les prompts :

```text
Contexte projet:
Application desktop locale Avalonia UI + .NET 10.
Objectif: harmoniser des photos en local, hors ligne, avec packaging Windows autonome.
Le projet doit rester portable vers Linux/macOS.
Aucune dépendance runtime externe type ExifTool.

État actuel de l'app:
- sélection d’un dossier => scan automatique
- affichage dans l’UI de:
  - nom source
  - chemin
  - date retenue
  - source de date
  - nom cible proposé
- bouton Convertir raccordé à l’UI
- case "Effacer l’original après succès"
- pipeline Core fonctionnel:
  - RenamePlanner
  - BatchRenameExecutor
  - ImageConverter
  - FileTimestampApplier
- bug du fichier temporaire `.tmp` corrigé
- publication Windows self-contained single-file validée
- test réel validé avec l’exécutable publié
```

## Résumé

Le projet est maintenant à un stade où une **v1 réellement utile** existe sur Windows. Le cœur du flux utilisateur fonctionne, mais plusieurs améliorations importantes restent à faire avant d’avoir une application vraiment confortable et plus universelle, en particulier sur l’UX, la journalisation, les formats avancés et l’écriture de métadonnées internes.