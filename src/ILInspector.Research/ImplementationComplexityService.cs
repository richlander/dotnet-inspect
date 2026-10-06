using ILInspector.Analysis;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>
/// Content-shaped request for comparing Analysis-issued implementation
/// profiles across two already executed assembly populations.
/// </summary>
public sealed record ImplementationComplexityComparisonRequest(
    IReadOnlyList<LibraryImplementationProfileAnalysisResult?> OldProfiles,
    IReadOnlyList<LibraryImplementationProfileAnalysisResult?> NewProfiles,
    IReadOnlySet<string>? TypeFilters = null,
    ImplementationComplexityTargetContext? TargetContext = null);

/// <summary>
/// The complete Research-issued target context for one focused complexity
/// comparison.
/// </summary>
public sealed class ImplementationComplexityTargetContext
{
    public ImplementationComplexityTargetContext(
        ResearchTargetResolution resolution,
        ResearchAdmittedPopulation population)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(population);
        if (!ReferenceEquals(resolution.Operation, population.Operation))
        {
            throw new ArgumentException(
                "The target resolution and admitted population must belong "
                    + "to the same Research operation.",
                nameof(resolution));
        }

        Resolution = resolution;
        Population = population;
    }

    public ResearchTargetResolution Resolution { get; }

    public ResearchAdmittedPopulation Population { get; }
}

/// <summary>
/// Compares Analysis-owned implementation profiles without reopening or
/// rediscovering assembly bodies.
/// </summary>
public static class ImplementationComplexityService
{
    public static ImplementationComplexityDiff Execute(
        ImplementationComplexityComparisonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OldProfiles);
        ArgumentNullException.ThrowIfNull(request.NewProfiles);

        if (request.OldProfiles.Count == 0
            || request.NewProfiles.Count == 0)
        {
            return Unavailable(
                "Normal-flow cyclomatic complexity requires profile results "
                + "from both implementation-diff endpoints.");
        }

        if (request.OldProfiles.Any(profile => profile is null)
            || request.NewProfiles.Any(profile => profile is null))
        {
            return Unavailable(
                "Normal-flow cyclomatic complexity was not published by one "
                + "or more Research inputs.");
        }

        if (request.OldProfiles
                .OfType<LibraryImplementationProfileAnalysisResult>()
                .Any(profile => profile.Receipt.ModuleIdentity.AssemblyIdentity is null)
            || request.NewProfiles
                .OfType<LibraryImplementationProfileAnalysisResult>()
                .Any(profile => profile.Receipt.ModuleIdentity.AssemblyIdentity is null))
        {
            return Unavailable(
                "Normal-flow cyclomatic complexity requires assembly "
                + "identity for endpoint pairing.");
        }

        var oldByAssembly = request.OldProfiles
            .Cast<LibraryImplementationProfileAnalysisResult>()
            .ToDictionary(
                profile => AssemblyKey(profile),
                StringComparer.Ordinal);
        var newByAssembly = request.NewProfiles
            .Cast<LibraryImplementationProfileAnalysisResult>()
            .ToDictionary(
                profile => AssemblyKey(profile),
                StringComparer.Ordinal);
        if (oldByAssembly.Values.Any(profile => !profile.WasRequested)
            || newByAssembly.Values.Any(profile => !profile.WasRequested))
        {
            return Unavailable(
                "Normal-flow cyclomatic complexity was not requested for one "
                + "or both implementation-diff endpoints.");
        }

        if (oldByAssembly.Values.Any(profile => !profile.Receipt.HasFullMethodEvidenceScope)
            || newByAssembly.Values.Any(profile => !profile.Receipt.HasFullMethodEvidenceScope))
        {
            return Unavailable(
                "Normal-flow cyclomatic complexity requires unscoped "
                + "method-evidence coverage for both implementation-diff "
                + "endpoints.");
        }

