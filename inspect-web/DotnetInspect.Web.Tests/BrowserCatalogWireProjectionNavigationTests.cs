using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspect.Web.Interop.Catalog;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserCatalogWireProjectionNavigationTests
{
    [Fact]
    public void FailedHierarchyEvidencePreservesExactDiagnostic()
    {
        var workspace = new NavigationConsumerSubject(
            "workspace",
            StructuralSubjectKind.Workspace,
            "Workspace",
            Summary: null,
            Parent: null);
        var diagnostic = new NavigationConsumerDiagnostic(
            NavigationDiagnosticKind.InspectionFailed,
            "library",
            "decode member: invalid");
        var descriptor = new NavigationConsumerSubjectDescriptor(
            StructuralSubjectKind.Member,
            "Member",
            Subject: null,
            NavigationDescriptorState.Failed,
            IsActive: false,
            IsRetained: false,
            [diagnostic],
            Action: null);
        var lensOutcome = new NavigationConsumerLensOutcome(
            NavigationOutcomeKind.Unavailable,
            NavigationLensBasisKind.Recommendation,
            workspace,
            EffectiveLens: null,
            Request: null,
            PreferredRole: null,
            PolicyFailure: null,
            Resolution: null);
        var snapshot = new NavigationConsumerSnapshot(
            "generation",
            new NavigationConsumerScopeStatus(
                NavigationScopeSnapshotKind.Current),
            workspace,
            ActivePackage: null,
            workspace,
            TypeInventoryLibraryContext: null,
            Packages: [],
            Hierarchy: [descriptor],
            Libraries: [],
            Types: [],
            Members: [],
            Lenses: [],
            lensOutcome,
            Diagnostics: ImmutableArray<NavigationConsumerDiagnostic>.Empty);
        var result = new NavigationConsumerResult(
            NavigationOperationKind.Initialize,
            "request",
            snapshot,
            new NavigationConsumerOutcome(NavigationOutcomeKind.Applied),
            NavigationSynchronizationDisposition.Current,
            Authority: null);

        BrowserRetainedNavigationResult projected =
            BrowserCatalogWireProjection.Project(result);

        BrowserRetainedNavigationSubjectDescriptor member =
            Assert.Single(projected.Snapshot.Hierarchy);
        BrowserRetainedNavigationDiagnostic evidence =
            Assert.Single(member.Evidence);
        Assert.Equal("InspectionFailed", evidence.Kind);
        Assert.Equal("library", evidence.Library);
        Assert.Equal("decode member: invalid", evidence.Message);
    }
}
