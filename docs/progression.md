# Command progression

`dotnet-inspect` arguments are intended to progress or transform from one command to another in a coherent and inuitive way.

These examples will target System.Text.Json since it is both a platform and a package type. That will enable the examples to focus on the commands more than the target and there is also functionality for types in both camps.

The tool can be run multiple ways, depending on how it is installed:

- `dotnet inspect`
- `dotnet-inspect`
- `dnx dotnet-inspect -y --`
- `dotnet run --project src/dotnet-inspect --`

These are all equivalent. The first two assume the tool was installed via `dotnet tool install -g`, the third via `dnx`, and last is a build-from-source scenario. THe examples use the `dotnet inspect`, but can be trivially and correctly be transformed to the other pattern. Note that `--` needs to be used for the last two examples as some flags are the same as `dnx` and `dotnet` and will be interpreted/swallowed by those tools.

The tool differientates output by verbosity. This document will stick to the default verbosity.

## Package inspection

```bash
$ dotnet inspect System.Text.Json
# System.Text.Json (10.0.2)

Provides high-performance and low-allocating types that serialize objects to JavaScript Object Notation (JSON) text and deserialize JSON text to objects, with UTF-8 support built-in. Also provides types to read and write JSON text encoded as UTF-8, and to create an in-memory document object model (DOM), that is read-only, for random access of the JSON elements within a structured view of the data.

The System.Text.Json library is built-in as part of the shared framework in .NET Runtime. The package can be installed when you need to use it in other target frameworks.

Type: Library | TFM: net10.0 | Updated: 2026-01-13
```

The default command is `package`. Specifying `package` explicitly results in the same output.

```bash
$ dotnet inspect package System.Text.Json
# System.Text.Json (10.0.2)

Provides high-performance and low-allocating types that serialize objects to JavaScript Object Notation (JSON) text and deserialize JSON text to objects, with UTF-8 support built-in. Also provides types to read and write JSON text encoded as UTF-8, and to create an in-memory document object model (DOM), that is read-only, for random access of the JSON elements within a structured view of the data.

The System.Text.Json library is built-in as part of the shared framework in .NET Runtime. The package can be installed when you need to use it in other target frameworks.

Type: Library | TFM: net10.0 | Updated: 2026-01-13
```

## Platform inspect
