using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryCoordinateCommandDefinitions
{
    internal static Command Create(
        SharedOptions opts,
        Option<string?> parentIlOffsetOption,
        Option<string?> parentIlOffsetsOption,
        Option<string?> parentHeapOption)
    {
        var command = new Command(
            "coordinate",
            "Inspect one exact coordinate within a selected .NET Library");
        var coordinateArgument = new Argument<string?>("coordinate")
        {
            Description =
                "MethodDef token plus IL offset (for example, 0x06000001+0x5)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        coordinateArgument.DefaultValueFactory = _ => null;
        coordinateArgument.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;

            string value = result.Tokens[^1].Value;
            if (!ILOffsetQuery.TryParse(value, out _, out _))
            {
                result.AddError(
                    $"Invalid coordinate '{value}'. Expected a MethodDef token "
                    + "plus IL offset, for example 0x06000001+0x5.");
            }
        });

        var libraryOption = new Option<string?>("--library")
        {
            Description =
                "Local Library path, or package-relative Library path with --package",
        };
        var packageOption = new Option<string?>("--package")
        {
            Description =
                "NuGet package name, package@version, or local package path",
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description = "Platform Library name (for example, System.Private.CoreLib)",
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description =
                "When resolving an unversioned package, include prerelease versions",
        };
        prereleaseOption.Aliases.Add("--prerelease");
        var frameworkOption = new Option<string?>("--framework")
        {
            Description =
                "Optional Platform framework family (runtime, aspnetcore)",
        };
        var versionOption = new Option<string?>("--version")
        {
            Description =
                "Platform runtime version (searches framework families in priority order)",
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description = "Select a package Library by TFM (for example, net8.0)",
        };

        command.Arguments.Add(coordinateArgument);
        command.Options.Add(libraryOption);
        command.Options.Add(packageOption);
        command.Options.Add(platformOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(frameworkOption);
        command.Options.Add(versionOption);
        command.Options.Add(tfmOption);
        command.Options.Add(opts.RawUrls);
        command.Options.Add(opts.BrowsableUrls);
        command.Options.Add(opts.Trace);
        command.Options.Add(opts.Effective);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        command.Options.Add(opts.Discover);
        command.Options.Add(opts.Select);
        command.Options.Add(opts.Columns);
        command.Options.Add(opts.Fields);
        command.Options.Add(opts.Schema);
        command.Options.Add(opts.Tree);
        opts.AddCountOptionTo(command);
        opts.AddPrintOptionTo(command);
        opts.AddShapeProjectionOptionsTo(command);
        opts.AddNuGetOptionsTo(command);

        command.Validators.Add(result =>
        {
            foreach (Option option in new Option[]
            {
                parentIlOffsetOption,
                parentIlOffsetsOption,
                parentHeapOption,
            })
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    result.AddError(
                        $"{option.Name} cannot be combined with library coordinate.");
                }
            }
        });

        command.SetAction(async (parseResult, _) =>
        {
            string? coordinate =
                parseResult.GetValue(coordinateArgument);
            if (string.IsNullOrWhiteSpace(coordinate))
            {
                CommandError.Write(
                    "library coordinate requires one exact coordinate.");
                return 1;
            }

            string? library = parseResult.GetValue(libraryOption);
            string? package = parseResult.GetValue(packageOption);
            string? platform = parseResult.GetValue(platformOption);
            string? framework = parseResult.GetValue(frameworkOption);
            string? version = parseResult.GetValue(versionOption);
            string? tfm = parseResult.GetValue(tfmOption);
            bool includePrerelease = parseResult.GetValue(prereleaseOption);

            if (!TryValidateSource(
                    opts,
                    parseResult,
                    library,
                    package,
                    platform,
                    framework,
                    version,
                    tfm,
                    includePrerelease,
                    out string? sourceError))
            {
                CommandError.Write(sourceError!);
                return 1;
            }

            string[]? select = opts.ParseSelect(parseResult);
            bool selectDefault = opts.ParseSelectDefault(parseResult);
            bool hasExplicitSelect =
                select is { Length: > 0 } || selectDefault;
            OutputFormat format = opts.ResolveFormat(parseResult);

            return await LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                AssemblyName = library,
                IncludeMetadata = true,
                PackagePath = package,
                IncludePrerelease = includePrerelease,
                PlatformAssembly = platform,
                PlatformFramework = framework,
                PlatformVersion = version,
                Tfm = tfm,
                ILOffsetParameter = coordinate,
                IsCoordinateCommand = true,
                BrowsableUrls =
                    parseResult.GetValue(opts.BrowsableUrls)
                    && !parseResult.GetValue(opts.RawUrls),
                JsonOutput = format == OutputFormat.Json,
                Markdown = parseResult.GetValue(opts.Markdown),
                PlainText = parseResult.GetValue(opts.PlainText),
                Tabular =
                    format is OutputFormat.Table
                        or OutputFormat.Tsv
                        or OutputFormat.Jsonl,
                Tsv = format == OutputFormat.Tsv,
                Jsonl = format == OutputFormat.Jsonl,
                TabularExplicitlySet =
                    opts.IsTableExplicitlySet(parseResult),
                FormatExplicitlySet =
                    opts.IsFormatExplicitlySet(parseResult),
                Format = format,
                Verbose = parseResult.GetValue(opts.Verbose),
                Trace = parseResult.GetValue(opts.Trace),
                Verbosity = opts.ParseVerbosity(parseResult),
                Discover = opts.ParseDiscover(parseResult),
                Effective = parseResult.GetValue(opts.Effective),
                Tree = parseResult.GetValue(opts.Tree),
                Select = select,
                SelectDefault = selectDefault,
                SelectExplicitlySet = hasExplicitSelect,
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                FieldsExplicitlySet =
                    parseResult.GetResult(opts.Fields)
                        is { Implicit: false },
                Count = parseResult.GetValue(opts.Count),
                Print = parseResult.GetValue(opts.Print),
                Value = parseResult.GetValue(opts.Value),
                Urls = parseResult.GetValue(opts.Urls),
                Paths = parseResult.GetValue(opts.Paths),
                JsonArray = parseResult.GetValue(opts.JsonArray),
                PrintRow = opts.ParsePrintRow(parseResult),
                ProjectionRow = opts.ParsePrintRow(parseResult),
                Rows = opts.ParseRows(parseResult),
                Schema = opts.ParseSchema(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                SourceOptions =
                    opts.ParseNuGetSourceOptions(parseResult),
            });
        });

        return command;
    }

    private static bool TryValidateSource(
        SharedOptions opts,
        ParseResult parseResult,
        string? library,
        string? package,
        string? platform,
        string? framework,
        string? version,
        string? tfm,
        bool includePrerelease,
        out string? error)
    {
        error = null;
        bool hasLibrary = !string.IsNullOrWhiteSpace(library);
        bool hasPackage = !string.IsNullOrWhiteSpace(package);
        bool hasPlatform = !string.IsNullOrWhiteSpace(platform);

        if (hasPackage && hasPlatform)
        {
            error = "--package cannot be combined with --platform.";
            return false;
        }

        if (hasLibrary && hasPlatform)
        {
            error =
                "--library cannot be combined with --platform; "
                + "--platform already identifies the selected Library.";
            return false;
        }

        if (!hasPackage
            && (!string.IsNullOrWhiteSpace(tfm) || includePrerelease))
        {
            error = "--tfm and --preview require --package.";
            return false;
        }

        if (!hasPlatform
            && (!string.IsNullOrWhiteSpace(framework)
                || !string.IsNullOrWhiteSpace(version)))
        {
            error = "--framework and --version require --platform.";
            return false;
        }

        if (string.Equals(tfm, "all", StringComparison.OrdinalIgnoreCase))
        {
            error =
                "library coordinate requires one selected Library; "
                + "--tfm all selects multiple Libraries.";
            return false;
        }

        bool structuralDiscovery =
            opts.IsDiscoveryMode(parseResult)
            && opts.ParseSchema(parseResult);
        if (!hasLibrary && !hasPackage && !hasPlatform
            && !structuralDiscovery)
        {
            error =
                "library coordinate requires --library, --package, "
                + "or --platform.";
            return false;
        }

        return true;
    }
}
