# photo-visualizer
visualise, convert, harmonize metada

## Vision du projet

PhotoApp est une application de bureau pensée pour traiter un volume d’environ 10 000 images stockées localement, sans cloud ni serveur distant. L’objectif principal est de fournir un outil pratique pour remettre de l’ordre dans une collection de photos personnelles en détectant les fichiers image, en normalisant les dates associées et en proposant une compression contrôlée sans sacrifier inutilement la qualité visuelle.[1][2][3]

Le projet vise en priorité Windows avec production d’un exécutable autonome, tout en gardant une base de code portable vers Linux et macOS. L’interface graphique repose sur Avalonia, qui cible les trois plateformes desktop depuis une même base .NET, tandis que la publication self-contained/single-file permet d’éviter l’installation préalable du runtime .NET côté utilisateur.[1][4][5][6]

## Objectifs fonctionnels

Le produit couvre quatre blocs fonctionnels principaux :

- exploration récursive d’un dossier racine et de ses sous-dossiers ;
- détection des formats d’image courants, en particulier JPEG, PNG et HEIF/HEIC ;
- compression batch avec estimation des gains et conservation optionnelle des originaux ;
- harmonisation des dates à partir des métadonnées EXIF, du nom de fichier ou de la date filesystem en dernier recours.[2][3][7]

Le projet doit aussi fournir une interface de prévisualisation simple, avec filtres par date, dossier et source, ainsi qu’un aperçu avant/après des métadonnées quand une opération d’harmonisation est proposée. L’application est pensée comme un outil personnel fiable, pas comme un service en ligne ni comme une galerie cloud.[1][8]

## Contraintes de départ

Les contraintes structurantes déjà fixées sont les suivantes :

- application **locale uniquement**, sans backend distant ;
- GUI desktop obligatoire ;
- priorité Windows pour la première livraison ;
- portabilité recherchée vers Linux et macOS ;
- exécutable autonome sans installation préalable de .NET sur la machine cible ;
- code lisible, simple à maintenir, sans architecture inutilement lourde.[1][5][6][8]

Un autre point important est l’exigence de packaging. Le projet cible un exécutable Windows publié en self-contained single-file, ce qui permet d’embarquer le runtime .NET directement dans l’application. En revanche, les dépendances natives tierces éventuelles doivent être choisies avec prudence, car elles compliquent le scénario “binaire unique” même si .NET lui-même n’est plus une dépendance externe.[5][9][10]

## Choix techniques retenus

### Plateforme applicative

Le socle choisi est **.NET 10 LTS** avec **Avalonia 12** pour l’interface graphique. Avalonia supporte les plateformes desktop Windows, Linux et macOS, avec un minimum .NET 8 pour desktop, ce qui rend l’usage de .NET 10 cohérent pour une base moderne et durable.[1][11][4]

Le mode de développement initial est **Windows + VS Code + .NET 10 SDK**. Les templates Avalonia officiels permettent de démarrer rapidement une application MVVM et l’écosystème VS Code dispose d’une extension Avalonia dédiée pour le support XAML.[11][12][13]

### Architecture logicielle

Le projet suit une séparation simple en couches :

| Projet | Rôle |
|---|---|
| `PhotoApp` | UI Avalonia, vues, view models, interactions utilisateur |
| `PhotoApp.Core` | logique métier pure, scan, règles de résolution des dates, cas limites |
| `PhotoApp.Tests` | tests unitaires et tests fonctionnels ciblés |

Cette séparation suit le pattern MVVM recommandé par Avalonia et permet de garder le code métier indépendant de l’interface graphique, ce qui simplifie la testabilité et le portage.[8][14][15]

### Traitement d’image

Pour le traitement batch lourd, la piste privilégiée est **libvips**, car la bibliothèque est reconnue pour ses performances et sa faible consommation mémoire sur le traitement d’images. Elle supporte de nombreux formats, dont JPEG, PNG et HEIF/HEIC selon les builds disponibles.[2][16]

