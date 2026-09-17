using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;

namespace ILInspector.Metadata.Tests;

public sealed class PublicMethodRootInventoryTests
{
    static readonly PublicMethodRootInventoryLimits FullLimits =
        new(
            MaximumTypeDefinitions: 1_000,
            MaximumMethodDefinitions: 10_000,
            MaximumRoots: 10_000);

    [Fact]
    public void Read_ReturnsEveryExactPublicMethodDef()
    {
        using FixtureImage fixture = OpenFixture();

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits);

        Assert.True(inventory.IsComplete);
        Assert.Null(inventory.Boundary);
        Assert.Equal(
            inventory.Roots
                .OrderBy(static root => root.Token),
            inventory.Roots);
        Assert.All(
            inventory.Roots,
            root => Assert.Equal(
                inventory.ModuleVersionId,
                root.ModuleVersionId));
        Assert.Equal(
            [
                "IPublicContract.InterfaceMethod",
                "PublicAbstract.Bodiless",
                "PublicNested..ctor",
                "PublicNested.PublicNestedMethod",
                "PublicTopLevel..ctor",
                "PublicTopLevel.CompilerGeneratedPublic",
                "PublicTopLevel.HiddenByPresentation",
                "PublicTopLevel.PublicMethod",
                "PublicTopLevel.add_Changed",
                "PublicTopLevel.get_Value",
                "PublicTopLevel.op_Addition",
                "PublicTopLevel.remove_Changed",
            ],
            DisplayRoots(fixture.Reader, inventory)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Read_RequiresPublicDeclaringTypeChain()
    {
        using FixtureImage fixture = OpenFixture();

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits);
        string[] roots = DisplayRoots(
            fixture.Reader,
            inventory);

        Assert.DoesNotContain(
            roots,
            root => root.Contains(
                "HiddenByInternal",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.Contains(
                "HiddenByProtected",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.Contains(
                "HiddenByPrivate",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".ProtectedMethod",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".InternalMethod",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".PrivateMethod",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            roots,
            root => root.EndsWith(
                ".set_Value",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Read_DoesNotApplyPresentationFilters()
    {
        using FixtureImage fixture = OpenFixture();

        string[] roots = DisplayRoots(
            fixture.Reader,
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits));

        Assert.Contains(
            "PublicTopLevel.HiddenByPresentation",
            roots);
        Assert.Contains(
            "PublicTopLevel.CompilerGeneratedPublic",
            roots);
    }

    [Fact]
    public void Read_ReportsIndependentExactCapacityBounds()
    {
        using FixtureImage fixture = OpenFixture();
        PublicMethodRootInventory full =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                FullLimits);

        PublicMethodRootInventory exact =
            PublicMethodRootInventoryReader.Read(
                fixture.Reader,
                new(
                    full.Receipt.VisitedTypeDefinitions,
                    full.Receipt.VisitedMethodDefinitions,
                    full.Receipt.RetainedRoots));
        Assert.True(exact.IsComplete);
        Assert.Equal(full.Roots, exact.Roots);

        AssertBound(
            fixture.Reader,
            full,
            new(
                full.Receipt.VisitedTypeDefinitions - 1,
                FullLimits.MaximumMethodDefinitions,
                FullLimits.MaximumRoots),
            PublicMethodRootInventoryLimit.TypeDefinitions);
        AssertBound(
            fixture.Reader,
            full,
            new(
                FullLimits.MaximumTypeDefinitions,
                full.Receipt.VisitedMethodDefinitions - 1,
                FullLimits.MaximumRoots),
            PublicMethodRootInventoryLimit.MethodDefinitions);
        AssertBound(
            fixture.Reader,
            full,
            new(
                FullLimits.MaximumTypeDefinitions,
                FullLimits.MaximumMethodDefinitions,
                full.Receipt.RetainedRoots - 1),
            PublicMethodRootInventoryLimit.Roots);
    }

    [Fact]
    public void Read_ReorderedMethodPtrPreservesMethodDefTokenOrder()
    {
        using var image = new PEReader(
            ImmutableArray.Create(
                MetadataMethodPtrFixture.Build(2, 1)));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);
        var limits =
            new PublicMethodRootInventoryLimits(10, 10, 10);

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                reader,
                limits);

        Assert.True(inventory.IsComplete);
        Assert.Equal(
            [0x06000001, 0x06000002],
            inventory.Roots.Select(static root => root.Token));

        PublicMethodRootInventory bounded =
            PublicMethodRootInventoryReader.Read(
                reader,
                limits with { MaximumRoots = 1 });
        Assert.False(bounded.IsComplete);
        Assert.Equal(
            PublicMethodRootInventoryLimit.Roots,
            bounded.Boundary?.Limit);
        Assert.Equal(
            [0x06000001],
            bounded.Roots.Select(static root => root.Token));

        PublicMethodRootInventory methodBounded =
            PublicMethodRootInventoryReader.Read(
                reader,
                limits with
                {
                    MaximumMethodDefinitions = 1,
                });
        Assert.False(methodBounded.IsComplete);
        Assert.Equal(
            PublicMethodRootInventoryLimit.MethodDefinitions,
            methodBounded.Boundary?.Limit);
        Assert.Equal(
            1,
            methodBounded.Receipt.VisitedMethodDefinitions);
        Assert.Empty(methodBounded.Roots);
    }

    [Fact]
    public void Read_OrphanedNestedPublicTypeFailsVisibly()
    {
        using var image = new PEReader(
            ImmutableArray.Create(
                BuildMalformedVisibilityImage(
                    TypeAttributes.NestedPublic,
                    addDeclaringType: false)));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);

        Assert.Throws<BadImageFormatException>(
            () => PublicMethodRootInventoryReader.Read(
                reader,
                FullLimits));
    }

    [Theory]
    [InlineData(TypeAttributes.NestedPrivate, false)]
    [InlineData(TypeAttributes.NotPublic, true)]
    public void Read_ContradictoryNonPublicNestingFailsVisibly(
        TypeAttributes visibility,
        bool addDeclaringType)
    {
        using var image = new PEReader(
            ImmutableArray.Create(
                BuildMalformedVisibilityImage(
                    visibility,
                    addDeclaringType)));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);

        Assert.Throws<BadImageFormatException>(
            () => PublicMethodRootInventoryReader.Read(
                reader,
                FullLimits));
    }

