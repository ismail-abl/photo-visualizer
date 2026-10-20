# Phase 2 — Exécution sûre (renommage sur place)

- **Date :** 2026-10-19
- **Statut :** implémentée sur la branche feat/phase2-execution-sure, en attente de validation
- **Cadre :** [vision et feuille de route](2026-10-14-vision-feuille-de-route-design.md), §6.2, §7 et §10 (phase 2). Cette spec précise la phase ; en cas d'écart, elle l'emporte sur le cadre pour la phase 2. Elle s'appuie sur le moteur de la [phase 1](2026-10-19-phase1-moteur-normalisation-design.md).

---

## 1. Objectif

Appliquer pour de bon les noms proposés par le moteur de phase 1 : **renommer les médias sur place et fixer les dates du fichier**, avec un lot figé, un journal écrit avant chaque action, une reprise après interruption et une annulation complète. La phase livre aussi les trois correctifs du moteur qui faussaient des renommages réels (§8).

## 2. Critère de fin

1. Renommer puis annuler un lot réel redonne un état identique : mêmes noms, mêmes contenus, mêmes dates (modification et création).
2. Un lot coupé au milieu (processus tué) est détecté au lancement suivant, puis terminé ou annulé sans perte ni écrasement.
3. Aucun fichier n'est écrasé, supprimé ou modifié dans son contenu ; seuls le nom et les dates changent.
4. Les trois correctifs du moteur (§8) sont en place, et la ré-analyse des 9 453 médias de référence ne change que les fichiers attendus.
5. Les tests sont verts sur Windows (Linux et macOS hors champ jusqu'à la fin du projet).

## 3. Périmètre

**Inclus :** renommage sur place des photos **et des vidéos** ; dates du fichier ; Live Photo et sidecars renommés avec leur média ; journal SQLite ; reprise ; annulation ; composant de quarantaine (brique testée) ; commandes CLI `renomme`, `journal`, `annule`, `reprend` ; deux boutons dans l'interface ; trois correctifs du moteur.

**Reporté :**

| Élément | Phase |
|---|---|
| Conversion sur place (HEIC, HEIF, AVIF → JPEG), original en quarantaine | 3 |
| Écriture des métadonnées internes (ExifTool) | 3 |
| Écrans Aperçu du plan et Journal, revue « À vérifier » | 4 |
| Empreinte SHA-256 avant renommage, doublons | 5 |
| Emplacement par défaut de la quarantaine, commandes CLI de la quarantaine | 3 (premier usage réel) |

Les fichiers HEIC, HEIF et AVIF sont **renommés tels quels** (`IMG-20240312-143005.heic`) et signalés « conversion différée » dans le plan.

## 4. Architecture

Dépendances de projets inchangées : `Operations` ne référence que `Core`, `Catalog` référence `Core` et `Metadata`.

| Projet | Reçoit |
|---|---|
| `PhotoApp.Core` | Modèle d'un lot (plan, éléments, états), interface `IBatchJournal` |
| `PhotoApp.Catalog` | `CatalogDatabase` (SQLite, `catalog.db` via `AppDataPaths.DatabasePath`) et `SqliteBatchJournal`, qui implémente `IBatchJournal` |
| `PhotoApp.Operations` | `BatchPlanner`, `InPlaceRenamer` (exécution), `BatchRecovery`, `BatchUndo`, `FileTimestamps`, `Quarantine` |
| `PhotoApp.Cli` | Commandes `renomme`, `journal`, `annule`, `reprend` |
| `PhotoApp` (interface) | Deux boutons : « Renommer sur place » et « Annuler le dernier lot » |

Nouvelle dépendance : `Microsoft.Data.Sqlite`, dans `PhotoApp.Catalog`.

L'ancien `BatchRenameExecutor` (dossier de sortie, conversion JPEG) reste intact : c'est le flux actuel de l'interface. Le renommage sur place est un flux distinct qui n'écrit qu'à travers `InPlaceRenamer`.

## 5. Cycle de vie d'un lot

### 5.1 Plan

`BatchPlanner` prend les résultats de l'analyse de phase 1 et retient les médias dont la confiance est **Sûre ou Probable** et qui ont un nom proposé. Les médias déjà au bon nom sont comptés « déjà conformes » et ne produisent aucun élément.

Chaque média retenu devient un **élément** ; ses fichiers liés forment avec lui un **groupe** :
- la vidéo ou la photo partenaire d'une Live Photo ;
- les sidecars (`.xmp`, `.aae`), qui gardent la convention du fichier d'origine : `photo.xmp` devient `<nouveau-nom-sans-extension>.xmp`, `photo.jpg.xmp` devient `<nouveau-nom-complet>.xmp`.

Un groupe est **tout ou rien** au moment du plan : si un de ses fichiers est écarté par les contrôles (§5.2), tout le groupe l'est.

Le plan est **figé** puis écrit dans le journal (lot à l'état *planifié*). Pour chaque fichier, il enregistre : chemin d'origine, chemin cible, date décidée (avec son décalage), taille, date de modification et date de création d'origine.

Un nom cible ne peut jamais être celui d'un fichier existant hors lot. Les noms sont alloués par l'allocateur de phase 1, qui connaît les noms présents dans le dossier.

### 5.2 Contrôles préalables

Fichier par fichier, avant l'exécution. Un fichier qui échoue est **écarté avec sa raison**, le lot continue.

- Le fichier existe encore.
- Il n'a pas changé depuis le plan (taille et date de modification).
- Le nom cible est libre.
- Le fichier n'est pas verrouillé (ouverture exclusive possible).
- Le chemin cible respecte la limite de longueur du système.

### 5.3 Exécution

États du lot : *planifié* → *en cours* → *terminé* ou *interrompu* ; après annulation : *annulé*.
États d'un élément : *en attente* → *en cours* → *fait*, ou *échec*, ou *écarté* ; après annulation : *annulé* ou *échec d'annulation*.

Pour chaque fichier, dans l'ordre du plan :

1. Le journal passe l'élément à *en cours* (transaction validée **avant** l'action).
2. `File.Move` dans le même dossier (atomique sur le même volume).
3. `FileTimestamps` fixe la date de modification et la date de création sur la date décidée (heure murale convertie avec le décalage connu ; fuseau du PC sinon). Sous Linux, la date de création n'est pas modifiable et n'est ni fixée ni restaurée.
4. Le journal passe l'élément à *fait* et enregistre la taille et la date de modification obtenues (pour détecter une modification ultérieure à l'annulation).

