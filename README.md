# Cove

A Windows utility in the spirit of PowerToys. A small pill at the top of the screen gives quick access to file tools (convert, compress, copy text, OneDrive upload), now playing, per-app volume, a mic indicator, timers and push-to-talk voice commands. Everything runs locally.

## Build and test

You need the .NET 10 SDK.

```powershell
dotnet restore Cove.sln
dotnet build Cove.sln -c Debug
dotnet test Cove.sln
dotnet run --project src/Cove.App
```

Code style is defined in [.editorconfig](.editorconfig). Check it with `dotnet format Cove.sln --verify-no-changes`.

## Branch naming

Never work directly on `main`. Create a branch per card:

- `feature/<area>-<thing>` for new work, e.g. `feature/files-compress`
- `fix/<area>-<thing>` for bug fixes, e.g. `fix/pill-focus-restore`

You can put the card ID in place of the area, e.g. `feature/K-04-code-style`.

Commit messages start with the card ID in brackets, e.g. `[KAN-6 / K-04] Add .editorconfig`.

## Pull requests

Open a PR into `main` and fill in the template. Ask the owner of each area you touched for a review. Team decisions are logged in [docs/decisions.md](docs/decisions.md).
