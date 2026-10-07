using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspect.Web.Interop.Analysis;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserResourceTriageProjectionTests
{
    [Fact]
    public void Projection_PreservesPartialEvidenceAndNonProjectableShare()
    {
        var subject = Subject();
        var method = new MethodIdentity("Fixture", Guid.NewGuid(),
            ILInspector.Analysis.TypeRef.Definition("Fixture", "Fixture", "Entry"), "Read", [],
            ILInspector.Analysis.TypeRef.CoreLib("System", "Void"), 0x06000001, true);
        var lifecycle = new ResourceLifecycleOccurrence(method, "ArrayPool<byte>",
            "pool-churn-on-exception", 7, []);
        var finding = new Finding<ResourceLifecycleOccurrence>(
            new FindingSubject("fixture", "fixture"), AnalysisFindings.ResourceLifecycleDescriptor,
            new FindingKey("candidate"), lifecycle);
        var assessment = new ResourceTriageAssessment("rt~1234", finding, [],
            ResourceTriageActionability.Unknown, ResourceTriageReason.NoBoundaryBeforeCleanup,
            ResourceTriageImpact.PoolChurnOnException, ResourceTriageRemediation.EnsureExceptionalCleanup,
            ResourceTriageConfidence.Medium);
        var triage = new ResourceTriageResult.Incomplete(
            new FindingInspection<ResourceLifecycleOccurrence>.Complete([finding]), [assessment],
            [new(ResourceLifecycleLimitationKind.ExceptionFlow, "Incomplete handler", method)]);
        var content = new AssemblyResourceTriageResult(triage,
            ImmutableDictionary<int, ResourceTriagePublicMember>.Empty, []);
        var inspection = new InspectionEnvelope<AssemblyContextEntry<AssemblyResourceTriageResult>>(
            new AssemblyContextEntry<AssemblyResourceTriageResult>.Available(subject, content),
            new InspectionShare.NonProjectable("triage/share", "No canonical projection"),
            [new InspectionDiagnostic("triage.partial", InspectionDiagnosticSeverity.Warning, "Partial evidence")]);

        BrowserResourceTriage result = AnalysisExports.ProjectResourceTriage(inspection);
        Assert.Equal("incomplete", result.Outcome);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("rt~1234", candidate.CandidateId);
        Assert.Equal(method.ModuleVersionId, candidate.ModuleVersionId);
        Assert.Equal(7, candidate.AcquireOffset);
        Assert.Equal(method.MetadataToken, candidate.MethodToken);
        Assert.Null(candidate.StableSelector);
        Assert.Equal("Fixture.Entry.Read()", candidate.Method);
        Assert.Single(result.Limitations);
        Assert.Equal("nonProjectable", result.Share!.Kind);
        Assert.Equal("triage/share", result.Share.Path);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void Projection_PreservesProducerFailureInsteadOfCompleteEmpty()
    {
        var error = new InspectionError(new FindingSubject("fixture", "fixture"),
            AnalysisFindings.ResourceLifecycleDescriptor, "Unreadable lifecycle evidence");
        var content = new AssemblyResourceTriageResult(new ResourceTriageResult.Failed(error),
            ImmutableDictionary<int, ResourceTriagePublicMember>.Empty, []);
        var inspection = new InspectionEnvelope<AssemblyContextEntry<AssemblyResourceTriageResult>>(
            new AssemblyContextEntry<AssemblyResourceTriageResult>.Available(Subject(), content),
            new InspectionShare.NonProjectable("triage/share", "No projection"));
        var result = AnalysisExports.ProjectResourceTriage(inspection);
        Assert.Equal("failed", result.Outcome);
        Assert.Equal(error.Reason, result.InspectionError);
        Assert.Empty(result.Candidates);
    }

    static AssemblyContextSubject Subject() => new(ResolvedAssemblyReference.Create(
        new AssemblyReferenceIdentity("Fixture", new Version(1, 0), null, null), null,
        () => new MemoryStream(), AssemblyResolutionProvenance.Local("fixture")));
}