Ce choix reste à valider rapidement contre la contrainte de packaging Windows single-file. libvips est performant, mais sa chaîne de dépendances natives peut compliquer un binaire strictement monolithique ; ce point doit donc être traité comme un risque technique dès les premiers jalons.[17][18]

### Métadonnées

Pour la lecture et l’écriture robustes des métadonnées, la piste prioritaire est **ExifTool**. ExifTool est spécialisé dans la lecture/écriture des métadonnées EXIF/XMP/IPTC et fonctionne sur Windows, macOS et Linux, ce qui en fait une brique très pertinente pour l’harmonisation temporelle.[3][7][19]

L’objectif n’est pas de disperser la logique de tags dans toute l’application. Au contraire, l’accès aux métadonnées doit être encapsulé dans un service dédié, afin de garder un comportement prévisible et testable face aux différences de formats, d’exports WhatsApp ou de sauvegardes cloud.[3][7]

## Priorités fonctionnelles

L’ordre de mise en œuvre retenu est volontairement progressif.

### 1. Valider le socle applicatif

La première étape consiste à sécuriser l’environnement de développement, la création de projet Avalonia, l’exécution locale et la publication Windows self-contained single-file. Cette étape a déjà été validée avec succès sur un projet Avalonia MVVM en `net10.0` publié en `win-x64`.[20][5][21]

### 2. Scanner les fichiers image

La deuxième étape consiste à implémenter un scan récursif robuste d’un dossier racine, avec filtrage des extensions supportées, gestion des erreurs d’accès, et retour d’un modèle simple décrivant les fichiers détectés. C’est la base de presque toutes les autres fonctionnalités, car la compression, la visualisation et l’harmonisation reposent toutes sur un inventaire fiable des fichiers image.[15][8]

### 3. Brancher l’UI sur le scan

Une fois le scan implémenté dans `PhotoApp.Core`, il faut le connecter à l’interface : sélection d’un dossier, lancement du scan, affichage de la liste, nombre de fichiers, taille cumulée et retours d’erreurs utilisateur simples. À ce stade, l’objectif est encore la robustesse et la lisibilité, pas l’optimisation avancée.[8][14]

### 4. Ajouter l’analyse des dates

Ensuite vient la résolution des dates à partir d’une stratégie claire :

1. date EXIF de prise de vue ;
2. date déduite du nom de fichier si exploitable ;
3. date de création/modification filesystem en dernier recours.

Cette règle doit être codée explicitement et produire, pour chaque image, une explication de la source retenue afin d’être affichable dans l’interface et vérifiable en test.[3][7]

### 5. Ajouter la compression

La compression vient après, car elle est potentiellement destructive. Elle devra proposer plusieurs niveaux de qualité, afficher une estimation du gain, permettre de conserver les originaux, et traiter les formats en fonction des capacités réelles de la brique image retenue.[2][16]

## Cas d’usage cibles

Le projet doit couvrir plusieurs cas concrets de photos personnelles :

- dossiers d’export WhatsApp, souvent avec noms de fichiers peu stables ;
- exports Google Photos, parfois accompagnés de fichiers JSON ;
- dossiers issus d’appareils photo ou smartphones avec EXIF fiables ;
- sauvegardes iCloud, OneDrive ou Dropbox recopiées localement ;
- dossiers hétérogènes avec doublons, sous-dossiers profonds et fichiers partiellement corrompus.

Le comportement attendu n’est pas de “deviner” silencieusement. L’application doit au contraire rendre visible la source d’information utilisée, signaler les conflits et éviter toute écriture massive non explicite sur les fichiers originaux.[3][7]

## Principes de conception

### Confidentialité

Toutes les opérations sont réalisées localement. Aucune synchronisation cloud, aucun backend web et aucun traitement distant ne font partie du produit cible. Cette contrainte protège la vie privée et réduit la surface de risque liée aux données personnelles.[1]

### Robustesse

Le scan et les opérations batch doivent être tolérants aux erreurs : un fichier illisible, un sous-dossier inaccessible ou une image corrompue ne doivent pas faire échouer l’ensemble du traitement. Chaque anomalie doit être journalisée et présentée de manière exploitable.[15]

### Lisibilité

