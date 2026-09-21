using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class TypeApiDeclarationInspectionTests
{
    static readonly ApiSurfaceProjectionLimits Limits =
        new(1, 10_000, 100_000, 1_000, 10_000, 1_000_000);

    [Fact]
    public async Task ApiVisible_TrimsPrivatePropertyAccessor()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                Name(
                    "ILInspector.Decompiler.Fixtures",
                    "SelectedPropertySamples"),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Equal(
            TypeApiDeclarationOutcome.Available,
            envelope.Content.Outcome);
        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains("public int Count { get; }", text);
        Assert.DoesNotContain("public int Count { get; private set; }", text);
        Assert.DoesNotContain("private set", text);
        Assert.DoesNotContain("=>", text);
    }

    [Fact]
    public async Task All_RetainsPrivatePropertyAccessorWithoutBodies()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                Name(
                    "ILInspector.Decompiler.Fixtures",
                    "SelectedPropertySamples"),
                TypeApiDeclarationScope.All);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains("public int Count { get; private set; }", text);
        Assert.Contains("private int _count;", text);
        Assert.DoesNotContain("=>", text);
    }

    [Fact]
    public async Task NestedRoot_RetainsGenericAncestorShellOnly()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.MetadataApiCorrespondenceV1.AssemblyPath(),
                Name(
                    "MetadataCorrespondenceFixture",
                    "Outer`1",
                    "Inner`1"),
                TypeApiDeclarationScope.ApiVisible);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains("public sealed class Outer<T>", text);
        Assert.Contains("public sealed class Inner<U>", text);
        Assert.Contains("public T? Nested(U value);", text);
        Assert.DoesNotContain("StableField", text);
        Assert.DoesNotContain("=>", text);
    }

    [Fact]
    public async Task ApiVisible_AdmitsProtectedInternal()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.AnalysisAsyncSiblingFriendBase
                    .AssemblyPath(),
                Name(
                    "ILInspector.Analysis.AsyncSiblingFriendBaseFixtures",
                    "ProtectedSiblingBase"),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Equal(
            TypeApiDeclarationOutcome.Available,
            envelope.Content.Outcome);
        Assert.Contains(
            "protected internal int Read();",
            Assert.IsType<string>(envelope.Content.Text));
        Assert.DoesNotContain(
            "InternalRead",
            envelope.Content.Text);
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible, false)]
    [InlineData(TypeApiDeclarationScope.All, true)]
    public async Task PrivateProtected_IsAdmittedOnlyByAll(
        TypeApiDeclarationScope scope,
        bool expected)
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                Name(
                    "ILInspector.Analysis.ClassicAsyncFixtures",
                    "ClassicPrivateProtectedSiblingBaseFixture"),
                scope);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Equal(
            expected,
            text.Contains(
                "private protected int Read(int value);",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnumConstants_RetainMetadataValues()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(BindingFlags).Assembly.Location,
                Name("System.Reflection", "BindingFlags"),
                TypeApiDeclarationScope.ApiVisible);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains("Instance = 4", text);
        Assert.Contains("Static = 8", text);
    }

    [Fact]
    public async Task OrdinaryConstants_RetainMetadataInitializers()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(Math).Assembly.Location,
                Name("System", "Math"),
                TypeApiDeclarationScope.ApiVisible);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains(
            "public const double PI = 3.141592653589793;",
            text);
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible)]
    [InlineData(TypeApiDeclarationScope.All)]
    public async Task InterfaceConstants_RetainInitializerAlongsideBodylessMethod(
        TypeApiDeclarationScope scope)
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(IApiDeclarationConstantsFixture)
                    .Assembly.Location,
                Name(
                    "DotnetInspector.Fixtures",
                    nameof(IApiDeclarationConstantsFixture)),
                scope);

        Assert.Equal(
            TypeApiDeclarationOutcome.Available,
            envelope.Content.Outcome);
        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains("public const int Answer = 42;", text);
        Assert.Contains("void Observe();", text);
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible)]
    [InlineData(TypeApiDeclarationScope.All)]
    public async Task FloatingPointNegativeZero_RetainsExactLiteralSemantics(
        TypeApiDeclarationScope scope)
    {
        Assert.Equal(
            long.MinValue,
            BitConverter.DoubleToInt64Bits(
                ApiDeclarationConstantsFixture.NegativeZero));
        Assert.Equal(
            int.MinValue,
            BitConverter.SingleToInt32Bits(
                ApiDeclarationConstantsFixture.FloatNegativeZero));

        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(ApiDeclarationConstantsFixture)
                    .Assembly.Location,
                Name(
                    "DotnetInspector.Fixtures",
                    nameof(ApiDeclarationConstantsFixture)),
                scope);

        Assert.Equal(
            TypeApiDeclarationOutcome.Available,
            envelope.Content.Outcome);
        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains(
            "public const double NegativeZero = -0D;",
            text);
        Assert.Contains(
            "public const float FloatNegativeZero = -0F;",
            text);
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible)]
    [InlineData(TypeApiDeclarationScope.All)]
    public async Task ExtensionReceiver_DoesNotPublishDiscoveryProjection(
        TypeApiDeclarationScope scope)
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(BodyShapeFixture).Assembly.Location,
                Name(
                    "DotnetInspector.Fixtures",
                    nameof(BodyShapeFixture)),
                scope);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.DoesNotContain(
            nameof(BodyShapeFixtureExtensions.ProjectedCreation),
            text);
    }

    [Fact]
    public async Task ExtensionDeclaringType_RetainsActualDeclaration()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(BodyShapeFixtureExtensions).Assembly.Location,
                Name(
                    "DotnetInspector.Fixtures",
                    nameof(BodyShapeFixtureExtensions)),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Contains(
            nameof(BodyShapeFixtureExtensions.ProjectedCreation),
            Assert.IsType<string>(envelope.Content.Text));
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible, false)]
    [InlineData(TypeApiDeclarationScope.All, true)]
    public async Task PrivateExplicitImplementation_IsAdmittedOnlyByAll(
        TypeApiDeclarationScope scope,
        bool expected)
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(List<>).Assembly.Location,
                Name("System.Collections.Generic", "List`1"),
                scope);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Equal(
            expected,
            text.Contains(
                "System.Collections.IList.get_IsFixedSize",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task InterfaceDeclaration_RetainsImplicitPublicProperty()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                typeof(System.Collections.IList).Assembly.Location,
                Name("System.Collections", "IList"),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Contains(
            "bool IsFixedSize { get; }",
            Assert.IsType<string>(envelope.Content.Text));
    }

    [Theory]
    [InlineData(
        TypeAttributes.NestedPublic,
        TypeApiDeclarationScope.ApiVisible,
        TypeApiDeclarationOutcome.Unavailable)]
    [InlineData(
        TypeAttributes.NestedPublic,
        TypeApiDeclarationScope.All,
        TypeApiDeclarationOutcome.Unavailable)]
    [InlineData(
        TypeAttributes.NestedPrivate,
        TypeApiDeclarationScope.ApiVisible,
        TypeApiDeclarationOutcome.Available)]
    [InlineData(
        TypeAttributes.NestedPrivate,
        TypeApiDeclarationScope.All,
        TypeApiDeclarationOutcome.Unavailable)]
    [InlineData(
        TypeAttributes.NestedFamORAssem,
        TypeApiDeclarationScope.ApiVisible,
        TypeApiDeclarationOutcome.Unavailable)]
    [InlineData(
        TypeAttributes.NestedFamANDAssem,
        TypeApiDeclarationScope.ApiVisible,
        TypeApiDeclarationOutcome.Available)]
    public async Task RejectedNestedDeclaration_AffectsOnlySelectedScope(
        TypeAttributes accessibility,
        TypeApiDeclarationScope scope,
        TypeApiDeclarationOutcome expected)
    {
        byte[] image = BuildRejectedNestedTypeImage(
            accessibility,
            includeSelectedSibling: false);

        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                ResolvedImage(image, "NestedFailure"),
                Name("", "Outer"),
                scope);

        Assert.Equal(expected, envelope.Content.Outcome);
        if (expected == TypeApiDeclarationOutcome.Unavailable)
        {
            Assert.Contains(
                envelope.Content.Failures,
                failure => failure.Operation == "type row");
        }
        else
        {
            Assert.DoesNotContain(
                "BrokenChild",
                Assert.IsType<string>(envelope.Content.Text));
        }
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible)]
    [InlineData(TypeApiDeclarationScope.All)]
    public async Task RejectedNestedSibling_DoesNotPoisonSelectedRoot(
        TypeApiDeclarationScope scope)
    {
        byte[] image = BuildRejectedNestedTypeImage(
            TypeAttributes.NestedPublic,
            includeSelectedSibling: true,
            invalidBrokenName: true);

        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                ResolvedImage(image, "NestedFailure"),
                Name("", "Outer", "Selected"),
                scope);

        Assert.Equal(
            TypeApiDeclarationOutcome.Available,
            envelope.Content.Outcome);
        Assert.Contains(
            "public sealed class Selected",
            Assert.IsType<string>(envelope.Content.Text));
    }

    [Theory]
    [InlineData(
        TypeAttributes.NestedPublic,
        TypeApiDeclarationScope.ApiVisible,
        TypeApiDeclarationOutcome.Unavailable)]
    [InlineData(
        TypeAttributes.NestedPublic,
        TypeApiDeclarationScope.All,
        TypeApiDeclarationOutcome.Unavailable)]
    [InlineData(
        TypeAttributes.NestedPrivate,
        TypeApiDeclarationScope.ApiVisible,
        TypeApiDeclarationOutcome.Available)]
    [InlineData(
        TypeAttributes.NestedPrivate,
        TypeApiDeclarationScope.All,
        TypeApiDeclarationOutcome.Unavailable)]
    public async Task RejectedNestedIdentityWithoutOwner_AffectsSelectedRoot(
        TypeAttributes accessibility,
        TypeApiDeclarationScope scope,
        TypeApiDeclarationOutcome expected)
    {
        byte[] image = BuildRejectedNestedTypeImage(
            accessibility,
            includeSelectedSibling: false,
            invalidBrokenName: true);

        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                ResolvedImage(image, "NestedFailure"),
                Name("", "Outer"),
                scope);

        Assert.Equal(
            expected,
            envelope.Content.Outcome);
        if (expected == TypeApiDeclarationOutcome.Unavailable)
        {
            Assert.Contains(
                envelope.Content.Failures,
                failure => failure.Operation == "type identity");
        }
        else
        {
            Assert.DoesNotContain(
                envelope.Content.Failures,
                failure => failure.Operation == "type identity");
        }
    }

    [Theory]
    [InlineData(TypeApiDeclarationScope.ApiVisible)]
    [InlineData(TypeApiDeclarationScope.All)]
    public async Task SelectedRoot_RetainsOnlyScopedNestedSubtree(
        TypeApiDeclarationScope scope)
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.MetadataPublicMethodRoots.AssemblyPath(),
                Name(
                    "ILInspector.Metadata.PublicMethodRootFixtures",
                    "PublicTopLevel"),
                scope);

        string text = Assert.IsType<string>(envelope.Content.Text);
        Assert.Contains("public sealed class PublicNested", text);
        Assert.Contains("protected sealed class ProtectedNested", text);
        Assert.DoesNotContain("InternalTopLevel", text);
        if (scope == TypeApiDeclarationScope.ApiVisible)
        {
            Assert.DoesNotContain("InternalNested", text);
            Assert.DoesNotContain("PrivateNested", text);
        }
        else
        {
            Assert.Contains("internal sealed class InternalNested", text);
            Assert.Contains("private sealed class PrivateNested", text);
        }
    }

    [Theory]
    [InlineData("MemorySafetyExtensionEnum", "public enum MemorySafetyExtensionEnum")]
    [InlineData("MemorySafetyExtensionDelegate", "public delegate void MemorySafetyExtensionDelegate()")]
    [InlineData("CheckedIntegerOperandSamples", "internal static class CheckedIntegerOperandSamples")]
    public async Task ExactRoot_RendersEnumDelegateAndNonPublicType(
        string type,
        string declaration)
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                Name(
                    type == "CheckedIntegerOperandSamples"
                        ? "ILInspector.Decompiler.Fixtures"
                        : "ILInspector.Decompiler.Fixtures.NewUnsafe",
                    type),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Equal(
            TypeApiDeclarationOutcome.Available,
            envelope.Content.Outcome);
        Assert.Contains(
            declaration,
            Assert.IsType<string>(envelope.Content.Text));
    }

    [Fact]
    public async Task MissingType_IsConclusiveNotFound()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                Name("ILInspector.Decompiler.Fixtures", "Missing"),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Equal(
            TypeApiDeclarationOutcome.NotFound,
            envelope.Content.Outcome);
        Assert.Null(envelope.Content.Text);
        Assert.Empty(envelope.Content.Failures);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code == "type-api-declaration.not-found");
    }

    [Fact]
    public async Task RejectedTypeIdentity_IsUnavailableNotNotFound()
    {
        byte[] image = BuildRejectedTypeIdentityImage();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    "RejectedIdentity",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "rejected declaration identity"));

        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                assembly,
                Name("", "Rejected"),
                TypeApiDeclarationScope.ApiVisible);

        Assert.Equal(
            TypeApiDeclarationOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(envelope.Content.Text);
        TypeApiDeclarationFailure failure =
            Assert.Single(envelope.Content.Failures);
        Assert.Equal(
            TypeApiDeclarationFailureKind.InspectionIncomplete,
            failure.Kind);
        Assert.Equal("type identity", failure.Operation);
        Assert.DoesNotContain(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code == "type-api-declaration.not-found");
    }

    [Fact]
    public async Task BoundedExtraction_DoesNotPublishPartialDeclaration()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                Name(
                    "ILInspector.Decompiler.Fixtures",
                    "SelectedPropertySamples"),
                TypeApiDeclarationScope.ApiVisible,
                new ApiSurfaceProjectionLimits(
                    1,
                    1,
                    100_000,
                    1_000,
                    10_000,
                    1_000_000));

        Assert.Equal(
            TypeApiDeclarationOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(envelope.Content.Text);
        Assert.Contains(
            envelope.Content.Failures,
            failure =>
                failure.Kind
                    == TypeApiDeclarationFailureKind
                        .ProjectionTruncated);
    }

    [Fact]
    public async Task RejectedParticipant_RetainsTypedFailure()
    {
        string path =
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
        ResolvedAssemblyReference source =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("declaration test"));
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                source.Identity,
                path: null,
                () => new MemoryStream([1, 2, 3], writable: false),
                AssemblyResolutionProvenance.Local(
                    "rejected declaration test"));
        var participant = new AssemblyContextParticipant(
            rejected,
            NoResolverAssemblyBindingPolicy.Instance);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            TypeApiDeclarationInspection.Execute(
                group,
                participant,
                Name(
                    "ILInspector.Decompiler.Fixtures",
                    "SelectedPropertySamples"),
                TypeApiDeclarationScope.ApiVisible,
                Limits,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            TypeApiDeclarationOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Content.Failures,
            failure =>
                failure.Kind
                    == TypeApiDeclarationFailureKind
                        .ParticipantRejected);
    }

    [Fact]
    public async Task CompletedEnvelope_IsAotJsonSerializable()
    {
        InspectionEnvelope<TypeApiDeclarationResult> envelope =
            await ExecuteAsync(
                FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
                Name(
                    "ILInspector.Decompiler.Fixtures.NewUnsafe",
                    "MemorySafetyExtensionEnum"),
                TypeApiDeclarationScope.ApiVisible);

        string json = JsonSerializer.Serialize(
            envelope,
            TypeApiDeclarationInspectionJsonContext.Default
                .InspectionEnvelopeTypeApiDeclarationResult);
        InspectionEnvelope<TypeApiDeclarationResult>? roundTrip =
            JsonSerializer.Deserialize(
                json,
                TypeApiDeclarationInspectionJsonContext.Default
                    .InspectionEnvelopeTypeApiDeclarationResult);

        Assert.NotNull(roundTrip);
        Assert.Equal(envelope.Content.Outcome, roundTrip.Content.Outcome);
        Assert.Equal(
            envelope.Content.TypeIdentity.Namespace,
            roundTrip.Content.TypeIdentity.Namespace);
        Assert.Equal(
            envelope.Content.TypeIdentity.Segments,
            roundTrip.Content.TypeIdentity.Segments);
        Assert.Equal(envelope.Content.Scope, roundTrip.Content.Scope);
        Assert.Equal(envelope.Content.Text, roundTrip.Content.Text);
        Assert.Empty(roundTrip.Content.Failures);
        Assert.IsType<InspectionShare.NonProjectable>(roundTrip.Share);
    }

    static async Task<InspectionEnvelope<TypeApiDeclarationResult>>
        ExecuteAsync(
            string assemblyPath,
            MetadataTypeDefinitionName type,
            TypeApiDeclarationScope scope,
            ApiSurfaceProjectionLimits? limits = null)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local("declaration test"));
        return await ExecuteAsync(
            assembly,
            type,
            scope,
            limits);
    }

    static async Task<InspectionEnvelope<TypeApiDeclarationResult>>
        ExecuteAsync(
            ResolvedAssemblyReference assembly,
            MetadataTypeDefinitionName type,
            TypeApiDeclarationScope scope,
            ApiSurfaceProjectionLimits? limits = null)
    {
        var participant = new AssemblyContextParticipant(
            assembly,
            NoResolverAssemblyBindingPolicy.Instance);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        return TypeApiDeclarationInspection.Execute(
            group,
            participant,
            type,
            scope,
            limits ?? Limits,
            TestContext.Current.CancellationToken);
    }

    static byte[] BuildRejectedTypeIdentityImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                "RejectedIdentity.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("RejectedIdentity"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle rejected =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Rejected"),
                baseType: default,
                fieldList:
                    MetadataTokens.FieldDefinitionHandle(1),
                methodList:
                    MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(rejected, rejected);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildRejectedNestedTypeImage(
        TypeAttributes brokenAccessibility,
        bool includeSelectedSibling,
        bool invalidBrokenName = false)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                "NestedFailure.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NestedFailure"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle outer = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("Outer"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        if (includeSelectedSibling)
        {
            TypeDefinitionHandle selected =
                metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic
                        | TypeAttributes.Sealed,
                    default,
                    metadata.GetOrAddString("Selected"),
                    baseType: default,
                    fieldList:
                        MetadataTokens.FieldDefinitionHandle(1),
                    methodList:
                        MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(selected, outer);
        }
        TypeDefinitionHandle broken =
            metadata.AddTypeDefinition(
                brokenAccessibility | TypeAttributes.Sealed,
                default,
                invalidBrokenName
                    ? default
                    : metadata.GetOrAddString("BrokenChild"),
                baseType: MetadataTokens.TypeReferenceHandle(99),
                fieldList:
                    MetadataTokens.FieldDefinitionHandle(1),
                methodList:
                    MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(broken, outer);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }

    static ResolvedAssemblyReference ResolvedImage(
        byte[] image,
        string assemblyName) =>
        ResolvedAssemblyReference.Create(
            new AssemblyReferenceIdentity(
                assemblyName,
                new Version(1, 0, 0, 0),
                null,
                null),
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local(
                "declaration malformed input test"));

    static MetadataTypeDefinitionName Name(
        string typeNamespace,
        params string[] segments) =>
        MetadataTypeDefinitionName.Create(typeNamespace, [.. segments])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The test Type identity must be valid.");
}
