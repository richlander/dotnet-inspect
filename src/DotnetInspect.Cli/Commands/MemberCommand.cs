using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using ILInspector.Metadata;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using ILInspector.SourceLink;
using DotnetInspect.Cli.Planning;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// Inspects type members (docs on by default).
/// </summary>
public static class MemberCommand
{
    public const string Name = "member";

    public static Task<int> ExecuteAsync(MemberOptions options)
        => ExecuteAsync(
            options,
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options));

    internal static Task<int> ExecuteAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan)
        => ExecuteCoreAsync(options, plan);

    internal static Task<int> ExecuteResolvedAsync(
        MemberOptions options,
        ApiSourceResult source,
        ApiServices.LoadedApiSurface loaded)
        => ExecuteCoreAsync(
            options,
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options),
            source,
            loaded);

    private static async Task<int> ExecuteCoreAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        ApiSourceResult? resolvedSource = null,
        ApiServices.LoadedApiSurface? loadedSurface = null)
    {
        if (plan.Intent.Surface != InspectionSurface.Member)
            throw new ArgumentException(
                "A member command requires a member inspection plan.",
                nameof(plan));
        ResolvedMemberInspectionPlan executionPlan = plan;
        MemberInspectionTerminalPlan? terminalPlan = null;
        if ((options.SourceParts || options.SourcePart is not null)
            && options.Select is null && options.IncludeSections is null)
        {
            options = options with { Select = [SectionNames.SourceLocations] };
            executionPlan = ResolvedMemberInspectionPlan.FromCompatibilityOptions(options);
        }
        if (MemberSourcePartsOutput.ValidateOptions(options) is { } partsError)
        {
            CommandError.Write(partsError);
            return 1;
        }

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

        if (MemberShareProjection.ValidateOptions(options) is { } shareOptionError)
        {
            CommandError.Write(shareOptionError);
            return 1;
        }
        if (options.ShareFormat is not null
            && !options.RouterDeferredTypeOrMember
            && !HasExactMemberSelector(options))
        {
            MemberShareProjection.WriteExactSelectorRequired();
            return 1;
        }

        bool mayResolveImpliedMember =
            options.MemberFilter.Count == 0
            && options.TypeName is { } unresolvedTarget
            && HasPotentialImpliedMember(unresolvedTarget);
        if (RejectExactMemberCardinality(
                options,
                executionPlan,
                mayResolveImpliedMember))
        {
            return 1;
        }
        bool catalogIsSyntacticallyResolved =
            options.MemberFilter.Count > 0
            || options.TypeName is { } syntacticTypeName
                && CSharpText.FqnParser.LastTopLevelDot(
                    syntacticTypeName) < 0;
        if (catalogIsSyntacticallyResolved
            && RejectTotalEffectiveDiscoveryMiss(plan))
        {
            return 1;
        }
        if (ApiCommand.RejectUniversallyInvalidMemberSelect(options))
            return 1;
        if (ApiCommand.RejectRouteIndependentOptionShape(options))
            return 1;

        var unresolvedOptions = options;
        bool memberGestureChanged = false;
        bool catalogNeedsTargetResolution =
            !options.RouterDeferredTypeOrMember
            && options.IncludeSections is null
            && options.MemberFilter.Count == 0
            && options.TypeName is { } unresolvedTypeName
            && CSharpText.FqnParser.LastTopLevelDot(
                unresolvedTypeName) > 0;
        if (!options.RouterDeferredTypeOrMember
            && !catalogNeedsTargetResolution)
        {
            // Shared preamble: section validation, discovery, verbosity promotion
            var (preamble, error) =
                ApiCommand.RunPreamble(options, executionPlan);
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

        bool ownsSource = resolvedSource is null;
        ApiSourceResult source;
        if (resolvedSource is null)
        {
            var (acquiredSource, sourceError) =
                await ApiSourceResolver.ResolveAsync(options);
            if (sourceError.HasValue)
                return sourceError.Value;

            source = acquiredSource;
        }
        else
        {
            source = resolvedSource;
        }

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
            var loaded = loadedSurface
                ?? (options.RouterDeferredTypeOrMember
                    ? ApiServices.LoadTypeApi(source, options)
                    : ApiServices.LoadFullApi(
                        searchPath, runtimeAssemblyPath, options.PackagePath,
                        packageName, apiSource, source.ApiVersion, selectedTfm,
                        logger, options, source.PackageExtractPath));
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
            if ((options.SourceParts || options.SourcePart is not null)
                && options.MemberFilter.Count == 0 && lookupResult.ImpliedMember is null)
            {
                CommandError.Write("Authored member parts require a member selection, such as Method:1.");
                return 1;
            }
            ResolvedAssemblyReference? sourceAssembly =
                loaded.TryGetSourceAssembly(apiType);
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
                if (mergedArity.HasValue && mergedFilter.Count != 1)
                {
                    CommandError.Write("A generic arity selector requires exactly one member name.");
                    return 1;
                }

                mergeOptions = mergeOptions with
                {
                    MemberFilter = mergedFilter,
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
                memberGestureChanged = true;
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

            if (options.RouterDeferredTypeOrMember
                || catalogNeedsTargetResolution
                || memberGestureChanged)
            {
                MemberOptions planningOptions =
                    options.RouterDeferredTypeOrMember
                        ? unresolvedOptions
                        : options;
                if (!planningOptions.MemberSectionsPreResolved)
                {
                    planningOptions = planningOptions with
                    {
                        IncludeSections = null,
                        ExactIncludeSectionsOverride = null,
                    };
                }
                executionPlan =
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(
                            planningOptions);
                if (RejectExactMemberCardinality(
                        planningOptions,
                        executionPlan,
                        mayResolveImpliedMember: false))
                {
                    return 1;
                }
                if (RejectTotalEffectiveDiscoveryMiss(
                        executionPlan))
                {
                    return 1;
                }
                var (preamble, error) =
                    ApiCommand.RunPreamble(
                        planningOptions,
                        executionPlan);
                if (error.HasValue) return error.Value;
                options = (MemberOptions)preamble.Options with
                {
                    PackagePath = source.ResolvedPackagePath,
                    PackageRangeAddress = null,
                    ProjectAssetsPath = projectAssetsPath,
                };
            }
            else if (!options.MemberSectionsPreResolved
                && options.MemberFilter.Count == 0
                && options.Select is { Length: > 0 })
            {
                var actualPipeline = ApiMemberSectionPipelines.Create(options);
                var actualSelect = SelectResolver.ResolveSelectAsSections(
                    options.Select,
                    actualPipeline.SelectableSectionNames,
                    actualPipeline.InfoSectionNames,
                    ApiMemberSectionPipelines.GetCategoryMap(actualPipeline),
                    selectDefault: options.SelectDefault,
                    exactOnlySections:
                        ApiMemberSectionPipelines.GetExactOnlySections(options));
                if (SelectOutput.WriteUnresolved(actualSelect))
                    return 1;
                if (actualSelect.Sections != null)
                {
                    options = options with
                    {
                        IncludeSections = actualSelect.Sections,
                        ExactIncludeSectionsOverride = actualSelect.ExactSections,
                    };
                }
            }

            if (options.BodyKindQuery.HasFilter
                && (options.MemberFilter.Count != 1
                    || options.MemberFilter.Any(MemberFilterHasWildcard)))
            {
                CommandError.Write(
                    "--where Kind=... requires one exact member name or selector.");
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

            if (options.ShareFormat is not null
                && !HasExactMemberSelector(options))
            {
                MemberShareProjection.WriteExactSelectorRequired();
                return 1;
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

            var acquisition = new ApiCommand.TypeAcquisitionContext(
                loaded.GetLibraryAssetPath(source.PackageExtractPath),
                packageName, packageVersion ?? source.ApiVersion, apiSource,
                selectedTfm,
                MemberCodeSourceAssembly: sourceAssembly);

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
            bool autoSelectedOverload = false;
            int? exactCloneMethodToken = null;
            if (!effectiveOptions.OverloadIndex.HasValue
                && string.IsNullOrWhiteSpace(effectiveOptions.MemberDigest)
                && ShouldAutoSelectSingleOverload(
                    effectiveOptions,
                    executionPlan))
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
                    autoSelectedOverload = true;
                    if (MemberFilterHasWildcard(autoMemberName))
                    {
                        effectiveOptions = effectiveOptions with
                        {
                            MemberFilter = new HashSet<string>(
                                StringComparer.OrdinalIgnoreCase)
                            {
                                autoOverloads[0].Name,
                            },
                        };
                    }
                }
            }

            // Multi-section count maps intentionally span the listing and detail
            // pipelines so unavailable sections can be retained as zero rows.
            if (autoSelectedOverload && !effectiveOptions.Count)
            {
                MemberOptions detailPlanningOptions =
                    effectiveOptions;
                if (!detailPlanningOptions.MemberSectionsPreResolved)
                {
                    string[]? resolvedSelectors =
                        GetDetailReplanSelectors(detailPlanningOptions);
                    detailPlanningOptions = detailPlanningOptions with
                    {
                        IncludeSections = null,
                        ExactIncludeSectionsOverride = null,
                        Select = resolvedSelectors,
                    };
                }
                executionPlan =
                    ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(
                            detailPlanningOptions);
                var (detailPreamble, detailError) =
                    ApiCommand.RunPreamble(
                        detailPlanningOptions,
                        executionPlan);
                if (detailError.HasValue)
                    return detailError.Value;
                effectiveOptions =
                    (MemberOptions)detailPreamble.Options with
                    {
                        Select = effectiveOptions.Select,
                        SelectDefault = effectiveOptions.SelectDefault,
                    };
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
                    !autoSelectedOverload
                    && target.Kind is MemberTargetKind.Property or MemberTargetKind.Event
                    && target.OverloadIndex.HasValue
                    && (target.DigestPrefix is not null
                        || memberResolution.Candidates.Count == 1);
                if (explicitAccessorSelector)
                {
                    if (target.Body?.MetadataToken is not { } methodToken)
                    {
                        CommandError.Write(
                            $"Member selector '{target.NormalizedSelector}' has no exact accessor method identity.");
                        return 1;
                    }
                    exactCloneMethodToken = methodToken;
                }
                if (effectiveOptions.BodyKindQuery.HasFilter
                    && BodyAccessorCount(selected) > 1
                    && !explicitAccessorSelector)
                {
                    WriteAccessorSelectionRequired(apiType, selected);
                    return 1;
                }
                apiType.Members = [selected];
                var detailDllPath = apiType.SourceAssemblyPath ?? apiDllPath;
                effectiveOptions = effectiveOptions with
                {
                    DllPath = detailDllPath,
                    OverloadIndex = target.Body?.DeclaringOverloadIndex
                        ?? target.DeclaringOverloadIndex,
                    SelectedBodyMethodToken =
                        target.Body?.MetadataToken,
                };
                if (effectiveOptions.EffectiveDiscovery)
                {
                    executionPlan =
                        ResolvedMemberInspectionPlan
                            .FromCompatibilityOptions(
                                effectiveOptions);
                }

                terminalPlan = MemberInspectionPlanBuilder.Create(
                    sourceAssembly,
                    detailDllPath,
                    selectedTfm,
                    apiType.FullName,
                    apiType.DefinitionName,
                    ApiMemberIdentity.GetMemberAnchor(apiType, selected),
                    executionPlan,
                    effectiveOptions,
                    ResolvedPackageSource(
                        source,
                        sourceAssembly,
                        selectedTfm));
                effectiveOptions =
                    MemberInspectionPlanBuilder.ApplySemanticDemand(
                        effectiveOptions,
                        terminalPlan);
                if (terminalPlan is ShareProjectionPlan sharePlan
                    && effectiveOptions.ShareFormat is { } shareFormat)
                {
                    return MemberShareProjection.Write(
                        source,
                        loaded,
                        apiType,
                        sharePlan,
                        shareFormat);
                }
            }

            if (effectiveOptions.OverloadIndex is null
                && TryGetSelectedSingleOverloadSections(
                    effectiveOptions,
                    executionPlan,
                    out var singleOverloadSections))
            {
                var memberName = effectiveOptions.MemberFilter.First();
                var overloads = GetCandidateMembers(apiType, effectiveOptions, memberName);
                if (overloads.Count > 1)
                {
                    var sectionLabel = singleOverloadSections.Count == 1
                        ? $"section '{singleOverloadSections[0]}' requires"
                        : $"sections {string.Join(", ", singleOverloadSections.Select(section => $"'{section}'"))} require";
                    CommandError.Write($"{sectionLabel} a single selected overload for member '{memberName}'.");
                    if (MemberFilterHasWildcard(memberName))
                    {
                        CommandError.WriteLine(
                            $"Replace wildcard '{memberName}' with one exact member name, "
                            + "then select an overload with Name~<digest> "
                            + "(shown in the Digest column) or Name:1.");
                    }
                    else
                    {
                        CommandError.WriteLine($"Select one overload with {memberName}~<digest> (shown in the Digest column of the member listing), or positionally with {memberName}:1 through {memberName}:{overloads.Count}.");
                    }
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

                apiType.Members = arityCandidates;
            }

            if ((effectiveOptions.SourceParts || effectiveOptions.SourcePart is not null)
                && MemberSourcePartsOutput.ValidateSections(effectiveOptions) is { } sectionError)
            {
                CommandError.Write(sectionError);
                return 1;
            }

            if (!CloneCandidatesCommand.ValidatePredicateSelection(
                    effectiveOptions.CloneCandidateQuery,
                    effectiveOptions.IncludeSections))
            {
                return 1;
            }

            if (CloneCandidatesCommand.IsSelected(
                    effectiveOptions.IncludeSections))
            {
                if (apiType.Members.Count != 1)
                {
                    CommandError.Write(
                        $"Section '{SectionNames.CloneCandidates}' requires one exact logical member.");
                    return 1;
                }
                string? clonePath =
                    apiType.SourceAssemblyPath
                    ?? sourceAssembly?.Path
                    ?? runtimeAssemblyPath
                    ?? apiDllPath;
                if (clonePath is null)
                {
                    CommandError.Write(
                        $"Member '{apiType.Members[0].Name}' has no resolved assembly path for Clone Candidates.");
                    return 1;
                }
                if (!CloneCandidatesCommand.TryCreateMemberSeed(
                        clonePath,
                        apiType,
                        apiType.Members[0],
                        exactCloneMethodToken,
                        out StructuralCloneSearchSeed.Member? seed,
                        out string? seedError))
                {
                    CommandError.Write(seedError!);
                    return 1;
                }
                ResolvedAssemblyReference cloneAssembly =
                    sourceAssembly
                    ?? ResolvedAssemblyReference.CreateFromPath(
                        clonePath,
                        AssemblyResolutionProvenance.Local(
                            "member Clone Candidates"));
                return await CloneCandidatesCommand.ExecuteAsync(
                    cloneAssembly,
                    clonePath,
                    seed!,
                    effectiveOptions.CloneCandidateQuery,
                    CloneCandidateOutputOptions.From(effectiveOptions),
                    new CloneCandidateWorkspaceOptions(
                        source.PackageExtractPath,
                        effectiveOptions.ProjectAssetsPath,
                        effectiveOptions.Tfm,
                        effectiveOptions.SourceOptions));
            }

            if (effectiveOptions.OverloadIndex is null
                && effectiveOptions.IncludeSections?.Contains(SectionNames.UnsafeMembers) == true
                && (runtimeAssemblyPath ?? apiDllPath) is { } unsafeDllPath)
            {
                effectiveOptions = effectiveOptions with { DllPath = unsafeDllPath };
            }

            if (effectiveOptions.OverloadIndex is null
                && effectiveOptions.IncludeSections?
                    .Contains(SectionNames.MemberMetrics) == true
                && (apiType.SourceAssemblyPath
                    ?? runtimeAssemblyPath
                    ?? apiDllPath) is { } profileDllPath)
            {
                effectiveOptions =
                    effectiveOptions with { DllPath = profileDllPath };
            }

            if (TryCreateImplementationProfileFamilySelection(
                    apiType,
                    effectiveOptions,
                    out ImplementationProfileFamilySelection? familySelection))
            {
                effectiveOptions =
                    await AttachImplementationProfileFamilyInspectionAsync(
                        apiType,
                        effectiveOptions,
                        loaded,
                        familySelection);
                InspectionEnvelope<
                    AssemblyContextEntry<
                        AssemblyImplementationProfileFamilyInspection>>
                    inspection =
                        effectiveOptions
                            .ImplementationProfileFamilyInspection!;
                ApiCommand.WriteSourceInspectionDiagnostics(
                    inspection.Diagnostics);
                if (inspection.Content
                    is not AssemblyContextEntry<
                        AssemblyImplementationProfileFamilyInspection>
                        .Available)
                {
                    return 1;
                }
            }

            // Enrich with local XML docs only (source info is in the source command)
            {
                var dllPath = runtimeAssemblyPath ?? apiDllPath;
                if (dllPath != null
                    && (effectiveOptions.ShowDocs
                        || effectiveOptions.ShowSamples))
                    await DocumentationEnricher.EnrichAsync(
                        [apiType],
                        source,
                        loaded,
                        effectiveOptions,
                        context.HttpClient);
            }

            if (apiDllPath != null
                && NeedsMemberSourceLocationResolution(effectiveOptions))
            {
                var locationDllPath = apiType.SourceAssemblyPath ?? pdbLookupPath;
                var locations = await MemberSourceLocationCollector.EnrichAsync(
                    apiType,
                    locationDllPath,
                    sourceAssembly,
                    packageName,
                    packageVersion,
                    effectiveOptions,
                    context.HttpClient,
                    logger);
                effectiveOptions = effectiveOptions with
                {
                    PdbPath = locations.PdbPath ?? effectiveOptions.PdbPath,
                    SourceLocationMappings = locations.Mappings,
                };
            }

            // Resolve PDB/source only when selected detail sections need them.
            if (effectiveOptions.OverloadIndex.HasValue && apiDllPath != null
                && AuthorizesMemberSourceResolution(apiType, effectiveOptions))
            {
                bool fetchSource =
                    AuthorizesMemberProviderSourceContent(
                        apiType,
                        effectiveOptions);
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
                // lookup is that same assembly; otherwise the
                // token's row would not align (forwarded facade, or a reference
                // assembly for the surface vs an implementation assembly for
                // bodies), so fall back to name/overload resolution.
                var tokenOriginAssembly = apiType.SourceAssemblyPath ?? apiDllPath;
                string methodSourceAssemblyPath =
                    apiType.IsForwarded
                        && sourceAssembly?.Path is { } supplierPath
                            ? supplierPath
                            : pdbLookupPath;
                var requestedSourceSections =
                    ApiCommand.GetRequestedMemberSections(
                        apiType,
                        effectiveOptions);
                if (requestedSourceSections.Contains(SectionNames.SourceDiff)
                    && sourceMember?.MetadataToken is not null)
                {
                    bool? selectedMemberHasBody =
                        ApiCommand.ResolveMemberBodyState(
                            methodSourceAssemblyPath,
                            sourceTypeName,
                            sourceMember.Name,
                            sourceOverloadIndex,
                            publicOnly,
                            tokenOriginAssembly,
                            sourceMember.MetadataToken.Value,
                            logger.Log);
                    ResolvedAssemblyReference comparisonAssembly =
                        sourceAssembly?.Path is { } sourceAssemblyPath
                        && LibraryMetadataService
                            .ReferenceTreePathComparer(OperatingSystem.IsWindows())
                            .Equals(
                                Path.GetFullPath(sourceAssemblyPath),
                                Path.GetFullPath(tokenOriginAssembly))
                            ? sourceAssembly
                            : ResolvedAssemblyReference.CreateFromPath(
                                tokenOriginAssembly,
                                AssemblyResolutionProvenance.Local(
                                    "member source comparison"));
                    var bindingPolicy = new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            tokenOriginAssembly)
                        {
                            ProjectAssetsPath =
                                effectiveOptions.ProjectAssetsPath,
                            TargetFramework = effectiveOptions.Tfm,
                            IncludeDepsJsonAssets = false,
                            IncludeAspNetCoreSharedFramework = false,
                            PreferImplementationAssemblies = true,
                            AllowPlatformAssemblyVersionRollForward = true,
                        });
                    var participant = new AssemblyContextParticipant(
                        comparisonAssembly,
                        bindingPolicy);
                    await using var workspace = new InspectionWorkspace();
                    using AssemblyContextGroup group =
                        workspace.CreateAssemblyContextGroup([participant]);
                    var queryContext = new AssemblyContextSourceQueryContext(
                        context.HttpClient,
                        FileSystemPdbStore.CreateDefault(),
                        new SourcePolicyPackageSourceAuthorization(
                            effectiveOptions.SourceOptions),
                        new SourceFetch(
                            DotnetInspector.Networking.HttpClientFactory
                                .SharedUntrustedFetch))
                    {
                        RepositoryPaths =
                            effectiveOptions.SourceRepositories,
                        NuGetSourceOptions =
                            effectiveOptions.SourceOptions,
                        AllowLocalSourceReads = true,
                        AllowAdjacentPdbReads = true,
                        Log = logger.Log,
                    };
                    InspectionEnvelope<AssemblyMemberSourceComparisonEntry> inspection =
                        await MemberSourceInspection.CompareAsync(
                            group,
                            participant,
                            AssemblyMemberSourceRequest.From(
                                apiType,
                                sourceMember,
                                effectiveOptions.RenderOptions),
                            queryContext);
                    AssemblyMemberSourceComparisonEntry comparison = inspection.Content;
                    AssemblyMemberPdbSourceAttempt.Unavailable? unavailablePdb =
                        comparison switch
                        {
                            AssemblyMemberSourceComparisonEntry.Available
                            {
                                Pdb: AssemblyMemberPdbSourceAttempt.Unavailable
                                    unavailable
                            } => unavailable,
                            AssemblyMemberSourceComparisonEntry.Unavailable
                                unavailable => unavailable.Pdb,
                            _ => null,
                        };
                    PdbMemberSourceOutcome? pdbOutcome =
                        unavailablePdb?.Inspection.Outcome;
                    effectiveOptions = effectiveOptions with
                    {
                        MemberSourceComparison = comparison,
                        MemberSourceComparisonInspection = inspection,
                        MemberHasNoBody = selectedMemberHasBody == false,
                        MemberHasNoPdbDeclaration =
                            pdbOutcome
                            == PdbMemberSourceOutcome.NoVouchedDeclaration,
                        MemberSourceTooComplex =
                            pdbOutcome
                            == PdbMemberSourceOutcome.SourceTooComplex,
                        MemberSourceCoordinatesInvalid =
                            pdbOutcome
                            == PdbMemberSourceOutcome
                                .InvalidSequencePointCoordinates,
                        PdbSourceUnavailableReason =
                            comparison
                                is AssemblyMemberSourceComparisonEntry.Available
                                {
                                    Pdb:
                                        AssemblyMemberPdbSourceAttempt.Available
                                }
                                ? null
                                : ApiCommand.PdbSourceUnavailableReason(
                                    comparison),
                    };
                    if (effectiveOptions.PdbPath is null
                        && NeedsMemberPipelinePdbPath(
                            requestedSourceSections))
                    {
                        string? pdbPath = sourceAssembly is null
                            ? await ApiCommand.TryAcquirePdbPathAsync(
                                methodSourceAssemblyPath,
                                effectiveOptions,
                                logger,
                                context.HttpClient)
                            : await ApiCommand.TryAcquirePdbPathAsync(
                                methodSourceAssemblyPath,
                                sourceAssembly,
                                effectiveOptions,
                                logger,
                                context.HttpClient,
                                fallbackPackageName: packageName,
                                fallbackPackageVersion: packageVersion);
                        if (pdbPath is not null)
                        {
                            effectiveOptions = effectiveOptions with
                            {
                                PdbPath = pdbPath,
                            };
                        }
                    }
                }
                else
                {
                    bool? selectedMemberHasBody = null;
                    if (sourceMember?.MetadataToken is { } sourceMemberToken)
                    {
                        selectedMemberHasBody =
                            ApiCommand.ResolveMemberBodyState(
                                methodSourceAssemblyPath,
                                sourceTypeName,
                                sourceMember.Name,
                                sourceOverloadIndex,
                                publicOnly,
                                tokenOriginAssembly,
                                sourceMemberToken,
                                logger.Log);
                        if (selectedMemberHasBody == false)
                        {
                            effectiveOptions = effectiveOptions with
                            {
                                MemberHasNoBody = true,
                            };
                        }
                    }

                    if (fetchSource
                        && selectedMemberHasBody != false
                        && sourceMember?.MetadataToken is not null)
                    {
                        ResolvedAssemblyReference? participantAssembly =
                            sourceAssembly?.Path is { } sourceAssemblyPath
                            && LibraryMetadataService
                                .ReferenceTreePathComparer(
                                    OperatingSystem.IsWindows())
                                .Equals(
                                    Path.GetFullPath(sourceAssemblyPath),
                                    Path.GetFullPath(tokenOriginAssembly))
                                ? sourceAssembly
                                : null;
                        var (participant, queryContext) =
                            AuthoredSourceDocumentPrinter.CreateContext(
                                tokenOriginAssembly,
                                effectiveOptions,
                                participantAssembly,
                                packageName,
                                packageVersion,
                                context.HttpClient);
                        InspectionEnvelope<AssemblyMemberSourceEntry> inspection;
                        await using (var workspace = new InspectionWorkspace())
                        {
                            using AssemblyContextGroup group =
                                workspace.CreateAssemblyContextGroup(
                                    [participant]);
                            inspection =
                                await MemberSourceInspection.ExecuteAsync(
                                    group,
                                    participant,
                                    AssemblyMemberSourceRequest
                                        .From(
                                            apiType,
                                            sourceMember,
                                            effectiveOptions.RenderOptions)
                                        .WithoutDecompiledFallback(),
                                    queryContext);
                        }

                        AssemblyMemberSourceEntry sourceResult =
                            inspection.Content;
                        AssemblyMemberSource.Pdb? pdbSource =
                            sourceResult
                            is AssemblyMemberSourceEntry.Available
                            {
                                Source:
                                    AssemblyMemberSource.Pdb available,
                            }
                                ? available
                                : null;
                        PdbMemberSourceInspection? pdbAttempt =
                            sourceResult switch
                            {
                                AssemblyMemberSourceEntry.Available
                                {
                                    Source:
                                        AssemblyMemberSource.Decompiled
                                            decompiled,
                                } => decompiled.PdbAttempt,
                                AssemblyMemberSourceEntry.Unavailable
                                {
                                    PdbAttempt: { } unavailable,
                                } => unavailable,
                                _ => null,
                            };
                        PdbMemberSourceOutcome? pdbOutcome =
                            pdbAttempt?.Outcome;
                        SourceDocumentObservation? document =
                            pdbSource?.Inspection.Document;
                        effectiveOptions = effectiveOptions with
                        {
                            MethodSource = pdbSource is null
                                ? null
                                : new MethodSourceContext(
                                    pdbSource.Text,
                                    document?.ResolvedUrl
                                        ?? document?.OriginalPath,
                                    document?.ChecksumAlgorithm,
                                    document?.Checksum,
                                    pdbSource.Inspection
                                            .ChecksumVerification
                                        ?? SourceChecksumVerification
                                            .Unavailable),
                            MemberHasNoPdbDeclaration =
                                pdbOutcome
                                == PdbMemberSourceOutcome
                                    .NoVouchedDeclaration,
                            MemberSourceTooComplex =
                                pdbOutcome
                                == PdbMemberSourceOutcome
                                    .SourceTooComplex,
                            MemberSourceCoordinatesInvalid =
                                pdbOutcome
                                == PdbMemberSourceOutcome
                                    .InvalidSequencePointCoordinates,
                            PdbSourceUnavailableReason =
                                pdbSource is null
                                    ? ApiCommand
                                        .PdbSourceUnavailableReason(
                                            sourceResult)
                                    : null,
                        };
                    }
                    else if (fetchSource
                        && selectedMemberHasBody != false)
                    {
                        effectiveOptions = effectiveOptions with
                        {
                            PdbSourceUnavailableReason =
                                ApiCommand.NoPdbSourceMappingReason,
                        };
                    }

                    if (effectiveOptions.PdbPath is null
                        && selectedMemberHasBody != false
                        && NeedsMemberPipelinePdbPath(
                            requestedSourceSections))
                    {
                        string? pdbPath = sourceAssembly is null
                            ? await ApiCommand.TryAcquirePdbPathAsync(
                                methodSourceAssemblyPath,
                                effectiveOptions,
                                logger,
                                context.HttpClient)
                            : await ApiCommand.TryAcquirePdbPathFromSelectedPathAsync(
                                methodSourceAssemblyPath,
                                sourceAssembly,
                                effectiveOptions,
                                logger,
                                context.HttpClient,
                                fallbackPackageName: packageName,
                                fallbackPackageVersion: packageVersion);
                        if (pdbPath is not null)
                        {
                            effectiveOptions = effectiveOptions with
                            {
                                PdbPath = pdbPath,
                            };
                        }
                    }
                }
            }

            if (apiDllPath is not null
                && AuthorizesMemberOrdinarySource(
                    apiType,
                    effectiveOptions))
            {
                effectiveOptions =
                    await AttachMemberSourceInspectionAsync(
                        apiType,
                        effectiveOptions,
                        apiDllPath,
                        sourceAssembly,
                        loaded.TryGetBindingContext(apiType)
                            ?? loaded.RootBindingContext,
                        packageName,
                        packageVersion,
                        context.HttpClient);
            }

            if (effectiveOptions.EffectiveDiscovery)
            {
                if (terminalPlan is not null
                    && terminalPlan is not EffectiveDiscoveryPlan)
                {
                    throw new InvalidOperationException(
                        "Exact-member effective discovery requires an effective-discovery plan.");
                }
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
                if (terminalPlan is null)
                    executionPlan = ResolvedMemberInspectionPlan
                        .FromCompatibilityOptions(effectiveOptions);
                return ApiCommand.ExecuteEffectiveDiscovery(
                    apiType,
                    ApiInspectionCatalogRegistry.CreateMemberPipeline(
                        executionPlan.Selection.Catalog,
                        executionPlan.Intent.Members.OverloadIndex),
                    effectiveOptions,
                    acquisition);
            }

            if (effectiveOptions.OverloadIndex.HasValue
                && apiDllPath is not null
                && ApiCommand.GetRequestedMemberSections(
                        apiType,
                        effectiveOptions)
                    .Contains(SectionNames.DecompiledSource))
            {
                effectiveOptions =
                    await AttachMemberDecompilationInspectionAsync(
                        apiType,
                        effectiveOptions,
                        apiDllPath,
                        sourceAssembly,
                        context.HttpClient);
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
                effectiveOptions = IncludeCallersSection(effectiveOptions) with
                {
                    CallerScopeAssemblies = callerScopeAssemblySet.Assemblies,
                };
            }

            if (terminalPlan is not null
                && terminalPlan is not SectionExecutionPlan)
            {
                throw new InvalidOperationException(
                    "Exact-member output requires a section-execution plan.");
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
            var writeExitCode = await ApiCommand.WriteTypeOutputAsync(
                apiType, acquisition.FoundIn, acquisition.PackageName, acquisition.PackageVersion,
                acquisition.ApiSource, acquisition.SelectedTfm, effectiveOptions,
                memberCodeSourceAssembly: sourceAssembly);
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

                tips.Add(new(TypeCommand.Name, $"{simpleName} {sourceFlag} --tree", "view type tree"));
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
            if (ownsSource && tempDir != null && Directory.Exists(tempDir))
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

    private static AssemblyResolutionProvenance.PackageAsset?
        ResolvedPackageSource(
            ApiSourceResult source,
            ResolvedAssemblyReference? sourceAssembly,
            string? selectedTfm)
    {
        if (sourceAssembly?.Provenance
            is AssemblyResolutionProvenance.PackageAsset package)
        {
            return package;
        }
        if (!string.Equals(
                source.ApiSource,
                SourceKind.NuGet,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(source.PackageName)
            || string.IsNullOrWhiteSpace(source.PackageVersion))
        {
            return null;
        }

        return new AssemblyResolutionProvenance.PackageAsset(
            source.PackageName,
            source.PackageVersion,
            selectedTfm,
            rid: null);
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

        if (GetDeferredTypeIncompatibleOption(unresolvedOptions)
            is { } incompatibleOption)
        {
            CommandError.Write(
                $"Unrecognized option '{incompatibleOption}'.");
            return 1;
        }

        return await TypeCommand.ExecuteResolvedAsync(
            TypeCommand.FromDeferredMemberOptions(unresolvedOptions),
            source,
            loaded);
    }

    private static string? GetDeferredTypeIncompatibleOption(
        MemberOptions options)
    {
        if (options.Focus is not null) return "--focus";
        if (options.OverloadIndexExplicitlySet) return "--index";
        if (options.CtorOnly) return "--ctor";
        if (options.CallerScopeDirectories.Length > 0) return "--bin";
        if (options.CallerScopePackages.Length > 0) return "--caller-package";
        if (options.MermaidOutput || options.EmbeddedMermaid) return "--mermaid";
        return null;
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
                context.Logger,
                options.PlatformFramework);

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
            context.Logger,
            options.PlatformFramework);

        return resolution.Status switch
        {
            TypeFindIfMissStatus.Found => await ExecuteAsync(resolution.ApplyTo(options)),
            TypeFindIfMissStatus.Ambiguous => resolution.WriteAmbiguousError(),
            _ => null
        };
    }

    private static bool ShouldAutoSelectSingleOverload(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan)
    {
        if (options.MemberFilter.Count != 1)
            return false;
        if (!TryGetSelectedSingleOverloadSections(
                options,
                plan,
                out _))
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

    private static bool TryGetSelectedSingleOverloadSections(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        out List<string> sections)
    {
        sections = [];
        if (options.MemberFilter.Count != 1)
            return false;
        if (options.BodyKindQuery.HasFilter)
        {
            sections = options.IncludeSections is { Count: > 0 } bodySections
                ? bodySections.Where(section => BodyKindQueryOptions.Sections.Contains(
                    section, StringComparer.OrdinalIgnoreCase)).ToList()
                : [SectionNames.BodyShapes];
            return true;
        }
        if (options.EffectiveDiscovery
            && plan.Selection.RequiredTarget
                == InspectionTargetRequirement.ExactMember)
        {
            sections = SingleOverloadSectionNames
                .Where(plan.Selection.ResolvedSections.Contains)
                .ToList();
            return sections.Count > 0;
        }
        if (options.IncludeSections is not { Count: > 0 })
            return false;
        if (options.Select?.Any(selector =>
                selector.Equals(
                    SectionCategoryNames.Source,
                    StringComparison.OrdinalIgnoreCase)) == true)
        {
            sections = SingleOverloadSectionNames
                .Where(options.IncludeSections.Contains)
                .ToList();
            return sections.Count > 0;
        }
        // Bare -S carries no selector value, so it cannot be recognized by inspecting Select.
        if (!options.MemberSectionsPreResolved
            && options.ImplicitIncludeSections.Count == 0
            && ((options.SelectDefault && options.Select is null)
                || IsPureSelector(options.Select, SelectResolver.AllSelector)))
            return false;

        IReadOnlySet<string>? exactIncludeSections =
            options.ExactIncludeSections;
        if (exactIncludeSections is null
            && options.ImplicitIncludeSections.Count == 0)
            return false;

        sections = SingleOverloadSectionNames
            .Where(section =>
                exactIncludeSections?.Contains(section) == true
                || options.ImplicitIncludeSections.Contains(section))
            .ToList();
        return sections.Count > 0;
    }

    private static bool RejectTotalEffectiveDiscoveryMiss(
        ResolvedMemberInspectionPlan plan)
    {
        if (plan.Intent.Sections.DiscoveryMode
                != InspectionDiscoveryMode.Effective
            || plan.Selection.UnresolvedSelectors.IsEmpty
            || !plan.Selection.ResolvedSections.IsEmpty)
        {
            return false;
        }

        return SelectOutput.WriteUnresolved(
            plan.Selection.ToSelectResult());
    }

    private static MemberOptions IncludeCallersSection(MemberOptions options)
    {
        var includeSections = options.IncludeSections is { Count: > 0 } existing
            ? new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var implicitSections = new HashSet<string>(
            options.ImplicitIncludeSections,
            StringComparer.OrdinalIgnoreCase);
        includeSections.Add(SectionNames.Callers);
        implicitSections.Add(SectionNames.Callers);
        return options with
        {
            IncludeSections = includeSections,
            ImplicitIncludeSections = implicitSections,
        };
    }

    private static string[]? GetDetailReplanSelectors(MemberOptions options)
    {
        if (options.Select is not { Length: > 0 }
            && !options.SelectDefault)
        {
            return options.IncludeSections is { Count: > 0 }
                ? [.. options.IncludeSections]
                : null;
        }

        var inventoryOptions = options with { OverloadIndex = null };
        var inventoryPipeline =
            ApiMemberSectionPipelines.Create(inventoryOptions);
        SelectResult inventorySelection =
            SelectResolver.ResolveSelectAsSections(
                options.Select,
                inventoryPipeline.SelectableSectionNames,
                inventoryPipeline.InfoSectionNames,
                ApiMemberSectionPipelines.GetCategoryMap(
                    inventoryPipeline),
                options.SelectDefault);
        HashSet<string> unresolvedSelectors =
            inventorySelection.Unresolved
                .Select(static miss => miss.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> selectors =
        [
            .. options.Select?.Where(
                selector => !unresolvedSelectors.Contains(selector))
                ?? [],
        ];

        HashSet<string> selectedSections =
            inventorySelection.Sections
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.IncludeSections is { Count: > 0 })
        {
            selectors.AddRange(
                options.IncludeSections.Where(
                    section => !selectedSections.Contains(section)));
        }

        return selectors.Count > 0 ? [.. selectors] : null;
    }

    private static readonly string[] SingleOverloadSectionNames =
    [
        SectionNames.Signature,
        SectionNames.CustomAttributes,
        SectionNames.Source,
        SectionNames.DecompiledSource,
        SectionNames.FidelityCauses,
        SectionNames.AppliedTaste,
        SectionNames.AnnotatedSource,
        SectionNames.AnnotatedSourceDocument,
        SectionNames.FindingCensus,
        SectionNames.CostOverlay,
        SectionNames.SemanticsOverlay,
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
        SectionNames.BodyShapeSummary,
        SectionNames.TopLeverage,
        SectionNames.PerformanceTriage,
        SectionNames.CloneCandidates,
        SectionNames.Facts,
        SectionNames.IL,
    ];

    private static readonly string[] MemberSourceResolutionSectionNames =
    [
        SectionNames.DecompiledSource,
        SectionNames.AnnotatedSource,
        SectionNames.AnnotatedSourceDocument,
        SectionNames.FindingCensus,
        SectionNames.BodyShapes,
        SectionNames.BodyShapeSummary,
        SectionNames.Facts,
    ];

    private static readonly string[] MemberPipelinePdbPathSectionNames =
    [
        .. MemberSourceResolutionSectionNames,
        SectionNames.FidelityCauses,
        SectionNames.AppliedTaste,
        SectionNames.CostOverlay,
        SectionNames.SemanticsOverlay,
    ];

    private static bool IsPureSelector(string[]? select, string name) =>
        select is { Length: 1 } && select[0].Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool NeedsMemberPipelinePdbPath(
        IReadOnlySet<string> sections)
        => sections.Overlaps(MemberPipelinePdbPathSectionNames);

    private static List<ApiMember> GetCandidateMembers(
        ApiType apiType,
        MemberOptions options,
        string memberName)
    {
        if (!MemberFilterHasWildcard(memberName))
        {
            return GetTargetCandidates(
                    apiType,
                    options,
                    memberName)
                .Select(candidate => candidate.Member)
                .ToList();
        }

        var filter = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            memberName,
        };
        IEnumerable<ApiMember> candidates =
            apiType.Members.Where(member =>
                TypeMatcher.MatchesMemberFilter(
                    member.Name,
                    filter));
        if (options.KindFilter.Count > 0)
        {
            candidates = candidates.Where(member =>
                options.KindFilter.Contains(member.Kind));
        }
        if (options.MemberGenericArity is { } arity)
        {
            candidates = candidates.Where(member =>
                (member.SignatureModel?.TypeParameters.Count ?? 0)
                == arity);
        }

        return candidates
            .OrderBy(member => member.Name, StringComparer.Ordinal)
            .ThenBy(
                ApiMemberIdentity.GetMemberSignatureSortKey,
                StringComparer.Ordinal)
            .ToList();
    }

    internal static bool TryCreateImplementationProfileFamilySelection(
        ApiType apiType,
        MemberOptions options,
        [NotNullWhen(true)]
        out ImplementationProfileFamilySelection? selection)
    {
        selection = null;
        if (options.IncludeAll
            || options.OverloadIndex.HasValue
            || !string.IsNullOrWhiteSpace(options.MemberDigest)
            || options.MemberGenericArity.HasValue
            || options.KindFilter.Count > 0
            || options.SelectedBodyMethodToken.HasValue
            || options.MemberFilter.Count != 1
            || options.IncludeSections?
                .Contains(SectionNames.MemberMetrics) != true)
        {
            return false;
        }

        string memberName = options.MemberFilter.First();
        if (MemberFilterHasWildcard(memberName)
            || apiType.DefinitionName is null)
        {
            return false;
        }

        List<ApiMember> candidates =
            GetCandidateMembers(apiType, options, memberName);
        if (candidates.Count < 2
            || candidates.Any(member =>
                member.Kind is not ("method" or "extension-method")))
        {
            return false;
        }

        string[] selectors =
        [
            .. candidates
                .Select(member =>
                    ApiMemberIdentity
                        .GetMemberAnchor(apiType, member)
                        .StableSelector)
                .Distinct(StringComparer.Ordinal),
        ];
        if (selectors.Length != candidates.Count)
            return false;

        selection = new ImplementationProfileFamilySelection(
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(apiType),
            selectors);
        return true;
    }

    private static async Task<MemberOptions>
        AttachImplementationProfileFamilyInspectionAsync(
        ApiType apiType,
        MemberOptions options,
        ApiServices.LoadedApiSurface loaded,
        ImplementationProfileFamilySelection selection)
    {
        ResolvedAssemblyReference definingAssembly =
            loaded.TryGetSourceAssembly(apiType)
            ?? ResolvedAssemblyReference.CreateFromPath(
                apiType.SourceAssemblyPath
                    ?? loaded.ApiDllPath,
                AssemblyResolutionProvenance.Local(
                    "member implementation profiles"));
        SelectedTypeBindingContext? bindingContext =
            loaded.TryGetBindingContext(apiType)
            ?? loaded.RootBindingContext;
        IAssemblyBindingPolicy bindingPolicy =
            bindingContext?.Policy
            ?? (definingAssembly.Path is { } definingAssemblyPath
                ? new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        definingAssemblyPath)
                    {
                        ProjectAssetsPath =
                            options.ProjectAssetsPath,
                        TargetFramework = options.Tfm,
                        IncludeDepsJsonAssets = false,
                        IncludeAspNetCoreSharedFramework = false,
                        PreferImplementationAssemblies = true,
                        AllowPlatformAssemblyVersionRollForward = true,
                    })
                : throw new InvalidOperationException(
                    "A pathless selected API participant requires its "
                        + "authoritative binding policy."));
        var participant =
            new AssemblyContextParticipant(
                definingAssembly,
                bindingPolicy);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        InspectionEnvelope<
            AssemblyContextEntry<
                AssemblyImplementationProfileFamilyInspection>>
            inspection =
                ImplementationProfileFamilyInspectionOperation.Execute(
                    group,
                    participant,
                    selection);
        return options with
        {
            ImplementationProfileFamilyInspection = inspection,
        };
    }

    private static bool RejectExactMemberCardinality(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        bool mayResolveImpliedMember)
    {
        if (plan.Selection.RequiredTarget
                != InspectionTargetRequirement.ExactMember
            || options.MemberFilter.Count == 1
            || (options.MemberFilter.Count == 0
                && mayResolveImpliedMember))
        {
            return false;
        }

        CommandError.Write(
            options.BodyKindQuery.HasFilter
                ? "--where Kind=... requires one exact member name or selector."
                : "Exact-member section selection requires exactly one member name.");
        return true;
    }

    private static bool HasExactMemberSelector(MemberOptions options) =>
        options.OverloadIndex.HasValue
        || !string.IsNullOrWhiteSpace(options.MemberDigest);

    private static bool HasPotentialImpliedMember(string target)
    {
        var (_, memberName) =
            SharedParsers.SplitTrailingMember(target);
        return memberName is not null
            || CSharpText.FqnParser.LastTopLevelDot(
                target) > 0;
    }

    private static IReadOnlyList<MemberTargetCandidate> GetTargetCandidates(ApiType apiType, MemberOptions options, string memberName)
        => MemberTargetResolver.GetCandidates(
                apiType,
                options.MemberGenericArity is { } arity
                    ? new MemberTargetSelector(memberName, memberName, GenericArity: arity)
                    : new MemberTargetSelector(memberName, memberName),
                options.KindFilter);

    static async Task<MemberOptions>
        AttachMemberDecompilationInspectionAsync(
            ApiType apiType,
            MemberOptions options,
            string apiDllPath,
            ResolvedAssemblyReference? sourceAssembly,
            HttpClient httpClient)
    {
        ApiMember? selectedMember =
            apiType.Members.Count == 1
                ? apiType.Members[0]
                : null;
        ApiMember? sourceAccessor =
            ResolveSourceAccessor(
                apiType,
                selectedMember,
                options.OverloadIndex);
        ApiMember? sourceMember =
            sourceAccessor
            ?? selectedMember;
        if (sourceMember?.MetadataToken is not { } sourceMemberToken
            || MetadataTokens.EntityHandle(sourceMemberToken).Kind
                != HandleKind.MethodDefinition)
            return options;

        string tokenOriginAssembly =
            apiType.SourceAssemblyPath
            ?? apiDllPath;
        DecompilationInspectionPreparation.Prepared preparation =
            await DecompilationInspectionPreparation.CreateAsync(
                tokenOriginAssembly,
                sourceAssembly,
                options,
                httpClient,
                "member decompilation");
        AssemblyMemberSourceRequest request =
            ResolveMemberDecompilationRequest(
                apiType,
                sourceMember,
                sourceAccessor,
                options,
                tokenOriginAssembly,
                preparation.Assembly);

        await using var workspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [preparation.Participant]);
        InspectionEnvelope<AssemblyMemberDecompilationEntry>
            inspection =
                await MemberSourceInspection.DecompileAsync(
                    group,
                    preparation.Participant,
                    request,
                    preparation.QueryContext,
                    preparation.PortablePdb);
        return options with
        {
            MemberDecompilationInspection = inspection,
        };
    }

    static AssemblyMemberSourceRequest
        ResolveMemberDecompilationRequest(
            ApiType apiType,
            ApiMember sourceMember,
            ApiMember? sourceAccessor,
            MemberOptions options,
            string tokenOriginAssembly,
            ResolvedAssemblyReference decompilationAssembly)
    {
        string lookupType =
            sourceMember.DeclaringType
            ?? apiType.FullName;
        int lookupOverloadIndex =
            sourceAccessor is not null
                ? 0
                : (sourceMember.DeclaringOverloadIndex
                    ?? options.OverloadIndex!.Value) - 1;
        bool directRequest =
            options.IncludeAll;
        bool publicOnly =
            !directRequest
            && sourceMember.Kind
                is not ("explicit-interface-implementation"
                    or "finalizer");
        bool tokenAddressesSelectedAssembly =
            decompilationAssembly.Path is null
            || LibraryMetadataService
                .ReferenceTreePathComparer(
                    OperatingSystem.IsWindows())
                .Equals(
                    Path.GetFullPath(
                        decompilationAssembly.Path),
                    Path.GetFullPath(
                        tokenOriginAssembly));

        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(
                decompilationAssembly);
        MethodBodySelection selection =
            session.MethodBodies.ResolveMethod(
                lookupType,
                sourceMember.Name,
                lookupOverloadIndex,
                publicOnly,
                tokenAddressesSelectedAssembly
                    ? sourceMember.MetadataToken
                    : null)
            ?? throw new InvalidOperationException(
                "The selected member could not be resolved in the decompilation assembly.");
        ApiType targetType =
            session.ApiSurface(includeAll: true).Types.Single(
                candidate =>
                    candidate.FullName == lookupType);
        ApiMember? targetMember =
            targetType.Members.FirstOrDefault(
                candidate =>
                    candidate.MetadataToken
                    == selection.MetadataToken);
        targetMember ??=
            targetType.Members
                .SelectMany(
                    member =>
                        ApiMemberAccessors.Create(
                            member,
                            targetType))
                .FirstOrDefault(
                    candidate =>
                        candidate.MetadataToken
                        == selection.MetadataToken);
        if (targetMember is null)
        {
            throw new InvalidOperationException(
                "The selected MethodDef could not be projected as an API member.");
        }

        return AssemblyMemberSourceRequest.From(
            targetType,
            targetMember,
            options.RenderOptions);
    }

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
        if (sections.Overlaps(
                [
                    SectionNames.Source,
                    SectionNames.PdbSource,
                    SectionNames.SourceDiff,
                ]))
            return true;

        bool pdbAuthorized = options.IncludeSections is { Count: > 0 }
                             || options.Verbosity >= Verbosity.Detailed;
        return pdbAuthorized
               && sections.Overlaps(MemberSourceResolutionSectionNames);
    }

    internal static bool AuthorizesMemberSourceResolution(
        ApiType apiType,
        MemberOptions options)
        => options.OverloadIndex.HasValue
           && NeedsMemberSourceResolution(apiType, options);

    internal static bool AuthorizesMemberSourceContent(
        ApiType apiType,
        MemberOptions options)
    {
        if (!AuthorizesMemberSourceResolution(apiType, options))
            return false;

        var pipeline = ApiMemberSectionPipelines.Create(options);
        return pipeline.GetAuthorizedSections(
                SectionCapabilities.MayFetchSources,
                options.UserVerbosity,
                options.IncludeSections)
            .Overlaps(
                [
                    SectionNames.Source,
                    SectionNames.PdbSource,
                    SectionNames.SourceDiff,
                ]);
    }

    private static bool AuthorizesMemberProviderSourceContent(
        ApiType apiType,
        MemberOptions options)
    {
        if (!AuthorizesMemberSourceContent(apiType, options))
            return false;

        return ApiCommand.GetRequestedMemberSections(apiType, options)
            .Overlaps([SectionNames.PdbSource, SectionNames.SourceDiff]);
    }

    private static bool AuthorizesMemberOrdinarySource(
        ApiType apiType,
        MemberOptions options)
    {
        if (ResolveOrdinarySourceMember(apiType, options) is null
            || options.IncludeSections?.Contains(SectionNames.Source) != true)
        {
            return false;
        }

        var pipeline = ApiMemberSectionPipelines.Create(options);
        return pipeline.GetAuthorizedSections(
                SectionCapabilities.MayFetchSources,
                options.UserVerbosity,
                options.IncludeSections)
            .Contains(SectionNames.Source);
    }

    private static async Task<MemberOptions>
        AttachMemberSourceInspectionAsync(
        ApiType apiType,
        MemberOptions options,
        string apiDllPath,
        ResolvedAssemblyReference? sourceAssembly,
        SelectedTypeBindingContext? bindingContext,
        string? packageName,
        string? packageVersion,
        HttpClient httpClient)
    {
        ApiMember? selectedMember =
            ResolveOrdinarySourceMember(apiType, options);
        ApiMember? sourceAccessor =
            ResolveSourceAccessor(
                apiType,
                selectedMember,
                options.OverloadIndex);
        ApiMember? sourceMember =
            sourceAccessor
            ?? selectedMember;
        if (sourceMember?.MetadataToken is not { } sourceMemberToken
            || MetadataTokens.EntityHandle(sourceMemberToken).Kind
                != HandleKind.MethodDefinition)
        {
            return options;
        }

        string assemblyPath =
            apiType.SourceAssemblyPath
            ?? apiDllPath;
        ResolvedAssemblyReference? selectedAssembly =
            sourceAssembly?.Path is { } selectedPath
            && LibraryMetadataService
                .ReferenceTreePathComparer(
                    OperatingSystem.IsWindows())
                .Equals(
                    Path.GetFullPath(selectedPath),
                    Path.GetFullPath(assemblyPath))
                ? sourceAssembly
                : null;
        var (participant, queryContext) =
            AuthoredSourceDocumentPrinter.CreateContext(
                assemblyPath,
                options,
                selectedAssembly,
                packageName,
                packageVersion,
                httpClient,
                bindingContext?.Policy);
        InspectionEnvelope<AssemblyMemberSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([participant]);
            inspection =
                await MemberSourceInspection.ExecuteAsync(
                        group,
                        participant,
                        AssemblyMemberSourceRequest.From(
                            apiType,
                            sourceMember,
                            options.RenderOptions),
                        queryContext)
                    .ConfigureAwait(false);
        }

        ApiCommand.WriteSourceInspectionDiagnostics(
            inspection.Diagnostics);
        return options with
        {
            MemberSourceInspection = inspection,
        };
    }

    private static ApiMember? ResolveOrdinarySourceMember(
        ApiType apiType,
        MemberOptions options)
    {
        if (options.MemberFilter.Count == 1)
        {
            string memberName =
                options.MemberFilter.First();
            List<ApiMember> candidates =
                GetCandidateMembers(
                    apiType,
                    options,
                    memberName);
            if (candidates.Count == 1)
                return candidates[0];
        }

        return apiType.Members.Count == 1
            ? apiType.Members[0]
            : null;
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
