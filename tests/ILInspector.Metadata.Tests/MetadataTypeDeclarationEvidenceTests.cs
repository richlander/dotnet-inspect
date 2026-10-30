using System.Collections;
using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata.ConsumerCanary;
using InertText;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeDeclarationEvidenceTests
{
    [Fact]
    public void CompleteSignatureIsCachedSeparatelyFromDeclaration()
    {
        string path =
            typeof(TypeSignatureSubject<,>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    "TypeSignatureSubject`2"));
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        MetadataTypeDeclarationResult declaration =
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken);
        MetadataOperationCounters declarationCounters =
            operation.Counters;
        MetadataTypeSignatureResult signature =
            declarations.PostTypeSignature(
                address,
                TestContext.Current.CancellationToken);
        MetadataOperationCounters signatureCounters =
            operation.Counters;

        var declarationPosted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                declaration);
        var signaturePosted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                signature);
        Assert.Same(
            declarationPosted.Evidence,
            signaturePosted.Evidence.Declaration);
        Assert.NotEqual(
            declarationCounters,
            signatureCounters);
        Assert.Same(
            declaration,
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken));
        Assert.Equal(signatureCounters, operation.Counters);
        Assert.Same(
            signature,
            declarations.PostTypeSignature(
                address,
                TestContext.Current.CancellationToken));
        Assert.Equal(signatureCounters, operation.Counters);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => declarations.PostTypeSignature(
                address,
                cancellation.Token));
    }

    [Fact]
    public void RepeatedSignatureRejectionIsCachedWithoutAdditionalWork()
    {
        using Fixture fixture =
            Fixture.CreateMalformedConstraintType();
        using var assembly =
            AssemblyInspectionSession.Open(fixture.Path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        MetadataTypeSignatureResult first =
            declarations.PostTypeSignature(
                fixture["Generic"],
                TestContext.Current.CancellationToken);
        MetadataOperationCounters counters = operation.Counters;
        MetadataTypeSignatureResult second =
            declarations.PostTypeSignature(
                fixture["Generic"],
                TestContext.Current.CancellationToken);

        Assert.IsType<MetadataTypeSignatureResult.Rejected>(
            first);
        Assert.Same(first, second);
        Assert.Equal(counters, operation.Counters);
    }

    [Fact]
    public void CompleteSignatureChargesEveryOwnedWorkDimension()
    {
        string path =
            typeof(TypeSignatureSubject<,>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    "TypeSignatureSubject`2"));
        var baseline = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(path, address));
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
        Assert.IsType<MetadataTypeSignatureResult.Posted>(
            RunSignature(path, address, exact));

        foreach ((MetadataOperationDimension dimension, long limit)
            in new[]
            {
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
                MetadataTypeSignatureResult.Rejected>(
                    RunSignature(
                        path,
                        address,
                        Limit(dimension, limit - 1)));
            Assert.Equal(
                MetadataTypeDeclarationFailureReason
                    .BudgetExceeded,
                rejected.Failure.Reason);
            Assert.Equal(
                dimension,
                rejected.Failure.BudgetDimension);
        }
    }

    [Fact]
    public void CompleteSignaturePostsBaseConstraintsAndImmediateNestedTypes()
    {
        string path =
            typeof(TypeSignatureSubject<,>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    "TypeSignatureSubject`2"));

        MetadataTypeSignatureResult result =
            RunSignature(path, address);
        var posted =
            Assert.IsType<MetadataTypeSignatureResult.Posted>(
                result);
        MetadataTypeSignatureEvidence evidence =
            posted.Evidence;

        Assert.Equal(3, evidence.NestedTypeCount);
        Assert.NotNull(evidence.BaseType);
        var baseInstance = Assert.IsType<
            MetadataTypeSignatureType.GenericInstance>(
                evidence.BaseType.Type);
        Assert.True(
            baseInstance.Definition.Name.Namespace
                .ValueTextEquals(
                    "ILInspector.Metadata.Tests"u8));
        Assert.True(
            Assert.Single(
                baseInstance.Definition.Name.Segments)
                .ValueTextEquals("TypeSignatureBase`1"u8));
        var baseArray = Assert.IsType<
            MetadataTypeSignatureType.SzArray>(
                Assert.Single(baseInstance.Arguments));
        Assert.Equal(
            0,
            Assert.IsType<
                MetadataTypeSignatureType.TypeParameter>(
                    baseArray.Element)
                .Index);

        Assert.Equal(2, evidence.GenericParameters.Length);
        Assert.Empty(
            evidence.GenericParameters[0].TypeConstraints);
        ImmutableArray<
            MetadataTypeSignatureConstraintEvidence>
            constraints = evidence.GenericParameters[1]
                .TypeConstraints;
        Assert.Equal(4, constraints.Length);
        Assert.True(
            NamedDefinition(constraints[0].Type)
                .Segments[^1]
                .ValueTextEquals(
                    "TypeSignatureConstraintMarker"u8));
        Assert.True(
            NamedDefinition(constraints[1].Type)
                .Segments[^1]
                .ValueTextEquals("IComparable`1"u8));
        Assert.True(
            NamedDefinition(constraints[2].Type)
                .Segments[^1]
                .ValueTextEquals("IEnumerable`1"u8));
        Assert.True(
            NamedDefinition(constraints[3].Type)
                .Segments[^1]
                .ValueTextEquals("IDisposable"u8));
        Assert.All(
            constraints,
            constraint => Assert.Equal(
                (byte)1,
                constraint.NullableFlag));
    }

    [Fact]
    public void CompleteSignatureNamesRemainDetachedAfterSessionDisposal()
    {
        string path =
            typeof(TypeSignatureSubject<,>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    "TypeSignatureSubject`2"));

        MetadataTypeSignatureResult result =
            RunSignature(path, address);
        var posted =
            Assert.IsType<MetadataTypeSignatureResult.Posted>(
                result);
        Assert.NotNull(posted.Evidence.BaseType);
        var baseInstance = Assert.IsType<
            MetadataTypeSignatureType.GenericInstance>(
                posted.Evidence.BaseType.Type);

        Assert.True(
            baseInstance.Definition.Name.Namespace
                .ValueTextEquals(
                    "ILInspector.Metadata.Tests"u8));
        Assert.True(
            Assert.Single(
                baseInstance.Definition.Name.Segments)
                .ValueTextEquals("TypeSignatureBase`1"u8));
    }

    [Fact]
    public void TypeSpecificationBasePostsByteBackedGenericShape()
    {
        using Fixture fixture =
            Fixture.CreateTypeSpecificationBases();

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(
                    fixture.Path,
                    fixture["ClassSpec"]));
        Assert.NotNull(posted.Evidence.BaseType);
        var generic = Assert.IsType<
            MetadataTypeSignatureType.GenericInstance>(
                posted.Evidence.BaseType.Type);

        Assert.True(
            generic.Definition.Name.Namespace
                .ValueTextEquals("Samples"u8));
        Assert.Equal(
            MetadataTypeSignatureNamedKind.Class,
            generic.Definition.Kind);
        Assert.True(
            Assert.Single(generic.Definition.Name.Segments)
                .ValueTextEquals("GenericBase`1"u8));
        Assert.Equal(
            PrimitiveTypeCode.Int32,
            Assert.IsType<
                MetadataTypeSignatureType.Primitive>(
                    Assert.Single(generic.Arguments))
                .Code);
        Assert.Equal(0, posted.Evidence.NestedTypeCount);
    }

    [Theory]
    [InlineData(
        typeof(TypeSignatureConstraintMarker),
        "System",
        "Object")]
    [InlineData(
        typeof(TypeSignatureLocalDerived),
        "ILInspector.Metadata.Tests",
        "TypeSignatureConstraintMarker")]
    [InlineData(typeof(int), "System", "ValueType")]
    public void CompleteSignaturePostsDirectNamedBase(
        Type type,
        string expectedNamespace,
        string expectedName)
    {
        string path = type.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    type.Namespace!,
                    type.Name));

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(path, address));
        Assert.NotNull(posted.Evidence.BaseType);
        var named = Assert.IsType<
            MetadataTypeSignatureType.Named>(
                posted.Evidence.BaseType.Type);

        Assert.Equal(
            MetadataTypeSignatureNamedKind.Unspecified,
            named.Kind);
        Assert.True(
            named.Name.Namespace.ValueTextEquals(
                System.Text.Encoding.UTF8.GetBytes(
                    expectedNamespace)));
        Assert.True(
            Assert.Single(named.Name.Segments)
                .ValueTextEquals(
                    System.Text.Encoding.UTF8.GetBytes(
                        expectedName)));
    }

    [Fact]
    public void DirectBaseTypeNameKeepsLiteralBacktickBytes()
    {
        using Fixture fixture =
            Fixture.CreateLiteralBacktickTypeNames();

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(
                    fixture.Path,
                    fixture["Derived"]));
        Assert.NotNull(posted.Evidence.BaseType);
        var named = Assert.IsType<
            MetadataTypeSignatureType.Named>(
                posted.Evidence.BaseType.Type);

        Assert.True(
            Assert.Single(named.Name.Segments)
                .ValueTextEquals("Base`Literal"u8));
        Assert.Equal(
            [0],
            named.Name.IntroducedGenericParameterCounts);
    }

    [Fact]
    public void DirectConstraintTypeNameKeepsLiteralBacktickBytes()
    {
        using Fixture fixture =
            Fixture.CreateLiteralBacktickTypeNames();

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(
                    fixture.Path,
                    fixture["Generic"]));
        MetadataTypeSignatureConstraintEvidence constraint =
            Assert.Single(
                Assert.Single(posted.Evidence.GenericParameters)
                    .TypeConstraints);
        var named = Assert.IsType<
            MetadataTypeSignatureType.Named>(
                constraint.Type);

        Assert.True(
            Assert.Single(named.Name.Segments)
                .ValueTextEquals("Constraint`Literal"u8));
        Assert.Equal(
            [0],
            named.Name.IntroducedGenericParameterCounts);
    }

    [Fact]
    public void CanonicalTypeDefArityMustMatchGenericParameters()
    {
        using Fixture fixture =
            Fixture.CreateContradictoryTypeNameArity();

        var rejected = Assert.IsType<
            MetadataTypeSignatureResult.Rejected>(
                RunSignature(
                    fixture.Path,
                    fixture["Derived"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage
                .SignatureBaseProjection,
            rejected.Failure.Stage);
    }

    [Fact]
    public void CompleteSignatureRejectsUnrenderableArrayRank()
    {
        using Fixture fixture =
            Fixture.CreateUnrenderableArrayConstraint();

        var rejected = Assert.IsType<
            MetadataTypeSignatureResult.Rejected>(
                RunSignature(
                    fixture.Path,
                    fixture["Generic"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage
                .SignatureConstraintProjection,
            rejected.Failure.Stage);
    }

    [Fact]
    public void CompleteSignaturePostsNoBaseForSystemObject()
    {
        string path = typeof(object).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(reader, "System", "Object"));

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(path, address));

        Assert.Null(posted.Evidence.BaseType);
        Assert.Empty(posted.Evidence.GenericParameters);
    }

    [Fact]
    public void CompleteSignaturePostsBaseDynamicTransformFlags()
    {
        string path =
            typeof(TypeSignatureDynamicSubject).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    nameof(TypeSignatureDynamicSubject)));

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(path, address));
        Assert.NotNull(posted.Evidence.BaseType);

        Assert.Equal(
            [false, true],
            posted.Evidence.BaseType.DynamicFlags.Select(
                flag => flag != 0));
    }

    [Fact]
    public void CompleteSignaturePostsConstraintNullableFlag()
    {
        string path =
            typeof(TypeSignatureNullableConstraint<>)
                .Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    "TypeSignatureNullableConstraint`1"));

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(path, address));
        MetadataTypeSignatureConstraintEvidence constraint =
            Assert.Single(
                Assert.Single(posted.Evidence.GenericParameters)
                    .TypeConstraints);

        Assert.Equal((byte)2, constraint.NullableFlag);
    }

    [Fact]
    public void DeclarationDoesNotDecodeMalformedConstraintType()
    {
        using Fixture fixture =
            Fixture.CreateMalformedConstraintType();

        var declaration = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(fixture.Path, fixture["Generic"]));
        Assert.Equal(
            1,
            Assert.Single(
                declaration.Evidence.Signature
                    .GenericParameters)
                .ConstraintCount);

        var rejected = Assert.IsType<
            MetadataTypeSignatureResult.Rejected>(
                RunSignature(
                    fixture.Path,
                    fixture["Generic"]));
        Assert.Equal(
            MetadataTypeDeclarationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage
                .SignatureConstraintProjection,
            rejected.Failure.Stage);
    }

    [Fact]
    public void OuterConstraintCannotReferenceNestedTypeParameter()
    {
        using Fixture fixture =
            Fixture.CreateOuterConstraintWithNestedParameter();

        var declaration = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(fixture.Path, fixture["Inner"]));
        Assert.Equal(
            2,
            declaration.Evidence.Signature
                .GenericParameters.Length);

        var rejected = Assert.IsType<
            MetadataTypeSignatureResult.Rejected>(
                RunSignature(
                    fixture.Path,
                    fixture["Inner"]));
        Assert.Equal(
            MetadataTypeDeclarationFailureReason
                .MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage
                .SignatureConstraintProjection,
            rejected.Failure.Stage);
    }

    [Fact]
    public void ConstraintTypeNameKeepsIllFormedUtf8Bytes()
    {
        using Fixture fixture =
            Fixture.CreateIllFormedConstraintTypeName();

        var posted = Assert.IsType<
            MetadataTypeSignatureResult.Posted>(
                RunSignature(
                    fixture.Path,
                    fixture["Generic"]));
        MetadataTypeSignatureConstraintEvidence constraint =
            Assert.Single(
                Assert.Single(posted.Evidence.GenericParameters)
                    .TypeConstraints);
        var named = Assert.IsType<
            MetadataTypeSignatureType.Named>(
                constraint.Type);
        byte[] expected =
            System.Text.Encoding.UTF8.GetBytes(
                "QQConstraint");
        expected[0] = 0xff;
        expected[1] = 0xfe;

        Assert.True(
            Assert.Single(named.Name.Segments)
                .ValueTextEquals(expected));
    }

    [Fact]
    public void RepeatedExactRequestReusesPostedResultWithoutAdditionalWork()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(reader, "System", "Int32"));
        MetadataTypeDefinitionAddress otherAddress =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(reader, "System", "Int64"));
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        MetadataTypeDeclarationResult first =
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken);
        MetadataOperationCounters counters = operation.Counters;
        MetadataTypeDeclarationResult second =
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken);

        Assert.IsType<MetadataTypeDeclarationResult.Posted>(first);
        Assert.Same(first, second);
        Assert.Equal(counters, operation.Counters);

        MetadataTypeDeclarationResult other =
            declarations.PostTypeDeclaration(
                otherAddress,
                TestContext.Current.CancellationToken);
        Assert.NotSame(first, other);
        MetadataOperationCounters countersAfterOther =
            operation.Counters;
        Assert.NotEqual(counters, countersAfterOther);
        Assert.Same(
            first,
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken));
        Assert.Equal(countersAfterOther, operation.Counters);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => declarations.PostTypeDeclaration(
                address,
                cancellation.Token));
    }

    [Fact]
    public void RepeatedExactRequestReusesRejectedResultWithoutAdditionalWork()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(reader, "System", "Int32"));
        MetadataTypeDefinitionAddress otherAddress =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(reader, "System", "Int64"));
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            new MetadataOperationPolicy(maxMetadataRows: 0));
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        MetadataTypeDeclarationResult first =
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken);
        MetadataOperationCounters counters = operation.Counters;
        MetadataTypeDeclarationResult second =
            declarations.PostTypeDeclaration(
                address,
                TestContext.Current.CancellationToken);

        Assert.IsType<MetadataTypeDeclarationResult.Rejected>(first);
        Assert.Same(first, second);
        Assert.Equal(counters, operation.Counters);
        Assert.NotSame(
            first,
            declarations.PostTypeDeclaration(
                otherAddress,
                TestContext.Current.CancellationToken));
    }

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
        Assert.Equal(
            MetadataTypeDeclarationBaseKind.ValueType,
            posted.Evidence.BaseKind);
        Assert.True(posted.Evidence.InterfaceCount > 0);
        Assert.False(posted.Evidence.IsHidden);
        Assert.False(posted.Evidence.IsCompilerGenerated);
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
    public void RealStringBuilderPostsBaseAndInterfaceSummaryEvidence()
    {
        string path =
            typeof(System.Text.StringBuilder).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = FindType(
            reader,
            "System.Text",
            nameof(System.Text.StringBuilder));

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
        Assert.Equal(
            MetadataTypeDeclarationBaseKind.Object,
            posted.Evidence.BaseKind);
        Assert.True(posted.Evidence.InterfaceCount > 0);
    }

    [Fact]
    public void CompilerProducedHiddenTypePostsAdmissionEvidence()
    {
        string path =
            typeof(TypeDeclarationHiddenClass).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = FindType(
            reader,
            "ILInspector.Metadata.Tests",
            nameof(TypeDeclarationHiddenClass));

        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(
                    path,
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        handle)));

        Assert.True(posted.Evidence.IsHidden);
    }

    [Fact]
    public void CompilerGeneratedNamePostsAdmissionEvidence()
    {
        string path =
            typeof(__CompilerGeneratedTypeDeclarationProbe)
                .Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = FindType(
            reader,
            "ILInspector.Metadata.Tests",
            nameof(__CompilerGeneratedTypeDeclarationProbe));

        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(
                    path,
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        handle)));

        Assert.True(posted.Evidence.IsCompilerGenerated);
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
        Assert.False(posted.Evidence.IsReadOnly);
    }

    [Theory]
    [InlineData(typeof(TypeDeclarationReadonlyStruct), false)]
    [InlineData(typeof(TypeDeclarationReadonlyRefStruct), true)]
    public void CompilerProducedReadonlyStructPostsModifierEvidence(
        Type type,
        bool isByRefLike)
    {
        string path = type.Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle handle = FindType(
            reader,
            "ILInspector.Metadata.Tests",
            type.Name);

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
        Assert.Equal(isByRefLike, posted.Evidence.IsByRefLike);
        Assert.True(posted.Evidence.IsReadOnly);
    }

    [Fact]
    public void NestedLookalikeAttributeDoesNotPostRefLikeEvidence()
    {
        using Fixture fixture =
            Fixture.CreateRefLikeMarker(
                RefLikeMarkerKind.NestedLookalike);

        MetadataTypeDeclarationEvidence evidence =
            Posted(fixture, "Struct");

        Assert.Equal(
            MetadataTypeDeclarationCategory.Struct,
            evidence.Category);
        Assert.False(evidence.IsByRefLike);
    }

    [Fact]
    public void UnresolvedAttributeOwnerRejectsRefLikeEvidence()
    {
        using Fixture fixture =
            Fixture.CreateRefLikeMarker(
                RefLikeMarkerKind.UnresolvedOwner);

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Struct"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void InvalidAttributeConstructorTagRejectsRefLikeEvidence()
    {
        using Fixture fixture =
            Fixture.CreateRefLikeMarker(
                RefLikeMarkerKind.InvalidConstructorTag);

        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(fixture.Path, fixture["Struct"]));

        Assert.Equal(
            MetadataTypeDeclarationFailureReason.MalformedMetadata,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataTypeDeclarationStage.CategoryClassification,
            rejected.Failure.Stage);
    }

    [Fact]
    public void NestedAttributeOwnerTraversalChargesRelationshipBudget()
    {
        using Fixture shorter = Fixture.CreateRefLikeMarker(
            RefLikeMarkerKind.NestedTypeReferenceLookalike,
            nestedReferenceDepth: 2);
        using Fixture longer = Fixture.CreateRefLikeMarker(
            RefLikeMarkerKind.NestedTypeReferenceLookalike,
            nestedReferenceDepth: 3);

        var shorterPosted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(shorter.Path, shorter["Struct"]));
        var longerPosted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(longer.Path, longer["Struct"]));
        Assert.Equal(
            shorterPosted.Counters.RelationshipEdges + 1,
            longerPosted.Counters.RelationshipEdges);

        long exactLimit = longerPosted.Counters.RelationshipEdges;
        Assert.IsType<MetadataTypeDeclarationResult.Posted>(
            Run(
                longer.Path,
                longer["Struct"],
                new MetadataOperationPolicy(
                    long.MaxValue,
                    maxRelationshipEdges: exactLimit)));
        var rejected = Assert.IsType<
            MetadataTypeDeclarationResult.Rejected>(
                Run(
                    longer.Path,
                    longer["Struct"],
                    new MetadataOperationPolicy(
                        long.MaxValue,
                        maxRelationshipEdges: exactLimit - 1)));
        Assert.Equal(
            MetadataTypeDeclarationFailureReason.BudgetExceeded,
            rejected.Failure.Reason);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            rejected.Failure.BudgetDimension);
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
    public void CompilerProducedParametersPostTheirConstraintAnnotations()
    {
        string path =
            typeof(TypeDeclarationAnnotatedParameters<,,,,>)
                .Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(
                    path,
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        FindType(
                            reader,
                            "ILInspector.Metadata.Tests",
                            "TypeDeclarationAnnotatedParameters`5"))));

        Assert.Equal(
            [
                ("TNotNull", "notnull", 0),
                ("TNullableClass", "class?", 0),
                ("TClass", "class", 0),
                ("TUnmanaged", "unmanaged", 1),
                ("TPlain", null, 0),
            ],
            posted.Evidence.Signature.GenericParameters.Select(
                parameter =>
                {
                    Assert.True(parameter.Annotations.IsCanonical);
                    return (
                        parameter.Name
                            .Contain(TextPolicy.Field)
                            .ToString(),
                        GenericConstraintKeywords.PrimaryKeyword(
                            parameter.Attributes,
                            parameter.Annotations.NullableFlag,
                            parameter.Annotations.IsUnmanaged),
                        parameter.ConstraintCount);
                }));
    }

    [Fact]
    public void ConstraintAnnotationsAgreeWithTheApiSurfaceProjection()
    {
        string path = typeof(int).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            pe,
            includeAll: true,
            typesOnly: true);
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        int compared = 0;
        foreach (ApiType type in surface.Types)
        {
            if (type.TypeParameters.Count == 0
                || type.MetadataToken is not { } token)
            {
                continue;
            }

            var posted = Assert.IsType<
                MetadataTypeDeclarationResult.Posted>(
                    declarations.PostTypeDeclaration(
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            MetadataTokens.TypeDefinitionHandle(
                                token)),
                        TestContext.Current.CancellationToken));
            ImmutableArray<
                MetadataTypeGenericParameterDeclarationEvidence>
                parameters =
                    posted.Evidence.Signature.GenericParameters;
            // A nested Type repeats its enclosing GenericParam rows, which the post retains
            // from their declaring owner; only rows the leaf introduces are the same rows.
            int leaf =
                posted.Evidence.DefinitionIdentity.Segments.Length - 1;
            if (parameters.Length != type.TypeParameters.Count
                || parameters.Any(parameter =>
                    parameter.DefinitionSegmentIndex != leaf))
            {
                continue;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                MetadataTypeGenericParameterDeclarationEvidence
                    parameter = parameters[i];
                TypeParameter expected = type.TypeParameters[i];
                Assert.True(
                    parameter.Name.ValueTextEquals(
                        System.Text.Encoding.UTF8.GetBytes(
                            expected.Name)));
                Assert.True(parameter.Annotations.IsCanonical);
                if (parameter.ConstraintCount != 0)
                    continue;

                Assert.Equal(
                    expected.Constraints,
                    new[]
                    {
                        GenericConstraintKeywords.PrimaryKeyword(
                            parameter.Attributes,
                            parameter.Annotations.NullableFlag,
                            parameter.Annotations.IsUnmanaged),
                        GenericConstraintKeywords
                            .NewConstraintKeyword(
                                parameter.Attributes),
                        GenericConstraintKeywords
                            .AllowsRefStructKeyword(
                                parameter.Attributes),
                    }.OfType<string>());
                compared++;
            }
        }

        Assert.True(
            compared > 500,
            $"Only {compared} CoreLib generic parameters were compared.");
    }

    [Fact]
    public void NonCanonicalAnnotationsPostWithoutAClaim()
    {
        using Fixture fixture =
            Fixture.CreateGenericParameterAnnotations(
                illFormedName: false);

        MetadataTypeGenericParameterAnnotations Annotations(
            string name) =>
            Assert.Single(
                Posted(fixture, name).Signature.GenericParameters)
                .Annotations;

        Assert.Equal(
            new MetadataTypeGenericParameterAnnotations(
                true,
                1,
                false),
            Annotations("Canonical`1"));
        Assert.Equal(
            new MetadataTypeGenericParameterAnnotations(
                true,
                2,
                false),
            Annotations("Contextual`1"));
        Assert.False(Annotations("Truncated`1").IsCanonical);
        Assert.False(Annotations("Duplicated`1").IsCanonical);
        Assert.False(Annotations("EmptyArray`1").IsCanonical);
        Assert.False(Annotations("MalformedContext`1").IsCanonical);
    }

    [Fact]
    public void IllFormedParameterNameKeepsItsBytes()
    {
        using Fixture control =
            Fixture.CreateGenericParameterAnnotations(
                illFormedName: false);
        using Fixture patched =
            Fixture.CreateGenericParameterAnnotations(
                illFormedName: true);

        var expected = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(control.Path, control["Named`1"]));
        var posted = Assert.IsType<
            MetadataTypeDeclarationResult.Posted>(
                Run(patched.Path, patched["Named`1"]));
        MetadataUtf8Name expectedName =
            Assert.Single(
                expected.Evidence.Signature.GenericParameters).Name;
        MetadataUtf8Name name =
            Assert.Single(
                posted.Evidence.Signature.GenericParameters).Name;

        Assert.Equal(7, name.Length);
        Assert.NotEqual(expectedName, name);
        Assert.Equal(
            "\\uDCFF\\uDCFEParam",
            name.Contain(TextPolicy.Field).ToString());
        Assert.Equal(
            expected.Counters.RetainedText + 10,
            posted.Counters.RetainedText);
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
        Assert.Collection(
            posted.Evidence.Signature.GenericParameters,
            parameter =>
            {
                Assert.Equal(0, parameter.DefinitionSegmentIndex);
                Assert.Equal(0, parameter.MetadataIndex);
                Assert.True(parameter.Name.ValueTextEquals("T"u8));
                Assert.Equal(0, parameter.ConstraintCount);
                Assert.Equal(
                    GenericParameterAttributes
                        .ReferenceTypeConstraint
                        | GenericParameterAttributes
                            .DefaultConstructorConstraint,
                    parameter.Attributes);
            },
            parameter =>
            {
                Assert.Equal(1, parameter.DefinitionSegmentIndex);
                Assert.Equal(1, parameter.MetadataIndex);
                Assert.True(parameter.Name.ValueTextEquals("U"u8));
                Assert.Equal(0, parameter.ConstraintCount);
                Assert.Equal(
                    GenericParameterAttributes.None,
                    parameter.Attributes);
            });
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

    [Fact]
    public void SignatureCancellationIsObservedAtPublicationBoundary()
    {
        string path =
            typeof(TypeSignatureSubject<,>).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MetadataTypeDefinitionAddress address =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                FindType(
                    reader,
                    "ILInspector.Metadata.Tests",
                    "TypeSignatureSubject`2"));
        using var cancellation = new CancellationTokenSource();
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded,
            kind =>
            {
                if (kind
                    == MetadataOperationWorkKind
                        .TypeSignaturePublication)
                {
                    cancellation.Cancel();
                }
            });
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);

        Assert.Throws<OperationCanceledException>(() =>
            declarations.PostTypeSignature(
                address,
                cancellation.Token));
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
                typeof(MetadataTypeSignatureResult),
                typeof(MetadataTypeSignatureResult.Posted),
                typeof(MetadataTypeSignatureResult.Rejected),
                typeof(MetadataTypeSignatureEvidence),
                typeof(MetadataTypeSignatureType),
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

    static MetadataTypeSignatureResult RunSignature(
        string path,
        MetadataTypeDefinitionAddress address,
        MetadataOperationPolicy? policy = null)
    {
        using var assembly = AssemblyInspectionSession.Open(path);
        using var operation = new MetadataOperationContext(
            policy ?? MetadataOperationPolicy.Unbounded);
        using MetadataDeclarationSession declarations =
            assembly.CreateDeclarationSession(operation);
        return declarations.PostTypeSignature(
            address,
            TestContext.Current.CancellationToken);
    }

    static MetadataTypeSignatureNamedType NamedDefinition(
        MetadataTypeSignatureType type) =>
        type switch
        {
            MetadataTypeSignatureType.Named named =>
                named.Name,
            MetadataTypeSignatureType.GenericInstance generic =>
                generic.Definition.Name,
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected a named Type shape, not {type.GetType().Name}."),
        };

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

        internal static Fixture CreateRefLikeMarker(
            RefLikeMarkerKind markerKind,
            int nestedReferenceDepth = 2)
        {
            var metadata = CreateBuilder(
                "ref-like-marker.dll",
                "RefLikeMarker");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle valueType =
                AddTypeReference(metadata, core, "ValueType");
            AddModuleType(metadata);
            TypeDefinitionHandle target = AddType(
                metadata,
                "Struct",
                TypeAttributes.Public | TypeAttributes.Sealed,
                valueType);

            EntityHandle constructorOwner;
            if (markerKind == RefLikeMarkerKind.NestedLookalike)
            {
                TypeDefinitionHandle outer =
                    metadata.AddTypeDefinition(
                        TypeAttributes.Public,
                        metadata.GetOrAddString("System.Runtime"),
                        metadata.GetOrAddString("CompilerServices"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(1));
                TypeDefinitionHandle nested =
                    metadata.AddTypeDefinition(
                        TypeAttributes.NestedPublic,
                        default,
                        metadata.GetOrAddString(
                            "IsByRefLikeAttribute"),
                        default,
                        MetadataTokens.FieldDefinitionHandle(1),
                        MetadataTokens.MethodDefinitionHandle(1));
                metadata.AddNestedType(nested, outer);
                constructorOwner = nested;
            }
            else if (markerKind
                == RefLikeMarkerKind.NestedTypeReferenceLookalike)
            {
                if (nestedReferenceDepth is not 2 and not 3)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(nestedReferenceDepth));
                }

                AssemblyReferenceHandle markerAssembly =
                    AddAssemblyReference(
                        metadata,
                        "Lookalike.Attributes",
                        recognized: false);
                TypeReferenceHandle current =
                    nestedReferenceDepth == 2
                        ? metadata.AddTypeReference(
                            markerAssembly,
                            metadata.GetOrAddString(
                                "System.Runtime"),
                            metadata.GetOrAddString(
                                "CompilerServices"))
                        : metadata.AddTypeReference(
                            markerAssembly,
                            metadata.GetOrAddString("System"),
                            metadata.GetOrAddString("Runtime"));
                if (nestedReferenceDepth == 3)
                {
                    current = metadata.AddTypeReference(
                        current,
                        default,
                        metadata.GetOrAddString(
                            "CompilerServices"));
                }
                constructorOwner = metadata.AddTypeReference(
                    current,
                    default,
                    metadata.GetOrAddString(
                        "IsByRefLikeAttribute"));
            }
            else
            {
                constructorOwner = target;
            }

            var constructorSignature = new BlobBuilder();
            constructorSignature.WriteByte(0x20);
            constructorSignature.WriteCompressedInteger(0);
            constructorSignature.WriteByte(0x01);
            MemberReferenceHandle constructor =
                metadata.AddMemberReference(
                    constructorOwner,
                    metadata.GetOrAddString(".ctor"),
                    metadata.GetOrAddBlob(
                        constructorSignature));
            CustomAttributeHandle attribute =
                metadata.AddCustomAttribute(
                    target,
                    constructor,
                    metadata.GetOrAddBlob(
                        new byte[] { 0x01, 0x00, 0x00, 0x00 }));

            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Struct"] = target,
                },
                markerKind switch
                {
                    RefLikeMarkerKind.UnresolvedOwner =>
                        (image, pe, reader) =>
                        PatchMemberReferenceParentToNil(
                            image,
                            pe,
                            reader,
                            constructor),
                    RefLikeMarkerKind.InvalidConstructorTag =>
                        (image, pe, reader) =>
                        PatchCustomAttributeConstructorToInvalidTag(
                            image,
                            pe,
                            reader,
                            attribute),
                    _ => null,
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
            CreateUnrenderableArrayConstraint()
        {
            var metadata = CreateBuilder(
                "unrenderable-array-constraint.dll",
                "UnrenderableArrayConstraint");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            AddModuleType(metadata);
            var signature = new BlobBuilder();
            signature.WriteByte(0x14);
            signature.WriteByte(0x08);
            signature.WriteCompressedInteger(
                ArrayShapeText.MaxRenderableRank + 1);
            signature.WriteCompressedInteger(0);
            signature.WriteCompressedInteger(0);
            TypeSpecificationHandle array =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature));
            TypeDefinitionHandle type = AddType(
                metadata,
                "Generic`1",
                TypeAttributes.Public,
                @object);
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    0);
            metadata.AddGenericParameterConstraint(
                parameter,
                array);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Generic"] = type,
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

        internal static Fixture CreateGenericParameterAnnotations(
            bool illFormedName)
        {
            var metadata = CreateBuilder(
                "generic-annotations.dll",
                "GenericAnnotations");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            MemberReferenceHandle nullable =
                AddAttributeConstructor(
                    metadata,
                    core,
                    "NullableAttribute");
            MemberReferenceHandle context =
                AddAttributeConstructor(
                    metadata,
                    core,
                    "NullableContextAttribute");
            AddModuleType(metadata);

            var handles =
                new Dictionary<string, TypeDefinitionHandle>();
            void Add(
                string name,
                string parameterName,
                byte[][] parameterAnnotations,
                byte[][]? typeContexts = null)
            {
                TypeDefinitionHandle type = AddType(
                    metadata,
                    name,
                    TypeAttributes.Public,
                    @object);
                GenericParameterHandle parameter =
                    metadata.AddGenericParameter(
                        type,
                        GenericParameterAttributes.None,
                        metadata.GetOrAddString(parameterName),
                        0);
                foreach (byte[] value in parameterAnnotations)
                {
                    metadata.AddCustomAttribute(
                        parameter,
                        nullable,
                        metadata.GetOrAddBlob(value));
                }
                foreach (byte[] value in typeContexts ?? [])
                {
                    metadata.AddCustomAttribute(
                        type,
                        context,
                        metadata.GetOrAddBlob(value));
                }
                handles[name] = type;
            }

            byte[] notAnnotated = [0x01, 0x00, 0x01, 0x00, 0x00];
            byte[] annotated = [0x01, 0x00, 0x02, 0x00, 0x00];
            Add("Canonical`1", "T", [notAnnotated]);
            Add("Contextual`1", "T", [], [annotated]);
            Add("Truncated`1", "T", [[0x01, 0x00, 0x01]]);
            Add("Duplicated`1", "T", [notAnnotated, annotated]);
            Add(
                "EmptyArray`1",
                "T",
                [[0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]]);
            Add(
                "MalformedContext`1",
                "T",
                [],
                [[0x01, 0x00, 0x02]]);
            Add("Named`1", "QQParam", [notAnnotated]);

            return Write(
                metadata,
                handles,
                illFormedName
                    ? static (image, _, _) =>
                        PatchStringBytes(
                            image,
                            "\0QQParam\0"u8,
                            1,
                            [0xFF, 0xFE])
                    : null);
        }

        internal static Fixture CreateMalformedConstraintType()
        {
            var metadata = CreateBuilder(
                "malformed-constraint.dll",
                "MalformedConstraint");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            AddModuleType(metadata);
            TypeDefinitionHandle type = AddType(
                metadata,
                "Generic`1",
                TypeAttributes.Public,
                @object);
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    0);
            metadata.AddGenericParameterConstraint(
                parameter,
                MetadataTokens.TypeReferenceHandle(999));
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Generic"] = type,
                });
        }

        internal static Fixture
            CreateLiteralBacktickTypeNames()
        {
            var metadata = CreateBuilder(
                "literal-backtick-type-names.dll",
                "LiteralBacktickTypeNames");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            AddModuleType(metadata);
            TypeDefinitionHandle baseType = AddType(
                metadata,
                "Base`Literal",
                TypeAttributes.Public,
                @object);
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public,
                baseType);
            TypeDefinitionHandle constraint = AddType(
                metadata,
                "Constraint`Literal",
                TypeAttributes.Public,
                @object);
            TypeDefinitionHandle generic = AddType(
                metadata,
                "Generic`1",
                TypeAttributes.Public,
                @object);
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    generic,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    0);
            metadata.AddGenericParameterConstraint(
                parameter,
                constraint);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                    ["Generic"] = generic,
                });
        }

        internal static Fixture
            CreateContradictoryTypeNameArity()
        {
            var metadata = CreateBuilder(
                "contradictory-type-name-arity.dll",
                "ContradictoryTypeNameArity");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            AddModuleType(metadata);
            TypeDefinitionHandle baseType = AddType(
                metadata,
                "Base`1",
                TypeAttributes.Public,
                @object);
            TypeDefinitionHandle derived = AddType(
                metadata,
                "Derived",
                TypeAttributes.Public,
                baseType);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Derived"] = derived,
                });
        }

        internal static Fixture
            CreateOuterConstraintWithNestedParameter()
        {
            var metadata = CreateBuilder(
                "outer-constraint-nested-parameter.dll",
                "OuterConstraintNestedParameter");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            AddModuleType(metadata);
            TypeDefinitionHandle outer = AddType(
                metadata,
                "Outer`1",
                TypeAttributes.Public,
                @object);
            TypeDefinitionHandle inner = AddType(
                metadata,
                "Inner`1",
                TypeAttributes.NestedPublic,
                @object);
            GenericParameterHandle outerParameter =
                metadata.AddGenericParameter(
                    outer,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    0);
            metadata.AddGenericParameter(
                inner,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                0);
            metadata.AddGenericParameter(
                inner,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("U"),
                1);
            var signature = new BlobBuilder();
            signature.WriteByte(0x13);
            signature.WriteCompressedInteger(1);
            TypeSpecificationHandle invalidConstraint =
                metadata.AddTypeSpecification(
                    metadata.GetOrAddBlob(signature));
            metadata.AddGenericParameterConstraint(
                outerParameter,
                invalidConstraint);
            metadata.AddNestedType(inner, outer);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Inner"] = inner,
                });
        }

        internal static Fixture
            CreateIllFormedConstraintTypeName()
        {
            var metadata = CreateBuilder(
                "ill-formed-constraint-name.dll",
                "IllFormedConstraintName");
            AssemblyReferenceHandle core = AddAssemblyReference(
                metadata,
                "System.Runtime",
                recognized: true);
            TypeReferenceHandle @object =
                AddTypeReference(metadata, core, "Object");
            AssemblyReferenceHandle dependency =
                AddAssemblyReference(
                    metadata,
                    "Dependency",
                    recognized: false);
            TypeReferenceHandle constraint =
                metadata.AddTypeReference(
                    dependency,
                    metadata.GetOrAddString("Samples"),
                    metadata.GetOrAddString("QQConstraint"));
            AddModuleType(metadata);
            TypeDefinitionHandle type = AddType(
                metadata,
                "Generic`1",
                TypeAttributes.Public,
                @object);
            GenericParameterHandle parameter =
                metadata.AddGenericParameter(
                    type,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    0);
            metadata.AddGenericParameterConstraint(
                parameter,
                constraint);
            return Write(
                metadata,
                new Dictionary<string, TypeDefinitionHandle>
                {
                    ["Generic"] = type,
                },
                static (image, _, _) =>
                    PatchStringBytes(
                        image,
                        "\0QQConstraint\0"u8,
                        1,
                        [0xff, 0xfe]));
        }

        static MemberReferenceHandle AddAttributeConstructor(
            MetadataBuilder metadata,
            AssemblyReferenceHandle core,
            string name)
        {
            TypeReferenceHandle type = metadata.AddTypeReference(
                core,
                metadata.GetOrAddString(
                    "System.Runtime.CompilerServices"),
                metadata.GetOrAddString(name));
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: true)
                .Parameters(
                    1,
                    returnType => returnType.Void(),
                    parameters => parameters
                        .AddParameter()
                        .Type()
                        .Byte());
            return metadata.AddMemberReference(
                type,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(signature));
        }

        static void PatchStringBytes(
            byte[] image,
            ReadOnlySpan<byte> unique,
            int offset,
            ReadOnlySpan<byte> replacement)
        {
            int index = image.AsSpan().IndexOf(unique);
            Assert.True(index >= 0);
            Assert.Equal(
                -1,
                image.AsSpan(index + 1).IndexOf(unique));
            replacement.CopyTo(image.AsSpan(index + offset));
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

        static void PatchMemberReferenceParentToNil(
            byte[] image,
            PEReader pe,
            MetadataReader reader,
            MemberReferenceHandle handle)
        {
            int offset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(TableIndex.MemberRef)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(TableIndex.MemberRef));
            int maxParentRows = new[]
            {
                TableIndex.TypeDef,
                TableIndex.TypeRef,
                TableIndex.ModuleRef,
                TableIndex.MethodDef,
                TableIndex.TypeSpec,
            }.Max(reader.GetTableRowCount);
            int parentIndexSize =
                maxParentRows < (1 << (16 - 3))
                    ? sizeof(ushort)
                    : sizeof(uint);
            image.AsSpan(offset, parentIndexSize).Clear();
        }

        static void PatchCustomAttributeConstructorToInvalidTag(
            byte[] image,
            PEReader pe,
            MetadataReader reader,
            CustomAttributeHandle handle)
        {
            int rowOffset =
                pe.PEHeaders.MetadataStartOffset
                + reader.GetTableMetadataOffset(
                    TableIndex.CustomAttribute)
                + ((MetadataTokens.GetRowNumber(handle) - 1)
                    * reader.GetTableRowSize(
                        TableIndex.CustomAttribute));
            int constructorIndexSize =
                Math.Max(
                    reader.GetTableRowCount(TableIndex.MethodDef),
                    reader.GetTableRowCount(TableIndex.MemberRef))
                    < (1 << (16 - 3))
                        ? sizeof(ushort)
                        : sizeof(uint);
            int blobIndexSize =
                reader.GetHeapSize(HeapIndex.Blob)
                    > ushort.MaxValue
                        ? sizeof(uint)
                        : sizeof(ushort);
            int parentIndexSize =
                reader.GetTableRowSize(TableIndex.CustomAttribute)
                - constructorIndexSize
                - blobIndexSize;
            BinaryPrimitives.WriteUInt16LittleEndian(
                image.AsSpan(
                    rowOffset + parentIndexSize,
                    sizeof(ushort)),
                1);
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
    where T : class, new()
{
    public sealed class Inner<U>
    {
    }
}

public sealed class TypeDeclarationAnnotatedParameters<
    TNotNull,
    TNullableClass,
    TClass,
    TUnmanaged,
    TPlain>
    where TNotNull : notnull
    where TNullableClass : class?
    where TClass : class
    where TUnmanaged : unmanaged
{
}

public class TypeSignatureBase<T>
{
}

public sealed class TypeSignatureDynamicSubject
    : TypeSignatureBase<dynamic>
{
}

public abstract class TypeSignatureConstraintMarker
{
}

public sealed class TypeSignatureNullableConstraint<T>
    where T : TypeSignatureConstraintMarker?
{
}

public sealed class TypeSignatureLocalDerived
    : TypeSignatureConstraintMarker
{
}

public class TypeSignatureSubject<T, U>
    : TypeSignatureBase<T[]>
    where T : class?
    where U : TypeSignatureConstraintMarker,
        IComparable<U>,
        IEnumerable<T>,
        IDisposable,
        new()
{
    public class PublicNested
    {
    }

    private class PrivateNested
    {
    }

    public class NestedParent
    {
        public class Grandchild
        {
        }
    }
}

public ref struct TypeDeclarationRefStruct
{
}

public readonly struct TypeDeclarationReadonlyStruct
{
}

public readonly ref struct TypeDeclarationReadonlyRefStruct
{
}

[System.ComponentModel.EditorBrowsable(
    System.ComponentModel.EditorBrowsableState.Never)]
public sealed class TypeDeclarationHiddenClass
{
}

public sealed class __CompilerGeneratedTypeDeclarationProbe
{
}

enum RefLikeMarkerKind
{
    NestedLookalike,
    NestedTypeReferenceLookalike,
    UnresolvedOwner,
    InvalidConstructorTag,
}
