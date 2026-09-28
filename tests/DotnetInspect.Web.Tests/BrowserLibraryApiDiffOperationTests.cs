using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using DotnetInspect.Web.Interop.Metadata;

namespace DotnetInspect.Web.Tests;

[CollectionDefinition(
    "Library API diff operations",
    DisableParallelization = true)]
public sealed class BrowserLibraryApiDiffOperationCollection;

[Collection("Library API diff operations")]
[SupportedOSPlatform("browser")]
public sealed class BrowserLibraryApiDiffOperationTests
{
    const string Framework = "net11.0";
    const string TargetVersion = "1.0.0";
    const string CurrentVersion = "2.0.0";
    const string AssemblyName = "LibraryApiDiffFixture.dll";

    [Fact]
    public async Task MicrosoftAzureSignalR_DoesNotRequireFrameworkConstraintResolution()
    {
        const string packageId = "Microsoft.Azure.SignalR";
        const string version = "1.33.1";
        const string framework = "net8.0";
        byte[] package = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "LibraryApiDiff",
                "microsoft.azure.signalr.1.33.1.nupkg"),
            TestContext.Current.CancellationToken);
        await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
            new BrowserPackage(
                packageId,
                version,
                package,
                fromCache: false));

        BrowserInspectionScope scope;
        await using (BrowserScopeLease<BrowserInspectionScope> lease =
            await BrowserPackageWorkspace.OpenScopeAsync(
                packageId,
                version,
                framework,
                TestContext.Current.CancellationToken))
        {
            scope = lease.Scope;
        }

        try
        {
            BrowserPackageCoordinate coordinate =
                Assert.Single(scope.Coordinates);
            string compileAssetId =
                coordinate.CompileAsset("Microsoft.Azure.SignalR.dll").Id;
            var request = new BrowserLibraryApiDiffRequest(
                1,
                packageId,
                version,
                version,
                framework,
                compileAssetId);
            string requestJson = JsonSerializer.Serialize(
                request,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffRequest);

            string resultJson = await MetadataExports.QueryLibraryApiDiff(
                Guid.NewGuid().ToString(),
                requestJson);
            BrowserLibraryApiDiffResult result =
                JsonSerializer.Deserialize(
                    resultJson,
                    BrowserMetadataJsonContext.Default
                        .BrowserLibraryApiDiffResult)!;

            Assert.Equal(
                BrowserLibraryApiDiffResultKind.Succeeded,
                result.Kind);
            BrowserLibraryApiDiffSucceeded value =
                Assert.IsType<BrowserLibraryApiDiffSucceeded>(result.Value);
            Assert.Empty(value.Target.Issues);
            Assert.Empty(value.Current.Issues);
            Assert.Empty(value.Types);
        }
        finally
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(scope);
        }
    }

    [Fact]
    public async Task ExportProjectsCompleteProducerOrderedChangedTypeInventory()
    {
        await using Fixture fixture = await Fixture.Open();

        BrowserLibraryApiDiffResult result =
            await fixture.Query(fixture.Request());

        Assert.Equal(1, result.SchemaVersion);
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        Assert.Null(result.Unavailable);
        Assert.Null(result.Rejected);
        Assert.Null(result.FailureKind);
        BrowserLibraryApiDiffSucceeded value =
            Assert.IsType<BrowserLibraryApiDiffSucceeded>(result.Value);
        Assert.Equal("LibraryApiDiffFixture", value.LibraryDisplay);
        Assert.Equal(TargetVersion, value.Target.Version);
        Assert.Equal(CurrentVersion, value.Current.Version);
        Assert.Equal(
            BrowserLibraryApiDiffSurfaceScope.Public,
            value.Target.Scope);
        Assert.Equal(
            BrowserLibraryApiDiffSurfaceScope.Public,
            value.Current.Scope);
        Assert.Equal(fixture.CompileAssetId, value.Target.Asset.Id);
        Assert.Equal(fixture.CompileAssetId, value.Current.Asset.Id);
        Assert.Equal(
            "lib/net11.0/LibraryApiDiffFixture.dll",
            value.Target.Asset.Path);
        Assert.Empty(value.Target.Issues);
        Assert.Empty(value.Current.Issues);
        Assert.Equal(value.Aggregate.ChangedTypeCount, value.Types.Length);
        Assert.Equal(
            value.Types.Select(type => type.DocumentIdentifier),
            value.Types.Select(type =>
                type.After?.Identifier ?? type.Before!.Identifier));

        BrowserLibraryApiDiffType removed = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.RemovedType");
        Assert.Equal(
            BrowserLibraryApiDiffTypeState.Deletion,
            removed.State);
        Assert.Equal(3, removed.ChangedMemberCount);
        Assert.Equal(1, removed.BreakingCount);
        Assert.Null(removed.TypeDefinitionChanged);
        Assert.Null(removed.After);
        Assert.Equal(
            "LibraryApiDiffFixture.RemovedType",
            removed.Before!.Identifier);
        Assert.Equal("LibraryApiDiffFixture", removed.Before.Namespace);
        Assert.Equal(["RemovedType"], removed.Before.Segments);
        Assert.Equal(3, removed.Members.Length);
        Assert.All(
            removed.Members,
            member =>
            {
                Assert.Equal(
                    BrowserLibraryApiDiffMemberPairKind.Removed,
                    member.PairKind);
                Assert.Equal(
                    BrowserLibraryApiDiffMemberRelationRole.Before,
                    member.Role);
                Assert.NotNull(member.Before);
                Assert.Null(member.After);
                Assert.Equal(
                    BrowserLibraryApiDiffExploreDestinationKind.MemberDiff,
                    member.Explore!.Kind);
                Assert.Equal(member.Before, member.Explore.Target.Member);
                Assert.Null(member.Explore.Current.Member);
            });

        BrowserLibraryApiDiffType added = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.AddedType");
        Assert.Equal(
            BrowserLibraryApiDiffTypeState.Addition,
            added.State);
        Assert.Equal(3, added.ChangedMemberCount);
        Assert.Equal(1, added.AdditiveCount);
        Assert.Null(added.TypeDefinitionChanged);
        Assert.Null(added.Before);
        Assert.Equal(
            "LibraryApiDiffFixture.AddedType",
            added.After!.Identifier);
        Assert.Equal(3, added.Members.Length);
        Assert.All(
            added.Members,
            member =>
            {
                Assert.Equal(
                    BrowserLibraryApiDiffMemberPairKind.Added,
                    member.PairKind);
                Assert.Equal(
                    BrowserLibraryApiDiffMemberRelationRole.After,
                    member.Role);
                Assert.Null(member.Before);
                Assert.NotNull(member.After);
                Assert.Equal(
                    BrowserLibraryApiDiffExploreDestinationKind.MemberDiff,
                    member.Explore!.Kind);
                Assert.Null(member.Explore.Target.Member);
                Assert.Equal(member.After, member.Explore.Current.Member);
            });

        BrowserLibraryApiDiffType definitionOnly = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.TypeDefinitionOnly");
        Assert.Equal(
            BrowserLibraryApiDiffTypeState.Diff,
            definitionOnly.State);
        Assert.True(definitionOnly.TypeDefinitionChanged);
        Assert.Equal(0, definitionOnly.ChangedMemberCount);
        Assert.NotNull(definitionOnly.Before);
        Assert.NotNull(definitionOnly.After);
        Assert.Empty(definitionOnly.Members);

        BrowserLibraryApiDiffType receiver = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.ProjectionReceiver");
        BrowserLibraryApiDiffType extensions = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.ProjectionExtensions");
        Assert.Equal(1, receiver.ChangedMemberCount);
        Assert.Equal(1, extensions.ChangedMemberCount);
        BrowserLibraryApiDiffMember receiverMoved = Assert.Single(
            receiver.Members,
            member => member.Role
                == BrowserLibraryApiDiffMemberRelationRole.After);
        BrowserLibraryApiDiffMember extensionMoved = Assert.Single(
            extensions.Members,
            member => member.DocumentIdentifier
                == receiverMoved.DocumentIdentifier);
        Assert.Equal(
            BrowserLibraryApiDiffMemberPairKind.Changed,
            receiverMoved.PairKind);
        Assert.Equal(
            BrowserLibraryApiDiffMemberRelationRole.Before,
            extensionMoved.Role);
        Assert.Equal(receiverMoved.Before, extensionMoved.Before);
        Assert.Equal(receiverMoved.After, extensionMoved.After);
        Assert.Equal(receiverMoved.Explore, extensionMoved.Explore);
        Assert.Equal(receiverMoved.Before, receiverMoved.Explore!.Target.Member);
        Assert.Equal(receiverMoved.After, receiverMoved.Explore.Current.Member);
        Assert.Equal(fixture.PackageId, receiverMoved.Explore.Target.PackageId);
        Assert.Equal(TargetVersion, receiverMoved.Explore.Target.Version);
        Assert.Equal(CurrentVersion, receiverMoved.Explore.Current.Version);
        Assert.Equal(
            fixture.CompileAssetId,
            receiverMoved.Explore.Target.Asset.Id);
        Assert.Equal(
            value.Target.Assembly,
            receiverMoved.Explore.Target.Assembly);
        Assert.Equal(
            "LibraryApiDiffFixture.ProjectionExtensions",
            receiverMoved.Before!.DeclaringTypeIdentifier);
        Assert.Equal(
            "LibraryApiDiffFixture.ProjectionReceiver",
            receiverMoved.After!.DeclaringTypeIdentifier);
        Assert.Equal("Transform", receiverMoved.Before.MemberName);
        Assert.Equal("Transform", receiverMoved.After.MemberName);
        // Both placements of one relation carry the same correspondence
        // provenance, and a projected match is always a bounded soft match.
        Assert.Equal(receiverMoved.Match, extensionMoved.Match);
        if (receiverMoved.Match is { } match)
        {
            Assert.NotEmpty(match.Tier);
            Assert.InRange(match.Confidence, 1, 99);
        }
        Assert.NotEmpty(receiverMoved.After.StableSelector);
        Assert.NotEmpty(receiverMoved.After.CanonicalSignature);
        Assert.Equal(10, receiverMoved.After.Fingerprint.Length);
        Assert.Equal(
            new BrowserLibraryApiDiffAggregate(8, 1, 1, 10, 5, 3, 0),
            value.Aggregate);
    }

    [Fact]
    public async Task ExportPlacesCompatibilityChangesOnTheirTypeAndMember()
    {
        await using Fixture fixture = await Fixture.Open();

        BrowserLibraryApiDiffResult result =
            await fixture.Query(fixture.Request());
        BrowserLibraryApiDiffSucceeded value =
            Assert.IsType<BrowserLibraryApiDiffSucceeded>(result.Value);

        // Member-level changes land on the Member relation they describe, with
        // the producer's classification and message; the Type keeps none of them.
        BrowserLibraryApiDiffType hard = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.HardChangedType");
        Assert.Empty(hard.Changes);
        BrowserLibraryApiDiffMember first = Assert.Single(hard.Members);
        BrowserLibraryApiDiffChange virtualRemoved = Assert.Single(
            first.Changes,
            change => change.Kind
                == BrowserLibraryApiDiffChangeKind.VirtualRemoved);
        Assert.Equal(
            BrowserLibraryApiDiffChangeClassification.Breaking,
            virtualRemoved.Classification);
        Assert.Equal(
            BrowserLibraryApiDiffChangeCategory.Signature,
            virtualRemoved.Category);

        BrowserLibraryApiDiffType constraintChange = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.MethodConstraintChange");
        Assert.Empty(constraintChange.Changes);
        BrowserLibraryApiDiffMember constrainedMethod =
            Assert.Single(constraintChange.Members);
        Assert.Collection(
            constrainedMethod.Changes,
            tightened =>
            {
                Assert.Equal(
                    BrowserLibraryApiDiffChangeKind
                        .TypeParameterConstraintTightened,
                    tightened.Kind);
                Assert.Equal(
                    "LibraryApiDiffFixture.Dependency.AfterConstraint",
                    tightened.NewValue);
            },
            loosened =>
            {
                Assert.Equal(
                    BrowserLibraryApiDiffChangeKind
                        .TypeParameterConstraintLoosened,
                    loosened.Kind);
                Assert.Equal(
                    "LibraryApiDiffFixture.Dependency.BeforeConstraint",
                    loosened.OldValue);
            });
        Assert.NotEmpty(virtualRemoved.Message);
        Assert.Equal(hard.BreakingCount, first.Changes.Length);

        // A definition-only change is a fact without a classified change row; the
        // Browser must show it from TypeDefinitionChanged, not from Changes.
        BrowserLibraryApiDiffType definitionOnly = Assert.Single(
            value.Types,
            type => type.Display
                == "LibraryApiDiffFixture.TypeDefinitionOnly");
        Assert.True(definitionOnly.TypeDefinitionChanged);
        Assert.Empty(definitionOnly.Changes);
        Assert.Empty(definitionOnly.Members);

        // Whole-Type additions and removals are Type-level changes: the Type
        // entry carries the one classified change and its Members carry none.
        BrowserLibraryApiDiffType added = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.AddedType");
        BrowserLibraryApiDiffChange typeAdded = Assert.Single(added.Changes);
        Assert.Equal(BrowserLibraryApiDiffChangeKind.TypeAdded, typeAdded.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffChangeClassification.Additive,
            typeAdded.Classification);
        Assert.All(added.Members, member => Assert.Empty(member.Changes));
        BrowserLibraryApiDiffType removed = Assert.Single(
            value.Types,
            type => type.Display == "LibraryApiDiffFixture.RemovedType");
        BrowserLibraryApiDiffChange typeRemoved = Assert.Single(removed.Changes);
        Assert.Equal(
            BrowserLibraryApiDiffChangeKind.TypeRemoved,
            typeRemoved.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffChangeClassification.Breaking,
            typeRemoved.Classification);
        Assert.All(removed.Members, member => Assert.Empty(member.Changes));
        int placed = value.Types.Sum(type =>
            type.Changes.Length
            + type.Members.Sum(member => member.Changes.Length));
        int issued = value.Types.Sum(type =>
            type.BreakingCount + type.AdditiveCount + type.PotentiallyBreakingCount);
        Assert.Equal(issued, placed);
    }

    [Fact]
    public async Task SameVersionIsSuccessfulAndEmpty()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserLibraryApiDiffRequest request =
            fixture.Request(
                currentVersion: TargetVersion,
                targetVersion: TargetVersion);

        BrowserLibraryApiDiffResult result =
            await fixture.Query(request);

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        BrowserLibraryApiDiffSucceeded value =
            Assert.IsType<BrowserLibraryApiDiffSucceeded>(result.Value);
        Assert.Equal(TargetVersion, value.Target.Version);
        Assert.Equal(TargetVersion, value.Current.Version);
        Assert.Empty(value.Types);
        Assert.Equal(
            new BrowserLibraryApiDiffAggregate(0, 0, 0, 0, 0, 0, 0),
            value.Aggregate);
    }

    [Fact]
    public void IncompleteEndpointRemainsTypedUnavailable()
    {
        BrowserLibraryApiDiffRequest request = Request("Unavailable.Package");
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var truncation = new ApiSurfaceProjectionTruncation(
            ApiSurfaceProjectionLimit.Types,
            Bound: 1,
            ProjectedParticipants: 1,
            OmittedParticipants: 0,
            ProjectedTypes: 1,
            ProjectedMembers: 0,
            ProjectedInspectionFailures: 0,
            ProjectedTypeForwarders: 0,
            InspectedMetadataRows: 12,
            ProjectedRetainedTextCharacters: 24);
        var target = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: false,
            [new LibraryApiDiffEndpointIssue.Truncated(truncation)]);
        var current = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);
        var unavailable = new LibraryApiDiffOutcome.Unavailable(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            target,
            current);

        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Inspection(unavailable),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Unavailable,
            result.Kind);
        Assert.Null(result.Value);
        Assert.Null(result.Rejected);
        BrowserLibraryApiDiffUnavailable wire =
            Assert.IsType<BrowserLibraryApiDiffUnavailable>(
                result.Unavailable);
        Assert.Equal(
            BrowserLibraryApiDiffUnavailableKind.TargetIncomplete,
            wire.Kind);
        Assert.False(wire.Target.IsComplete);
        Assert.True(wire.Current.IsComplete);
        BrowserLibraryApiDiffEndpointIssue issue =
            Assert.Single(wire.Target.Issues);
        Assert.Equal(
            BrowserLibraryApiDiffEndpointIssueKind.Truncated,
            issue.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffProjectionLimit.Types,
            issue.Truncation!.Limit);
        Assert.Equal(1, issue.Truncation.Bound);
        Assert.Empty(wire.Current.Issues);
    }

    [Fact]
    public void InspectionFailureEvidenceSurvivesTheBrowserProjection()
    {
        BrowserLibraryApiDiffRequest request = Request("Unavailable.Package");
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var dependency = new AssemblyReferenceIdentity(
            "Dependency",
            new Version(2, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);
        var failures = new LibraryApiDiffEndpointIssue.InspectionFailures(1)
        {
            Details =
            [
                new LibraryApiDiffInspectionFailure(
                    new InertString(TextPolicy.Field, "generic-constraint"),
                    0x02000001,
                    MetadataTypeNameFailureMechanism.Signature,
                    new InertString(TextPolicy.Field, "MalformedSignature"),
                    new InertString(TextPolicy.Field, "constraint failed"),
                    identity,
                    dependency),
            ],
        };
        var target = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: false,
            [failures]);
        var current = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);

        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Inspection(new LibraryApiDiffOutcome.Unavailable(
                    LibraryApiDiffUnavailableKind.BeforeIncomplete,
                    target,
                    current)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        BrowserLibraryApiDiffEndpointIssue issue = Assert.Single(
            result.Unavailable!.Target.Issues);
        Assert.Equal(1, issue.Count);
        BrowserLibraryApiDiffInspectionFailure failure = Assert.Single(
            issue.InspectionFailures!);
        Assert.Equal("generic-constraint", failure.Operation);
        Assert.Equal(0x02000001, failure.SubjectToken);
        Assert.Equal(
            BrowserLibraryApiDiffInspectionFailureMechanism.Signature,
            failure.Mechanism);
        Assert.Equal("MalformedSignature", failure.Kind);
        Assert.Equal("constraint failed", failure.Detail);
        Assert.Equal("LibraryApiDiffFixture", failure.SubjectAssembly!.Name);
        Assert.Equal("Dependency", failure.DependencyAssembly!.Name);
    }

    [Fact]
    public async Task ExactCompileAssetMismatchFailsWithoutNameFallback()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserLibraryApiDiffRequest request =
            fixture.Request() with
            {
                CompileAssetId =
                    "compile:lib/net11.0/NotTheSelectedAsset.dll",
            };

        BrowserLibraryApiDiffResult result =
            await fixture.Query(request);

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Failed,
            result.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffFailureKind.Expected,
            result.FailureKind);
        Assert.Null(result.Value);
        Assert.Contains("exact compile asset", result.Error);
        Assert.Contains("target package endpoint", result.Error);
        Assert.Null(result.Inspection);
    }

    [Fact]
    public void CompleteBaselineCanExceedTransportWhenTheViewFits()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult admitted =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(500),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            admitted.Kind);
        Assert.Equal(
            500,
            admitted.Value!.Types.Length);
        Assert.NotNull(admitted.Inspection);

        BrowserLibraryApiDiffResult rejected =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(
                    BrowserLibraryApiDiffWireProjection.MaxChangedTypes),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Rejected,
            rejected.Kind);
        Assert.Null(rejected.Value);
        Assert.Null(rejected.Unavailable);
        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(
                rejected.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind
                .CollectionEntryLimitExceeded,
            evidence.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection.MaxOrdinaryWorkerCollectionEntries,
            evidence.Bound);
        Assert.True(evidence.Observed > evidence.Bound);
        Assert.Null(rejected.Inspection);
    }

    [Fact]
    public void OversizedContentTextRejectsTheWholeBaselineAndInventory()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(2_000, new string('x', 4_000)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Rejected,
            result.Kind);
        Assert.Null(result.Value);
        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(result.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.SerializedResultLimitExceeded,
            evidence.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection.MaxOrdinaryWorkerJsonCharacters,
            evidence.Bound);
        Assert.True(
            evidence.Observed
                > BrowserLibraryApiDiffWireProjection.MaxOrdinaryWorkerJsonCharacters);
        Assert.Null(result.Inspection);
    }

    [Fact]
    public void MemberInventoryTextCannotExceedWireAdmission()
    {
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                Request("Transport.Package"),
                AvailableWithMembers(
                    memberCount: 1,
                    memberDisplay: new string('x', 3_100_000)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(BrowserLibraryApiDiffResultKind.Rejected, result.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.TypeTextLimitExceeded,
            result.Rejected!.Kind);
        Assert.Null(result.Value);
        Assert.NotNull(result.Inspection);
    }

    [Fact]
    public void CompleteMemberBaselineCanExceedTransportBeforeViewProjection()
    {
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                Request("Transport.Package"),
                AvailableWithMembers(memberCount: 12_000),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(BrowserLibraryApiDiffResultKind.Rejected, result.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.CollectionEntryLimitExceeded,
            result.Rejected!.Kind);
        Assert.Null(result.Value);
        Assert.Null(result.Rejected.Target);
        Assert.Null(result.Rejected.Current);
        Assert.Null(result.Inspection);
    }

    [Fact]
    public void NestedTypeSegmentsCannotExceedWorkerCollectionAdmission()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(
                    BrowserLibraryApiDiffWireProjection.MaxChangedTypes,
                    segmentCount: 15),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(result.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.CollectionEntryLimitExceeded,
            evidence.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection
                .MaxOrdinaryWorkerCollectionEntries,
            evidence.Bound);
        Assert.True(
            evidence.Observed
                > BrowserLibraryApiDiffWireProjection
                    .MaxOrdinaryWorkerCollectionEntries);
        Assert.Null(evidence.Target);
        Assert.Null(evidence.Current);
    }

    [Fact]
    public void ExactWorkerCollectionBoundaryIsInclusive()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        InspectionEnvelope<LibraryApiDiffOutcome> baseline = Available(0);
        var endpoint = Assert.IsType<LibraryApiDiffOutcome.Available>(
            baseline.Content).Document.After;
        var diagnostic = new InspectionDiagnostic(
            "code", InspectionDiagnosticSeverity.Warning, "summary");
        var issue = new LibraryApiDiffEndpointIssue.Failed(
            new InertString(TextPolicy.Field, "Endpoint failed."));
        BrowserLibraryApiDiffResult Project(int issueCount, int diagnosticCount) =>
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                new InspectionEnvelope<LibraryApiDiffOutcome>(
                    new LibraryApiDiffOutcome.Unavailable(
                        LibraryApiDiffUnavailableKind.BeforeIncomplete,
                        new LibraryApiDiffEndpointSummary(
                            endpoint.Identity, endpoint.Scope, IsComplete: false,
                            [.. Enumerable.Repeat<LibraryApiDiffEndpointIssue>(issue, issueCount)]),
                        endpoint),
                    baseline.Share,
                    Enumerable.Repeat(diagnostic, diagnosticCount)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        // An issue contributes 13 entries across Content and view; a diagnostic
        // contributes six. These valid populations can hit the exact bound.
        const int bound = BrowserLibraryApiDiffWireProjection.MaxOrdinaryWorkerCollectionEntries;
        for (int issueCount = 1; issueCount <= 6; issueCount++)
        {
            long fixedEntries = WorkerCollectionEntries(Project(issueCount, 0));
            if ((bound - fixedEntries) % 6 != 0)
                continue;
            int diagnosticCount = (int)((bound - fixedEntries) / 6);
            BrowserLibraryApiDiffResult admitted = Project(issueCount, diagnosticCount);
            Assert.Equal(BrowserLibraryApiDiffResultKind.Unavailable, admitted.Kind);
            Assert.NotNull(admitted.Inspection);
            Assert.Equal(bound, WorkerCollectionEntries(admitted));
            Assert.Equal(diagnosticCount, admitted.Inspection.Diagnostics.Length);

            BrowserLibraryApiDiffResult rejected = Project(issueCount + 1, diagnosticCount - 2);
            Assert.Equal(
                BrowserLibraryApiDiffRejectionKind.CollectionEntryLimitExceeded,
                rejected.Rejected!.Kind);
            Assert.Equal(bound + 1, rejected.Rejected.Observed);
            Assert.Null(rejected.Inspection);
            return;
        }
        Assert.Fail("The fixture did not reach the exact collection-entry boundary.");
    }

    [Fact]
    public void EscapedJsonCannotExceedTheOrdinaryWorkerTransport()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(1, new string('\u0001', 1_000_000)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Rejected,
            result.Kind);
        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(result.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind
                .SerializedResultLimitExceeded,
            evidence.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffWireProjection
                .MaxOrdinaryWorkerJsonCharacters,
            evidence.Bound);
        Assert.True(
            evidence.Observed
                > BrowserLibraryApiDiffWireProjection
                    .MaxOrdinaryWorkerJsonCharacters);
        Assert.Null(evidence.Target);
        Assert.Null(evidence.Current);
    }

    [Fact]
    public void NonAsciiJsonUsesWorkerCompatibleCharacterAdmission()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(1, new string('\u00e9', 1_000_000)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        string escapedJson = JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        Assert.True(
            escapedJson.Length
                + BrowserLibraryApiDiffWireProjection
                    .OrdinaryWorkerResultTupleOverhead
                > BrowserLibraryApiDiffWireProjection
                    .MaxOrdinaryWorkerJsonCharacters);
    }

    [Fact]
    public void SupplementaryUnicodeUsesWorkerCompatibleCharacterAdmission()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Available(1, SupplementaryLetters(900_000)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        string escapedJson = JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        Assert.True(
            escapedJson.Length
                + BrowserLibraryApiDiffWireProjection
                    .OrdinaryWorkerResultTupleOverhead
                > BrowserLibraryApiDiffWireProjection
                    .MaxOrdinaryWorkerJsonCharacters);
    }

    [Fact]
    public void OversizedEndpointEvidenceProducesABoundedTransportRejection()
    {
        BrowserLibraryApiDiffRequest request = Request("Transport.Package");
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var operation = new InertString(TextPolicy.Field, "constraint");
        var kind = new InertString(TextPolicy.Field, "MalformedSignature");
        var detail = new InertString(TextPolicy.Field, "failure");
        const int failureCount = 60_000;
        var failures =
            new LibraryApiDiffEndpointIssue.InspectionFailures(failureCount)
            {
                Details =
                [
                    .. Enumerable.Range(0, failureCount).Select(index =>
                        new LibraryApiDiffInspectionFailure(
                            operation,
                            0x02000000 + index + 1,
                            MetadataTypeNameFailureMechanism.Signature,
                            kind,
                            detail,
                            SubjectAssembly: null,
                            DependencyAssembly: null)),
                ],
            };
        var target = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: false,
            [failures]);
        var current = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);

        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                request,
                Inspection(new LibraryApiDiffOutcome.Unavailable(
                    LibraryApiDiffUnavailableKind.BeforeIncomplete,
                    target,
                    current)),
                EndpointContext(TargetVersion),
                EndpointContext(CurrentVersion));

        BrowserLibraryApiDiffRejected evidence =
            Assert.IsType<BrowserLibraryApiDiffRejected>(result.Rejected);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.CollectionEntryLimitExceeded,
            evidence.Kind);
        Assert.Null(evidence.Target);
        Assert.Null(evidence.Current);
        Assert.Null(result.Inspection);
        string json = JsonSerializer.Serialize(
            result,
            BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        Assert.True(
            json.Length
                + BrowserLibraryApiDiffWireProjection
                    .OrdinaryWorkerResultTupleOverhead
                <= BrowserLibraryApiDiffWireProjection
                    .MaxOrdinaryWorkerJsonCharacters);
    }

    [Theory]
    [InlineData("available")]
    [InlineData("unavailable")]
    [InlineData("rejected")]
    public void TransportPreservesCanonicalContentShareAndOrderedDiagnostics(
        string outcome)
    {
        var available = Assert.IsType<LibraryApiDiffOutcome.Available>(
            Available(1).Content);
        LibraryApiDiffOutcome content = outcome switch
        {
            "available" => available,
            "unavailable" => new LibraryApiDiffOutcome.Unavailable(
                LibraryApiDiffUnavailableKind.BeforeIncomplete,
                new LibraryApiDiffEndpointSummary(
                    available.Document.Before.Identity,
                    ApiSurfaceScope.Public,
                    IsComplete: false,
                    [new LibraryApiDiffEndpointIssue.Failed(
                        new InertString(TextPolicy.Field, "Endpoint failed."))]),
                available.Document.After),
            _ => new LibraryApiDiffOutcome.Rejected(
                LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
                available.Document.Before,
                available.Document.After),
        };
        var share = new InspectionShare.NonProjectable(
            "comparison/endpoints", "The ordered endpoints cannot be shared.");
        InspectionDiagnostic[] diagnostics =
        [
            new("first", InspectionDiagnosticSeverity.Warning, "<warning>", "T:Widget"),
            new("second", InspectionDiagnosticSeverity.Information, "detail"),
        ];
        var inspection = new InspectionEnvelope<LibraryApiDiffOutcome>(
            content, share, diagnostics);
        BrowserLibraryApiDiffResult result = BrowserLibraryApiDiffWireProjection.Project(
            Request("Envelope.Package"), inspection,
            EndpointContext(TargetVersion), EndpointContext(CurrentVersion));
        Assert.NotNull(result.Inspection);
        Assert.Same(share, result.Inspection.Share);
        Assert.Equal(diagnostics, result.Inspection.Diagnostics);

        string json = JsonSerializer.Serialize(
            result, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult);
        BrowserLibraryApiDiffResult roundTrip = JsonSerializer.Deserialize(
            json, BrowserMetadataJsonContext.Default.BrowserLibraryApiDiffResult)!;
        Assert.NotNull(roundTrip.Inspection);
        JsonElement expectedContent = JsonSerializer.SerializeToElement(
            content, LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome);
        Assert.True(JsonElement.DeepEquals(expectedContent, roundTrip.Inspection.Content));
        var roundTripShare = Assert.IsType<InspectionShare.NonProjectable>(
            roundTrip.Inspection.Share);
        Assert.Equal(share.Path, roundTripShare.Path);
        Assert.Equal(share.Reason.ToString(), roundTripShare.Reason.ToString());
        Assert.Equal(
            diagnostics.Select(item => (item.Code, item.Severity,
                item.Summary.ToString(), item.Correspondence?.ToString())),
            roundTrip.Inspection.Diagnostics.Select(item => (item.Code, item.Severity,
                item.Summary.ToString(), item.Correspondence?.ToString())));
    }

    [Fact]
    public void ViewOnlyRejectionRetainsTheCompleteAvailableBaseline()
    {
        BrowserLibraryApiDiffResult result =
            BrowserLibraryApiDiffWireProjection.Project(
                Request("Envelope.Package"),
                Available(1, new string('x', 2_000_000)),
                EndpointContext(TargetVersion), EndpointContext(CurrentVersion));

        Assert.Equal(BrowserLibraryApiDiffResultKind.Rejected, result.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffRejectionKind.TypeTextLimitExceeded,
            result.Rejected!.Kind);
        Assert.Null(result.Value);
        Assert.NotNull(result.Inspection);
        Assert.Equal(
            "available",
            result.Inspection.Content.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task CancellationWinsOverLateCompletionAndOldIdCannotCancelSuccessor()
    {
        BrowserLibraryApiDiffRequest request = Request("Bridge.Package");
        var firstStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        string firstId = Guid.NewGuid().ToString();
        Task<BrowserLibraryApiDiffResult> first =
            MetadataExports.RunLibraryApiDiffOperationAsync(
                BrowserManagedOperationId.From(firstId),
                () => request,
                async _ =>
                {
                    firstStarted.SetResult();
                    await firstRelease.Task;
                    return new BrowserManagedOperationBodyResult<
                        BrowserLibraryApiDiffResult,
                        string,
                        string>.Succeeded(
                            ProjectedAvailable(request));
                });
        await firstStarted.Task;

        BrowserLibraryApiDiffCancellation cancellation =
            Cancel(firstId, "superseded");
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.Requested,
            cancellation.Kind);
        firstRelease.SetResult();
        BrowserLibraryApiDiffResult canceled = await first;
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Canceled,
            canceled.Kind);
        Assert.Equal("superseded", canceled.Reason);
        Assert.Null(canceled.Value);
        Assert.Null(canceled.Inspection);

        var secondStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        string secondId = Guid.NewGuid().ToString();
        Task<BrowserLibraryApiDiffResult> second =
            MetadataExports.RunLibraryApiDiffOperationAsync(
                BrowserManagedOperationId.From(secondId),
                () => request,
                async _ =>
                {
                    secondStarted.SetResult();
                    await secondRelease.Task;
                    return new BrowserManagedOperationBodyResult<
                        BrowserLibraryApiDiffResult,
                        string,
                        string>.Succeeded(
                            ProjectedAvailable(request));
                });
        await secondStarted.Task;

        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.NotActive,
            Cancel(firstId, "user").Kind);
        Assert.False(second.IsCompleted);
        secondRelease.SetResult();
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            (await second).Kind);
        Assert.Equal(
            BrowserLibraryApiDiffCancellationKind.NotActive,
            Cancel(secondId, "user").Kind);
    }

    [Theory]
    [InlineData("{")]
    [InlineData(
        """
        {
          "schemaVersion": 2,
          "packageId": "Example.Package",
          "currentVersion": "2.0.0",
          "targetVersion": "1.0.0",
          "targetFramework": "net11.0",
          "compileAssetId": "lib/net11.0/Example.dll"
        }
        """)]
    public async Task InvalidRequestJsonReturnsExpectedTypedFailure(
        string requestJson)
    {
        string json = await MetadataExports.QueryLibraryApiDiff(
            Guid.NewGuid().ToString(),
            requestJson);
        BrowserLibraryApiDiffResult result =
            JsonSerializer.Deserialize(
                json,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult)!;

        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Failed,
            result.Kind);
        Assert.Equal(
            BrowserLibraryApiDiffFailureKind.Expected,
            result.FailureKind);
        Assert.Null(result.Request);
        Assert.Null(result.Value);
        Assert.Null(result.Inspection);
        Assert.NotNull(result.Error);
        Assert.NotNull(result.Diagnostic);
    }

    [Fact]
    public async Task SourceGeneratedJsonRoundTripsTheClosedInventoryShape()
    {
        await using Fixture fixture = await Fixture.Open();
        BrowserLibraryApiDiffRequest request = fixture.Request();
        string requestJson = JsonSerializer.Serialize(
            request,
            BrowserMetadataJsonContext.Default
                .BrowserLibraryApiDiffRequest);
        Assert.Equal(
            request,
            JsonSerializer.Deserialize(
                requestJson,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffRequest));

        string json = await MetadataExports.QueryLibraryApiDiff(
            Guid.NewGuid().ToString(),
            requestJson);
        BrowserLibraryApiDiffResult result =
            JsonSerializer.Deserialize(
                json,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult)!;
        Assert.Equal(
            BrowserLibraryApiDiffResultKind.Succeeded,
            result.Kind);
        Assert.NotNull(result.Inspection);
        Assert.IsType<LibraryApiDiffOutcome.Available>(
            result.Inspection.Content.Deserialize(
                LibraryApiDiffJsonContext.Default.LibraryApiDiffOutcome));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal("Succeeded", root.GetProperty("kind").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        JsonElement row = root
            .GetProperty("value")
            .GetProperty("types")[0];
        Assert.Equal(
            [
                "additiveCount",
                "after",
                "before",
                "breakingCount",
                "changedMemberCount",
                "changes",
                "display",
                "documentIdentifier",
                "members",
                "potentiallyBreakingCount",
                "state",
                "typeDefinitionChanged",
            ],
            row.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        JsonElement member = root
            .GetProperty("value")
            .GetProperty("types")
            .EnumerateArray()
            .SelectMany(type =>
                type.GetProperty("members").EnumerateArray())
            .First();
        Assert.Equal(
            [
                "after",
                "before",
                "changes",
                "documentIdentifier",
                "explore",
                "match",
                "pairKind",
                "role",
            ],
            member.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        JsonElement memberIdentity = member.TryGetProperty(
                "after",
                out JsonElement after)
            && after.ValueKind != JsonValueKind.Null
                ? after
                : member.GetProperty("before");
        Assert.Equal(
            [
                "canonicalSignature",
                "declaringTypeIdentifier",
                "display",
                "fingerprint",
                "memberName",
                "stableSelector",
                "typeFullName",
            ],
            memberIdentity.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.False(row.TryGetProperty("selectedType", out _));
    }

    static BrowserLibraryApiDiffCancellation Cancel(
        string operationId,
        string reason) =>
        JsonSerializer.Deserialize(
            MetadataExports.CancelLibraryApiDiff(operationId, reason),
            BrowserMetadataJsonContext.Default
                .BrowserLibraryApiDiffCancellation)!;

    static BrowserLibraryApiDiffResult ProjectedAvailable(
        BrowserLibraryApiDiffRequest request) =>
        BrowserLibraryApiDiffWireProjection.Project(
            request,
            Available(0),
            EndpointContext(request.TargetVersion),
            EndpointContext(request.CurrentVersion));

    static InspectionEnvelope<LibraryApiDiffOutcome> Available(
        int typeCount,
        string? display = null,
        int segmentCount = 1)
    {
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var endpoint = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);
        ImmutableArray<ComparisonSubject<LibraryApiTypeDiff>> subjects =
        [
            .. Enumerable.Range(0, typeCount)
                .Select(index =>
                {
                    MetadataTypeDefinitionName name =
                        TypeName($"Type{index}", segmentCount);
                    var typeIdentity = new LibraryApiTypeIdentity(
                        name,
                        display ?? $"Transport.Type{index}");
                    var diff = new LibraryApiTypeDiff(
                        typeIdentity,
                        typeIdentity,
                        LibraryApiTypePairKind.Present,
                        TypeDefinitionChanged: false,
                        [],
                        []);
                    return new ComparisonSubject<LibraryApiTypeDiff>(
                        typeIdentity.Identifier,
                        display ?? typeIdentity.Display,
                        new ComparisonSubjectChange.Diff(),
                        diff);
                }),
        ];
        var document = new ComparisonDocument<LibraryApiTypeDiff>(
            ComparisonDocument<LibraryApiTypeDiff>.CurrentSchemaVersion,
            SubjectCoordinateBasis.RootRelative,
            "transport-library",
            "Transport",
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<
                LibraryApiTypeDiff>.NotApplicable(),
            subjects,
            []);
        return Inspection(new LibraryApiDiffOutcome.Available(
            new LibraryApiDiffDocument(
                endpoint,
                endpoint,
                new LibraryApiDiffSummary(
                    typeCount,
                    AddedTypeCount: 0,
                    RemovedTypeCount: 0,
                    ChangedMemberCount: 0,
                    BreakingCount: 0,
                    AdditiveCount: 0,
                    PotentiallyBreakingCount: 0),
                document)));
    }

    static InspectionEnvelope<LibraryApiDiffOutcome> AvailableWithMembers(
        int memberCount,
        string? memberDisplay = null)
    {
        AssemblyReferenceIdentity identity = AssemblyIdentity();
        var endpoint = new LibraryApiDiffEndpointSummary(
            identity,
            ApiSurfaceScope.Public,
            IsComplete: true,
            []);
        MetadataTypeDefinitionName name = TypeName("MemberContainer");
        var typeIdentity = new LibraryApiTypeIdentity(
            name,
            "Transport.MemberContainer");
        ImmutableArray<LibraryApiMemberDiff> members =
        [
            .. Enumerable.Range(0, memberCount).Select(index =>
            {
                string memberName = $"Member{index}";
                string canonicalSignature =
                    $"System.Void Transport.MemberContainer::{memberName}()";
                var anchor = new MemberAnchor(
                    $"M:Transport.MemberContainer.{memberName}",
                    canonicalSignature,
                    MemberAnchor.ComputeFingerprint(canonicalSignature),
                    typeIdentity.Identifier,
                    memberName);
                var memberIdentity = new LibraryApiMemberIdentity(
                    typeIdentity,
                    anchor,
                    memberDisplay ?? memberName);
                return new LibraryApiMemberDiff(
                    new LibraryApiMemberRelation(
                        $"member-relation:{index}",
                        LibraryApiMemberPairKind.Changed,
                        memberIdentity,
                        memberIdentity,
                        Match: null),
                    LibraryApiMemberRelationRole.Both);
            }),
        ];
        var type = new LibraryApiTypeDiff(
            typeIdentity,
            typeIdentity,
            LibraryApiTypePairKind.Present,
            TypeDefinitionChanged: false,
            [],
            members);
        var document = new ComparisonDocument<LibraryApiTypeDiff>(
            ComparisonDocument<LibraryApiTypeDiff>.CurrentSchemaVersion,
            SubjectCoordinateBasis.RootRelative,
            "transport-library",
            "Transport",
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<
                LibraryApiTypeDiff>.NotApplicable(),
            [new(
                typeIdentity.Identifier,
                typeIdentity.Display,
                new ComparisonSubjectChange.Diff(),
                type)],
            []);
        return Inspection(new LibraryApiDiffOutcome.Available(
            new LibraryApiDiffDocument(
                endpoint,
                endpoint,
                new LibraryApiDiffSummary(
                    ChangedTypeCount: 1,
                    AddedTypeCount: 0,
                    RemovedTypeCount: 0,
                    ChangedMemberCount: memberCount,
                    BreakingCount: 0,
                    AdditiveCount: 0,
                    PotentiallyBreakingCount: 0),
                document)));
    }

    static InspectionEnvelope<LibraryApiDiffOutcome> Inspection(
        LibraryApiDiffOutcome content) =>
        new(
            content,
            new InspectionShare.NonProjectable(
                "comparison/endpoints",
                "The comparison endpoints cannot be shared."));

    static long WorkerCollectionEntries(BrowserLibraryApiDiffResult result)
    {
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                result,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult));
        return BrowserLibraryApiDiffWireProjection
            .OrdinaryWorkerResultTupleOverhead
            + Count(document.RootElement);

        static long Count(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.Array =>
                    1
                    + element.GetArrayLength()
                    + element.EnumerateArray().Sum(Count),
                JsonValueKind.Object =>
                    1
                    + element.EnumerateObject().Count()
                    + element.EnumerateObject()
                        .Sum(property => Count(property.Value)),
                _ => 0,
            };
    }

    static string SupplementaryLetters(int count) =>
        string.Create(
            checked(count * 2),
            count,
            static (characters, letterCount) =>
            {
                for (int index = 0; index < letterCount; index++)
                {
                    characters[index * 2] = '\ud801';
                    characters[index * 2 + 1] = '\udc00';
                }
            });

    static MetadataTypeDefinitionName TypeName(
        string segment,
        int segmentCount = 1) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "Transport",
                segmentCount == 1
                    ? [segment]
                    :
                    [
                        segment,
                        .. Enumerable.Repeat("T", segmentCount - 1),
                    ])).Name;

    static BrowserLibraryApiDiffRequest Request(string packageId) =>
        new(
            1,
            packageId,
            CurrentVersion,
            TargetVersion,
            Framework,
            "compile:lib/net11.0/LibraryApiDiffFixture.dll");

    static BrowserLibraryApiDiffEndpointContext EndpointContext(
        string version) =>
        new(
            "Projection.Package",
            version,
            Framework,
            "compile:lib/net11.0/LibraryApiDiffFixture.dll",
            "lib/net11.0/LibraryApiDiffFixture.dll",
            AssemblyName);

    static AssemblyReferenceIdentity AssemblyIdentity() =>
        new(
            "LibraryApiDiffFixture",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);

    sealed class Fixture : IAsyncDisposable
    {
        readonly BrowserInspectionScope _targetScope;
        readonly BrowserInspectionScope _currentScope;

        Fixture(
            string packageId,
            string compileAssetId,
            BrowserInspectionScope targetScope,
            BrowserInspectionScope currentScope)
        {
            PackageId = packageId;
            CompileAssetId = compileAssetId;
            _targetScope = targetScope;
            _currentScope = currentScope;
        }

        internal string PackageId { get; }
        internal string CompileAssetId { get; }

        internal static async Task<Fixture> Open()
        {
            string packageId =
                "Library.Api.Diff." + Guid.NewGuid().ToString("N");
            await Register(
                packageId,
                TargetVersion,
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath());
            await Register(
                packageId,
                CurrentVersion,
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath());

            BrowserInspectionScope targetScope;
            await using (BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId,
                    TargetVersion,
                    Framework,
                    TestContext.Current.CancellationToken))
            {
                targetScope = lease.Scope;
            }
            BrowserInspectionScope currentScope;
            await using (BrowserScopeLease<BrowserInspectionScope> lease =
                await BrowserPackageWorkspace.OpenScopeAsync(
                    packageId,
                    CurrentVersion,
                    Framework,
                    TestContext.Current.CancellationToken))
            {
                currentScope = lease.Scope;
            }

            string targetAsset =
                Assert.IsType<DotnetInspector.Packages.PackageCompileAsset>(
                    targetScope.Coordinates[0].DefaultAsset).Id;
            string currentAsset =
                Assert.IsType<DotnetInspector.Packages.PackageCompileAsset>(
                    currentScope.Coordinates[0].DefaultAsset).Id;
            Assert.Equal(targetAsset, currentAsset);
            return new Fixture(
                packageId,
                currentAsset,
                targetScope,
                currentScope);
        }

        internal BrowserLibraryApiDiffRequest Request(
            string currentVersion = CurrentVersion,
            string targetVersion = TargetVersion) =>
            new(
                1,
                PackageId,
                currentVersion,
                targetVersion,
                Framework,
                CompileAssetId);

        internal async Task<BrowserLibraryApiDiffResult> Query(
            BrowserLibraryApiDiffRequest request)
        {
            string requestJson = JsonSerializer.Serialize(
                request,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffRequest);
            string resultJson = await MetadataExports.QueryLibraryApiDiff(
                Guid.NewGuid().ToString(),
                requestJson);
            return JsonSerializer.Deserialize(
                resultJson,
                BrowserMetadataJsonContext.Default
                    .BrowserLibraryApiDiffResult)!;
        }

        public async ValueTask DisposeAsync()
        {
            await BrowserPackageWorkspace.RemoveScopeAsync(_targetScope);
            if (!ReferenceEquals(_targetScope, _currentScope))
            {
                await BrowserPackageWorkspace.RemoveScopeAsync(
                    _currentScope);
            }
        }

        static async Task Register(
            string packageId,
            string version,
            string assemblyPath) =>
            await BrowserPackageWorkspace.RegisterAcquiredPackageAsync(
                new BrowserPackage(
                    packageId,
                    version,
                    Archive(
                        packageId,
                        version,
                        File.ReadAllBytes(assemblyPath)),
                    fromCache: false));

        static byte[] Archive(
            string packageId,
            string version,
            byte[] assembly)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(
                buffer,
                ZipArchiveMode.Create,
                leaveOpen: true))
            {
                using (Stream manifest =
                    archive.CreateEntry($"{packageId}.nuspec").Open())
                {
                    manifest.Write(
                        Encoding.UTF8.GetBytes(
                            $"<package><metadata><id>{packageId}</id>"
                                + $"<version>{version}</version>"
                                + "<authors>Tests</authors>"
                                + "<description>Library API diff fixture"
                                + "</description></metadata></package>"));
                }
                using Stream library = archive.CreateEntry(
                        "lib/net11.0/LibraryApiDiffFixture.dll")
                    .Open();
                library.Write(assembly);
            }
            return buffer.ToArray();
        }
    }
}
