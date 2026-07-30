using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the diff and library commands.
/// </summary>
public static class InspectionCommandDefinitions
{
    public static Command CreateTimelineCommand(SharedOptions opts)
    {
        var command = new Command(
            TimelineCommand.Name,
            "Correlate API or member-body Findings across a package version range");
        var argsArgument = new Argument<string[]>("args")
        {
            Description = "Package@A..B and type focus when --package/--type are omitted",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var packageOption = new Option<string?>("--package")
        {
            Description = "Package version range (e.g., System.Text.Json@8.0.0..10.0.0)",
        };
        var typeOption = new Option<string?>("--type")
        {
            Description = "Type focus (full name or unique short name)",
        };
        typeOption.Aliases.Add("-t");
        var memberOption = new Option<string?>("--member")
        {
            Description = "Exact member selector; required for Analysis Findings",
        };
        memberOption.Aliases.Add("-m");
        var findingOption = new Option<string?>("--finding")
        {
            Description = "Observation census: api.type, api.member, api.attribute, analysis.allocation, analysis.call-site, or analysis.unsafety",
        };
        var atOption = new Option<string[]>("--at")
        {
            Description = "Evaluate an exact version, #N, first, last, or all; repeat for sparse probes",
            AllowMultipleArgumentsPerToken = false,
        };
        var membersOption = new Option<bool>("--members")
        {
            Description = "Alias for --finding api.member",
        };
        var typePresenceOption = new Option<bool>("--type-presence")
        {
            Description = "Alias for --finding api.type",
        };
        var attributesOption = new Option<bool>("--attributes")
        {
            Description = "Alias for --finding api.attribute",
        };
        var tfmOption = new Option<string?>("--tfm")
        {
            Description = "Target framework (e.g., net8.0)",
        };
        var allOption = new Option<bool>("--all")
        {
            Description = "Include non-public, hidden, and obsolete API",
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description = "Include prerelease versions inside the range",
        };
        prereleaseOption.Aliases.Add("--prerelease");

        command.Arguments.Add(argsArgument);
        command.Options.Add(packageOption);
        command.Options.Add(typeOption);
        command.Options.Add(memberOption);
        command.Options.Add(findingOption);
        command.Options.Add(atOption);
        command.Options.Add(membersOption);
        command.Options.Add(typePresenceOption);
        command.Options.Add(attributesOption);
        command.Options.Add(tfmOption);
        command.Options.Add(allOption);
        command.Options.Add(prereleaseOption);
        opts.AddTableOptionsTo(command);
        opts.AddJsonOptionTo(command);
        command.Options.Add(opts.Markdown);
        opts.AddOutputOptionsTo(command);
        command.Options.Add(opts.Select);
        command.Options.Add(opts.Columns);
        command.Options.Add(opts.Fields);
        opts.AddCountOptionTo(command);
        opts.AddNuGetOptionsTo(command);

        command.SetAction(async (parseResult, ct) =>
        {
            string[] positional = parseResult.GetValue(argsArgument) ?? [];
            string? package = parseResult.GetValue(packageOption);
            string? type = parseResult.GetValue(typeOption);
            int positionalIndex = 0;
            if (package is null && positionalIndex < positional.Length)
                package = positional[positionalIndex++];
            if (type is null && positionalIndex < positional.Length)
                type = positional[positionalIndex++];
            if (positionalIndex < positional.Length)
            {
                Console.Error.WriteLine("Error: too many positional arguments.");
                return 1;
            }

            var aliases = new List<string>();
            if (parseResult.GetValue(membersOption))
                aliases.Add(MetadataFindings.MemberDescriptor.Id);
            if (parseResult.GetValue(typePresenceOption))
                aliases.Add(MetadataFindings.TypeDescriptor.Id);
            if (parseResult.GetValue(attributesOption))
                aliases.Add(MetadataFindings.AttributeDescriptor.Id);
            string? explicitFinding = parseResult.GetValue(findingOption);
            if (aliases.Count > 1 || (aliases.Count == 1 && explicitFinding is not null))
            {
                Console.Error.WriteLine(
                    "Error: specify only one of --finding, --members, --type-presence, or --attributes.");
                return 1;
            }

            return await TimelineCommand.ExecuteAsync(new TimelineOptions
            {
                PackageVersionRange = package ?? "",
                TypeName = type ?? "",
                MemberName = parseResult.GetValue(memberOption),
                Finding = aliases.Count == 1
                    ? aliases[0]
                    : explicitFinding ?? MetadataFindings.MemberDescriptor.Id,
                At = parseResult.GetValue(atOption) ?? [],
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
            });
        });

        return command;
    }

    public static Command CreateDiffCommand(SharedOptions opts)
    {
        var diffCommand = new Command(
            DiffCommand.Name,
            "Compare API surfaces, analysis signals, or implementation evidence between versions");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Version range and type filter. When no --package/--platform/--library is given, first arg is the package version range.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package")
        {
            Description = "Package with version range (e.g., System.Text.Json@9.0.0..10.0.2)"
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description = "Platform library with version range (e.g., System.Text.Json@8.0.23..10.0.2)"
        };
        var libraryOption = new Option<string?>("--library")
        {
            Description = "Local library path range (e.g., old/Foo.dll..new/Foo.dll)"
        };
        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework for platform diff (runtime, aspnetcore). Default: runtime"
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var typeFilterOption = new Option<string[]>("-t")
        {
            Description = "Filter to specific type(s)",
            AllowMultipleArgumentsPerToken = false
        };
        typeFilterOption.Aliases.Add("--type");
        var memberFilterOption = new Option<string[]>("-m")
        {
            Description = "Filter to specific member selector(s); use with --type or pass Type.Member",
            AllowMultipleArgumentsPerToken = false
        };
        memberFilterOption.Aliases.Add("--member");
        var nameOnlyOption = new Option<bool>("--name-only") { Description = "Show only type names that changed" };
        var breakingOption = new Option<bool>("--breaking") { Description = "Show only breaking changes" };
        var additiveOption = new Option<bool>("--additive") { Description = "Show only additive changes" };
        var changedOption = new Option<bool>("--changed") { Description = "Analysis Diff only: show only in-place changes to members present in both versions (drop added/removed members)" };
        var allocRegressionsOption = new Option<bool>("--alloc-regressions") { Description = "Analysis Diff focus: show only allocation increases on members present in both versions (the file-able set), in-loop (hot) ones first" };
        var authoredSourceOption = new Option<bool>("--authored-source") { Description = "Implementation Diff only: acquire checksum-verified authored SourceLink evidence" };
        var repoOption = new Option<string[]>("--repo")
        {
            Description = "Implementation Diff: read authored source from local git clone(s) by SourceLink commit + PDB checksum, before the network. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var findingOption = new Option<string?>("--finding") { Description = "Finding Transitions producer: api.type, api.member, api.attribute, analysis.allocation, or analysis.call-site" };
        var legendOption = new Option<bool>("--legend") { Description = "Show legend explaining change symbols" };

        diffCommand.Arguments.Add(argsArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(libraryOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(typeFilterOption);
        diffCommand.Options.Add(memberFilterOption);
        opts.AddTableOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Json);
        diffCommand.Options.Add(opts.Markdown);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        diffCommand.Options.Add(changedOption);
        diffCommand.Options.Add(allocRegressionsOption);
        diffCommand.Options.Add(authoredSourceOption);
        diffCommand.Options.Add(repoOption);
        diffCommand.Options.Add(findingOption);
        diffCommand.Options.Add(legendOption);
        opts.AddOutputOptionsTo(diffCommand);
        opts.AddNuGetOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Discover);
        diffCommand.Options.Add(opts.Tree);
        diffCommand.Options.Add(opts.Select);

        var commandArgs = new DiffOptionsParser.DiffCommandArgs(
            argsArg, packageOption, platformOption, libraryOption, frameworkOption, tfmOption, allOption,
            typeFilterOption, memberFilterOption, opts.NoHeaders, nameOnlyOption, breakingOption, additiveOption, changedOption, allocRegressionsOption, authoredSourceOption, findingOption, legendOption, repoOption);

        diffCommand.SetAction(async (parseResult, ct) =>
        {
            var result = DiffOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case DiffOptionsParser.VersionNumberError error:
                    Console.Error.WriteLine($"Error: '{error.Value}' looks like a version number. Use '{error.VersionRange}@{error.Value}' to specify a version.");
                    return 1;

                case DiffOptionsParser.Success success:
                    var exitCode = await DiffCommand.ExecuteAsync(success.Options);

                    if (exitCode == 0)
                    {
                        if (success.Options.Legend)
                            Hints.WriteDiffLegend();

                        if (!success.Options.FormatExplicitlySet)
                        {
                            var tips = DiffOptionsParser.BuildTips(success.Options, success.Options.TypeFilter);
                            Hints.WriteTips(success.TipLevel, [.. tips]);
                        }
                    }

                    return exitCode;

                default:
                    return 1;
            }
        });

        return diffCommand;
    }

    public static Command CreateLibraryCommand(SharedOptions opts)
    {
        var assemblyCommand = new Command("library", "Inspect a .NET library file");

        var assemblyPathArg = new Argument<string?>("source")
        {
            Description = "Library file path, NuGet package name (e.g., System.Text.Json), or package@version",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyPathArg.DefaultValueFactory = _ => null;

        var referencesOption = new Option<bool>("--references") { Description = "Show library references" };
        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Show library dependencies as a tree (tip: use 'depends --library' instead)" };
        var asmPlatformOption = new Option<string?>("--platform") { Description = "Inspect platform library (e.g., System.Text.Json)" };
        var asmPackageOption = new Option<string?>("--package") { Description = "Inspect library from NuGet package (e.g., System.Text.Json or System.Text.Json@9.0.4)" };
        var asmPrereleaseOption = new Option<bool>("--preview") { Description = "When resolving an unversioned package, include prerelease versions" };
        asmPrereleaseOption.Aliases.Add("--prerelease");
        var asmFrameworkOption = new Option<string?>("--framework") { Description = "Optional platform framework family (runtime, aspnetcore)" };
        var asmVersionOption = new Option<string?>("--version") { Description = "Platform runtime version (searches framework families in priority order)" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0, or 'all' for every TFM)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter Source Files rows by type glob/name (e.g., *Json*)" };
        typeFilterOption.Aliases.Add("--type");
        var ilOffsetOption = new Option<string?>("--il-offset") { Description = "MethodDef token + IL offset for coordinate-scoped sections (e.g., 0x06000001+0x5)" };
        var ilOffsetsOption = new Option<string?>("--il-offsets") { Description = "Text file of sparse MethodDef token + IL offset coordinates to explain" };
        var heapOption = new Option<string?>("--heap") { Description = "Metadata heap coordinate for the coordinate-scoped heap section (e.g., #Strings:0x1a4)" };
        var extractResourcesOption = new Option<string?>("--extract-resources")
        {
            Description = "Extract embedded resources beneath a directory without overwriting files"
        };
        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(referencesOption);
        assemblyCommand.Options.Add(dependenciesOption);
        assemblyCommand.Options.Add(asmPlatformOption);
        assemblyCommand.Options.Add(asmPackageOption);
        assemblyCommand.Options.Add(asmPrereleaseOption);
        assemblyCommand.Options.Add(asmFrameworkOption);
        assemblyCommand.Options.Add(asmVersionOption);
        assemblyCommand.Options.Add(asmTfmOption);
        assemblyCommand.Options.Add(typeFilterOption);
        assemblyCommand.Options.Add(ilOffsetOption);
        assemblyCommand.Options.Add(ilOffsetsOption);
        assemblyCommand.Options.Add(heapOption);
        assemblyCommand.Options.Add(opts.RawUrls);
        assemblyCommand.Options.Add(opts.BrowsableUrls);
        assemblyCommand.Options.Add(extractResourcesOption);
        opts.AddAllOptionsTo(assemblyCommand);
        opts.AddCountOptionTo(assemblyCommand);
        opts.AddPrintOptionTo(assemblyCommand);
        opts.AddShapeProjectionOptionsTo(assemblyCommand);
        opts.AddPerformanceTriageOptionsTo(assemblyCommand);

        assemblyCommand.SetAction(async (parseResult, ct) =>
        {
            var source = parseResult.GetValue(assemblyPathArg);
            var explicitPackage = parseResult.GetValue(asmPackageOption);
            var explicitPlatform = parseResult.GetValue(asmPlatformOption);

            // Disambiguate positional arg: local file vs package name
            string? assemblyPath = null;
            string? packagePath = explicitPackage;
            string? platformAssembly = explicitPlatform;
            var requestedFramework = parseResult.GetValue(asmFrameworkOption);
            var requestedPlatformVersion = parseResult.GetValue(asmVersionOption);

            if (!string.IsNullOrEmpty(source) && string.IsNullOrEmpty(explicitPlatform) && string.IsNullOrEmpty(explicitPackage))
            {
                if (File.Exists(source))
                    assemblyPath = source;
                else if (source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || source.Contains('/')
                    || source.Contains('\\'))
                {
                    // A non-existent value that looks like a local library path (has a file
                    // extension or directory separator) is reported as a missing file rather
                    // than misclassified as a NuGet package. See #1690.
                    assemblyPath = source;
                }
                else if (!source.Contains('@') && PlatformResolver.IsPlatformCandidate(source))
                {
                    // Platform-preferred routing for System.*/Microsoft.* bare names
                    bool verbose = parseResult.GetValue(opts.Verbose);
                    Action<string>? log = CommandLineHelpers.CreateVerboseLogger(verbose);
                    var (asmPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                        source, HttpClientFactory.Shared, log,
                        requestedFramework,
                        platformVersion: requestedPlatformVersion,
                        useRuntimeAssemblies: true);
                    if (error == null && asmPath != null)
                        platformAssembly = source;
                    else if (!string.IsNullOrEmpty(requestedFramework) || !string.IsNullOrEmpty(requestedPlatformVersion))
                        platformAssembly = source;
                    else
                        packagePath = source;
                }
                else
                    packagePath = source;
            }
            else if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(explicitPackage))
            {
                assemblyPath = source;
            }

            bool showReferences = parseResult.GetValue(referencesOption);
            bool showDependencies = parseResult.GetValue(dependenciesOption);

            var typeFilter = parseResult.GetValue(typeFilterOption);
            var select = opts.ParseSelect(parseResult);
            var selectDefault = opts.ParseSelectDefault(parseResult);
            bool hasExplicitSelect = select is { Length: > 0 } || selectDefault;
            var performanceTriage = opts.ParsePerformanceTriageOptions(parseResult);
            if (!PerformanceTriageOptions.TryValidate(performanceTriage, out var triageShapeError))
            {
                Console.Error.WriteLine(triageShapeError);
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(typeFilter))
                select = [.. select ?? [], "Source Files"];
            // Only surface performance sections from row filters when the user did not select
            // sections with -S; an explicit selection like -S "Top Leverage" must not silently gain
            // a second section and break single-section formats (--table/--tsv/--jsonl). When the
            // filter is a single --triage-shape that maps to one kind section, target that section
            // directly so tabular output stays single-section; otherwise surface the @Performance
            // group (via the "Performance Triage" category alias).
            if (performanceTriage.HasFilters && !opts.IsDiscoveryMode(parseResult) && !hasExplicitSelect)
            {
                var target = SectionNames.PerformanceTriage;
                if (performanceTriage.Shapes is { Length: > 0 })
                {
                    var kinds = performanceTriage.Shapes
                        .Select(PerformanceKinds.SectionForShape)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (kinds.Length == 1)
                        target = kinds[0];
                }
                select = [.. select ?? [], target];
            }

            var options = new LibraryOptions
            {
                AssemblyName = assemblyPath,
                IncludeMetadata = true,
                IncludeReferences = showReferences,
                IncludeDependencies = showDependencies,
                PackagePath = packagePath,
                IncludePrerelease = parseResult.GetValue(asmPrereleaseOption),
                PlatformAssembly = platformAssembly,
                PlatformFramework = requestedFramework,
                PlatformVersion = requestedPlatformVersion,
                Tfm = parseResult.GetValue(asmTfmOption),
                TypeFilter = typeFilter,
                ILOffsetParameter = parseResult.GetValue(ilOffsetOption),
                ILOffsetsPath = parseResult.GetValue(ilOffsetsOption),
                HeapParameter = parseResult.GetValue(heapOption),
                BrowsableUrls = parseResult.GetValue(opts.BrowsableUrls)
                    && !parseResult.GetValue(opts.RawUrls),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                Markdown = parseResult.GetValue(opts.Markdown),
                PlainText = parseResult.GetValue(opts.PlainText),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                TabularExplicitlySet = opts.IsTableExplicitlySet(parseResult),
                FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
                Format = opts.ResolveFormat(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                Discover = opts.ParseDiscover(parseResult),
                Tree = parseResult.GetValue(opts.Tree),
                Select = select,
                SelectDefault = selectDefault,
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Print = parseResult.GetValue(opts.Print),
                Value = parseResult.GetValue(opts.Value),
                Urls = parseResult.GetValue(opts.Urls),
                Paths = parseResult.GetValue(opts.Paths),
                JsonArray = parseResult.GetValue(opts.JsonArray),
                PrintRow = opts.ParsePrintRow(parseResult),
                ProjectionRow = opts.ParsePrintRow(parseResult),
                Rows = opts.ParseRows(parseResult),
                PerformanceTriage = performanceTriage,
                Schema = opts.ParseSchema(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                ExtractResources = parseResult.GetValue(extractResourcesOption)
            };

            return await LibraryCommand.ExecuteAsync(options);
        });

        return assemblyCommand;
    }
}
