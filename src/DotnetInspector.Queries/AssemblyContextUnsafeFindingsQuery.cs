using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record UnsafeFindingPublicMember(
    string TypeDefinitionId,
    string Member,
    string StableSelector,
    string BodyMember,
    string BodySelector,
    int BodyToken);

public sealed record AssemblyUnsafeFinding(
    SafetyFact Finding,
    UnsafeFindingPublicMember? PublicMember);

public sealed record AssemblyUnsafeFindings(
    ImmutableArray<AssemblyUnsafeFinding> Findings,
    ImmutableArray<AnalysisDiagnostic> Diagnostics,
    ImmutableArray<ApiSurfaceInspectionFailure>
        ApiSurfaceInspectionFailures)
{
    public int TotalFindings => Findings.Length;

    public int NonPublicFindings =>
        Findings.Count(finding => finding.PublicMember is null);
}

/// <summary>
/// Produces an ungraded unsafe-finding census for one assembly-context
/// participant and attributes physical bodies to product-issued public API
/// identities.
/// </summary>
public static class AssemblyContextUnsafeFindingsQuery
{
    public static InspectionQuery<
        AssemblyContextEntry<AssemblyUnsafeFindings>> Definition { get; } =
        new(
            "Assembly context unsafe findings",
            InspectionCost.Unbounded);

    public static AssemblyContextEntry<AssemblyUnsafeFindings>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);

        AssemblyContextEntry<AssemblyContextPublicMemberInventory>
            publicMembers =
                AssemblyContextQueryExecutor.ExecuteParticipant(
                    group,
                    participant,
                    AssemblyContextPublicMemberAttribution
                        .ProjectPrimary);
        return publicMembers switch
        {
            AssemblyContextEntry<
                AssemblyContextPublicMemberInventory>.Rejected
                rejected =>
                new AssemblyContextEntry<
                    AssemblyUnsafeFindings>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<
                AssemblyContextPublicMemberInventory>.Failed
                failed =>
                new AssemblyContextEntry<
                    AssemblyUnsafeFindings>.Failed(
                        failed.Subject,
                        failed.Error),
            AssemblyContextEntry<
                AssemblyContextPublicMemberInventory>.Available
                available =>
                AnalyzeParticipant(
                    group,
                    participant,
                    available),
            _ => throw new InvalidOperationException(
                $"Unknown public-member entry "
                    + $"'{publicMembers.GetType().Name}'."),
        };
    }

    static AssemblyContextEntry<AssemblyUnsafeFindings>
        AnalyzeParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextEntry<
                AssemblyContextPublicMemberInventory>.Available
                    publicMembers) =>
        AssemblyContextQueryExecutor.ExecuteParticipantOverSnapshot(
            group,
            participant,
            (subject, snapshot) =>
            {
                EnsureSameSubject(subject, publicMembers.Subject);
                return Analyze(
                    group,
                    subject,
                    snapshot,
                    publicMembers.Value);
            });

    static AssemblyUnsafeFindings Analyze(
        AssemblyContextGroup group,
        AssemblyContextSubject subject,
        AssemblyImageSnapshot snapshot,
        AssemblyContextPublicMemberInventory publicMembers)
    {
        LibraryCallGraphAnalysisResult? callGraph = null;
        try
        {
            var resolver = AssemblyContextAnalysisSource.Resolver(
                group,
                subject);
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecuteImage(
                    AssemblyContextAnalysisSource.Name(subject),
                    snapshot.Content,
                    LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures.MethodEvidence),
                    resolver);
            callGraph = execution.CallGraph;
            ImmutableArray<SafetyFact> findings =
                SemanticFactProjection.SafetyFacts(
                    execution.Safety.Evidence,
                    execution.Safety.Occurrences.Values
                        .SelectMany(static occurrences =>
                            occurrences));
            var result = new AssemblyUnsafeFindings(
                [
                    .. findings
                        .Select(finding =>
                            new AssemblyUnsafeFinding(
                                finding,
                                PublicMember(
                                    finding.Method.MetadataToken,
                                    publicMembers.ByBodyToken)))
                        .OrderBy(
                            finding =>
                                finding.PublicMember?.TypeDefinitionId
                                    ?? finding.Finding.Method.DeclaringType
                                        .ToQualifiedDisplayString(),
                            StringComparer.Ordinal)
                        .ThenBy(
                            finding =>
                                finding.PublicMember?.StableSelector
                                    ?? finding.Finding.Method.Name,
                            StringComparer.Ordinal)
                        .ThenBy(
                            finding =>
                                finding.Finding.Method.MetadataToken)
                        .ThenBy(
                            finding =>
                                finding.Finding.ILOffset ?? -1)
                        .ThenBy(
                            finding =>
                                finding.Finding.SafetyKind,
                            StringComparer.Ordinal),
                ],
                execution.Receipt.Diagnostics,
                publicMembers.InspectionFailures);
            resolver.ValidateForPublication();
            return result;
        }
        finally
        {
            callGraph?.ReleaseCaches();
        }
    }

    static UnsafeFindingPublicMember? PublicMember(
        int bodyToken,
        IReadOnlyDictionary<
            int,
            AssemblyContextPublicMember> publicMembers) =>
        publicMembers.TryGetValue(
            bodyToken,
            out AssemblyContextPublicMember? publicMember)
            ? new(
                publicMember.TypeDefinitionId,
                publicMember.Member,
                publicMember.StableSelector,
                publicMember.BodyMember,
                publicMember.BodySelector,
                publicMember.BodyToken)
            : null;

    static void EnsureSameSubject(
        AssemblyContextSubject actual,
        AssemblyContextSubject expected)
    {
        if (!ReferenceEquals(
                actual.Registration,
                expected.Registration))
        {
            throw new InspectionQueryException(
                "Unsafe-finding public members belong to another "
                    + "assembly-context participant.");
        }
    }
}
