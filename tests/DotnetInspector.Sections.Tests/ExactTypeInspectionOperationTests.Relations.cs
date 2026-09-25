using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Queries;
using QuerySpace;
using QuerySpace.Operations;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task RelationsExecuteThroughQuerySpaceWithExactCountAndRows()
    {
        byte[] assembly = BuildHierarchyAssembly();
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", assembly));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                "interface"),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Relations.IContract"),
                LoadOptions(client, store),
                plan,
                new SubjectRelationPopulationCountRequest(),
                new SubjectRelationPopulationRowsRequest(10),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        Assert.Equal(
            2,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    available.Relations.Population.Count).Value);
        var rows = Assert.IsType<
            SubjectRelationPopulationRowsOutcome.Read>(
                available.Relations.Population.Rows);
        Assert.Equal(2, rows.Items.Length);
        Assert.All(
            rows.Items,
            row => Assert.Equal(
                SubjectRelationForm.Interface,
                row.Form));
        Assert.True(
            available.Relations.Relations.Evidence.IsComplete);
    }

    [Theory]
    [InlineData("Relations.IGeneric`1", "interface")]
    [InlineData("Relations.Base", "base-type")]
    public async Task RelationsMatchDefinitionLevelHierarchyTargets(
        string type,
        string form)
    {
        var store = await CachedStoreAsync(
            ("lib/net11.0/Hierarchy.dll", BuildHierarchyAssembly()));
        using var client = new HttpClient(new FailingHandler());
        SubjectRelationsQueryPlan plan = Assert.IsType<
            SubjectRelationsQueryPlanResult.Accepted>(
                SubjectRelationsQuery.ResolveIntent(
                    SubjectRelationsRouteKind.Type,
                    PortableQueryIntent.Create(
                        [
                            new(
                                SubjectRelationsQuery.DirectionTermKey,
                                PortableQueryOperator.Equal,
                                "incoming"),
                            new(
                                SubjectRelationsQuery.FormTermKey,
                                PortableQueryOperator.Equal,
                                form),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken)).Plan;

        ExactTypeRelationsInspectionOutcome outcome =
            await ExactTypeRelationsInspectionOperation.ExecuteAsync(
                new ExactTypeInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    type),
                LoadOptions(client, store),
                plan,
                new SubjectRelationPopulationCountRequest(),
                new SubjectRelationPopulationRowsRequest(10),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available = Assert.IsType<
            ExactTypeRelationsInspectionOutcome.Available>(outcome);
        Assert.Equal(
            1,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    available.Relations.Population.Count).Value);
        Assert.Single(
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                available.Relations.Population.Rows).Items);
    }

    static byte[] BuildHierarchyAssembly()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Hierarchy.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Hierarchy"),
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
        TypeDefinitionHandle contract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("IContract"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle first =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("First"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle second =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("Second"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddInterfaceImplementation(first, contract);
        metadata.AddInterfaceImplementation(second, contract);
        TypeDefinitionHandle genericContract =
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("IGeneric`1"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            genericContract,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        TypeDefinitionHandle genericImplementation =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("GenericImplementation"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        var genericSignature = new BlobBuilder();
        genericSignature.WriteByte(0x15);
        genericSignature.WriteByte(0x12);
        genericSignature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(genericContract) << 2);
        genericSignature.WriteCompressedInteger(1);
        genericSignature.WriteByte(0x08);
        TypeSpecificationHandle constructedContract =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(genericSignature));
        metadata.AddInterfaceImplementation(
            genericImplementation,
            constructedContract);
        TypeDefinitionHandle baseType =
            metadata.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Abstract,
                metadata.GetOrAddString("Relations"),
                metadata.GetOrAddString("Base"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Relations"),
            metadata.GetOrAddString("Derived"),
            baseType,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