Le projet doit rester simple. Pas d’architecture “enterprise”, pas de framework de plugins prématuré, pas de couche d’abstraction sans besoin concret. Le code métier doit rester direct, testable et compréhensible rapidement.[15][8]

### Sécurité des données

La compression et la réécriture des métadonnées doivent être conçues comme des opérations sensibles. Toute action modifiant les fichiers devra être précédée d’une prévisualisation, d’un résumé des changements et, si possible, d’une stratégie de sauvegarde ou de conservation des originaux.[3][7]

## Architecture de code proposée

Structure de dossier cible :

```text
PhotoApp/
├── PhotoApp.sln
├── PhotoApp/
│   ├── Assets/
│   ├── Converters/
│   ├── Views/
│   ├── ViewModels/
│   ├── Services/
│   ├── App.axaml
│   └── Program.cs
├── PhotoApp.Core/
│   ├── Models/
│   ├── Scanning/
│   ├── Metadata/
│   ├── Compression/
│   ├── Dates/
│   └── Abstractions/
└── PhotoApp.Tests/
    ├── Scanning/
    ├── Dates/
    └── Metadata/
```

L’UI Avalonia doit rester centrée sur la présentation, la navigation et l’orchestration des actions utilisateur. Toute logique de scan, de parsing de date, de décision métier ou de traitement fichier doit rester dans `PhotoApp.Core`.[8][14]

## Fonctionnalités prévues

### Scan récursif

Le scanner devra :

- parcourir un dossier racine et tous ses sous-dossiers ;
- reconnaître les extensions supportées ;
- ignorer les fichiers non image ;
- gérer les erreurs d’accès sans interrompre le traitement ;
- remonter les fichiers potentiellement problématiques ;
- fournir des métadonnées de base utiles à l’UI (nom, chemin, taille, extension).

Une abstraction simple doit permettre plus tard d’ajouter détection de doublons, hachage, ou état “corrompu/suspect” sans casser la première implémentation.[15]

### Visualisation

L’interface devra offrir une visualisation basique mais fluide :

- liste ou grille des images détectées ;
- compteur de fichiers ;
- filtres par dossier, type, source de date ;
- panneau détail avec aperçu de métadonnées ;
- comparaison avant/après pour les opérations d’harmonisation.

La première version ne doit pas viser une galerie sophistiquée. La priorité est la clarté de l’information et la stabilité sur une photothèque volumineuse.[8]

### Harmonisation des dates

Pour chaque image, l’application devra déterminer une date de référence à partir d’une stratégie ordonnée et explicite. Les écritures de métadonnées devront être traçables et réversibles autant que possible, avec journal des actions effectuées.[3][7]

### Compression

La compression devra proposer au minimum :

- qualité configurable ;
- aperçu du gain estimé ;
- traitement batch ;
- conservation optionnelle de l’original ;
- résumé final du nombre de fichiers modifiés et de l’espace gagné.

La compatibilité exacte par format dépendra de la brique technique validée, en particulier pour HEIC/HEIF. Ce point doit rester visible dans les choix produit et dans la documentation développeur.[2][16][17]

## Gestion des risques

### Risque packaging

Le plus gros risque technique identifié est le conflit entre :

- support de formats photo variés ;
- dépendances natives performantes ;
- volonté de distribuer un unique `.exe` autonome.

Le runtime .NET peut être embarqué dans un déploiement self-contained single-file, mais les bibliothèques natives tierces peuvent compliquer le packaging ou impliquer une extraction interne au lancement. Ce sujet doit être validé très tôt avec un prototype réel, pas seulement supposé.[5][6][17]

### Risque corruption/perte de données

Toute opération d’écriture sur des photos personnelles est sensible. Une erreur de logique peut provoquer une perte de métadonnées ou un écrasement non voulu. Le produit devra donc privilégier les prévisualisations, la journalisation et la conservation optionnelle des originaux.[3][7]

### Risque performance UI

Sur 10 000 images, une interface naïve peut devenir lente si elle charge trop tôt les miniatures ou maintient trop d’objets en mémoire. Il faudra donc garder une stratégie de chargement progressif, avec miniatures différées et listes adaptées à un gros volume.[2][16]

