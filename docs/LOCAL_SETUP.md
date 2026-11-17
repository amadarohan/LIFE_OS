# Local Setup

Everything needed to run Life OS on a development machine, from a clean checkout.

---

## 1. Prerequisites

| Requirement | Minimum | How to check |
| --- | --- | --- |
| .NET SDK | 9.0 | `dotnet --list-sdks` |
| SQL Server | 2019 or later (Express is fine) | `sqlcmd -S localhost -E -Q "SELECT @@VERSION"` |
| EF Core CLI | 9.0.x | `dotnet ef --version` |

The solution targets `net9.0`. If you have .NET 10 installed, it can build the project, but the
target framework stays at 9.0 until the whole solution is migrated deliberately.

Install the EF Core CLI once per machine:

```powershell
dotnet tool install --global dotnet-ef --version 9.0.23
```

If it is already installed, update it instead:

```powershell
dotnet tool update --global dotnet-ef --version 9.0.23
```

---

## 2. NuGet source

The repository contains a `NuGet.config` at the root that clears inherited sources and uses only
`nuget.org`. This is deliberate: machine-level configs on some corporate networks point at internal
feeds that require credentials and break restore.

If your environment needs an additional feed, add it to the repo `NuGet.config` rather than
modifying the machine-level file.

---

## 3. Database

### Create the database

The application never calls `EnsureCreated()`. The schema comes from migrations only.

```powershell
dotnet ef database update `
  -p src/LifeOS.Infrastructure `
  -s src/LifeOS.Infrastructure
```

The design-time factory reads the connection string from the `LIFEOS_CONNECTION` environment
variable and falls back to:

```
Server=localhost;Database=LifeOS;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True
```

To target a different instance for a single command:

```powershell
$env:LIFEOS_CONNECTION = "Server=.\SQLEXPRESS;Database=LifeOS;Trusted_Connection=True;TrustServerCertificate=True"
dotnet ef database update -p src/LifeOS.Infrastructure -s src/LifeOS.Infrastructure
```

### Verify

```powershell
sqlcmd -S localhost -E -d LifeOS -Q "SELECT COUNT(*) FROM sys.tables"
```

Expect 42 rows: 41 domain tables plus `__EFMigrationsHistory`.

### Adding a migration

```powershell
dotnet ef migrations add <Name> `
  -p src/LifeOS.Infrastructure `
  -s src/LifeOS.Infrastructure `
  -o Persistence/Migrations
```

Always confirm the model and the snapshot agree before committing:

```powershell
dotnet ef migrations has-pending-model-changes -p src/LifeOS.Infrastructure -s src/LifeOS.Infrastructure
```

It must print `No changes have been made to the model since the last migration.`

### Resetting a development database

This deletes all local data. Only do this on a machine where the data is disposable.

```powershell
sqlcmd -S localhost -E -Q "ALTER DATABASE LifeOS SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE LifeOS;"
dotnet ef database update -p src/LifeOS.Infrastructure -s src/LifeOS.Infrastructure
```

---

## 4. Configuration

Settings live in `src/LifeOS.Web/appsettings.json`. Secrets must not be committed; use user secrets
for anything sensitive.

```powershell
dotnet user-secrets init --project src/LifeOS.Web
dotnet user-secrets set "ConnectionStrings:LifeOS" "Server=localhost;Database=LifeOS;Trusted_Connection=True;TrustServerCertificate=True" --project src/LifeOS.Web
```

| Key | Purpose | Default |
| --- | --- | --- |
| `ConnectionStrings:LifeOS` | SQL Server connection | none — required |
| `LifeOS:TimeZone` | IANA or Windows timezone id used for local dates | machine local zone |
| `LifeOS:StorageRoot` | Root folder for attachments | `storage` next to the binary |
| `Ai:Local:Endpoint` | Ollama base URL | `http://localhost:11434` |
| `Ai:Local:Model` | Local model name | none |
| `Ai:Cloud:Endpoint` | OpenAI-compatible base URL | none |
| `Ai:Cloud:Model` | Cloud model name | none |
| `Ai:Cloud:ApiKey` | Cloud API key — stored DPAPI-protected in the database, not in config | none |

API keys entered through the Settings page are encrypted with Windows DPAPI scoped to the current
user before being written to `AppSettings`. They are never written to `appsettings.json` and never
logged.

---

## 5. Build and run

```powershell
dotnet build LifeOS.sln --nologo
dotnet run --project src/LifeOS.Web
```

The app opens on the HTTPS URL from `src/LifeOS.Web/Properties/launchSettings.json`.

---

## 6. Tests

```powershell
dotnet test LifeOS.sln --nologo
```

Integration tests create a uniquely named database per run and drop it afterwards, so they need a
reachable SQL Server but do not touch the `LifeOS` development database.

---

## 7. Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| `NU1101` / `401 Unauthorized` during restore | Machine-level NuGet config points at an authenticated feed | Confirm the repo `NuGet.config` exists and contains `<clear />` |
| `NU1605` package downgrade | `Microsoft.Extensions.*` version lower than EF Core's requirement | Align all `Microsoft.Extensions.*` packages to the EF Core version |
| `dotnet ef` reports "Unable to create a DbContext" | Design-time factory not found or connection string invalid | Check `LifeOsDbContextFactory` and `LIFEOS_CONNECTION` |
| Migration applies but tables are missing | Wrong database targeted | Re-run with an explicit `LIFEOS_CONNECTION` |
| `PlatformNotSupportedException` on startup | Running on non-Windows without an `ISecretProtector` | Provide an alternative implementation in `AddInfrastructure` |
| Attachments fail to save | `LifeOS:StorageRoot` not writable | Point it at a writable folder |
