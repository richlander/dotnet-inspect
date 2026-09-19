using ILInspector.Analysis;

namespace ILInspector.Research;

/// <summary>
/// Content-shaped request for comparing Analysis-issued implementation
/// profiles across two already executed assembly populations.
/// </summary>
public sealed record ImplementationComplexityComparisonRequest(
    IReadOnlyList<LibraryImplementationProfileAnalysisResult?> OldProfiles,
    IReadOnlyList<LibraryImplementationProfileAnalysisResult?> NewProfiles,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

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

        var changes = new List<ImplementationComplexityChange>();
        foreach (string key in oldByAssembly.Keys
            .Intersect(newByAssembly.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal))
        {
            changes.AddRange(CompareProfiles(
                oldByAssembly[key].Profiles,
                newByAssembly[key].Profiles,
                request));
        }

        return new ImplementationComplexityDiff(true, null, changes);
    }

    static string AssemblyKey(
        LibraryImplementationProfileAnalysisResult profile)
        => profile.Receipt.ModuleIdentity.AssemblyIdentity!.Name;

    static IReadOnlyList<ImplementationComplexityChange> CompareProfiles(
        IReadOnlyList<MethodImplementationProfile> oldProfiles,
        IReadOnlyList<MethodImplementationProfile> newProfiles,
        ImplementationComplexityComparisonRequest request)
    {
        var oldByKey = oldProfiles
            .Select(CreateProfileEntry)
            .Where(entry => MatchesFilters(entry.Subject, request))
            .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var newByKey = newProfiles
            .Select(CreateProfileEntry)
            .Where(entry => MatchesFilters(entry.Subject, request))
            .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
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
            ImplementationComplexityChangeKind kind;
            int? delta = null;
            if (oldProfile is null)
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

            changes.Add(new ImplementationComplexityChange(
                subject,
                kind,
                oldProfile?.NormalFlowCyclomaticComplexity,
                newProfile?.NormalFlowCyclomaticComplexity,
                delta,
                oldProfile?.IsComplete ?? false,
                newProfile?.IsComplete ?? false,
                oldEntry?.Profile.EvidenceMethod,
                newEntry?.Profile.EvidenceMethod));
        }

        return changes;
    }

    static ComplexityProfileEntry CreateProfileEntry(
        MethodImplementationProfile profile)
    {
        var subject = ResearchMemberIdentity.SubjectFromMethod(profile.Method);
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
        ResearchSubjectKey subject,
        ImplementationComplexityComparisonRequest request)
        => ResearchDiff.MatchesTypeFilters(
               subject.TypeName ?? "",
               request.TypeFilters)
           && (request.MemberTargetIdentities is null
               || request.MemberTargetIdentities.Count == 0
               || request.MemberTargetIdentities.Contains(subject.Id));

    static ImplementationComplexityDiff Unavailable(string reason)
        => new(false, reason, []);

    sealed record ComplexityProfileEntry(
        string Key,
        MethodImplementationProfile Profile,
        ResearchSubjectKey Subject);
}
