# Frigo Tracker

Inventaire partagé du frigo, du congélateur et du garde-manger, avec liste d'épicerie et alertes de péremption
(ex. viande congelée depuis plus de 8 mois). Blazor WebAssembly (PWA) + Supabase, publié sur GitHub Pages.

App : https://avallarino1984.github.io/FrigoTracker/

## Mise en route

1. **Supabase** (projet existant) : SQL Editor → New query → coller `supabase/setup.sql` → Run.
2. **Comptes** : Authentication → Users → Add user → Create new user (courriel + mot de passe, « Auto Confirm User »), un par personne.
3. **GitHub Pages** : Settings → Pages → Source : **GitHub Actions**. Chaque push sur `main` teste et publie.
4. **Dans l'app** (Réglages) : la première personne se connecte et crée le foyer ; l'autre se connecte et entre le code affiché.

## Développement

```bash
dotnet test
dotnet run --project src/FrigoTracker.Web
```

Voir `docs/developpement.md` pour l'architecture, la synchro et les pièges connus.
