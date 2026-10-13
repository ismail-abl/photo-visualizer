# PhotoManager — Vision d'ensemble et feuille de route

- **Date :** 2026-10-14
- **Statut :** validé en brainstorming, en attente de relecture
- **Portée :** document cadre. Chaque phase de la feuille de route (§10) fera l'objet de sa propre spec détaillée et de son propre plan d'implémentation.

---

## 1. Objectif produit

Application desktop locale, hors ligne, graphique et moderne, développée en priorité pour Windows et portable sur Linux et macOS. Trois piliers, par ordre d'importance :

1. **Agrégation et normalisation des métadonnées** : quels que soient l'appareil (iPhone, Samsung, Xiaomi…), l'application (WhatsApp, Signal, Snapchat…), le système (captures Windows, Linux, macOS, Android) ou le format de date (ISO, européen, américain, epoch), produire une date de prise de vue fiable, un nom de fichier unique et des métadonnées cohérentes.
2. **Galerie** : parcourir la bibliothèque, visualiser les photos et consulter toutes leurs métadonnées.
3. **Conversion JPEG** pour gagner de la place, sans perdre les métadonnées.

Fonctionnalités annexes retenues : **mode confidentialité** (suppression des métadonnées privées), **détection de doublons**, **mini-carte** pour les photos géolocalisées.

## 2. Décisions actées

| Sujet | Décision |
|---|---|
| Pile technique | .NET 10 + Avalonia 12, en faisant évoluer le projet existant. Priorité à la facilité de compilation et d'exécution (`dotnet build` / `dotnet run`). |
| Lecture des métadonnées | Native via MetadataExtractor, rapide et adaptée à l'indexation. |
| Écriture des métadonnées | **ExifTool embarqué** dans l'application, piloté en processus persistant (`-stay_open`), sans aucune installation pour l'utilisateur. Cette décision remplace « pas d'ExifTool au runtime » du README. |
| Décodage d'images (HEIC, AVIF, RAW) | Magick.NET (NuGet multiplateforme). |
| Connaissance des photos | **Catalogue indexé** : des dossiers sources sont déclarés, indexés dans une base SQLite locale, avec miniatures en cache et surveillance des changements. |
| Modèle de sécurité | **Gradué** : plan → aperçu → application, journal, annulation, quarantaine (§7). |
| Heure de référence | **Heure locale du lieu de prise de vue** (heure « murale »). Le décalage est conservé quand il est connu. |
| Format horaire | 24 h, toujours. |
| Nommage | `IMG-AAAAMMJJ-HHmmss` pour les photos et captures, `VID-AAAAMMJJ-HHmmss` pour les vidéos (§5.5). |

## 3. Constat de départ

### 3.1 Projet existant

Le projet compte trois projets (`PhotoApp`, `PhotoApp.Core`, `PhotoApp.Tests`) et 32 tests qui passent. Les défauts suivants expliquent son fonctionnement intermittent et seront corrigés en phase 0 :

1. Le scan récursif inclut le dossier de sortie `Converted/`. Au deuxième passage, les sorties sont re-scannées et le déplacement échoue (« target exists »).
2. HEIC, HEIF et WebP sont scannés, mais `ImageConverter` les rejette (`NotSupportedException`). Toutes les photos iPhone échouent.
3. L'heure EXIF, qui est locale et sans fuseau, est marquée UTC puis réappliquée en UTC. Les dates sur le disque sont décalées d'1 ou 2 h. Les dates XMP, elles, sont converties en UTC, d'où des incohérences entre sources.
4. Le repli sur le système de fichiers utilise `LastWriteTime` (la date de copie) en le marquant « avec heure ». Il produit de fausses dates présentées comme fiables.
5. Le nettoyage dans le `catch` de `BatchRenameExecutor` recalcule le chemin temporaire avec un nouveau `Guid`. Il laisse des fichiers orphelins.
6. Le parseur de noms a des trous : les captures Windows (`Screenshot 2025-08-27 211650`) perdent l'heure, les formats macOS 12 h, epoch et Nextcloud iOS ne sont pas reconnus, et l'ambiguïté JJ/MM contre MM/JJ n'est pas traitée.
7. Le scan et la résolution des dates tournent sur le thread de l'interface, qui se fige.
8. ImageSharp 3.1.11 est touché par des vulnérabilités connues, dont plusieurs de gravité élevée.
9. Le hash de collision est calculé sur le chemin source. Il n'est pas stable et ne respecte pas l'ordre chronologique.