Un échec sur un fichier (verrou, droits, collision tardive) le passe à *échec* avec le message, remet le fichier dans son état d'origine si le déplacement avait eu lieu, et **n'arrête pas le lot**.

**Arrêt demandé** (Ctrl+C ou bouton) : le fichier en cours se termine, puis le lot passe à *interrompu*.

**Aucun fichier n'est renommé deux fois** : il n'y a pas de nom temporaire intermédiaire. Cela évite de doubler les événements de synchronisation dans un dossier Nextcloud.

### 5.4 Reprise

Au lancement de `renomme`, `annule`, `reprend` et `journal`, un lot *en cours* ou *interrompu* est détecté et signalé. `BatchRecovery` résout chaque élément laissé *en cours* **d'après le disque** :

| Ancien nom | Nouveau nom | Décision |
|---|---|---|
| absent | présent | l'élément est *fait* ; les dates sont réappliquées si besoin |
| présent | absent | l'élément repasse *en attente* |
| présent | présent | **anomalie** : l'élément passe à *échec* (« les deux noms existent »), rien n'est écrasé |
| absent | absent | **anomalie** : l'élément passe à *échec* (« fichier introuvable ») |

Le lot peut ensuite être terminé (`reprend`) ou annulé (`annule`).

### 5.5 Annulation

`BatchUndo` traite les éléments *fait* dans l'ordre inverse : renommage du nom cible vers le nom d'origine, puis restauration des dates d'origine (modification et création) enregistrées dans le journal.

