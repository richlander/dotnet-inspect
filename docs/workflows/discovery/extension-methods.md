---
id: extension-methods
description: Discover extension methods available for a type across platform and packages
commands: [extensions]
areas: [extensions, discovery, reachable]
---

# Extension Methods

> Find extension methods available for a type. The `extensions` command scans platform frameworks and curated packages by default. Use `--reachable` to also discover extensions on types reachable via properties and methods — essential for understanding the full API surface available from a given starting point.

## Preconditions

Use an isolated session. The default search includes a curated package set, so
the first command primes that package metadata cache and requires network.

```bash
export DOTNET_INSPECT_ISOLATED=extension-methods
dotnet-inspect extensions HttpClient -v:q > /dev/null
```

## 1. Find extensions for a type

> Goal: See what extension methods are available for a well-known type.

### 1a. Quiet summary

```prompt
What extension methods are available for HttpClient?
```

```bash
dotnet-inspect extensions HttpClient -v:q
```

```expect
# Extension Methods for HttpClient
| HttpClient |
```

```expect-not
Tips:
```

### 1b. Default verbosity (full table)

```bash
dotnet-inspect extensions HttpClient -n 10
```

```expect
# Extension Methods for HttpClient
## Extensions
| Name | Overloads | Kind | Class | Library | Source |
HttpClientJsonExtensions
```

```expect-not
Tips:
```

## 2. Reachable type extensions

> Goal: Discover extensions not just on the target type but on types reachable through its properties and methods. This answers "what can I do starting from HttpClient?"

### 2a. Quiet summary with reachable

```prompt
What extension methods are available on HttpClient and the types it exposes?
```

```bash
dotnet-inspect extensions HttpClient --reachable -v:q
```

```expect
# Extension Methods for HttpClient
| HttpClient |
System.IO.Stream
System.Net.Http.HttpContent
```

### 2b. Full reachable output

```bash
dotnet-inspect extensions HttpClient --reachable -n 20
```

```expect
## Extensions
| Name | Overloads | Kind | Class | Library | Source | Type | Via |
```

```query
grep -c '| method |'
```

## 3. Scope to platform only

> Goal: Restrict the search to platform framework assemblies.

```bash
dotnet-inspect extensions HttpClient --platform -v:q
```

```expect
# Extension Methods for HttpClient
| HttpClient |
```

## 4. Type with many extensions

> Goal: Types like IServiceCollection have a large number of extensions across frameworks.

### 4a. Quiet summary

```prompt
How many extension methods does IServiceCollection have?
```

```bash
dotnet-inspect extensions IServiceCollection -v:q
```

```expect
# Extension Methods for IServiceCollection
## Summary
| Type | Extensions | Via |
```

```query
grep -oE 'IServiceCollection \| [0-9]+'
```

### 4b. Line-limited output

```bash
dotnet-inspect extensions IServiceCollection -n 10
```

```expect
# Extension Methods for IServiceCollection
## Extensions
| Name | Overloads | Kind | Class | Library | Source |
```

## 5. Type with no extensions

> Goal: Graceful output when no extension methods are found for a type in the given scope.

```bash
dotnet-inspect extensions Version --platform -v:q
```

```expect
# Extension Methods for Version
No extension methods found.
```

## 6. Platform-scoped extensions

> Goal: The `--platform` flag searches all platform frameworks (runtime, aspnetcore, netstandard).

```bash
dotnet-inspect extensions IServiceCollection --platform -v:q
```

```expect
# Extension Methods for IServiceCollection
## Summary
| Type | Extensions | Via |
```

```query
grep -oE 'IServiceCollection \| [0-9]+'
```
