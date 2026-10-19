# Phase 1 — Moteur de normalisation (lecture seule)

- **Date :** 2026-10-19
- **Statut :** validé en brainstorming, en attente de relecture
- **Cadre :** [vision et feuille de route](2026-10-14-vision-feuille-de-route-design.md), §5 et §10 (phase 1). Cette spec précise la phase ; en cas d'écart, elle l'emporte sur le cadre pour la phase 1.

---

## 1. Objectif

Remplacer la sélection de date actuelle (`DateSelector` : premier candidat trouvé, natif puis nom puis fichier) par un **moteur de normalisation unique** qui collecte tous les candidats, reconnaît la source par un profil, confronte les candidats, attribue une confiance expliquée et propose un nom. Le moteur sert à la fois :

- à une nouvelle CLI, `photoapp analyse`, qui produit un rapport sans rien écrire ;
- à l'interface existante, qui renomme et convertit à partir de ses décisions.

## 2. Critère de fin

1. La CLI produit un rapport sur la bibliothèque réelle (dossiers Images et Vidéos) **sans aucune écriture** à côté des photos.
2. Chaque source du cadre §3.2 est reconnue par son profil.
3. L'**objectif chiffré** de « Sûre + Probable », fixé à mi-phase (§12), est atteint.
4. L'interface utilise le moteur unique et n'applique que Sûre et Probable.
5. Les tests sont verts sur Windows, Linux et macOS.

## 3. Périmètre

**Inclus :** cadre §5 en entier, sauf ce qui suit ; vidéos MOV et MP4 ; sidecars `.xmp` lus ; association des Live Photo, par `ContentIdentifier` puis par nom de base ; consensus des voisins pour les dates ambiguës.

**Reporté :**

