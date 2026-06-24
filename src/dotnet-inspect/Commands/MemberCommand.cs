using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects type members (docs on by default).
/// </summary>
public static class MemberCommand
{
    public const string Name = "member";

    public static async Task<int> ExecuteAsync(MemberOptions options)
    {
        if (options.ShowMemberIndex)
            options = options with { Select = [.. options.Select ?? [], SectionNames.MemberIndex] };

        // Validate that member command has a type argument
        if (string.IsNullOrEmpty(options.TypeName))
        {
            if (await TryExecuteFindIfMissAsync(options) is { } findIfMissExitCode)
                return findIfMissExitCode;

            Console.Error.WriteLine("Error: member requires a type name.");
            Console.Error.WriteLine("Usage: dotnet-inspect member <type> --package <pkg>");
            Console.Error.WriteLine("   or: dotnet-inspect member -m Type.Member --package <pkg>");
            NamespacePrefixHints.WriteIfLikelyNamespacePrefix(options.PackagePath ?? options.PlatformAssembly ?? "");
            return 1;
        }

        // Shared preamble: section validation, discovery, verbosity promotion
        var (preamble, error) = ApiCommand.RunPreamble(options);
        if (error.HasValue) return error.Value;

        options = (MemberOptions)preamble.Options;
        var memberPipeline = preamble.MemberPipeline;

        var (source, sourceError) = await ApiSourceResolver.ResolveAsync(options);
        if (sourceError.HasValue) return sourceError.Value;

        var searchPath = source.SearchPath;
        var runtimeAssemblyPath = source.RuntimeAssemblyPath;
        var packageName = source.PackageName;
        var packageVersion = source.PackageVersion;
        var apiSource = source.ApiSource;
        var selectedTfm = source.SelectedTfm;
        var tempDir = source.TempDir;
        var typeName = source.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        try
        {
            var loaded = ApiServices.LoadFullApi(
                searchPath, runtimeAssemblyPath, options.PackagePath, packageName,
                apiSource, source.ApiVersion, selectedTfm, logger, options.IncludeAll);
            if (loaded == null)
            {
                Console.Error.WriteLine("Error: Could not extract API from library.");
                return 1;
            }

            var api = loaded.Api;
            var apiDllPath = loaded.ApiDllPath;
            var pdbLookupPath = loaded.PdbLookupPath;

            var lookupResult = ApiTypeLookupService.LookupType(api, typeName!);
            if (!lookupResult.Found)
            {
                lookupResult.WriteNotFoundError(Console.Error);
                return 1;
            }

            var apiType = lookupResult.Type!;

            // Check each member filter before producing output
            if (options.MemberFilter.Count > 0)
            {
                var memberValidation = ApiTypeLookupService.ValidateMemberFilters(apiType, options.MemberFilter);
                if (!memberValidation.IsValid)
                {
                    // The ranking/graph surfaces walk the full IL index and surface non-public
                    // members; member selection hides them without --all. If a missed filter
                    // would match a non-public member, hint at --all instead of dead-ending.
                    if (!options.IncludeAll && apiDllPath is { } dllForHint)
                    {
                        var allMemberNames = AssemblyReader.ExtractApiSurface(dllForHint, includeAll: true)?
                            .Types.FirstOrDefault(t => t.FullName == apiType.FullName)?
                            .Members.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (allMemberNames is { Count: > 0 })
                        {
                            var nonPublic = ApiTypeLookupService.FindNonPublicMatches(
                                memberValidation.MissedFilters, allMemberNames);
                            if (nonPublic.Count > 0)
                                memberValidation = memberValidation with { NonPublicMatches = nonPublic };
                        }
                    }

                    memberValidation.WriteError(Console.Error);
                    return 1;
                }
            }

            var foundIn = apiDllPath != null ? Path.GetFileNameWithoutExtension(apiDllPath) : null;

            // Default --docs on for single-type view at Normal+ unless explicitly disabled
            MemberOptions effectiveOptions = options;
            if (!options.DocsExplicitlySet && options.Verbosity >= Verbosity.Normal)
                effectiveOptions = options with { ShowDocs = true };
            if (effectiveOptions.HasCallerScope)
                effectiveOptions = IncludeCallersSection(effectiveOptions);

            // Keep member-name lookups as overload inventories. Only auto-select the lone
            // overload when the user explicitly asks for a selected-overload detail section.
            // A Name~digest selector resolves its own overload below, so skip auto-select
            // here to avoid a spurious "digest cannot be combined with --index" conflict.
            if (!effectiveOptions.OverloadIndex.HasValue
                && string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest)
                && ShouldAutoSelectSingleOverload(effectiveOptions))
            {
                var autoMemberName = effectiveOptions.MemberFilter.First();
                var autoOverloads = GetCandidateMembers(apiType, effectiveOptions, autoMemberName);
                if (autoOverloads.Count == 1)
                    effectiveOptions = effectiveOptions with { OverloadIndex = 1 };
            }

            var digestSelected = false;

            // Name~digest: select a specific overload by canonical member digest.
            if (!string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest))
            {
                if (effectiveOptions.OverloadIndex.HasValue)
                {
                    Console.Error.WriteLine("Error: digest selector cannot be combined with --index/Name:N.");
                    return 1;
                }

                if (effectiveOptions.MemberFilter.Count != 1)
                {
                    Console.Error.WriteLine("Error: Name~digest requires exactly one member name.");
                    return 1;
                }

                var memberName = effectiveOptions.MemberFilter.First();
                var overloads = GetCandidateMembers(apiType, effectiveOptions, memberName);
                var displayOverloads = overloads
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ThenBy(ApiOutputFormatter.GetMemberSignatureSortKey, StringComparer.Ordinal)
                    .ToList();
                var rows = ApiOutputFormatter.BuildMemberIndexRows(apiType, displayOverloads);
                var matches = rows
                    .Select((row, index) => (row, index))
                    .Where(item => item.row.Digest.StartsWith(effectiveOptions.MemberDigest, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matches.Count == 0)
                {
                    Console.Error.WriteLine($"Error: No overload of {memberName} matches digest '{effectiveOptions.MemberDigest}'. Use -S \"Member Index\" to list digests.");
                    return 1;
                }

                if (matches.Count > 1)
                {
                    Console.Error.WriteLine($"Error: Digest '{effectiveOptions.MemberDigest}' is ambiguous. Use a longer digest prefix:");
                    foreach (var (row, _) in matches)
                        Console.Error.WriteLine($"  {row.Stable}  {row.CanonicalSignature}");
                    return 1;
                }

                var selected = displayOverloads[matches[0].index];
                apiType.Members = [selected];
                var detailDllPath = apiType.SourceAssemblyPath ?? apiDllPath;
                effectiveOptions = effectiveOptions with
                {
                    DllPath = detailDllPath,
                    OverloadIndex = overloads.IndexOf(selected) + 1
                };
                digestSelected = true;
            }

            // --index: select a specific overload and show IL
            if (effectiveOptions.OverloadIndex.HasValue && !digestSelected)
            {
                if (effectiveOptions.MemberFilter.Count != 1)
                {
                    Console.Error.WriteLine("Error: --index/Name:N requires exactly one member name.");
                    return 1;
                }

                var memberName = effectiveOptions.MemberFilter.First();
                var overloads = GetCandidateMembers(apiType, effectiveOptions, memberName);
                var displayOverloads = overloads
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ThenBy(ApiOutputFormatter.GetMemberSignatureSortKey, StringComparer.Ordinal)
                    .ToList();

                int idx = effectiveOptions.OverloadIndex.Value;
                if (idx < 1 || idx > overloads.Count)
                {
                    Console.Error.WriteLine($"Error: {memberName}:{idx} is out of range. Use {memberName}:1 through {memberName}:{overloads.Count}.");
                    return 1;
                }

                var selected = displayOverloads[idx - 1];
                apiType.Members = [selected];
                var detailDllPath = apiType.SourceAssemblyPath ?? apiDllPath;
                effectiveOptions = effectiveOptions with
                {
                    DllPath = detailDllPath,
                    OverloadIndex = overloads.IndexOf(selected) + 1
                };
            }

            if (effectiveOptions.OverloadIndex is null
                && TryGetSelectedSingleOverloadSections(effectiveOptions, out var singleOverloadSections))
            {
                var memberName = effectiveOptions.MemberFilter.First();
                var overloads = GetCandidateMembers(apiType, effectiveOptions, memberName);
                if (overloads.Count > 1)
                {
                    var sectionLabel = singleOverloadSections.Count == 1
                        ? $"section '{singleOverloadSections[0]}' requires"
                        : $"sections {string.Join(", ", singleOverloadSections.Select(section => $"'{section}'"))} require";
                    Console.Error.WriteLine($"Error: {sectionLabel} a single selected overload for member '{memberName}'.");
                    Console.Error.WriteLine($"Use {memberName}:1 through {memberName}:{overloads.Count}, or run -S \"Member Index\" to list stable ~digest selectors.");
                    return 1;
                }
            }

            if (effectiveOptions.OverloadIndex is null
                && effectiveOptions.IncludeSections?.Contains(SectionNames.UnsafeMembers) == true
                && (runtimeAssemblyPath ?? apiDllPath) is { } unsafeDllPath)
            {
                effectiveOptions = effectiveOptions with { DllPath = unsafeDllPath };
            }

            // Enrich with local XML docs only (source info is in the source command)
            {
                var dllPath = runtimeAssemblyPath ?? apiDllPath;
                if (dllPath != null && effectiveOptions.ShowDocs)
                    SourceEnricher.EnrichFromLocalXmlDocs(apiType, dllPath, effectiveOptions, logger);
            }

            if (apiDllPath != null && NeedsMemberSourceLocationResolution(effectiveOptions))
            {
                var locationDllPath = apiType.SourceAssemblyPath ?? pdbLookupPath;
                var pdbPath = await MemberSourceLocationCollector.EnrichAsync(
                    apiType, locationDllPath, packageName, packageVersion,
                    effectiveOptions, context.HttpClient, logger);
                if (pdbPath != null)
                    effectiveOptions = effectiveOptions with { PdbPath = pdbPath };
            }

            // Resolve PDB/source only when selected detail sections need them.
            if (effectiveOptions.OverloadIndex.HasValue && apiDllPath != null
                && NeedsMemberSourceResolution(apiType, effectiveOptions))
            {
                bool fetchSource = ApiCommand.GetRequestedMemberSections(apiType, effectiveOptions)
                    .Contains(SectionNames.OriginalSource);
                var selectedMember = apiType.Members.Count == 1 ? apiType.Members[0] : null;
                var sourceTypeName = selectedMember?.DeclaringType ?? apiType.FullName;
                var sourceOverloadIndex = (selectedMember?.DeclaringOverloadIndex ?? effectiveOptions.OverloadIndex.Value) - 1;
                var publicOnly = selectedMember?.Kind != "explicit-interface-implementation";
                var resolved = await ApiCommand.ResolveMethodSourceAsync(
                    pdbLookupPath, sourceTypeName,
                    effectiveOptions.MemberFilter.First(),
                    sourceOverloadIndex,
                    effectiveOptions, context.HttpClient, logger, fetchSource, publicOnly);

                effectiveOptions = effectiveOptions with
                {
                    MethodSource = resolved.Source,
                    PdbPath = resolved.PdbPath
                };
            }

            if (effectiveOptions.EffectiveDiscovery)
            {
                return ApiCommand.ExecuteEffectiveDiscovery(
                    apiType, ApiMemberSectionPipelines.Create(effectiveOptions), effectiveOptions);
            }

            // For caller-scope queries without a specific overload, ensure DllPath is set so we can
            // open the member's own assembly index for aggregated callers across all overloads.
            if (effectiveOptions.HasCallerScope && effectiveOptions.DllPath == null && apiDllPath != null)
            {
                effectiveOptions = effectiveOptions with { DllPath = apiDllPath };
            }

            // Cross-assembly Callers: expand --bin/--directory, --project, and --caller-package
            // into the assemblies to scan for inbound callers, in addition to the selected
            // member's own assembly. Works for a specific overload or all overloads of a member.
            if (effectiveOptions.HasCallerScope)
            {
                var tempDirs = new List<string>();
                try
                {
                    var ownAssembly = effectiveOptions.DllPath ?? runtimeAssemblyPath ?? apiDllPath;
                    var scopeAssemblies = await CallerScopeResolver.ResolveAsync(
                        effectiveOptions.CallerScopeDirectories,
                        effectiveOptions.CallerScopeProjects,
                        effectiveOptions.CallerScopePackages,
                        effectiveOptions.Tfm,
                        ownAssembly,
                        context.HttpClient,
                        tempDirs,
                        logger);

                    // Supplying a caller scope is an explicit request for the Callers section, so it
                    // renders (with an empty-state note when nothing matches) even at low verbosity.
                    effectiveOptions = effectiveOptions with
                    {
                        CallerScopeAssemblies = scopeAssemblies,
                        IncludeSections = IncludeCallersSection(effectiveOptions).IncludeSections
                    };
                }
                finally
                {
                    // Clean up temp directories from package downloads
                    foreach (var dir in tempDirs)
                    {
                        try
                        {
                            if (Directory.Exists(dir))
                                Directory.Delete(dir, recursive: true);
                        }
                        catch
                        {
                            // Best-effort cleanup
                        }
                    }
                }
            }

            var projectionSections = effectiveOptions.IncludeSections;
            if (projectionSections is null && ApiOutputFormatter.ShouldRenderSectionedTabularView(apiType, effectiveOptions))
            {
                var grouped = ApiOutputFormatter.GroupMembersByKind(
                    apiType, effectiveOptions.MemberFilter, effectiveOptions.UnsafeOnly, effectiveOptions.KindFilter);
                if (grouped.Count == 1)
                    projectionSections = [GetMemberSectionName(grouped.Keys.Single())];
            }

            if ((effectiveOptions.Fields is { Length: > 0 } || effectiveOptions.Columns is { Length: > 0 })
                && projectionSections is { Count: > 0 })
            {
                var schema = ApiCommand.ToQueryableSchema(
                    ApiCommand.GetTypeDocumentSchema(effectiveOptions),
                    effectiveOptions);
                if (!ProjectionDiagnostics.ValidateProjection(schema, projectionSections, effectiveOptions.Fields, effectiveOptions.Columns))
                    return 1;
            }

            var writeExitCode = ApiCommand.WriteTypeOutput(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions);
            if (writeExitCode != 0)
                return writeExitCode;

            if (!effectiveOptions.FormatExplicitlySet && !effectiveOptions.IsRawOutput && effectiveOptions.OverloadIndex == null)
            {
                var sourceFlag = !string.IsNullOrEmpty(options.PlatformAssembly) ? $"--platform {options.PlatformAssembly}"
                    : !string.IsNullOrEmpty(options.PackagePath) ? $"--package {packageName ?? options.PackagePath}"
                    : !string.IsNullOrEmpty(options.AssemblyPath) ? $"--library {options.AssemblyPath}"
                    : "";

                var simpleName = TypeMatcher.GetSimpleName(apiType.FullName);

                var overloadGroups = apiType.Members
                    .Where(ApiMemberSectionDescriptors.IsMethodLike)
                    .GroupBy(m => m.Name)
                    .OrderByDescending(g => g.Count())
                    .ToList();
                var exampleGroup = overloadGroups.FirstOrDefault();

                List<Tip> tips = [];

                if (exampleGroup != null)
                {
                    var memberName = exampleGroup.Key == ".ctor" ? ".ctor" : exampleGroup.Key;
                    tips.Add(new(Name, $"{simpleName} {sourceFlag} {memberName}:1", "view member detail (source, IL)"));
                }

                if (overloadGroups.Any(g => g.Count() > 1))
                    tips.Add(new(Name, $"{simpleName} {sourceFlag} --show-index", "show member selectors"));

                tips.Add(new(TypeCommand.Name, $"{simpleName} {sourceFlag} --shape", "view type shape"));
                tips.Add(new(Name, $"-m {simpleName}.{(exampleGroup?.Key ?? "Method")} {sourceFlag}", "dotted member syntax"));

                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                    tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..{packageVersion} -t {simpleName}", "compare API changes"));

                Hints.WriteTips(effectiveOptions.TipLevel, [.. tips]);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static async Task<int?> TryExecuteFindIfMissAsync(MemberOptions options)
    {
        if (options.PackagePath == null || options.AssemblyPath != null || options.PlatformAssembly != null)
            return null;

        var context = new CommandContext(options.Verbose);
        if (options.MemberFilter.Count == 0 && !options.CtorOnly)
        {
            var memberResolution = await TypeFindIfMissResolver.ResolvePlatformMemberAsync(
                options.PackagePath,
                options.IncludeAll,
                options.SourceOptions,
                context.HttpClient,
                context.Logger);

            if (memberResolution.Status == TypeFindIfMissStatus.Found)
                return await ExecuteAsync(memberResolution.ApplyTo(options));
            if (memberResolution.Status == TypeFindIfMissStatus.Ambiguous)
                return memberResolution.WriteAmbiguousError();
        }

        var resolution = await TypeFindIfMissResolver.ResolvePlatformAsync(
            options.PackagePath,
            options.IncludeAll,
            options.SourceOptions,
            context.HttpClient,
            context.Logger);

        return resolution.Status switch
        {
            TypeFindIfMissStatus.Found => await ExecuteAsync(resolution.ApplyTo(options)),
            TypeFindIfMissStatus.Ambiguous => resolution.WriteAmbiguousError(),
            _ => null
        };
    }

    private static bool ShouldAutoSelectSingleOverload(MemberOptions options)
    {
        if (options.MemberFilter.Count != 1)
            return false;
        if (!TryGetSelectedSingleOverloadSections(options, out _))
            return false;
        return true;
    }

    private static bool TryGetSelectedSingleOverloadSections(MemberOptions options, out List<string> sections)
    {
        sections = [];
        if (options.MemberFilter.Count != 1)
            return false;
        if (options.IncludeSections is not { Count: > 0 } includeSections)
            return false;
        if (IsPureSelector(options.Select, SelectResolver.InfoSelector)
            || IsPureSelector(options.Select, SelectResolver.AllSelector))
            return false;

        sections = SingleOverloadSectionNames
            .Where(includeSections.Contains)
            .ToList();
        return sections.Count > 0;
    }

    private static MemberOptions IncludeCallersSection(MemberOptions options)
    {
        var includeSections = options.IncludeSections is { Count: > 0 } existing
            ? new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        includeSections.Add(SectionNames.Callers);
        return options with { IncludeSections = includeSections };
    }

    private static readonly string[] SingleOverloadSectionNames =
    [
        SectionNames.Signature,
        SectionNames.CustomAttributes,
        SectionNames.DecompiledSource,
        SectionNames.AnnotatedSource,
        SectionNames.OriginalSource,
        SectionNames.Calls,
        SectionNames.Callers,
        SectionNames.CallGraph,
        SectionNames.CallerGraph,
        SectionNames.UnsafeOperations,
        SectionNames.OptimizationOpportunities,
        SectionNames.Facts,
        SectionNames.IL
    ];

    private static bool IsPureSelector(string[]? select, string name) =>
        select is { Length: 1 } && select[0].Equals(name, StringComparison.OrdinalIgnoreCase);

    private static List<ApiMember> GetCandidateMembers(ApiType apiType, MemberOptions options, string memberName)
    {
        var members = apiType.Members
            .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));
        if (options.KindFilter.Count > 0)
            members = members.Where(m => options.KindFilter.Contains(m.Kind));
        return members.ToList();
    }

    private static string GetMemberSectionName(string kind) => kind switch
    {
        "constructor" => SectionNames.Constructors,
        "field" => SectionNames.Fields,
        "property" => SectionNames.Properties,
        "method" => SectionNames.Methods,
        "operator" => SectionNames.Operators,
        "explicit-interface-implementation" => SectionNames.ExplicitInterfaceImplementations,
        "extension-method" => SectionNames.ExtensionMethods,
        "event" => SectionNames.Events,
        _ => kind
    };

    private static bool NeedsMemberSourceResolution(ApiType apiType, MemberOptions options)
    {
        var sections = ApiCommand.GetRequestedMemberSections(apiType, options);
        if (sections.Contains(SectionNames.OriginalSource))
            return true;

        bool pdbAuthorized = options.IncludeSections is { Count: > 0 }
                             || options.Verbosity >= Verbosity.Detailed;
        return pdbAuthorized
               && (sections.Contains(SectionNames.DecompiledSource)
                   || sections.Contains(SectionNames.AnnotatedSource)
                   || sections.Contains(SectionNames.Facts));
    }

    private static bool NeedsMemberSourceLocationResolution(MemberOptions options)
        => options.IncludeSections?.Contains(SectionNames.SourceLocations) == true;
}
