using System.CommandLine;

using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.CommandLine;

public static class InspectionGraphCommandDefinitions
{
    public static Command CreateGraphCommand(SharedOptions opts)
    {
        var command = new Command(
            InspectionGraphCommand.Name,
            "Inspect typed relationships across an explicit workspace");
        var integrations = CreateIntegrationsCommand(opts);
        var libraries = CreateLibrariesCommand(opts);
        var cluster = CreateClusterCommand(opts);
        var calls = CreateCallsCommand(opts);
        command.Subcommands.Add(integrations);
        command.Subcommands.Add(libraries);
        command.Subcommands.Add(cluster);
        command.Subcommands.Add(calls);
        command.SetAction(_ =>
        {
            HelpWriter.WriteHelp(command);
            return 0;
        });
        return command;
    }

    static Command CreateCallsCommand(SharedOptions opts)
    {
        var command = new Command(
            ExternalCallGraphCommand.Name,
            "Show supply-chain package exits with shortest baseline connectors");
        var typeArgument = new Argument<string?>("type")
        {
            Description = "Type containing the focus member",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var memberArgument = new Argument<string?>("member")
        {
            Description = "Member selector (Name, Name:N, or Name~digest)",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var rootPackageOption = new Option<string>("--root-package")
        {
            Description =
                "Root package containing the selected member (name or name@version)",
        };
        var rootTfmOption = new Option<string>("--root-tfm")
        {
            Description =
                "Target framework selecting the exact root package asset",
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description =
                "Dependency traversal target framework (default: net12.0)",
        };
        var allOption = new Option<bool>("--all")
        {
            Description =
                "Include non-public types and members in focus selection",
        };
        var baselineOption = new Option<string>("--baseline")
        {
            Description =
                "Supply-chain baseline: nothing, self, or self+registered-ecosystems",
            DefaultValueFactory = _ =>
                "self+registered-ecosystems",
        };
        var firstPartyPrefixOption =
            new Option<string[]>("--first-party-prefix")
            {
                Description =
                    "Explicit first-party package prefix; repeat for additional prefixes",
                AllowMultipleArgumentsPerToken = false,
            };
        var depthOption = new Option<int>("--depth")
        {
            Description = "Maximum outgoing call depth",
            DefaultValueFactory = _ => 3,
        };
        var maxNodesOption = new Option<int>("--max-nodes")
        {
            Description = "Maximum call-graph nodes",
            DefaultValueFactory = _ => 25,
        };

        command.Arguments.Add(typeArgument);
        command.Arguments.Add(memberArgument);
        command.Options.Add(rootPackageOption);
        command.Options.Add(rootTfmOption);
        command.Options.Add(tfmOption);
        command.Options.Add(allOption);
        command.Options.Add(baselineOption);
        command.Options.Add(firstPartyPrefixOption);
        command.Options.Add(depthOption);
        command.Options.Add(maxNodesOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(
            command,
            validateLegacyRowWindow: static _ => false);
        command.Options.Add(opts.Tree);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.Validators.Add(result =>
        {
            if (result.GetResult(depthOption) is { Implicit: false }
                && result.GetValue(depthOption) < 0)
            {
                result.AddError("--depth must be non-negative.");
            }
            if (result.GetResult(maxNodesOption) is { Implicit: false }
                && result.GetValue(maxNodesOption) < 1)
            {
                result.AddError("--max-nodes must be positive.");
            }
            if (result.GetValue(opts.Tree)
                && result.GetValue(opts.Mermaid))
            {
                result.AddError(
                    "--tree and --mermaid are alternate graph renderings; choose one.");
            }
            if (result.GetValue(opts.Tree)
                && (result.GetValue(opts.Json)
                    || result.GetValue(opts.Markdown)
                    || result.GetValue(opts.PlainText)
                    || result.GetValue(opts.Table)
                    || result.GetValue(opts.Tsv)
                    || result.GetValue(opts.Jsonl)
                    || result.GetResult(opts.Verbosity)
                        is { Implicit: false }))
            {
                result.AddError(
                    "--tree is a standalone graph rendering and cannot combine with another output format.");
            }
            string? baselineValue =
                result.GetValue(baselineOption);
            bool baselineParsed = TryParseSupplyChainBaseline(
                    baselineValue,
                    out PackageSupplyChainBaseline baseline);
            if (!baselineParsed)
            {
                result.AddError(
                    "--baseline must be nothing, self, or self+registered-ecosystems.");
            }
            string[] prefixes =
                result.GetValue(firstPartyPrefixOption) ?? [];
            foreach (string prefix in prefixes)
            {
                try
                {
                    _ = new PackagePrefixDeclaration(prefix);
                }
                catch (ArgumentException)
                {
                    result.AddError(
                        $"--first-party-prefix '{prefix}' is not a valid package prefix.");
                }
            }
            if (baselineParsed
                && baseline
                    is PackageSupplyChainBaseline.Nothing
                && prefixes.Length != 0)
            {
                result.AddError(
                    "--first-party-prefix requires the self or self+registered-ecosystems baseline.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "External call graph",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string? rootPackage =
                parseResult.GetValue(rootPackageOption);
            string? rootTfm =
                parseResult.GetValue(rootTfmOption);
            string? tfm = parseResult.GetValue(tfmOption);
            string? type = parseResult.GetValue(typeArgument);
            string? member = parseResult.GetValue(memberArgument);
            _ = TryParseSupplyChainBaseline(
                parseResult.GetValue(baselineOption),
                out PackageSupplyChainBaseline baseline);
            if (string.IsNullOrWhiteSpace(type))
            {
                CommandError.Write("A focus type is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph calls --help' for usage.");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(member))
            {
                CommandError.Write("A focus member is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph calls --help' for usage.");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(rootPackage))
            {
                CommandError.Write("--root-package is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph calls --help' for usage.");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(rootTfm))
            {
                CommandError.Write("--root-tfm is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph calls --help' for usage.");
                return 1;
            }

            return await ExternalCallGraphCommand.ExecuteAsync(
                new ExternalCallGraphOptions
                {
                    TypeName = type,
                    Member = member,
                    RootPackage = rootPackage,
                    RootTfm = rootTfm,
                    Tfm = tfm,
                    IncludeAll = parseResult.GetValue(allOption),
                    SupplyChainBaseline = baseline,
                    FirstPartyPackagePrefixes =
                        parseResult.GetValue(firstPartyPrefixOption)
                        ?? [],
                    Depth = parseResult.GetValue(depthOption),
                    MaxNodes = parseResult.GetValue(maxNodesOption),
                    Format = opts.ResolveFormat(parseResult),
                    EmbeddedMermaid =
                        opts.IsEmbeddedMermaid(parseResult),
                    Tree = parseResult.GetValue(opts.Tree),
                    Count = parseResult.GetValue(opts.Count),
                    RowSelection = rowSelection,
                    Rows = rowSelection is null
                        ? opts.ParseRows(parseResult)
                        : null,
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions =
                        opts.ParseNuGetSourceOptions(parseResult),
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
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }

    static bool TryParseSupplyChainBaseline(
        string? value,
        out PackageSupplyChainBaseline baseline)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
                baseline =
                    PackageSupplyChainBaseline
                        .SelfAndRegisteredEcosystems;
                return true;
            case "nothing":
                baseline =
                    PackageSupplyChainBaseline.Nothing;
                return true;
            case "self":
                baseline =
                    PackageSupplyChainBaseline.Self;
                return true;
            case "self+registered-ecosystems":
                baseline =
                    PackageSupplyChainBaseline
                        .SelfAndRegisteredEcosystems;
                return true;
            default:
                baseline = default;
                return false;
        }
    }

    static Command CreateLibrariesCommand(SharedOptions opts)
        => CreateLibraryCallUseCommand(
            opts,
            LibraryCallUseCommand.Name,
            "Discover direct-use clusters between two local libraries",
            LibraryCallUseRouteKind.Libraries,
            clusterArgument: null);

    static Command CreateClusterCommand(SharedOptions opts)
    {
        var clusterArgument = new Argument<int?>("cluster")
        {
            Description =
                "Positive pair-local Direct-Use Cluster ordinal",
            Arity = ArgumentArity.ZeroOrOne,
        };
        return CreateLibraryCallUseCommand(
            opts,
            LibraryCallUseCommand.ClusterName,
            "Inspect one Direct-Use Cluster between two local libraries",
            LibraryCallUseRouteKind.Cluster,
            clusterArgument);
    }

    static Command CreateLibraryCallUseCommand(
        SharedOptions opts,
        string name,
        string description,
        LibraryCallUseRouteKind routeKind,
        Argument<int?>? clusterArgument)
    {
        var command = new Command(
            name,
            description);
        var libraryOption = new Option<string[]>("--library")
        {
            Description =
                "Local managed library in the induced pair. Specify exactly twice.",
            AllowMultipleArgumentsPerToken = false,
        };
        if (clusterArgument is not null)
        {
            command.Arguments.Add(clusterArgument);
        }
        command.Options.Add(libraryOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(command);
        opts.AddSectionOptionsTo(command);
        opts.AddCountOptionTo(command);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string[]? discover = opts.ParseDiscover(parseResult);
            int? cluster = clusterArgument is null
                ? null
                : parseResult.GetValue(clusterArgument);
            if (routeKind == LibraryCallUseRouteKind.Cluster
                && cluster is null
                && discover is null)
            {
                CommandError.Write(
                    "A positive Direct-Use Cluster ordinal is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph cluster --help' for usage.");
                return 1;
            }
            if (cluster <= 0)
            {
                CommandError.Write(
                    "The Direct-Use Cluster ordinal must be positive.");
                return 1;
            }

            if (!CliRowSelectionCommandRegistry
                .TryGetPreparedSemanticIntent(
                    parseResult,
                    routeKind == LibraryCallUseRouteKind.Cluster
                        ? "Direct-Use Cluster call site"
                        : "Library direct-use cluster",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string[] libraries =
                parseResult.GetValue(libraryOption) ?? [];

            return await LibraryCallUseCommand.ExecuteAsync(
                new LibraryCallUseOptions
                {
                    Libraries = libraries,
                    RouteKind = routeKind,
                    QueryPlan =
                        GraphLibrariesQuery.CreatePlan(cluster),
                    Format = opts.ResolveFormat(parseResult),
                    Count = parseResult.GetValue(opts.Count),
                    RowSelection = rowSelection,
                    Rows = rowSelection is null
                        ? opts.ParseRows(parseResult)
                        : null,
                    NoHeader =
                        parseResult.GetValue(opts.NoHeaders),
                    Verbose =
                        parseResult.GetValue(opts.Verbose),
                    Columns =
                        opts.ParseColumns(parseResult),
                    Fields =
                        opts.ParseFields(parseResult),
                    Discover = discover,
                    Select =
                        opts.ParseSelect(parseResult),
                    SelectDefault =
                        opts.ParseSelectDefault(parseResult),
                    Schema =
                        opts.ParseSchema(parseResult),
                    Tree =
                        opts.ParseTree(parseResult),
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
            isActive: result =>
                LibraryCallUseRowSelectionAdoption.IsActive(
                    result,
                    opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }

    static Command CreateIntegrationsCommand(SharedOptions opts)
    {
        var command = new Command(
            InspectionGraphCommand.IntegrationsName,
            "Induce Integration relationships over an explicit package set");
        var packageOption = new Option<string[]>("--package")
        {
            Description =
                "Package in the induced set (name or name@version). Repeat for each package.",
            AllowMultipleArgumentsPerToken = false,
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description =
                "Shared target framework for the package set (for example net10.0)",
        };
        var relationshipOption = new Option<string[]>("--relationship")
        {
            Description =
                "Exact relationship id. Repeat to override the default Integration family.",
            AllowMultipleArgumentsPerToken = false,
        };
        CliOptionValueValidation.AcceptOnlyFromAmong(
            relationshipOption,
            StringComparer.Ordinal,
            [.. InspectionGraphCommand.SupportedRelationshipIds]);
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description =
                "Allow prerelease versions when an unversioned package floats",
        };
        prereleaseOption.Aliases.Add("--prerelease");

        command.Options.Add(packageOption);
        command.Options.Add(tfmOption);
        command.Options.Add(relationshipOption);
        command.Options.Add(prereleaseOption);
        command.Options.Add(opts.Json);
        command.Options.Add(opts.Markdown);
        command.Options.Add(opts.PlainText);
        command.Options.Add(opts.Mermaid);
        opts.AddTableOptionsTo(command);
        opts.AddOutputOptionsTo(
            command,
            validateLegacyRowWindow: static _ => false);
        command.Options.Add(opts.Tree);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.Validators.Add(result =>
        {
            string? missingRelationshipValue =
                result.GetResult(relationshipOption)
                    is { Tokens.Count: > 0 } relationshipResult
                    ? relationshipResult.Tokens
                        .Select(static token => token.Value)
                        .FirstOrDefault(static value =>
                            value.StartsWith(
                                "-",
                                StringComparison.Ordinal))
                    : null;
            if (missingRelationshipValue is not null)
            {
                result.AddError(
                    $"--relationship requires a relationship id before '{missingRelationshipValue}'.");
            }
            if (result.GetValue(opts.Tree)
                && result.GetValue(opts.Mermaid))
            {
                result.AddError(
                    "--tree and --mermaid are alternate graph renderings; choose one.");
            }
            if (result.GetValue(opts.Tree)
                && (result.GetValue(opts.Json)
                    || result.GetValue(opts.Markdown)
                    || result.GetValue(opts.PlainText)
                    || result.GetValue(opts.Table)
                    || result.GetValue(opts.Tsv)
                    || result.GetValue(opts.Jsonl)
                    || result.GetResult(opts.Verbosity)
                        is { Implicit: false }))
            {
                result.AddError(
                    "--tree is a standalone graph rendering and cannot combine with another output format.");
            }
        });

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Integration graph",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            string[] packages =
                parseResult.GetValue(packageOption) ?? [];
            string? tfm = parseResult.GetValue(tfmOption);
            if (packages.Length == 0)
            {
                CommandError.Write("At least one --package is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph integrations --help' for usage.");
                return 1;
            }
            if (string.IsNullOrWhiteSpace(tfm))
            {
                CommandError.Write("A shared --tfm is required.");
                CommandError.WriteLine(
                    "Run 'dotnet-inspect graph integrations --help' for usage.");
                return 1;
            }

            OutputFormat format = opts.ResolveFormat(parseResult);
            return await InspectionGraphCommand.ExecuteAsync(
                new InspectionGraphOptions
                {
                    Packages = packages,
                    Tfm = tfm,
                    Relationships =
                        parseResult.GetValue(relationshipOption) ?? [],
                    IncludePrerelease =
                        parseResult.GetValue(prereleaseOption),
                    Format = format,
                    EmbeddedMermaid =
                        opts.IsEmbeddedMermaid(parseResult),
                    Tree = parseResult.GetValue(opts.Tree),
                    Count = parseResult.GetValue(opts.Count),
                    RowSelection = rowSelection,
                    Rows = rowSelection is null
                        ? opts.ParseRows(parseResult)
                        : null,
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions =
                        opts.ParseNuGetSourceOptions(parseResult),
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
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return command;
    }
}
