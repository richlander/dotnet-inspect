---
id: project-context-search
description: Search, inspect, and map APIs in restored project dependencies and build output
commands: [find, type, member, implements, extensions, depends]
areas: [find, type, member, relationships, project, bin, dependencies]
---

# Project Context

> Search or inspect APIs within the scope of a project's restored dependencies
> (`--project`) or build output (`--bin`). `--project` means an existing
> `project.assets.json` restored-assets context; passing a `.csproj` or project
> directory only locates that file. dotnet-inspect does not restore, build, or
> evaluate MSBuild for this mode. If the assets file is missing, source
> resolution commands (`type`/`member`) report an error; relationship search
> commands warn and continue, matching their other optional scopes.

Use `--project` when you want package/framework APIs that are available to a
project. Use `--bin` when you want DLLs copied into a build output directory,
including project output assemblies.

## Preconditions

Create a deterministic `net10.0` project with pinned dependencies. The local
MSBuild files keep this standalone fixture independent of repository-wide build
and central-package settings.

```bash
export PROJECT_WORKFLOW="$PWD/artifacts/workflows/find-project"
rm -rf "$PROJECT_WORKFLOW"
mkdir -p "$PROJECT_WORKFLOW/FindDemo"

cat > "$PROJECT_WORKFLOW/Directory.Build.props" <<'EOF'
<Project />
EOF
cat > "$PROJECT_WORKFLOW/Directory.Build.targets" <<'EOF'
<Project />
EOF
cat > "$PROJECT_WORKFLOW/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
EOF
cat > "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0" />
    <PackageReference Include="System.CommandLine" Version="2.0.3" />
    <PackageReference Include="Markout" Version="0.33.0" />
    <PackageReference Include="System.Security.Cryptography.Pkcs" Version="10.0.0" />
  </ItemGroup>
</Project>
EOF
cat > "$PROJECT_WORKFLOW/FindDemo/Program.cs" <<'EOF'
Console.WriteLine("workflow fixture");
EOF

dotnet build "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" \
  -c Release --nologo --verbosity quiet
```

## 1. Search project dependencies

> Goal: Find types across all packages referenced by a project, using the `.csproj` file.

### 1a. Find types by pattern

```prompt
What Command types are in my project's dependencies?
```

```bash
dotnet-inspect find 'Command*' \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" -v:q
```

```expect
# Find: Command*
Command
System.CommandLine
```

### 1b. Find interfaces

```prompt
What logger interfaces do my dependencies expose?
```

```bash
dotnet-inspect find 'ILogger*' \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" -v:q
```

```expect
# Find: ILogger*
ILogger
ILoggerFactory
ILoggerProvider
Microsoft.Extensions.Logging
```

## 2. Inspect APIs from project assets

> Goal: Resolve a type/member from the restored dependency graph the project
> actually uses.

### 2a. Inspect a type

```bash
dotnet-inspect type Command \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" --markdown -v:q
```

```expect
# System.CommandLine.Command
Package: System.CommandLine
Version: 2.0.3
Kind: class
Source: Project
```

### 2b. Inspect members

```bash
dotnet-inspect member Command \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" \
  -S "Member Index" -n 12
```

```expect
Member Index
```

### 2c. Missing assets are an error

Create an existing project without restoring it:

```setup
rm -rf "$PROJECT_WORKFLOW/Missing"
dotnet new console --no-restore -n Missing \
  -o "$PROJECT_WORKFLOW/Missing" > /dev/null
```

```bash
dotnet-inspect type Command \
  --project "$PROJECT_WORKFLOW/Missing/Missing.csproj"
```

```expect-error
project.assets.json not found
Run 'dotnet restore'.
```

## 3. Map relationships in project dependencies

> Goal: Search package-bound relationship shapes without naming each package
> directly.

### 3a. Find types by implemented interface

```bash
dotnet-inspect implements IEquatable \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" -v:q
```

```expect
Markout
```

### 3b. Find extension methods from referenced packages

```bash
dotnet-inspect extensions string \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" -v:n
```

```expect
UnicodeToOctetString
System.Security.Cryptography.Pkcs
```

### 3c. Walk dependencies from a project-resolved type

```bash
dotnet-inspect depends Command \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" -v:q
```

```expect
System.CommandLine.Command
```

## 4. Search build output

> Goal: Find types in the compiled output directory — includes the project's own types and all copied dependencies.

### 4a. Find types by pattern

```bash
BIN=$(find "$PROJECT_WORKFLOW/FindDemo/bin/Release" -name FindDemo.dll \
  -exec dirname {} \; | head -1)
test -n "$BIN"
dotnet-inspect find 'Command*' --bin "$BIN" -v:q
```

```expect
# Find: Command*
Command
System.CommandLine
```

### 4b. Find across all dependencies

```bash
BIN=$(find "$PROJECT_WORKFLOW/FindDemo/bin/Release" -name FindDemo.dll \
  -exec dirname {} \; | head -1)
test -n "$BIN"
dotnet-inspect find '*Logger*' --bin "$BIN" -v:q
```

```expect
# Find: *Logger*
ILogger
LoggerFactory
Microsoft.Extensions.Logging
```

## 5. Compare project vs bin results

> Goal: Both `--project` and `--bin` search the same dependency set, but `--project` shows source attribution (package@version) while `--bin` shows local file context.

### 5a. Project shows source

```bash
dotnet-inspect find 'Command' \
  --project "$PROJECT_WORKFLOW/FindDemo/FindDemo.csproj" -v:q
```

```expect
System.CommandLine@2.0.3
```

### 5b. Bin shows local context

```bash
BIN=$(find "$PROJECT_WORKFLOW/FindDemo/bin/Release" -name FindDemo.dll \
  -exec dirname {} \; | head -1)
test -n "$BIN"
dotnet-inspect find 'Command' --bin "$BIN" -v:q
```

```expect
Command
System.CommandLine
```
