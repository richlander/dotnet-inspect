using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;
using DotnetInspector.MetadataRendering;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

internal static class LibraryCoordinateCommandDefinitions
{
    internal static Command Create(
        SharedOptions opts,
        Command parentCommand,
        Argument<string?> parentSourceArgument,
        Option<string?> metadataRootOption)
    {
        var command = new Command(
            "coordinate",
            "Inspect exact coordinates within a selected .NET Library");
        var coordinateArgument = new Argument<string?>("coordinate")
        {
            Description =
                "MethodDef token plus IL offset, or metadata heap plus address "
                + "(for example, 0x06000001+0x5 or #Strings:0x1a4)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        coordinateArgument.DefaultValueFactory = _ => null;
        coordinateArgument.Validators.Add(result =>
        {
            if (result.Tokens.Count == 0)
                return;

            string value = result.Tokens[^1].Value;
            if (!TryParseCoordinate(value, out _, out string? error))
                result.AddError(error!);
        });
        var fileOption = new Option<string?>("--file")
        {
            Description =
                "Text file of up to 1,024 sparse MethodDef token plus IL offset coordinates",
        };

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
        command.Options.Add(fileOption);
        command.Options.Add(metadataRootOption);
        command.Options.Add(opts.PreferRenderedUrls);
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
        CliRowSelectionCommandRegistry.Register(
            command,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            result =>
                !string.IsNullOrWhiteSpace(
                    result.GetValue(fileOption))
                && opts.ParseDiscover(result) is null,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        var acceptedParentOptions = new HashSet<Option>(command.Options);
        command.Validators.Add(result =>
        {
            if (result.GetResult(parentSourceArgument) is { Tokens.Count: > 0 })
            {
                result.AddError(
                    "A Library inspection source cannot precede library coordinate; "
                    + "place 'coordinate' immediately after 'library'.");
            }

            Option? unsupportedParentOption =
                parentCommand.Options.FirstOrDefault(
                    option => !acceptedParentOptions.Contains(option)
                        && result.GetResult(option) is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                result.AddError(
                    $"{unsupportedParentOption.Name} cannot be combined with "
                    + "library coordinate.");
            }
        });

        command.SetAction(async (parseResult, _) =>
        {
            string? coordinate =
                parseResult.GetValue(coordinateArgument);
            string? coordinateFile =
                parseResult.GetValue(fileOption);
            bool hasCoordinate = !string.IsNullOrWhiteSpace(coordinate);
            bool hasCoordinateFile = !string.IsNullOrWhiteSpace(coordinateFile);
            if (hasCoordinate == hasCoordinateFile)
            {
                CommandError.Write(
                    hasCoordinate
                        ? "library coordinate accepts either one exact coordinate "
                            + "or --file, not both."
                        : "library coordinate requires one exact coordinate or "
                            + "--file <path>.");
                return 1;
            }

            string[]? select = opts.ParseSelect(parseResult);
            bool selectDefault = opts.ParseSelectDefault(parseResult);
            bool hasExplicitSelect =
                select is { Length: > 0 } || selectDefault;
            if (hasCoordinateFile && hasExplicitSelect)
            {
                CommandError.Write(
                    "-S/--select is not available with library coordinate "
                    + "--file, which renders its own payload rather than "
                    + "sections.");
                return 1;
            }

            if (!CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "IL coordinate",
                        out RowSelectionIntent<string>? rowSelection,
                        out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string? library = parseResult.GetValue(libraryOption);
            string? package = parseResult.GetValue(packageOption);
            string? platform = parseResult.GetValue(platformOption);
            string? framework = parseResult.GetValue(frameworkOption);
            string? version = parseResult.GetValue(versionOption);
            string? tfm = parseResult.GetValue(tfmOption);
            bool includePrerelease = parseResult.GetValue(prereleaseOption);
            LibraryCoordinateRequest? coordinateRequest = null;
            if (hasCoordinate
                && !TryParseCoordinate(
                    coordinate!,
                    out coordinateRequest,
                    out string? coordinateError))
            {
                CommandError.Write(coordinateError!);
                return 1;
            }

            if (!InspectionCommandDefinitions.TryParseMetadataRoot(
                    parseResult.GetValue(metadataRootOption),
                    out MetadataRootKind metadataRoot,
                    out string? metadataRootError))
            {
                CommandError.Write(metadataRootError!);
                return 1;
            }

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

            bool structuralDiscovery =
                IsStructuralDiscovery(opts, parseResult);
            if (hasCoordinateFile && !structuralDiscovery)
            {
                ILCoordinatePopulationOutcome population =
                    ILOffsetQuery.ReadPopulation(coordinateFile!);
                if (!population.Succeeded)
                {
                    CommandError.Write(
                        ILOffsetQuery.PopulationFailureMessage(
                            population.Failure!));
                    return 1;
                }

                coordinateRequest =
                    new LibraryCoordinateRequest.FilePopulation(
                        coordinateFile!,
                        population.Population);
            }
            else if (hasCoordinateFile)
            {
                coordinateRequest =
                    new LibraryCoordinateRequest.FilePopulation(
                        coordinateFile!,
                        Population: null);
            }
            OutputFormat format = opts.ResolveFormat(parseResult);

            if (!LibrarySourceAdapter.TryDeclare(
                    library,
                    package,
                    platform,
                    "--library",
                    out var sourceIntent,
                    out string? sourceIntentError))
            {
                CommandError.Write(sourceIntentError!);
                return 1;
            }

            return await LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                SourceIntent = sourceIntent,
                AssemblyName = library,
                IncludeMetadata = true,
                IncludePrerelease = includePrerelease,
                PlatformFramework = framework,
                PlatformVersion = version,
                Tfm = tfm,
                CoordinateRequest = coordinateRequest,
                MetadataRoot = metadataRoot,
                PreferRenderedUrls =
                    parseResult.GetValue(opts.PreferRenderedUrls),
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
                FormatFlagExplicitlySet =
                    opts.IsFormatFlagExplicitlySet(parseResult),
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
                CoordinateRowSelection = rowSelection,
                Rows = rowSelection is null
                    ? opts.ParseRows(parseResult)
                    : null,
                Schema = opts.ParseSchema(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                SourceOptions =
                    opts.ParseNuGetSourceOptions(parseResult),
            });
        });

        return command;
    }

    private static bool TryParseCoordinate(
        string value,
        out LibraryCoordinateRequest? request,
        out string? error)
    {
        if (ILOffsetQuery.TryParse(
                value,
                out int methodToken,
                out int ilOffset))
        {
            request =
                new LibraryCoordinateRequest.IlPoint(
                    value,
                    methodToken,
                    ilOffset);
            error = null;
            return true;
        }

        if (MetadataHeapCoordinate.TryParse(
                value,
                out HeapKind heap,
                out int address,
                out string? heapError))
        {
            request =
                new LibraryCoordinateRequest.HeapPoint(
                    value,
                    heap,
                    address);
            error = null;
            return true;
        }

        request = null;
        if (LooksLikeHeapCoordinate(value))
        {
            error = $"Invalid coordinate '{value}': {heapError}";
            return false;
        }

        error =
            $"Invalid coordinate '{value}'. Expected a MethodDef token plus "
            + "IL offset (0x06000001+0x5) or a metadata heap plus address "
            + "(#Strings:0x1a4).";
        return false;
    }

    private static bool LooksLikeHeapCoordinate(string value)
    {
        string trimmed = value.Trim();
        return trimmed.StartsWith('#')
            || trimmed.Contains(':')
            || MetadataHeapCoordinate.TryParseHeap(trimmed, out _);
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
            IsStructuralDiscovery(opts, parseResult);
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

    private static bool IsStructuralDiscovery(
        SharedOptions options,
        ParseResult parseResult)
    {
        string[]? discover = options.ParseDiscover(parseResult);
        return discover is not null
            && (options.ParseSchema(parseResult)
                || (!parseResult.GetValue(options.Effective)
                    && discover.Length > 0));
    }
}
