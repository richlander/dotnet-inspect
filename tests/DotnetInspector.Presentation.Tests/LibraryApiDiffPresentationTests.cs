using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using Inspector.Artifacts;
using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using Inspector.Findings;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Presentation.Tests;

public sealed class LibraryApiDiffPresentationTests
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    [Fact]
    public async Task Representations_PreserveBothRowGrainsAndPreWindowSummary()
    {
        AssemblyContextApiComparisonResult comparison = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        var selection = new LibraryApiChangesSelection(Rows: MarkoutRowWindow.Head(2));
        var types = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiTypeChangeRow>>.Available>(
            LibraryApiChangesInspection.TypeTable(comparison, selection).Content).Content;
        var typeDocument = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiTypeChangeRow>>.Available>(
            LibraryApiChangesInspection.TypeDocument(comparison, selection).Content).Content;
        var changes = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>.Available>(
            LibraryApiChangesInspection.ChangesTable(comparison, selection).Content).Content;
        var changesDocument = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiChangeRow>>.Available>(
            LibraryApiChangesInspection.ChangesDocument(comparison, selection).Content).Content;
        var complete = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiChangeRow>>.Available>(
            LibraryApiChangesInspection.ChangesDocument(comparison, new()).Content).Content;

        Assert.Equal(2, types.Rows.Length);
        Assert.Equal(2, changes.Rows.Length);
        Assert.Equal(
            JsonSerializer.Serialize(types, LibraryApiChangesJsonContext.Default.TypeTable),
            JsonSerializer.Serialize(new(typeDocument.Rows), LibraryApiChangesJsonContext.Default.TypeTable));
        Assert.Equal(
            JsonSerializer.Serialize(changes, LibraryApiChangesJsonContext.Default.ChangesTable),
            JsonSerializer.Serialize(new(changesDocument.Rows), LibraryApiChangesJsonContext.Default.ChangesTable));
        Assert.Equal(complete.Summary, changesDocument.Summary);
        Assert.True(changesDocument.Summary.ChangedTypeCount > changesDocument.Rows.Length);
        Assert.Equal(typeDocument.Summary, changesDocument.Summary);
        Assert.NotEmpty(complete.Rows.OfType<LibraryApiChangeRow.Unclassified>());
        Assert.Contains(complete.Rows.OfType<LibraryApiChangeRow.Classified>(),
            row => row.Change.Subject.BeforeMember is not null || row.Change.Subject.AfterMember is not null);
    }

    [Fact]
    public async Task Representations_ShareOrdinarySelectionExclusionsAndClassifications()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        AssemblyContextApiComparisonResult comparison = await Compare(
            pair.OldAssemblyPath(), pair.NewAssemblyPath(), scope: ApiSurfaceScope.ApiAxis);
        var table = LibraryApiChangesInspection.ChangesTable(comparison, new());
        var document = LibraryApiChangesInspection.ChangesDocument(comparison, new());
        var rows = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>.Available>(
            table.Content).Content.Rows;
        Assert.Contains(table.Diagnostics, diagnostic => diagnostic.Code == "diff.outside-ordinary");
        Assert.Equal(8,
            Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesDocument<LibraryApiChangeRow>>.Available>(
                document.Content).Content.OutsideOrdinaryRowCount);
        Assert.Equal(table.Diagnostics.Select(diagnostic => diagnostic.Summary.ToString()),
            document.Diagnostics.Select(diagnostic => diagnostic.Summary.ToString()));
        Assert.All(rows.OfType<LibraryApiChangeRow.Classified>(), row => Assert.True(row.Change.IsOrdinary));
        Assert.All(rows.OfType<LibraryApiChangeRow.Unclassified>(), row => Assert.True(row.Evidence.IsOrdinary));
        Assert.Contains(rows.OfType<LibraryApiChangeRow.Classified>(),
            row => row.Change.State == LibraryApiChangeState.Hidden);

        var filtered = Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>.Available>(
            LibraryApiChangesInspection.ChangesTable(comparison,
                new(new(name => name.EndsWith(".PublicToProtected", StringComparison.Ordinal)),
                    [ChangeClassification.Breaking])).Content).Content.Rows;
        Assert.NotEmpty(filtered);
        Assert.All(filtered, row =>
            Assert.Equal(ChangeClassification.Breaking,
                Assert.IsType<LibraryApiChangeRow.Classified>(row).Change.Classification));
    }

    [Fact]
    public async Task Representations_RoundTripStructuredRowsAndTypedFailures()
    {
        AssemblyContextApiComparisonResult available = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(), FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        AssemblyContextApiComparisonResult unavailable = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(), FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            new(1, 1, 1_000_000, 1_000, 1_000_000, 10_000_000));
        AssemblyContextApiComparisonResult rejected = await Compare(
            FixtureCatalog.DiffV1.AssemblyPath(), FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        AssemblyContextApiComparisonResult empty = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(), FixtureCatalog.LibraryApiDiffV1.AssemblyPath());

        foreach (AssemblyContextApiComparisonResult comparison in new[] { available, unavailable, rejected, empty })
        {
            RoundTrip(LibraryApiChangesInspection.TypeTable(comparison, new()).Content,
                LibraryApiChangesJsonContext.Default.TypeTableOutcome);
            RoundTrip(LibraryApiChangesInspection.ChangesTable(comparison, new()).Content,
                LibraryApiChangesJsonContext.Default.ChangesTableOutcome);
            RoundTrip(LibraryApiChangesInspection.TypeDocument(comparison, new()).Content,
                LibraryApiChangesJsonContext.Default.TypeDocumentOutcome);
            RoundTrip(LibraryApiChangesInspection.ChangesDocument(comparison, new()).Content,
                LibraryApiChangesJsonContext.Default.ChangesDocumentOutcome);
        }
        Assert.Empty(Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>.Available>(
            LibraryApiChangesInspection.ChangesTable(empty, new()).Content).Content.Rows);
        Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>.Unavailable>(
            LibraryApiChangesInspection.ChangesTable(unavailable, new()).Content);
        Assert.Equal(LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            Assert.IsType<LibraryApiChangesOutcome<LibraryApiChangesTable<LibraryApiChangeRow>>.Rejected>(
                LibraryApiChangesInspection.ChangesTable(rejected, new()).Content).Failure.Kind);

        static void RoundTrip<T>(T content, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        {
            string json = JsonSerializer.Serialize(content, info);
            T? restored = JsonSerializer.Deserialize(json, info);
            Assert.NotNull(restored);
            Assert.Equal(json, JsonSerializer.Serialize(restored, info));
        }
    }

    [Fact]
    public async Task Create_SameLibrary_ProducesAnEmptyValueComparableDocument()
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();

        LibraryApiDiffOutcome.Available first =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(await Compare(path, path)));
        LibraryApiDiffOutcome.Available second =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(await Compare(path, path)));

        Assert.Empty(first.Document.Comparison.Subjects);
        Assert.Equal(0, first.Document.Summary.ChangedTypeCount);
        Assert.Equal(0, first.Document.Summary.ChangedMemberCount);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(SubjectCoordinateBasis.RootRelative, first.Document.Comparison.SubjectCoordinateBasis);
        Assert.IsType<ComparisonSubjectChange.Diff>(first.Document.Comparison.Change);
        Assert.IsType<ComparisonRootComparison<LibraryApiTypeDiff>.NotApplicable>(
            first.Document.Comparison.Comparison);
    }

    [Fact]
    public async Task Create_RealDiffFixture_RetainsCompatibilityRowsAndDistinctMembers()
    {
        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(
                        FixtureCatalog.DiffV1.AssemblyPath(),
                        FixtureCatalog.DiffV2.AssemblyPath())));

        Assert.True(available.Document.Summary.BreakingCount > 0);
        Assert.True(available.Document.Summary.AdditiveCount > 0);
        Assert.Equal(
            new Version(1, 0, 0, 0),
            available.Document.Before.Identity.Version);
        Assert.Equal(
            new Version(1, 0, 0, 0),
            available.Document.After.Identity.Version);
        ComparisonSubject<LibraryApiTypeDiff> methodRemoval = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "DiffFixtureSample.MethodRemovalSample");
        Assert.Contains(
            methodRemoval.Comparison.CompatibilityChanges,
            change => change.Kind == ChangeKind.MemberRemoved
                && change.Classification == ChangeClassification.Breaking
                && change.Subject.BeforeMember?.Display == "Removed");
        Assert.Equal(2, methodRemoval.Comparison.ChangedMemberCount);
        Assert.Equal(
            methodRemoval.Comparison.Members
                .Select(member => member.Relation.Identifier)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            methodRemoval.Comparison.ChangedMemberCount);
        Assert.Equal(
            2,
            methodRemoval.Comparison.Members
                .Select(member => member.Relation.Before?.Anchor)
                .Distinct()
                .Count());

        LibraryApiTypeDiff bodyState = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "DiffFixtureSample.BodyStateSample").Comparison;
        Assert.Contains(
            bodyState.CompatibilityChanges,
            change => change.Kind == ChangeKind.AbstractRemoved
                && change.Classification == ChangeClassification.Additive);
        Assert.True(bodyState.TypeDefinitionChanged);
    }

    [Fact]
    public async Task Create_SpecializedFixture_PreservesTypeTopologyAndCrossTypeCorrespondence()
    {
        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(
                        FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                        FixtureCatalog.LibraryApiDiffV2.AssemblyPath())));

        Assert.NotEqual(available.Document.Before.Identity.Version, available.Document.After.Identity.Version);
        ComparisonSubject<LibraryApiTypeDiff> removed = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "LibraryApiDiffFixture.RemovedType");
        Assert.IsType<ComparisonSubjectChange.Deletion>(removed.Change);
        Assert.Equal(LibraryApiTypePairKind.Removed, removed.Comparison.PairKind);
        Assert.Null(removed.Comparison.After);
        Assert.Equal(3, removed.Comparison.ChangedMemberCount);
        LibraryApiCompatibilityChange removedTypeChange = Assert.Single(
            removed.Comparison.CompatibilityChanges);
        Assert.Equal(ChangeKind.TypeRemoved, removedTypeChange.Kind);
        Assert.Equal(ChangeClassification.Breaking, removedTypeChange.Classification);

        ComparisonSubject<LibraryApiTypeDiff> added = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "LibraryApiDiffFixture.AddedType");
        Assert.IsType<ComparisonSubjectChange.Addition>(added.Change);
        Assert.Equal(LibraryApiTypePairKind.Added, added.Comparison.PairKind);
        Assert.Null(added.Comparison.Before);
        Assert.Equal(3, added.Comparison.ChangedMemberCount);
        LibraryApiCompatibilityChange addedTypeChange = Assert.Single(
            added.Comparison.CompatibilityChanges);
        Assert.Equal(ChangeKind.TypeAdded, addedTypeChange.Kind);
        Assert.Equal(ChangeClassification.Additive, addedTypeChange.Classification);

        LibraryApiTypeDiff receiver = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "LibraryApiDiffFixture.ProjectionReceiver").Comparison;
        LibraryApiTypeDiff extensions = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "LibraryApiDiffFixture.ProjectionExtensions").Comparison;
        LibraryApiMemberDiff receiverRelation = Assert.Single(
            receiver.Members,
            member => member.Relation.Match?.Tier
                == MetadataFindings.ExtensionInstanceMatchTier);
        LibraryApiMemberDiff extensionRelation = Assert.Single(
            extensions.Members,
            member => member.Relation.Identifier == receiverRelation.Relation.Identifier);
        Assert.Single(receiver.Members);
        Assert.Single(extensions.Members);
        Assert.Equal(LibraryApiMemberRelationRole.After, receiverRelation.Role);
        Assert.Equal(LibraryApiMemberRelationRole.Before, extensionRelation.Role);
        Assert.Equal(
            "LibraryApiDiffFixture.ProjectionExtensions",
            receiverRelation.Relation.Before?.DeclaringType.Display);
        Assert.Equal(
            "LibraryApiDiffFixture.ProjectionReceiver",
            receiverRelation.Relation.After?.DeclaringType.Display);
        Assert.Contains(
            receiver.CompatibilityChanges,
            change => change.Kind == ChangeKind.MemberAdded
                && change.Subject.AfterMember?.Display == "Transform");
        Assert.Contains(
            extensions.CompatibilityChanges,
            change => change.Kind == ChangeKind.MemberRemoved
                && change.Subject.BeforeMember?.Display == "Transform");

        LibraryApiTypeDiff typeDefinitionOnly = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display
                == "LibraryApiDiffFixture.TypeDefinitionOnly").Comparison;
        Assert.Equal(LibraryApiTypePairKind.Changed, typeDefinitionOnly.PairKind);
        Assert.True(typeDefinitionOnly.TypeDefinitionChanged);
        Assert.Empty(typeDefinitionOnly.CompatibilityChanges);
        Assert.Equal(0, typeDefinitionOnly.ChangedMemberCount);
        Assert.Equal(
            available.Document.Summary.ChangedMemberCount,
            available.Document.Comparison.Subjects
                .SelectMany(subject => subject.Comparison.Members)
                .Select(member => member.Relation.Identifier)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            available.Document.Summary.BreakingCount
                + available.Document.Summary.AdditiveCount
                + available.Document.Summary.PotentiallyBreakingCount,
            available.Document.Comparison.Subjects.Sum(
                subject => subject.Comparison.CompatibilityChanges.Length));
    }

    [Fact]
    public async Task Create_IncompleteProjection_ReturnsEndpointSpecificUnavailableEvidence()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            new ApiSurfaceProjectionLimits(
                maxParticipants: 1,
                maxTypes: 1,
                maxMembers: 100,
                maxInspectionFailures: 10,
                maxTypeForwarders: 10,
                maxMetadataRows: 10_000));

        LibraryApiDiffOutcome.Unavailable unavailable =
            Assert.IsType<LibraryApiDiffOutcome.Unavailable>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(LibraryApiDiffUnavailableKind.BothIncomplete, unavailable.Kind);
        Assert.All(
            new[] { unavailable.Before, unavailable.After },
            endpoint =>
            {
                Assert.False(endpoint.IsComplete);
                Assert.Contains(
                    endpoint.Issues,
                    issue => issue is LibraryApiDiffEndpointIssue.Truncated);
            });
    }

    [Fact]
    public async Task Create_RejectedBeforeEndpoint_RetainsTheCompleteAfterEndpoint()
    {
        byte[] healthyBytes =
            File.ReadAllBytes(FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        using var reader = new PEReader(new MemoryStream(healthyBytes, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant rejectedParticipant = new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(new byte[] { 1, 2, 3 }, writable: false),
                AssemblyResolutionProvenance.Local("Rejected Before")),
            policy);
        AssemblyContextParticipant healthyParticipant = Participant(
            healthyBytes,
            "Healthy After",
            policy);
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([rejectedParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([healthyParticipant]);
        AssemblyContextApiComparisonResult result =
            AssemblyContextApiComparisonQuery.Execute(
                beforeGroup,
                rejectedParticipant,
                afterGroup,
                healthyParticipant,
                ApiSurfaceScope.Public,
                GenerousLimits);

        LibraryApiDiffOutcome.Unavailable unavailable =
            Assert.IsType<LibraryApiDiffOutcome.Unavailable>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            unavailable.Kind);
        Assert.False(unavailable.Before.IsComplete);
        Assert.Contains(
            unavailable.Before.Issues,
            issue => issue is LibraryApiDiffEndpointIssue.Rejected);
        Assert.True(unavailable.After.IsComplete);
        Assert.Empty(unavailable.After.Issues);
    }

    [Fact]
    public async Task Create_DegradedSignature_ReturnsTypedUnavailableEvidence()
    {
        var healthySignature = new BlobBuilder();
        new BlobEncoder(healthySignature).FieldSignature().Object();
        var degradedSignature = new BlobBuilder();
        SignatureTypeEncoder fieldType =
            new BlobEncoder(degradedSignature).FieldSignature();
        for (int depth = 0; depth <= SignatureBlobGuard.DefaultMaxDepth; depth++)
            fieldType = fieldType.SZArray();
        fieldType.Object();

        AssemblyContextApiComparisonResult result = await Compare(
            BuildFieldImage("SignatureComparison", degradedSignature.ToArray()),
            BuildFieldImage("SignatureComparison", healthySignature.ToArray()),
            "SignatureComparison");

        LibraryApiDiffOutcome.Unavailable unavailable =
            Assert.IsType<LibraryApiDiffOutcome.Unavailable>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            unavailable.Kind);
        LibraryApiDiffEndpointIssue.DegradedSignatures degraded =
            Assert.IsType<LibraryApiDiffEndpointIssue.DegradedSignatures>(
                Assert.Single(unavailable.Before.Issues));
        Assert.Equal(1, degraded.Count);
        Assert.True(unavailable.After.IsComplete);
    }

    [Fact]
    public async Task Create_FailedBeforeRow_RetainsTheCompleteAfterEndpoint()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        var failedEntry = new AssemblyContextEntry<AssemblyApiSurface>.Failed(
            result.Before.Subject,
            new IOException("Synthetic projection failure"));
        var failedProjection = new AssemblyContextApiSurfaceResult(
            new AssemblyContextResult<AssemblyApiSurface>([failedEntry]),
            []);
        AssemblyContextApiComparisonEndpoint failedEndpoint =
            CreateNonPublic<AssemblyContextApiComparisonEndpoint>(
                result.Before.Subject,
                failedProjection);
        result = CreateNonPublic<AssemblyContextApiComparisonResult>(
            result.Scope,
            failedEndpoint,
            result.After,
            null);

        LibraryApiDiffOutcome.Unavailable unavailable =
            Assert.IsType<LibraryApiDiffOutcome.Unavailable>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            unavailable.Kind);
        LibraryApiDiffEndpointIssue.Failed failed =
            Assert.IsType<LibraryApiDiffEndpointIssue.Failed>(
                Assert.Single(unavailable.Before.Issues));
        Assert.Equal(
            "Synthetic projection failure",
            failed.Detail.ToString());
        Assert.True(unavailable.After.IsComplete);
        Assert.Empty(unavailable.After.Issues);
    }

    [Fact]
    public async Task Create_DifferentLogicalLibraries_ReturnsTypedRejection()
    {
        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(
                        FixtureCatalog.DiffV1.AssemblyPath(),
                        FixtureCatalog.LibraryApiDiffV2.AssemblyPath())));

        Assert.Equal(
            LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            rejected.Kind);
        Assert.True(rejected.Before.IsComplete);
        Assert.True(rejected.After.IsComplete);
    }

    [Fact]
    public async Task Create_MissingExactTypeIdentity_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        FindingComparison<ApiTypeHandle>.Complete types =
            Assert.IsType<FindingComparison<ApiTypeHandle>.Complete>(
                comparison.Types.Value);
        PairFinding<ApiTypeHandle> changed = Assert.Single(
            types.Pairs,
            pair => pair is PairFinding<ApiTypeHandle>.Changed);
        var changedCase = Assert.IsType<PairFinding<ApiTypeHandle>.Changed>(
            changed.Value);
        changedCase.New.Payload.Type.DefinitionName = null;

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.MissingExactTypeIdentity,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_MultipleCompatibilityRowsForOneMember_CountsTheRelationOnce()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.DiffV1.AssemblyPath(),
            FixtureCatalog.DiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        TypeDiff typeDiff = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            current => current.TypeFullName == "DiffFixtureSample.MethodRemovalSample");
        var changes = Assert.IsType<List<ApiChange>>(typeDiff.Changes);
        ApiChange removed = changes.First(
            change => change.Kind == ChangeKind.MemberRemoved);
        changes.Add(removed with { Message = "A second compatibility row" });

        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(result));
        LibraryApiTypeDiff methodRemoval = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Display == "DiffFixtureSample.MethodRemovalSample").Comparison;

        Assert.Equal(3, methodRemoval.CompatibilityChanges.Length);
        Assert.Equal(2, methodRemoval.ChangedMemberCount);
    }

    [Fact]
    public async Task Create_DelimiterCollidingTypeDisplays_UseDistinctExactIdentifiers()
    {
        byte[] before = BuildDelimiterCollisionImage("Before");
        byte[] after = BuildDelimiterCollisionImage("After");

        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(before, after, "DelimiterCollision")));

        ComparisonSubject<LibraryApiTypeDiff>[] dotCollisions =
        [
            .. available.Document.Comparison.Subjects.Where(
                subject => subject.Display == "Collision.Outer.Inner"),
        ];
        Assert.Equal(3, dotCollisions.Length);
        Assert.Equal(
            3,
            dotCollisions
                .Select(subject => subject.Identifier)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            dotCollisions
                .Select(subject => subject.Identifier)
                .Order(StringComparer.Ordinal),
            dotCollisions.Select(subject => subject.Identifier));
        ComparisonSubject<LibraryApiTypeDiff> literalPlus = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Comparison.Before?.DefinitionName.Segments
                is [var segment]
                && segment == "Outer+Inner");
        ComparisonSubject<LibraryApiTypeDiff> nested = Assert.Single(
            available.Document.Comparison.Subjects,
            subject => subject.Comparison.Before?.DefinitionName.Segments.Length == 2);
        Assert.NotEqual(literalPlus.Identifier, nested.Identifier);
    }

    [Fact]
    public async Task Create_DuplicateMemberAnchors_GetDeterministicOccurrenceIdentifiers()
    {
        byte[] before = BuildDuplicateMemberImage(includeMethods: true);
        byte[] after = BuildDuplicateMemberImage(includeMethods: false);

        LibraryApiDiffOutcome.Available first =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(before, after, "DuplicateMembers")));
        LibraryApiDiffOutcome.Available second =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(before, after, "DuplicateMembers")));
        LibraryApiTypeDiff type = Assert.Single(first.Document.Comparison.Subjects).Comparison;

        Assert.Equal(2, type.Members.Length);
        Assert.Equal(type.Members[0].Relation.Before?.Anchor, type.Members[1].Relation.Before?.Anchor);
        Assert.NotEqual(
            type.Members[0].Relation.Identifier,
            type.Members[1].Relation.Identifier);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Create_ContradictoryStructuredSubject_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.DiffV1.AssemblyPath(),
            FixtureCatalog.DiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        TypeDiff typeDiff = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            current => current.TypeFullName == "DiffFixtureSample.MethodRemovalSample");
        var changes = Assert.IsType<List<ApiChange>>(typeDiff.Changes);
        int removedIndex = changes.FindIndex(change => change.Kind == ChangeKind.MemberRemoved);
        ApiChange removed = changes[removedIndex];
        ApiChangeSubject subject = Assert.IsType<ApiChangeSubject>(removed.Subject);
        FindingComparison<ApiTypeHandle>.Complete types =
            Assert.IsType<FindingComparison<ApiTypeHandle>.Complete>(
                comparison.Types.Value);
        ApiTypeHandle differentType = types.Pairs
            .Select(TypeAfter)
            .First(type => type is not null
                && type.TypeFullName != "DiffFixtureSample.MethodRemovalSample")!;
        changes[removedIndex] = removed with
        {
            Subject = subject with { OldType = differentType },
        };

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_UnassociatedMemberSubject_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.DiffV1.AssemblyPath(),
            FixtureCatalog.DiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        TypeDiff typeDiff = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            current => current.TypeFullName == "DiffFixtureSample.MethodRemovalSample");
        var changes = Assert.IsType<List<ApiChange>>(typeDiff.Changes);
        int removedIndex = changes.FindIndex(change => change.Kind == ChangeKind.MemberRemoved);
        ApiChange removed = changes[removedIndex];
        ApiChangeSubject subject = Assert.IsType<ApiChangeSubject>(removed.Subject);
        ApiMemberHandle oldMember = Assert.IsType<ApiMemberHandle>(subject.OldMember);
        var unrelatedAnchor = new ILInspector.MetadataPrimitives.MemberAnchor(
            "Unassociated~0000000000",
            "M:DiffFixtureSample.MethodRemovalSample.Unassociated()",
            "0000000000",
            oldMember.TypeFullName,
            oldMember.MemberName);
        changes[removedIndex] = removed with
        {
            Subject = subject with
            {
                OldMember = new ApiMemberHandle(
                    oldMember.Type,
                    oldMember.Member,
                    unrelatedAnchor),
            },
        };

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_TypeAdditionRowOnChangedType_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.DiffV1.AssemblyPath(),
            FixtureCatalog.DiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        TypeDiff typeDiff = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            current => current.TypeFullName == "DiffFixtureSample.BodyStateSample");
        var changes = Assert.IsType<List<ApiChange>>(typeDiff.Changes);
        int abstractRemovedIndex =
            changes.FindIndex(change => change.Kind == ChangeKind.AbstractRemoved);
        ApiChange abstractRemoved = changes[abstractRemovedIndex];
        ApiChangeSubject subject =
            Assert.IsType<ApiChangeSubject>(abstractRemoved.Subject);
        changes[abstractRemovedIndex] = abstractRemoved with
        {
            Kind = ChangeKind.TypeAdded,
            Subject = subject with { OldType = null },
        };

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_CrossedMemberTransitionEndpoints_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        ApiChange firstChange = FindChange(
            comparison.ApiDiff,
            "LibraryApiDiffFixture.HardChangedType",
            ChangeKind.VirtualRemoved);
        ApiChange secondChange = FindChange(
            comparison.ApiDiff,
            "LibraryApiDiffFixture.OtherHardChangedType",
            ChangeKind.VirtualRemoved);
        ApiChangeSubject firstSubject =
            Assert.IsType<ApiChangeSubject>(firstChange.Subject);
        ApiChangeSubject secondSubject =
            Assert.IsType<ApiChangeSubject>(secondChange.Subject);
        ReplaceChange(
            comparison.ApiDiff,
            "LibraryApiDiffFixture.HardChangedType",
            firstChange,
            firstChange with
            {
                Subject = firstSubject with
                {
                    NewType = secondSubject.NewType,
                    NewMember = secondSubject.NewMember,
                },
            });

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_OneSidedRowOnHardChangedMember_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        ApiChange change = FindChange(
            comparison.ApiDiff,
            "LibraryApiDiffFixture.HardChangedType",
            ChangeKind.VirtualRemoved);
        ApiChangeSubject subject = Assert.IsType<ApiChangeSubject>(change.Subject);
        ReplaceChange(
            comparison.ApiDiff,
            "LibraryApiDiffFixture.HardChangedType",
            change,
            change with
            {
                Kind = ChangeKind.MemberRemoved,
                Subject = subject with
                {
                    NewType = null,
                    NewMember = null,
                },
            });

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_DuplicateExactTypeIdentity_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        FindingComparison<ApiTypeHandle>.Complete types =
            Assert.IsType<FindingComparison<ApiTypeHandle>.Complete>(
                comparison.Types.Value);
        PairFinding<ApiTypeHandle>.Present first = types.Pairs
            .Select(pair => pair.Value)
            .OfType<PairFinding<ApiTypeHandle>.Present>()
            .First();
        PairFinding<ApiTypeHandle>.Present second = types.Pairs
            .Select(pair => pair.Value)
            .OfType<PairFinding<ApiTypeHandle>.Present>()
            .Skip(1)
            .First();
        second.Old.Payload.Type.DefinitionName =
            first.Old.Payload.Type.DefinitionName;
        second.New.Payload.Type.DefinitionName =
            first.New.Payload.Type.DefinitionName;

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.DuplicateExactTypeIdentity,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_MissingMemberAnchor_ReturnsTypedRejection()
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath());
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        FindingComparison<ApiMemberHandle>.Complete members =
            Assert.IsType<FindingComparison<ApiMemberHandle>.Complete>(
                comparison.Members.Value);
        PairFinding<ApiMemberHandle>.Changed changed = members.Pairs
            .Select(pair => pair.Value)
            .OfType<PairFinding<ApiMemberHandle>.Changed>()
            .First();
        typeof(ApiMemberHandle)
            .GetProperty(nameof(ApiMemberHandle.Anchor))!
            .SetValue(changed.New.Payload, null);

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(
                LibraryApiDiffPresentationAdapter.Create(result));

        Assert.Equal(
            LibraryApiDiffRejectionKind.MissingMemberAnchor,
            rejected.Kind);
    }

    [Fact]
    public async Task Create_RetainsTheRequestedApiSurfaceScope()
    {
        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(
                        FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                        FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
                        scope: ApiSurfaceScope.IncludeAll)));

        Assert.Equal(ApiSurfaceScope.IncludeAll, available.Document.Before.Scope);
        Assert.Equal(ApiSurfaceScope.IncludeAll, available.Document.After.Scope);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.Added", LibraryApiChangeState.Added)]
    [InlineData("FindingDiffAnyFixture.Removed", LibraryApiChangeState.Removed)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesHidden", LibraryApiChangeState.Hidden)]
    [InlineData("FindingDiffAnyFixture.TypeBecomesHidden", LibraryApiChangeState.Hidden)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesVisible", LibraryApiChangeState.Unhidden)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesObsolete", LibraryApiChangeState.Obsoleted)]
    [InlineData("FindingDiffAnyFixture.ObsoleteMessageChanged", LibraryApiChangeState.Obsoleted)]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", LibraryApiChangeState.Changed)]
    public async Task Create_DerivesEachRowStateFromKindAndStatus(
        string type,
        LibraryApiChangeState state)
    {
        LibraryApiCompatibilityChange change = Assert.Single(
            Subject(await ApiAxisFindingDiffAny(), type).Comparison.CompatibilityChanges);

        Assert.Equal(state, change.State);
        Assert.True(change.IsOrdinary);
    }

    [Theory]
    // A public Member made protected keeps its ordinary Before side.
    [InlineData("FindingDiffAnyFixture.PublicToProtected", true, false)]
    // A protected Member made public gains an ordinary After side.
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToPublic", false, true)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesHidden", true, false)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesVisible", false, true)]
    [InlineData("FindingDiffAnyFixture.ProtectedMemberChanged", false, false)]
    [InlineData("FindingDiffAnyFixture.HiddenMemberChanged", false, false)]
    // A public Member of a hidden Type is not ordinary; one of a public nested Type of a
    // hidden Type is, because hidden status does not pass to a nested Type.
    [InlineData("FindingDiffAnyFixture.HiddenTypeChanged", false, false)]
    [InlineData("FindingDiffAnyFixture.HiddenOuter+Inner", true, true)]
    // Obsolete does not remove a declaration from the ordinary population.
    [InlineData("FindingDiffAnyFixture.ObsoleteMemberChanged", true, true)]
    public async Task Create_CarriesEachSidesOrdinaryFact(
        string type,
        bool beforeOrdinary,
        bool afterOrdinary)
    {
        LibraryApiCompatibilityChange change = Assert.Single(
            Subject(await ApiAxisFindingDiffAny(), type).Comparison.CompatibilityChanges);

        Assert.Equal(beforeOrdinary, change.BeforeStatus!.IsOrdinary);
        Assert.Equal(afterOrdinary, change.AfterStatus!.IsOrdinary);
        Assert.Equal(beforeOrdinary || afterOrdinary, change.IsOrdinary);
    }

    [Fact]
    public async Task Create_CarriesTypeStatusOnEveryOccupiedSide()
    {
        LibraryApiDiffDocument document = await ApiAxisFindingDiffAny();

        LibraryApiTypeDiff obsolete =
            Subject(document, "FindingDiffAnyFixture.TypeBecomesObsolete").Comparison;
        Assert.False(obsolete.Before!.Status.IsObsolete);
        Assert.True(obsolete.After!.Status.IsObsolete);
        Assert.True(obsolete.After.Status.IsOrdinary);

        LibraryApiTypeDiff nested =
            Subject(document, "FindingDiffAnyFixture.NestedPublicToProtected+Nested").Comparison;
        Assert.True(nested.Before!.Status.IsOrdinary);
        Assert.False(nested.After!.Status.IsOrdinary);
        Assert.True(nested.IsOrdinary);

        LibraryApiTypeDiff inner = Subject(document, "FindingDiffAnyFixture.HiddenOuter+Inner").Comparison;
        Assert.False(inner.Before!.Status.IsHidden);
        Assert.True(inner.Before.Status.IsOrdinary);
        Assert.True(inner.IsOrdinary);
        LibraryApiMemberDiff member = Assert.Single(inner.Members);
        Assert.True(member.Relation.IsOrdinary);
        Assert.Equal(inner.Before, member.Relation.Before!.DeclaringType);
    }

    [Fact]
    public async Task OrdinaryPresentation_CountsRowsOutsideItOnlyForTheApiAxis()
    {
        LibraryApiDiffDocument apiAxis = await ApiAxisFindingDiffAny();
        Assert.True(apiAxis.PresentsOrdinaryPopulation);
        Assert.Equal(7, LibraryApiOrdinaryPresentation.OutsideRowCount(apiAxis));
        Assert.False(
            LibraryApiOrdinaryPresentation.HasOrdinaryRow(
                Subject(apiAxis, "FindingDiffAnyFixture.ProtectedMemberChanged").Comparison));
        Assert.True(
            LibraryApiOrdinaryPresentation.HasOrdinaryRow(
                Subject(apiAxis, "FindingDiffAnyFixture.PublicToProtected").Comparison));

        foreach (ApiSurfaceScope scope in new[] { ApiSurfaceScope.Public, ApiSurfaceScope.IncludeAll })
        {
            LibraryApiDiffDocument document = Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(
                    await Compare(
                        FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath(),
                        FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath(),
                        scope: scope))).Document;
            Assert.False(document.PresentsOrdinaryPopulation);
            Assert.Equal(0, LibraryApiOrdinaryPresentation.OutsideRowCount(document));
        }
    }

    // A Type selection projects a nested Type without its enclosing Type, and the Public scope
    // never projects a hidden one. Its ordinary fact comes from metadata, so it matches the
    // complete comparison: ordinary under a public Type, and under a hidden one, whose hidden
    // status does not pass to the Types it nests.
    [Theory]
    [InlineData(ApiSurfaceScope.ApiAxis, "FindingDiffAnyFixture.NestedPublicToProtected+Nested", true, false)]
    [InlineData(ApiSurfaceScope.ApiAxis, "FindingDiffAnyFixture.NestedProtectedToPublic+Nested", false, true)]
    [InlineData(ApiSurfaceScope.ApiAxis, "FindingDiffAnyFixture.HiddenOuter+Inner", true, true)]
    [InlineData(ApiSurfaceScope.IncludeAll, "FindingDiffAnyFixture.NestedPublicToProtected+Nested", true, false)]
    [InlineData(ApiSurfaceScope.IncludeAll, "FindingDiffAnyFixture.HiddenOuter+Inner", true, true)]
    [InlineData(ApiSurfaceScope.Public, "FindingDiffAnyFixture.HiddenOuter+Inner", true, true)]
    public async Task Create_TypeSelectionOfANestedType_KeepsItsOrdinaryFact(
        ApiSurfaceScope scope,
        string identifier,
        bool beforeOrdinary,
        bool afterOrdinary)
    {
        string leaf = identifier[(identifier.LastIndexOf('+') + 1)..];
        LibraryApiDiffDocument selected = Assert.IsType<LibraryApiDiffOutcome.Available>(
            LibraryApiDiffPresentationAdapter.Create(
                await Compare(
                    FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath(),
                    FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath(),
                    scope: scope,
                    typeSelection: new ApiTypeSelection(
                        name => TypeMatcher.MatchesTypeFilter(name, leaf))))).Document;

        LibraryApiTypeDiff nested = Subject(selected, identifier).Comparison;
        Assert.Equal(beforeOrdinary, nested.Before!.Status.IsOrdinary);
        Assert.Equal(afterOrdinary, nested.After!.Status.IsOrdinary);
        LibraryApiDiffDocument complete = Assert.IsType<LibraryApiDiffOutcome.Available>(
            LibraryApiDiffPresentationAdapter.Create(
                await Compare(
                    FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath(),
                    FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath(),
                    scope: scope))).Document;
        Assert.Equal(Subject(complete, identifier).Comparison, nested);
    }

    static async Task<LibraryApiDiffDocument> ApiAxisFindingDiffAny()
        => Assert.IsType<LibraryApiDiffOutcome.Available>(
            LibraryApiDiffPresentationAdapter.Create(
                await Compare(
                    FixtureCatalog.MetadataFindingDiffAnyPair.OldAssemblyPath(),
                    FixtureCatalog.MetadataFindingDiffAnyPair.NewAssemblyPath(),
                    scope: ApiSurfaceScope.ApiAxis))).Document;

    static ComparisonSubject<LibraryApiTypeDiff> Subject(
        LibraryApiDiffDocument document,
        string identifier)
        => Assert.Single(
            document.Comparison.Subjects,
            subject => subject.Identifier == identifier);

    static async Task<AssemblyContextApiComparisonResult> Compare(
        string beforePath,
        string afterPath,
        ApiSurfaceProjectionLimits? limits = null,
        ApiSurfaceScope scope = ApiSurfaceScope.Public,
        ApiTypeSelection? typeSelection = null)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            SinglePathGroup(workspace, beforePath, "Before", policy);
        using AssemblyContextGroup afterGroup =
            SinglePathGroup(workspace, afterPath, "After", policy);
        return AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            scope,
            limits ?? GenerousLimits,
            typeSelection);
    }

    static async Task<AssemblyContextApiComparisonResult> Compare(
        byte[] beforeBytes,
        byte[] afterBytes,
        string assemblyName)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant beforeParticipant = Participant(
            beforeBytes,
            assemblyName + " Before",
            policy);
        AssemblyContextParticipant afterParticipant = Participant(
            afterBytes,
            assemblyName + " After",
            policy);
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);
        return AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.Public,
            GenerousLimits);
    }

    static AssemblyContextGroup SinglePathGroup(
        InspectionWorkspace workspace,
        string path,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(provenanceLabel)),
                    policy),
            ]);

    static AssemblyContextParticipant Participant(
        byte[] bytes,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
    {
        using var reader = new PEReader(new MemoryStream(bytes, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
        return new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local(provenanceLabel)),
            policy);
    }

    static ApiTypeHandle? TypeAfter(PairFinding<ApiTypeHandle> pair)
        => pair switch
        {
            PairFinding<ApiTypeHandle>.Added added => added.New.Payload,
            PairFinding<ApiTypeHandle>.Present present => present.New.Payload,
            PairFinding<ApiTypeHandle>.Changed changed => changed.New.Payload,
            PairFinding<ApiTypeHandle>.Removed => null,
        };

    static T CreateNonPublic<T>(params object?[] arguments)
        => (T)Activator.CreateInstance(
            typeof(T),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: arguments,
            culture: null)!;

    static ApiChange FindChange(
        ApiDiff diff,
        string typeFullName,
        ChangeKind kind)
        => Assert.Single(
            Assert.Single(
                diff.TypeDiffs,
                type => type.TypeFullName == typeFullName).Changes,
            change => change.Kind == kind);

    static void ReplaceChange(
        ApiDiff diff,
        string typeFullName,
        ApiChange oldChange,
        ApiChange newChange)
    {
        var changes = Assert.IsType<List<ApiChange>>(
            Assert.Single(
                diff.TypeDiffs,
                type => type.TypeFullName == typeFullName).Changes);
        changes[changes.IndexOf(oldChange)] = newChange;
    }

    static byte[] BuildDelimiterCollisionImage(string methodName)
    {
        var metadata = CreateAssemblyMetadata("DelimiterCollision");
        BlobHandle methodSignature =
            metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 });
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Collision"),
            metadata.GetOrAddString("Outer+Inner"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Collision"),
            metadata.GetOrAddString("Outer"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        TypeDefinitionHandle inner = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic | TypeAttributes.Abstract,
            default,
            metadata.GetOrAddString("Inner"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(2));
        metadata.AddNestedType(inner, outer);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Collision"),
            metadata.GetOrAddString("Outer.Inner"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Collision.Outer"),
            metadata.GetOrAddString("Inner"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(4));
        AddAbstractMethod(metadata, methodName + "LiteralPlus", methodSignature);
        AddAbstractMethod(metadata, methodName + "Nested", methodSignature);
        AddAbstractMethod(metadata, methodName + "LiteralDot", methodSignature);
        AddAbstractMethod(metadata, methodName + "Namespaced", methodSignature);
        return Serialize(metadata);
    }

    static byte[] BuildDuplicateMemberImage(bool includeMethods)
    {
        var metadata = CreateAssemblyMetadata("DuplicateMembers");
        BlobHandle methodSignature =
            metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x01 });
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Duplicates"),
            metadata.GetOrAddString("Container"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        if (includeMethods)
        {
            AddAbstractMethod(metadata, "Repeated", methodSignature);
            AddAbstractMethod(metadata, "Repeated", methodSignature);
        }
        return Serialize(metadata);
    }

    static byte[] BuildFieldImage(string assemblyName, byte[] fieldSignature)
    {
        var metadata = CreateAssemblyMetadata(assemblyName);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Signatures"),
            metadata.GetOrAddString("Container"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        return Serialize(metadata);
    }

    static MetadataBuilder CreateAssemblyMetadata(string assemblyName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(assemblyName + ".dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        return metadata;
    }

    static void AddAbstractMethod(
        MetadataBuilder metadata,
        string name,
        BlobHandle signature)
        => metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.Abstract
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            signature,
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
