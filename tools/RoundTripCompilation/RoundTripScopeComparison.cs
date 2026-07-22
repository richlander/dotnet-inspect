using System.Collections.Immutable;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Research;
using DecompilerMetadataSource = ILInspector.Decompiler.Pipeline.MetadataSource;

namespace DotnetInspector.RoundTripCompilation;

public enum RoundTripScopeComparisonStatus
{
    Completed,
    Unavailable,
}

public sealed record RoundTripScopeMemberComparison(
    RoundTripTarget Target,
    RoundTripEvidenceStatus CSharpStatus,
    IlBodyDiffOutcome IlStatus,
    RoundTripCSharpEvidence? CSharpDiff,
    RoundTripIlEvidence? IlDiff,
    [property: JsonIgnore] ImplementationMemberDiffResult? Evidence,
    string? CSharpFailure,
    string? IlFailure);

public sealed record RoundTripScopeComparisonResult(
    RoundTripScopeComparisonStatus Status,
    RoundTripComparisonResult? Cluster,
    RoundTripComparisonResult? All,
    ImmutableArray<RoundTripScopeMemberComparison> Members,
    string? Failure);

public static class RoundTripScopeComparison
{
    public static RoundTripScopeComparisonResult Compare(
        RoundTripRequest clusterRequest,
        RoundTripCompilationProvenance clusterContext,
        byte[] clusterPe,
        RoundTripRequest allRequest,
        RoundTripCompilationProvenance allContext,
        byte[] allPe)
    {
        ArgumentNullException.ThrowIfNull(clusterRequest);
        ArgumentNullException.ThrowIfNull(clusterContext);
        ArgumentNullException.ThrowIfNull(clusterPe);
        ArgumentNullException.ThrowIfNull(allRequest);
        ArgumentNullException.ThrowIfNull(allContext);
        ArgumentNullException.ThrowIfNull(allPe);

        if (clusterRequest.Scope != RoundTripScope.Cluster || allRequest.Scope != RoundTripScope.All)
            return Unavailable("scope pair must be cluster then all");
        if (!SameRequestContext(clusterRequest, allRequest))
            return Unavailable("scope pair request context differs beyond scope");
        if (!clusterContext.HasExactReferenceContent || !allContext.HasExactReferenceContent)
            return Unavailable("scope pair reference provenance lacks exact content hashes");
        if (!SameCompilationContext(clusterContext, allContext))
            return Unavailable("scope pair compiler or reference context differs");

        var cluster = RoundTripComparison.Compare(clusterRequest, clusterPe, clusterContext);
        var all = RoundTripComparison.Compare(allRequest, allPe, allContext);
        if (cluster.Status != RoundTripComparisonStatus.Completed
            || all.Status != RoundTripComparisonStatus.Completed)
        {
            return new RoundTripScopeComparisonResult(
                RoundTripScopeComparisonStatus.Unavailable,
                cluster,
                all,
                Members: [],
                Failure: cluster.Failure ?? all.Failure ?? "original-to-donor comparison unavailable");
        }

        string clusterPath = TemporaryPath("cluster");
        string allPath = TemporaryPath("all");
        try
        {
            File.WriteAllBytes(clusterPath, clusterPe);
            File.WriteAllBytes(allPath, allPe);
            using var clusterSource = DecompilerMetadataSource.OpenWithoutSymbols(clusterPath);
            using var allSource = DecompilerMetadataSource.OpenWithoutSymbols(allPath);
            var members = ImmutableArray.CreateBuilder<RoundTripScopeMemberComparison>();
            foreach (var target in clusterRequest.Targets)
            {
                var clusterMember = cluster.Members.Single(member => member.Target.Method == target.Method);
                var allMember = all.Members.Single(member => member.Target.Method == target.Method);
                if (clusterMember.Correspondence.Target is not { } clusterTarget
                    || allMember.Correspondence.Target is not { } allTarget)
                {
                    string failure = clusterMember.Correspondence.Failure
                        ?? allMember.Correspondence.Failure
                        ?? "selected member correspondence unavailable";
                    members.Add(new RoundTripScopeMemberComparison(
                        target,
                        RoundTripEvidenceStatus.Unavailable,
                        IlBodyDiffOutcome.Unavailable,
                        CSharpDiff: null,
                        IlDiff: null,
                        Evidence: null,
                        CSharpFailure: failure,
                        IlFailure: failure));
                    continue;
                }

                var subject = new FindingSubject(
                    target.Anchor.StableSelector,
                    $"{target.Anchor.TypeFullName}.{target.Anchor.MemberName}");
                var clusterInspection = CSharpFindings.Inspect(clusterSource, clusterTarget.Handle, subject);
                var allInspection = CSharpFindings.Inspect(allSource, allTarget.Handle, subject);
                var evidence = ImplementationDiff.CompareMembers(
                    clusterSource,
                    clusterTarget.Handle,
                    allSource,
                    allTarget.Handle);
                var csharpStatus = clusterInspection is FindingInspection<CSharpCanonicalLine>.Complete
                    && allInspection is FindingInspection<CSharpCanonicalLine>.Complete
                        ? evidence.CSharpDiff is { IsExact: true }
                            ? RoundTripEvidenceStatus.Exact
                            : RoundTripEvidenceStatus.Changed
                        : RoundTripEvidenceStatus.Unavailable;
                members.Add(new RoundTripScopeMemberComparison(
                    target,
                    csharpStatus,
                    evidence.IlDiff?.Diff.Outcome ?? IlBodyDiffOutcome.Unavailable,
                    ToEvidence(evidence.CSharpDiff),
                    ToEvidence(evidence.IlDiff?.Diff),
                    evidence,
                    CSharpFailure: csharpStatus == RoundTripEvidenceStatus.Unavailable
                        ? "cluster or all C# inspection was not complete"
                        : null,
                    IlFailure: evidence.IlDiff?.Diff.Failure));
            }

            return new RoundTripScopeComparisonResult(
                RoundTripScopeComparisonStatus.Completed,
                cluster,
                all,
                members.ToImmutable(),
                Failure: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return new RoundTripScopeComparisonResult(
                RoundTripScopeComparisonStatus.Unavailable,
                cluster,
                all,
                Members: [],
                Failure: $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            DeleteTemporary(clusterPath);
            DeleteTemporary(allPath);
        }
    }

    static bool SameRequestContext(RoundTripRequest cluster, RoundTripRequest all)
        => cluster.Artifact == all.Artifact
           && cluster.Module == all.Module
           && cluster.BodyPolicy == all.BodyPolicy
           && CanonicalTargets(cluster).SequenceEqual(CanonicalTargets(all), StringComparer.Ordinal)
           && CanonicalReplacements(cluster).SequenceEqual(CanonicalReplacements(all), StringComparer.Ordinal);

    static IEnumerable<string> CanonicalTargets(RoundTripRequest request)
        => request.Targets
            .Select(target => $"{target.Method.ModuleVersionId:N}:{target.Method.Token:x8}:{target.Anchor.CanonicalSignature}")
            .Order(StringComparer.Ordinal);

    static IEnumerable<string> CanonicalReplacements(RoundTripRequest request)
        => request.Replacements
            .Select(replacement => string.Join("\n",
                replacement.Method.ModuleVersionId.ToString("N"),
                replacement.Method.Token.ToString("x8"),
                replacement.Anchor.CanonicalSignature,
                replacement.Body.Source,
                replacement.Body.RequiresAsyncModifier,
                replacement.Body.RequiresUnsafeModifier,
                replacement.Body.ConstructorInitializer?.Kind.ToString() ?? "",
                string.Join("\u001f", replacement.Body.ConstructorInitializer?.Arguments ?? [])))
            .Order(StringComparer.Ordinal);

    static bool SameCompilationContext(
        RoundTripCompilationProvenance left,
        RoundTripCompilationProvenance right)
        => left with { References = [], ParseFeatures = [] }
               == right with { References = [], ParseFeatures = [] }
           && left.ParseFeatures.SequenceEqual(right.ParseFeatures, StringComparer.Ordinal)
           && left.References.SequenceEqual(right.References);

    static RoundTripCSharpEvidence? ToEvidence(CSharpBodyDiffResult? diff)
        => diff is null ? null : new(
            Normalize(diff.Rows),
            Normalize(diff.FailureRows),
            Normalize(diff.IdentityFailures));

    static RoundTripIlEvidence? ToEvidence(IlBodyDiffResult? diff)
        => diff is null ? null : new(
            diff.Outcome,
            diff.Failure,
            Normalize(diff.Rows),
            Normalize(diff.FailureRows));

    static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values)
        => values.IsDefault ? [] : values;

    static string TemporaryPath(string scope)
        => Path.Combine(Path.GetTempPath(), $"dotnet-inspect-roundtrip-{scope}-{Guid.NewGuid():N}.dll");

    static void DeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static RoundTripScopeComparisonResult Unavailable(string failure)
        => new(
            RoundTripScopeComparisonStatus.Unavailable,
            Cluster: null,
            All: null,
            Members: [],
            failure);
}