### 3.2 Inventaire de la bibliothèque réelle

Réalisé en lecture seule le 2026-10-12 sur environ 9 300 médias de `Pictures` et `Videos`.

| Source | Volume | Motif de nom | Métadonnées internes |
|---|---|---|---|
| WhatsApp | ~4 400 | `IMG-20151114-WA0017.jpg` | Aucune. Date au jour seulement ; le compteur `WAnnnn` donne l'ordre |
| Xiaomi M2012K11AG | ~1 300 | `IMG_20230304_132015.jpg` | EXIF, décalage parfois présent |
| Samsung SM-G990B | ~630 | `20260917_222944.heic` / `.jpg` | EXIF + décalage. 295 doublons entre `Camera/` et `DCIM/Camera/` |
| iPhone 12 (app Nextcloud iOS) | ~690 | `24-07-11 17-07-10 0477.mov` (`AA-MM-JJ HH-MM-SS NNNN`) | 264 JPEG sans EXIF ; les MOV portent les métadonnées Apple QuickTime |
| Captures Android | ~625 | `Screenshot_20240410_111652.jpg` | Aucune |
| Captures Windows | ~540 | `Screenshot 2025-08-27 211650.jpg` | Aucune |
| Captures GNOME (FR et EN) | ~250 | `Capture d’écran du 2026-03-27 01-22-28.png`, `Screenshot from …` | Aucune (attention à l'apostrophe typographique `’`) |
| Albums renommés | ~430 | `album_japon/001.jpg` | EXIF sans décalage (voyage au Japon). 92 exports de `album_japon_final` sans EXIF |
| Copies compressées | ~575 | `IMG_…-min.jpg` | EXIF conservé : dérivés d'originaux |
| Snapchat, Signal, téléchargements | ~300 | `hqdefault-4B744.jpg`, `signal-2023-12-18-211842_002.jpg` | Généralement aucune |

Autres constats :
- **Aucune coordonnée GPS détectée** sur l'échantillon. C'est observé, pas expliqué.
- Les erreurs de lecture viennent de caches d'applications (dossiers cachés `.gs`) et d'un fichier de 0 octet.
- `Documents\backup\discord-channel-bak` (~12 000 fichiers) est un export Discord, pas une bibliothèque photo.

**Formats retenus :**
- **Priorité 1** : JPEG, PNG, HEIC, MOV, MP4.
- **Priorité 2** : WebP, GIF.
- **RAW** : aucun fichier présent, prévu en phase 8 (lecture + sidecar `.xmp`).
- **Hors périmètre** : MKV, SVG, ICO, JSON (sauf sidecars).

## 4. Architecture

```
PhotoApp (Avalonia, MVVM)
  Galerie · Visionneuse + carte · Propriétés · Revue · Aperçu du plan · Journal
        │                                   │
PhotoApp.Catalog                     PhotoApp.Operations
  index SQLite, miniatures,            plan → vérification → application,
  surveillance, doublons               journal, annulation, quarantaine
        │                                   │
        └──────────────┬────────────────────┘
               PhotoApp.Core   (pur, sans E/S)
                 profils de sources, parseurs de noms, résolution de date,
                 confiance, fuseaux, nommage, règles de confidentialité
                       │
               PhotoApp.Metadata
                 lecture : MetadataExtractor
                 écriture : ExifTool embarqué
                 détection du vrai format (octets magiques)

PhotoApp.Cli : même cœur, sans interface (analyse → rapport, automatisation)
PhotoApp.Tests : tests unitaires + corpus de fichiers d'exemple par source
```

**Principes**
- `Core` est **pur** : il reçoit des métadonnées brutes et renvoie des décisions. Tout le savoir sur les sources y vit et se teste sans fichier.
- **Une seule porte d'écriture** : seul `Operations` modifie des fichiers, toujours à partir d'un plan validé et journalisé.
- **Corpus de test** : de petits fichiers d'exemple par source, nettoyés de toute donnée privée. Chaque cas réel nouveau devient un test.
- **ExifTool** est copié automatiquement par le build, sans étape manuelle.

**Correspondance avec le code existant :**

| Code actuel | Destination |
|---|---|
| `FileNameDateParser`, `ImageDateResolver`, `NormalizedFileNameBuilder`, `RenamePlanner` | `Core` |
| `NativeImageMetadataReader` | `Metadata` |
| `BatchRenameExecutor`, `FileTimestampApplier` | `Operations` |
| `ImageScanner` | `Catalog` |

## 5. Moteur de normalisation

### 5.1 Collecte des candidats

Tous les candidats sont collectés, pas seulement le premier trouvé. Chacun porte une **valeur**, une **précision** (jour, minute, seconde, sous-seconde) et une **nature d'heure** (locale avec décalage, locale flottante, UTC).

| Candidat | Nature |
|---|---|
| EXIF `DateTimeOriginal` + `OffsetTimeOriginal` + `SubSecTimeOriginal` | locale (± décalage) |
| QuickTime Apple `com.apple.quicktime.creationdate` | locale avec décalage |
| QuickTime `mvhd` / `tkhd` / `mdhd` | UTC |
| XMP `photoshop:DateCreated`, `exif:DateTimeOriginal`, `xmp:CreateDate` | variable |
| GPS `GPSDateStamp` + `GPSTimeStamp` | UTC. Sert aussi à déduire le décalage par comparaison avec `DateTimeOriginal` |
| Nom de fichier, via un profil de source | locale |
| Sidecars (`.json` Google Takeout, `.xmp`) | variable |
| Date de modification du fichier | instant. Confiance faible, toujours |

### 5.2 Profils de sources

Un profil est identifié par une combinaison de marque/modèle, motif de nom et indices de dossier. Il définit le parseur de nom, la sémantique de l'heure et les particularités connues. Les profils sont **déclaratifs** : ajouter une source revient à ajouter un profil et ses fichiers de test.

Profils minimaux :
- **Toutes les sources du §3.2.**
- **Couverture générale :**
  - Pixel `PXL_AAAAMMJJ_HHmmssSSS`, Huawei, OnePlus, `MVIMG_` ;
  - Telegram `photo_AAAA-MM-JJ_HH-mm-ss` ;
  - Signal `signal-AAAA-MM-JJ-HHmmss` ;
  - WeChat `mmexport<epoch>` ;
  - Facebook `FB_IMG_<epoch>` ;
  - `WhatsApp Image AAAA-MM-JJ at HH.mm.ss` ;
  - captures macOS en 12 h et en plusieurs langues (`Screenshot 2024-01-05 at 3.04.12 PM`, `Capture d’écran 2024-01-05 à 15.04.12`) ;
  - KDE Spectacle ;
  - Outil Capture Windows ;
  - horodatages epoch de 10 ou 13 chiffres.

Les noms d'entrée en 12 h sont convertis ; la sortie est toujours en 24 h.

### 5.3 Formats de date ambigus

ISO, européen (JJ-MM-AAAA) et américain (MM-JJ-AAAA) sont tous reconnus. Une date ambiguë se tranche dans cet ordre :
1. une composante supérieure à 12 ;
2. la locale du profil ;
3. le consensus des fichiers voisins du même dossier ;
4. les autres candidats.

À défaut, la date est marquée **ambiguë** et envoyée en revue.

### 5.4 Confrontation, plausibilité, confiance

- Sont rejetées les valeurs hors de la fenêtre [1990-01-01, aujourd'hui + 1 jour] et les horloges remises à zéro (`2000-01-01 00:00`, `1970-01-01`).
- Un désaccord entre candidats est expliqué quand c'est possible. Par exemple, une photo transférée par messagerie a un EXIF ancien et un nom récent : la prise de vue réelle est retenue, l'autre valeur conservée comme information.
- Des candidats concordants renforcent la confiance.
- Quatre niveaux de confiance :
  - **Sûre** et **Probable** : appliquées automatiquement ;
  - **Incertaine** : envoyée dans la file « À vérifier » ;
  - **Aucune** : jamais inventée, le fichier est signalé.
- **Outils manuels** : décalage horaire par lot (ex. `album_japon` : +8 h, fuseau Asia/Tokyo), date fixée pour une sélection, choix d'un candidat. Toute correction manuelle donne la confiance « Sûre (manuelle) ».

### 5.5 Règles de nommage

**Objectif :** dans chaque famille de fichiers, l'ordre alphabétique est identique à l'ordre chronologique.

- **Photos et captures :** `IMG-AAAAMMJJ-HHmmss.ext`.
- **Vidéos :** `VID-AAAAMMJJ-HHmmss.ext`.
- **Exception Live Photo :** la partie vidéo d'une Live Photo garde le préfixe `IMG-` et le même nom de base que sa photo, pour préserver l'association.
- **Collision** dans la même seconde : suffixe `-01`, `-02`…, numéroté dans l'ordre réel (sous-secondes EXIF, compteur WhatsApp, puis empreinte de contenu en dernier recours). Un fichier déjà nommé n'est jamais renuméroté : un nouveau venu prend le prochain numéro libre.
- **Date au jour seulement :** `IMG-AAAAMMJJ-000000-NN.ext`. Le `000000` sert uniquement au tri et regroupe ces fichiers en tête de journée ; la précision réelle (« jour ») reste enregistrée dans les métadonnées.
- **Extension** en minuscules. `.jpeg` est normalisé en `.jpg`.
- **Idempotence :** un fichier déjà normalisé et cohérent n'est pas modifié.
- Le modèle de nom restera paramétrable ; le format ci-dessus est celui par défaut.

```
IMG-20240105-000000-01.jpg   WhatsApp WA0017 (jour seul)
IMG-20240105-091530.heic     photo Samsung
IMG-20240105-150412.heic     Live Photo (photo)
IMG-20240105-150412.mov      Live Photo (mouvement)
IMG-20240105-150412-01.jpg   rafale, 1re
IMG-20240105-201145.png      capture Windows
VID-20240105-183000.mov      vidéo iPhone
VID-20240105-183000-01.mp4   vidéo WhatsApp à la même seconde
```

## 6. Écriture, groupes, doublons, conversion, confidentialité

### 6.1 Écriture des métadonnées

Toutes les écritures passent par ExifTool, puis sont vérifiées (§7).

| Format | Écriture |
|---|---|
| JPEG, HEIC, PNG, WebP | EXIF `DateTimeOriginal`, `CreateDate`, `OffsetTime*`, `SubSec*` + équivalents XMP |
| MOV, MP4 | Dates QuickTime en UTC réelle + `Keys:CreationDate` avec décalage |
| RAW | Sidecar `.xmp` uniquement |
| Dates du fichier | L'instant exact. Windows et macOS : création + modification. Linux : modification seulement (limite du système) |

Règles :
- On complète ou on corrige ; on n'écrase jamais une valeur plus fiable par une moins fiable.
- La marque et le modèle ne sont jamais modifiés. Un nom lisible (« Samsung Galaxy S21 FE ») sert uniquement à l'affichage.
- **Date au jour seulement :** XMP en précision « jour » ; EXIF à **12:00:00** par défaut (réglable), pour qu'aucun fuseau ne fasse changer le jour affiché.

### 6.2 Groupes de fichiers liés

Ces fichiers sont toujours renommés et déplacés ensemble :
- Live Photo (photo + `.mov`, associés par `ContentIdentifier` Apple, sinon par le nom de base) ;
- sidecars `.xmp` et `.aae` ;
- `.json` Google Takeout, y compris les noms tronqués (`photo.jp.json`, `photo(1).jpg.json`) ;
- paires RAW + JPEG.

Les Motion Photos Samsung et Google contiennent leur vidéo dans le fichier image : sa présence est contrôlée après chaque écriture.

### 6.3 Doublons

- **Identiques** (même empreinte SHA-256) : on garde un exemplaire, les autres vont en quarantaine.
- **Dérivés** (copies `-min`, exports, recompressions), repérés par empreinte perceptuelle : ils sont reliés à l'original et peuvent **hériter de sa date**.

### 6.4 Conversion JPEG

- Optionnelle, par lot. Qualité réglable, 90 par défaut.
- Copie de toutes les métadonnées, de l'orientation et du profil couleur ICC (Display P3).
- Décodage via Magick.NET (HEIC, AVIF, RAW).
- Conversion ignorée si le résultat serait plus lourd que la source.
- Captures PNG exclues par défaut (forçable).
- Les vidéos ne sont pas converties.
- L'original part en quarantaine après vérification, jamais en suppression directe.

### 6.5 Mode confidentialité

- **Retire :** GPS (photos et vidéos), numéros de série du boîtier et de l'objectif, marque, modèle, logiciel, propriétaire, MakerNotes, historique XMP, miniature embarquée.
- **Conserve :** la date, l'orientation et le profil couleur.
- **Deux usages :**
  - **Exporter pour partage**, le cas principal : une copie nettoyée va dans un dossier choisi.
  - **Nettoyage sur place**, avec vérification et quarantaine.

## 7. Exécution sûre

Le niveau de risque dépend de l'opération :

| Opération | Risque | Traitement |
|---|---|---|
| Renommer, dates du fichier | Faible | Sur place, journalisé, annulable |
| Écrire les métadonnées internes | Élevé | Écriture atomique : fichier temporaire, puis vérification (relecture des métadonnées + empreinte des pixels inchangée + Motion Photo intacte), puis remplacement. L'original est conservé en quarantaine |
| Convertir | Moyen | Nouveau fichier ; l'original va en quarantaine après vérification |

Déroulement d'un lot :
- Le **plan est figé** et enregistré.
- **Contrôles préalables** : espace disque, droits, fichier inchangé depuis le calcul du plan (taille, date de modification, empreinte), fichier non verrouillé.
- **Journal écrit avant l'action** (états *en attente* → *fait*). Au démarrage, un lot interrompu est détecté, puis terminé ou annulé proprement.
- Un échec sur un fichier n'arrête pas le lot. L'annulation est possible à tout moment : le fichier en cours se termine, puis le traitement s'arrête.
- **Annuler ce lot** : renommages inversés, originaux restaurés depuis la quarantaine.
- **Quarantaine** sur le même volume que les photos, hors des dossiers synchronisés (Nextcloud). Chaque emplacement est enregistré dans la base. Purge manuelle, ou automatique après 30 jours (réglable).
- **Nextcloud** : le dossier synchronisé est détecté, un avertissement s'affiche avant les gros lots, et le traitement par tranches est proposé.
- Résumé de fin de lot avec la liste des erreurs, exportable.

## 8. Catalogue et interface

### 8.1 Catalogue

- SQLite (`Microsoft.Data.Sqlite`) : sources, fichiers, métadonnées brutes, décisions de normalisation, groupes, doublons, journal des lots.
- Indexation en arrière-plan, parallèle et incrémentale (chemin, taille, date de modification), avec surveillance des dossiers.
- Miniatures en cache : miniature EXIF embarquée en priorité, décodage sinon.
- **Exclusions :** dossiers cachés, fichiers de 0 octet, vrai format différent de l'extension (signalé), règles d'exclusion par dossier, quarantaine de l'application.
- **Objectifs de performance :** ~10 000 fichiers indexés en quelques minutes au premier passage, réouverture en moins d'une seconde, défilement fluide (grille virtualisée).

### 8.2 Écrans

1. **Galerie** : grille chronologique par mois. Filtres : type (photo, vidéo, capture), appareil, source, confiance, « À vérifier », doublons, sans date.
2. **Visionneuse** : zoom et navigation. Les vidéos s'ouvrent d'abord dans le lecteur du système. Mini-carte (Mapsui) quand des coordonnées existent ; les fonds de carte en ligne sont désactivables, et dans ce cas seules les coordonnées s'affichent.
3. **Panneau Propriétés :**
   - onglet **Résumé**, organisé comme l'onglet Détails de Windows (Origine, Image, Appareil photo, GPS, Fichier) ;
   - onglet **Toutes les métadonnées** : tableau `Groupe | Balise | Valeur` groupé par bloc (EXIF, GPS, XMP, MakerNotes, QuickTime, ICC, Fichier), avec recherche, copie et export CSV/JSON ; un mode exhaustif à la demande passe par ExifTool ;
   - champs privés surlignés, et modifications prévues affichées en *avant → après*.
4. **Revue « À vérifier »** : candidats côte à côte, choix applicable à une série.
5. **Aperçu du plan** : avant/après, compteurs, avertissements, bouton **Appliquer**.
6. **Journal** : historique des lots, bouton **Annuler ce lot**.
7. **Réglages › Stockage** : taille de chaque élément, vider le cache, purger la quarantaine, réinitialiser.

## 9. Données de l'application, installation, désinstallation

**Règle, dès la phase 0 :** tout ce que l'application produit vit sous des racines centralisées, résolues dans le code par une classe unique. Rien n'est écrit à côté des photos, sauf les sidecars `.xmp` demandés explicitement.

| | Windows | Linux | macOS |
|---|---|---|---|
| Base, réglages, journaux | `%LOCALAPPDATA%\PhotoManager\` | `$XDG_DATA_HOME/photomanager/` | `~/Library/Application Support/PhotoManager/` |
| Cache (miniatures, ExifTool extrait) | `%LOCALAPPDATA%\PhotoManager\cache\` | `$XDG_CACHE_HOME/photomanager/` | `~/Library/Caches/PhotoManager/` |
| Quarantaine | Même volume que les photos, hors dossiers synchronisés, emplacements enregistrés en base | idem | idem |

**Installateur (phase 7) :**
- Velopack : installation, désinstallation et mises à jour automatiques sur les 3 OS.
- Signature de code : Azure Trusted Signing pour Windows, notarisation Apple pour macOS.
- Formats : AppImage pour Linux, `.app`/`.dmg` pour macOS.
- **Désinstallation :** si la quarantaine contient des originaux, le désinstallateur avertit et propose de les restaurer ; il ne les supprime jamais en silence. Le catalogue et le cache ne sont supprimés qu'à la demande.

## 10. Feuille de route

Chaque phase livre une application utilisable. L'interface existante évolue à chaque phase ; la galerie complète arrive en phase 4. La compilation et les tests sur Windows, Linux et macOS tournent en intégration continue dès la phase 0.

| Phase | Contenu | Critère de fin |
|---|---|---|
| **0. Fondations** | Correction des 9 défauts du §3.1, mise à jour d'ImageSharp, découpage en projets (§4), racines de données centralisées (§9), corpus de test initial, intégration continue sur 3 OS | Le flux actuel est fiable sur JPEG et PNG ; un 2ᵉ passage sur le même dossier ne produit ni erreur ni doublon ; tests verts sur les 3 OS |
| **1. Moteur de normalisation** (lecture seule) | §5 en entier ; commande `analyse` dans la CLI, avec rapport (CSV + résumé) | Rapport sur la bibliothèque réelle sans aucune écriture ; chaque source du §3.2 reconnue par son profil ; objectif chiffré de « Sûre » + « Probable » fixé dans la spec de phase |
| **2. Exécution sûre** | Renommage sur place + dates du fichier, §6.2 et §7 (plan, journal, annulation, quarantaine, reprise) | Renommer puis annuler un lot réel redonne un état identique (noms, contenus, dates) |
| **3. Écriture des métadonnées** | ExifTool embarqué, §6.1, vérification d'écriture, décalage horaire par lot | `album_japon` corrigé ; HEIC Samsung et MOV iPhone intacts (pixels, Motion Photo, métadonnées non visées) |
| **4. Catalogue et galerie** | §8 en entier, dont le panneau Propriétés et la page Stockage | ~9 000 fichiers parcourus de façon fluide ; réouverture en moins d'une seconde |
| **5. Doublons et conversion** | §6.3 et §6.4 | Les 295 doublons `Camera`/`DCIM\Camera` traités ; dates de `album_japon_final` récupérées par héritage |
| **6. Confidentialité et carte** | §6.5 + mini-carte | Un export ne contient plus aucune donnée privée (contrôle avec ExifTool) |
| **7. Distribution** | §9 (Velopack, signature, AppImage, `.dmg`) | Installation, mise à jour et désinstallation propres sur les 3 OS |
| **8. Extensions** | RAW, import Google Takeout et iCloud, lecteur vidéo intégré, nouveaux profils | Au fil de l'eau |

## 11. Stratégie de test

- **Core :** tests unitaires exhaustifs, sans fichier. Un test par motif de nom, par format de date et par cas d'ambiguïté ou de conflit.
- **Corpus :** fichiers d'exemple réels par source, nettoyés de toute donnée privée, versionnés dans le dépôt. Tests d'intégration lecture → décision → écriture → relecture.
- **Operations :** tests d'intégration sur dossiers temporaires, avec coupure simulée au milieu d'un lot, annulation et contrôle d'état identique.
- **Validation sur données réelles :** rapport de la CLI sur la bibliothèque, toujours en lecture seule ou sur une copie.
- **Intégration continue :** compilation et tests sur Windows, Linux et macOS.

## 12. Hors périmètre

- Retouche d'image, montage vidéo, conversion vidéo.
- Synchronisation cloud, comptes, fonctionnalités en ligne (hors fonds de carte optionnels).
- Reconnaissance faciale, étiquetage automatique par IA.
