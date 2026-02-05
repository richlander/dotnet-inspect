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

## Development Workflow

### Branch Strategy

- **`main`**: Protected branch containing release-ready code
- **Feature branches**: All development happens in feature branches
- **Pull Requests**: Required for all changes to `main`

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

3. Push and create a Pull Request:
   ```bash
   git push -u origin feature/my-change
   ```

4. Wait for CI to pass all checks before merging.

## Continuous Integration

### PR Checks

When a Pull Request is opened or updated, GitHub Actions runs:

1. **Build**: Compile all RID-specific variants
2. **Unit Tests**: Run xUnit tests
3. **Smoke Tests**: Install the tool from the built package and verify core commands work

All checks must pass before the PR can be merged.

### Build Matrix

| Runner | RID | Build Type |
|--------|-----|------------|
| `windows-latest` | win-x64 | Native AOT |
| `windows-latest` | win-arm64 | Native AOT (cross-compile) |
| `ubuntu-latest` | linux-x64 | Native AOT |
| `ubuntu-24.04-arm` | linux-arm64 | Native AOT |
| `macos-latest` | osx-arm64 | Native AOT |
| `ubuntu-latest` | any | CoreCLR |

### Smoke Tests

Each platform runs smoke tests after building:

```bash
# Install from local package
dotnet tool install --global dotnet-inspect --add-source ./artifacts/packages

# Verify core functionality
dotnet-inspect --version
dotnet-inspect --help
dotnet-inspect System.Text.Json
dotnet-inspect api JsonSerializer --package System.Text.Json@10.0.0
dotnet-inspect platform
```

The tool is uninstalled after testing to keep the runner clean.

## Release Process

### Triggering a Release

Releases are triggered by pushing a version tag:

```bash
# Ensure you're on main with latest changes
git checkout main
git pull origin main

# Create and push a version tag
git tag v0.2.0
git push origin v0.2.0
```

### Release Pipeline

When a version tag is pushed:

1. **Build all packages** on their respective platforms
2. **Run smoke tests** on each platform
3. **Upload packages** as workflow artifacts
4. **Publish to NuGet** (if all tests pass):
   - Publish RID-specific packages first (in parallel)
   - Wait for all RID packages to be available
   - Publish the pointer package last
5. **Create GitHub Release** with the packages attached

### Publishing Order

The publishing order is critical:

```
┌─────────────────────────────────────────────────────────┐
│ 1. Publish RID-specific packages (parallel)             │
│    - dotnet-inspect.win-x64.x.y.z.nupkg                │
│    - dotnet-inspect.win-arm64.x.y.z.nupkg              │
│    - dotnet-inspect.linux-x64.x.y.z.nupkg              │
│    - dotnet-inspect.linux-arm64.x.y.z.nupkg            │
│    - dotnet-inspect.osx-arm64.x.y.z.nupkg              │
│    - dotnet-inspect.any.x.y.z.nupkg                    │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│ 2. Wait for packages to be indexed                      │
└─────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│ 3. Publish pointer package                              │
│    - dotnet-inspect.x.y.z.nupkg                        │
└─────────────────────────────────────────────────────────┘
```

The pointer package must be published last because it references the RID-specific packages. If published first, users would get installation errors.

## Version Management

### Version Number

The version is defined in `src/dotnet-inspect/dotnet-inspect.csproj`:

```xml
<VersionPrefix>0.2.0</VersionPrefix>
```

### Updating the Version

1. Update `VersionPrefix` in the csproj file
2. Commit the change: `git commit -am "Bump version to 0.2.0"`
3. Create a PR and merge to main
4. Tag the release: `git tag v0.2.0 && git push origin v0.2.0`

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
dotnet tool install --global dotnet-inspect --add-source ./artifacts/packages/Release

# Test it
dotnet-inspect --version

# Uninstall when done
dotnet tool uninstall --global dotnet-inspect
```

## Troubleshooting

### Build Failures

- **Native AOT errors**: Ensure you're building on a platform that matches the target OS
- **Missing dependencies**: Run `dotnet restore` first

### Test Failures

- **Baseline mismatch**: Review the diff; if changes are intentional, run `./scripts/baseline-test.sh --update`
- **Smoke test failures**: Check that the package was built and the tool installed correctly

### Publishing Failures

- **NuGet API key issues**: Ensure the `NUGET_API_KEY` secret is set in GitHub repository settings
- **Package already exists**: Version numbers cannot be reused; bump the version