- Si le fichier cible a changé depuis le lot (taille ou date de modification différentes de celles enregistrées en 5.3), l'élément est **signalé et laissé tel quel** (état *échec d'annulation*), jamais écrasé.
- Si le nom d'origine est occupé par un autre fichier, même traitement.
- Un lot *annulé* ne peut pas l'être deux fois. Un lot partiellement annulé peut être repris par la même commande.

### 5.6 Quarantaine

`Quarantine` est une brique testée, **non utilisée par la phase 2** : déplacer un fichier vers un dossier de quarantaine donné, enregistrer son emplacement d'origine dans la base, le restaurer, purger. Le dossier est un paramètre ; son emplacement par défaut (même volume que les photos, hors dossiers synchronisés, cadre §7) sera choisi en phase 3, avec la conversion sur place qui en est le premier usage.

## 6. Journal (SQLite)

Base `catalog.db`, ouverte en mode WAL. Chaque changement d'état est une transaction. Les tables de la phase 2 sont `batch` (identifiant, date de création, dossiers, options, état, compteurs) et `batch_item` (lot, rang dans le lot, groupe, rôle : média, partenaire Live Photo ou sidecar, chemins d'origine et cible, état, message d'erreur, date décidée, taille, dates d'origine et dates obtenues). La table `quarantine_entry` s'y ajoute pour la brique de la quarantaine. Une version de schéma est enregistrée pour les migrations des phases suivantes (le catalogue de la phase 4 réutilise la même base).

Avertissement avant la **réinitialisation du catalogue** (cadre §8.2, Réglages › Stockage, phase 4) : elle supprimerait aussi le journal, donc l'annulation des lots. La phase 2 n'ajoute aucune réinitialisation ; cette règle est consignée pour la phase 4.

## 7. CLI