        // A recoverable per-method Analysis failure (for example, a body
        // that cannot be decoded) leaves a diagnostic but no profile for
        // that method, which is indistinguishable from a genuinely added or
        // removed method once profiles are compared by presence alone.
        // Rather than risk reporting an analysis failure as a confident
        // Added/Removed/Changed complexity result, treat any diagnostic on
        // either endpoint as making that endpoint's complexity coverage
        // incomplete.
        if (oldByAssembly.Values.Any(profile => !profile.Receipt.Diagnostics.IsEmpty)
            || newByAssembly.Values.Any(profile => !profile.Receipt.Diagnostics.IsEmpty))
        {
            return Unavailable(
                "Normal-flow cyclomatic complexity requires diagnostic-free "
                + "method-evidence coverage for both implementation-diff "
                + "endpoints.");
        }

        if (request.TargetContext is { } targetContext)
        {
            return new ImplementationComplexityDiff(
                true,
                null,
                WithLocalContext(
                    CompareTargetedProfiles(
                        oldByAssembly.Values,
                        newByAssembly.Values,
                        request,
                        targetContext)));
        }

        var changes = new List<ImplementationComplexityChange>();
        foreach (string key in oldByAssembly.Keys
            .Union(newByAssembly.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            IReadOnlyList<MethodImplementationProfile> oldProfiles =
                oldByAssembly.TryGetValue(key, out var oldAssembly)
                    ? oldAssembly.Profiles
                    : [];
            IReadOnlyList<MethodImplementationProfile> newProfiles =
                newByAssembly.TryGetValue(key, out var newAssembly)
                    ? newAssembly.Profiles
                    : [];
            changes.AddRange(CompareProfiles(
                oldProfiles,
                newProfiles,
                request));
        }

        return new ImplementationComplexityDiff(true, null, WithLocalContext(changes));
    }

    /// <summary>
    /// Adds local context to already-paired changes. Complexity deltas are
    /// ranked against every delta-bearing change. Complete, unambiguous
    /// profile pairs are also partitioned by their direction-only structural
    /// signature.
    /// </summary>
    static IReadOnlyList<ImplementationComplexityChange> WithLocalContext(
        IReadOnlyList<ImplementationComplexityChange> changes)
    {
        int[] absoluteDeltas = changes
            .Where(change => change.Delta is not null)
            .Select(change => Math.Abs(change.Delta!.Value))
            .Order()
            .ToArray();
        ImplementationStructuralChange[] structuralChanges =
        [
            .. changes
                .Where(change => change.StructuralChange is not null)
                .Select(change => change.StructuralChange!),
        ];
        Dictionary<ImplementationStructuralChangeSignature, int> cohortSizes =
            structuralChanges
                .GroupBy(change => change.Signature)
                .ToDictionary(group => group.Key, group => group.Count());

        return changes
            .Select(change =>
            {
                ImplementationComplexityPopulationContext?
                    populationContext = null;
                if (change.Delta is not null)
                {
                    int absoluteDelta = Math.Abs(change.Delta.Value);
                    int countAtOrBelow =
                        UpperBound(absoluteDeltas, absoluteDelta);
                    populationContext =
                        new ImplementationComplexityPopulationContext(
                            absoluteDeltas.Length,
                            100.0 * countAtOrBelow
                                / absoluteDeltas.Length);
                }

                ImplementationStructuralChangeCohortContext?
                    structuralCohortContext = null;
                if (change.StructuralChange is not null)
                {
                    structuralCohortContext =
                        new ImplementationStructuralChangeCohortContext(
                            structuralChanges.Length,
                            cohortSizes[
                                change.StructuralChange.Signature]);
                }

                return change with
                {
                    PopulationContext = populationContext,
                    StructuralCohortContext = structuralCohortContext,
                };
            })
            .ToArray();
    }

