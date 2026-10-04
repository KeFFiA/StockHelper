# StockHelper

Desktop app for tracking consumables: item catalog, stock-taking, automatic consumption calculation, stock forecasting and Excel export.

**User guides (Russian):** [overview](docs/README.md) · [general](docs/general.md) · [storekeeper](docs/storekeeper.md) · [manager](docs/manager.md) · [administrator](docs/administrator.md)

**Download:** [latest release](https://github.com/KeFFiA/StockHelper/releases/latest) — `StockHelper-win.msi` ·
[demo version](https://github.com/KeFFiA/StockHelper/releases/tag/demo) — `StockHelper-Demo.exe` with sample data.

## Features

- Item catalog: categories, units, storage locations, minimum stock, price, archiving
- Receipts (purchases)
- Stock-takes: draft → completed, counted per storage location, keyboard-first entry
- Analytics: consumption between stock-takes, average daily usage, days-left forecast, purchase list, consumption cost
- Excel export of any table and dedicated reports
- Users with roles (Administrator / Manager / Storekeeper)
- Automatic backups before database migrations
- Automatic updates via GitHub Releases

## Tech stack

.NET 10 · WPF (Fluent theme) + MVVM (CommunityToolkit.Mvvm) · Microsoft.Extensions.Hosting ·
EF Core + SQLite (PostgreSQL-ready) · ClosedXML · Velopack · Serilog · xUnit

## Solution layout

```
src/
  StockHelper.Core/   domain model, interfaces, pure calculation services (no WPF / EF Core)
  StockHelper.Data/   EF Core DbContext, configurations, migrations, repositories
  StockHelper.App/    WPF app: views, view models, themes, resources, DI, updates
tests/
  StockHelper.Core.Tests/
  StockHelper.Data.Tests/
```

Dependencies: `App → Data → Core`, `App → Core`.

## Getting started

Requirements: Windows 10/11, [.NET 10 SDK](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`).

```powershell
dotnet tool restore
dotnet build
dotnet test
dotnet run --project src/StockHelper.App
```

On the first start the app asks to create an administrator account.

### Trying the app with sample data

`--demo` fills an empty database with sample items, receipts and four monthly stock-takes.
Use `STOCKHELPER_DATA_DIR` to keep it away from your real data:

```powershell
$env:STOCKHELPER_DATA_DIR = "$env:TEMP\stockhelper-demo"
dotnet run --project src/StockHelper.App -- --demo
```

### Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+N | New record on the current page |
| Ctrl+F | Focus search |
| Ctrl+E | Export the current table to Excel |
| F5 | Refresh |
| Ctrl+1…9 | Open a menu section |
| Enter / ↑ / ↓ | Stock-take: save and move to the next / previous row |

### Database migrations

```powershell
dotnet ef migrations add <Name> --project src/StockHelper.Data --startup-project src/StockHelper.Data --output-dir Migrations
```

Migrations are applied on start-up; an existing database is backed up first.

## User data

All data is stored outside the install folder:

```
%APPDATA%\StockHelper\
  stockhelper.db   database
  backups\         automatic and manual backups
  logs\            log files
  settings.json    user settings
```

## Design system

Styles and design tokens (typography, spacing, radii, icons, control styles) live in
`src/StockHelper.App/Themes/`. Views reference tokens and styles only — no hardcoded colors or sizes.
Colors come from the WPF Fluent theme and follow the Windows light/dark mode and accent color.

## Branches

- `dev` — day-to-day work; feature branches merge here.
- `main` — releases only. Installed apps update from releases tagged on `main`.
- `demo` — customer demo. Every push rebuilds `StockHelper-Demo.exe` (portable, sample data, one-click
  accounts for each role, password `demo`, data in `%APPDATA%\StockHelper-Demo`) and publishes it as the
  [`demo` pre-release](https://github.com/KeFFiA/StockHelper/releases/tag/demo). To refresh it:
  `git push origin dev:demo` (or `main:demo`). Local demo build: `dotnet build src/StockHelper.App -p:DemoBuild=true`.

## Releases

Versioning follows SemVer. Every release must have a changelog entry.

1. Add a `## X.Y.Z — <date>` section (Russian, user-facing) to `src/StockHelper.App/Resources/Changelog.md`.
   The app shows it once after the update; the same section becomes the GitHub release description.
2. Bump `<Version>` in `Directory.Build.props`.
3. Merge `dev` into `main`, tag and push:

```powershell
git checkout main
git merge --ff-only dev
git tag v1.0.0
git push origin main v1.0.0
```

The tag triggers `.github/workflows/release.yml`: tests, self-contained publish, Velopack pack and
upload to GitHub Releases. `StockHelper-win.msi` is the only installer offered: a wizard with the license agreement
(`LICENSE.md`, Russian and English) and the choice to install for the current user or for all users; the one-click
`Setup.exe` and the portable zip are removed from the release. Installed apps download the
update in the background and offer to restart.

Code signing: add a `SIGN_PARAMS` repository secret with signtool arguments; without it packages are unsigned.

## License

Proprietary software. Copyright © 2026 KeFFiA. All rights reserved.
The source code is published for reference only; using, copying, modifying or distributing it requires the
copyright holder's permission. See [LICENSE.md](LICENSE.md) (Russian and English) and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the open-source components included in the app.
