# Agent Instructions

## File-Based Apps

Do NOT use `dotnet-script`, `dotnet script`, `dotnet-fsi`, or `.csx` files. Always use file-based apps (new in .NET 10). Always prefer file-based apps over Python, unless a specific Python library is needed.

Run with `dotnet run /tmp/check.cs`. Write throwaway scripts to `/tmp/`.

Reference: <https://raw.githubusercontent.com/dotnet/docs/refs/heads/main/docs/core/sdk/file-based-apps.md>

### File-based app with project reference

```csharp
#:project ../src/MyLib/MyLib.csproj

using MyLib.Domain;

var items = await MyService.LoadAsync();
Console.WriteLine($"Found {items.Count} items");
```

## Building and Testing

Build the main project:

```bash
dotnet build src/dotnet-inspect -c Release
```

**IMPORTANT: Tests use xunit v3 with `OutputType Exe`. You MUST use `dotnet run`, NOT `dotnet test`. Using `dotnet test` will silently produce no output.**

```bash
dotnet run --project src/dotnet-inspect.Tests -c Release
dotnet run --project src/DotnetInspector.Decompiler.Tests -c Release
dotnet run --project src/DotnetInspector.Services.Tests -c Release
dotnet run --project tests/DotnetInspector.Metadata.Tests -c Release
```

Some tests in `dotnet-inspect.Tests` require `ilasm`/`ildasm` and will skip if not installed.

## Git Commits

Never amend commits. Always create new commits instead of using `git commit --amend`.

## Branching

The `main` branch is protected. All work must be done on a feature branch.

Create feature branches with descriptive names, e.g.:
- `feature/issue-3-assembly-references`
- `fix/null-reference-in-parser`

## Markdown Linting

All markdown files must pass `markdownlint` before committing. When there are lint errors, run the auto-fixer first:

```bash
npx markdownlint-cli --fix <file>
npx markdownlint-cli <file>
```

Run `markdownlint` on all changed markdown files as part of preparing a PR.