    /// <summary>
    /// Count of elements in a sorted array that are less than or equal to
    /// <paramref name="value"/>.
    /// </summary>
    static int UpperBound(int[] sortedValues, int value)
    {
        int low = 0;
        int high = sortedValues.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (sortedValues[middle] <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    static string AssemblyKey(
        LibraryImplementationProfileAnalysisResult profile)
        => profile.Receipt.ModuleIdentity.AssemblyIdentity!.Name;

    static IReadOnlyList<ImplementationComplexityChange>
        CompareTargetedProfiles(
            IEnumerable<LibraryImplementationProfileAnalysisResult>
                oldAnalyses,
            IEnumerable<LibraryImplementationProfileAnalysisResult>
                newAnalyses,
            ImplementationComplexityComparisonRequest request,
            ImplementationComplexityTargetContext targetContext)
    {
        LibraryImplementationProfileAnalysisResult[] oldPopulation =
            [.. oldAnalyses];
        LibraryImplementationProfileAnalysisResult[] newPopulation =
            [.. newAnalyses];
        var changes = new List<ImplementationComplexityChange>();
        var projectedTargets = new HashSet<ComplexityTargetProjectionKey>();
        var returnTypeCollisions =
            new Dictionary<ResearchTargetDomainKey, IReadOnlySet<string>>();

        foreach (ResearchTargetCorrespondenceOutcome correspondence
            in targetContext.Resolution.Correspondences)
        {
            if (correspondence is not (
                ResearchTargetCorrespondenceOutcome.Paired
                or ResearchTargetCorrespondenceOutcome.BeforeOnly
                or ResearchTargetCorrespondenceOutcome.AfterOnly))
            {
                continue;
            }

            ResearchSubjectKey subject =
                ResearchDiff.SubjectFromCorrespondence(
                    targetContext.Resolution,
                    targetContext.Population,
                    correspondence,
                    returnTypeCollisions);
            if (!ResearchDiff.MatchesTypeFilters(
                subject.TypeName ?? "",
                request.TypeFilters))
            {
                continue;
            }

            MetadataMethodAddress? beforeAddress =
                Address(correspondence, ResearchComparisonSide.Before);
            MetadataMethodAddress? afterAddress =
                Address(correspondence, ResearchComparisonSide.After);
            if (!projectedTargets.Add(new(
                subject.Id,
                correspondence.Domain.Key,
                correspondence.Kind,
                beforeAddress,
                afterAddress)))
            {
                continue;
            }

            changes.AddRange(CompareTargetProfiles(
                ProfilesAt(oldPopulation, beforeAddress),
                ProfilesAt(newPopulation, afterAddress),
                subject));
        }

        return changes;
    }

    static MetadataMethodAddress? Address(
        ResearchTargetCorrespondenceOutcome correspondence,
        ResearchComparisonSide side)
        => (correspondence, side) switch
        {
            (ResearchTargetCorrespondenceOutcome.Paired paired,
                ResearchComparisonSide.Before) =>
                paired.Before.Target.Address,
            (ResearchTargetCorrespondenceOutcome.Paired paired,
                ResearchComparisonSide.After) =>
                paired.After.Target.Address,
            (ResearchTargetCorrespondenceOutcome.BeforeOnly beforeOnly,
                ResearchComparisonSide.Before) =>
                beforeOnly.Before.Target.Address,
            (ResearchTargetCorrespondenceOutcome.AfterOnly afterOnly,
                ResearchComparisonSide.After) =>
                afterOnly.After.Target.Address,
            _ => null,
        };

    static IReadOnlyList<MethodImplementationProfile> ProfilesAt(
        IEnumerable<LibraryImplementationProfileAnalysisResult> analyses,
        MetadataMethodAddress? address)
    {
        if (address is not { } target)
            return [];

        return
        [
            .. analyses
                .Where(analysis =>
                    analysis.Receipt.ModuleIdentity.ModuleVersionId
                        == target.ModuleVersionId)
                .SelectMany(static analysis => analysis.Profiles)
                .Where(profile =>
                    profile.Method.MetadataToken == target.Token),
        ];
    }

    static IReadOnlyList<ImplementationComplexityChange>
        CompareTargetProfiles(
            IReadOnlyList<MethodImplementationProfile> oldProfiles,
            IReadOnlyList<MethodImplementationProfile> newProfiles,
            ResearchSubjectKey subject)
    {
        if (oldProfiles.Count == 0 && newProfiles.Count == 0)
            return [];

        if (oldProfiles.Count <= 1 && newProfiles.Count <= 1)
        {
            return
            [
                CreateChange(
                    subject,
                    oldProfiles.SingleOrDefault(),
                    newProfiles.SingleOrDefault(),
                    ambiguous: false),
            ];
        }

        var oldByKey = oldProfiles.ToDictionary(
            profile => MethodKey(profile.EvidenceMethod),
            StringComparer.Ordinal);
        var newByKey = newProfiles.ToDictionary(
            profile => MethodKey(profile.EvidenceMethod),
            StringComparer.Ordinal);
        var changes = new List<ImplementationComplexityChange>();
        foreach (string key in oldByKey.Keys
            .Union(newByKey.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            oldByKey.TryGetValue(
                key,
                out MethodImplementationProfile? oldProfile);
            newByKey.TryGetValue(
                key,
                out MethodImplementationProfile? newProfile);
            changes.Add(CreateChange(
                subject,
                oldProfile,
                newProfile,
                ambiguous: true));
        }

        return changes;
    }

    static IReadOnlyList<ImplementationComplexityChange> CompareProfiles(
        IReadOnlyList<MethodImplementationProfile> oldProfiles,
        IReadOnlyList<MethodImplementationProfile> newProfiles,
        ImplementationComplexityComparisonRequest request)
    {
        IReadOnlySet<string> returnTypeCollisions =
            ResearchMemberIdentity.ReturnTypeCollisionSubjectIds(
                oldProfiles.Select(profile => profile.Method)
                    .Concat(newProfiles.Select(profile => profile.Method)));
        var oldEntries = oldProfiles
            .Select(profile => CreateProfileEntry(
                profile,
                returnTypeCollisions))
            .Where(entry => MatchesFilters(
                entry,
                request))
            .ToArray();
        var newEntries = newProfiles
            .Select(profile => CreateProfileEntry(
                profile,
                returnTypeCollisions))
            .Where(entry => MatchesFilters(
                entry,
                request))
            .ToArray();
        var oldByKey = oldEntries.ToDictionary(
            entry => entry.Key,
            StringComparer.Ordinal);
        var newByKey = newEntries.ToDictionary(
            entry => entry.Key,
            StringComparer.Ordinal);

        // A logical member (subject) can own more than one physical
        // evidence method - most commonly, multiple compiler-generated
        // lambda/state-machine bodies. Those generated names are ordinal-
        // based and can shift when lambdas are inserted, removed, or
        // reordered, so a same-name match across versions is not a
        // trustworthy correspondence. Rather than confidently claiming
        // Added/Removed/Changed on a possibly wrong pairing, report those
        // subjects as Incomplete.
        var ambiguousSubjectIds = oldEntries
            .Select(entry => entry.Subject.Id)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Concat(newEntries
                .Select(entry => entry.Subject.Id)
                .GroupBy(id => id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            .ToHashSet(StringComparer.Ordinal);

        var changes = new List<ImplementationComplexityChange>();

        foreach (string key in oldByKey.Keys
            .Union(newByKey.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            oldByKey.TryGetValue(key, out var oldEntry);
            newByKey.TryGetValue(key, out var newEntry);
            var subject = newEntry?.Subject
                ?? oldEntry?.Subject
                ?? throw new InvalidOperationException(
                    "A complexity comparison key had no profile.");
            MethodImplementationProfile? oldProfile = oldEntry?.Profile;
            MethodImplementationProfile? newProfile = newEntry?.Profile;
            changes.Add(CreateChange(
                subject,
                oldProfile,
                newProfile,
                ambiguousSubjectIds.Contains(subject.Id)));
        }

        return changes;
    }

    static ImplementationComplexityChange CreateChange(
        ResearchSubjectKey subject,
        MethodImplementationProfile? oldProfile,
        MethodImplementationProfile? newProfile,
        bool ambiguous)
    {
        ImplementationComplexityChangeKind kind;
        int? delta = null;
        if (ambiguous)
        {
            kind = ImplementationComplexityChangeKind.Incomplete;
            if (oldProfile is not null && newProfile is not null)
            {
                delta = newProfile.NormalFlowCyclomaticComplexity
                    - oldProfile.NormalFlowCyclomaticComplexity;
            }
        }
        else if (oldProfile is null)
        {
            kind = ImplementationComplexityChangeKind.Added;
        }
        else if (newProfile is null)
        {
            kind = ImplementationComplexityChangeKind.Removed;
        }
        else
        {
            delta = newProfile.NormalFlowCyclomaticComplexity
                - oldProfile.NormalFlowCyclomaticComplexity;
            kind = !oldProfile.IsComplete || !newProfile.IsComplete
                ? ImplementationComplexityChangeKind.Incomplete
                : delta == 0
                    ? ImplementationComplexityChangeKind.Unchanged
                    : ImplementationComplexityChangeKind.Changed;
        }

        ImplementationStructuralChange? structuralChange =
            (kind is ImplementationComplexityChangeKind.Changed
                or ImplementationComplexityChangeKind.Unchanged)
                && oldProfile is not null
                && newProfile is not null
                ? CreateStructuralChange(oldProfile, newProfile)
                : null;
        return new ImplementationComplexityChange(
            subject,
            kind,
            oldProfile?.NormalFlowCyclomaticComplexity,
            newProfile?.NormalFlowCyclomaticComplexity,
            delta,
            oldProfile?.IsComplete ?? false,
            newProfile?.IsComplete ?? false,
            oldProfile?.EvidenceMethod,
            newProfile?.EvidenceMethod,
            oldProfile,
            newProfile,
            StructuralChange: structuralChange);
    }

    static ImplementationStructuralChange CreateStructuralChange(
        MethodImplementationProfile oldProfile,
        MethodImplementationProfile newProfile)
        => new(
            newProfile.InstructionCount - oldProfile.InstructionCount,
            newProfile.NormalFlowCyclomaticComplexity
                - oldProfile.NormalFlowCyclomaticComplexity,
            newProfile.LoopCount - oldProfile.LoopCount,
            ExceptionRegionCount(newProfile)
                - ExceptionRegionCount(oldProfile),
            newProfile.DirectCallCount - oldProfile.DirectCallCount,
            newProfile.AllocationCount - oldProfile.AllocationCount,
            Convert.ToInt32(newProfile.Async)
                - Convert.ToInt32(oldProfile.Async));

    static int ExceptionRegionCount(MethodImplementationProfile profile)
        => profile.CatchCount
            + profile.FilterCount
            + profile.FinallyCount
            + profile.FaultCount;

    static ComplexityProfileEntry CreateProfileEntry(
        MethodImplementationProfile profile,
        IReadOnlySet<string> returnTypeCollisions)
    {
        ResearchSubjectKey baseSubject =
            ResearchMemberIdentity.SubjectFromMethod(profile.Method);
        ResearchSubjectKey subject =
            returnTypeCollisions.Contains(baseSubject.Id)
                ? ResearchMemberIdentity.SubjectFromMethod(
                    profile.Method,
                    includeReturnType: true)
                : baseSubject;
        return new(
            $"{subject.Id}|{MethodKey(profile.EvidenceMethod)}",
            profile,
            subject);
    }

    static string MethodKey(MethodIdentity method)
        => $"{GenericMemberIdentity.KeyFragment(method.DeclaringType)}|"
            + $"{method.Name}|{method.GenericArity}|{method.IsExtension}|"
            + $"{string.Join(",", method.ParameterTypes.Select(GenericMemberIdentity.KeyFragment))}|"
            + $"{GenericMemberIdentity.KeyFragment(method.ReturnType)}";

    static bool MatchesFilters(
        ComplexityProfileEntry entry,
        ImplementationComplexityComparisonRequest request)
        => ResearchDiff.MatchesTypeFilters(
               entry.Subject.TypeName ?? "",
               request.TypeFilters);

    static ImplementationComplexityDiff Unavailable(string reason)
        => new(false, reason, []);

    sealed record ComplexityProfileEntry(
        string Key,
        MethodImplementationProfile Profile,
        ResearchSubjectKey Subject);

    sealed record ComplexityTargetProjectionKey(
        string SubjectId,
        ResearchTargetDomainKey Domain,
        ResearchTargetCorrespondenceKind CorrespondenceKind,
        MetadataMethodAddress? Before,
        MetadataMethodAddress? After);
}
