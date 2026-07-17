using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

public class MetadataRelationshipTraversalTests
{
    const string WorkerVariable = "DOTNET_INSPECT_RELATIONSHIP_TRAVERSAL_WORKER";

    [Fact]
    public void TypeDefinitionChain_CompletesOutermostToLeaf()
    {
        TypeDefinitionHandle outer = default;
        TypeDefinitionHandle middle = default;
        TypeDefinitionHandle leaf = default;
        using var image = BuildMetadata(metadata =>
        {
            outer = AddTypeDefinition(metadata, TypeAttributes.Public, "N", "Outer");
            middle = AddTypeDefinition(metadata, TypeAttributes.NestedPublic, "", "Middle");
            leaf = AddTypeDefinition(metadata, TypeAttributes.NestedPublic, "", "Leaf");
            metadata.AddNestedType(middle, outer);
            metadata.AddNestedType(leaf, middle);
        });

        var completed = Assert.IsType<
            RelationshipTraversalResult<RelationshipChain<TypeDefinitionHandle>>.Completed>(
                MetadataRelationshipTraversal.WalkTypeDefinitionDeclaringChain(
                    image.Reader,
                    leaf));

        Assert.Equal([outer, middle, leaf], completed.Value.Handles);
        Assert.True(completed.Value.Terminal.IsNil);
        Assert.Equal(3, completed.ConsumedNodes);
        Assert.Equal(
            "N.Outer.Middle.Leaf",
            TypeResolver.ResolveTypeNameFromDefinition(image.Reader, leaf).GetValueOrThrow());
    }