| Élément | Phase |
|---|---|
| Outils manuels (décalage par lot, date fixée, choix d'un candidat, cadre §5.4) | 3 ou 4 (interface et stockage des décisions) |
| Sidecars `.json` Google Takeout | 8 (import Takeout) |
| Renommage sur place, groupes déplacés ensemble | 2 |
| Renommage et conversion des vidéos depuis l'interface | 2 |

## 4. Architecture

**Flux :** scan → lecture des métadonnées (E/S, en parallèle) → moteur (pur, sur le lot entier) → rapport CSV ou interface.

Le moteur travaille sur un **lot** : le consensus des voisins et les collisions de noms ont besoin de voir tout le dossier.

| Projet | Rôle en phase 1 |
|---|---|
| **Catalog** | `ImageScanner` devient `MediaScanner`. Il ajoute les vidéos et `.gif`, rattache à chaque média ses sidecars `.xmp` et `.aae` (même dossier, même nom de base), relève la taille et la date de modification. Exclusions inchangées : fichiers et dossiers cachés, fichiers de 0 octet, dossiers exclus. |
| **Metadata** | `MetadataReader` produit un `RawMetadata` par fichier, sans aucune décision : dates EXIF (`DateTimeOriginal`, `DateTimeDigitized`, décalages, sous-secondes), dates XMP (dans le fichier et dans le sidecar), QuickTime Apple (`com.apple.quicktime.creationdate`), QuickTime `mvhd`, GPS date et heure, marque, modèle, logiciel, `ContentIdentifier` Apple, **vrai format** détecté par MetadataExtractor. Il fournit aussi l'empreinte de contenu (SHA-256) à la demande. |
| **Core** (pur, sans E/S) | `SourceProfiles` (table des profils), `ProfileMatcher`, `CandidateCollector`, `AmbiguityResolver`, `DateArbiter`, `MediaNamer`. Point d'entrée : `NormalizationEngine.Normalize(lot, hacheur) → résultats`. Le hacheur est une interface de Core, implémentée dans Metadata. |
| **Cli** (nouveau) | `PhotoApp.Cli`, commande `analyse` (§9). Référence Catalog, Metadata et Core, **jamais Operations**. |
| **PhotoApp** (interface) | Utilise le moteur à la place d'`ImageDateResolver` et `DateSelector` (§10). |
| **Operations** | Inchangé. |

**Remplacés :** `DateSelector`, `ImageDateSource`, `ImageDateMetadata`, `ImageDateResolver` (Metadata) et `NativeImageMetadataReader` disparaissent au profit du moteur et de `MetadataReader`. `ImageDateCandidate` devient `DateCandidate`, qui remplace `HasTime` par une précision. `FileNameDateParser` est éclaté : chaque profil porte ses motifs, et un parseur générique reste en filet pour les noms inconnus.

**Conservés et étendus :** `DatePlausibility`, `UtcOffsetParser`, `XmpDateParser`, `NameAllocator`, `NormalizedFileNameBuilder`, `RenamePlanner`.

## 5. Modèle de décision

### 5.1 Candidats

Un candidat porte :

- une **valeur** : l'heure murale, telle qu'écrite ;
- une **précision** : jour, minute, seconde, sous-seconde ;
- une **nature** : locale avec décalage, locale flottante, UTC ;
- une **origine** : EXIF original, EXIF numérisation, QuickTime Apple, QuickTime `mvhd`, XMP, XMP sidecar, GPS, nom (avec le profil qui l'a lu), date du fichier.

Sont **écartés** d'emblée, tout en restant visibles dans le rapport : les valeurs hors de [1990-01-01, aujourd'hui + 1 jour] et les horloges remises à zéro (`2000-01-01 00:00:00`, `1970-01-01`).

Sont **forts** : EXIF, QuickTime Apple, QuickTime `mvhd`, XMP et le nom lu par un profil connu. Le nom lu par le parseur générique, le GPS et la date du fichier ne le sont pas.

### 5.2 Tolérances

- **Concordance** de deux valeurs : écart de 2 s au plus en précision seconde, même minute en précision minute, même jour en précision jour. On compare à la précision la plus faible des deux. Deux valeurs dont le décalage est connu se comparent comme des instants ; deux valeurs locales, comme des heures murales ; une valeur locale sans décalage et une valeur UTC relèvent de l'écart de fuseau ci-dessous.
- **Écart de fuseau valide** entre une valeur locale et une valeur UTC : multiple de 15 min à 2 s près, d'au plus 14 h en valeur absolue.
- **Décalage déduit du GPS** : `DateTimeOriginal − GPS`, arrondi au quart d'heure, retenu si le reste est d'au plus 2 min et le décalage d'au plus 14 h.
- **Latence d'écriture EXIF** : l'EXIF d'un appareil peut être écrit jusqu'à 10 s après l'heure de son nom (mesure réelle : Samsung +2 à +4 s, Xiaomi jusqu'à +10 s) ; l'EXIF et le nom concordent si EXIF − nom ∈ [−2 s, +10 s].
- **En-tête vidéo `mvhd`** : il est souvent écrit à la fin de l'enregistrement ; il concorde avec l'heure de début s'il la suit de 0 à 7 min (−2 s toléré). Un nom au jour concorde avec une valeur UTC si l'instant tombe dans [J − 14 h, J + 1 j + 12 h[.

Ces valeurs sont des constantes nommées, revues après la mesure de mi-phase.

### 5.3 Décision

Une décision contient la date retenue (un candidat, éventuellement complété d'un décalage déduit), la **confiance**, le **profil** reconnu, la **raison** (identifiant de la règle), tous les candidats et des **drapeaux** : `ambigue`, `desaccord`, `format-reel` (vrai format ≠ extension), `fuseau-suppose`, `derive` (copie `-min`, `(1)`…), `live-photo`.

Confiances : **Sûre** et **Probable** sont applicables ; **Incertaine** part dans « À vérifier » ; **Aucune** est signalée et aucune date n'est inventée.

### 5.4 Règles

**Étape 1 — candidat principal et confiance de base.** La première règle qui s'applique décide.

| Raison | Situation | Confiance de base |
|---|---|---|
| `messagerie-exif` | Profil de messagerie (WhatsApp, Signal, Telegram) avec un EXIF **antérieur ou égal** au nom, comparé à la précision du nom : la vraie prise de vue est retenue (transfert, ou heure plus précise le même jour), le nom est conservé comme information | selon l'EXIF (règles `exif-*` ci-dessous) |
| `messagerie-nom` | Profil de messagerie : **le nom fait foi** | Sûre, précision du nom |
| `capture-nom` | Profil de capture d'écran : le nom est l'heure de capture | Sûre |
| `exif-decalage` | EXIF `DateTimeOriginal` (sinon `DateTimeDigitized`) avec décalage | Sûre |
| `quicktime-apple` | `com.apple.quicktime.creationdate` (décalage inclus) | Sûre |
| `exif-concordant` | EXIF sans décalage, concordant avec le nom d'un profil ou avec un décalage déduit du GPS | Sûre, avec le décalage déduit s'il existe |
| `exif-seul` | EXIF sans décalage, sans autre confirmation | Probable |
| `video-utc-nom` | `mvhd` (UTC) et nom d'un profil séparés par un écart de fuseau valide | Sûre, heure du nom, décalage déduit |
| `nom-profil` | Nom lu par un profil d'appareil, sans métadonnées internes | Probable |
| `xmp-seul` | Date XMP seule (fichier ou sidecar) | Probable |
| `utc-seul` | `mvhd` seul, converti avec le fuseau du PC ; drapeau `fuseau-suppose` | Probable |
| `nom-generique` | Nom lu par le parseur générique, avec l'heure | Probable |
| `nom-generique-jour` | Nom lu par le parseur générique, jour seul | Incertaine |
| `ambigue` | Date JJ/MM non tranchée (§5.5) | Incertaine |
| `date-fichier-seule` | Seule la date de modification du fichier est plausible | Incertaine |
| `aucune` | Aucun candidat plausible | Aucune |

**Étape 2 — confrontation.** Le candidat principal est comparé à chaque autre candidat **fort** :

- s'il y a concordance, ou un écart de fuseau valide entre une valeur locale et une valeur UTC : rien ne change ;
- si le désaccord est **expliqué** par un enregistrement postérieur (EXIF antérieur ou égal au nom, quel que soit le profil : transfert par messagerie, téléchargement, copie retouchée ou exportée) : rien ne change, et l'autre valeur reste une information ;
- sinon : la confiance tombe à **Incertaine**, la raison devient `desaccord`, et le drapeau `desaccord` est posé avec les deux valeurs.

La date du fichier ne participe jamais à la confrontation. Pour un profil de messagerie, elle ne peut donc jamais contredire le nom : par exemple, `IMG-20210915-WA0005.jpg` modifié le 2023-03-01 reste daté du 2021-09-15, Sûre.

### 5.5 Dates ambiguës (JJ/MM ou MM/JJ)

Une date dont les deux premières composantes sont ≤ 12 et différentes est tranchée dans cet ordre :

1. une composante > 12 (déjà traité par le parseur) ;
2. l'ordre déclaré par le profil ;
3. le **consensus des voisins** : les fichiers du même dossier lus par le même motif dont la date n'est pas ambiguë. Si au moins un existe et que tous indiquent le même ordre, cet ordre est adopté ;
4. les autres candidats, y compris la date du fichier : si une seule des deux lectures tombe le même jour qu'un autre candidat, elle est adoptée.

À défaut, la raison est `ambigue` et la confiance Incertaine.

## 6. Profils de sources

Un profil est un enregistrement C# immuable, déclaré dans une table unique (`SourceProfiles.All`) :

- identifiant et nom affiché ;
- famille : appareil, messagerie, capture, export/téléchargement ;
- motifs de nom (regex à groupes nommés `year`, `month`, `day`, `hour`, `minute`, `second`, `fraction`, `ampm`, `epoch`, `counter`) ;
- indices facultatifs : marque et modèle EXIF, nom de dossier ;
- sémantique de l'heure du nom : locale (cas général) ou UTC (epoch) ;
- ordre JJ/MM déclaré, le cas échéant.

**Reconnaissance**, dans cet ordre : motif de nom, puis marque et modèle, puis indice de dossier. Avant la reconnaissance, les suffixes de copie (`-min`, ` (1)`, `(1)`, ` - Copie`) sont retirés du nom ; leur présence pose le drapeau `derive`. Sans profil, le rapport indique `inconnu` et le parseur générique est essayé.

**Profils livrés :**

| Famille | Profils |
|---|---|
| Messagerie | WhatsApp `IMG-AAAAMMJJ-WAnnnn` / `VID-…-WAnnnn` (le compteur `counter` sert à l'ordre), `WhatsApp Image AAAA-MM-JJ at HH.mm.ss` ; Signal `signal-AAAA-MM-JJ-HHmmss[_nnn]` ; Telegram `photo_AAAA-MM-JJ_HH-mm-ss` |
| Appareil | Xiaomi `IMG_AAAAMMJJ_HHmmss` / `VID_…` ; Samsung `AAAAMMJJ_HHmmss` ; Pixel `PXL_AAAAMMJJ_HHmmssSSS` ; Huawei, OnePlus, `MVIMG_` ; Nextcloud iOS `AA-MM-JJ HH-MM-SS NNNN` (suffixe numérique ou hexadécimal) ; iPhone natif `IMG_nnnn` (sans date dans le nom, reconnu par la marque Apple) |
| Capture | Android `Screenshot_AAAAMMJJ_HHmmss[_app]` ; Windows `Screenshot AAAA-MM-JJ HHmmss` ; Outil Capture Windows ; GNOME `Capture d’écran du AAAA-MM-JJ HH-mm-ss` et `Screenshot from …` (apostrophes `’` et `'`) ; macOS 12 h et 24 h, en anglais et en français ; KDE Spectacle ; captures de jeu (AMD, Steam…) `Titre_AAAA_MM_JJ_HH_mm_ss_fff` et `Titre JJ.MM.AAAA HH_mm_ss` (jour en premier) |
| Export / téléchargement | WeChat `mmexport<epoch>`, Facebook `FB_IMG_<epoch>` (epoch de 10 ou 13 chiffres, UTC) ; Snapchat et téléchargements (`hqdefault-…`, sans date dans le nom) ; images collées Obsidian `Pasted image AAAAMMJJHHmmss` |

Les heures en 12 h sont converties ; tout est en 24 h ensuite.

## 7. Nommage proposé

Le moteur **propose** un nom pour chaque média Sûr ou Probable. Rien n'est renommé en phase 1 par la CLI. Le renommage sur place suivra en phase 2 avec ces mêmes noms. L'interface garde son propre `RenamePlanner` pour son dossier de sortie (§10).

- **Préfixe :** `IMG-` pour les photos et captures, `VID-` pour les vidéos. Le type vient du **vrai format**, pas de l'extension.
- **Forme :** `IMG-AAAAMMJJ-HHmmss.ext` ; jour seul : `IMG-AAAAMMJJ-000000-NN.ext`, le suffixe étant toujours présent.
- **Extension :** celle du vrai format, en minuscules, `.jpeg` → `.jpg`. Un écart avec l'extension d'origine pose le drapeau `format-reel`.
- **Live Photo :** une photo et un `.mov` du même dossier sont associés par le même `ContentIdentifier`, sinon par le même nom de base. Le `.mov` prend la date et le nom de base de la photo, avec le préfixe `IMG-` (drapeau `live-photo`). Si la photo n'a pas de nom proposé, le `.mov` n'en a pas non plus.
- **Collisions** dans le même dossier, à la même seconde : `-01`, `-02`…, dans l'ordre des sous-secondes EXIF, puis du compteur du nom (`WAnnnn`), puis de l'empreinte SHA-256, calculée seulement pour les fichiers encore à égalité.
- **Idempotence :** un fichier dont le nom est déjà conforme et correspond à la date retenue garde son nom (« déjà normalisé »). Ces noms sont réservés avant toute attribution : un fichier déjà nommé n'est jamais renuméroté, et un nouveau venu prend le prochain numéro libre.
- **Incertaine** et **Aucune** : aucun nom proposé.

## 8. Lecture des métadonnées

- MetadataExtractor lit EXIF, XMP, GPS, QuickTime et le type de fichier. Aucun nouveau paquet n'est nécessaire pour la lecture.
- Le sidecar `.xmp` est lu avec le même `XmpDateParser`.
- La lecture est parallèle, avec un degré égal au nombre de cœurs. Le moteur, lui, est séquentiel et déterministe : un même lot donne toujours le même résultat.
- Objectif de durée : la bibliothèque réelle (~9 300 fichiers) en quelques minutes. Il est mesuré à mi-phase et ne sert pas de critère bloquant.

## 9. CLI : commande `analyse`

```
photoapp analyse <dossier>... [--out <fichier.csv>] [--exclude <dossier>]...
```

- Les arguments sont lus avec System.CommandLine.
- **Lecture seule** : le projet ne référence pas Operations. Un test de bout en bout vérifie que les fichiers analysés gardent leur taille, leur date de modification et leur SHA-256.
- **Sortie par défaut** : `<racine de données>/reports/analyse-AAAAMMJJ-HHmmss.csv`. `AppDataPaths` gagne une propriété `ReportsFolder`. Le dossier est créé s'il manque, car il appartient à l'application.
- **CSV** en UTF-8 avec BOM, séparateur `;`, une ligne par média. Colonnes, dans cet ordre : `chemin`, `type`, `format_reel`, `profil`, `date`, `precision`, `decalage`, `confiance`, `raison`, `nom_propose`, `exif`, `quicktime`, `gps`, `xmp`, `nom`, `date_fichier`, `fichiers_lies`, `drapeaux`, `erreur`. Les dates sont en ISO (`2024-01-05 15:04:12`, `+09:00`) et les drapeaux séparés par `|`.
- **Résumé dans la console** :
  - totaux par confiance ;
  - par profil : nombre et % Sûre + Probable ;
  - les 20 motifs de nom les plus fréquents parmi les fichiers `inconnu` (chiffres remplacés par `#`), pour repérer les profils manquants ;
  - fichiers exclus (cachés, vides), dossiers inaccessibles, erreurs ;
  - durée.
- La progression s'affiche pendant l'analyse.
- **Code de sortie** : 0 si l'analyse a abouti, même avec des fichiers illisibles ; 1 pour un argument invalide ; 2 pour une erreur fatale, par exemple un rapport impossible à écrire.

## 10. Interface

- Elle obtient ses décisions du moteur, en analysant le dossier choisi comme un lot.
- Elle affiche, pour chaque fichier, la date retenue, la confiance et la raison en clair.
- Elle n'applique, par renommage et conversion, que Sûre et Probable. Incertaine et Aucune sont listées « À vérifier » et ne sont pas touchées.
- Elle reste limitée aux photos : les vidéos sont ignorées par l'interface jusqu'à la phase 2.
- Elle garde son `RenamePlanner` et son manifeste pour le dossier de sortie : la date vient du moteur, l'attribution des noms dans le dossier de sortie reste la sienne.

## 11. Erreurs

- Un fichier illisible ou corrompu donne une ligne avec la colonne `erreur`, la confiance Aucune et la raison `lecture-impossible`. L'analyse continue.
- Les fichiers cachés, les fichiers de 0 octet et les dossiers inaccessibles sont exclus et comptés dans le résumé.
- Une valeur de métadonnée mal formée est ignorée : le candidat est absent et la lecture des autres valeurs continue.

## 12. Mesure à mi-phase et objectif chiffré

Dès que le moteur et une CLI minimale fonctionnent, la CLI analyse la bibliothèque réelle. Les dossiers Images et Vidéos sont résolus via les Known Folders, sans chemin écrit en dur, et l'analyse est en lecture seule.

À partir du rapport :

1. l'utilisateur fixe l'**objectif chiffré** de « Sûre + Probable », qui est inscrit ici ;
2. les profils et les règles sont complétés d'après les motifs inconnus et les désaccords ;
3. les tolérances du §5.2 sont revues si nécessaire.

**Mesure du 2026-10-19, avant itération :** 9 449 médias (8 955 photos, 494 vidéos) analysés en 3,5 s ; Sûre 80,4 %, Probable 9,0 %, Incertaine 10,6 %, Aucune 0 % — Sûre + Probable : 89,4 %. Lecture seule vérifiée (empreinte des 9 617 fichiers identique avant et après).

**Après la première itération (même jour) :** Sûre 86,6 %, Probable 7,5 %, Incertaine 5,9 %, Aucune 0 % — Sûre + Probable : 94,1 %. Restent Incertaines les photos sans date exploitable : album_japon (EXIF « 0000:00:00 », 123), album_japon_final (sans EXIF, 105), Snapchat (105), téléchargements et mèmes (~200). Lecture seule vérifiée à nouveau.

**Objectif chiffré :** à fixer par l'utilisateur au vu de la mesure (la mesure et la première itération ont été faites pendant son absence).

## 13. Tests

- **Core** : tests unitaires sans fichier.
  - Pour chaque profil, des noms qui doivent être reconnus et d'autres qui ne doivent pas l'être.
  - Un test par règle du §5.4, par cas de confrontation et par cas d'ambiguïté (chaque étape du §5.5).
  - Pour le nommage : IMG/VID, Live Photo, collisions et leur ordre, jour seul, idempotence, vrai format.
- **Metadata** : fichiers **synthétiques** générés pendant les tests.
  - Photos : EXIF, XMP et GPS écrits par Magick.NET (déjà dépendance des tests).
  - MOV/MP4 : une aide de test écrit à la main un conteneur minimal avec `mvhd` et les clés Apple.
  - Le nettoyage de vrais fichiers pour le corpus attendra ExifTool (phase 3).
- **Catalog** : vidéos et sidecars rattachés ; exclusions.
- **Cli** : test de bout en bout sur un dossier temporaire (CSV attendu, fichiers intacts, codes de sortie).
- **Interface** : le ViewModel n'applique que Sûre et Probable.
- **CI** : les trois OS, comme en phase 0.

## 14. Décisions de la session du 2026-10-19

| Sujet | Décision |
|---|---|
| Report | Outils manuels → phase 3 ou 4 ; sidecars Google Takeout → phase 8 |
| WhatsApp | Le nom de fichier fait foi : Sûre, précision « jour ». La date du fichier ne le contredit jamais. Un EXIF antérieur ou du même jour l'emporte (transfert, cadre §5.4) ; un EXIF postérieur au nom est un désaccord → Incertaine |
| Objectif chiffré | Mesurer d'abord, fixer à mi-phase |
| Interface | Moteur unique dès la phase 1 |
| Approche | Profils en C# déclaratif et règles explicites nommées, chaque décision portant sa raison (pas de score pondéré ni de profils en JSON) |
