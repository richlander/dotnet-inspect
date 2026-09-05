using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationSubjectInventoryTests
{
    [Fact]
    public void EveryBoundedInventoryRow_PreservesProducerOrderAndIdentity()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember first = Library(coordinate, "First");
        WorkspaceContextMember second = Library(coordinate, "Second");
        ApiType firstType = Type(
            "FirstType",
            "public",
            Member("First"),
            Member("Second"));
        ApiType secondType = Type("SecondType", "private", Member("Third"));
        ApiType thirdType = Type("ThirdType", "protected");

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [first, second],
            first,
            Surface(
                Available(first, firstType, secondType),
                Available(second, thirdType)));

        Assert.Equal(
            [
                StructuralSubjectIdentity.ForLibrary(first),
                StructuralSubjectIdentity.ForLibrary(second),
            ],
            inventory.Libraries.Select(library => library.Subject));
        Assert.True(inventory.Libraries[0].IsPrimary);
        Assert.False(inventory.Libraries[1].IsPrimary);
        NavigationTypeInventoryOutcome.Available aggregate =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                inventory.Types);
        Assert.Equal(
            [firstType, secondType, thirdType],
            aggregate.Rows.Select(row => row.ProducerRow));
        Assert.Equal(
            ["Sample.FirstType", "Sample.SecondType", "Sample.ThirdType"],
            aggregate.Rows.Select(
                row => row.Subject.Identity.Type.ToEscapedFullName()));
        Assert.Equal(
            firstType.Members,
            aggregate.Rows[0].Members.Select(member => member.ProducerRow));
        Assert.Equal(
            firstType.Members.Select(member =>
                ApiMemberIdentity.GetMemberAnchor(firstType, member)),
            aggregate.Rows[0].Members.Select(member => member.Subject.Identity.Member));
        Assert.All(
            aggregate.Rows[0].Members,
            member => Assert.Same(
                aggregate.Rows[0].Subject,
                member.Subject.DeclaringType));
    }

    [Fact]
    public void ProjectedMemberFromRealProducer_BindsDeclarationTypeAndAnchor()
    {
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.CreateFromPath(
                typeof(NavigationSubjectInventoryTests).Assembly.Location,
                AssemblyResolutionProvenance.Local("inventory gate")),
            NoResolverAssemblyBindingPolicy.Instance);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        var library = new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            participant);
        AssemblyContextApiSurfaceResult surface =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [library],
            library,
            surface);

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                inventory.Types);
        NavigationTypeInventoryRow receiver = Assert.Single(
            types.Rows,
            row => row.ProducerRow.FullName == typeof(InventoryFixture).FullName);
        NavigationMemberInventoryRow projected = Assert.Single(
            receiver.Members,
            row => row.ProducerRow.Name == nameof(InventoryExtensions.Extend));
        NavigationTypeInventoryRow declaring = Assert.Single(
            types.Rows,
            row => row.ProducerRow.FullName
                == typeof(InventoryExtensions).FullName);
        NavigationMemberInventoryRow declaration = Assert.Single(
            declaring.Members,
            row => row.ProducerRow.Name
                    == nameof(InventoryExtensions.Extend)
                && row.ProducerRow.DeclaringTypeCanonicalName is null
                && row.Subject.DeclaringType == declaring.Subject);
        Assert.Equal(
            declaring.ProducerRow.DefinitionName,
            projected.ProducerRow.DeclaringTypeDefinitionName);
        Assert.Same(declaring.Subject, projected.Subject.DeclaringType);
        Assert.NotEqual(receiver.Subject, projected.Subject.DeclaringType);
        Assert.Equal(
            declaration.Subject.Identity.Member,
            projected.Subject.Identity.Member);
        Assert.DoesNotContain(
            types.Evidence,
            evidence => evidence
                    is NavigationInventoryEvidence
                        .ProjectedMemberIdentityFailure failure
                && ReferenceEquals(
                    failure.ProducerRow,
                    projected.ProducerRow));
    }

    [Fact]
    public void ProjectedMemberWithoutTypedDeclaringIdentity_FailsClosed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        ApiMember projected = ProjectedMember(
            "Extend",
            declaringType: null);
        ApiType receiver = Type("Receiver", "public", projected);

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver))).Types);

        Assert.Empty(Assert.Single(types.Rows).Members);
        NavigationInventoryEvidence.ProjectedMemberIdentityFailure failure =
            Assert.IsType<
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure>(
                    Assert.Single(types.Evidence));
        Assert.Equal(
            NavigationProjectedMemberIdentityFailureKind
                .DeclaringTypeIdentityMissing,
            failure.Kind);
        Assert.Same(receiver, failure.ContainingType);
        Assert.Same(projected, failure.ProducerRow);
    }

    [Fact]
    public void ProjectedMemberWithUnreturnedDeclaringType_FailsClosed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        ApiMember projected = ProjectedMember(
            "Extend",
            TypeName("Other", "Extensions"));
        ApiType receiver = Type("Receiver", "public", projected);

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver))).Types);

        Assert.Empty(Assert.Single(types.Rows).Members);
        NavigationInventoryEvidence.ProjectedMemberIdentityFailure failure =
            Assert.IsType<
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure>(
                    Assert.Single(types.Evidence));
        Assert.Equal(
            NavigationProjectedMemberIdentityFailureKind
                .DeclaringTypeNotReturned,
            failure.Kind);
        Assert.Same(projected, failure.ProducerRow);
    }

    [Fact]
    public void ProjectedMemberWithAmbiguousDeclaringType_FailsClosed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        MetadataTypeDefinitionName declaringType =
            TypeName("Sample", "Extensions");
        ApiMember projected = ProjectedMember("Extend", declaringType);
        ApiType receiver = Type("Receiver", "public", projected);
        ApiType first = Type("FirstExtensions", "public");
        ApiType second = Type("SecondExtensions", "public");
        first.DefinitionName = declaringType;
        second.DefinitionName = declaringType;

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver, first, second))).Types);

        Assert.Empty(
            Assert.Single(
                types.Rows,
                row => ReferenceEquals(
                    row.ProducerRow,
                    receiver)).Members);
        NavigationInventoryEvidence.ProjectedMemberIdentityFailure failure =
            Assert.IsType<
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure>(
                    Assert.Single(types.Evidence));
        Assert.Equal(
            NavigationProjectedMemberIdentityFailureKind
                .DeclaringTypeAmbiguous,
            failure.Kind);
        Assert.Same(projected, failure.ProducerRow);
    }

    [Fact]
    public void ProjectedMemberWithoutDeclarationMemberIdentity_FailsClosed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        MetadataTypeDefinitionName declaringType =
            TypeName("Sample", "Extensions");
        ApiMember projected = ProjectedMember(
            "Extend",
            declaringType,
            metadataToken: null);
        ApiType receiver = Type("Receiver", "public", projected);
        ApiType declaring = Type("Extensions", "public", Member("Extend"));

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver, declaring))).Types);

        Assert.Empty(
            Assert.Single(
                types.Rows,
                row => ReferenceEquals(
                    row.ProducerRow,
                    receiver)).Members);
        NavigationInventoryEvidence.ProjectedMemberIdentityFailure failure =
            Assert.IsType<
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure>(
                    Assert.Single(types.Evidence));
        Assert.Equal(
            NavigationProjectedMemberIdentityFailureKind
                .DeclarationMemberIdentityMissing,
            failure.Kind);
        Assert.Same(projected, failure.ProducerRow);
    }

    [Fact]
    public void ProjectedMemberWithUnreturnedDeclarationMember_FailsClosed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        MetadataTypeDefinitionName declaringType =
            TypeName("Sample", "Extensions");
        ApiMember projected = ProjectedMember("Extend", declaringType);
        ApiType receiver = Type("Receiver", "public", projected);
        ApiType declaring = Type("Extensions", "public");

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver, declaring))).Types);

        Assert.Empty(
            Assert.Single(
                types.Rows,
                row => ReferenceEquals(
                    row.ProducerRow,
                    receiver)).Members);
        NavigationInventoryEvidence.ProjectedMemberIdentityFailure failure =
            Assert.IsType<
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure>(
                    Assert.Single(types.Evidence));
        Assert.Equal(
            NavigationProjectedMemberIdentityFailureKind
                .DeclarationMemberNotReturned,
            failure.Kind);
        Assert.Same(projected, failure.ProducerRow);
    }

    [Fact]
    public void ProjectedMemberWithAmbiguousDeclarationMember_FailsClosed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        MetadataTypeDefinitionName declaringType =
            TypeName("Sample", "Extensions");
        ApiMember projected = ProjectedMember("Extend", declaringType);
        ApiType receiver = Type("Receiver", "public", projected);
        ApiMember first = Member("Extend");
        first.MetadataToken = projected.MetadataToken;
        ApiMember second = Member("Extend");
        second.MetadataToken = projected.MetadataToken;
        ApiType declaring =
            Type("Extensions", "public", first, second);

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver, declaring))).Types);

        Assert.Empty(
            Assert.Single(
                types.Rows,
                row => ReferenceEquals(
                    row.ProducerRow,
                    receiver)).Members);
        NavigationInventoryEvidence.ProjectedMemberIdentityFailure failure =
            Assert.IsType<
                NavigationInventoryEvidence.ProjectedMemberIdentityFailure>(
                    Assert.Single(types.Evidence));
        Assert.Equal(
            NavigationProjectedMemberIdentityFailureKind
                .DeclarationMemberAmbiguous,
            failure.Kind);
        Assert.Same(projected, failure.ProducerRow);
    }

    [Fact]
    public void ProjectedMemberLookup_DoesNotUseDeclaringText()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        MetadataTypeDefinitionName declaringType =
            TypeName("Sample", "Extensions");
        ApiMember projected = ProjectedMember("Extend", declaringType);
        projected.DeclaringType = "Misleading.Display";
        projected.DeclaringTypeCanonicalName = "Misleading.Canonical";
        ApiType receiver = Type("Receiver", "public", projected);
        ApiMember declaration = Member("Extend");
        declaration.MetadataToken = projected.MetadataToken;
        ApiType declaring =
            Type("Extensions", "public", declaration);

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                Classify(
                    coordinate,
                    [library],
                    library,
                    Surface(Available(library, receiver, declaring))).Types);

        NavigationTypeInventoryRow declaringRow = Assert.Single(
            types.Rows,
            row => ReferenceEquals(row.ProducerRow, declaring));
        NavigationMemberInventoryRow projectedRow = Assert.Single(
            Assert.Single(
                types.Rows,
                row => ReferenceEquals(
                    row.ProducerRow,
                    receiver)).Members);
        Assert.Same(
            declaringRow.Subject,
            projectedRow.Subject.DeclaringType);
        Assert.Equal(
            ApiMemberIdentity.GetMemberAnchor(declaring, declaration),
            projectedRow.Subject.Identity.Member);
    }

    [Fact]
    public void SuccessfulProducerRows_AreTrustworthyDespitePeerFailure()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember healthy = Library(coordinate, "Healthy");
        WorkspaceContextMember failed = Library(coordinate, "Failed");
        ApiType returned = Type("Widget", "public");
        var error = new InvalidOperationException("inspection failed");

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [healthy, failed],
            healthy,
            Surface(
                Available(healthy, returned),
                Failed(failed, error)));

        NavigationTypeInventoryOutcome.Available aggregate =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                inventory.Types);
        Assert.Same(returned, Assert.Single(aggregate.Rows).ProducerRow);
        NavigationInventoryEvidence.ParticipantFailed retained =
            Assert.IsType<NavigationInventoryEvidence.ParticipantFailed>(
                Assert.Single(aggregate.Evidence));
        Assert.Same(error, retained.Error);
        Assert.Single(inventory.InitialCandidates[0].Types);
        Assert.Empty(inventory.InitialCandidates[1].Types);
        Assert.Equal(
            inventory.InitialCandidates[0].Types[0].Subject,
            NavigationInitialSubjectRecommendation.Recommend(
                inventory.Root,
                allLibraries: null,
                inventory.InitialCandidates).Subject);
    }

    [Fact]
    public void CompleteSuccessfulEmptyInventory_IsUnavailable()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember first = Library(coordinate, "First");
        WorkspaceContextMember second = Library(coordinate, "Second");

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [first, second],
            primary: null,
            Surface(Available(first), Available(second)));

        Assert.IsType<NavigationTypeInventoryOutcome.Unavailable>(
            inventory.Types);
        Assert.All(
            inventory.Libraries,
            library => Assert.IsType<NavigationTypeInventoryOutcome.Unavailable>(
                library.Types));
        Assert.All(
            inventory.InitialCandidates,
            candidate => Assert.Empty(candidate.Types));
    }

    [Fact]
    public void NoCandidateWithIndeterminateProducer_IsFailed()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember rejected = Library(coordinate, "Rejected");
        WorkspaceContextMember incomplete = Library(coordinate, "Incomplete");
        var openFailure = new CandidateOpenFailure(
            CandidateOpenFailureKind.InvalidImage,
            "invalid image");
        var inspectionFailure = InspectionFailure("read type");

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [rejected, incomplete],
            primary: null,
            Surface(
                Rejected(rejected, openFailure),
                AvailableWithFailures(incomplete, [inspectionFailure])));

        NavigationTypeInventoryOutcome.Failed aggregate =
            Assert.IsType<NavigationTypeInventoryOutcome.Failed>(
                inventory.Types);
        Assert.Empty(aggregate.Rows);
        Assert.Collection(
            aggregate.Evidence,
            evidence => Assert.Same(
                openFailure,
                Assert.IsType<
                    NavigationInventoryEvidence.ParticipantRejected>(
                        evidence).Failure),
            evidence => Assert.Same(
                inspectionFailure,
                Assert.IsType<
                    NavigationInventoryEvidence.InspectionFailed>(
                        evidence).Failure));
        Assert.All(
            inventory.Libraries,
            library => Assert.IsType<NavigationTypeInventoryOutcome.Failed>(
                library.Types));
    }

    [Fact]
    public void ProjectionTruncation_NeverProvesUnavailability()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember projected = Library(coordinate, "Projected");
        WorkspaceContextMember omitted = Library(coordinate, "Omitted");
        ApiSurfaceProjectionTruncation truncation = Truncation(
            projectedParticipants: 1,
            omittedParticipants: 1);

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [projected, omitted],
            primary: null,
            Surface(truncation, Available(projected)));

        Assert.IsType<NavigationTypeInventoryOutcome.Unavailable>(
            inventory.Libraries[0].Types);
        NavigationTypeInventoryOutcome.Failed omittedTypes =
            Assert.IsType<NavigationTypeInventoryOutcome.Failed>(
                inventory.Libraries[1].Types);
        NavigationInventoryEvidence.ProjectionOmitted evidence =
            Assert.IsType<NavigationInventoryEvidence.ProjectionOmitted>(
                Assert.Single(omittedTypes.Evidence));
        Assert.Same(truncation, evidence.Truncation);
        Assert.Equal(
            StructuralSubjectIdentity.ForLibrary(omitted),
            evidence.Library);
        Assert.IsType<NavigationTypeInventoryOutcome.Failed>(inventory.Types);

        ApiType returned = Type("Widget", "public");
        NavigationSubjectInventory partial = Classify(
            coordinate,
            [projected, omitted],
            primary: null,
            Surface(truncation, Available(projected, returned)));
        NavigationTypeInventoryOutcome.Available partialTypes =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                partial.Types);
        Assert.Same(returned, Assert.Single(partialTypes.Rows).ProducerRow);
        Assert.IsType<NavigationInventoryEvidence.ProjectionOmitted>(
            Assert.Single(partialTypes.Evidence));
    }

    [Fact]
    public void ProducerEvidence_IsRetainedWithoutTranslation()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        ApiType returned = Type("Widget", "public");
        ApiSurfaceInspectionFailure failure =
            InspectionFailure("decode signature");

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [library],
            library,
            Surface(AvailableWithFailures(library, [failure], returned)));

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                inventory.Types);
        NavigationInventoryEvidence.InspectionFailed retained =
            Assert.IsType<NavigationInventoryEvidence.InspectionFailed>(
                Assert.Single(types.Evidence));
        Assert.Same(failure, retained.Failure);
        Assert.Same(returned, Assert.Single(types.Rows).ProducerRow);
    }

    [Fact]
    public void InitialCandidates_ContainOnlyTrustworthyExactRows()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        ApiType exact = Type("Exact", "public");
        ApiType missingIdentity = Type("Legacy", "private");
        missingIdentity.DefinitionName = null;

        NavigationSubjectInventory inventory = Classify(
            coordinate,
            [library],
            library,
            Surface(Available(library, exact, missingIdentity)));

        NavigationTypeInventoryOutcome.Available types =
            Assert.IsType<NavigationTypeInventoryOutcome.Available>(
                inventory.Types);
        Assert.Same(exact, Assert.Single(types.Rows).ProducerRow);
        NavigationInventoryEvidence.TypeIdentityMissing missing =
            Assert.IsType<NavigationInventoryEvidence.TypeIdentityMissing>(
                Assert.Single(types.Evidence));
        Assert.Same(missingIdentity, missing.ProducerRow);
        NavigationInitialTypeCandidate candidate =
            Assert.Single(Assert.Single(inventory.InitialCandidates).Types);
        Assert.Equal(types.Rows[0].Subject, candidate.Subject);
        Assert.Equal("public", candidate.Accessibility.Id);

        NavigationSubjectInventory onlyMissing = Classify(
            coordinate,
            [library],
            library,
            Surface(Available(library, missingIdentity)));
        Assert.IsType<NavigationTypeInventoryOutcome.Failed>(
            onlyMissing.Types);
    }

    [Fact]
    public void InventoryJoin_RequiresExactParticipantRegistration()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember expected = Library(coordinate, "Expected");
        WorkspaceContextMember foreign = Library(coordinate, "Foreign");

        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected],
                primary: null,
                Surface(Available(foreign, Type("Widget", "public")))));
        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected],
                primary: null,
                Surface()));
        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected],
                foreign,
                Surface(Available(expected))));

        WorkspaceContextMember second = Library(coordinate, "Second");
        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected, second],
                primary: null,
                Surface(Available(second), Available(expected))));
        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected, expected],
                primary: null,
                Surface(Available(expected), Available(expected))));
        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected, second],
                primary: null,
                Surface(
                    Truncation(
                        projectedParticipants: 1,
                        omittedParticipants: 2),
                    Available(expected))));

        var foreignCoordinatePrimary = new WorkspaceContextMember(
            expected.Declared,
            new RealizedMemberCoordinate.Package(
                coordinate.PackageId,
                "2.0.0",
                coordinate.Producer,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            expected.Participant);
        Assert.Throws<ArgumentException>(
            () => Classify(
                coordinate,
                [expected],
                foreignCoordinatePrimary,
                Surface(Available(expected))));
    }

    [Fact]
    public void InventoryModels_UseSequenceValueEquality()
    {
        RealizedMemberCoordinate.Package coordinate = Coordinate();
        WorkspaceContextMember library = Library(coordinate, "Library");
        ApiType type = Type("Widget", "public", Member("Run"));
        AssemblyContextApiSurfaceResult surface =
            Surface(Available(library, type));
        var equalLibrary = new WorkspaceContextMember(
            library.Declared,
            Coordinate(),
            library.Participant);

        NavigationSubjectInventory first = Classify(
            coordinate,
            [library],
            library,
            surface);
        NavigationSubjectInventory second = Classify(
            Coordinate(),
            [equalLibrary],
            equalLibrary,
            surface);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    static NavigationSubjectInventory Classify(
        RealizedMemberCoordinate.Package coordinate,
        ImmutableArray<WorkspaceContextMember> libraries,
        WorkspaceContextMember? primary,
        AssemblyContextApiSurfaceResult surface) =>
        NavigationSubjectInventoryClassification.Classify(
            StructuralSubjectIdentity.ForRoot(coordinate),
            libraries,
            primary,
            surface);

    static AssemblyContextApiSurfaceResult Surface(
        params AssemblyContextEntry<AssemblyApiSurface>[] entries) =>
        Surface(truncation: null, entries);

    static AssemblyContextApiSurfaceResult Surface(
        ApiSurfaceProjectionTruncation? truncation,
        params AssemblyContextEntry<AssemblyApiSurface>[] entries) =>
        new(
            new AssemblyContextResult<AssemblyApiSurface>([.. entries]),
            [],
            truncation);

    static AssemblyContextEntry<AssemblyApiSurface> Available(
        WorkspaceContextMember library,
        params ApiType[] types) =>
        AvailableWithFailures(library, [], types);

    static AssemblyContextEntry<AssemblyApiSurface> AvailableWithFailures(
        WorkspaceContextMember library,
        ImmutableArray<ApiSurfaceInspectionFailure> failures,
        params ApiType[] types)
    {
        var surface = new ApiSurface
        {
            Types = [.. types],
            InspectionFailures = [.. failures],
        };
        return new AssemblyContextEntry<AssemblyApiSurface>.Available(
            new AssemblyContextSubject(library.Participant.Assembly),
            new AssemblyApiSurface(surface, failures));
    }

    static AssemblyContextEntry<AssemblyApiSurface> Rejected(
        WorkspaceContextMember library,
        CandidateOpenFailure failure) =>
        new AssemblyContextEntry<AssemblyApiSurface>.Rejected(
            new AssemblyContextSubject(library.Participant.Assembly),
            failure);

    static AssemblyContextEntry<AssemblyApiSurface> Failed(
        WorkspaceContextMember library,
        Exception error) =>
        new AssemblyContextEntry<AssemblyApiSurface>.Failed(
            new AssemblyContextSubject(library.Participant.Assembly),
            error);

    static ApiType Type(
        string name,
        string accessibility,
        params ApiMember[] members) =>
        new()
        {
            Namespace = "Sample",
            Name = name,
            DefinitionName = TypeName("Sample", name),
            Accessibility = accessibility,
            Members = [.. members],
        };

    static ApiMember Member(string name) =>
        new()
        {
            Name = name,
            Kind = "method",
            Signature = $"void {name}()",
        };

    static ApiMember ProjectedMember(
        string name,
        MetadataTypeDefinitionName? declaringType,
        int? metadataToken = 0x06000001)
    {
        ApiMember member = Member(name);
        member.Kind = "extension-method";
        member.MetadataToken = metadataToken;
        member.DeclaringType = "Display text is not identity";
        member.DeclaringTypeCanonicalName = "Sample.Extensions";
        member.DeclaringTypeDefinitionName = declaringType;
        return member;
    }

    static ApiSurfaceInspectionFailure InspectionFailure(string operation) =>
        new(
            operation,
            SubjectToken: 1,
            MetadataTypeNameFailureMechanism.Metadata,
            Kind: "TypeDef",
            Detail: "failed");

    static ApiSurfaceProjectionTruncation Truncation(
        int projectedParticipants,
        int omittedParticipants) =>
        new(
            ApiSurfaceProjectionLimit.Types,
            Bound: 1,
            projectedParticipants,
            omittedParticipants,
            ProjectedTypes: 0,
            ProjectedMembers: 0,
            ProjectedInspectionFailures: 0,
            ProjectedTypeForwarders: 0,
            InspectedMetadataRows: 0,
            ProjectedRetainedTextCharacters: 0);

    static RealizedMemberCoordinate.Package Coordinate() =>
        new(
            "sample.package",
            "1.0.0",
            "nuget-org",
            "net11.0",
            runtimeIdentifier: null);

    static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate,
        string name)
    {
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                name,
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null),
            path: null,
            () => new MemoryStream([0], writable: false),
            AssemblyResolutionProvenance.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;
}
