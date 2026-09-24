using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataRelationInspectionTests
{
    [Fact]
    public void ExtensionAndReferenceProducersRetainExactEvidence()
    {
        string path = typeof(MetadataFindings).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Extensions,
                            MetadataRelationFamily.AssemblyReferences,
                        ],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.Extensions.Disposition);
        MetadataExtensionRelationEvidence method =
            Assert.Single(
            available.Result.Extensions.Evidence,
            static extension =>
                extension.Member.CanonicalSignature.StartsWith(
                    "M:",
                    StringComparison.Ordinal)
                && extension.Member.CanonicalSignature
                    == "M:ILInspector.Metadata.MetadataReaderExtensions.GetFullTypeName(System.Reflection.Metadata.MetadataReader,System.Reflection.Metadata.TypeDefinition)"
                && IsNamedType(
                    extension.Receiver,
                    "System.Reflection.Metadata",
                    "MetadataReader"));
        Assert.Equal(
            HandleKind.MethodDefinition,
            MetadataTokens.EntityHandle(
                method.DeclarationMetadataToken).Kind);

        MetadataExtensionRelationEvidence property =
            Assert.Single(
            available.Result.Extensions.Evidence,
            static extension =>
                extension.Member.CanonicalSignature.StartsWith(
                    "P:",
                    StringComparison.Ordinal)
                && extension.Member.CanonicalSignature.Contains(
                    "IsPublic",
                    StringComparison.Ordinal)
                && extension.Receiver
                    is MetadataTypeIdentity.Named);
        Assert.Equal(
            HandleKind.PropertyDefinition,
            MetadataTokens.EntityHandle(
                property.DeclarationMetadataToken).Kind);

        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            available.Result.AssemblyReferences.Disposition);
        Assert.Contains(
            available.Result.AssemblyReferences.Evidence,
            static reference =>
                reference.Target.Name
                    == "System.Reflection.Metadata");
        Assert.All(
            available.Result.AssemblyReferences.Evidence,
            reference =>
                Assert.Equal(
                    available.Result.Receipt.Assembly,
                    reference.Source));
    }

    [Fact]
    public void HierarchyAndSignaturesPreserveConstructedShapesAndSites()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        MetadataTypeDefinitionAddress convert =
            Address(path, "System", "Convert");
        MetadataTypeDefinitionAddress memoryExtensions =
            Address(path, "System", "MemoryExtensions");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Hierarchy,
                            MetadataRelationFamily.Extensions,
                            MetadataRelationFamily.Signatures,
                        ],
                        MetadataOperationPolicy.Unbounded,
                        typeScope:
                        [
                            memoryStream,
                            convert,
                            memoryExtensions,
                        ]),
                    TestContext.Current.CancellationToken));

        MetadataHierarchyRelationEvidence baseType =
            Assert.Single(
                available.Result.Hierarchy.Evidence,
                relation =>
                    relation.Source == memoryStream
                    && relation.Kind
                        == MetadataHierarchyRelationKind.BaseType);
        AssertNamedType(
            baseType.Target,
            "System.IO",
            "Stream");
        Assert.Equal(
            HandleKind.TypeDefinition,
            MetadataTokens.EntityHandle(
                baseType.MetadataToken).Kind);

        MetadataSignatureRelationEvidence toHexString =
            Assert.Single(
                available.Result.Signatures.Evidence,
                relation =>
                    relation.Kind
                        == MetadataSignatureRelationKind.Accepts
                    && relation.ParameterIndex == 0
                    && relation.Member.MemberName == "ToHexString"
                    && IsConstructedType(
                        relation.Shape,
                        "System",
                        "ReadOnlySpan`1",
                        "byte"));
        Assert.Equal(convert, toHexString.DeclaringType);

        MetadataSignatureRelationEvidence asSpan =
            Assert.Single(
                available.Result.Signatures.Evidence,
                relation =>
                    relation.Kind
                        == MetadataSignatureRelationKind.Returns
                    && relation.Member.CanonicalSignature.Contains(
                        "AsSpan(System.String)",
                        StringComparison.Ordinal)
                    && IsConstructedType(
                        relation.Shape,
                        "System",
                        "ReadOnlySpan`1",
                        "char"));
        Assert.Equal(memoryExtensions, asSpan.DeclaringType);
        MetadataExtensionRelationEvidence asSpanExtension =
            Assert.Single(
                available.Result.Extensions.Evidence,
                relation =>
                    relation.Member.CanonicalSignature
                        == asSpan.Member.CanonicalSignature);
        Assert.Equal(asSpan.Member, asSpanExtension.Member);
    }

    [Fact]
    public void ProducerLimitReturnsPartialInsteadOfExactEmpty()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [MetadataRelationFamily.Hierarchy],
                        new MetadataOperationPolicy(
                            maxMetadataRows: long.MaxValue,
                            maxRelationshipEdges: 0),
                        typeScope: [memoryStream]),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            available.Result.Hierarchy.Disposition);
        Assert.Empty(available.Result.Hierarchy.Evidence);
        MetadataRelationDiagnostic diagnostic =
            Assert.Single(available.Result.Hierarchy.Diagnostics);
        Assert.Equal(
            MetadataRelationDiagnosticKind.Limit,
            diagnostic.Kind);
        Assert.Equal(
            MetadataOperationDimension.RelationshipEdges,
            diagnostic.BudgetDimension);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Considered);
        Assert.Equal(1, available.Result.Hierarchy.Coverage?.Limited);
    }

    [Fact]
    public void TypeScopeMustBelongToExactInspectedImage()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Private.CoreLib.dll");
        MetadataTypeDefinitionAddress memoryStream =
            Address(path, "System.IO", "MemoryStream");
        var foreign = new MetadataTypeDefinitionAddress(
            Guid.NewGuid(),
            memoryStream.Definition);
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var request = new MetadataRelationInspectionRequest(
            [MetadataRelationFamily.Hierarchy],
            MetadataOperationPolicy.Unbounded,
            typeScope: [foreign]);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => session.Relations(
                request,
                TestContext.Current.CancellationToken));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public void MalformedModuleMvidSettlesRequestedFamiliesAsFailed()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    BuildInvalidModuleMvidImage(),
                    writable: false));

        var available =
            Assert.IsType<MetadataRelationInspectionOutcome.Available>(
                session.Relations(
                    new(
                        [
                            MetadataRelationFamily.Hierarchy,
                            MetadataRelationFamily.Signatures,
                        ],
                        MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));

        Assert.Null(available.Result.Receipt.ModuleVersionId);
        Assert.NotNull(available.Result.Receipt.Assembly);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Failed,
            available.Result.Hierarchy.Disposition);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Failed,
            available.Result.Signatures.Disposition);
        Assert.Empty(available.Result.Hierarchy.Evidence);
        Assert.Empty(available.Result.Signatures.Evidence);
        Assert.All(
            available.Result.Hierarchy.Diagnostics
                .Concat(available.Result.Signatures.Diagnostics),
            diagnostic => Assert.Equal(
                MetadataRelationDiagnosticKind.MalformedMetadata,
                diagnostic.Kind));
    }

    private static MetadataTypeDefinitionAddress Address(
        string path,
        string @namespace,
        string name)
    {
        using var stream = File.OpenRead(path);
        using var image =
            new PEReader(
                stream,
                PEStreamOptions.PrefetchMetadata);
        MetadataReader reader = image.GetMetadataReader();
        foreach (TypeDefinitionHandle handle
            in reader.TypeDefinitions)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(handle);
            if (reader.StringComparer.Equals(
                    definition.Namespace,
                    @namespace)
                && reader.StringComparer.Equals(
                    definition.Name,
                    name))
            {
                return MetadataTypeDefinitionAddress.FromHandle(
                    reader,
                    handle);
            }
        }

        throw new InvalidOperationException(
            $"Type '{@namespace}.{name}' was not found.");
    }

    private static void AssertNamedType(
        MetadataTypeIdentity identity,
        string @namespace,
        string name)
    {
        var named =
            Assert.IsType<MetadataTypeIdentity.Named>(identity);
        Assert.Equal(
            @namespace,
            named.Definition.Namespace.ToString());
        Assert.Equal(
            name,
            Assert.Single(named.Definition.Segments).ToString());
    }

    private static bool IsConstructedType(
        MetadataTypeIdentity identity,
        string @namespace,
        string name,
        string argumentName) =>
        identity
            is MetadataTypeIdentity.GenericInstance
            {
                Definition: var definition,
                Arguments: [MetadataTypeIdentity.Primitive primitive],
            }
        && definition.Namespace.ToString() == @namespace
        && definition.Segments.Length == 1
        && definition.Segments[0].ToString() == name
        && primitive.Name.ToString() == argumentName;

    private static bool IsNamedType(
        MetadataTypeIdentity identity,
        string @namespace,
        string name) =>
        identity is MetadataTypeIdentity.Named named
        && named.Definition.Namespace.ToString() == @namespace
        && named.Definition.Segments.Length == 1
        && named.Definition.Segments[0].ToString() == name;

    private static byte[] BuildInvalidModuleMvidImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("InvalidMvid.dll"),
            MetadataTokens.GuidHandle(100),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("InvalidMvid"),
            new Version(1, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

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
}
