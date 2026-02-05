# Release Workflow

This document describes the build, test, and release workflow for dotnet-inspect.

## Overview

dotnet-inspect is distributed as a .NET tool with RID-specific Native AOT packages for optimal performance on common platforms, plus a CoreCLR fallback for all other platforms.

### Package Structure

| Package | Platform | Type |
|---------|----------|------|
| `dotnet-inspect` | - | Pointer package (references all variants) |
| `dotnet-inspect.win-x64` | Windows x64 | Native AOT, self-contained |
| `dotnet-inspect.win-arm64` | Windows ARM64 | Native AOT, self-contained |
| `dotnet-inspect.linux-x64` | Linux x64 | Native AOT, self-contained |
| `dotnet-inspect.linux-arm64` | Linux ARM64 | Native AOT, self-contained |
| `dotnet-inspect.osx-arm64` | macOS ARM64 | Native AOT, self-contained |
| `dotnet-inspect.any` | Any | CoreCLR, framework-dependent |

When users install the tool, the .NET CLI automatically selects the best package for their platform.

## Branch Strategy

```
feature/* ──PR──▶ main ──merge──▶ release
                   │                  │
                   ▼                  ▼
              Build & Test      Build, Test, Publish
```

| Branch | Purpose | CI Actions |
|--------|---------|------------|
| `feature/*` | Development work | - |
| `main` | Integration branch, always releasable | Build, test, smoke test |
| `release` | Triggers NuGet publishing | Build, test, smoke test, publish, GitHub release |

### Artifacts vs Releases

| Source | Workflow Artifacts | NuGet Packages | GitHub Release |
|--------|-------------------|----------------|----------------|
| Pull Request | ✓ Downloadable | ✗ | ✗ |
| Push to `main` | ✓ Downloadable | ✗ | ✗ |
| Push to `release` | ✓ Downloadable | ✓ Published | ✓ Created |

**Workflow Artifacts**: Every successful build uploads packages as artifacts. These can be downloaded from the Actions workflow run to test pre-release builds without publishing to NuGet.

**GitHub Releases**: Only created when changes are pushed to `release`. Includes a version tag (`v0.3.0`) and all packages attached for download.

### Why This Model?

- **Intentional releases**: Publishing only happens when you explicitly merge to `release`, not on every PR
- **Safe main branch**: All PRs are tested before merge; `main` should always be in a releasable state
- **Simple workflow**: No tags to manage, no version bumping automation needed
- **Review opportunity**: The merge to `release` is a deliberate action that can be reviewed

## Development Workflow

### Making Changes

1. Create a feature branch from `main`:
   ```bash
   git checkout main
   git pull origin main
   git checkout -b feature/my-change
   ```

2. Make your changes and commit:
   ```bash
   git add .
   git commit -m "Description of changes"
   ```

3. Push and create a Pull Request to `main`:
   ```bash
   git push -u origin feature/my-change
   ```

4. Wait for CI to pass all checks, then merge the PR.

### CI Checks on Pull Requests

When a PR is opened against `main`, GitHub Actions runs:

1. **Build**: Compile the pointer package and all RID-specific variants
2. **Unit Tests**: Run xUnit tests
3. **Smoke Tests**: Install the tool from built packages on each platform and verify core commands work

All checks must pass before the PR can be merged.

## Release Process

### Preparing a Release

1. Ensure `main` has all the changes you want to release
2. Update the version in `src/dotnet-inspect/dotnet-inspect.csproj`:
   ```xml
   <VersionPrefix>0.3.0</VersionPrefix>
   ```
3. Commit and push to `main` (via PR or direct if you have access)

### Publishing a Release

Merge `main` into `release`:

```bash
git checkout release
git pull origin release
git merge main
git push origin release
```

Or create a PR from `main` to `release` for additional review.

### What Happens on Release

When changes are pushed to the `release` branch:

1. **Build** all packages on their respective platforms
2. **Run smoke tests** on each platform
3. **Publish to NuGet**:
   - RID-specific packages first (win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64, any)
   - Pointer package last (must be published after RID packages are available)
4. **Create GitHub Release** with version tag and attached packages

### Publishing Order

The pointer package references RID-specific packages, so the order matters:

```
1. dotnet-inspect.win-x64.0.3.0.nupkg
2. dotnet-inspect.win-arm64.0.3.0.nupkg
3. dotnet-inspect.linux-x64.0.3.0.nupkg
4. dotnet-inspect.linux-arm64.0.3.0.nupkg
5. dotnet-inspect.osx-arm64.0.3.0.nupkg
6. dotnet-inspect.any.0.3.0.nupkg
7. dotnet-inspect.0.3.0.nupkg  ← pointer package, published last
```

## Build Matrix

| Runner | RID | Build Type |
|--------|-----|------------|
| `windows-latest` | win-x64 | Native AOT |
| `windows-latest` | win-arm64 | Native AOT (cross-compile) |
| `ubuntu-latest` | linux-x64 | Native AOT |
| `ubuntu-24.04-arm` | linux-arm64 | Native AOT |
| `macos-latest` | osx-arm64 | Native AOT |
| `ubuntu-latest` | any | CoreCLR |

## Version Management

The version is defined in `src/dotnet-inspect/dotnet-inspect.csproj`:

```xml
<VersionPrefix>0.3.0</VersionPrefix>
```

Update this before merging to `release`. The GitHub Release will automatically be tagged with `v{version}`.

## Local Development

### Building Locally

```bash
# Build all projects
dotnet build src/dotnet-inspect

# Build with Native AOT for your platform
dotnet publish src/dotnet-inspect -c Release -r linux-x64 --self-contained -p:PublishAot=true
```

### Running Tests Locally

```bash
# Unit tests
dotnet test src/dotnet-inspect.Tests

# Baseline regression tests
./scripts/baseline-test.sh

# Update baseline after intentional changes
./scripts/baseline-test.sh --update

# Smoke tests (requires tool to be installed)
./scripts/smoke-test.sh
```

### Creating Local Packages

```bash
# Create pointer package
dotnet pack src/dotnet-inspect -c Release

# Create RID-specific AOT package (on matching OS)
dotnet pack src/dotnet-inspect -c Release -r linux-x64

# Create CoreCLR fallback package
dotnet pack src/dotnet-inspect -c Release -r any -p:PublishAot=false
```

### Testing a Local Package

```bash
# Install from local artifacts
dotnet tool install --global dotnet-inspect --add-source ./artifacts/package/release --no-cache

# Test it
dotnet-inspect --version

# Uninstall when done
dotnet tool uninstall --global dotnet-inspect
```

## Repository Setup

### Required Secrets

In GitHub repository settings, configure:

- **`NUGET_API_KEY`**: API key for publishing to nuget.org

### Environment Protection (Optional)

You can add protection rules to the `nuget-publish` environment:

1. Go to Settings → Environments → nuget-publish
2. Add required reviewers for manual approval before publishing
3. Add deployment branch rules to restrict to `release` branch

## Troubleshooting

### Build Failures

- **Native AOT errors**: Ensure you're building on a platform that matches the target OS
- **Missing dependencies**: Run `dotnet restore` first

### Test Failures

- **Baseline mismatch**: Review the diff; if changes are intentional, run `./scripts/baseline-test.sh --update`
- **Smoke test failures**: Check that the package was built and the tool installed correctly

### Publishing Failures

- **NuGet API key issues**: Ensure `NUGET_API_KEY` secret is set in GitHub repository settings
- **Package already exists**: Version numbers cannot be reused; bump the version
- **Pointer package fails**: RID-specific packages may not be indexed yet; the workflow publishes them first to avoid this
