# Développement de l'app Frigo Tracker

Document pour reprendre le code dans un nouveau chat. Le code à jour est **dans le repo GitHub** : toujours partir du repo.

## Où est quoi

- **Repo** : https://github.com/avallarino1984/FrigoTracker (public, branche `main`)
- **Copie locale** : `~/Documents/GitHub/FrigoTracker` sur le Mac mini, gérée avec GitHub Desktop
- **App en ligne** : https://avallarino1984.github.io/FrigoTracker/ (à installer sur les iPhone : Safari → Partager → Sur l'écran d'accueil)
- **Déploiement** : chaque push sur `main` → GitHub Actions (tests puis publication GitHub Pages), 1-2 min
- **Données** : Supabase, **même projet que Calories Tracker** (`ldvozfwkzftflkeisqhl`), tables préfixées `frigo_`. URL + clé publishable dans `wwwroot/appsettings.json` (publiques par conception). Connexion courriel + mot de passe, comptes créés dans le tableau de bord, inscriptions désactivées.

## Stack et conventions

- .NET 10, Blazor WebAssembly en PWA, VS Code sur Mac ; tests xUnit
- **Commentaires de code toujours en anglais** ; textes de l'interface en français
- Logique pure dans `FrigoTracker.Core` (testée), UI dans `FrigoTracker.Web`
- Les pages ne parlent qu'à `AppState`

## Modèle de données (partagé à deux)

- `frigo_households` : le foyer, avec un `join_code` de 6 caractères
- `frigo_members` : qui fait partie du foyer (un compte Supabase chacun)
- `frigo_items` : **une ligne par article** (pas un document JSON comme Calories Tracker), pour que deux personnes puissent modifier en même temps sans s'écraser
- `frigo_grocery` : liste d'épicerie partagée
- RLS : `frigo_is_member(household_id)` (security definer) ; création/adhésion par les fonctions `frigo_create_household` et `frigo_join_household`
- Trigger `frigo_touch` : `updated_at` et `updated_by` fixés par le serveur
- Enums stockés en texte (`Fridge`, `Freezer`, `Meat`…) : **ne jamais renommer un membre d'enum**, sinon les lignes existantes ne se relisent plus

## Synchro

- Chaque modification : appliquée localement (localStorage), mise dans l'**outbox** (`PendingChange` : table + id + Upsert/Delete), puis envoyée ligne par ligne
- Synchro = vider l'outbox, puis relire toutes les lignes du foyer ; les lignes avec une modif encore en attente gardent la version locale (`Outbox.MergePulled`)
- Relecture à chaque retour au premier plan (`visibilitychange`) et via le bouton ⟳
- Se déconnecter efface les données locales (le nuage les garde) ; refusé s'il reste des modifs non envoyées

## Règles de conservation

- `StorageGuide.cs` : durée max au congélateur et durée après décongélation, par catégorie (viande 8 mois, haché 3 mois, volaille 9 mois…). **Seul endroit à modifier** pour ajuster les durées
- Échéance (`FreshnessCalculator.Deadline`) : date explicite si présente ; sinon au congélateur, date de congélation + mois du guide ; sinon aucune
- Alerte : frigo 3 jours avant, garde-manger 7 jours, congélateur 30 jours
- Mettre au congélateur remet la date de rangement à aujourd'hui et efface la date de l'emballage ; décongeler donne la durée « décongelé » de la catégorie
- Popup « À surveiller » à l'ouverture, une fois par jour (`AlertsDismissedOn`), + badge rouge sur l'onglet Frigo

## Design

- Direction « étiquettes de congélation » : fond givre, chaque article est une étiquette en ruban jaune avec le nom au marqueur (Kalam), texte en Atkinson Hyperlegible ; polices dans `wwwroot/fonts` (cachées hors ligne, `.woff2` inclus dans le service worker)
- Inventaire : un onglet par emplacement (point rouge s'il y a de l'urgent), sections « À manger d'abord » / « Le reste », jauge = part de la durée de conservation écoulée (`FreshnessCalculator.ElapsedShare`)
- Pas de boutons sur les étiquettes : toucher → page de l'article avec « Manger 1 » / « Terminé » (+ ajout à l'épicerie coché par défaut)
- « Ajouter » reprend l'emplacement affiché (`Home.LastLocation`)

## Structure

```
src/FrigoTracker.Core/
  Models.cs          InventoryItem, GroceryItem, StorageLocation, FoodCategory
  StorageGuide.cs    Durées de conservation par catégorie
  Freshness.cs       Échéances, niveaux (Fresh/Soon/Expired), textes
  InventoryRules.cs  Consommer, déplacer (congeler/décongeler), doublons d'épicerie
  Outbox.cs          File des modifs non envoyées, fusion à la relecture
src/FrigoTracker.Web/
  Services/AppState.cs, SupabaseClient.cs (REST, sans package)
  Pages/ Home (Frigo), ItemEdit (Ajouter / Modifier), Grocery (Épicerie), Settings (Réglages)
  Shared/ ItemLabel, AlertPopup, AccountCard, HouseholdCard, GroceryLine, StateComponent, Fmt
  wwwroot/ appsettings.json, index.html, service-worker.published.js, icônes
tests/FrigoTracker.Core.Tests/
supabase/setup.sql
.github/workflows/deploy.yml
```

## Pièges (hérités de Calories Tracker)

- **Razor** : `< 11 =>` dans un `switch` d'un bloc `@code` est lu comme une balise HTML → utiliser des `if`.
- **Safari iOS** : les `<input type="date">` débordent dans une grille → chacun sur sa ligne.
- **`<select>` lié à un `double`** : `@bind:culture="CultureInfo.InvariantCulture"` (navigateur en fr-CA).
- **Cache hors ligne** : préfixe `frigo-offline-cache-` (Calories Tracker utilise `offline-cache-` sur le même domaine github.io : un préfixe commun ferait effacer le cache de l'autre app).
- **GitHub Pages** sert l'app sous `/FrigoTracker/` : le `<base href>` et le service worker calculent ce chemin à l'exécution.
- **Service worker** : `skipWaiting` + `clients.claim` + rechargement sur `controllerchange`.
- **Supabase, GRANT obligatoires** pour les tables créées depuis le 2026-10-30 : déjà dans `setup.sql`. Toute nouvelle table doit avoir ses `grant … to authenticated` dans le même script, sinon « permission denied ».
- **Ajout de champs** : prévoir une valeur par défaut côté C# (anciennes données locales) et une colonne avec `default` côté SQL.

## Façon de travailler

1. Le nouveau chat lit le code dans le repo (ou le clone).
2. Modifs + tests ; vérifier que `dotnet test` passe.
3. Livraison : zip des fichiers modifiés → `unzip -o` dans `~/Documents/GitHub/FrigoTracker` → GitHub Desktop : Commit to main, Push origin.
