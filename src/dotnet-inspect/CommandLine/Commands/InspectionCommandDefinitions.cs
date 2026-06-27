using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the diff and library commands.
/// </summary>
public static class InspectionCommandDefinitions
{
    public static Command CreateDiffCommand(SharedOptions opts)
    {
        var diffCommand = new Command(DiffCommand.Name, "Compare API surfaces between package, platform, or local library versions");

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
            AllowMultipleArgumentsPerToken = true
        };
        typeFilterOption.Aliases.Add("--type");
        var nameOnlyOption = new Option<bool>("--name-only") { Description = "Show only type names that changed" };
        var breakingOption = new Option<bool>("--breaking") { Description = "Show only breaking changes" };
        var additiveOption = new Option<bool>("--additive") { Description = "Show only additive changes" };
        var changedOption = new Option<bool>("--changed") { Description = "Analysis Diff only: show only in-place changes to members present in both versions (drop added/removed members)" };
        var allocRegressionsOption = new Option<bool>("--alloc-regressions") { Description = "Analysis Diff focus: show only allocation increases on members present in both versions (the file-able set), in-loop (hot) ones first" };
        var legendOption = new Option<bool>("--legend") { Description = "Show legend explaining change symbols" };

        diffCommand.Arguments.Add(argsArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(libraryOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(typeFilterOption);
        opts.AddTableOptionsTo(diffCommand);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        diffCommand.Options.Add(changedOption);
        diffCommand.Options.Add(allocRegressionsOption);
        diffCommand.Options.Add(legendOption);
        opts.AddOutputOptionsTo(diffCommand);
        opts.AddNuGetOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Select);

        var commandArgs = new DiffOptionsParser.DiffCommandArgs(
            argsArg, packageOption, platformOption, libraryOption, frameworkOption, tfmOption, allOption,
            typeFilterOption, opts.OneLine, opts.NoHeaders, nameOnlyOption, breakingOption, additiveOption, changedOption, allocRegressionsOption, legendOption);

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
        var ilOffsetOption = new Option<string?>("--il-offset") { Description = "Resolve MethodDef token + IL offset to source (e.g., 0x06000001+0x5)" };
        var extractResourcesOption = new Option<string?>("--extract-resources") { Description = "Extract embedded resources to a directory" };
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
        assemblyCommand.Options.Add(opts.RawUrls);
        assemblyCommand.Options.Add(opts.BrowsableUrls);
        assemblyCommand.Options.Add(extractResourcesOption);
        opts.AddAllOptionsTo(assemblyCommand);
        opts.AddCountOptionTo(assemblyCommand);

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
            if (!string.IsNullOrWhiteSpace(typeFilter))
                select = [.. select ?? [], "Source Files"];

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
                ILOffset = parseResult.GetValue(ilOffsetOption),
                BrowsableUrls = parseResult.GetValue(opts.BrowsableUrls)
                    && !parseResult.GetValue(opts.RawUrls),
                JsonOutput = parseResult.GetValue(opts.Json),
                Markdown = parseResult.GetValue(opts.Markdown),
                PlainText = parseResult.GetValue(opts.PlainText),
                OneLine = opts.ResolveOneLine(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                OneLineExplicitlySet = opts.IsTableExplicitlySet(parseResult),
                FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
                Format = opts.ResolveFormat(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                Discover = opts.ParseDiscover(parseResult),
                Tree = parseResult.GetValue(opts.Tree),
                Select = select,
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Count = parseResult.GetValue(opts.Count),
                Rows = opts.ParseRows(parseResult),
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
