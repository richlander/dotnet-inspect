using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Planning;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the type and member commands.
/// </summary>
public static class ApiCommandDefinitions
{
    /// <summary>
    /// Creates the type command for fast type discovery (compact table, no docs by default).
    /// </summary>
    public static Command CreateTypeCommand(SharedOptions opts)
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
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var shapeOption = new Option<bool>("--shape") { Description = "Output type shape (inheritance, interfaces, members)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter types with unsafe signatures (pointers)" };
        var repoOption = new Option<string[]>("--repo")
        {
            Description = "Read PDB-mapped type source from local git clone(s) by SourceLink commit + PDB checksum, before the network. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name or limit count (-m 5)",
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
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(opts.RawUrls);
        typeCommand.Options.Add(opts.BrowsableUrls);
        opts.AddTableOptionsTo(typeCommand);
        typeCommand.Options.Add(shapeOption);
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
        typeCommand.Options.Add(opts.Bare);
        typeCommand.Options.Add(opts.Taste);
        typeCommand.Options.Add(opts.ReadableNames);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        var commandArgs = new TypeOptionsParser.TypeCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, projectOption, frameworkOption, tfmOption,
            allOption, typeFilterOption, compactOption,
            opts.NoHeaders, shapeOption, unsafeOption, repoOption, memberOption, kindOption, atOption);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
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
                    return DiscoverOutput.Execute(d.Discover, typeSchemaMap, tree: d.Tree,
                        json: typeFormat == OutputFormat.Json,
                        tsv: typeFormat == OutputFormat.Tsv,
                        jsonl: typeFormat == OutputFormat.Jsonl,
                        markdown: typeFormat == OutputFormat.Markdown,
                        verbosity: (int)opts.ParseVerbosity(parseResult),
                        sectionCategories: typePipeline.GetCategoryMap(),
                        projection: ProjectionAudit.Requested(parseResult, opts));

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
                        success.Plan);

                default:
                    return 1;
            }
        });

        return typeCommand;
    }

    /// <summary>
    /// Creates the member command for deep member inspection (docs on by default).
    /// </summary>
    public static Command CreateMemberCommand(SharedOptions opts)
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
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name (supports globs, Type.Member dotted syntax)",
            AllowMultipleArgumentsPerToken = false
        };
        memberOption.Aliases.Add("--member");
        var ctorOption = new Option<bool>("--ctor") { Description = "Filter members to constructors (shorthand for -m .ctor)" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter members to unsafe signatures (pointers)" };
        var indexOption = new Option<int?>("--index") { Description = "Select member overload by index (or use Name:N shorthand)" };
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
        var shapeOption = new Option<bool>("--shape")
        {
            Description = "Output type shape when the routed target resolves as a type",
            Hidden = true
        };
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
        memberCommand.Options.Add(allOption);
        memberCommand.Options.Add(memberOption);
        memberCommand.Options.Add(ctorOption);
        memberCommand.Options.Add(opts.Limit);
        memberCommand.Options.Add(opts.Json);
        memberCommand.Options.Add(compactOption);
        memberCommand.Options.Add(opts.RawUrls);
        memberCommand.Options.Add(opts.BrowsableUrls);
        opts.AddTableOptionsTo(memberCommand);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(binOption);
        memberCommand.Options.Add(callerProjectOption);
        memberCommand.Options.Add(callerPackageOption);
        memberCommand.Options.Add(repoOption);
        memberCommand.Options.Add(kindOption);
        memberCommand.Options.Add(shapeOption);
        memberCommand.Options.Add(routerDeferredTargetOption);
        opts.AddSectionOptionsTo(memberCommand);
        opts.AddCountOptionTo(memberCommand);
        opts.AddPrintOptionTo(memberCommand);
        opts.AddShapeProjectionOptionsTo(memberCommand);
        opts.AddPerformanceTriageOptionsTo(memberCommand);
        memberCommand.Options.Add(opts.Mermaid);
        memberCommand.Options.Add(opts.Markdown);
        memberCommand.Options.Add(opts.PlainText);
        memberCommand.Options.Add(opts.Bare);
        memberCommand.Options.Add(opts.Taste);
        memberCommand.Options.Add(opts.ReadableNames);
        memberCommand.Options.Add(opts.Focus);
        opts.AddOutputOptionsTo(memberCommand);
        opts.AddNuGetOptionsTo(memberCommand);

        var commandArgs = new MemberOptionsParser.MemberCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, memberOption, ctorOption,
            compactOption, opts.NoHeaders,
            unsafeOption, indexOption, kindOption,
            binOption, callerProjectOption, callerPackageOption, repoOption, atOption,
            shapeOption, routerDeferredTargetOption);

        memberCommand.SetAction(async (parseResult, ct) =>
        {
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
                    var memberPipeline = ApiMemberSectionPipelines.Create(new MemberOptions());
                    return DiscoverOutput.Execute(d.Discover, memberSchemaMap, tree: d.Tree,
                        json: memberFormat == OutputFormat.Json,
                        tsv: memberFormat == OutputFormat.Tsv,
                        jsonl: memberFormat == OutputFormat.Jsonl,
                        markdown: memberFormat == OutputFormat.Markdown,
                        verbosity: (int)opts.ParseVerbosity(parseResult),
                        sectionCategories: memberPipeline.GetCategoryMap(),
                        projection: ProjectionAudit.Requested(parseResult, opts));

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
}
