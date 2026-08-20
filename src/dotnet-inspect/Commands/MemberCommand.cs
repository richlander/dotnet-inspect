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
        // Validate that member command has a type argument
        if (string.IsNullOrEmpty(options.TypeName))
        {
            if (await TryExecuteFindIfMissAsync(options) is { } findIfMissExitCode)
                return findIfMissExitCode;

            CommandError.Write("member requires a type name.");
            CommandError.WriteLine("Usage: dotnet-inspect member <type> --package <pkg>");
            CommandError.WriteLine("   or: dotnet-inspect member -m Type.Member --package <pkg>");
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
        var projectAssetsPath = source.ProjectAssetsPath;
        var tempDir = source.TempDir;
        CallerScopeAssemblySet? callerScopeAssemblySet = null;
        var typeName = source.TypeName;
        var context = source.Context;
        var logger = context.Logger;

        options = options with
        {
            PackagePath = source.ResolvedPackagePath,
            PackageRangeAddress = null,
            ProjectAssetsPath = projectAssetsPath,
        };

        try
        {
            var loaded = ApiServices.LoadFullApi(
                searchPath, runtimeAssemblyPath, options.PackagePath, packageName,
                apiSource, source.ApiVersion, selectedTfm, logger, options);
            if (loaded == null)
            {
                CommandError.Write("Could not extract API from library.");
                return 1;
            }

            var api = loaded.Api;
            var apiDllPath = loaded.ApiDllPath;
            var pdbLookupPath = loaded.PdbLookupPath;
            var lookupResult = ApiTypeLookupService.LookupType(api, typeName!);
            if (!lookupResult.Found)
            {
                lookupResult.WriteNotFoundError();
                return 1;
            }

            var apiType = lookupResult.Type!;

            // If the type resolved by peeling a trailing Type.Member suffix (e.g.
            // "System.String.Length" -> type System.String + member Length), apply the peeled
            // segment as a member filter. Selector-bearing suffixes like ":N"/"~hash" are split
            // in the parser, but generic arity can still arrive here from Type.M<T>.
            if (lookupResult.ImpliedMember is { } impliedMember)
            {
                var impliedSelector = MemberTargetSelector.Parse(impliedMember);
                var mergedFilter = new HashSet<string>(options.MemberFilter, StringComparer.OrdinalIgnoreCase)
                {
                    impliedSelector.Name
                };
                options = options with
                {
                    MemberFilter = mergedFilter,
                    MemberGenericArity = options.MemberGenericArity ?? impliedSelector.GenericArity
                };
            }
            else if (options.MemberFilter.Count == 0 && options.Select is { Length: > 0 })
            {
                var actualPipeline = ApiMemberSectionPipelines.Create(options);
                var actualSelect = SelectResolver.ResolveSelectAsSections(
                    options.Select,
                    actualPipeline.SelectableSectionNames,
                    actualPipeline.InfoSectionNames,
                    actualPipeline.GetCategoryMap(),
                    selectDefault: options.SelectDefault);
                if (SelectOutput.WriteUnresolved(actualSelect))
                    return 1;
                if (actualSelect.Sections != null)
                    options = options with { IncludeSections = actualSelect.Sections };
            }

            if (options.BodyKindQuery.HasFilter
                && (options.MemberFilter.Count != 1
                    || options.MemberFilter.Any(MemberFilterHasWildcard)))
            {
                CommandError.Write(
                    "--where Kind=... requires one exact member name or selector "
                    + "(for example, Name:1 or Name~digest).");
                return 1;
            }
            if (options.BodyKindQuery.HasFilter
                && options.IncludeSections is null
                && options.Discover is null)
            {
                options = options with
                {
                    IncludeSections = [SectionNames.BodyShapes],
                };
            }

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

                    memberValidation.WriteError();
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
                {
                    if (effectiveOptions.BodyKindQuery.HasFilter
                        && BodyAccessorCount(autoOverloads[0]) > 1)
                    {
                        WriteAccessorSelectionRequired(
                            apiType,
                            autoOverloads[0]);
                        return 1;
                    }
                    effectiveOptions = effectiveOptions with { OverloadIndex = 1 };
                }
            }

            if (effectiveOptions.OverloadIndex.HasValue
                || !string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest))
            {
                if (effectiveOptions.MemberFilter.Count != 1)
                {
                    CommandError.Write(string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest)
                        ? "--index/Name:N requires exactly one member name."
                        : "Name~digest requires exactly one member name.");
                    return 1;
                }

                var memberName = effectiveOptions.MemberFilter.First();
                var selector = new MemberTargetSelector(
                    memberName,
                    memberName,
                    effectiveOptions.OverloadIndex,
                    effectiveOptions.MemberDigest,
                    GenericArity: effectiveOptions.MemberGenericArity);
                var memberResolution = MemberTargetResolver.Resolve(apiType, selector, effectiveOptions.KindFilter);
                if (memberResolution.Diagnostic is { } diagnostic)
                {
                    CommandError.Write(diagnostic.Message, [.. diagnostic.CandidateDetails()]);
                    return 1;
                }

                var target = memberResolution.Target!;
                var selected = target.ApiMember.Member;
                bool explicitAccessorSelector =
                    target.Kind is MemberTargetKind.Property or MemberTargetKind.Event
                    && target.OverloadIndex.HasValue
                    && (target.DigestPrefix is not null
                        || memberResolution.Candidates.Count == 1);
                if (effectiveOptions.BodyKindQuery.HasFilter
                    && BodyAccessorCount(selected) > 1
                    && !explicitAccessorSelector)
                {
                    WriteAccessorSelectionRequired(apiType, selected);
                    return 1;
                }
                apiType.DeclaringTypeMembers ??= apiType.Members;
                apiType.Members = [selected];
                apiType.SelectedAccessorOrdinal =
                    target.Kind is MemberTargetKind.Property or MemberTargetKind.Event
                        ? explicitAccessorSelector
                            ? target.OverloadIndex
                            : 1
                        : null;
                var detailDllPath = apiType.SourceAssemblyPath ?? apiDllPath;
                effectiveOptions = effectiveOptions with
                {
                    DllPath = detailDllPath,
                    OverloadIndex = target.Body?.DeclaringOverloadIndex ?? target.DeclaringOverloadIndex
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
                    CommandError.Write($"{sectionLabel} a single selected overload for member '{memberName}'.");
                    CommandError.WriteLine($"Select one overload with {memberName}~<digest> (shown in the Digest column of the member listing), or positionally with {memberName}:1 through {memberName}:{overloads.Count}.");
                    return 1;
                }
            }

            if (effectiveOptions.OverloadIndex is null
                && string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest)
                && effectiveOptions.MemberGenericArity.HasValue
                && effectiveOptions.MemberFilter.Count == 1)
            {
                var memberName = effectiveOptions.MemberFilter.First();
                var arityCandidateTargets = GetTargetCandidates(apiType, effectiveOptions, memberName);
                var unfilteredSelectorIndices = GetTargetCandidates(
                        apiType,
                        effectiveOptions with { MemberGenericArity = null },
                        memberName)
                    .ToDictionary(candidate => candidate.Member, candidate => candidate.SelectorIndex);
                var arityCandidates = arityCandidateTargets.Select(candidate =>
                {
                    if (unfilteredSelectorIndices.TryGetValue(candidate.Member, out var selectorIndex))
                        candidate.Member.SelectorOverloadIndex = selectorIndex;
                    return candidate.Member;
                }).ToList();
                if (arityCandidates.Count == 0)
                {
                    CommandError.Write($"No members matched selector '{memberName}' with generic arity {effectiveOptions.MemberGenericArity.Value}.");
                    return 1;
                }

                apiType.DeclaringTypeMembers ??= apiType.Members;
                apiType.Members = arityCandidates;
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

            if (RejectBodylessFactsRequest(apiType, ref effectiveOptions))
                return 1;

            if (RejectBodylessBodyShapesRequest(apiType, effectiveOptions))
                return 1;

            bool selectedMemberDefinitelyHasNoBody =
                SelectedMemberDefinitelyHasNoBody(apiType, effectiveOptions);
            if (selectedMemberDefinitelyHasNoBody)
                effectiveOptions = effectiveOptions with { MemberHasNoBody = true };

            if (apiDllPath != null
                && !selectedMemberDefinitelyHasNoBody
                && NeedsMemberSourceLocationResolution(effectiveOptions))
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
                    .Overlaps([SectionNames.OriginalSource, SectionNames.SourceDiff]);
                var selectedMember = apiType.Members.Count == 1 ? apiType.Members[0] : null;
                // A property/event (including an indexer) has no body of its own: its authored
                // source lives in the accessor the selected ordinal addresses, so resolve by that
                // accessor's name and MethodDef token rather than the property's name and absent
                // token, which would otherwise resolve nothing (issue #3278).
                var sourceAccessor = ResolveSourceAccessor(apiType, selectedMember, effectiveOptions.OverloadIndex);
                var sourceMember = sourceAccessor ?? selectedMember;
                var sourceTypeName = sourceMember?.DeclaringType ?? apiType.FullName;
                // Accessor names are unique within their declaring type, so the name fallback
                // (used only when the token cannot be trusted) addresses the first match.
                var sourceOverloadIndex = sourceAccessor is not null
                    ? 0
                    : (selectedMember?.DeclaringOverloadIndex ?? effectiveOptions.OverloadIndex.Value) - 1;
                // A directly-requested single member (name + overload) is already explicitly named
                // by the caller. When non-public members are in scope (--all), honor that request
                // for Original Source / Source Diff regardless of accessibility; member inventories
                // keep the public-only default. Explicit interface implementations stay resolvable.
                var directRequest = selectedMember != null && effectiveOptions.IncludeAll;
                var publicOnly = !directRequest
                    && selectedMember?.Kind is not ("explicit-interface-implementation" or "finalizer");
                // The selected member's metadata token indexes the assembly it
                // was extracted from — apiType.SourceAssemblyPath (the target
                // assembly for a forwarded type, otherwise the extraction dll).
                // Only resolve source by token when the assembly opened for
                // lookup (pdbLookupPath) IS that same assembly; otherwise the
                // token's row would not align (forwarded facade, or a reference
                // assembly for the surface vs an implementation assembly for
                // bodies), so fall back to name/overload resolution.
                var tokenOriginAssembly = apiType.SourceAssemblyPath ?? apiDllPath;
                var sourceMetadataToken = string.Equals(pdbLookupPath, tokenOriginAssembly, StringComparison.Ordinal)
                    ? (sourceMember?.MetadataToken ?? 0)
                    : 0;
                var resolved = sourceAccessor is { HasMethodBody: false }
                    ? new ApiCommand.ResolvedMethodSource(
                        Source: null,
                        PdbPath: effectiveOptions.PdbPath,
                        MemberHasNoBody: true)
                    : await ApiCommand.ResolveMethodSourceAsync(
                        pdbLookupPath, sourceTypeName,
                        sourceMember?.Name ?? effectiveOptions.MemberFilter.First(),
                        sourceOverloadIndex,
                        effectiveOptions, context.HttpClient, logger, fetchSource, publicOnly,
                        sourceMetadataToken);

                effectiveOptions = effectiveOptions with
                {
                    MethodSource = resolved.Source,
                    MemberHasNoBody = resolved.MemberHasNoBody,
                    MemberHasNoAuthoredDeclaration = resolved.MemberHasNoAuthoredDeclaration,
                    MemberSourceTooComplex = resolved.MemberSourceTooComplex,
                    MemberSourceCoordinatesInvalid = resolved.MemberSourceCoordinatesInvalid,
                    PdbPath = resolved.PdbPath
                };
            }

            if (effectiveOptions.EffectiveDiscovery)
            {
                if (!effectiveOptions.BodyKindQuery.HasFilter
                    && ApiCommand.TargetsBodyShapes(
                        effectiveOptions,
                        effectiveOptions.Discover))
                {
                    CommandError.Write(
                        "Section 'Body Shapes' requires --where "
                        + "\"Kind=<C# Body Kinds ID>\".");
                    return 1;
                }
                return ApiCommand.ExecuteEffectiveDiscovery(
                    apiType, ApiMemberSectionPipelines.Create(effectiveOptions), effectiveOptions,
                    new ApiCommand.TypeAcquisitionContext(
                        foundIn, packageName, packageVersion, apiSource, selectedTfm));
            }

            // For caller-scope queries without a specific overload, ensure DllPath is set so we can
            // open the member's own assembly index for aggregated callers across all overloads.
            if (effectiveOptions.HasCallerScope && effectiveOptions.DllPath == null && apiDllPath != null)
            {
                effectiveOptions = effectiveOptions with { DllPath = apiDllPath };
            }

            // Expand --bin/--directory, --project, and --caller-package into assemblies
            // for cross-assembly callers and Call Graph traversal, in addition to the
            // selected member's own assembly.
            if (effectiveOptions.HasCallerScope)
            {
                var ownAssembly = effectiveOptions.DllPath ?? runtimeAssemblyPath ?? apiDllPath;
                callerScopeAssemblySet = await CallerScopeResolver.ResolveAsync(
                    effectiveOptions.CallerScopeDirectories,
                    effectiveOptions.CallerScopeProjects,
                    effectiveOptions.CallerScopePackages,
                    effectiveOptions.Tfm,
                    ownAssembly,
                    context.HttpClient,
                    logger);

                // Supplying a caller scope is an explicit request for the Callers section, so it
                // renders (with an empty-state note when nothing matches) even at low verbosity.
                effectiveOptions = effectiveOptions with
                {
                    CallerScopeAssemblies = callerScopeAssemblySet.Assemblies,
                    IncludeSections = IncludeCallersSection(effectiveOptions).IncludeSections
                };
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

            int selectedSurfaceExitCode =
                ApiCommand.WriteSelectedSurfaceDiagnostics(
                api,
                apiType,
                effectiveOptions.MemberFilter);
            var writeExitCode = await ApiCommand.WriteTypeOutputAsync(apiType, foundIn, packageName, packageVersion, apiSource, selectedTfm, effectiveOptions);
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
                    tips.Add(new(Name, $"{simpleName} {sourceFlag} -S \"Member Index\"", "full selector/identity table"));

                tips.Add(new(TypeCommand.Name, $"{simpleName} {sourceFlag} --shape", "view type shape"));
                tips.Add(new(Name, $"-m {simpleName}.{(exampleGroup?.Key ?? "Method")} {sourceFlag}", "dotted member syntax"));

                if (!string.IsNullOrEmpty(packageName) && !string.IsNullOrEmpty(packageVersion))
                    tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..{packageVersion} -t {simpleName}", "compare API changes"));

                Hints.WriteTips(effectiveOptions.TipLevel, [.. tips]);
            }

            return selectedSurfaceExitCode;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }

            callerScopeAssemblySet?.Dispose();
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

    private static bool MemberFilterHasWildcard(string filter)
        => filter.Contains('*', StringComparison.Ordinal)
           || filter.Contains('?', StringComparison.Ordinal);

    private static int BodyAccessorCount(ApiMember member)
        => member.Kind switch
        {
            "property" => (member.GetterToken.HasValue ? 1 : 0)
                + (member.SetterToken.HasValue ? 1 : 0),
            "event" => (member.AdderToken.HasValue ? 1 : 0)
                + (member.RemoverToken.HasValue ? 1 : 0),
            _ => 0,
        };

    private static void WriteAccessorSelectionRequired(
        ApiType type,
        ApiMember member)
    {
        var stable = ApiMemberIdentity
            .GetMemberAnchor(type, member)
            .StableSelector;
        int count = BodyAccessorCount(member);
        CommandError.Write(
            $"Member '{member.Name}' has {count} body accessors. "
                + $"Select one with {stable}:1 through {stable}:{count}.");
    }

    private static bool TryGetSelectedSingleOverloadSections(MemberOptions options, out List<string> sections)
    {
        sections = [];
        if (options.MemberFilter.Count != 1)
            return false;
        if (options.BodyKindQuery.HasFilter)
        {
            sections = [SectionNames.BodyShapes];
            return true;
        }
        if (options.IncludeSections is not { Count: > 0 } includeSections)
            return false;
        // Bare -S carries no selector value, so it cannot be recognized by inspecting Select.
        if ((options.SelectDefault && options.Select is null)
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
        SectionNames.FidelityCauses,
        SectionNames.AppliedTaste,
        SectionNames.AnnotatedSource,
        SectionNames.AnnotatedSourceDocument,
        SectionNames.OriginalSource,
        SectionNames.SourceDiff,
        SectionNames.Calls,
        SectionNames.ExceptionRegions,
        SectionNames.AllocationFacts,
        SectionNames.SafetyFacts,
        SectionNames.CostFacts,
        SectionNames.Callers,
        SectionNames.CallGraph,
        SectionNames.UnsafeOperations,
        SectionNames.BodyShapes,
        SectionNames.TopLeverage,
        SectionNames.PerformanceTriage,
        SectionNames.Facts,
        SectionNames.IL
    ];

    private static bool IsPureSelector(string[]? select, string name) =>
        select is { Length: 1 } && select[0].Equals(name, StringComparison.OrdinalIgnoreCase);

    private static List<ApiMember> GetCandidateMembers(ApiType apiType, MemberOptions options, string memberName)
        => GetTargetCandidates(apiType, options, memberName)
            .Select(candidate => candidate.Member)
            .ToList();

    private static IReadOnlyList<MemberTargetCandidate> GetTargetCandidates(ApiType apiType, MemberOptions options, string memberName)
        => MemberTargetResolver.GetCandidates(
                apiType,
                options.MemberGenericArity is { } arity
                    ? new MemberTargetSelector(memberName, memberName, GenericArity: arity)
                    : new MemberTargetSelector(memberName, memberName),
                options.KindFilter);

    private static string GetMemberSectionName(string kind) => kind switch
    {
        "constructor" => SectionNames.Constructors,
        "finalizer" => SectionNames.Finalizer,
        "field" => SectionNames.Fields,
        "property" => SectionNames.Properties,
        "method" => SectionNames.Methods,
        "operator" => SectionNames.Operators,
        "explicit-interface-implementation" => SectionNames.ExplicitInterfaceImplementations,
        "extension-method" => SectionNames.ExtensionMethods,
        "event" => SectionNames.Events,
        _ => kind
    };

    internal static bool NeedsMemberSourceResolution(ApiType apiType, MemberOptions options)
    {
        var requestedSections = ApiCommand.GetRequestedMemberSections(apiType, options);
        var sections = ApiOutputFormatter.ResolveExecutionSections(
            apiType,
            requestedSections,
            options.OverloadIndex);
        if (SelectedMemberDefinitelyHasNoBody(apiType, options))
            return false;

        if (sections.Overlaps([SectionNames.OriginalSource, SectionNames.SourceDiff]))
            return true;

        bool pdbAuthorized = options.IncludeSections is { Count: > 0 }
                             || options.Verbosity >= Verbosity.Detailed;
        return pdbAuthorized
               && (sections.Contains(SectionNames.DecompiledSource)
                   || sections.Contains(SectionNames.AnnotatedSource)
                   || sections.Contains(SectionNames.AnnotatedSourceDocument)
                   || sections.Contains(SectionNames.BodyShapes)
                   || sections.Contains(SectionNames.Facts));
    }

    /// <summary>
    /// Facts describes a decoded IL body, so a body target metadata positively reports as bodyless
    /// can never render it. Fail visibly when nothing else in the request renders, and otherwise
    /// drop only Facts, note the absent body, and keep every other requested section, so neither
    /// shape produces success-shaped empty output. An unknown body fact is not evidence of absence
    /// and stays eligible.
    /// </summary>
    private static bool RejectBodylessFactsRequest(
        ApiType apiType,
        ref MemberOptions options)
    {
        var requestedSections = ApiCommand.GetRequestedMemberSections(apiType, options);
        if (!requestedSections.Contains(SectionNames.Facts)
            || options.OverloadIndex is not { } ordinal
            || apiType.Members is not [{ } selected])
        {
            return false;
        }

        var accessor = ResolveSourceAccessor(apiType, selected, ordinal);
        if (!ApiMemberSectionDescriptors.DefinitelyHasNoBody(accessor ?? selected))
            return false;

        IReadOnlySet<string> renderableSections =
            ResolveBodylessRenderableSections(apiType, requestedSections, ordinal);
        bool rendersAbstractAccessorAnnotatedSource =
            ApiOutputFormatter.IsSelectedAbstractAccessor(apiType, ordinal)
            && renderableSections.Contains(SectionNames.AnnotatedSource);
        bool hasOtherRenderableSection = rendersAbstractAccessorAnnotatedSource
            || ApiMemberSectionPipelines.Create(options)
                .GetInspectionViews(apiType, includeInapplicable: true)
                .Any(view => renderableSections.Contains(view.Id)
                    && !view.Id.Equals(SectionNames.Facts, StringComparison.OrdinalIgnoreCase)
                    && view.CanRender)
            || HasBroadRawDocumentJsonSelection(options);
        string target = accessor is null ? "method" : "accessor";
        if (hasOtherRenderableSection)
        {
            CommandError.WriteNote($"The selected {target} has no IL body.");
            options = WithoutFactsSection(options);
            return false;
        }

        CommandError.Write($"The selected {target} has no IL body.");
        return true;
    }

    /// <summary>
    /// The requested sections that can still render for a selected body target metadata positively
    /// reports as bodyless. A selected property/event accessor is addressed through its owner, so
    /// the execution-section projection names the sections it still renders. A directly selected
    /// method addresses itself: while the body projection still carries it — a concrete
    /// extern/internal-call method — every requested body section keeps its normal behavior and
    /// reports the absent body, and once the projection drops it — an abstract declaration — no
    /// body section addresses it at all. The views that need no body target render either way.
    /// </summary>
    /// <remarks>
    /// This decision is scoped to the Facts policy above. It never filters the sections the
    /// renderer runs, so every section a direct method already rendered keeps rendering.
    /// </remarks>
    internal static IReadOnlySet<string> ResolveBodylessRenderableSections(
        ApiType apiType,
        IReadOnlySet<string> requestedSections,
        int ordinal)
    {
        var selected = apiType.Members is [{ } member] ? member : null;
        IReadOnlySet<string> bodyTargetSections =
            ResolveSourceAccessor(apiType, selected, ordinal) is not null
                ? ApiOutputFormatter.ResolveExecutionSections(apiType, requestedSections, ordinal)
                : ApiOutputFormatter.ResolveBodyMethods(apiType, requestedSections, ordinal).Count > 0
                    ? requestedSections
                    : EmptySections;

        HashSet<string> sections = new(bodyTargetSections, StringComparer.OrdinalIgnoreCase);
        if (ApiOutputFormatter.IsSelectedAbstractAccessor(apiType, ordinal)
            && requestedSections.Contains(SectionNames.AnnotatedSource))
        {
            // Abstract accessors render Annotated Source through the explicit absence path,
            // rather than entering the decompiler projection.
            sections.Add(SectionNames.AnnotatedSource);
        }
        sections.UnionWith(requestedSections.Where(SectionsThatNeedNoBodyTarget.Contains));
        return sections;
    }

    private static bool HasBroadRawDocumentJsonSelection(MemberOptions options)
        => options.JsonOutput
           && !options.Count
           && !options.Print
           && !options.Value
           && !options.Urls
           && !options.Paths
           && options.Select?.Any(selector =>
               selector.StartsWith('@')
               || selector.Contains('*')
               || selector.Contains('?')) == true;

    /// <summary>
    /// The sections that render for a member metadata positively reports as bodyless without
    /// needing a body-projection target. Signature and Source Locations read the declaration only.
    /// Original Source and Source Diff report the absence itself: the caller sets
    /// <see cref="MemberOptions.MemberHasNoBody"/> for this same member immediately after this
    /// decision, which is what <see cref="ApiCommand.OriginalSourceUnavailableNote"/> and the diff
    /// it feeds render instead of source text (issue #3299).
    /// </summary>
    private static readonly IReadOnlySet<string> SectionsThatNeedNoBodyTarget =
        new HashSet<string>(
            [
                SectionNames.Signature,
                SectionNames.SourceLocations,
                SectionNames.OriginalSource,
                SectionNames.SourceDiff,
                SectionNames.ExceptionRegions,
            ],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Drops only the Facts section from an explicit selection. The body stage asks Research for
    /// facts about a body that does not exist and fails the whole render, so the note path removes
    /// the one section it just reported as unrenderable and leaves every other selected section —
    /// including the ones that report the absent body — in place.
    /// </summary>
    private static MemberOptions WithoutFactsSection(MemberOptions options)
    {
        if (options.IncludeSections is not { Count: > 0 } sections)
            return options;

        HashSet<string> remaining = new(
            sections.Where(section =>
                !section.Equals(SectionNames.Facts, StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
        // An empty include set reads as "no explicit selection" and would restore the verbosity
        // defaults, so a selection of nothing but Facts keeps its original shape.
        if (remaining.Count is 0 || remaining.Count == sections.Count)
            return options;

        string[]? remainingSelectors = options.Select?
            .Where(selector =>
                !selector.Equals(SectionNames.Facts, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return options with
        {
            IncludeSections = remaining,
            Select = remainingSelectors is { Length: > 0 } ? remainingSelectors : null,
        };
    }

    private static readonly IReadOnlySet<string> EmptySections =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static bool RejectBodylessBodyShapesRequest(
        ApiType apiType,
        MemberOptions options)
    {
        if (!options.BodyKindQuery.HasFilter
            || !SelectedMemberDefinitelyHasNoBody(apiType, options))
        {
            return false;
        }

        ApiMember? selected = apiType.Members is [{ } member] ? member : null;
        ApiMember? accessor = ResolveSourceAccessor(apiType, selected, options.OverloadIndex);
        CommandError.Write(
            $"The selected {(accessor is null ? "method" : "accessor")} has no IL body.");
        return true;
    }

    internal static bool SelectedMemberDefinitelyHasNoBody(
        ApiType apiType,
        MemberOptions options)
    {
        if (options.OverloadIndex is not { } ordinal
            || apiType.Members is not [{ } selected])
        {
            return false;
        }
        var bodyMember = ResolveSourceAccessor(apiType, selected, ordinal) ?? selected;
        return ApiMemberSectionDescriptors.DefinitelyHasNoBody(bodyMember);
    }

    private static bool NeedsMemberSourceLocationResolution(MemberOptions options)
        => options.IncludeSections?.Contains(SectionNames.SourceLocations) == true;

    /// <summary>
    /// The accessor method that carries a selected property's or event's authored source, or
    /// <see langword="null"/> when the selected member is already method-like (or is a field,
    /// which has no accessor). The accessor ordinal follows the same addressing the body
    /// sections use: 1 is the getter/adder and 2 the setter/remover, counting only accessors
    /// that exist (issue #3278).
    /// </summary>
    private static ApiMember? ResolveSourceAccessor(ApiType apiType, ApiMember? selected, int? accessorOrdinal)
    {
        if (selected is null
            || ApiMemberSectionDescriptors.IsMethodLike(selected)
            || !ApiMemberSectionDescriptors.HasAccessorTokens(selected))
        {
            return null;
        }

        var accessors = ApiOutputFormatter.AccessorMethods(selected, apiType).ToList();
        if (accessors.Count == 0)
            return null;

        var index = (accessorOrdinal ?? 1) - 1;
        return index >= 0 && index < accessors.Count ? accessors[index] : accessors[0];
    }
}
