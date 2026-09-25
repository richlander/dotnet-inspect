using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataRelationInspectionTests
{
    [Fact]
    public void AssemblyReferencePopulationPushesCountAndBoundedRows()
    {
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(
                    BuildDuplicateAssemblyReferenceImage(),
                    writable: false));

        var first =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            new(),
                            new(
                                startOrdinal: 0,
                                maximumRows: 1)),
                        TestContext.Current.CancellationToken));

        var counted =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Counted>(first.Result.Count);
        Assert.Equal(2, counted.Value);
        var firstRows =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Read>(first.Result.Rows);
        MetadataAssemblyReferenceRelationPopulationRow firstRow =
            Assert.Single(firstRows.Items);
        Assert.Equal("Sample.First", firstRow.Target.Name);
        Assert.Equal(2, firstRow.MetadataTokens.Length);
        Assert.Equal(1, firstRows.NextOrdinal);
        Assert.Equal(3, first.Result.Coverage.Considered);
        Assert.Equal(3, first.Result.Coverage.Examined);

        var second =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            rows: new(
                                startOrdinal: firstRows.NextOrdinal!.Value,
                                maximumRows: 1),
                            expectedModuleVersionId:
                                first.Result.Receipt.ModuleVersionId),
                        TestContext.Current.CancellationToken));
        var secondRows =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Read>(second.Result.Rows);
        Assert.Equal(
            "Sample.Second",
            Assert.Single(secondRows.Items).Target.Name);
        Assert.Null(secondRows.NextOrdinal);

        var countOnly =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            count: new()),
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            2,
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Counted>(countOnly.Result.Count).Value);
        Assert.Null(countOnly.Result.Rows);

        var stale =
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationOutcome.Available>(
                    session.AssemblyReferenceRelations(
                        new(
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(
                                startOrdinal: 0,
                                maximumRows: 1),
                            expectedModuleVersionId: Guid.NewGuid()),
                        TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            MetadataAssemblyReferenceRelationPopulationCountOutcome
                .Failed>(stale.Result.Count);
        Assert.Equal(
            MetadataAssemblyReferenceRelationPopulationRowsRejection
                .StaleSource,
            Assert.IsType<
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Rejected>(stale.Result.Rows).Reason);
        Assert.Equal(
            MetadataRelationDiagnosticKind.StaleSource,
            Assert.Single(stale.Result.Diagnostics).Kind);
    }

    [Fact]
    public void ExtensionPopulationPushesCountAndBoundedRowsForHttpClient()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "System.Net.Http.Json.dll");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        AssemblyReferenceIdentity receiverAssembly =
            Assert.Single(
                session.AssemblyReferenceIdentities(),
                static reference =>
                    reference.Name == "System.Net.Http");
        var receiver = new MetadataExtensionReceiverSelection(
            receiverAssembly,
            TypeName("System.Net.Http", "HttpClient"));

        var first =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(
                                startOrdinal: 0,
                                maximumRows: 2)),
                        TestContext.Current.CancellationToken));
        int expected =
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    first.Result.Count).Value;
        Assert.True(expected > 2);
        Assert.Equal(
            MetadataRelationFamilyDisposition.Complete,
            first.Result.Disposition);
        Assert.True(first.Result.Coverage.Excluded > 0);

        var drained =
            new List<MetadataExtensionRelationPopulationRow>();
        MetadataExtensionRelationPopulationResult current = first.Result;
        while (true)
        {
            var rows =
                Assert.IsType<
                    MetadataExtensionRelationPopulationRowsOutcome.Read>(
                        current.Rows);
            Assert.InRange(rows.Items.Length, 0, 2);
            drained.AddRange(rows.Items);
            if (rows.NextOrdinal is null)
                break;

            current =
                Assert.IsType<
                    MetadataExtensionRelationPopulationOutcome.Available>(
                        session.ExtensionRelations(
                            new(
                                receiver,
                                MetadataOperationPolicy.Unbounded,
                                rows: new(
                                    rows.NextOrdinal.Value,
                                    maximumRows: 2),
                                expectedModuleVersionId:
                                    current.Receipt.ModuleVersionId),
                            TestContext.Current.CancellationToken))
                    .Result;
        }

        Assert.Equal(expected, drained.Count);
        Assert.All(
            drained.SelectMany(static row => row.Occurrences),
            occurrence =>
            {
                Assert.Equal(
                    HandleKind.MethodDefinition,
                    MetadataTokens.EntityHandle(
                        occurrence.DeclarationMetadataToken).Kind);
                AssertNamedType(
                    occurrence.Receiver,
                    "System.Net.Http",
                    "HttpClient");
            });
        Assert.Contains(
            drained.SelectMany(static row => row.Occurrences),
            static occurrence =>
                occurrence.Member.MemberName == "GetFromJsonAsync");

        var countOnly =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new()),
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            expected,
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    countOnly.Result.Count).Value);
        Assert.Null(countOnly.Result.Rows);

        var stale =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(0, 1),
                            expectedModuleVersionId: Guid.NewGuid()),
                        TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            MetadataExtensionRelationPopulationCountOutcome.Failed>(
                stale.Result.Count);
        Assert.Equal(
            MetadataExtensionRelationPopulationRowsRejection.StaleSource,
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Rejected>(
                    stale.Result.Rows).Reason);

        var limited =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            new MetadataOperationPolicy(
                                maxMetadataRows: long.MaxValue,
                                maxRelationshipEdges: 0),
                            count: new(),
                            rows: new(0, 1)),
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            MetadataRelationFamilyDisposition.Partial,
            limited.Result.Disposition);
        _ = Assert.IsType<
            MetadataExtensionRelationPopulationCountOutcome.Incomplete>(
                limited.Result.Count);
        _ = Assert.IsType<
            MetadataExtensionRelationPopulationRowsOutcome.Incomplete>(
                limited.Result.Rows);
        Assert.Equal(
            MetadataRelationDiagnosticKind.Limit,
            Assert.Single(limited.Result.Diagnostics).Kind);
    }

    [Fact]
    public void ExtensionPopulationPreservesConstructedReceiverContext()
    {
        string path =
            typeof(Inspector.Findings.FindingExtensions).Assembly.Location;
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        AssemblyReferenceIdentity receiverAssembly =
            Assert.Single(
                session.AssemblyReferenceIdentities(),
                static reference =>
                    reference.Name == "System.Runtime");
        var receiver = new MetadataExtensionReceiverSelection(
            receiverAssembly,
            TypeName("System.Collections.Generic", "IEnumerable`1"));

        var available =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            receiver,
                            MetadataOperationPolicy.Unbounded,
                            count: new(),
                            rows: new(0, 10)),
                        TestContext.Current.CancellationToken));
        MetadataExtensionRelationPopulationRow row =
            Assert.Single(
                Assert.IsType<
                    MetadataExtensionRelationPopulationRowsOutcome.Read>(
                        available.Result.Rows).Items);
        MetadataExtensionRelationEvidence evidence =
            Assert.Single(row.Occurrences);

        Assert.Equal("Keys<T>", evidence.Member.MemberName);
        var generic =
            Assert.IsType<MetadataTypeIdentity.GenericInstance>(
                evidence.Receiver);
        Assert.Equal(
            "IEnumerable`1",
            generic.Definition.Segments[0].ToString());
        Assert.Contains(
            generic.Arguments,
            static argument =>
                ContainsGenericParameter(argument));
        Assert.Equal(
            1,
            Assert.IsType<
                MetadataExtensionRelationPopulationCountOutcome.Counted>(
                    available.Result.Count).Value);
    }

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
        MetadataExtensionReceiverSelection propertyReceiver =
            ReceiverSelection(
                available.Result.Receipt.Assembly!,
                property.Receiver);
        var propertyPopulation =
            Assert.IsType<
                MetadataExtensionRelationPopulationOutcome.Available>(
                    session.ExtensionRelations(
                        new(
                            propertyReceiver,
                            MetadataOperationPolicy.Unbounded,
                            rows: new(0, 100)),
                        TestContext.Current.CancellationToken));
        Assert.Contains(
            Assert.IsType<
                MetadataExtensionRelationPopulationRowsOutcome.Read>(
                    propertyPopulation.Result.Rows)
                .Items.SelectMany(static row => row.Occurrences),
            occurrence =>
                occurrence.DeclarationMetadataToken
                    == property.DeclarationMetadataToken);

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

    private static MetadataTypeDefinitionName TypeName(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [.. segments])).Name;

    private static MetadataExtensionReceiverSelection ReceiverSelection(
        AssemblyReferenceIdentity source,
        MetadataTypeIdentity receiver)
    {
        MetadataNamedTypeIdentity named = receiver switch
        {
            MetadataTypeIdentity.Named value => value.Definition,
            MetadataTypeIdentity.GenericInstance value =>
                value.Definition,
            _ => throw new Xunit.Sdk.XunitException(
                "Expected a named extension receiver."),
        };
        AssemblyReferenceIdentity assembly =
            named.Scope.Kind switch
            {
                MetadataTypeScopeKind.CurrentModule
                    or MetadataTypeScopeKind.ModuleReference =>
                    source,
                MetadataTypeScopeKind.AssemblyReference
                    when named.Scope.Assembly is { } reference =>
                    new(
                        reference.Name.ToString(),
                        reference.Version,
                        reference.Culture?.ToString(),
                        reference.PublicKeyToken?.ToString()),
                _ => throw new Xunit.Sdk.XunitException(
                    "Expected a resolvable extension receiver scope."),
            };
        return new(
            assembly,
            TypeName(
                named.Namespace.ToString(),
                [.. named.Segments.Select(static segment =>
                    segment.ToString())]));
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

    private static bool ContainsGenericParameter(
        MetadataTypeIdentity identity) =>
        identity switch
        {
            MetadataTypeIdentity.GenericParameter => true,
            MetadataTypeIdentity.GenericInstance generic =>
                generic.Arguments.Any(ContainsGenericParameter),
            MetadataTypeIdentity.SzArray array =>
                ContainsGenericParameter(array.Element),
            MetadataTypeIdentity.Array array =>
                ContainsGenericParameter(array.Element),
            MetadataTypeIdentity.Pointer pointer =>
                ContainsGenericParameter(pointer.Element),
            MetadataTypeIdentity.ByReference byReference =>
                ContainsGenericParameter(byReference.Element),
            MetadataTypeIdentity.Modified modified =>
                ContainsGenericParameter(modified.Modifier)
                || ContainsGenericParameter(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                ContainsGenericParameter(pinned.Type),
            _ => false,
        };

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

    private static byte[] BuildDuplicateAssemblyReferenceImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ReferencePopulation.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ReferencePopulation"),
            new Version(1, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("Sample.First"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("sample.first"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("Sample.Second"),
            new Version(1, 0),
            default,
            default,
            default,
            default);
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