## Construction et exécution

### Build de développement

Pour le développement, utiliser `dotnet build` :

```bash
dotnet build PhotoApp/PhotoApp.csproj
```

### Build de distribution Windows

Pour produire un exécutable autonome Windows (self-contained single-file) :

```bash
dotnet publish PhotoApp/PhotoApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

L'exécutable `PhotoApp.exe` sera généré dans `PhotoApp/bin/Release/net10.0/win-x64/publish/`.

### Validation avec le build de distribution

Pour valider uniquement avec le vrai build de distribution, utiliser le script `run-published.ps1` à la racine du projet :

```powershell
.\run-published.ps1
```

Ce script publie automatiquement en Release win-x64 self-contained single-file puis lance `PhotoApp.exe`.

## Journalisation et diagnostic

Dès les premiers jalons, l’application devra produire des logs lisibles. Les événements importants à tracer sont :

- démarrage et fin de scan ;
- nombre de fichiers détectés ;
- erreurs d’accès ;
- décisions de résolution de date ;
- opérations de compression ;
- écritures de métadonnées.

Un journal texte local suffit en première version. L’objectif est de faciliter le diagnostic sans ajouter une stack d’observabilité excessive.

## Décisions déjà prises

Les choix actés à ce stade sont les suivants :

| Sujet | Décision |
|---|---|
| Plateforme prioritaire | Windows |
| Portabilité visée | Linux et macOS ensuite |
| Langage / runtime | C# / .NET 10 LTS |
| GUI | Avalonia |
| IDE de départ | VS Code |
| Type de distribution cible | `.exe` Windows autonome |
| Style d’architecture | simple, pragmatique, testable |

Le projet Avalonia MVVM a déjà été créé et publié avec succès en `win-x64` self-contained single-file, ce qui valide le socle de départ pour la suite.[20][5][21]

## Roadmap courte

### Jalons immédiats

1. créer `PhotoApp.Core` et `PhotoApp.Tests` ;
2. implémenter un scanner récursif d’images ;
3. brancher le scan à l’UI ;
4. afficher les résultats dans la fenêtre principale ;
5. ajouter la résolution de dates ;
6. ajouter la prévisualisation de changements ;
7. seulement après, ajouter la compression réelle.

### Jalons suivants

- gestion des doublons ;
- cache local SQLite ;
- miniatures persistées ;
- file d’attente de jobs ;
- annulation d’opérations longues ;
- reporting plus complet des changements.

## Règles de travail pour l’IA de code

Quand une IA de code intervient sur le projet, elle doit respecter ces règles :

- privilégier la simplicité et la lisibilité ;
- ne pas introduire de dépendance externe sans justification claire ;
- éviter les abstractions prématurées ;
- ne pas mélanger logique UI et logique métier ;
- proposer des modifications petites et testables ;
- conserver les noms de classes explicites ;
- gérer proprement les erreurs filesystem ;
- écrire des tests sur les règles critiques ;
- éviter de casser l’objectif de packaging autonome Windows.

L’IA doit produire du code maintenable par un développeur humain seul, pas une architecture démonstrative.

## Définition de “version 0 utile”

La première version utile du projet n’a pas besoin d’être complète. Elle sera considérée comme satisfaisante si elle permet :

- de choisir un dossier ;
- de scanner récursivement les images ;
- d’afficher la liste des résultats ;
- de montrer les dates candidates ;
- de proposer une date retenue avec sa source ;
- de prévisualiser une harmonisation ;
- de publier un exécutable Windows autonome.

La compression avancée, le support parfait de tous les formats exotiques et l’optimisation poussée de la galerie peuvent venir après. Ce cadrage évite de diluer l’effort trop tôt.

## Référence de démarrage

Commandes utiles de base :

```powershell
dotnet new avalonia.mvvm -o PhotoApp
cd .\PhotoApp
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Ces commandes correspondent au flux de création et de publication documenté pour Avalonia/.NET, et ont déjà été validées sur l’environnement Windows cible du projet.[20][5][21]