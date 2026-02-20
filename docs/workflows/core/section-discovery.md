# Section Discovery

> The `-s` flag (with no argument) lists available sections for any asset. The sections vary by source — platform assemblies expose different sections than NuGet packages. Section discovery must work consistently across all asset resolution paths: bare name, `--platform`, `--package`, `library`, and `package`.

Ref: [PR #107](https://github.com/richlander/dotnet-inspect/pull/107) — fixed `-s` discovery for the `--platform` path.

## Preconditions

Isolated session with cached packages. Offline mode ensures no unexpected network dependencies.

```bash
export DOTNET_INSPECT_ISOLATED=section-discovery
export DOTNET_INSPECT_OFFLINE=1
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
DOTNET_INSPECT_OFFLINE=0 dotnet-inspect System.CommandLine -v:q
```

## Discover sections for a platform library

> Goal: List sections for a platform assembly via both bare name and explicit `--platform`.

### Using bare name

```bash
dotnet-inspect System.Collections -s
```

```expect
Library Info
Custom Attributes
Type Forwarders
```

### Using `library --platform`

```bash
dotnet-inspect library --platform System.Collections -s
```

```expect
Library Info
Custom Attributes
Type Forwarders
```

## Discover sections for a NuGet package

> Goal: List sections for a NuGet package via both bare name and explicit `package`.

### Using bare name

```bash
dotnet-inspect System.CommandLine -s
```

```expect
Package
Package Dependencies
```

### Using `package`

```bash
dotnet-inspect package System.CommandLine -s
```

```expect
Package
Package Dependencies
```

## Platform and package sections differ for the same name

> Goal: A library that exists in both platform and NuGet exposes different sections depending on the source.

### Platform path

```bash
dotnet-inspect library --platform System.Collections -s
```

```expect
Library Info
Resources
Type Forwarders
```

### Package path

```bash
dotnet-inspect library --package System.Collections -s
```

```expect
Library Info
Custom Attributes
```

```expect-not
Resources
Type Forwarders
```

## Select a specific section

> Goal: `-s [name]` renders only that section's content, not the full output.

### Platform section

```bash
dotnet-inspect library --platform System.Collections -s "Custom Attributes"
```

```expect
## Custom Attributes
| Name | Target | Value |
```

```expect-not
## Library Info
## Type Forwarders
```

### Package section

```bash
dotnet-inspect library --package System.Collections -s "Library Info"
```

```expect
## Library Info
| Property | Value |
```

```expect-not
## Custom Attributes
```
