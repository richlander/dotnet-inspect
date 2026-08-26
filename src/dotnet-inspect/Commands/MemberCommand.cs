using DotnetInspector.CommandLine;
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

        if (ApiCommand.RejectUniversallyInvalidMemberSelect(options))
            return 1;
        if (ApiCommand.RejectRouteIndependentOptionShape(options))
            return 1;

        var unresolvedOptions = options;
        if (!options.RouterDeferredTypeOrMember)
        {
            // Shared preamble: section validation, discovery, verbosity promotion
            var (preamble, error) = ApiCommand.RunPreamble(options);
            if (error.HasValue) return error.Value;
            options = (MemberOptions)preamble.Options;
        }
        else if (options.Discover != null
                 && !options.EffectiveDiscovery)
        {
            var (_, discoveryExitCode) = ApiCommand.RunPreamble(options);
            if (discoveryExitCode.HasValue)
                return discoveryExitCode.Value;

            throw new InvalidOperationException(
                "Static discovery did not produce an exit code.");
        }

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
                if (options.RouterDeferredTypeOrMember)
                {
                    return await ExecuteDeferredTypeAsync(
                        unresolvedOptions,
                        source,
                        loaded);
                }

                lookupResult.WriteNotFoundError();
                return 1;
            }

            var apiType = lookupResult.Type!;
            if (options.RouterDeferredTypeOrMember
                && lookupResult.ImpliedMember is null
                && DeferredExactTargetUsesTypePipeline(
                    apiType,
                    unresolvedOptions))
            {
                return await ExecuteDeferredTypeAsync(
                    unresolvedOptions,
                    source,
                    loaded);
            }

            if (lookupResult.ImpliedMember is { } impliedMember)
            {
                var mergeOptions = options.RouterDeferredTypeOrMember
                    ? unresolvedOptions
                    : options;
                var impliedSelector = MemberTargetSelector.Parse(impliedMember);
                if (mergeOptions.MemberGenericArity is { } explicitArity
                    && impliedSelector.GenericArity is { } impliedArity
                    && explicitArity != impliedArity)
                {
                    CommandError.Write("A member selection cannot combine different generic arities.");
                    return 1;
                }
                if (MemberOverloadSelectorsConflict(
                        mergeOptions,
                        impliedSelector))
                {
                    CommandError.Write(
                        "A member selection cannot combine different overload selectors.");
                    return 1;
                }

                var mergedFilter = new HashSet<string>(
                    mergeOptions.MemberFilter,
                    StringComparer.OrdinalIgnoreCase)
                {
                    impliedSelector.Name
                };
                var mergedArity =
                    mergeOptions.MemberGenericArity
                    ?? impliedSelector.GenericArity;
                var mergedKinds = new HashSet<string>(
                    mergeOptions.KindFilter,
                    StringComparer.OrdinalIgnoreCase);
                if (impliedSelector.Kind is { Length: > 0 } impliedKind)
                    mergedKinds.Add(impliedKind);
                if (mergedArity.HasValue && mergedFilter.Count != 1)
                {
                    CommandError.Write("A generic arity selector requires exactly one member name.");
                    return 1;
                }

                mergeOptions = mergeOptions with
                {
                    MemberFilter = mergedFilter,
                    KindFilter = mergedKinds,
                    MemberGenericArity = mergedArity,
                    OverloadIndex =
                        mergeOptions.OverloadIndex
                        ?? impliedSelector.OverloadIndex,
                    MemberDigest = MergeDigestPrefixes(
                        mergeOptions.MemberDigest,
                        impliedSelector.DigestPrefix)
                };
                if (options.RouterDeferredTypeOrMember)
                    unresolvedOptions = mergeOptions;
                else
                    options = mergeOptions;
            }

            if (options.RouterDeferredTypeOrMember)
            {
                unresolvedOptions = NormalizeResolvedTypeQualifiers(
                    unresolvedOptions,
                    apiType.FullName);
            }
            else
            {
                options = NormalizeResolvedTypeQualifiers(
                    options,
                    apiType.FullName);
            }

            if (options.RouterDeferredTypeOrMember)
            {
                if (options.ShapeExplicitlySet)
                {
                    CommandError.Write("--shape is only valid for type targets.");
                    return 1;
                }

                var (preamble, error) =
                    ApiCommand.RunPreamble(unresolvedOptions);
                if (error.HasValue) return error.Value;
                options = (MemberOptions)preamble.Options with
                {
                    PackagePath = source.ResolvedPackagePath,
                    PackageRangeAddress = null,
                    ProjectAssetsPath = projectAssetsPath,
                };
            }
            var memberPipeline = ApiMemberSectionPipelines.Create(options);

            // Structural discovery ignores -S. Once lookup has identified the actual member
            // pipeline, report that schema directly just as the non-dotted path does.
            if (options.Discover is not null && !options.EffectiveDiscovery)
            {
                return ApiCommand.ExecuteStructuralTypeDiscovery(options, memberPipeline);
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

            if (options.MemberSelectionDeferredToLookup)
            {
                if (ApiCommand.ReresolveSectionsForMemberLookup(options) is not { } resolved)
                    return 1;
                options = resolved;
                memberPipeline = ApiMemberSectionPipelines.Create(options);
            }
            else if (ApiCommand.RevalidateResolvedMemberSections(options, memberPipeline) is { } revalidated)
            {
                options = revalidated;
                if (options.MemberPipelineDeferredToLookup)
                {
                    if (ApiCommand.FinalizeResolvedMemberSelection(options, memberPipeline)
                        is not { } finalized)
                        return 1;
                    options = finalized;
                }
            }
            else
                return 1;

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
            var discoveredCallerSections =
                GetDiscoveredCallerSections(effectiveOptions);
            var callersImplicitlySelected = effectiveOptions.HasCallerScope
                && !IsWholeDocumentJson(effectiveOptions)
                && (!HasAuthoredSectionRequest(effectiveOptions)
                    || discoveredCallerSections.Count > 0);
            if (callersImplicitlySelected)
            {
                effectiveOptions = IncludeCallerScopeSections(
                    effectiveOptions,
                    discoveredCallerSections);
            }
            var authoredSelection = effectiveOptions;

            // Keep member-name lookups as overload inventories. Only auto-select the lone
            // overload when the user explicitly asks for a selected-overload detail section.
            // A Name~digest selector resolves its own overload below, so skip auto-select
            // here to avoid a spurious "digest cannot be combined with --index" conflict.
            if (!effectiveOptions.OverloadIndex.HasValue
                && string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest)
                && ShouldAutoSelectSingleOverload(authoredSelection))
            {
                var autoMemberName = effectiveOptions.MemberFilter.First();
                var autoCandidates = GetTargetCandidates(
                        apiType,
                        effectiveOptions,
                        autoMemberName)
                    .Where(candidate =>
                        !effectiveOptions.UnsafeOnly
                        || candidate.Member.IsUnsafe)
                    .ToList();
                if (autoCandidates.Count == 1)
                {
                    var autoCandidate = autoCandidates[0];
                    if (effectiveOptions.BodyKindQuery.HasFilter
                        && BodyAccessorCount(autoCandidate.Member) > 1)
                    {
                        WriteAccessorSelectionRequired(
                            apiType,
                            autoCandidate.Member);
                        return 1;
                    }
                    var inventoryPipeline = ApiMemberSectionPipelines.Create(authoredSelection);
                    if (ApiCommand.RevalidateResolvedMemberSections(
                            authoredSelection,
                            inventoryPipeline)
                        is not { } inventorySections)
                        return 1;

                    inventorySections = inventorySections with
                    {
                        OverloadIndex = autoCandidate.SelectorIndex,
                        AutoSelectedSingleOverload = true
                    };
                    var detailPipeline = ApiMemberSectionPipelines.Create(inventorySections);
                    if (ApiCommand.FinalizeResolvedMemberSelection(
                            inventorySections,
                            detailPipeline)
                        is not { } detailOptions)
                        return 1;
                    effectiveOptions = detailOptions;
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
                apiType.Members = [selected];
                selected.SelectorOverloadIndex = target.SelectorIndex;
                var detailDllPath = apiType.SourceAssemblyPath ?? apiDllPath;
                effectiveOptions = effectiveOptions with
                {
                    DllPath = detailDllPath,
                    OverloadIndex = target.Body?.DeclaringOverloadIndex ?? target.DeclaringOverloadIndex
                };
            }

            if (effectiveOptions.OverloadIndex is null
                && TryGetSelectedSingleOverloadSections(authoredSelection, out var singleOverloadSections))
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

            if (ApiMemberSectionPipelines.ShouldAggregateCallers(
                    apiType,
                    effectiveOptions))
            {
                var callerTokens = GetAggregatedCallerMembers(apiType, effectiveOptions)
                    .SelectMany(member => BodyMethodTokens(apiType, member))
                    .ToHashSet();
                effectiveOptions = effectiveOptions with
                {
                    AggregatedCallerMemberTokens = callerTokens
                };
            }
            else if (effectiveOptions.CallerScopeSectionImplicitlySelected
                && effectiveOptions.OverloadIndex is null
                && string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest))
            {
                effectiveOptions = ExcludeCallersSection(effectiveOptions);
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
                    .Overlaps([SectionNames.PdbSource, SectionNames.SourceDiff]);
                var selectedMember = apiType.Members.Count == 1 ? apiType.Members[0] : null;
                // A property/event (including an indexer) has no body of its own: its PDB source
                // is located through the accessor the selected ordinal addresses, so resolve by that
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
                // for PDB Source / Source Diff regardless of accessibility; member inventories
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
                var sourceMetadataToken = LibraryMetadataService
                    .ReferenceTreePathComparer(OperatingSystem.IsWindows())
                    .Equals(
                        Path.GetFullPath(pdbLookupPath),
                        Path.GetFullPath(tokenOriginAssembly))
                    ? (sourceMember?.MetadataToken ?? 0)
                    : 0;
                var resolved = await ApiCommand.ResolveMethodSourceAsync(
                    pdbLookupPath, sourceTypeName,
                    sourceMember?.Name ?? effectiveOptions.MemberFilter.First(),
                    sourceOverloadIndex,
                    effectiveOptions, context.HttpClient, logger, fetchSource, publicOnly,
                    sourceMetadataToken,
                    tokenOriginAssembly,
                    sourceMember?.MetadataToken ?? 0);

                effectiveOptions = effectiveOptions with
                {
                    MethodSource = resolved.Source,
                    MemberHasNoBody = resolved.MemberHasNoBody,
                    MemberHasNoPdbDeclaration = resolved.MemberHasNoPdbDeclaration,
                    MemberSourceTooComplex = resolved.MemberSourceTooComplex,
                    MemberSourceCoordinatesInvalid = resolved.MemberSourceCoordinatesInvalid,
                    PdbSourceUnavailableReason = resolved.PdbSourceUnavailableReason,
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

            // Aggregated caller queries always inspect the member's own assembly, with any
            // explicit caller scope contributing additional assemblies below.
            var callerTargetAssembly = apiType.SourceAssemblyPath ?? apiDllPath;
            if ((effectiveOptions.HasCallerScope
                 || ApiMemberSectionPipelines.ShouldAggregateCallers(
                     apiType,
                     effectiveOptions))
                && effectiveOptions.DllPath == null
                && callerTargetAssembly != null)
            {
                effectiveOptions = effectiveOptions with { DllPath = callerTargetAssembly };
            }

            // Expand --bin/--directory, --project, and --caller-package into assemblies
            // for cross-assembly callers and Call Graph traversal, in addition to the
            // selected member's own assembly.
            if (RequiresCallerScopeResolution(effectiveOptions))
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

                effectiveOptions = effectiveOptions with
                {
                    CallerScopeAssemblies = callerScopeAssemblySet.Assemblies
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
                if (!ApiCommand.ValidateTypeProjection(
                        schema,
                        projectionSections,
                        effectiveOptions.Fields,
                        effectiveOptions.Columns))
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

            if ((effectiveOptions.Count || !effectiveOptions.JsonOutput)
                && (apiType.Members is [{ Kind: "field" }]
                    || IsSelectedBodyEvidenceUnavailable(
                        apiType,
                        effectiveOptions)))
            {
                ApiCommand.WarnEmptySelectedSections(
                    apiType,
                    effectiveOptions,
                    ApiMemberSectionPipelines.Create(effectiveOptions));
            }

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

    private static bool DeferredExactTargetUsesTypePipeline(
        ApiType type,
        MemberOptions options) =>
        options.RouterDeferredTypeMemberValues.Length == 0
        || type.DefinitionName?.Segments.Length is not 1;

    private static MemberOptions NormalizeResolvedTypeQualifiers(
        MemberOptions options,
        string resolvedTypeName)
    {
        if (options.MemberFilter.Count == 0)
            return options;

        HashSet<string> memberFilter = new(
            options.MemberFilter.Select(
                member => SharedParsers.StripResolvedTypeQualifier(
                    member,
                    resolvedTypeName)),
            StringComparer.OrdinalIgnoreCase);
        return options with { MemberFilter = memberFilter };
    }

    private static async Task<int> ExecuteDeferredTypeAsync(
        MemberOptions unresolvedOptions,
        ApiSourceResult source,
        ApiServices.LoadedApiSurface loaded)
    {
        if (unresolvedOptions.MemberGenericArity.HasValue)
        {
            CommandError.Write(
                "The type command's -m filter does not support generic arity selectors; use the member command.");
            return 1;
        }

        if (unresolvedOptions.CallerScopeProjects.Length > 0)
        {
            var projectValueCount =
                unresolvedOptions.CallerScopeProjects.Length
                + (unresolvedOptions.ProjectPath is null ? 0 : 1);
            CommandError.Write(projectValueCount > 1
                ? $"Option '--project' expects a single argument but "
                    + $"{projectValueCount} were provided."
                : "--project cannot be combined with --package, --library, or --platform.");
            return 1;
        }

        if (ApiCommand.GetDeferredTypeIncompatibleOption(unresolvedOptions)
            is { } incompatibleOption)
        {
            CommandError.Write(
                $"Unrecognized option '{incompatibleOption}'.");
            return 1;
        }

        return await TypeCommand.ExecuteResolvedAsync(
            ApiCommand.ToTypeOptions(unresolvedOptions),
            source,
            loaded);
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

    private static bool MemberOverloadSelectorsConflict(
        MemberOptions explicitOptions,
        MemberTargetSelector impliedSelector)
    {
        if (explicitOptions.OverloadIndex is { } explicitIndex
            && impliedSelector.OverloadIndex is { } impliedIndex)
        {
            return explicitIndex != impliedIndex;
        }

        if (explicitOptions.MemberDigest is { Length: > 0 } explicitDigest
            && impliedSelector.DigestPrefix is { Length: > 0 } impliedDigest)
        {
            return !explicitDigest.StartsWith(
                       impliedDigest,
                       StringComparison.OrdinalIgnoreCase)
                   && !impliedDigest.StartsWith(
                       explicitDigest,
                       StringComparison.OrdinalIgnoreCase);
        }

        return (explicitOptions.OverloadIndex.HasValue
                && impliedSelector.DigestPrefix is { Length: > 0 })
            || (explicitOptions.MemberDigest is { Length: > 0 }
                && impliedSelector.OverloadIndex.HasValue);
    }

    private static string? MergeDigestPrefixes(
        string? explicitDigest,
        string? impliedDigest)
    {
        if (string.IsNullOrEmpty(explicitDigest))
            return impliedDigest;
        if (string.IsNullOrEmpty(impliedDigest))
            return explicitDigest;

        return explicitDigest.Length >= impliedDigest.Length
            ? explicitDigest
            : impliedDigest;
    }

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
        if (!options.MemberSectionsPreResolved
            && ((options.SelectDefault && options.Select is null)
                || IsPureSelector(options.Select, SelectResolver.AllSelector)))
            return false;

        sections = SingleOverloadSectionNames
            .Where(includeSections.Contains)
            .ToList();
        return sections.Count > 0;
    }

    private static MemberOptions IncludeCallerScopeSections(
        MemberOptions options,
        IReadOnlySet<string> discoveredCallerSections)
    {
        var includeSections = options.IncludeSections is { Count: > 0 } existing
            ? new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (discoveredCallerSections.Count > 0)
            includeSections.UnionWith(discoveredCallerSections);
        else
            includeSections.Add(SectionNames.Callers);
        return options with
        {
            IncludeSections = includeSections,
            CallerScopeSectionImplicitlySelected =
                includeSections.Contains(SectionNames.Callers)
        };
    }

    private static HashSet<string> GetDiscoveredCallerSections(
        MemberOptions options)
    {
        if (options.Discover is not { Length: > 0 } discover)
            return [];

        var pipeline = ApiMemberSectionPipelines.Create(options);
        var resolved = SelectResolver.ResolveSelectAsSections(
            discover,
            pipeline.SelectableSectionNames,
            pipeline.InfoSectionNames,
            pipeline.GetCategoryMap());
        if (resolved.HasError || resolved.Sections is not { } sections)
            return [];

        return sections
            .Where(section =>
                section.Equals(
                    SectionNames.Callers,
                    StringComparison.OrdinalIgnoreCase)
                || section.Equals(
                    SectionNames.CallGraph,
                    StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool RequiresCallerScopeResolution(MemberOptions options)
        => options.HasCallerScope
           && options.IncludeSections is { } sections
           && (sections.Contains(SectionNames.Callers)
               || sections.Contains(SectionNames.CallGraph));

    private static MemberOptions ExcludeCallersSection(MemberOptions options)
    {
        var includeSections = options.IncludeSections is { } existing
            ? new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)
            : [];
        includeSections.Remove(SectionNames.Callers);
        HashSet<string>? exactIncludeSections = null;
        if (options.ExactIncludeSectionsOverride is { } exactExisting)
        {
            exactIncludeSections = new HashSet<string>(
                exactExisting,
                StringComparer.OrdinalIgnoreCase);
            exactIncludeSections.Remove(SectionNames.Callers);
        }
        return options with
        {
            IncludeSections = includeSections,
            ExactIncludeSectionsOverride = exactIncludeSections,
            CallerScopeSectionImplicitlySelected = false
        };
    }

    private static bool HasAuthoredSectionRequest(MemberOptions options)
        => options.MemberSectionsPreResolved
           || options.Select is { Length: > 0 }
           || options.SelectDefault
           || options.Discover is { Length: > 0 }
           || options.BodyKindQuery.HasFilter;

    private static bool IsWholeDocumentJson(MemberOptions options)
        => options.JsonOutput
           && !options.Count
           && options.Discover is null
           && !options.Print
           && !options.Value
           && !options.Urls
           && !options.Paths;

    private static bool IsSelectedBodyEvidenceUnavailable(
        ApiType type,
        MemberOptions options)
    {
        if (options.ExactIncludeSections is not { } exactSections)
            return false;

        var requestedBodySections = exactSections
            .Where(BodyEvidenceSectionNames.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requestedBodySections.Count == 0)
            return false;

        var filteredType = ApiCommand.BuildFilteredTypeForSections(
            type,
            options);
        return ApiOutputFormatter.ResolveBodyMethods(
            filteredType,
            requestedBodySections,
            options).Count == 0;
    }

    private static readonly HashSet<string> BodyEvidenceSectionNames =
    [
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
        SectionNames.CostOverlay,
        SectionNames.SemanticsOverlay,
        SectionNames.IL,
        SectionNames.Facts
    ];

    private static IEnumerable<int> BodyMethodTokens(
        ApiType type,
        ApiMember member)
    {
        if (ApiMemberSectionDescriptors.IsMethodLike(member))
        {
            if (member.MetadataToken is { } token)
                yield return token;
            yield break;
        }

        if (!ApiMemberSectionDescriptors.HasAccessorTokens(member))
            yield break;

        foreach (var accessor in ApiOutputFormatter.AccessorMethods(member, type))
        {
            if (accessor.MetadataToken is { } token)
                yield return token;
        }
    }

    private static IEnumerable<ApiMember> GetAggregatedCallerMembers(
        ApiType type,
        MemberOptions options)
    {
        IEnumerable<ApiMember> members = ApiOutputFormatter
            .GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly, options.KindFilter)
            .SelectMany(group => group.Value);
        if (options.MemberGenericArity.HasValue && options.MemberFilter.Count == 1)
        {
            var memberName = options.MemberFilter.First();
            var arityCandidates = GetCandidateMembers(type, options, memberName).ToHashSet();
            members = members.Where(arityCandidates.Contains);
        }
        return members;
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
        SectionNames.PdbSource,
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
        select is { Length: > 0 }
        && select.All(selector => selector.Equals(name, StringComparison.OrdinalIgnoreCase));

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
        var sections = ApiCommand.GetRequestedMemberSections(apiType, options);
        if (sections.Overlaps([SectionNames.PdbSource, SectionNames.SourceDiff]))
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

    private static bool NeedsMemberSourceLocationResolution(MemberOptions options)
        => options.IncludeSections?.Contains(SectionNames.SourceLocations) == true;

    /// <summary>
    /// The accessor method whose PDB sequence points locate a selected property's or event's
    /// source, or
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