    [Fact]
    public void Read_DeepPublicNestingCompletes()
    {
        const int Depth = 512;
        using var image = new PEReader(
            ImmutableArray.Create(
                BuildDeepNestedPublicImage(Depth)));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                reader,
                new(Depth + 2, 1, 1));

        Assert.True(inventory.IsComplete);
        Assert.Equal(Depth + 2, inventory.Receipt.VisitedTypeDefinitions);
        Assert.Equal(1, inventory.Receipt.VisitedMethodDefinitions);
        Assert.Equal(
            [0x06000001],
            inventory.Roots.Select(static root => root.Token));
    }

    [Fact]
    public void Read_ParentAfterChildNestingCompletes()
    {
        using var image = new PEReader(
            ImmutableArray.Create(
                BuildParentAfterChildImage()));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);

        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                reader,
                FullLimits);

        Assert.True(inventory.IsComplete);
        Assert.Empty(inventory.Roots);
    }

    [Fact]
    public void Read_NestedTypeCycleFailsVisibly()
    {
        using var image = new PEReader(
            ImmutableArray.Create(
                BuildNestedTypeCycleImage()));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);

        Assert.Throws<BadImageFormatException>(
            () => PublicMethodRootInventoryReader.Read(
                reader,
                FullLimits));
    }

    [Fact]
    public void Read_InvalidMethodAccessFailsInCompleteAndBoundedScans()
    {
        using (var completeImage = new PEReader(
            ImmutableArray.Create(
                BuildInvalidMethodAccessImage(
                    malformedMethodRow: 1))))
        {
            MetadataReader reader =
                MetadataFormatAdmission.GetMetadataReader(
                    completeImage);
            Assert.Throws<BadImageFormatException>(
                () => PublicMethodRootInventoryReader.Read(
                    reader,
                    FullLimits));
        }

        using var boundedImage = new PEReader(
            ImmutableArray.Create(
                BuildInvalidMethodAccessImage(
                    malformedMethodRow: 2)));
        MetadataReader boundedReader =
            MetadataFormatAdmission.GetMetadataReader(
                boundedImage);
        Assert.Throws<BadImageFormatException>(
            () => PublicMethodRootInventoryReader.Read(
                boundedReader,
                FullLimits with
                {
                    MaximumMethodDefinitions = 1,
                }));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("aliased")]
    [InlineData("out-of-range")]
    [InlineData("descending")]
    [InlineData("uncovered")]
    [InlineData("count-mismatch")]
    public void Read_MalformedMethodOwnershipFailsVisibly(
        string shape)
    {
        byte[] bytes = shape switch
        {
            "duplicate" =>
                MetadataMethodPtrFixture.BuildDuplicate(),
            "aliased" =>
                MetadataMethodPtrFixture.BuildAliased(),
            "out-of-range" =>
                MetadataMethodPtrFixture.BuildOutOfRange(),
            "descending" =>
                MetadataMethodPtrFixture.BuildDescending(),
            "uncovered" =>
                MetadataMethodPtrFixture.BuildUncovered(),
            "count-mismatch" =>
                MetadataMethodPtrFixture.BuildCountMismatch(),
            _ => throw new UnreachableException(),
        };
        using var image =
            new PEReader(ImmutableArray.Create(bytes));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(image);

        Assert.Throws<BadImageFormatException>(
            () => PublicMethodRootInventoryReader.Read(
                reader,
                FullLimits));
    }

    static void AssertBound(
        MetadataReader reader,
        PublicMethodRootInventory full,
        PublicMethodRootInventoryLimits limits,
        PublicMethodRootInventoryLimit expected)
    {
        PublicMethodRootInventory inventory =
            PublicMethodRootInventoryReader.Read(
                reader,
                limits);

        Assert.False(inventory.IsComplete);
        Assert.Equal(expected, inventory.Boundary?.Limit);
        Assert.Equal(
            expected switch
            {
                PublicMethodRootInventoryLimit.TypeDefinitions =>
                    limits.MaximumTypeDefinitions,
                PublicMethodRootInventoryLimit.MethodDefinitions =>
                    limits.MaximumMethodDefinitions,
                PublicMethodRootInventoryLimit.Roots =>
                    limits.MaximumRoots,
                _ => throw new UnreachableException(),
            },
            inventory.Boundary?.Maximum);
        Assert.Equal(
            full.Roots.Take(inventory.Roots.Length),
            inventory.Roots);

        int charged = expected switch
        {
            PublicMethodRootInventoryLimit.TypeDefinitions =>
                inventory.Receipt.VisitedTypeDefinitions,
            PublicMethodRootInventoryLimit.MethodDefinitions =>
                inventory.Receipt.VisitedMethodDefinitions,
            PublicMethodRootInventoryLimit.Roots =>
                inventory.Receipt.RetainedRoots,
            _ => throw new UnreachableException(),
        };
        Assert.Equal(inventory.Boundary?.Maximum, charged);
    }

    static string[] DisplayRoots(
        MetadataReader reader,
        PublicMethodRootInventory inventory) =>
        [
            .. inventory.Roots.Select(root =>
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(root.Handle);
                TypeDefinition type =
                    reader.GetTypeDefinition(
                        method.GetDeclaringType());
                return $"{reader.GetString(type.Name)}."
                    + reader.GetString(method.Name);
            }),
        ];

    static FixtureImage OpenFixture() =>
        new(
            FixtureCatalog.MetadataPublicMethodRoots
                .AssemblyPath());

    static byte[] BuildMalformedVisibilityImage(
        TypeAttributes visibility,
        bool addDeclaringType)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("OrphanedNested.dll"),
            metadata.GetOrAddGuid(
                new Guid("AA250EA5-2BB9-4FD4-AD84-85737B9557AA")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("OrphanedNested"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Root"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        TypeDefinitionHandle outer =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                method);
        TypeDefinitionHandle malformed =
            metadata.AddTypeDefinition(
                visibility,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Malformed"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                method);
        if (addDeclaringType)
        {
            metadata.AddNestedType(malformed, outer);
        }

        return Serialize(metadata);
    }

    static byte[] BuildDeepNestedPublicImage(int depth)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("DeepNested.dll"),
            metadata.GetOrAddGuid(
                new Guid("C7C3D0EF-862D-4AE8-8859-185020193EC6")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("DeepNested"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        MethodDefinitionHandle method =
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Root"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        TypeDefinitionHandle parent =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                method);
        for (int index = 0; index < depth; index++)
        {
            TypeDefinitionHandle nested =
                metadata.AddTypeDefinition(
                    TypeAttributes.NestedPublic,
                    default,
                    metadata.GetOrAddString($"N{index}"),
                    default,
                    MetadataTokens.FieldDefinitionHandle(1),
                    method);
            metadata.AddNestedType(nested, parent);
            parent = nested;
        }

        return Serialize(metadata);
    }

    static byte[] BuildParentAfterChildImage()
    {
        MetadataBuilder metadata =
            CreateRelationshipMetadata("ParentAfterChild");
        TypeDefinitionHandle child =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Child"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle parent =
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Parent"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(child, parent);
        return Serialize(metadata);
    }

    static byte[] BuildNestedTypeCycleImage()
    {
        MetadataBuilder metadata =
            CreateRelationshipMetadata("NestedCycle");
        TypeDefinitionHandle first =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("First"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle second =
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Second"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(first, second);
        metadata.AddNestedType(second, first);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateRelationshipMetadata(
        string name)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] BuildInvalidMethodAccessImage(
        int malformedMethodRow)
    {
        MetadataBuilder metadata =
            CreateRelationshipMetadata("InvalidMethodAccess");
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        BlobHandle signatureHandle =
            metadata.GetOrAddBlob(signature);
        for (int row = 1; row <= 2; row++)
        {
            MethodAttributes access =
                row == malformedMethodRow
                    ? MethodAttributes.MemberAccessMask
                    : MethodAttributes.Public;
            metadata.AddMethodDefinition(
                access | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"M{row}"),
                signatureHandle,
                bodyOffset: 0,
                parameterList:
                    MetadataTokens.ParameterHandle(1));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Owner"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class FixtureImage : IDisposable
    {
        readonly FileStream _stream;
        readonly PEReader _image;

        internal FixtureImage(string path)
        {
            _stream = File.OpenRead(path);
            _image = new PEReader(_stream);
            Reader = _image.GetMetadataReader();
        }

        internal MetadataReader Reader { get; }

        public void Dispose()
        {
            _image.Dispose();
            _stream.Dispose();
        }
    }
}
