using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.MemorySafetyFixtures;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataAccessorDeclarationEvidenceTests
{
    [Fact]
    public void Mdp004_PropertyAggregatePreservesEveryPhysicalOccurrence()
    {
        using var fixture = Fixture.Create(
            methodCount: 4,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
                PropertyRow(2, 0x0004),
                PropertyRow(3, 0x0004),
                PropertyRow(4, 0x0001),
            ]);
        var work = new List<MetadataOperationWorkKind>();
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property);

        var first = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    request,
                    TestContext.Current.CancellationToken));
        var second = declaration.PostAccessorDeclaration(
            request,
            TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(request.Type, first.Evidence.Type);
        Assert.Equal(request.Declaration, first.Evidence.Declaration);
        Assert.Equal(
            [
                (1, 0x0002, MetadataAccessorSemanticsRole.Getter, 1),
                (2, 0x0004, MetadataAccessorSemanticsRole.Other, 2),
                (3, 0x0004, MetadataAccessorSemanticsRole.Other, 3),
                (4, 0x0001, MetadataAccessorSemanticsRole.Setter, 4),
            ],
            first.Evidence.Accessors
                .Select(accessor => (
                    accessor.PhysicalRowNumber,
                    (int)accessor.RawSemantics,
                    accessor.Role,
                    MetadataTokens.GetRowNumber(
                        accessor.Method.Method.Handle)))
                .ToArray());
        Assert.Equal(
            4,
            first.Evidence.Accessors
                .Select(accessor => accessor.Method)
                .Distinct()
                .Count());
        Assert.Contains(
            MetadataOperationWorkKind.MethodSemanticsAssociationRead,
            work);
        Assert.Contains(
            MetadataOperationWorkKind.AccessorDeclarationPublication,
            work);
        Assert.Equal(
            1,
            work.Count(kind =>
                kind
                == MetadataOperationWorkKind
                    .AccessorDeclarationPublication));
    }

    [Fact]
    public void Mdp004_EventAggregatePreservesRaiseAndOther()
    {
        using var fixture = Fixture.Create(
            methodCount: 5,
            eventCount: 1,
            rows:
            [
                EventRow(1, 0x0008),
                EventRow(2, 0x0010),
                EventRow(3, 0x0020),
                EventRow(4, 0x0004),
                EventRow(5, 0x0004),
            ]);

        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Event));

        Assert.Equal(
            [
                MetadataAccessorSemanticsRole.AddOn,
                MetadataAccessorSemanticsRole.RemoveOn,
                MetadataAccessorSemanticsRole.Fire,
                MetadataAccessorSemanticsRole.Other,
                MetadataAccessorSemanticsRole.Other,
            ],
            posted.Evidence.Accessors
                .Select(accessor => accessor.Role)
                .ToArray());
        Assert.Equal(
            [1, 2, 3, 4, 5],
            posted.Evidence.Accessors
                .Select(accessor => accessor.PhysicalRowNumber)
                .ToArray());
    }

    [Fact]
    public void Mdp004_CompilerProducedPropertyAndEventPostExactAccessors()
    {
        Type fixtureType = typeof(MemorySafetyDeclarationFixtures);
        string path = fixtureType.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            (TypeDefinitionHandle)MetadataTokens.EntityHandle(
                fixtureType.MetadataToken);
        PropertyDefinitionHandle property =
            (PropertyDefinitionHandle)MetadataTokens.EntityHandle(
                fixtureType.GetProperty(
                    nameof(MemorySafetyDeclarationFixtures.Property))!
                    .MetadataToken);
        EventDefinitionHandle @event =
            (EventDefinitionHandle)MetadataTokens.EntityHandle(
                fixtureType.GetEvent(
                    nameof(MemorySafetyDeclarationFixtures.CustomEvent))!
                    .MetadataToken);
        MetadataTypeDefinitionAddress typeAddress =
            MetadataTypeDefinitionAddress.FromHandle(reader, type);
        var propertyRequest = new MetadataAccessorDeclarationRequest(
            typeAddress,
            MetadataAccessorDeclarationAddress.Create(reader, property));
        var eventRequest = new MetadataAccessorDeclarationRequest(
            typeAddress,
            MetadataAccessorDeclarationAddress.Create(reader, @event));

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        var propertyPosted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    propertyRequest,
                    TestContext.Current.CancellationToken));
        var eventPosted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                declaration.PostAccessorDeclaration(
                    eventRequest,
                    TestContext.Current.CancellationToken));

        Assert.Equal(typeAddress, propertyPosted.Evidence.Type);
        Assert.Equal(typeAddress, eventPosted.Evidence.Type);
        Assert.Equal(2, propertyPosted.Evidence.Accessors.Length);
        Assert.Equal(2, eventPosted.Evidence.Accessors.Length);

        Dictionary<MetadataAccessorSemanticsRole,
            MetadataMethodDeclarationEvidence> propertyAccessors =
            propertyPosted.Evidence.Accessors.ToDictionary(
                accessor => accessor.Role,
                accessor => accessor.Method);
        Assert.Equal(
            "get_Property",
            propertyAccessors[MetadataAccessorSemanticsRole.Getter]
                .Name.ToString());
        Assert.Empty(
            propertyAccessors[MetadataAccessorSemanticsRole.Getter]
                .Signature.ParameterTypes);
        Assert.Equal(
            "set_Property",
            propertyAccessors[MetadataAccessorSemanticsRole.Setter]
                .Name.ToString());
        Assert.Single(
            propertyAccessors[MetadataAccessorSemanticsRole.Setter]
                .Signature.ParameterTypes);

        Dictionary<MetadataAccessorSemanticsRole,
            MetadataMethodDeclarationEvidence> eventAccessors =
            eventPosted.Evidence.Accessors.ToDictionary(
                accessor => accessor.Role,
                accessor => accessor.Method);
        Assert.Equal(
            "add_CustomEvent",
            eventAccessors[MetadataAccessorSemanticsRole.AddOn]
                .Name.ToString());
        Assert.Equal(
            "remove_CustomEvent",
            eventAccessors[MetadataAccessorSemanticsRole.RemoveOn]
                .Name.ToString());
        Assert.All(
            propertyAccessors.Values.Concat(eventAccessors.Values),
            accessor =>
            {
                Assert.True(accessor.Attributes.HasFlag(
                    MethodAttributes.SpecialName));
                Assert.False(accessor.Attributes.HasFlag(
                    MethodAttributes.Static));
                Assert.False(accessor.OperatorCandidate);
            });
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property)]
    [InlineData(MetadataAccessorDeclarationKind.Event)]
    public void Mdp004_ZeroAccessorAggregateIsComplete(
        MetadataAccessorDeclarationKind kind)
    {
        using var fixture = Fixture.Create(
            methodCount: 0,
            propertyCount:
                kind == MetadataAccessorDeclarationKind.Property ? 1 : 0,
            eventCount:
                kind == MetadataAccessorDeclarationKind.Event ? 1 : 0);

        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(
                Run(fixture, kind));

        Assert.Empty(posted.Evidence.Accessors);
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0000)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0003)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0008)]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x8000)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0x0002)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0x0018)]
    public void Mdp004_IllegalRoleBitsRejectTheAggregate(
        MetadataAccessorDeclarationKind kind,
        ushort rawSemantics)
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount:
                kind == MetadataAccessorDeclarationKind.Property ? 1 : 0,
            eventCount:
                kind == MetadataAccessorDeclarationKind.Event ? 1 : 0,
            rows:
            [
                new(
                    1,
                    kind == MetadataAccessorDeclarationKind.Property
                        ? MethodSemanticsAssociationKind.Property
                        : MethodSemanticsAssociationKind.Event,
                    1,
                    rawSemantics),
            ]);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(fixture, kind));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.RoleValidation,
            rejected.Failure.Mechanism);
        Assert.Equal(1, rejected.Failure.PhysicalRowNumber);
        Assert.Equal(rawSemantics, rejected.Failure.RawSemantics);
        Assert.Null(rejected.Failure.AccessorFailure);
    }

    [Theory]
    [InlineData(MetadataAccessorDeclarationKind.Property, 0x0002)]
    [InlineData(MetadataAccessorDeclarationKind.Event, 0x0008)]
    public void Mdp004_DuplicateConventionalRoleRejectsTheAggregate(
        MetadataAccessorDeclarationKind kind,
        ushort rawSemantics)
    {
        MethodSemanticsAssociationKind associationKind =
            kind == MetadataAccessorDeclarationKind.Property
                ? MethodSemanticsAssociationKind.Property
                : MethodSemanticsAssociationKind.Event;
        using var fixture = Fixture.Create(
            methodCount: 2,
            propertyCount:
                kind == MetadataAccessorDeclarationKind.Property ? 1 : 0,
            eventCount:
                kind == MetadataAccessorDeclarationKind.Event ? 1 : 0,
            rows:
            [
                new(1, associationKind, 1, rawSemantics),
                new(2, associationKind, 1, rawSemantics),
            ]);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(fixture, kind));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.DuplicateRole,
            rejected.Failure.Mechanism);
        Assert.Equal(2, rejected.Failure.PhysicalRowNumber);
    }

    [Fact]
    public void Mdp004_CrossDeclaringTypeMethodRejectsTheAggregate()
    {
        using var fixture = Fixture.Create(
            methodCount: 2,
            propertyCount: 1,
            rows:
            [
                PropertyRow(2, 0x0002),
            ],
            secondTypeMethodStart: 2);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.DirectOwnership,
            rejected.Failure.Mechanism);
        Assert.Equal(1, rejected.Failure.PhysicalRowNumber);
        Assert.Equal(2, rejected.Failure.Method!.Value.Token
            & 0x00FFFFFF);
    }

    [Fact]
    public void Mdp004_NonmonotonicAssociationOrderRejectsTheCensus()
    {
        byte[] image = MethodSemanticsRowReaderTests.BuildImage(
            methodCount: 2,
            propertyCount: 2,
            rows:
            [
                PropertyRow(1, 0x0002, propertyRow: 1),
                PropertyRow(2, 0x0002, propertyRow: 2),
            ]);
        MethodSemanticsRowReaderTests.PatchAssociation(
            image,
            encodedAssociation: 5,
            rowIndex: 0);
        MethodSemanticsRowReaderTests.PatchAssociation(
            image,
            encodedAssociation: 3,
            rowIndex: 1);
        using var fixture = new Fixture(image);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.AssociationOrdering,
            rejected.Failure.Mechanism);
        Assert.Null(rejected.Failure.AccessorFailure);
    }

    [Fact]
    public void Mdp006_CensusBudgetRejectionRemainsVisible()
    {
        using var fixture = Fixture.Create(
            methodCount: 2,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
                PropertyRow(2, 0x0001),
            ]);
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxRetainedMethodSemanticsAssociations: 1);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property,
                    policy));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.AssociationCensus,
            rejected.Failure.Stage);
        Assert.NotNull(rejected.Failure.CensusFailure);
        Assert.Equal(
            MetadataOperationDimension
                .RetainedMethodSemanticsAssociations,
            rejected.Failure.BudgetDimension);
        Assert.Null(rejected.Failure.AccessorFailure);
    }

    [Fact]
    public void Mdp006_AccessorPostRejectionRemainsVisible()
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
            ]);
        var policy = new MetadataOperationPolicy(
            long.MaxValue,
            maxDeclarationCandidates: 1);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                Run(
                    fixture,
                    MetadataAccessorDeclarationKind.Property,
                    policy));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationStage.AccessorDeclaration,
            rejected.Failure.Stage);
        Assert.NotNull(rejected.Failure.AccessorFailure);
        Assert.Equal(
            MetadataMethodDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.AccessorFailure!.Reason);
        Assert.Equal(
            MetadataOperationDimension.DeclarationCandidates,
            rejected.Failure.BudgetDimension);
    }

    [Fact]
    public void Mdp006_ImageAdmissionRejectionAvoidsCensusWork()
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
            ]);
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var work = new List<MetadataOperationWorkKind>();
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(maxMetadataRows: 0),
            work.Add);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property);

        var rejected = Assert.IsType<
            MetadataAccessorDeclarationResult.Rejected>(
                declaration.PostAccessorDeclaration(
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataAccessorDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataAccessorDeclarationMechanism.ImageAdmission,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataOperationDimension.MetadataRows,
            rejected.Failure.BudgetDimension);
        Assert.Empty(work);
    }

    [Fact]
    public void Mdp009_CachedAggregateRequiresLiveDeclarationOwner()
    {
        using var fixture = Fixture.Create(
            methodCount: 1,
            propertyCount: 1,
            rows:
            [
                PropertyRow(1, 0x0002),
            ]);
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                MetadataAccessorDeclarationKind.Property);
        MetadataAccessorDeclarationResult detached =
            declaration.PostAccessorDeclaration(
                request,
                TestContext.Current.CancellationToken);

        declaration.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            declaration.PostAccessorDeclaration(
                request,
                TestContext.Current.CancellationToken));
        var posted = Assert.IsType<
            MetadataAccessorDeclarationResult.Posted>(detached);
        Assert.Single(posted.Evidence.Accessors);
    }

    static MetadataAccessorDeclarationResult Run(
        Fixture fixture,
        MetadataAccessorDeclarationKind kind,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(operation);
        MetadataAccessorDeclarationRequest request =
            CreateRequest(
                assembly.GetMetadataReaderForDeclarationSession(),
                kind);
        return declaration.PostAccessorDeclaration(
            request,
            TestContext.Current.CancellationToken);
    }

    static MetadataAccessorDeclarationRequest CreateRequest(
        MetadataReader reader,
        MetadataAccessorDeclarationKind kind)
    {
        MetadataTypeDefinitionAddress type =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                MetadataTokens.TypeDefinitionHandle(2));
        MetadataAccessorDeclarationAddress declaration = kind switch
        {
            MetadataAccessorDeclarationKind.Property =>
                MetadataAccessorDeclarationAddress.Create(
                    reader,
                    MetadataTokens.PropertyDefinitionHandle(1)),
            MetadataAccessorDeclarationKind.Event =>
                MetadataAccessorDeclarationAddress.Create(
                    reader,
                    MetadataTokens.EventDefinitionHandle(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new(type, declaration);
    }

    static MethodSemanticsRowReaderTests.RawSemanticsRow PropertyRow(
        int methodRow,
        ushort rawSemantics,
        int propertyRow = 1) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Property,
            propertyRow,
            rawSemantics);

    static MethodSemanticsRowReaderTests.RawSemanticsRow EventRow(
        int methodRow,
        ushort rawSemantics) =>
        new(
            methodRow,
            MethodSemanticsAssociationKind.Event,
            1,
            rawSemantics);

    sealed class Fixture : IDisposable
    {
        internal Fixture(byte[] image)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"accessor-declaration-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(Path, image);
        }

        internal string Path { get; }

        internal static Fixture Create(
            int methodCount,
            int propertyCount = 0,
            int eventCount = 0,
            IReadOnlyList<
                MethodSemanticsRowReaderTests.RawSemanticsRow>? rows = null,
            int? secondTypeMethodStart = null) =>
            new(
                MethodSemanticsRowReaderTests.BuildImage(
                    methodCount: methodCount,
                    propertyCount: propertyCount,
                    eventCount: eventCount,
                    rows: rows,
                    secondTypeMethodStart: secondTypeMethodStart));

        public void Dispose() => File.Delete(Path);
    }
}
