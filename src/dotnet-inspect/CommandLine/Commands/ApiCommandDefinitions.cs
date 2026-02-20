using System.CommandLine;
using System.CommandLine.Help;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the type, member, and deprecated api commands.
/// </summary>
public static class ApiCommandDefinitions
{
    /// <summary>
    /// Creates a deprecated hidden api command that shows a deprecation message.
    /// </summary>
    public static Command CreateDeprecatedApiCommand()
    {
        var deprecatedApiCommand = new Command("api", "Deprecated: Use 'type' or 'member' instead") { Hidden = true };
        deprecatedApiCommand.TreatUnmatchedTokensAsErrors = false;
        deprecatedApiCommand.SetAction(_ =>
        {
            Console.Error.WriteLine("The 'api' command is deprecated. Please use:");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  type   - Discover types in a package/library (terse, no docs by default)");
            Console.Error.WriteLine("  member - Inspect type members (docs by default)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  dotnet-inspect type --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member JsonSerializer --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member -m JsonSerializer.Deserialize --package System.Text.Json");
            return 1;
        });
        return deprecatedApiCommand;
    }

    /// <summary>
    /// Creates the type command for fast type discovery (terse, no docs by default).
    /// </summary>
    public static Command CreateTypeCommand(SharedOptions opts)
    {
        var typeCommand = new Command(TypeCommand.Name, "Discover types in a package or library (terse output)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type pattern. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var sourcelinkOnlyOption = new Option<bool>("--sourcelink-only") { Description = "Filter types to those with SourceLink resolution" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var shapeOption = new Option<bool>("--shape") { Description = "Output type shape (inheritance, interfaces, members)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter types with unsafe signatures (pointers)" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name or limit count (-m 5)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");

        typeCommand.Arguments.Add(argsArg);
        typeCommand.Options.Add(packageOption);
        typeCommand.Options.Add(assemblyOption);
        typeCommand.Options.Add(platformOption);
        typeCommand.Options.Add(frameworkOption);
        typeCommand.Options.Add(tfmOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Limit);
        typeCommand.Options.Add(sourcelinkOnlyOption);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(oneLineOption);
        typeCommand.Options.Add(noHeaderOption);
        typeCommand.Options.Add(shapeOption);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(memberOption);
        opts.AddSectionOptionsTo(typeCommand);
        typeCommand.Options.Add(opts.Markout);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
            bool hasExplicitSource = SourceResolver.HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(opts.IncludeSections) != null && parseResult.GetValue(opts.IncludeSections) == null)
                {
                    var allTypeSections = SectionRegistry.ApiTypeSections;
                    SectionRegistry.ListSections(allTypeSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            var source = await SourceResolver.ResolveAsync(
                args, explicitPackage, explicitAssembly, explicitPlatform,
                parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);

            if (source.VersionError)
            {
                Console.Error.WriteLine(source.VersionErrorMessage);
                return 1;
            }

            var packagePath = source.PackagePath;
            var typeName = source.TypeName;
            var apiFrameworkOverride = source.FrameworkOverride;

            var typeFilterValue = parseResult.GetValue(typeFilterOption);
            int? typeLimit = null;
            string? typeFilter = typeFilterValue;
            if (typeFilterValue != null && int.TryParse(typeFilterValue, out var tNum))
            {
                typeLimit = tNum;
                typeFilter = null;
            }

            // Parse -m: number = member limit, glob = member filter
            var memberValues = parseResult.GetValue(memberOption) ?? [];
            HashSet<string> memberFilter = [];
            int? memberLimit = null;
            if (memberValues.Length == 1 && int.TryParse(memberValues[0], out var mNum))
            {
                memberLimit = mNum;
            }
            else if (memberValues.Length > 0)
            {
                memberFilter = new HashSet<string>(memberValues, StringComparer.OrdinalIgnoreCase);
            }

            var options = new ApiOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = source.AssemblyPath,
                PlatformAssembly = source.PlatformAssembly,
                PlatformFramework = apiFrameworkOverride ?? parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = typeFilter,
                MemberFilter = memberFilter,
                Limit = memberLimit ?? typeLimit,
                ShowDocs = false,  // Type command: docs off by default
                DocsExplicitlySet = false,
                SourceLinkOnly = parseResult.GetValue(sourcelinkOnlyOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                ShapeOutput = parseResult.GetValue(shapeOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            options = options with
            {
                TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || typeLimit != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
            };

            return await TypeCommand.ExecuteAsync(options);
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

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name (supports globs, Type.Member dotted syntax)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");
        var ctorOption = new Option<bool>("--ctor") { Description = "Filter members to constructors (shorthand for -m .ctor)" };
        var docsOption = new Option<bool>("--docs") { Description = "Include XML doc comments (on by default, use --no-docs to suppress)" };
        var noDocsOption = new Option<bool>("--no-docs") { Description = "Suppress XML doc comments" };
        var useLocalDocsOption = new Option<bool>("--use-local-docs") { Description = "Include XML docs from local packs directory (offline)" };
        var samplesOption = new Option<bool>("--samples") { Description = "Include code samples from source" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing (default: /raw/ for LLM consumption)" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter members to unsafe signatures (pointers)" };
        var indexOption = new Option<int?>("--index") { Description = "Select member overload by index (or use Name:N shorthand)" };
        var paramsOption = new Option<string>("--params") { Description = "Select member overload by parameter types (comma-separated)" };
        var ofOption = new Option<string>("-of") { Description = "Select member overload by first parameter type" };
        var selectOption = new Option<bool>("--select") { Description = "Show member overload index (Name:N) column" };

        memberCommand.Arguments.Add(argsArg);
        memberCommand.Options.Add(packageOption);
        memberCommand.Options.Add(assemblyOption);
        memberCommand.Options.Add(platformOption);
        memberCommand.Options.Add(frameworkOption);
        memberCommand.Options.Add(tfmOption);
        memberCommand.Options.Add(allOption);
        memberCommand.Options.Add(memberOption);
        memberCommand.Options.Add(ctorOption);
        memberCommand.Options.Add(opts.Limit);
        memberCommand.Options.Add(docsOption);
        memberCommand.Options.Add(noDocsOption);
        memberCommand.Options.Add(useLocalDocsOption);
        memberCommand.Options.Add(samplesOption);
        memberCommand.Options.Add(browsableUrlsOption);
        memberCommand.Options.Add(opts.Json);
        memberCommand.Options.Add(compactOption);
        memberCommand.Options.Add(oneLineOption);
        memberCommand.Options.Add(noHeaderOption);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(paramsOption);
        memberCommand.Options.Add(ofOption);
        memberCommand.Options.Add(selectOption);
        opts.AddSectionOptionsTo(memberCommand);
        memberCommand.Options.Add(opts.Markout);
        opts.AddOutputOptionsTo(memberCommand);
        opts.AddNuGetOptionsTo(memberCommand);

        memberCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
            bool hasExplicitSource = SourceResolver.HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(opts.IncludeSections) != null && parseResult.GetValue(opts.IncludeSections) == null)
                {
                    var allMemberSections = SectionRegistry.ApiMemberSections;
                    SectionRegistry.ListSections(allMemberSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            // Member command needs to extract positional members separately
            List<string> positionalMembers = [];
            if (hasExplicitSource && args.Length >= 2)
                positionalMembers.AddRange(args[1..]);
            else if (!hasExplicitSource && args.Length >= 3)
                positionalMembers.AddRange(args[2..]);

            var source = await SourceResolver.ResolveAsync(
                args, explicitPackage, explicitAssembly, explicitPlatform,
                parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: false);

            if (source.VersionError)
            {
                Console.Error.WriteLine(source.VersionErrorMessage);
                return 1;
            }

            var packagePath = source.PackagePath;
            var typeName = source.TypeName;
            var apiFrameworkOverride = source.FrameworkOverride;

            var badOption = positionalMembers.FirstOrDefault(m => m.StartsWith("--"));
            if (badOption != null)
            {
                Console.Error.WriteLine($"Error: Unrecognized option '{badOption}'.");
                return 1;
            }

            var members = parseResult.GetValue(memberOption) ?? [];
            var allMembers = members.Concat(positionalMembers).ToArray();
            var ctorOnly = parseResult.GetValue(ctorOption);

            // Parse dotted syntax (Type.Member) from -m option
            string? dottedTypeFilter = null;
            for (int i = 0; i < allMembers.Length; i++)
            {
                var memberArg = allMembers[i];
                var dotIdx = memberArg.LastIndexOf('.');
                // Only split if: has dot, not a glob pattern, and first segment isn't empty
                if (dotIdx > 0 && !memberArg.Contains('*') && !memberArg.Contains('?'))
                {
                    dottedTypeFilter = memberArg[..dotIdx];
                    allMembers[i] = memberArg[(dotIdx + 1)..];
                    // Use the extracted type name if no explicit type was provided
                    if (string.IsNullOrEmpty(typeName))
                        typeName = dottedTypeFilter;
                    break;
                }
            }

            // Parse Name:N shorthand for explicit overload selection
            int? shorthandIndex = null;
            for (int i = 0; i < allMembers.Length; i++)
            {
                var colonIdx = allMembers[i].LastIndexOf(':');
                if (colonIdx > 0 && int.TryParse(allMembers[i][(colonIdx + 1)..], out var idx))
                {
                    allMembers[i] = allMembers[i][..colonIdx];
                    shorthandIndex = idx;
                }
            }
            // Note: We don't auto-select overload 1 when a single member is filtered.
            // This allows seeing all overloads when e.g. `-m GetValue` matches multiple.
            // Use explicit Name:1 syntax to select a specific overload.

            HashSet<string> memberFilter = [];
            int? memberLimit = null;
            if (ctorOnly)
            {
                memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" };
            }
            else if (allMembers.Length == 1 && int.TryParse(allMembers[0], out var mNum))
            {
                memberLimit = mNum;
                shorthandIndex = null;
            }
            else if (allMembers.Length > 0)
            {
                memberFilter = new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase);
            }

            // Determine docs behavior: --no-docs suppresses, --docs enables, default is on
            bool showDocs = !parseResult.GetValue(noDocsOption);
            bool docsExplicitlySet = parseResult.GetResult(docsOption) is { Implicit: false }
                || parseResult.GetResult(noDocsOption) is { Implicit: false }
                || parseResult.GetResult(useLocalDocsOption) is { Implicit: false };

            // If --docs is explicitly set, honor it (overrides --no-docs precedence)
            if (parseResult.GetResult(docsOption) is { Implicit: false })
                showDocs = true;

            var options = new ApiOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = source.AssemblyPath,
                PlatformAssembly = source.PlatformAssembly,
                PlatformFramework = apiFrameworkOverride ?? parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                MemberFilter = memberFilter,
                Limit = memberLimit,
                ShowDocs = showDocs || parseResult.GetValue(useLocalDocsOption),
                DocsExplicitlySet = docsExplicitlySet,
                UseLocalDocs = parseResult.GetValue(useLocalDocsOption),
                ShowSamples = parseResult.GetValue(samplesOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                CtorOnly = ctorOnly,
                OverloadIndex = parseResult.GetValue(indexOption) ?? shorthandIndex,
                ParamTypes = parseResult.GetValue(paramsOption)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                FirstParamType = parseResult.GetValue(ofOption),
                ShowSelect = parseResult.GetValue(selectOption),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                IsMemberCommand = true
            };

            options = options with
            {
                TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || memberLimit != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
            };

            return await MemberCommand.ExecuteAsync(options);
        });

        return memberCommand;
    }
}