Même style que `analyse` (`System.CommandLine`). Les rapports vont dans `reports\` des données de l'application, jamais dans un dossier analysé (garde de la phase 1 reprise).

- **`photoapp renomme <dossiers…> [--exclude <dossier>]… [--applique] [--oui] [--tranche <n>]`**
  - Sans `--applique` : **dry-run**. Analyse, construit le plan (Sûre et Probable), affiche les compteurs (à renommer, déjà conformes, écartés avec leur raison, conversion différée), écrit un CSV du plan dans `reports\`, ne modifie aucun fichier.
  - Avec `--applique` : enregistre le plan, demande une confirmation interactive (nombre de fichiers) sauf avec `--oui`, exécute.
  - Dossiers situés dans un dossier Nextcloud : avertissement avant un gros lot, traitement par tranches de `<n>` fichiers proposé.
- **`photoapp journal [--lot <id>]`** : liste des lots (date, dossiers, compteurs, état) ; avec `--lot`, le détail fichier par fichier, échecs et écartés compris.
- **`photoapp annule [<lot>]`** : annule le lot indiqué, ou le dernier lot terminé s'il est omis. Confirmation sauf `--oui`.
- **`photoapp reprend <lot>`** : termine un lot interrompu.

**Codes de sortie :** 0 succès ; 1 arguments invalides ; 2 erreur fatale ; **3 lot terminé avec des fichiers en échec**. `--oui` n'a aucun effet sans `--applique`.

## 8. Correctifs du moteur

1. **Garde par source dans `MetadataReader`.** Chaque source (EXIF, GPS, XMP, QuickTime) est lue dans sa propre garde. Une valeur mal formée supprime cette source seule, avec un drapeau dans le rapport, au lieu de faire perdre la date du fichier entier.
2. **Live Photo (`MediaNamer`).** Une vidéo ne prend le nom de sa photo que si les deux décisions s'accordent (même date à la seconde près) ou si la vidéo n'a pas de décision applicable propre. L'appariement par nom de base exige que les `ContentIdentifier` concordent quand les deux fichiers en ont un. En cas de conflit, chaque fichier suit sa propre décision et le rapport le signale.
3. **Captures d'écran (`DateArbiter`).** Une métadonnée (EXIF ou XMP) **antérieure** au nom d'une capture fait foi, classée Sûre (`exif-avant-nom`), comme pour les appareils : le nom est l'heure d'enregistrement, la métadonnée l'heure réelle de la capture. Une métadonnée postérieure reste en désaccord. Cas réel : `Screenshot 2025-05-19 224500.png`, XMP à 22:44:46.079, devient `IMG-20250519-224446.png`.
4. **Profil « epoch » nu.** Aucun changement de règle : un test fige le comportement actuel (0 fichier concerné dans la bibliothèque de référence).

## 9. Interface

Deux boutons seulement, qui appellent le même moteur que la CLI :
- **Renommer sur place** : analyse, résumé du plan, confirmation, exécution avec progression et bouton d'arrêt ;
- **Annuler le dernier lot** : confirmation, puis annulation.

Le flux actuel (dossier de sortie, conversion JPEG) reste en place. L'aperçu détaillé et l'écran Journal arrivent en phase 4.

## 10. Erreurs

- Un fichier en échec n'arrête jamais le lot ; le résumé de fin liste les erreurs (exportable depuis `journal --lot`).
- Aucune opération n'écrase un fichier : toute collision de nom est un échec explicite.
- Base de données inaccessible ou verrouillée : erreur fatale (code 2) **avant** toute action sur les fichiers.
- Un second processus ne peut pas exécuter un lot en même temps : l'exécution prend un verrou sur la base ; le second reçoit un message clair (code 2).

## 11. Tests

- **Unitaires :** états d'un lot ; `BatchPlanner` (Sûre et Probable retenus, déjà conformes, écartés, groupes tout ou rien, nom des sidecars) ; les quatre combinaisons de `BatchRecovery` ; `SqliteBatchJournal` (transactions, schéma, verrou) ; `FileTimestamps` ; `Quarantine` ; les correctifs du §8.
- **Intégration, sur dossiers temporaires** (cadre §11) :
  - renommer puis annuler redonne un état identique (noms, contenus, dates) ;
  - coupure simulée au milieu d'un lot, puis reprise, puis annulation ;
  - fichier modifié entre le plan et l'exécution ; nom cible déjà pris ; fichier verrouillé ;
  - Live Photo et sidecars renommés et annulés en groupe ;
  - fichier modifié après le lot : l'annulation le laisse en place et le signale.
- **CLI de bout en bout** sur dossier temporaire : dry-run sans écriture (comparaison avant/après de l'arborescence), codes de sortie, confirmation, `--oui`.
- **Régression du moteur :** ré-analyse des 9 453 médias de référence, diff avec le rapport de la phase 1 ; seuls les fichiers attendus changent.
- **Vérification manuelle** sur une copie d'un petit dossier réel avant tout lot sur la bibliothèque : dry-run, application, annulation, comparaison des états.

## 12. Décisions de la session du 2026-10-19

| Question | Décision |
|---|---|
| Surface de la phase | CLI d'abord ; interface réduite à deux boutons ; écrans Aperçu et Journal en phase 4 |
| Fichiers HEIC, HEIF, AVIF | Renommés tels quels, conversion différée à la phase 3 |
| Stockage du journal | SQLite dans `catalog.db` (`Microsoft.Data.Sqlite`) |
| Fichiers renommés par défaut | Sûre **et** Probable ; le dry-run reste le comportement de base, `--applique` déclenche l'exécution |
| Points ouverts de la phase 1 | Les quatre traités : garde GPS, Live Photo, captures (métadonnée antérieure au nom), test du profil epoch |
| Stratégie de renommage | Journal d'abord, déplacements directs, un seul renommage par fichier ; pas de noms temporaires |
| Écarts d'implémentation | Verrou d'exécution par fichier `catalog.db.lock` ; colonne « options » du lot supprimée ; code de sortie 3 aussi pour un lot interrompu ; changement de casse seul en deux temps (voir le plan, « Écarts assumés ») |
