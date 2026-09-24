using System.Collections;
using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.ConsumerCanary;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeDeclarationEvidenceTests
{
    [Fact]
    public void RealInt32PostsExactNamedPrimitiveAndCategoryEvidence()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = FindType(
            reader,
            "System",
            "Int32");
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                handle);

        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(path, address));

        Assert.Equal(address, posted.Evidence.Type);
        Assert.Equal(
            reader.GetTypeDefinition(handle).Attributes,
            posted.Evidence.Attributes);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            posted.Evidence.Category);
        Assert.False(posted.Evidence.IsByRefLike);
        Assert.True(posted.Evidence.DefinesCoreLibraryRoot);
        Assert.Null(posted.Evidence.DeclaringType);
        Assert.Equal(
            "System",
            posted.Evidence.DefinitionIdentity.Namespace.ToString());
        Assert.Equal(
            "Int32",
            Assert.Single(
                posted.Evidence.DefinitionIdentity.Segments)
                .ToString());
        Assert.Equal(
            0,
            Assert.Single(
                posted.Evidence.DefinitionIdentity
                    .IntroducedGenericParameterCounts));
        var open = Assert.IsType<MetadataTypeIdentity.Named>(
            posted.Evidence.OpenSelfIdentity);
        Assert.True(open.IsValueType);
        Assert.Equal(
            posted.Evidence.DefinitionIdentity,
            open.Definition);
        Assert.Equal(
            "int",
            Assert.IsType<MetadataTypeIdentity.Primitive>(
                posted.Evidence.PrimitiveAlias)
                .Name.ToString());
        Assert.True(
            MetadataMethodImplementationConsumerCanary.Consume(
                MetadataMethodImplementationConsumerCanary.PostType(
                    path,
                    address)));
    }

    [Fact]
    public void CompilerProducedRefStructPostsModifierEvidence()
    {
        string path =
            typeof(TypeDeclarationRefStruct).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = FindType(
            reader,
            "ILInspector.Metadata.Tests",
            nameof(TypeDeclarationRefStruct));

        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(
                    path,
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        handle)));

        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            posted.Evidence.Category);
        Assert.True(posted.Evidence.IsByRefLike);
    }

    [Fact]
    public void CoreCategoryRootsRemainClasses()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();

        foreach (string name in
            new[]
            {
                "ValueType",
                "Enum",
                "Delegate",
                "MulticastDelegate",
            })
        {
            TypeDefinitionHandle handle = FindType(
                reader,
                "System",
                name);
            var posted = Assert.IsType<
                MetadataTypeDeclarationResult.Posted>(
                    Run(
                        path,
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            handle)));
            Assert.Equal(
                MetadataTypeDeclarationCategory.Class,
                posted.Evidence.Category);
        }
    }

    [Fact]
    public void CompilerProducedNestedGenericPostsExactOpenSelf()
    {
        string path =
            typeof(TypeDeclarationGenericOuter<>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle outer = FindType(
            reader,
            "ILInspector.Metadata.Tests",
            "TypeDeclarationGenericOuter`1");
        TypeDefinitionHandle inner = reader.GetTypeDefinition(outer)
            .GetNestedTypes()
            .Single(candidate =>
                reader.GetString(
                    reader.GetTypeDefinition(candidate).Name)
                == "Inner`1");
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                inner);

        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(path, address));

        Assert.Equal(
            ["TypeDeclarationGenericOuter`1", "Inner`1"],
            posted.Evidence.DefinitionIdentity.Segments
                .Select(segment => segment.ToString()));
        Assert.Equal(
            [1, 1],
            posted.Evidence.DefinitionIdentity
                .IntroducedGenericParameterCounts);
        var open = Assert.IsType<
            MetadataTypeIdentity.GenericInstance>(
                posted.Evidence.OpenSelfIdentity);
        Assert.Equal(2, open.Arguments.Length);
        Assert.Collection(
            open.Arguments,
            argument => Assert.Equal(
                new MetadataTypeIdentity.GenericParameter(false, 0),
                argument),
            argument => Assert.Equal(
                new MetadataTypeIdentity.GenericParameter(false, 1),
                argument));
        Assert.Equal(
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                outer),
            posted.Evidence.DeclaringType);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Class,
            posted.Evidence.Category);
    }

    [Fact]
    public void RecognizedCoreReferencesClassifyOrdinaryCategories()
    {
        using Fixture fixture = Fixture.CreateCategories();

        Assert.Equal(
            MetadataTypeDeclarationCategory.Class,
            Posted(fixture, "Class").Category);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Interface,
            Posted(fixture, "Interface").Category);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            Posted(fixture, "Struct").Category);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Enum,
            Posted(fixture, "Enum").Category);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Delegate,
            Posted(fixture, "Delegate").Category);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Class,
            Posted(fixture, "UnrecognizedValueType").Category);
    }

    [Fact]
    public void AssemblyReferenceCultureParticipatesInOperationBudget()
    {
        using Fixture baselineFixture =
            Fixture.CreateCategories();
        var baseline = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(
                    baselineFixture.Path,
                    baselineFixture["Struct"]));
        using Fixture culturedFixture =
            Fixture.CreateCategoriesWithCoreCulture(
                new string('c', 1000));

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(
                    culturedFixture.Path,
                    culturedFixture["Struct"],
                    new MetadataOperationPolicy(
                        long.MaxValue,
                        maxRetainedText:
                            baseline.Counters.RetainedText)));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RetainedText,
            rejected.Failure.BudgetDimension);
    }

    [Fact]
    public void SameSpelledNonCoreInt32HasNoPrimitiveAlias()
    {
        using Fixture fixture = Fixture.CreateSpoofInt32();

        MetadataTypeDeclarationEvidence evidence =
            Posted(fixture, "Int32");

        Assert.False(evidence.DefinesCoreLibraryRoot);
        Assert.Null(evidence.PrimitiveAlias);
        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            evidence.Category);
    }

    [Fact]
    public void DuplicateStructuredNameRejectsWithoutArtifactText()
    {
        using Fixture fixture = Fixture.CreateDuplicateName();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Duplicate"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.IdentityProjection,
            rejected.Failure.Stage);
        Assert.DoesNotContain(
            "ArtifactSecretName",
            rejected.Failure.Detail,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(MetadataTypeDeclarationResult.Rejected)
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
            property =>
                property.PropertyType
                    == typeof(MetadataTypeDeclarationEvidence));
    }

    [Fact]
    public void AmbiguousEnclosingNameRejectsUniqueNestedLeaf()
    {
        using Fixture fixture =
                Fixture.CreateAmbiguousEnclosingName();

        var rejected = Assert.IsType<
                MetadataTypeDeclarationResult.Rejected>(
                    Run(fixture.Path, fixture["Nested"]));

        Assert.Equal(
                MetadataTypeDeclarationFailureReason.MalformedMetadata,
                rejected.Failure.Reason);
        Assert.Equal(
                MetadataTypeDeclarationStage.IdentityProjection,
                rejected.Failure.Stage);
    }

    [Fact]
    public void DuplicateNestedClassRowRejectsAtomically()
    {
        using Fixture fixture =
            Fixture.CreateDuplicateNestedRelationship();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Nested"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.NestingValidation,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataTypeDeclarationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void MalformedTypeDefinitionListRejectsAtRowRead()
    {
        using Fixture fixture =
            Fixture.CreateMalformedFieldList();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Malformed"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.TypeDefinitionRead,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataTypeDeclarationMechanism.RowRead,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void TypeSpecificationClassBaseIsClassAndValueTypeIsUnsupported()
    {
        using Fixture fixture =
            Fixture.CreateTypeSpecificationBases();

        Assert.Equal(
            MetadataTypeDeclarationCategory.Class,
            Posted(fixture, "ClassSpec").Category);

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["ValueTypeSpec"]));
        Assert.Equal(
            MetadataTypeDeclarationFailureReason.UnsupportedShape,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void TypeSpecificationWithOutOfRangeRootRejects()
    {
        using Fixture fixture =
            Fixture.CreateOutOfRangeTypeSpecificationRoot();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["InvalidSpec"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void UnsortedGenericParameterOwnersRejectBeforeIdentityPublication()
    {
        using Fixture fixture =
            Fixture.CreateUnsortedGenericParameters();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Generic"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.IdentityProjection,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataTypeDeclarationMechanism.RelationshipTraversal,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void MalformedCoreRootCannotAuthenticatePrimitiveAlias()
    {
        using Fixture fixture =
            Fixture.CreateMalformedCoreRoot();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Int32"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataTypeDeclarationMechanism.CoreRootAuthentication,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void NestedRelationshipCannotAuthenticateTopLevelCoreRoot()
    {
        using Fixture fixture =
            Fixture.CreateNestedCoreRoot();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Int32"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
        Assert.Equal(
            MetadataTypeDeclarationMechanism.CoreRootAuthentication,
            rejected.Failure.Mechanism);
    }

    [Fact]
    public void ModuleScopedTypeReferenceAuthenticatesLocalCoreRoot()
    {
        using Fixture fixture =
            Fixture.CreateModuleScopedCoreBase();

        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            Posted(fixture, "Derived").Category);
    }

    [Fact]
    public void OutOfRangeTypeReferenceScopeRejects()
    {
        using Fixture fixture =
            Fixture.CreateOutOfRangeTypeReferenceScope();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Derived"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void NestedTypeReferenceWithInvalidTerminalScopeRejects()
    {
        using Fixture fixture =
            Fixture.CreateNestedTypeReferenceWithInvalidTerminal();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Derived"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void MissingModuleLocalNestedTypeReferenceRejects()
    {
        using Fixture fixture =
            Fixture.CreateMissingModuleLocalNestedReference();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Derived"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void TypeSpecificationAuthenticatesReferencedCoreRoot()
    {
        using Fixture fixture =
            Fixture.CreateTypeSpecificationCoreBase();

        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            Posted(fixture, "Derived").Category);
    }

    [Fact]
    public void MissingEnclosingRelationshipRejectsNestedLeaf()
    {
        using Fixture fixture =
            Fixture.CreateMissingEnclosingRelationship();

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Inner"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.NestingValidation,
            rejected.Failure.Stage);
    }

    [Theory]
    [InlineData(65534, 2)]
    [InlineData(65535, 2)]
    [InlineData(65536, 4)]
    public void SimpleTableIndexWidthUsesEcmaBoundary(
        int rows,
        int expected) =>
        Assert.Equal(
            expected,
            MetadataTypeDeclarationEvidenceOperation
                .IndexSize(rows));

    [Fact]
    public void InvalidAddressRejectsBeforeDeclarationScan()
    {
        using Fixture fixture = Fixture.CreateCategories();
        MetadataTypeDefinitionAddress valid = fixture["Class"];
        MetadataTypeDefinitionAddress invalid =
            MetadataTypeDefinitionAddress.FromToken(
                valid.ModuleVersionId,
                0x0200FFFF);

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, invalid));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.InvalidRequest,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.RequestValidation,
            rejected.Failure.Stage);
        Assert.Equal(0, rejected.Counters.DeclarationCandidates);
    }

    [Fact]
    public void OperationLimitsAcceptExactCostsAndRejectFirstExcess()
    {
        using Fixture fixture =
            Fixture.CreateTypeSpecificationBases();
        MetadataTypeDefinitionAddress address =
            fixture["ClassSpec"];
        var baseline = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(fixture.Path, address));
        MetadataOperationCounters counters =
            baseline.Counters;
        var exact = new MetadataOperationPolicy(
            long.MaxValue,
            maxDeclarationCandidates:
                counters.DeclarationCandidates,
            maxRelationshipEdges:
                counters.RelationshipEdges,
            maxSignatureBytes: counters.SignatureBytes,
            maxStructuredNodes: counters.StructuredNodes,
            maxRetainedText: counters.RetainedText);
        Assert.IsType<MetadataTypeDeclarationResult.Posted>(
            Run(fixture.Path, address, exact));

        foreach ((MetadataOperationDimension dimension, long limit)
            in new[]
            {
                (
                    MetadataOperationDimension
                        .DeclarationCandidates,
                    counters.DeclarationCandidates),
                (
                    MetadataOperationDimension
                        .RelationshipEdges,
                    counters.RelationshipEdges),
                (
                    MetadataOperationDimension.SignatureBytes,
                    counters.SignatureBytes),
                (
                    MetadataOperationDimension.StructuredNodes,
                    counters.StructuredNodes),
                (
                    MetadataOperationDimension.RetainedText,
                    counters.RetainedText),
            })
        {
            Assert.True(limit > 0);
            var rejected = Assert.IsType<
                MetadataTypeDeclarationResult.Rejected>(
                    Run(
                        fixture.Path,
                        address,
                        Limit(dimension, limit - 1)));
            Assert.Equal(
                MetadataTypeDeclarationFailureReason.BudgetExceeded,
                rejected.Failure.Reason);
            Assert.Equal(
                dimension,
                rejected.Failure.BudgetDimension);
        }
    }

    [Fact]
    public void CancellationIsObservedAtPublicationBoundary()
    {
        using Fixture fixture = Fixture.CreateCategories();
        RunPublicationCancellation(fixture);
    }

    static void RunPublicationCancellation(Fixture fixture)
    {
        using var cancellation = new CancellationTokenSource();
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            kind =>
            {
                if (kind
                    == MetadataOperationWorkKind
                        .TypeDeclarationPublication)
                {
                    cancellation.Cancel();
                }
            });
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        Assert.Throws<OperationCanceledException>(() =>
            declarations.PostTypeDeclaration(
                fixture["Class"],
                cancellation.Token));
    }

    [Fact]
    public void PostedEvidenceSurvivesAllAuthorityDisposal()
    {
        using Fixture fixture = Fixture.CreateCategories();
        MetadataTypeDeclarationResult.Posted posted;
        var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        var declarations =
            assembly.CreateDeclarationSession(operation);
        posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                declarations.PostTypeDeclaration(
                    fixture["Struct"],
                    TestContext.Current.CancellationToken));

        declarations.Dispose();
        operation.Dispose();
        assembly.Dispose();

        Assert.Equal(
            "Struct",
            Assert.Single(
                posted.Evidence.DefinitionIdentity.Segments)
                .ToString());
        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            posted.Evidence.Category);
    }

    [Fact]
    public void DisposedAuthoritiesRejectNewPosts()
    {
        using Fixture fixture = Fixture.CreateCategories();
        var assembly = AssemblyInspectionSession.Open(fixture.Path);
        var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        declarations.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            declarations.PostTypeDeclaration(
                fixture["Class"],
                TestContext.Current.CancellationToken));

        using var secondAssembly =
            AssemblyInspectionSession.Open(fixture.Path);
        var secondOperation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        MetadataDeclarationSession second =
            secondAssembly.CreateDeclarationSession(
                secondOperation);
        secondOperation.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            second.PostTypeDeclaration(
                fixture["Class"],
                TestContext.Current.CancellationToken));
        second.Dispose();
        secondAssembly.Dispose();

        var thirdAssembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var thirdOperation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        MetadataDeclarationSession third =
            thirdAssembly.CreateDeclarationSession(
                thirdOperation);
        thirdAssembly.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            third.PostTypeDeclaration(
                fixture["Class"],
                TestContext.Current.CancellationToken));
        third.Dispose();
        operation.Dispose();
        assembly.Dispose();
    }

    [Fact]
    public void ImageAdmissionFailureReturnsTypedRejection()
    {
        using Fixture fixture = Fixture.CreateCategories();
        using var stream = File.OpenRead(fixture.Path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        long rows = Enum.GetValues<TableIndex>()
            .Sum(table => (long)reader.GetTableRowCount(table));

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(
                    fixture.Path,
                    fixture["Class"],
                    new MetadataOperationPolicy(rows - 1)));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationMechanism.ImageAdmission,
            rejected.Failure.Mechanism);
        Assert.Equal(
            MetadataOperationDimension.MetadataRows,
            rejected.Failure.BudgetDimension);
    }

    [Fact]
    public void PublicResultGraphCarriesNoLiveAuthorityOrMutableCollection()
    {
        var pending = new Stack<Type>(
            new[]
            {
                typeof(MetadataTypeDeclarationResult),
                typeof(MetadataTypeDeclarationResult.Posted),
                typeof(MetadataTypeDeclarationResult.Rejected),
                typeof(MetadataTypeDeclarationEvidence),
                typeof(MetadataTypeDeclarationFailure),
            });
        var visited = new HashSet<Type>();

        while (pending.TryPop(out Type? type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!visited.Add(type))
                continue;

            Assert.False(typeof(Stream).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(Delegate).IsAssignableFrom(type), type.FullName);
            Assert.False(
                type == typeof(MetadataReader)
                    || type == typeof(PEReader),
                type.FullName);
            Assert.False(
                !IsImmutableArray(type)
                    && (typeof(IList).IsAssignableFrom(type)
                        || ImplementsMutableCollection(type)),
                type.FullName);

            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }
            if (type.IsGenericType)
            {
                foreach (Type argument
                    in type.GetGenericArguments())
                {
                    pending.Push(argument);
                }
            }
            if (type.Namespace == "ILInspector.Metadata")
            {
                foreach (PropertyInfo property
                    in type.GetProperties(
                        BindingFlags.Instance
                        | BindingFlags.Public))
                {
                    pending.Push(property.PropertyType);
                }
            }
        }
    }

    static bool ImplementsMutableCollection(Type type) =>
        type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType
            && candidate.GetGenericTypeDefinition()
                is { } definition
            && (definition == typeof(ICollection<>)
                || definition == typeof(IList<>)));

    static bool IsImmutableArray(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition()
            == typeof(ImmutableArray<>);

    static MetadataOperationPolicy Limit(
        MetadataOperationDimension dimension,
        long limit) =>
        new(
            long.MaxValue,
            maxDeclarationCandidates:
                dimension
                    == MetadataOperationDimension
                        .DeclarationCandidates
                    ? limit
                    : long.MaxValue,
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
                    : long.MaxValue);

    static MetadataTypeDeclarationEvidence Posted(
        Fixture fixture,
        string name) =>
        Assert.IsType<MetadataTypeDeclarationResult.Posted>(
            Run(fixture.Path, fixture[name]))
            .Evidence;

    static MetadataTypeDeclarationResult Run(
        string path,
        MetadataTypeDefinitionAddress address,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.PostTypeDeclaration(
            address,
            TestContext.Current.CancellationToken);
    }

    static TypeDefinitionHandle FindType(
        MetadataReader reader,
        string @namespace,
        string name) =>
        reader.TypeDefinitions.Single(handle =>
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            return reader.StringComparer.Equals(
                    definition.Namespace,
                    @namespace)
                && reader.StringComparer.Equals(
                    definition.Name,
                    name);
        });

    sealed class Fixture : IDisposable
    {
        readonly IReadOnlyDictionary<
            string,
            MetadataTypeDefinitionAddress> _types;

        Fixture(
            string path,
            IReadOnlyDictionary<
                string,
                MetadataTypeDefinitionAddress> types)
        {
            Path = path;
            _types = types;
        }

        internal string Path { get; }

        internal MetadataTypeDefinitionAddress this[string name] =>
            _types[name];

        internal static Fixture CreateCategories() =>
            CreateCategories(coreCulture: null);

        internal static Fixture CreateCategoriesWithCoreCulture(
            string culture) =>
            CreateCategories(culture);

        static Fixture CreateCategories(string? coreCulture)
        {
            var metadata = CreateBuilder(
                "type-categories.dll",
                "TypeCategories");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true,
                culture: coreCulture);
            AssemblyReferenceHandle fake = AddAssemblyReference(
                metadata,
                "Fake.Core",
                recognized: false);
            TypeReferenceHandle objectType =
                AddTypeReference(metadata, core, "Object");
            TypeReferenceHandle valueType =
                AddTypeReference(metadata, core, "ValueType");
            TypeReferenceHandle enumType =
                AddTypeReference(metadata, core, "Enum");
            TypeReferenceHandle multicastDelegate =
                AddTypeReference(
                    metadata,
                    core,
                    "MulticastDelegate");
            TypeReferenceHandle fakeValueType =
                AddTypeReference(
                    metadata,
                    fake,
                    "ValueType");

            AddModuleType(metadata);
            var handles =
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Class"] = AddType(
                        metadata,
                        "Class",
                        TypeAttributes.Public,
                        objectType),
                    ["Interface"] = AddType(
                        metadata,
                        "Interface",
                        TypeAttributes.Public
                            | TypeAttributes.Interface
                            | TypeAttributes.Abstract,
                        default),
                    ["Struct"] = AddType(
                        metadata,
                        "Struct",
                        TypeAttributes.Public
                            | TypeAttributes.Sealed,
                        valueType),
                    ["Enum"] = AddType(
                        metadata,
                        "Enum",
                        TypeAttributes.Public
                            | TypeAttributes.Sealed,
                        enumType),
                    ["Delegate"] = AddType(
                        metadata,
                        "Delegate",
                        TypeAttributes.Public
                            | TypeAttributes.Sealed,
                        multicastDelegate),
                    ["UnrecognizedValueType"] = AddType(
                        metadata,
                        "UnrecognizedValueType",
                        TypeAttributes.Public
                            | TypeAttributes.Sealed,
                        fakeValueType),
                };
            return Write(metadata, handles);
        }

        internal static Fixture CreateSpoofInt32()
        {
            var metadata = CreateBuilder(
                "spoof-int32.dll",
                "SpoofInt32");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle valueType =
                AddTypeReference(metadata, core, "ValueType");
            AddModuleType(metadata);
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Sealed,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Int32"),
                valueType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Int32"] = type,
                });
        }

        internal static Fixture CreateDuplicateName()
        {
            var metadata = CreateBuilder(
                "duplicate-name.dll",
                "DuplicateName");
            AddModuleType(metadata);
            TypeDefinitionHandle first = AddType(
                metadata,
                "ArtifactSecretName",
                TypeAttributes.Public,
                default);
            _ = AddType(
                metadata,
                "ArtifactSecretName",
                TypeAttributes.Public,
                default);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Duplicate"] = first,
                });
        }

        internal static Fixture CreateDuplicateNestedRelationship()
        {
            var metadata = CreateBuilder(
                "duplicate-nesting.dll",
                "DuplicateNesting");
            AddModuleType(metadata);
            TypeDefinitionHandle parent = AddType(
                metadata,
                "Parent",
                TypeAttributes.Public,
                default);
            TypeDefinitionHandle nested = AddType(
                metadata,
                "Nested",
                TypeAttributes.NestedPublic,
                default);
            metadata.AddNestedType(nested, parent);
            metadata.AddNestedType(nested, parent);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Nested"] = nested,
                });
        }

        internal static Fixture CreateAmbiguousEnclosingName()
        {
            var metadata = CreateBuilder(
                "ambiguous-enclosing.dll",
                "AmbiguousEnclosing");
            AddModuleType(metadata);
            TypeDefinitionHandle firstParent = AddType(
                metadata,
                "Parent",
                TypeAttributes.Public,
                default);
            _ = AddType(
                metadata,
                "Parent",
                TypeAttributes.Public,
                default);
            TypeDefinitionHandle nested = AddType(
                metadata,
                "UniqueLeaf",
                TypeAttributes.NestedPublic,
                default);
            metadata.AddNestedType(nested, firstParent);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Nested"] = nested,
                });
        }

        internal static Fixture CreateMalformedFieldList()
        {
            var metadata = CreateBuilder(
                "malformed-type-row.dll",
                "MalformedTypeRow");
            AddModuleType(metadata);
            TypeDefinitionHandle malformed = AddType(
                metadata,
                "Malformed",
                TypeAttributes.Public,
                default);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Malformed"] = malformed,
                },
                (image, pe, reader) =>
                {
                    int rowOffset =
                        pe.PEHeaders.MetadataStartOffset
                        + reader.GetTableMetadataOffset(
                            TableIndex.TypeDef)
                        + ((MetadataTokens.GetRowNumber(malformed)
                                - 1)
                            * reader.GetTableRowSize(
                                TableIndex.TypeDef));
                    int fieldListOffset =
                        rowOffset
                        + reader.GetTableRowSize(
                            TableIndex.TypeDef)
                        - (2 * sizeof(ushort));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        image.AsSpan(
                            fieldListOffset,
                            sizeof(ushort)),
                        ushort.MaxValue);
                });
        }

        internal static Fixture CreateTypeSpecificationBases()
        {
            var metadata = CreateBuilder(
                "type-spec-bases.dll",
                "TypeSpecBases");
            AssemblyReferenceHandle reference =
                AddAssemblyReference(
                    metadata,
                    "GenericBases",
                    recognized: false);
            TypeReferenceHandle genericBase =
                metadata.AddTypeReference(
                    reference,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("GenericBase`1"));
            AddModuleType(metadata);
            TypeSpecificationHandle classSpec =
                AddGenericInstantiation(
                    metadata,
                    genericBase,
                    rawTypeKind: 0x12);
            TypeSpecificationHandle valueTypeSpec =
                AddGenericInstantiation(
                    metadata,
                    genericBase,
                    rawTypeKind: 0x11);
            TypeDefinitionHandle classType = AddType(
                metadata,
                "ClassSpec",
                TypeAttributes.Public,
                classSpec);
            TypeDefinitionHandle valueType = AddType(
                metadata,
                "ValueTypeSpec",
                TypeAttributes.Public,
                valueTypeSpec);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["ClassSpec"] = classType,
                    ["ValueTypeSpec"] = valueType,
                });
        }

        internal static Fixture
            CreateOutOfRangeTypeSpecificationRoot()
        {
            var metadata = CreateBuilder(
                "invalid-type-spec-root.dll",
                "InvalidTypeSpecRoot");
            AssemblyReferenceHandle reference =
                AddAssemblyReference(
                    metadata,
                    "GenericBases",
                    recognized: false);
            _ = metadata.AddTypeReference(
                reference,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString("OnlyType"));
            AddModuleType(metadata);
            TypeSpecificationHandle invalidSpec =
                AddGenericInstantiation(
                    metadata,
                    typeReferenceRow: 999,
                    rawTypeKind: 0x12);
            TypeDefinitionHandle type = AddType(
                metadata,
                "InvalidSpec",
                TypeAttributes.Public,
                invalidSpec);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["InvalidSpec"] = type,
                });
        }

        internal static Fixture CreateUnsortedGenericParameters()
        {
            var metadata = CreateBuilder(
                "unsorted-generics.dll",
                "UnsortedGenerics");
            AddModuleType(metadata);
            TypeDefinitionHandle first = AddType(
                metadata,
                "First`1",
                TypeAttributes.Public,
                default);
            TypeDefinitionHandle second = AddType(
                metadata,
                "Second`1",
                TypeAttributes.Public,
                default);
            metadata.AddGenericParameter(
                second,
                default,
                metadata.GetOrAddString("U"),
                0);
            metadata.AddGenericParameter(
                first,
                default,
                metadata.GetOrAddString("T"),
                0);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Generic"] = first,
                });
        }

        internal static Fixture CreateMalformedCoreRoot()
        {
            var metadata = CreateBuilder(
                "malformed-core-root.dll",
                "MalformedCoreRoot");
            AddModuleType(metadata);
            TypeDefinitionHandle objectType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Object"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle valueType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Abstract,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("ValueType"),
                    objectType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle int32 =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Sealed,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Int32"),
                    valueType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Int32"] = int32,
                },
                (image, pe, reader) =>
                {
                    int rowOffset =
                        pe.PEHeaders.MetadataStartOffset
                        + reader.GetTableMetadataOffset(
                            TableIndex.TypeDef)
                        + ((MetadataTokens.GetRowNumber(objectType)
                                - 1)
                            * reader.GetTableRowSize(
                                TableIndex.TypeDef));
                    int extendsOffset =
                        rowOffset
                        + sizeof(uint)
                        + (2 * sizeof(ushort));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        image.AsSpan(
                            extendsOffset,
                            sizeof(ushort)),
                        1);
                });
        }

        internal static Fixture CreateNestedCoreRoot()
        {
            var metadata = CreateBuilder(
                "nested-core-root.dll",
                "NestedCoreRoot");
            AddModuleType(metadata);
            TypeDefinitionHandle container = AddType(
                metadata,
                "Container",
                TypeAttributes.Public,
                default);
            TypeDefinitionHandle objectType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Object"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(objectType, container);
            TypeDefinitionHandle valueType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Abstract,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("ValueType"),
                    objectType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            TypeDefinitionHandle int32 =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public
                        | TypeAttributes.Sealed,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Int32"),
                    valueType,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Int32"] = int32,
                });
        }

        internal static Fixture CreateModuleScopedCoreBase()
        {
            var metadata = CreateBuilder(
                "module-core-base.dll",
                "ModuleCoreBase");
            AddModuleType(metadata);
            TypeDefinitionHandle objectType =
                metadata.AddTypeDefinition(
                    TypeAttributes.Public,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("Object"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    MetadataTokens.MethodDefinitionHandle(1));
            _ = metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("ValueType"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            TypeReferenceHandle valueTypeReference =
                metadata.AddTypeReference(
                    MetadataTokens.EntityHandle(
                        0x00000001),
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("ValueType"));
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public
                    | TypeAttributes.Sealed,
                valueTypeReference);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                });
        }

        internal static Fixture
            CreateOutOfRangeTypeReferenceScope()
        {
            var metadata = CreateBuilder(
                "invalid-type-ref-scope.dll",
                "InvalidTypeRefScope");
            AddModuleType(metadata);
            TypeReferenceHandle invalidBase =
                metadata.AddTypeReference(
                    MetadataTokens.TypeReferenceHandle(999),
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString("ValueType"));
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public,
                invalidBase);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                });
        }

        internal static Fixture
            CreateNestedTypeReferenceWithInvalidTerminal()
        {
            var metadata = CreateBuilder(
                "invalid-nested-type-ref-scope.dll",
                "InvalidNestedTypeRefScope");
            AddModuleType(metadata);
            TypeReferenceHandle outer =
                metadata.AddTypeReference(
                    MetadataTokens.AssemblyReferenceHandle(
                        999),
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("Outer"));
            TypeReferenceHandle inner =
                metadata.AddTypeReference(
                    outer,
                    default,
                    metadata.GetOrAddString("Inner"));
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public,
                inner);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                });
        }

        internal static Fixture
            CreateMissingModuleLocalNestedReference()
        {
            var metadata = CreateBuilder(
                "missing-local-nested-ref.dll",
                "MissingLocalNestedRef");
            AddModuleType(metadata);
            TypeReferenceHandle outer =
                metadata.AddTypeReference(
                    MetadataTokens.EntityHandle(
                        0x00000001),
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("MissingOuter"));
            TypeReferenceHandle inner =
                metadata.AddTypeReference(
                    outer,
                    default,
                    metadata.GetOrAddString("MissingInner"));
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public,
                inner);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                });
        }

        internal static Fixture
            CreateTypeSpecificationCoreBase()
        {
            var metadata = CreateBuilder(
                "type-spec-core-base.dll",
                "TypeSpecCoreBase");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle valueType =
                AddTypeReference(
                    metadata,
                    core,
                    "ValueType");
            AddModuleType(metadata);
            TypeSpecificationHandle specification =
                AddGenericInstantiation(
                    metadata,
                    valueType,
                    rawTypeKind: 0x12);
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public
                    | TypeAttributes.Sealed,
                specification);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                });
        }

        internal static Fixture
            CreateMissingEnclosingRelationship()
        {
            var metadata = CreateBuilder(
                "missing-enclosing.dll",
                "MissingEnclosing");
            AddModuleType(metadata);
            TypeDefinitionHandle outer = AddType(
                metadata,
                "Outer",
                TypeAttributes.NestedPublic,
                default);
            TypeDefinitionHandle inner = AddType(
                metadata,
                "Inner",
                TypeAttributes.NestedPublic,
                default);
            metadata.AddNestedType(inner, outer);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Inner"] = inner,
                });
        }

        static MetadataBuilder CreateBuilder(
            string moduleName,
            string assemblyName)
        {
            var metadata = new MetadataBuilder();
            metadata.AddModule(
                0,
                metadata.GetOrAddString(moduleName),
                metadata.GetOrAddGuid(Guid.NewGuid()),
                default,
                default);
            metadata.AddAssembly(
                metadata.GetOrAddString(assemblyName),
                new Version(1, 0),
                default,
                default,
                default,
                AssemblyHashAlgorithm.None);
            return metadata;
        }

        static void AddModuleType(MetadataBuilder metadata) =>
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                default,
                metadata.GetOrAddString("<Module>"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        static TypeDefinitionHandle AddType(
            MetadataBuilder metadata,
            string name,
            TypeAttributes attributes,
            EntityHandle baseType) =>
            metadata.AddTypeDefinition(
                attributes,
                metadata.GetOrAddString("Samples"),
                metadata.GetOrAddString(name),
                baseType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));

        static AssemblyReferenceHandle AddAssemblyReference(
            MetadataBuilder metadata,
            string name,
            bool recognized,
            string? culture = null) =>
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(name),
                new Version(11, 0),
                culture is null
                    ? default
                    : metadata.GetOrAddString(culture),
                recognized
                    ? metadata.GetOrAddBlob(
                        new byte[]
                        {
                            0xb0,
                            0x3f,
                            0x5f,
                            0x7f,
                            0x11,
                            0xd5,
                            0x0a,
                            0x3a,
                        })
                    : default,
                default,
                default);

        static TypeReferenceHandle AddTypeReference(
            MetadataBuilder metadata,
            AssemblyReferenceHandle scope,
            string name) =>
            metadata.AddTypeReference(
                scope,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString(name));

        static TypeSpecificationHandle AddGenericInstantiation(
            MetadataBuilder metadata,
            TypeReferenceHandle definition,
            byte rawTypeKind)
            => AddGenericInstantiation(
                metadata,
                MetadataTokens.GetRowNumber(definition),
                rawTypeKind);

        static TypeSpecificationHandle AddGenericInstantiation(
            MetadataBuilder metadata,
            int typeReferenceRow,
            byte rawTypeKind)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x15);
            signature.WriteByte(rawTypeKind);
            signature.WriteCompressedInteger(
                (typeReferenceRow << 2)
                | 1);
            signature.WriteCompressedInteger(1);
            signature.WriteByte(0x08);
            return metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(signature));
        }

        static Fixture Write(
            MetadataBuilder metadata,
            IReadOnlyDictionary<
                string,
                TypeDefinitionHandle> handles,
            Action<byte[], PEReader, MetadataReader>? patch = null)
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
            byte[] imageBytes = image.ToArray();
            if (patch is not null)
            {
                using var patchPe = new PEReader(
                    new MemoryStream(
                        imageBytes,
                        writable: false));
                patch(
                    imageBytes,
                    patchPe,
                    patchPe.GetMetadataReader());
            }
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"type-post-{Guid.NewGuid():N}.dll");
            File.WriteAllBytes(path, imageBytes);

            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            MetadataReader reader = pe.GetMetadataReader();
            return new Fixture(
                path,
                handles.ToDictionary(
                    pair => pair.Key,
                    pair => MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        pair.Value)));
        }

        public void Dispose() => File.Delete(Path);
    }
}

public sealed class TypeDeclarationGenericOuter<T>
{
    public sealed class Inner<U>
    {
    }
}

public ref struct TypeDeclarationRefStruct
{
}
