using System.CommandLine;
using System.CommandLine.Parsing;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

public static class WorkspaceCommandDefinitions
{
    public static Command CreateWorkspaceCommand(SharedOptions opts)
    {
        var command = new Command(
            WorkspaceCommand.Name,
            "Author or inspect an inspection Workspace and optionally evaluate one exact Navigation occurrence");
        var packageOption = new Option<string[]>("--package")
        {
            Description =
                "Package in the Workspace (name or name@version). Exact duplicates are shown once.",
            AllowMultipleArgumentsPerToken = false,
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description =
                "Shared target framework for the package set (for example net10.0)",
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description =
                "Allow prerelease versions when an unversioned package floats",
        };
        prereleaseOption.Aliases.Add("--prerelease");
        var packetOption = new Option<string?>("--packet")
        {
            Description =
                "Use one canonical Base64URL Workspace packet string",
            Arity = ArgumentArity.ExactlyOne,
        };
        var replacePackageOption = new Option<int?>("--replace-package")
        {
            Description =
                "Replace one direct Package by its one-based packet navigation-row order",
        };
        var replacementVersionOption = new Option<string?>("--to-version")
        {
            Description = "Exact destination Version for --replace-package",
        };
        var replacementTfmOption = new Option<string?>("--to-tfm")
        {
            Description = "Destination TFM for --replace-package (isolated context only)",
        };
        var registerLibraryOption =
            new Option<string[]>("--register-library")
            {
                Description =
                    "Register an exact Package Library as package@version/assembly@assembly-version",
                AllowMultipleArgumentsPerToken = false,
            };
        var registerPackagePrefixOption =
            new Option<string[]>("--register-package-prefix")
            {
                Description =
                    "Register an inert literal Package ID prefix",
                AllowMultipleArgumentsPerToken = false,
            };
        var registerEcosystemOption =
            new Option<string[]>("--register-ecosystem")
            {
                Description =
                    "Register a shipped ecosystem by short or canonical ID",
                AllowMultipleArgumentsPerToken = false,
            };
        var kindOption = new Option<string[]>("--kind")
        {
            Description =
                "Select inventory kinds: package, exact-library, package-prefix, or ecosystem",
            AllowMultipleArgumentsPerToken = false,
        };
        CliOptionValueValidation.AcceptOnlyFromAmong(
            kindOption,
            StringComparer.OrdinalIgnoreCase,
            "package",
            "exact-library",
            "package-prefix",
            "ecosystem");
        var rootRequestOption = new Option<string?>("--root-request")
        {
            Description =
                "Reopen the exact package Root named by a reopening token from the Root column of 'package query ... --where \"library-literal=TEXT\" --tfm TFM'",
            Arity = ArgumentArity.ExactlyOne,
        };
        var activePackageOption = new Option<int?>("--active-package")
        {
            Description =
                "Evaluate one exact committed Package occurrence by its one-based Workspace order",
        };
        var libraryOption = new Option<string?>("--library")
        {
            Description =
                "Exact Library asset id from Navigation output",
        };
        var allLibrariesOption = new Option<bool>("--all-libraries")
        {
            Description =
                "Use the aggregate Library subject as a Type destination source",
        };
        var typeOption = new Option<string?>("--type")
        {
            Description =
                "Exact returned metadata Type full name for an atomic destination",
        };
        var memberOption = new Option<string?>("--member")
        {
            Description =
                "Exact returned Member stable selector for an atomic destination",
        };
        var lensOption = new Option<string?>("--lens")
        {
            Description =
                "Exact destination view-facet id, such as type.compare or member.compare",
        };
        var shareOption = WorkspaceShareOption.Create(
            "Emit the complete portable Workspace definition as a canonical packet or URL");
        var makePackageDependenciesExplicitOption =
            new Option<bool>("--make-package-dependencies-explicit")
            {
                Description =
                    "Acquire direct Package roots and append their exact direct "
                    + "dependencies to the portable Workspace definition; "
                    + "requires --share",
            };
        var anonymousNuGetSourceOption =
            new Option<string[]>("--nuget-source-anonymous")
        {
            Description =
                "Register one exact portable anonymous NuGet source endpoint",
            AllowMultipleArgumentsPerToken = false,
        };
        var authenticationRequiredNuGetSourceOption =
            new Option<string[]>("--nuget-source-auth-required")
            {
                Description =
                    "Register one exact portable NuGet source endpoint that requires authentication",
                AllowMultipleArgumentsPerToken = false,
            };
        var patForOption = new Option<string[]>("--pat-for")
            {
                Description =
                    "Bind an ephemeral Basic credential: HTTPS-ENDPOINT USERNAME env:NAME|stdin|file:PATH",
                Arity = ArgumentArity.OneOrMore,
                AllowMultipleArgumentsPerToken = true,
            };

        command.Options.Add(packageOption);
        command.Options.Add(tfmOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(packetOption);
        command.Options.Add(replacePackageOption);
        command.Options.Add(replacementVersionOption);
        command.Options.Add(replacementTfmOption);
        command.Options.Add(registerLibraryOption);
        command.Options.Add(registerPackagePrefixOption);
        command.Options.Add(registerEcosystemOption);
        command.Options.Add(kindOption);
        command.Options.Add(rootRequestOption);
        command.Options.Add(activePackageOption);
        command.Options.Add(libraryOption);
        command.Options.Add(allLibrariesOption);
        command.Options.Add(typeOption);
        command.Options.Add(memberOption);
        command.Options.Add(lensOption);
        command.Options.Add(shareOption);
        command.Options.Add(makePackageDependenciesExplicitOption);
        command.Options.Add(anonymousNuGetSourceOption);
        command.Options.Add(authenticationRequiredNuGetSourceOption);
        command.Options.Add(patForOption);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Envelope);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(
            command,
            validateLegacyRowWindow: result =>
                !IsTopLevelInventory(
                    result,
                    activePackageOption,
                    libraryOption,
                    allLibrariesOption,
                    typeOption,
                    memberOption,
                    lensOption,
                    shareOption,
                    replacePackageOption));
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Workspace inventory",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string[] packages =
                parseResult.GetValue(packageOption) ?? [];
            string? tfm = parseResult.GetValue(tfmOption);
            string? packet = parseResult.GetValue(packetOption);
            WorkspaceRegistrationInput[] orderedRegistrations =
                ParseOrderedRegistrations(
                    parseResult,
                    registerLibraryOption,
                    registerPackagePrefixOption,
                    registerEcosystemOption);
            WorkspaceTopLevelInventoryEntryKind[] inventoryKinds =
            [
                .. (parseResult.GetValue(kindOption) ?? [])
                    .Select(ParseInventoryKind),
            ];
            string? rootRequest = parseResult.GetValue(rootRequestOption);
            int? activePackage =
                parseResult.GetValue(activePackageOption);
            string? library = parseResult.GetValue(libraryOption);
            bool allLibraries =
                parseResult.GetValue(allLibrariesOption);
            string? type = parseResult.GetValue(typeOption);
            string? member = parseResult.GetValue(memberOption);
            string? lens = parseResult.GetValue(lensOption);
            if (!TryParsePackageSources(
                    parseResult,
                    opts,
                    anonymousNuGetSourceOption,
                    authenticationRequiredNuGetSourceOption,
                    patForOption,
                    out WorkspacePackageSourceDefinition[] packageSources,
                    out WorkspacePatBindingInput[] patBindings,
                    out NuGetSourceOptions sourceOptions))
            {
                return 1;
            }
            if (rootRequest is not null
                && (packages.Length > 0
                    || !string.IsNullOrWhiteSpace(tfm)))
            {
                CommandError.Write(
                    "--root-request opens the exact Root its token names and cannot be combined with --package or --tfm.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect workspace --help' for usage.");
                return 1;
            }

            if (packages.Length > 0
                && string.IsNullOrWhiteSpace(tfm))
            {
                CommandError.Write(
                    "A shared --tfm is required when the Workspace contains packages.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect workspace --help' for usage.");
                return 1;
            }
            return await WorkspaceCommand.ExecuteAsync(
                new WorkspaceOptions
                {
                    Packages = packages,
                    Tfm = tfm,
                    Packet = packet,
                    ReplacePackage = parseResult.GetValue(replacePackageOption),
                    ReplacementVersion = parseResult.GetValue(replacementVersionOption),
                    ReplacementTfm = parseResult.GetValue(replacementTfmOption),
                    EnvelopeOutput = parseResult.GetValue(opts.Envelope),
                    OrderedRegistrations = orderedRegistrations,
                    RegisteredLibraries =
                        parseResult.GetValue(registerLibraryOption) ?? [],
                    RegisteredPackagePrefixes =
                        parseResult.GetValue(registerPackagePrefixOption) ?? [],
                    RegisteredEcosystems =
                        parseResult.GetValue(registerEcosystemOption) ?? [],
                    InventoryKinds = inventoryKinds,
                    RootRequest = rootRequest,
                    ActivePackage = activePackage,
                    Library = library,
                    AllLibraries = allLibraries,
                    Type = type,
                    Member = member,
                    Lens = lens,
                    IncludePrerelease =
                        parseResult.GetValue(prereleaseOption),
                    Format = opts.ResolveFormat(parseResult),
                    Count = parseResult.GetValue(opts.Count),
                    RowSelection = rowSelection,
                    Rows = rowSelection is null
                        ? opts.ParseRows(parseResult)
                        : null,
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    ShareFormat =
                        WorkspaceShareOption.Parse(parseResult, shareOption),
                    MakePackageDependenciesExplicit =
                        parseResult.GetValue(
                            makePackageDependenciesExplicitOption),
                    PackageSources = packageSources,
                    PatBindings = patBindings,
                    SourceOptions = sourceOptions,
                },
                cancellationToken);
        });

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
                IsTopLevelInventory(
                    result.CommandResult,
                    activePackageOption,
                    libraryOption,
                    allLibrariesOption,
                    typeOption,
                    memberOption,
                    lensOption,
                    shareOption,
                    replacePackageOption),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }

    static bool IsTopLevelInventory(
        CommandResult commandResult,
        Option<int?> activePackageOption,
        Option<string?> libraryOption,
        Option<bool> allLibrariesOption,
        Option<string?> typeOption,
        Option<string?> memberOption,
        Option<string?> lensOption,
        Option<string?> shareOption,
        Option<int?> replacePackageOption) =>
        commandResult.GetValue(activePackageOption) is null
        && commandResult.GetValue(libraryOption) is null
        && !commandResult.GetValue(allLibrariesOption)
        && commandResult.GetValue(typeOption) is null
        && commandResult.GetValue(memberOption) is null
        && commandResult.GetValue(lensOption) is null
        && commandResult.GetResult(shareOption) is null
        && commandResult.GetValue(replacePackageOption) is null;

    static WorkspaceRegistrationInput[] ParseOrderedRegistrations(
        ParseResult parseResult,
        Option<string[]> registerLibraryOption,
        Option<string[]> registerPackagePrefixOption,
        Option<string[]> registerEcosystemOption)
    {
        var kinds =
            new Dictionary<string, WorkspaceRegistrationInputKind>(
                StringComparer.Ordinal)
            {
                [registerLibraryOption.Name] =
                    WorkspaceRegistrationInputKind.ExactLibrary,
                [registerPackagePrefixOption.Name] =
                    WorkspaceRegistrationInputKind.PackagePrefix,
                [registerEcosystemOption.Name] =
                    WorkspaceRegistrationInputKind.Ecosystem,
            };
        var registrations = new List<WorkspaceRegistrationInput>();
        for (int index = 0; index < parseResult.Tokens.Count; index++)
        {
            Token token = parseResult.Tokens[index];
            if (token.Type != TokenType.Option
                || !kinds.TryGetValue(
                    token.Value,
                    out WorkspaceRegistrationInputKind kind)
                || index + 1 >= parseResult.Tokens.Count
                || parseResult.Tokens[index + 1].Type == TokenType.Option)
            {
                continue;
            }

            registrations.Add(
                new WorkspaceRegistrationInput(
                    kind,
                    parseResult.Tokens[++index].Value));
        }

        return [.. registrations];
    }

    static WorkspaceTopLevelInventoryEntryKind ParseInventoryKind(
        string value) =>
        value.ToLowerInvariant() switch
        {
            "package" => WorkspaceTopLevelInventoryEntryKind.Package,
            "exact-library" =>
                WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
            "package-prefix" =>
                WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
            "ecosystem" => WorkspaceTopLevelInventoryEntryKind.Ecosystem,
            _ => throw new InvalidOperationException(
                "System.CommandLine admitted an unsupported Workspace inventory kind."),
        };

    static bool TryParsePackageSources(
        ParseResult parseResult,
        SharedOptions options,
        Option<string[]> anonymousNuGetSourceOption,
        Option<string[]> authenticationRequiredNuGetSourceOption,
        Option<string[]> patForOption,
        out WorkspacePackageSourceDefinition[] packageSources,
        out WorkspacePatBindingInput[] patBindings,
        out NuGetSourceOptions sourceOptions)
    {
        NuGetSourceOptions parsedSourceOptions =
            options.ParseNuGetSourceOptions(parseResult);
        sourceOptions = parsedSourceOptions;
        packageSources = [];
        patBindings = [];

        try
        {
            (WorkspacePackageSourceAuthentication Authentication, string Value)[]
                declarations = ParseOrderedPackageSourceDeclarations(
                    parseResult,
                    anonymousNuGetSourceOption,
                    authenticationRequiredNuGetSourceOption);
            if (declarations.Length > 0
                && (parsedSourceOptions.Sources.Length > 0
                    || parsedSourceOptions.AdditionalSources.Length > 0
                    || parsedSourceOptions.ConfigFile is not null))
            {
                throw new ArgumentException(
                    "Portable Workspace NuGet source registrations cannot be "
                        + "combined with --source, --add-source, or --nugetconfig.");
            }
            if (declarations.Length > WorkspaceSharePacketCodec.MaxPackageSources)
            {
                throw new ArgumentException(
                    $"A portable Workspace permits at most "
                        + $"{WorkspaceSharePacketCodec.MaxPackageSources} package sources.");
            }

            var sources =
                new List<WorkspacePackageSourceDefinition>(declarations.Length);
            foreach ((
                WorkspacePackageSourceAuthentication authentication,
                string endpoint) in declarations)
            {
                sources.Add(new WorkspacePackageSourceDefinition(
                    endpoint,
                    authentication));
            }
            WorkspacePackageSourceDefinition.ValidateSet(sources);
            packageSources = [.. sources];

            var bindings = new List<WorkspacePatBindingInput>();
            var boundSources = new HashSet<string>(StringComparer.Ordinal);
            int stdinBindings = 0;
            foreach ((string endpoint, string username, string provider)
                in ParsePatBindings(parseResult, patForOption))
            {
                WorkspacePatBindingInput binding =
                    ParsePatBinding(endpoint, username, provider);
                if (!boundSources.Add(binding.Endpoint))
                {
                    throw new ArgumentException(
                        $"--pat-for binds endpoint '{binding.Endpoint}' more than once.");
                }

                if (binding.Kind == WorkspacePatInputKind.StandardInput
                    && ++stdinBindings > 1)
                {
                    throw new ArgumentException(
                        "At most one --pat-for binding may read from stdin.");
                }
                bindings.Add(binding);
            }
            patBindings = [.. bindings];
            return true;
        }
        catch (ArgumentException ex)
        {
            CommandError.Write(
                "The Workspace package source options are invalid.",
                [ex.Message]);
            return false;
        }
    }

    static (
        WorkspacePackageSourceAuthentication Authentication,
        string Value)[] ParseOrderedPackageSourceDeclarations(
            ParseResult parseResult,
            Option<string[]> anonymousNuGetSourceOption,
            Option<string[]> authenticationRequiredNuGetSourceOption)
    {
        var modes =
            new Dictionary<string, WorkspacePackageSourceAuthentication>(
                StringComparer.Ordinal)
            {
                [anonymousNuGetSourceOption.Name] =
                    WorkspacePackageSourceAuthentication.Anonymous,
                [authenticationRequiredNuGetSourceOption.Name] =
                    WorkspacePackageSourceAuthentication.AuthenticationRequired,
            };
        var declarations = new List<(
            WorkspacePackageSourceAuthentication Authentication,
            string Value)>();
        for (int index = 0; index < parseResult.Tokens.Count; index++)
        {
            Token token = parseResult.Tokens[index];
            if (token.Type != TokenType.Option
                || !modes.TryGetValue(
                    token.Value,
                    out WorkspacePackageSourceAuthentication mode)
                || index + 1 >= parseResult.Tokens.Count
                || parseResult.Tokens[index + 1].Type == TokenType.Option)
            {
                continue;
            }

            declarations.Add((mode, parseResult.Tokens[++index].Value));
        }

        return [.. declarations];
    }

    static (string Endpoint, string Username, string Provider)[]
        ParsePatBindings(
        ParseResult parseResult,
        Option<string[]> patForOption)
    {
        var bindings = new List<(
            string Endpoint,
            string Username,
            string Provider)>();
        for (int index = 0; index < parseResult.Tokens.Count; index++)
        {
            Token token = parseResult.Tokens[index];
            if (token.Type != TokenType.Option
                || !string.Equals(
                    token.Value,
                    patForOption.Name,
                    StringComparison.Ordinal))
            {
                continue;
            }

            int firstValue = index + 1;
            int valueCount = 0;
            while (firstValue + valueCount < parseResult.Tokens.Count
                && parseResult.Tokens[firstValue + valueCount].Type
                    != TokenType.Option)
            {
                valueCount++;
            }
            if (valueCount != 3)
            {
                throw new ArgumentException(
                    "--pat-for requires exactly three values: "
                        + "HTTPS-ENDPOINT USERNAME env:NAME|stdin|file:PATH.");
            }

            bindings.Add((
                parseResult.Tokens[firstValue].Value,
                parseResult.Tokens[firstValue + 1].Value,
                parseResult.Tokens[firstValue + 2].Value));
            index += valueCount;
        }
        return [.. bindings];
    }

    static WorkspacePatBindingInput ParsePatBinding(
        string endpoint,
        string username,
        string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        string canonicalEndpoint = new WorkspacePackageSourceDefinition(
            endpoint,
            WorkspacePackageSourceAuthentication.AuthenticationRequired)
            .Endpoint;
        if (string.Equals(provider, "stdin", StringComparison.Ordinal))
        {
            return new WorkspacePatBindingInput(
                canonicalEndpoint,
                username,
                WorkspacePatInputKind.StandardInput,
                null);
        }
        if (provider.StartsWith("env:", StringComparison.Ordinal)
            && provider.Length > "env:".Length)
        {
            return new WorkspacePatBindingInput(
                canonicalEndpoint,
                username,
                WorkspacePatInputKind.Environment,
                provider["env:".Length..]);
        }
        if (provider.StartsWith("file:", StringComparison.Ordinal)
            && provider.Length > "file:".Length)
        {
            return new WorkspacePatBindingInput(
                canonicalEndpoint,
                username,
                WorkspacePatInputKind.File,
                provider["file:".Length..]);
        }

        throw new ArgumentException(
            $"--pat-for endpoint '{endpoint}' must use env:NAME, stdin, or file:PATH.");
    }
}
