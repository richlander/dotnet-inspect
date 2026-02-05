# Release Workflow

This document describes the build, test, and release workflow for dotnet-inspect.

## Overview

dotnet-inspect is distributed as a .NET tool with RID-specific Native AOT packages for optimal performance on common platforms, plus a CoreCLR fallback for all other platforms.

### Package Structure

| Package | Platform | Type |
| ------- | -------- | ---- |
| `dotnet-inspect` | - | Pointer package (references all variants) |
| `dotnet-inspect.win-x64` | Windows x64 | Native AOT, self-contained |
| `dotnet-inspect.win-arm64` | Windows ARM64 | Native AOT, self-contained |
| `dotnet-inspect.linux-x64` | Linux x64 | Native AOT, self-contained |
| `dotnet-inspect.linux-arm64` | Linux ARM64 | Native AOT, self-contained |
| `dotnet-inspect.osx-arm64` | macOS ARM64 | Native AOT, self-contained |
| `dotnet-inspect.any` | Any | CoreCLR, framework-dependent |

When users install the tool, the .NET CLI automatically selects the best package for their platform.

## Workflow Structure

Two separate workflows handle CI and publishing:

| Workflow | Trigger | Purpose |
| -------- | ------- | ------- |
| `ci.yml` | Push to main, PRs | Build, test, upload artifacts |
| `release.yml` | Manual dispatch | Publish to NuGet from a CI run |

### Artifacts vs Releases

| Source | Workflow Artifacts | NuGet Packages | GitHub Release |
| ------ | ------------------ | -------------- | -------------- |
| Pull Request | ✓ Downloadable | ✗ | ✗ |
| Push to `main` | ✓ Downloadable | ✗ | ✗ |
| Manual publish | - | ✓ Published | ✓ Created |

**Workflow Artifacts**: Every successful CI build uploads packages as artifacts. Download from the Actions run to test pre-release builds.

**GitHub Releases**: Created when you manually trigger the publish workflow.

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

1. **Markdownlint**: Lint markdown files (if docs changed)
2. **Build**: Compile the pointer package and all RID-specific variants
3. **Unit Tests**: Run xUnit tests
4. **Smoke Tests**: Install the tool from built packages on each platform and verify core commands work

All checks must pass before the PR can be merged.

## Release Process

### Publishing a Release

1. Find the CI workflow run you want to publish (from Actions tab)
2. Copy the run ID from the URL (e.g., `21724718480`)
3. Go to Actions → Publish → Run workflow
4. Enter the run ID and type "publish" to confirm
5. The workflow publishes to NuGet and creates a GitHub Release

### What Happens on Publish

1. **Download packages** from the specified CI run
2. **Publish to NuGet**:
   - RID-specific packages first (win-x64, win-arm64, linux-x64, linux-arm64, osx-arm64, any)
   - Pointer package last (must be published after RID packages are available)
3. **Create GitHub Release** with version tag and attached packages

### Publishing Order

The pointer package references RID-specific packages, so the order matters:

```text
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
| ------ | --- | ---------- |
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

Update this before publishing a new release.

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
3. Limit to specific users who can trigger the publish workflow

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
