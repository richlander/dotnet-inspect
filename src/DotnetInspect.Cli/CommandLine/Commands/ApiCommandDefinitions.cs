using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines the type and member commands.
/// </summary>
public static class ApiCommandDefinitions
{
    /// <summary>
    /// Creates the type command for fast type discovery (compact table, no docs by default).
    /// </summary>
    public static Command CreateTypeCommand(
        SharedOptions opts,
        out TypeOptionsParser.TypeCommandArgs structuralArgs)
    {
        var typeCommand = new Command(TypeCommand.Name, "Discover types in a package or library (compact table output)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type pattern. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, name@version, or name@A..B)" };
        var atOption = new Option<string?>("--at") { Description = "Address in a package range: exact version, #N, first, or last" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var projectOption = new Option<string?>("--project") { Description = "Source: restored project.assets.json context" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var workspaceOption = new Option<string?>("--workspace")
        {
            Description = "Source: canonical Base64URL Workspace packet string",
        };
        var matchOption = new Option<bool>("--match")
        {
            Description = "Match this Type's API coordinate across two literal package-version endpoints"
        };
        var shareOption = WorkspaceShareOption.Create(
            "Emit the resolved Type scenario as a canonical Workspace packet or complete URL");
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json or --envelope where supported)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter types with unsafe signatures (pointers)" };
        var repoOption = new Option<string[]>("--repo")
        {
            Description = "Read PDB-mapped type source from local git clone(s) by SourceLink commit + PDB checksum, before the network. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name or glob",
            AllowMultipleArgumentsPerToken = false
        };
        memberOption.Aliases.Add("--member");
        var kindOption = new Option<string[]>("-k")
        {
            Description = "Filter by kind (class, struct, interface, enum, delegate, method, property, field, event, constructor)",
            AllowMultipleArgumentsPerToken = false
        };
        kindOption.Aliases.Add("--kind");
        typeCommand.Arguments.Add(argsArg);
        typeCommand.Options.Add(packageOption);
        typeCommand.Options.Add(atOption);
        typeCommand.Options.Add(assemblyOption);
        typeCommand.Options.Add(platformOption);
        typeCommand.Options.Add(projectOption);
        typeCommand.Options.Add(frameworkOption);
        typeCommand.Options.Add(tfmOption);
        typeCommand.Options.Add(workspaceOption);
        typeCommand.Options.Add(matchOption);
        typeCommand.Options.Add(shareOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(opts.PreferRenderedUrls);
        opts.AddTableOptionsTo(typeCommand);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(repoOption);
        typeCommand.Options.Add(memberOption);
        typeCommand.Options.Add(kindOption);
        opts.AddSectionOptionsTo(typeCommand);
        opts.AddCountOptionTo(typeCommand);
        opts.AddPrintOptionTo(typeCommand);
        opts.AddShapeProjectionOptionsTo(typeCommand);
        opts.AddPerformanceTriageOptionsTo(typeCommand);
        typeCommand.Options.Add(opts.Markdown);
        typeCommand.Options.Add(opts.PlainText);
        opts.AddEnvelopeOptionTo(
            typeCommand,
            opts.Discover,
            opts.QueryHelp,
            opts.Select,
            opts.Limit,
            opts.Rows,
            opts.Head,
            opts.Tail,
            opts.Count);
        typeCommand.Options.Add(opts.Taste);
        typeCommand.Options.Add(opts.ReadableNames);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        var commandArgs = new TypeOptionsParser.TypeCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, projectOption, frameworkOption, tfmOption,
            allOption, typeFilterOption, compactOption,
            opts.NoHeaders, unsafeOption, repoOption, memberOption, kindOption, atOption,
            workspaceOption, shareOption);
        structuralArgs = commandArgs;

        CliRowSelectionCommandRegistry.Register(
            typeCommand,
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
                !result.GetValue(matchOption)
                && TypeOptionsParser.IsTypeListingRowSelection(
                    result,
                    opts,
                    commandArgs),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            typeCommand,
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
            result => CloneCandidateRowSelectionAdoption.IsActive(
                result,
                opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            typeCommand,
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
                !result.GetValue(matchOption)
                && TypeOptionsParser.IsTypeRelationsRowSelection(
                    result,
                    opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            if (parseResult.GetValue(opts.Envelope)
                && parseResult.GetResult(opts.Verbosity)
                    is { Implicit: false }
                && opts.ParseVerbosity(parseResult)
                    is Verbosity.Normal or Verbosity.Detailed)
            {
                CommandError.Write(
                    "--envelope cannot be combined with normal or detailed "
                        + "verbosity.");
                return 1;
            }

            if (parseResult.GetValue(workspaceOption) is not null
                && parseResult.GetValue(matchOption))
            {
                CommandError.Write(
                    "--workspace cannot be combined with --match.");
                return 1;
            }

            if (parseResult.GetValue(compactOption)
                && opts.ResolveFormat(parseResult) != OutputFormat.Json
                && !parseResult.GetValue(opts.Envelope))
            {
                CommandError.Write("--compact requires --json or --envelope.");
                return 1;
            }

            if (parseResult.GetValue(matchOption))
            {
                return ApiCoordinateMatchOptionsParser.ParseType(
                    parseResult,
                    opts,
                    commandArgs,
                    matchOption) switch
                {
                    ApiCoordinateMatchOptionsParser.Failure failure =>
                        WriteMatchError(failure.Error),
                    ApiCoordinateMatchOptionsParser.Success success =>
                        await ApiCoordinateMatchCommand.ExecuteAsync(
                            success,
                            ct),
                    _ => 1,
                };
            }

            if (opts.ResolveFormat(parseResult) == OutputFormat.Json
                && parseResult.GetValue(opts.Tree)
                && parseResult.GetValue(opts.Discover) is null)
            {
                CommandError.Write(
                    "--tree is not supported with JSON output on type.");
                return 1;
            }

            if (TypeOptionsParser.TryCreateStructuralPlan(
                    parseResult,
                    opts,
                    commandArgs,
                    out StructuralDiscoveryPlan? structuralPlan,
                    out OptionError? structuralError,
                    out bool targetFree))
            {
                if (structuralError is not null)
                {
                    CommandError.Write(structuralError.Value);
                    return 1;
                }

                StructuralDiscoveryRequest request =
                    StructuralDiscoveryRequest.From(
                        parseResult,
                        opts,
                        targetFree
                            ? OutputFormat.Table
                            : OutputFormat.Markdown);
                return structuralPlan switch
                {
                    StructuralDiscoveryPlan.Resolved resolved =>
                        StructuralViewRegistry.Execute(
                            resolved.Route,
                            request),
                    StructuralDiscoveryPlan.Alternatives alternatives =>
                        StructuralViewRegistry.Execute(
                            alternatives.Value,
                            request),
                    _ => 1,
                };
            }

            var result = await TypeOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case TypeOptionsParser.Discovery d:
                    var typeSchemaMap = ApiViewContext.Default.GetSchemaInfo<CliApiSurface>()!.ToDocumentSchema();
                    var typeFormat = opts.ResolveFormat(parseResult, OutputFormat.Table);
                    var typePipeline = ApiTypeSectionDescriptors.CreatePipeline();
                    return DiscoverOutput.Execute(
                        d.Discover,
                        typeSchemaMap,
                        DiscoveryOutputRequest.Create(
                            typeFormat,
                            d.Tree,
                            opts.IsTableExplicitlySet(parseResult),
                            parseResult.GetValue(opts.NoHeaders),
                            (int)opts.ParseVerbosity(parseResult),
                            ProjectionAudit.Requested(parseResult, opts)),
                        sectionCategories: typePipeline.GetCategoryMap());

                case TypeOptionsParser.ShowHelp:
                    CommandError.Write("Type name, pattern, or source required.");
                    CommandError.WriteLine("Run 'dotnet-inspect type --help' for usage.");
                    return 1;

                case TypeOptionsParser.VersionError error:
                    CommandError.Write(error.Error);
                    return 1;

                case TypeOptionsParser.UnrecognizedOption error:
                    CommandError.Write($"Unrecognized option '{error.Option}'.");
                    return 1;

                case TypeOptionsParser.Success success:
                    return await TypeCommand.ExecuteAsync(
                        success.Options,
                        success.Plan,
                        ct);

                default:
                    return 1;
            }
        });

        return typeCommand;
    }

    /// <summary>
    /// Creates the member command for deep member inspection (docs on by default).
    /// </summary>
    public static Command CreateMemberCommand(
        SharedOptions opts,
        out MemberOptionsParser.MemberCommandArgs structuralArgs)
    {
        var memberCommand = new Command(MemberCommand.Name, "Inspect type members (docs on by default)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package, type name, and member filter. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, name@version, or name@A..B)" };
        var atOption = new Option<string?>("--at") { Description = "Address in a package range: exact version, #N, first, or last" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var matchOption = new Option<bool>("--match")
        {
            Description = "Match this Member's API coordinate across two literal package-version endpoints"
        };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name (supports globs, Type.Member dotted syntax)",
            AllowMultipleArgumentsPerToken = false
        };
        memberOption.Aliases.Add("--member");
        var ctorOption = new Option<bool>("--ctor") { Description = "Filter members to constructors (shorthand for -m .ctor)" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json or --envelope where supported)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter members to unsafe signatures (pointers)" };
        var indexOption = new Option<int?>("--index") { Description = "Select member overload by index (or use Name:N shorthand)" };
        var sourcePartsOption = new Option<bool>("--source-parts")
        {
            Description = "Acquire verified authored source and show member, XML documentation, attribute, signature, and body ranges"
        };
        var sourcePartOption = new Option<string?>("--part")
        {
            Description = "With --print, select an authored member part: member, xml-docs, attributes, signature, or body"
        };
        var shareOption = WorkspaceShareOption.Create(
            "Emit one exact public NuGet member as a canonical Workspace packet or complete URL");
        var binOption = new Option<string[]>("--bin")
        {
            Description = "Scan output directory(s) for cross-assembly callers and Call Graph traversal. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        binOption.Aliases.Add("--directory");
        var callerProjectOption = new Option<string[]>("--project")
        {
            Description = "Source: restored project.assets.json context when no other source is supplied; also scans restored dependencies for callers and Call Graph traversal. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var callerPackageOption = new Option<string[]>("--caller-package")
        {
            Description = "Download and scan package(s) for cross-assembly callers and Call Graph traversal. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var repoOption = new Option<string[]>("--repo")
        {
            Description = "Read PDB-mapped source from local git clone(s) by SourceLink commit + PDB checksum, before the network (PDB Source). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var kindOption = new Option<string[]>("-k")
        {
            Description = "Filter by member kind (method, property, field, event, constructor)",
            AllowMultipleArgumentsPerToken = false
        };
        kindOption.Aliases.Add("--kind");
        var routerDeferredTargetOption =
            new Option<string?>(
                RouterCommandDefinition.DeferredTypeOrMemberOptionName)
            {
                Hidden = true
            };

        memberCommand.Arguments.Add(argsArg);
        memberCommand.Options.Add(packageOption);
        memberCommand.Options.Add(atOption);
        memberCommand.Options.Add(assemblyOption);
        memberCommand.Options.Add(platformOption);
        memberCommand.Options.Add(frameworkOption);
        memberCommand.Options.Add(tfmOption);
        memberCommand.Options.Add(matchOption);
        memberCommand.Options.Add(allOption);
        memberCommand.Options.Add(memberOption);
        memberCommand.Options.Add(ctorOption);
        memberCommand.Options.Add(opts.Limit);
        memberCommand.Options.Add(opts.Json);
        memberCommand.Options.Add(compactOption);
        memberCommand.Options.Add(opts.PreferRenderedUrls);
        opts.AddTableOptionsTo(memberCommand);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(sourcePartsOption);
        memberCommand.Options.Add(sourcePartOption);
        memberCommand.Options.Add(shareOption);
        memberCommand.Options.Add(binOption);
        memberCommand.Options.Add(callerProjectOption);
        memberCommand.Options.Add(callerPackageOption);
        memberCommand.Options.Add(repoOption);
        memberCommand.Options.Add(kindOption);
        memberCommand.Options.Add(routerDeferredTargetOption);
        opts.AddSectionOptionsTo(memberCommand);
        opts.AddCountOptionTo(memberCommand);
        opts.AddPrintOptionTo(memberCommand);
        opts.AddShapeProjectionOptionsTo(memberCommand);
        opts.AddPerformanceTriageOptionsTo(memberCommand);
        memberCommand.Options.Add(opts.Mermaid);
        memberCommand.Options.Add(opts.Markdown);
        memberCommand.Options.Add(opts.PlainText);
        memberCommand.Options.Add(opts.Envelope);
        memberCommand.Options.Add(opts.Taste);
        memberCommand.Options.Add(opts.ReadableNames);
        memberCommand.Options.Add(opts.Focus);
        opts.AddOutputOptionsTo(memberCommand);
        opts.AddNuGetOptionsTo(memberCommand);

        CliRowSelectionCommandRegistry.Register(
            memberCommand,
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
            result => MemberCallRowSelectionAdoption.IsActive(
                result,
                opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            memberCommand,
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
            result => MemberCallerRowSelectionAdoption.IsActive(
                result,
                opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            memberCommand,
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
            result => IsProjectedMemberFactsJson(result, opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            memberCommand,
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
            result => CloneCandidateRowSelectionAdoption.IsActive(
                result,
                opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        var commandArgs = new MemberOptionsParser.MemberCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, memberOption, ctorOption,
            compactOption, opts.NoHeaders,
            unsafeOption, indexOption, shareOption, kindOption,
            binOption, callerProjectOption, callerPackageOption, repoOption, atOption,
            routerDeferredTargetOption, sourcePartsOption, sourcePartOption);
        structuralArgs = commandArgs;

        memberCommand.SetAction(async (parseResult, ct) =>
        {
            if (IsExactCallGraphTransportSelection(
                    parseResult,
                    opts)
                && !ApiCommand.ValidateCallGraphTransport(
                    CreateCallGraphTransportPreflightOptions(
                        parseResult,
                        opts)))
            {
                return 1;
            }

            if (parseResult.GetValue(opts.Envelope)
                && !parseResult.GetValue(matchOption)
                && !IsExactCallGraphEnvelopeSelection(
                    parseResult,
                    opts))
            {
                CommandError.Write("--envelope on member requires --match.");
                return 1;
            }

            if (parseResult.GetValue(matchOption))
            {
                return ApiCoordinateMatchOptionsParser.ParseMember(
                    parseResult,
                    opts,
                    commandArgs,
                    matchOption) switch
                {
                    ApiCoordinateMatchOptionsParser.Failure failure =>
                        WriteMatchError(failure.Error),
                    ApiCoordinateMatchOptionsParser.Success success =>
                        await ApiCoordinateMatchCommand.ExecuteAsync(
                            success,
                            ct),
                    _ => 1,
                };
            }

            if (MemberOptionsParser.TryCreateStructuralPlan(
                    parseResult,
                    opts,
                    commandArgs,
                    out StructuralDiscoveryPlan? structuralPlan,
                    out OptionError? structuralError,
                    out bool targetFree))
            {
                if (structuralError is not null)
                {
                    CommandError.Write(structuralError.Value);
                    return 1;
                }

                StructuralDiscoveryRequest request =
                    StructuralDiscoveryRequest.From(
                        parseResult,
                        opts,
                        targetFree
                            ? OutputFormat.Table
                            : OutputFormat.Markdown);
                return structuralPlan switch
                {
                    StructuralDiscoveryPlan.Resolved resolved =>
                        StructuralViewRegistry.Execute(
                            resolved.Route,
                            request),
                    StructuralDiscoveryPlan.Alternatives alternatives =>
                        StructuralViewRegistry.Execute(
                            alternatives.Value,
                            request),
                    _ => 1,
                };
            }

            if (ApiCommand.RejectUniversallyInvalidMemberSelect(
                    opts.ParseDiscover(parseResult),
                    opts.ParseSelect(parseResult),
                    opts.ParseSelectDefault(parseResult),
                    allowListingPipeline:
                        parseResult.GetValue(
                            routerDeferredTargetOption) is not null,
                    includeMemberTypeView:
                        parseResult.GetValue(
                            routerDeferredTargetOption) is not null
                        || !MemberOptionsParser
                            .HasAcquisitionFreeMemberGesture(
                                parseResult,
                                commandArgs)))
            {
                return 1;
            }

            var result = await MemberOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case MemberOptionsParser.Discovery d:
                    var memberSchemaMap = ApiCommand.GetTypeDocumentSchema(new MemberOptions());
                    var memberFormat = opts.ResolveFormat(parseResult, OutputFormat.Table);
                    var memberPipelines = new[]
                    {
                        ApiMemberSectionDescriptors.CreatePipeline(),
                        ApiMemberOverloadSectionDescriptors.CreatePipeline(),
                        ApiMemberDetailSectionDescriptors.CreatePipeline(),
                    };
                    var memberCategories =
                        new Dictionary<string, string[]>(
                            StringComparer.OrdinalIgnoreCase);
                    foreach (var memberPipeline in memberPipelines)
                    {
                        foreach (var (category, sections) in
                                 ApiMemberSectionPipelines.GetCategoryMap(
                                     memberPipeline))
                        {
                            memberCategories[category] =
                                memberCategories.TryGetValue(
                                    category,
                                    out string[]? existing)
                                    ? [.. existing
                                        .Concat(sections)
                                        .Distinct(
                                            StringComparer.OrdinalIgnoreCase)]
                                    : sections;
                        }
                    }
                    HashSet<string> memberBaseSections =
                        memberPipelines
                            .SelectMany(
                                pipeline => pipeline.BaseSectionNames)
                            .ToHashSet(
                                StringComparer.OrdinalIgnoreCase);
                    HashSet<string> memberCatalogHiddenSections =
                        memberSchemaMap.SectionNames
                            .Where(section =>
                                !memberBaseSections.Contains(section))
                            .ToHashSet(
                                StringComparer.OrdinalIgnoreCase);
                    return DiscoverOutput.Execute(
                        d.Discover,
                        memberSchemaMap,
                        DiscoveryOutputRequest.Create(
                            memberFormat,
                            d.Tree,
                            opts.IsTableExplicitlySet(parseResult),
                            parseResult.GetValue(opts.NoHeaders),
                            (int)opts.ParseVerbosity(parseResult),
                            ProjectionAudit.Requested(parseResult, opts)),
                        sectionCategories:
                            memberCategories,
                        catalogHiddenSections:
                            memberCatalogHiddenSections,
                        listedCategoryDoors:
                            memberCategories.Keys.ToHashSet(
                                StringComparer.OrdinalIgnoreCase),
                        exactOnlySections:
                            ApiMemberSectionPipelines
                                .GetExactOnlySections(
                                    overloadInventory: false));

                case MemberOptionsParser.ShowHelp:
                    CommandError.Write("Type name or source required.");
                    CommandError.WriteLine("Run 'dotnet-inspect member --help' for usage.");
                    return 1;

                case MemberOptionsParser.VersionError error:
                    CommandError.Write(error.Error);
                    return 1;

                case MemberOptionsParser.UnrecognizedOption error:
                    CommandError.Write($"Unrecognized option '{error.Option}'.");
                    return 1;

                case MemberOptionsParser.Success success:
                    return await MemberCommand.ExecuteAsync(
                        success.Options,
                        success.Plan);

                default:
                    return 1;
            }
        });

        return memberCommand;
    }

    internal static bool IsExactCallGraphEnvelopeSelection(
        ParseResult parseResult,
        SharedOptions opts) =>
        parseResult.GetValue(opts.Envelope)
        && HasExactCallGraphSelector(parseResult, opts);

    private static bool IsExactCallGraphTransportSelection(
        ParseResult parseResult,
        SharedOptions opts)
    {
        if (!HasExactCallGraphSelector(parseResult, opts))
            return false;

        return parseResult.GetValue(opts.Envelope)
            || opts.ResolveFormat(parseResult) == OutputFormat.Json;
    }

    private static bool HasExactCallGraphSelector(
        ParseResult parseResult,
        SharedOptions opts)
    {
        if (opts.ParseSelectDefault(parseResult))
        {
            return false;
        }

        string[] selectors =
        [
            .. (opts.ParseSelect(parseResult) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        return selectors is [var selector]
            && selector.Equals(
                SectionNames.CallGraph,
                StringComparison.OrdinalIgnoreCase);
    }

    private static MemberOptions CreateCallGraphTransportPreflightOptions(
        ParseResult parseResult,
        SharedOptions opts)
    {
        OutputFormat format = opts.ResolveFormat(parseResult);
        return new()
        {
            EnvelopeOutput = parseResult.GetValue(opts.Envelope),
            JsonOutput = format == OutputFormat.Json,
            FormatExplicitlySet =
                opts.IsFormatExplicitlySet(parseResult),
            FormatFlagExplicitlySet =
                opts.IsFormatFlagExplicitlySet(parseResult),
            MarkdownExplicitlySet =
                parseResult.GetResult(opts.Markdown)
                    is { Implicit: false },
            PlainText = parseResult.GetValue(opts.PlainText),
            Tabular =
                format is OutputFormat.Table
                    or OutputFormat.Tsv
                    or OutputFormat.Jsonl,
            Tsv = format == OutputFormat.Tsv,
            Jsonl = format == OutputFormat.Jsonl,
            MermaidOutput = format == OutputFormat.Mermaid,
            EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            Count = parseResult.GetValue(opts.Count),
            Limit = parseResult.GetValue(opts.Limit),
            LineWindowExplicitlySet =
                parseResult.GetResult(opts.Limit) is { Implicit: false }
                || parseResult.GetResult(opts.Head)
                    is { Implicit: false }
                || parseResult.GetResult(opts.Tail)
                    is { Implicit: false }
                || parseResult.GetResult(opts.Lines)
                    is { Implicit: false }
                || parseResult.GetResult(opts.TailLines)
                    is { Implicit: false },
            Rows = opts.ParseRows(parseResult),
            Print = parseResult.GetValue(opts.Print),
            PrintRow = opts.ParsePrintRow(parseResult),
            Value = parseResult.GetValue(opts.Value),
            Urls = parseResult.GetValue(opts.Urls),
            Paths = parseResult.GetValue(opts.Paths),
            JsonArray = parseResult.GetValue(opts.JsonArray),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Select = opts.ParseSelect(parseResult),
            IncludeSections = [SectionNames.CallGraph],
            ExactIncludeSectionsOverride = [SectionNames.CallGraph],
            MemberSectionsPreResolved = true,
        };
    }

    private static bool IsProjectedMemberFactsJson(
        ParseResult parseResult,
        SharedOptions opts)
    {
        if (opts.ResolveFormat(parseResult) != OutputFormat.Json
            || opts.IsDiscoveryMode(parseResult)
            || parseResult.GetValue(opts.Count)
            || parseResult.GetValue(opts.Print)
            || parseResult.GetValue(opts.Value)
            || parseResult.GetValue(opts.Urls)
            || parseResult.GetValue(opts.Paths)
            || opts.ParseColumns(parseResult) is null
                && opts.ParseFields(parseResult) is null)
        {
            return false;
        }

        string[] selectors =
        [
            .. (opts.ParseSelect(parseResult) ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        return selectors is [var selector]
            && selector.Equals(
                SectionNames.Facts,
                StringComparison.OrdinalIgnoreCase);
    }

    private static int WriteMatchError(OptionError error)
    {
        CommandError.Write(error);
        return 1;
    }
}