    [Fact]
    public void TypeReferenceChain_CompletesOutermostToLeaf()
    {
        TypeReferenceHandle outer = default;
        TypeReferenceHandle middle = default;
        TypeReferenceHandle leaf = default;
        AssemblyReferenceHandle assembly = default;
        using var image = BuildMetadata(metadata =>
        {
            assembly = AddAssemblyReference(metadata);
            outer = metadata.AddTypeReference(
                assembly,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"));
            middle = metadata.AddTypeReference(
                outer,
                default,
                metadata.GetOrAddString("Middle"));
            leaf = metadata.AddTypeReference(
                middle,
                default,
                metadata.GetOrAddString("Leaf"));
        });

        var completed = Assert.IsType<
            RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>.Completed>(
                MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(
                    image.Reader,
                    leaf));

        Assert.Equal([outer, middle, leaf], completed.Value.Handles);
        Assert.Equal((EntityHandle)assembly, completed.Value.Terminal);
        Assert.Equal(
            "N.Outer.Middle.Leaf",
            image.Reader.ResolveFullTypeName(leaf).GetValueOrThrow());
    }

    [Fact]
    public void ExportedTypeChain_CompletesOutermostToLeaf()
    {
        ExportedTypeHandle outer = default;
        ExportedTypeHandle middle = default;
        ExportedTypeHandle leaf = default;
        AssemblyReferenceHandle assembly = default;
        using var image = BuildMetadata(metadata =>
        {
            assembly = AddAssemblyReference(metadata);
            outer = metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"),
                assembly,
                typeDefinitionId: 0);
            middle = metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Middle"),
                outer,
                typeDefinitionId: 0);
            leaf = metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("Leaf"),
                middle,
                typeDefinitionId: 0);
        });

        var completed = Assert.IsType<
            RelationshipTraversalResult<RelationshipChain<ExportedTypeHandle>>.Completed>(
                MetadataRelationshipTraversal.WalkExportedTypeImplementationChain(
                    image.Reader,
                    leaf));

        Assert.Equal([outer, middle, leaf], completed.Value.Handles);
        Assert.Equal((EntityHandle)assembly, completed.Value.Terminal);
        Assert.Equal(
            "N.Outer",
            image.Reader.GetFullTypeName(image.Reader.GetExportedType(outer)));
        Assert.Equal(
            "N.Outer.Middle.Leaf",
            image.Reader.ResolveFullTypeName(leaf).GetValueOrThrow());
    }

    [Fact]
    public void DeepAcyclicTypeReferenceChain_EnforcesExactNodeCeiling()
    {
        TypeReferenceHandle atCeiling = default;
        TypeReferenceHandle overCeiling = default;
        using var image = BuildMetadata(metadata =>
        {
            EntityHandle scope = AddAssemblyReference(metadata);
            for (int i = 1; i <= MetadataSafetyPolicy.MaxRelationshipNodes + 1; i++)
            {
                var handle = metadata.AddTypeReference(
                    scope,
                    i == 1 ? metadata.GetOrAddString("N") : default,
                    metadata.GetOrAddString($"T{i}"));
                scope = handle;
                if (i == MetadataSafetyPolicy.MaxRelationshipNodes)
                    atCeiling = handle;
                else if (i == MetadataSafetyPolicy.MaxRelationshipNodes + 1)
                    overCeiling = handle;
            }
        });

        var completed = Assert.IsType<
            RelationshipTraversalResult<RelationshipChain<TypeReferenceHandle>>.Completed>(
                MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(
                    image.Reader,
                    atCeiling));
        Assert.Equal(MetadataSafetyPolicy.MaxRelationshipNodes, completed.ConsumedNodes);
        var completedValue = Assert.IsType<RelationshipTraversalResult<string>.Completed>(
            TypeResolver.ResolveFullName(
                image.Reader,
                image.Reader.GetTypeReference(atCeiling)));
        Assert.Equal(MetadataSafetyPolicy.MaxRelationshipNodes, completedValue.ConsumedNodes);

        AssertRejected(
            MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(
                image.Reader,
                overCeiling),
            RelationshipTraversalRejectionKind.NodeBudget,
            MetadataSafetyPolicy.MaxRelationshipNodes);
        AssertRejected(
            TypeResolver.ResolveFullName(
                image.Reader,
                image.Reader.GetTypeReference(overCeiling)),
            RelationshipTraversalRejectionKind.NodeBudget,
            MetadataSafetyPolicy.MaxRelationshipNodes);
        Assert.Throws<BadImageFormatException>(
            () => image.Reader.GetFullTypeName(
                image.Reader.GetTypeReference(overCeiling)));
    }

    [Fact]
    public void MalformedTypeReferenceHandle_IsRejected()
    {
        TypeReferenceHandle handle = default;
        using var image = BuildMetadata(metadata =>
        {
            handle = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(2),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Broken"));
        });

        AssertRejected(
            MetadataRelationshipTraversal.WalkTypeReferenceResolutionScope(
                image.Reader,
                handle),
            RelationshipTraversalRejectionKind.MalformedMetadata,
            consumedNodes: 2);
    }

    [Fact]
    public void CyclicRelationshipFunnels_AreContainedInChildProcess()
        => RunWorker(nameof(CyclicRelationshipFunnelsWorker));

    [Fact]
    public void MultiRowCyclicRelationshipFunnels_AreContainedInChildProcess()
        => RunWorker(nameof(MultiRowCyclicRelationshipFunnelsWorker));

    [Fact]
    public void CyclicRelationshipFunnelsWorker()
    {
        if (!IsSelectedWorker(nameof(CyclicRelationshipFunnelsWorker)))
            return;

        TypeDefinitionHandle typeDefinition = default;
        using (var image = BuildMetadata(metadata =>
        {
            typeDefinition = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "N",
                "TypeDefLoop");
            metadata.AddNestedType(typeDefinition, typeDefinition);
        }))
        {
            AssertRejected(
                image.Reader.ResolveFullTypeName(typeDefinition),
                RelationshipTraversalRejectionKind.Cycle,
                consumedNodes: 1);
            Assert.Throws<BadImageFormatException>(
                () => image.Reader.GetFullTypeName(
                    image.Reader.GetTypeDefinition(typeDefinition)));
        }

        TypeReferenceHandle typeReference = default;
        using (var image = BuildMetadata(metadata =>
        {
            typeReference = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("TypeRefLoop"));
        }))
        {
            AssertRejected(
                image.Reader.ResolveFullTypeName(typeReference),
                RelationshipTraversalRejectionKind.Cycle,
                consumedNodes: 1);
            Assert.Throws<BadImageFormatException>(
                () => image.Reader.GetFullTypeName(
                    image.Reader.GetTypeReference(typeReference)));
        }

        ExportedTypeHandle exportedType = default;
        using (var image = BuildMetadata(metadata =>
        {
            exportedType = metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("ExportedTypeLoop"),
                MetadataTokens.ExportedTypeHandle(1),
                typeDefinitionId: 0);
        }))
        {
            AssertRejected(
                image.Reader.ResolveFullTypeName(exportedType),
                RelationshipTraversalRejectionKind.Cycle,
                consumedNodes: 1);
            Assert.Throws<BadImageFormatException>(
                () => image.Reader.GetFullTypeName(
                    image.Reader.GetExportedType(exportedType)));
        }
    }

    [Fact]
    public void MultiRowCyclicRelationshipFunnelsWorker()
    {
        if (!IsSelectedWorker(nameof(MultiRowCyclicRelationshipFunnelsWorker)))
            return;

        TypeDefinitionHandle firstTypeDefinition = default;
        using (var image = BuildMetadata(metadata =>
        {
            firstTypeDefinition = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "N",
                "FirstTypeDef");
            var second = AddTypeDefinition(
                metadata,
                TypeAttributes.NestedPublic,
                "",
                "SecondTypeDef");
            metadata.AddNestedType(firstTypeDefinition, second);
            metadata.AddNestedType(second, firstTypeDefinition);
        }))
        {
            AssertRejected(
                image.Reader.ResolveFullTypeName(firstTypeDefinition),
                RelationshipTraversalRejectionKind.Cycle,
                consumedNodes: 2);
            Assert.Throws<BadImageFormatException>(
                () => TypeResolver.GetTypeNameFromDefinition(
                    image.Reader,
                    firstTypeDefinition));
        }

        TypeReferenceHandle firstTypeReference = default;
        using (var image = BuildMetadata(metadata =>
        {
            firstTypeReference = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(2),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("FirstTypeRef"));
            metadata.AddTypeReference(
                firstTypeReference,
                default,
                metadata.GetOrAddString("SecondTypeRef"));
        }))
        {
            AssertRejected(
                image.Reader.ResolveFullTypeName(firstTypeReference),
                RelationshipTraversalRejectionKind.Cycle,
                consumedNodes: 2);
            Assert.Throws<BadImageFormatException>(
                () => TypeResolver.GetTypeNameFromReference(
                    image.Reader,
                    firstTypeReference));
        }

        ExportedTypeHandle firstExportedType = default;
        using (var image = BuildMetadata(metadata =>
        {
            firstExportedType = metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("FirstExportedType"),
                MetadataTokens.ExportedTypeHandle(2),
                typeDefinitionId: 0);
            metadata.AddExportedType(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString("SecondExportedType"),
                firstExportedType,
                typeDefinitionId: 0);
        }))
        {
            AssertRejected(
                image.Reader.ResolveFullTypeName(firstExportedType),
                RelationshipTraversalRejectionKind.Cycle,
                consumedNodes: 2);
            Assert.Throws<BadImageFormatException>(
                () => TypeResolver.GetTypeNameFromExportedType(
                    image.Reader,
                    firstExportedType));
        }
    }

    static void AssertRejected<T>(
        RelationshipTraversalResult<T> result,
        RelationshipTraversalRejectionKind kind,
        int consumedNodes)
        where T : notnull
    {
        var rejected = Assert.IsType<RelationshipTraversalResult<T>.Rejected>(result);
        Assert.Equal(kind, rejected.Rejection.Kind);
        Assert.Equal(consumedNodes, rejected.Rejection.ConsumedNodes);
    }

    static bool IsSelectedWorker(string methodName)
        => Environment.GetEnvironmentVariable(WorkerVariable) == methodName;

    static void RunWorker(string workerMethod)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(MetadataRelationshipTraversalTests).Assembly.Location);
        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add($"*{workerMethod}*");
        startInfo.Environment[WorkerVariable] = workerMethod;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        bool exited = process.WaitForExit(30_000);
        if (!exited)
            process.Kill(entireProcessTree: true);
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();

        Assert.True(exited, $"Child worker {workerMethod} timed out.");
        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\n"
            + $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
    }

    static TypeDefinitionHandle AddTypeDefinition(
        MetadataBuilder metadata,
        TypeAttributes attributes,
        string ns,
        string name)
        => metadata.AddTypeDefinition(
            attributes,
            ns.Length == 0 ? default : metadata.GetOrAddString(ns),
            metadata.GetOrAddString(name),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static AssemblyReferenceHandle AddAssemblyReference(MetadataBuilder metadata)
        => metadata.AddAssemblyReference(
            metadata.GetOrAddString("Reference"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);

    static MetadataImage BuildMetadata(Action<MetadataBuilder> addRows)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        AddTypeDefinition(metadata, default, "", "<Module>");
        addRows(metadata);

        var rootBuilder = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        rootBuilder.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return new MetadataImage(image.ToImmutableArray());
    }

    sealed class MetadataImage(ImmutableArray<byte> image) : IDisposable
    {
        readonly MetadataReaderProvider provider =
            MetadataReaderProvider.FromMetadataImage(image);

        public MetadataReader Reader => provider.GetMetadataReader();

        public void Dispose() => provider.Dispose();
    }
}
