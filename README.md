# StockHelper

Desktop app for tracking consumables: item catalog, stock-taking, automatic consumption calculation, stock forecasting and Excel export.

The UI is in Russian; code, commits and docs are in English.

## Features (planned / in progress)

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
dotnet build
dotnet test
dotnet run --project src/StockHelper.App
```

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

## Releases

Versioning follows SemVer. Pushing a tag `vX.Y.Z` builds, tests, packs the app with Velopack and
publishes it to GitHub Releases; installed apps pick up the update automatically.
