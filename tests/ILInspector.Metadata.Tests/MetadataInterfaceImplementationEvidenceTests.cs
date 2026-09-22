using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Metadata.ConsumerCanary;
using ILInspector.MetadataPrimitives;
using InertText;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataInterfaceImplementationEvidenceTests
{
    [Fact]
    public void ConstructedInterfaceMatchUsesMethodImplIdentityCurrency()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);

        MetadataInterfaceImplementationResult.Related related =
            AssertRelated(
                Run(path, type, interfaceType));
        MetadataInterfaceImplementationCertificate certificate =
            Assert.Single(related.Relationships);

        Assert.Equal(interfaceType, certificate.Interface);
        Assert.Equal(
            HandleKind.TypeSpecification,
            certificate.Target.Handle.Kind);
        Assert.Equal(1, related.Counters.InterfaceImplementationRows);
    }

    [Fact]
    public void GenericModifierIdentityIssuedByMethodImplMatches()
    {
        using GenericModifierFixture fixture =
            GenericModifierFixture.Create();
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(
                fixture.Path,
                fixture.Type,
                fixture.Body);

        MetadataInterfaceImplementationResult.Related related =
            AssertRelated(
                Run(
                    fixture.Path,
                    fixture.Type,
                    interfaceType));
        MetadataInterfaceImplementationCertificate certificate =
            Assert.Single(related.Relationships);

        Assert.Equal(interfaceType, certificate.Interface);
    }

    [Fact]
    public void SameSpelledArgumentsFromDifferentAssembliesStayDistinct()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle externalType,
            MethodDefinitionHandle externalBody) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        (TypeDefinitionHandle localType,
            MethodDefinitionHandle localBody) =
            FindExplicitImplementation(
                path,
                "LocalArgumentImplementation");
        MetadataTypeIdentity externalIdentity =
            ReadDeclarationOwner(
                path,
                externalType,
                externalBody);
        MetadataTypeIdentity localIdentity =
            ReadDeclarationOwner(
                path,
                localType,
                localBody);

        Assert.NotEqual(externalIdentity, localIdentity);
        AssertRelated(
            Run(path, externalType, externalIdentity));
        AssertRelated(
            Run(path, localType, localIdentity));
        Assert.IsType<MetadataInterfaceImplementationResult.Absent>(
            Run(path, externalType, localIdentity));
        Assert.IsType<MetadataInterfaceImplementationResult.Absent>(
            Run(path, localType, externalIdentity));
    }

    [Fact]
    public void ReadableUnrelatedRowDoesNotPoisonLaterMatch()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(path, "MultipleImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);

        MetadataInterfaceImplementationResult.Related related =
            AssertRelated(
                Run(path, type, interfaceType));

        Assert.Single(related.Relationships);
        Assert.Equal(2, related.Counters.InterfaceImplementationRows);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        InterfaceImplementationHandle expected =
            pe.GetMetadataReader().GetTypeDefinition(type)
                .GetInterfaceImplementations()
                .Last();
        Assert.Equal(
            expected,
            related.Relationships[0].Relationship.Handle);
    }

    [Fact]
    public void RealSystemInt32GenericMathAssociationMatches()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        Assert.Contains(
            "11.0.0-rc.1.26425.128",
            FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "");
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type =
            FindType(reader, "Int32", "System");
        MethodDefinitionHandle body =
            reader.GetTypeDefinition(type).GetMethods()
                .Single(handle =>
                {
                    string name = reader.GetString(
                        reader.GetMethodDefinition(handle).Name);
                    return name.Contains(
                            "IAdditionOperators",
                            StringComparison.Ordinal)
                        && name.EndsWith(
                            ".op_Addition",
                            StringComparison.Ordinal);
                });
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);

        MetadataInterfaceImplementationResult.Related related =
            AssertRelated(
                Run(path, type, interfaceType));
        MetadataInterfaceImplementationCertificate certificate =
            Assert.Single(related.Relationships);
        var constructed =
            Assert.IsType<MetadataTypeIdentity.GenericInstance>(
                certificate.Interface);

        Assert.Equal(3, constructed.Arguments.Length);
        Assert.Equal(interfaceType, certificate.Interface);
    }

    [Fact]
    public void OperationSpecificLimitsHaveExactBoundaries()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);
        MetadataInterfaceImplementationResult.Related baseline =
            AssertRelated(
                Run(path, type, interfaceType));

        foreach (MetadataOperationDimension dimension in new[]
        {
            MetadataOperationDimension.InterfaceImplementationRows,
            MetadataOperationDimension.RelationshipEdges,
            MetadataOperationDimension.SignatureBytes,
            MetadataOperationDimension.StructuredNodes,
            MetadataOperationDimension.RetainedText,
        })
        {
            long exact = Counter(baseline.Counters, dimension);
            Assert.True(exact > 0, $"{dimension} was vacuous.");

            MetadataInterfaceImplementationResult.Rejected below =
                Assert.IsType<
                    MetadataInterfaceImplementationResult.Rejected>(
                        Run(
                            path,
                            type,
                            interfaceType,
                            Policy(dimension, exact - 1)));
            Assert.Equal(
                MetadataInterfaceImplementationFailureReason
                    .BudgetExceeded,
                below.Failure.Reason);
            Assert.Equal(
                dimension,
                below.Failure.BudgetDimension);

            AssertRelated(
                Run(
                    path,
                    type,
                    interfaceType,
                    Policy(dimension, exact)));
            MetadataInterfaceImplementationResult.Related above =
                AssertRelated(
                    Run(
                        path,
                        type,
                        interfaceType,
                        Policy(dimension, exact + 1)));
            Assert.Equal(
                exact,
                Counter(above.Counters, dimension));
        }
    }

    [Fact]
    public void NestedTypeDefinitionTraversalChargesRelationshipEdges()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.CreateNestedTypeDefinition(depth: 8);
        MetadataInterfaceImplementationResult.Related baseline =
            AssertRelated(
                Run(fixture, fixture.InterfaceIdentity));

        Assert.True(
            baseline.Counters.RelationshipEdges > 2,
            "The nested declaring chain did not contribute edge work.");
        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(
                        fixture,
                        fixture.InterfaceIdentity,
                        Policy(
                            MetadataOperationDimension.RelationshipEdges,
                            2)));
        Assert.Equal(
            MetadataInterfaceImplementationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            rejected.Failure.BudgetDimension);
    }

    [Fact]
    public void TypeReferenceEdgeBudgetRejectsBeforeAncestorNameRead()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.CreateNestedTypeReference(
                depth: 8,
                corruptRootName: true);

        MetadataInterfaceImplementationResult.Rejected malformed =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(fixture, fixture.InterfaceIdentity));
        Assert.Equal(
            MetadataInterfaceImplementationFailureReason
                .MalformedMetadata,
            malformed.Failure.Reason);

        MetadataInterfaceImplementationResult.Rejected limited =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(
                        fixture,
                        fixture.InterfaceIdentity,
                        Policy(
                            MetadataOperationDimension.RelationshipEdges,
                            2)));
        Assert.Equal(
            MetadataInterfaceImplementationFailureReason.BudgetExceeded,
            limited.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            limited.Failure.BudgetDimension);
        Assert.Equal(2, limited.Counters.RelationshipEdges);
    }

    [Fact]
    public void CurrentModuleTypeReferenceUsesTheUniqueModuleRow()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.CreateTypeReferenceScope(moduleRow: 1);

        AssertRelated(
            Run(fixture, fixture.InterfaceIdentity));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void InvalidModuleTypeReferenceScopeRejects(
        int moduleRow)
    {
        using AuthoredFixture fixture =
            AuthoredFixture.CreateTypeReferenceScope(moduleRow);

        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(fixture, fixture.InterfaceIdentity));

        Assert.Equal(
            MetadataInterfaceImplementationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataInterfaceImplementationMechanism
                .RelationshipTraversal,
            rejected.Failure.Mechanism);
        Assert.Equal(
            HandleKind.ModuleDefinition,
            rejected.Failure.RelevantHandle.Kind);
        Assert.Equal(
            moduleRow,
            MetadataTokens.GetRowNumber(
                rejected.Failure.RelevantHandle));
    }

    [Fact]
    public void TypeReferenceCycleRemainsTheFirstDecisiveRejection()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.CreateTypeReferenceScope(
                moduleRow: 1,
                cycle: true);

        MetadataInterfaceImplementationResult.Rejected unbounded =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(fixture, fixture.InterfaceIdentity));
        MetadataInterfaceImplementationResult.Rejected limited =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(
                        fixture,
                        fixture.InterfaceIdentity,
                        Policy(
                            MetadataOperationDimension.RelationshipEdges,
                            4)));

        foreach (MetadataInterfaceImplementationResult.Rejected rejected
            in new[] { unbounded, limited })
        {
            Assert.Equal(
                MetadataInterfaceImplementationFailureReason.Cycle,
                rejected.Failure.Reason);
            Assert.Equal(
                MetadataInterfaceImplementationMechanism
                    .RelationshipTraversal,
                rejected.Failure.Mechanism);
            Assert.Equal(
                HandleKind.TypeReference,
                rejected.Failure.RelevantHandle.Kind);
        }
        Assert.True(limited.Counters.RelationshipEdges <= 4);
    }

    [Fact]
    public void InvalidIdentityAndForeignTypeRejectBeforeScan()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);

        MetadataInterfaceImplementationResult.Rejected invalidShape =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(
                        path,
                        type,
                        new MetadataTypeIdentity.Primitive(
                            new(
                                InertText.TextPolicy.Field,
                                "int"))));
        Assert.Equal(
            MetadataInterfaceImplementationFailureReason.InvalidRequest,
            invalidShape.Failure.Reason);
        Assert.Equal(
            0,
            invalidShape.Counters.InterfaceImplementationRows);

        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using var declarations =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        MetadataTypeDefinitionAddress foreign =
            MetadataTypeDefinitionAddress.FromHandle(reader, type)
                with
                {
                    ModuleVersionId = Guid.NewGuid(),
                };
        MetadataInterfaceImplementationResult.Rejected invalidType =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    declarations.Relate(
                        foreign,
                        interfaceType,
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            MetadataInterfaceImplementationFailureReason.InvalidRequest,
            invalidType.Failure.Reason);
        Assert.Equal(
            0,
            invalidType.Counters.InterfaceImplementationRows);
    }

    [Fact]
    public void IncompleteRequestedIdentitiesRejectBeforeScan()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        var valid =
            Assert.IsType<MetadataTypeIdentity.GenericInstance>(
                ReadDeclarationOwner(path, type, body));
        MetadataTypeIdentity[] invalid =
        [
            new MetadataTypeIdentity.Named(
                Definition: null!,
                IsValueType: false),
            new MetadataTypeIdentity.GenericInstance(
                valid.Definition,
                IsValueType: false,
                Arguments: []),
            new MetadataTypeIdentity.GenericInstance(
                valid.Definition,
                IsValueType: true,
                valid.Arguments),
        ];

        foreach (MetadataTypeIdentity identity in invalid)
        {
            MetadataInterfaceImplementationResult.Rejected rejected =
                Assert.IsType<
                    MetadataInterfaceImplementationResult.Rejected>(
                        Run(path, type, identity));
            Assert.Equal(
                MetadataInterfaceImplementationFailureReason
                    .InvalidRequest,
                rejected.Failure.Reason);
            Assert.Equal(
                0,
                rejected.Counters.InterfaceImplementationRows);
        }
    }

    [Fact]
    public void ResultsRemainUsableAfterOwnersAreDisposed()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);

        MetadataInterfaceImplementationResult.Related related =
            AssertRelated(Run(path, type, interfaceType));
        MetadataInterfaceImplementationResult.Absent absent =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Absent>(
                    Run(
                        path,
                        type,
                        ReadDeclarationOwner(
                            path,
                            FindExplicitImplementation(
                                path,
                                "LocalArgumentImplementation")
                                .Type,
                            FindExplicitImplementation(
                                path,
                                "LocalArgumentImplementation")
                                .Body)));
        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(
                        path,
                        type,
                        interfaceType,
                        Policy(
                            MetadataOperationDimension
                                .InterfaceImplementationRows,
                            0)));

        Assert.False(
            related.Relationships[0].Relationship.Handle.IsNil);
        Assert.True(absent.Counters.MetadataRows > 0);
        Assert.NotEmpty(rejected.Failure.Detail);
    }

    [Fact]
    public void NoFriendConsumerInvokesAndConsumesPublicOperation()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();

        MetadataInterfaceImplementationResult result =
            MetadataMethodImplementationConsumerCanary.RelateInterface(
                path,
                MetadataTypeDefinitionAddress.FromHandle(reader, type),
                interfaceType);

        Assert.True(
            MetadataMethodImplementationConsumerCanary.Consume(result));
    }

    [Fact]
    public void CancellationPropagatesWithoutAResult()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using var declarations =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();

        OperationCanceledException cancelled =
            Assert.Throws<OperationCanceledException>(
                () => declarations.Relate(
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        type),
                    interfaceType,
                    source.Token));

        Assert.Equal(source.Token, cancelled.CancellationToken);
    }

    [Fact]
    public void AuthoredDuplicateRowsRemainDistinctAndOrdered()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.Create(
                AuthoredScenario.DuplicateRows);

        MetadataInterfaceImplementationResult.Related related =
            AssertRelated(
                Run(fixture, fixture.InterfaceIdentity));

        Assert.Equal(2, related.Relationships.Length);
        Assert.Equal(
            [1, 2],
            related.Relationships.Select(
                relationship =>
                    MetadataTokens.GetRowNumber(
                        relationship.Relationship.Handle)));
        Assert.Equal(
            related.Relationships[0].Interface,
            related.Relationships[1].Interface);
        Assert.NotEqual(
            related.Relationships[0].Relationship,
            related.Relationships[1].Relationship);
    }

    [Fact]
    public void UnreadableRowAfterMatchRejectsWithoutPartialEvidence()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.Create(
                AuthoredScenario.InvalidTargetAfterMatch);

        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(fixture, fixture.InterfaceIdentity));

        Assert.Equal(
            MetadataInterfaceImplementationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            2,
            MetadataTokens.GetRowNumber(
                rejected.Failure.RelevantRow!.Value));
        Assert.Equal(2, rejected.Counters.InterfaceImplementationRows);
        Assert.Null(
            typeof(MetadataInterfaceImplementationResult.Rejected)
                .GetProperty("Relationships"));
    }

    [Fact]
    public void MalformedTypeSpecRejectsWithoutCertifyingAbsence()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.Create(
                AuthoredScenario.MalformedTypeSpecification);

        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(fixture, fixture.InterfaceIdentity));

        Assert.Equal(
            MetadataInterfaceImplementationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            HandleKind.TypeSpecification,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void CyclicTypeSpecRejectsWithoutCertifyingAbsence()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.Create(
                AuthoredScenario.CyclicTypeSpecification);

        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(fixture, fixture.InterfaceIdentity));

        Assert.True(
            rejected.Failure.Reason
                == MetadataInterfaceImplementationFailureReason.Cycle,
            rejected.Failure.Detail);
        Assert.Equal(
            HandleKind.TypeSpecification,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void TypeSpecOverOperationBudgetRejectsWithoutCertifyingAbsence()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.Create(
                AuthoredScenario.CyclicTypeSpecification);

        MetadataInterfaceImplementationResult.Rejected rejected =
            Assert.IsType<
                MetadataInterfaceImplementationResult.Rejected>(
                    Run(
                        fixture,
                        fixture.InterfaceIdentity,
                        Policy(
                            MetadataOperationDimension.SignatureBytes,
                            0)));

        Assert.Equal(
            MetadataInterfaceImplementationFailureReason
                .BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.SignatureBytes,
            rejected.Failure.BudgetDimension);
        Assert.Equal(
            HandleKind.TypeSpecification,
            rejected.Failure.RelevantHandle.Kind);
    }

    [Fact]
    public void InterfaceRowCountersAccumulateAcrossCallsAndSessions()
    {
        string path =
            FixtureCatalog.MetadataInterfaceImplFixtures.AssemblyPath();
        (TypeDefinitionHandle type, MethodDefinitionHandle body) =
            FindExplicitImplementation(
                path,
                "ExternalArgumentImplementation");
        MetadataTypeIdentity interfaceType =
            ReadDeclarationOwner(path, type, body);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);

        MetadataInterfaceImplementationResult.Related first =
            Run(path, type, interfaceType, operation);
        MetadataInterfaceImplementationResult.Related second =
            Run(path, type, interfaceType, operation);

        Assert.Equal(
            1,
            first.Counters.InterfaceImplementationRows);
        Assert.Equal(
            2,
            second.Counters.InterfaceImplementationRows);
    }

    [Fact]
    public void CancellationAfterFirstMatchPublishesNoPartialResult()
    {
        using AuthoredFixture fixture =
            AuthoredFixture.Create(
                AuthoredScenario.DuplicateRows);
        using var source = new CancellationTokenSource();
        int rows = 0;
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded,
                kind =>
                {
                    if (kind
                            == MetadataOperationWorkKind
                                .InterfaceImplementationRowRead
                        && ++rows == 2)
                    {
                        source.Cancel();
                    }
                });
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var declarations =
            assembly.CreateDeclarationSession(operation);

        OperationCanceledException cancelled =
            Assert.Throws<OperationCanceledException>(
                () => declarations.Relate(
                    fixture.Type,
                    fixture.InterfaceIdentity,
                    source.Token));

        Assert.Equal(source.Token, cancelled.CancellationToken);
        Assert.Equal(2, operation.Counters.InterfaceImplementationRows);
    }

    static MetadataInterfaceImplementationResult Run(
        string path,
        TypeDefinitionHandle type,
        MetadataTypeIdentity interfaceType,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation =
            new MetadataOperationContext(
                policy ?? MetadataOperationPolicy.Unbounded);
        using var declarations =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        return declarations.Relate(
            MetadataTypeDefinitionAddress.FromHandle(reader, type),
            interfaceType,
            TestContext.Current.CancellationToken);
    }

    static MetadataInterfaceImplementationResult.Related Run(
        string path,
        TypeDefinitionHandle type,
        MetadataTypeIdentity interfaceType,
        MetadataOperationContext operation)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var declarations =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        return AssertRelated(
            declarations.Relate(
                MetadataTypeDefinitionAddress.FromHandle(reader, type),
                interfaceType,
                TestContext.Current.CancellationToken));
    }

    static MetadataInterfaceImplementationResult Run(
        AuthoredFixture fixture,
        MetadataTypeIdentity interfaceType,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation =
            new MetadataOperationContext(
                policy ?? MetadataOperationPolicy.Unbounded);
        using var declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.Relate(
            fixture.Type,
            interfaceType,
            TestContext.Current.CancellationToken);
    }

    static MetadataTypeIdentity ReadDeclarationOwner(
        string path,
        TypeDefinitionHandle type,
        MethodDefinitionHandle body)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation =
            new MetadataOperationContext(
                MetadataOperationPolicy.Unbounded);
        using var declarations =
            assembly.CreateDeclarationSession(operation);
        MetadataReader reader =
            assembly.GetMetadataReaderForDeclarationSession();
        var related =
            Assert.IsType<MetadataMethodImplementationResult.Related>(
                declarations.Relate(
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        type),
                    MetadataMethodAddress.Create(reader, body),
                    TestContext.Current.CancellationToken));
        return Assert.Single(related.Relationships).DeclarationOwner;
    }

    static (TypeDefinitionHandle Type, MethodDefinitionHandle Body)
        FindExplicitImplementation(
            string path,
            string typeName)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle type = FindType(reader, typeName);
        MethodDefinitionHandle body =
            reader.GetTypeDefinition(type).GetMethods()
                .Single(handle =>
                    reader.GetString(
                            reader.GetMethodDefinition(handle).Name)
                        .EndsWith(
                            ".Echo",
                            StringComparison.Ordinal));
        return (type, body);
    }

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string name,
        string? @namespace = null) =>
        reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            return reader.StringComparer.Equals(
                    definition.Name,
                    name)
                && (@namespace is null
                    || reader.StringComparer.Equals(
                        definition.Namespace,
                        @namespace));
        });

    static MetadataInterfaceImplementationResult.Related AssertRelated(
        MetadataInterfaceImplementationResult result) =>
        Assert.IsType<
            MetadataInterfaceImplementationResult.Related>(result);

    static long Counter(
        MetadataOperationCounters counters,
        MetadataOperationDimension dimension) =>
        dimension switch
        {
            MetadataOperationDimension.InterfaceImplementationRows =>
                counters.InterfaceImplementationRows,
            MetadataOperationDimension.RelationshipEdges =>
                counters.RelationshipEdges,
            MetadataOperationDimension.SignatureBytes =>
                counters.SignatureBytes,
            MetadataOperationDimension.StructuredNodes =>
                counters.StructuredNodes,
            MetadataOperationDimension.RetainedText =>
                counters.RetainedText,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dimension)),
        };

    static MetadataOperationPolicy Policy(
        MetadataOperationDimension dimension,
        long limit) =>
        new(
            maxMetadataRows: long.MaxValue,
            maxRelationshipEdges:
                dimension
                    == MetadataOperationDimension.RelationshipEdges
                    ? limit
                    : long.MaxValue,
            maxSignatureBytes:
                dimension
                    == MetadataOperationDimension.SignatureBytes
                    ? limit
                    : long.MaxValue,
            maxStructuredNodes:
                dimension
                    == MetadataOperationDimension.StructuredNodes
                    ? limit
                    : long.MaxValue,
            maxRetainedText:
                dimension
                    == MetadataOperationDimension.RetainedText
                    ? limit
                    : long.MaxValue,
            maxInterfaceImplementationRows:
                dimension
                    == MetadataOperationDimension
                        .InterfaceImplementationRows
                    ? limit
                    : long.MaxValue);

    enum AuthoredScenario
    {
        DuplicateRows,
        InvalidTargetAfterMatch,
        MalformedTypeSpecification,
        CyclicTypeSpecification,
    }

    sealed class GenericModifierFixture : IDisposable
    {
        GenericModifierFixture(
            string path,
            TypeDefinitionHandle type,
            MethodDefinitionHandle body)
        {
            Path = path;
            Type = type;
            Body = body;
        }

        internal string Path { get; }
        internal TypeDefinitionHandle Type { get; }
        internal MethodDefinitionHandle Body { get; }

        internal static GenericModifierFixture Create()
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("generic-modifier.dll"),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("GenericModifierFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle contract =
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract`1"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddGenericParameter(
                contract,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            TypeDefinitionHandle modifier =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("Modifier`1"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));
            metadata.AddGenericParameter(
                modifier,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(2));

            var interfaceSignature = new BlobBuilder();
            interfaceSignature.WriteByte(0x15);
            interfaceSignature.WriteByte(0x12);
            interfaceSignature.WriteCompressedInteger(
                EncodeTypeDefOrRef(contract));
            interfaceSignature.WriteCompressedInteger(1);
            interfaceSignature.WriteByte(0x20);
            interfaceSignature.WriteCompressedInteger(
                EncodeTypeDefOrRef(modifier));
            interfaceSignature.WriteByte(0x08);
            TypeSpecificationHandle interfaceType =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(interfaceSignature));
            metadata.AddInterfaceImplementation(
                target,
                interfaceType);

            var methodSignature = new BlobBuilder();
            methodSignature.WriteByte(0x20);
            methodSignature.WriteCompressedInteger(0);
            methodSignature.WriteByte(0x01);
            BlobHandle signature =
                metadata.GetOrAddBlob(methodSignature);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1));
            MethodDefinitionHandle body =
                metadata.AddMethodDefinition(
                    MethodAttributes.Private
                        | MethodAttributes.Virtual
                        | MethodAttributes.Final,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString("Body"),
                    signature,
                    bodyOffset: 0,
                    MetadataTokens.ParameterHandle(1));
            MemberReferenceHandle declaration =
                metadata.AddMemberReference(
                    interfaceType,
                    metadata.GetOrAddString("M"),
                    signature);
            metadata.AddMethodImplementation(
                target,
                body,
                declaration);

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"interfaceimpl-modifier-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, Serialize(metadata));
            return new(path, target, body);
        }

        static int EncodeTypeDefOrRef(EntityHandle handle)
        {
            int tag = handle.Kind switch
            {
                HandleKind.TypeDefinition => 0,
                HandleKind.TypeReference => 1,
                HandleKind.TypeSpecification => 2,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(handle)),
            };
            return checked(
                (MetadataTokens.GetRowNumber(handle) << 2) | tag);
        }

        static byte[] Serialize(MetadataBuilder metadata)
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly)
                .Serialize(image);
            return image.ToArray();
        }

        public void Dispose() => File.Delete(Path);
    }

    sealed class AuthoredFixture : IDisposable
    {
        AuthoredFixture(
            string path,
            MetadataTypeDefinitionAddress type,
            MetadataTypeIdentity interfaceIdentity)
        {
            Path = path;
            Type = type;
            InterfaceIdentity = interfaceIdentity;
        }

        internal string Path { get; }
        internal MetadataTypeDefinitionAddress Type { get; }
        internal MetadataTypeIdentity InterfaceIdentity { get; }

        internal static AuthoredFixture Create(
            AuthoredScenario scenario)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("fixture.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("InterfaceImplFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            bool generic =
                scenario
                    == AuthoredScenario.CyclicTypeSpecification;
            TypeDefinitionHandle contract =
                metadata.AddTypeDefinition(
                    TypeAttributes.Interface
                        | TypeAttributes.Abstract
                        | TypeAttributes.Public,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString(
                        generic
                            ? "IContract`1"
                            : "IContract"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            if (generic)
            {
                metadata.AddGenericParameter(
                    contract,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            }

            switch (scenario)
            {
                case AuthoredScenario.DuplicateRows:
                    metadata.AddInterfaceImplementation(
                        target,
                        contract);
                    metadata.AddInterfaceImplementation(
                        target,
                        contract);
                    break;
                case AuthoredScenario.InvalidTargetAfterMatch:
                    metadata.AddInterfaceImplementation(
                        target,
                        contract);
                    metadata.AddInterfaceImplementation(
                        target,
                        MetadataTokens.TypeReferenceHandle(99));
                    break;
                case AuthoredScenario.MalformedTypeSpecification:
                    metadata.AddInterfaceImplementation(
                        target,
                        metadata.AddTypeSpecification(
                            AddBlob(metadata, 0x15)));
                    break;
                case AuthoredScenario.CyclicTypeSpecification:
                    int encodedContract =
                        EncodeTypeDefOrRef(contract);
                    int encodedSelf =
                        EncodeTypeDefOrRef(
                            MetadataTokens.TypeSpecificationHandle(
                                1));
                    metadata.AddInterfaceImplementation(
                        target,
                        metadata.AddTypeSpecification(
                            AddBlob(
                                metadata,
                                0x20,
                                checked((byte)encodedSelf),
                                0x12,
                                checked((byte)encodedContract))));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(scenario));
            }

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"interfaceimpl-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, Serialize(metadata));
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            var typeAddress =
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    target);
            var definitionIdentity =
                new MetadataNamedTypeIdentity(
                    new MetadataTypeScopeIdentity(
                        MetadataTypeScopeKind.CurrentModule,
                        mvid,
                        Text("fixture.dll"),
                        new MetadataAssemblyIdentity(
                            Text("InterfaceImplFixture"),
                            new Version(1, 0, 0, 0),
                            Culture: null,
                            PublicKeyToken: null)),
                    Text("Contracts"),
                    [Text(generic ? "IContract`1" : "IContract")],
                    [generic ? 1 : 0]);
            MetadataTypeIdentity identity = generic
                ? new MetadataTypeIdentity.GenericInstance(
                    definitionIdentity,
                    IsValueType: false,
                    [
                        new MetadataTypeIdentity.Primitive(
                            Text("int")),
                    ])
                : new MetadataTypeIdentity.Named(
                    definitionIdentity,
                    IsValueType: false);
            return new(path, typeAddress, identity);
        }

        internal static AuthoredFixture CreateNestedTypeDefinition(
            int depth)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(depth, 2);
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("nested-interface.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("NestedInterfaceFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

            var segments = new string[depth];
            TypeDefinitionHandle parent = default;
            TypeDefinitionHandle contract = default;
            for (int index = 0; index < depth; index++)
            {
                segments[index] =
                    index == depth - 1
                        ? "IContract"
                        : $"Outer{index}";
                TypeAttributes attributes =
                    index == 0
                        ? TypeAttributes.Public
                        : TypeAttributes.NestedPublic;
                if (index == depth - 1)
                {
                    attributes |=
                        TypeAttributes.Interface
                        | TypeAttributes.Abstract;
                }
                contract = metadata.AddTypeDefinition(
                    attributes,
                    index == 0
                        ? metadata.GetOrAddString("Contracts")
                        : default,
                    metadata.GetOrAddString(segments[index]),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
                if (!parent.IsNil)
                    metadata.AddNestedType(contract, parent);
                parent = contract;
            }
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddInterfaceImplementation(
                target,
                contract);

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"interfaceimpl-nested-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, Serialize(metadata));
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            var identity =
                new MetadataTypeIdentity.Named(
                    new MetadataNamedTypeIdentity(
                        new MetadataTypeScopeIdentity(
                            MetadataTypeScopeKind.CurrentModule,
                            mvid,
                            Text("nested-interface.dll"),
                            new MetadataAssemblyIdentity(
                                Text("NestedInterfaceFixture"),
                                new Version(1, 0, 0, 0),
                                Culture: null,
                                PublicKeyToken: null)),
                        Text("Contracts"),
                        [.. segments.Select(Text)],
                        [.. segments.Select(_ => 0)]),
                    IsValueType: false);
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    target),
                identity);
        }

        internal static AuthoredFixture CreateNestedTypeReference(
            int depth,
            bool corruptRootName)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(depth, 2);
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("nested-reference.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("NestedReferenceFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            EntityHandle scope =
                metadata.AddAssemblyReference(
                    metadata.GetOrAddString("External"),
                    new Version(1, 0, 0, 0),
                    default,
                    default,
                    (AssemblyFlags)0,
                    default);
            var segments = new string[depth];
            for (int index = 0; index < depth; index++)
            {
                segments[index] = $"Part{index}";
                scope = metadata.AddTypeReference(
                    scope,
                    index == 0
                        ? metadata.GetOrAddString("Contracts")
                        : default,
                    metadata.GetOrAddString(segments[index]));
            }
            metadata.AddInterfaceImplementation(
                target,
                scope);

            byte[] image = Serialize(metadata);
            if (corruptRootName)
            {
                using var probe =
                    new PEReader(new MemoryStream(image));
                MetadataReader probeReader =
                    probe.GetMetadataReader();
                int rowOffset =
                    probe.PEHeaders.MetadataStartOffset
                    + probeReader.GetTableMetadataOffset(
                        TableIndex.TypeRef);
                image[rowOffset + 2] = 0xfe;
                image[rowOffset + 3] = 0x7f;
            }

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"interfaceimpl-reference-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, image);
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            var identity =
                new MetadataTypeIdentity.Named(
                    new MetadataNamedTypeIdentity(
                        new MetadataTypeScopeIdentity(
                            MetadataTypeScopeKind.AssemblyReference,
                            Guid.Empty,
                            ModuleName: null,
                            Assembly: new MetadataAssemblyIdentity(
                                Text("External"),
                                new Version(1, 0, 0, 0),
                                Culture: null,
                                PublicKeyToken: null)),
                        Text("Contracts"),
                        [.. segments.Select(Text)],
                        [.. segments.Select(_ => 0)]),
                    IsValueType: false);
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    target),
                identity);
        }

        internal static AuthoredFixture CreateTypeReferenceScope(
            int moduleRow,
            bool cycle = false)
        {
            Guid mvid = Guid.NewGuid();
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                generation: 0,
                metadata.GetOrAddString("reference-scope.dll"),
                metadata.GetOrAddGuid(mvid),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString("ReferenceScopeFixture"),
                new Version(1, 0, 0, 0),
                default,
                default,
                (AssemblyFlags)0,
                AssemblyHashAlgorithm.None);
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddTypeDefinition(
                TypeAttributes.Interface
                    | TypeAttributes.Abstract
                    | TypeAttributes.Public,
                metadata.GetOrAddString("Contracts"),
                metadata.GetOrAddString("IContract"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle target =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Target"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            EntityHandle scope = cycle
                ? MetadataTokens.TypeReferenceHandle(1)
                : MetadataTokens.EntityHandle(moduleRow);
            TypeReferenceHandle reference =
                metadata.AddTypeReference(
                    scope,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IContract"));
            metadata.AddInterfaceImplementation(
                target,
                reference);

            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"interfaceimpl-reference-scope-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, Serialize(metadata));
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            var identity =
                new MetadataTypeIdentity.Named(
                    new MetadataNamedTypeIdentity(
                        new MetadataTypeScopeIdentity(
                            MetadataTypeScopeKind.CurrentModule,
                            mvid,
                            Text("reference-scope.dll"),
                            new MetadataAssemblyIdentity(
                                Text("ReferenceScopeFixture"),
                                new Version(1, 0, 0, 0),
                                Culture: null,
                                PublicKeyToken: null)),
                        Text("Contracts"),
                        [Text("IContract")],
                        [0]),
                    IsValueType: false);
            return new(
                path,
                MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    target),
                identity);
        }

        static BlobHandle AddBlob(
            MetadataBuilder metadata,
            params byte[] bytes)
        {
            var blob = new BlobBuilder();
            blob.WriteBytes(bytes);
            return metadata.GetOrAddBlob(blob);
        }

        static int EncodeTypeDefOrRef(EntityHandle handle)
        {
            int tag = handle.Kind switch
            {
                HandleKind.TypeDefinition => 0,
                HandleKind.TypeReference => 1,
                HandleKind.TypeSpecification => 2,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(handle)),
            };
            return checked(
                (MetadataTokens.GetRowNumber(handle) << 2) | tag);
        }

        static byte[] Serialize(MetadataBuilder metadata)
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(
                PEHeaderBuilder.CreateLibraryHeader(),
                new MetadataRootBuilder(
                    metadata,
                    suppressValidation: true),
                new BlobBuilder(),
                flags: CorFlags.ILOnly)
                .Serialize(image);
            return image.ToArray();
        }

        static InertString Text(string value) =>
            new(TextPolicy.Field, value);

        public void Dispose() => File.Delete(Path);
    }
}
