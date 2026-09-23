using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using NuGetFetch;

namespace ILInspector.Metadata.Tests;

public sealed partial class AssemblyTypeDeclarationInventoryTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void BorrowedInventory_ReusesOwnerAndSurvivesDisposal()
    {
        byte[] image = BuildImage(metadata =>
        {
            DiscoveryAttributeConstructors attributes =
                AddDiscoveryAttributeConstructors(metadata);
            TypeDefinitionHandle type =
                AddDefinition(metadata, TypeAttributes.Public, "N", "Type");
            AddEditorBrowsable(metadata, type, attributes.EditorBrowsable, 1);
        });
        int opens = 0;
        var descriptor = Descriptor(image, () =>
        {
            opens++;
            return new MemoryStream(image, writable: false);
        });
        AssemblyTypeDeclarationInventory inventory;
        using (var context = PdbContext.OpenMetadataOnly(descriptor))
        {
            using var session = AssemblyInspectionSession.Borrow(context);
            inventory = Read(session);
            Assert.Equal(descriptor.Identity, inventory.Identity);
            AssemblyTypeDeclaration declaration =
                Assert.Single(inventory.GetDeclarations());
            Assert.Equal("Type", declaration.Name.Segments[0]);
            Assert.Equal(
                new TypeDeclarationDiscoveryAttributes(
                    IsEditorBrowsableNever: true,
                    IsObsolete: false),
                declaration.DiscoveryAttributes);

            session.Dispose();
            Assert.True(context.HasMetadata);
            Assert.Throws<ObjectDisposedException>(() => session.TypeDeclarations());
        }

        Assert.Equal(1, opens);
        AssemblyTypeDeclaration detached = Assert.Single(inventory.GetDeclarations());
        Assert.Equal(Name("N", "Type"), detached.Name);
        Assert.True(detached.DiscoveryAttributes!.IsEditorBrowsableNever);
        Assert.Equal(
            AssemblyTypeDeclarationKind.Definition,
            Assert.Single(inventory.Declarations).Kind);
        Assert.Equal(
            AssemblyTypeDefinitionKind.Class,
            Assert.Single(inventory.Declarations).DefinitionKind);
    }

    [Fact]
    public void Definitions_RetainHighLevelTypeKinds()
    {
        byte[] image = BuildImage(metadata =>
        {
            AssemblyReferenceHandle core =
                AddReference(metadata, "System.Runtime");
            TypeReferenceHandle Base(string name) =>
                metadata.AddTypeReference(
                    core,
                    metadata.GetOrAddString("System"),
                    metadata.GetOrAddString(name));

            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "N",
                "Class",
                Base("Object"));
            AddDefinition(
                metadata,
                TypeAttributes.Public | TypeAttributes.Interface,
                "N",
                "Interface");
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "N",
                "Struct",
                Base("ValueType"));
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "N",
                "Enum",
                Base("Enum"));
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "N",
                "Delegate",
                Base("MulticastDelegate"));
        });
        using var session =
            AssemblyInspectionSession.Open(Descriptor(image));
        Dictionary<string, AssemblyTypeDefinitionKind?> kinds =
            Read(session).Declarations.ToDictionary(
                static declaration => declaration.Name.Segments[0],
                static declaration => declaration.DefinitionKind);

        Assert.Equal(AssemblyTypeDefinitionKind.Class, kinds["Class"]);
        Assert.Equal(
            AssemblyTypeDefinitionKind.Interface,
            kinds["Interface"]);
        Assert.Equal(
            AssemblyTypeDefinitionKind.ValueType,
            kinds["Struct"]);
        Assert.Equal(AssemblyTypeDefinitionKind.Enum, kinds["Enum"]);
        Assert.Equal(
            AssemblyTypeDefinitionKind.Delegate,
            kinds["Delegate"]);
    }

    [Fact]
    public void RetainedDeclarationBound_StopsBeforeReturningPartialInventory()
    {
        byte[] image = BuildImage(metadata =>
        {
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "N",
                "First");
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "N",
                "Second");
        });
        using var session =
            AssemblyInspectionSession.Open(Descriptor(image));

        var incomplete =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Incomplete>(
                session.TypeDeclarations(
                    maximumRetainedDeclarations: 1));

        Assert.Equal(
            AssemblyTypeDeclarationInventoryBound.RetainedDeclarations,
            incomplete.Bound);
        Assert.Equal(2, incomplete.MeasuredDeclarations);
        Assert.True(incomplete.MeasuredRetainedTextCharacters > 0);
        Assert.Equal(
            2,
            Read(session).Declarations.Length);
    }

    [Fact]
    public void
        RetainedTextBound_StopsBeforeReturningPartialInventory()
    {
        byte[] image = BuildImage(metadata =>
        {
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "Namespace",
                "First");
            AddDefinition(
                metadata,
                TypeAttributes.Public,
                "Namespace",
                "Second");
        });
        using var session =
            AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory complete = Read(session);

        var incomplete =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Incomplete>(
                session.TypeDeclarations(
                    maximumRetainedDeclarations: int.MaxValue,
                    maximumRetainedTextCharacters:
                        checked((int)complete.RetainedTextCharacters - 1)));

        Assert.Equal(
            AssemblyTypeDeclarationInventoryBound
                .RetainedTextCharacters,
            incomplete.Bound);
        Assert.True(
            incomplete.MeasuredRetainedTextCharacters
                > complete.RetainedTextCharacters - 1);
        Assert.True(incomplete.MeasuredDeclarations > 0);
        Assert.Equal(
            complete.RetainedTextCharacters,
            Read(session).RetainedTextCharacters);
    }

    [Fact]
    public void BorrowedInventory_RejectsUseAfterLenderDisposal()
    {
        byte[] image = BuildImage(_ => { });
        using var context = PdbContext.OpenMetadataOnly(Descriptor(image));
        using var session = AssemblyInspectionSession.Borrow(context);
        _ = Read(session);
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.TypeDeclarations());
    }

    [Fact]
    public void PublicAndAllViews_PreserveNestedVisibilityAndExcludeModuleRow()
    {
        byte[] image = BuildImage(metadata =>
        {
            var outer = AddDefinition(metadata, TypeAttributes.Public, "N", "Outer`1");
            var inner = AddDefinition(metadata, TypeAttributes.NestedPublic, "", "Inner`2");
            metadata.AddNestedType(inner, outer);
            var hidden = AddDefinition(metadata, TypeAttributes.NotPublic, "N", "Hidden");
            var hiddenChild = AddDefinition(metadata, TypeAttributes.NestedPublic, "", "Child");
            metadata.AddNestedType(hiddenChild, hidden);
            var privateChild = AddDefinition(metadata, TypeAttributes.NestedPrivate, "", "Private");
            metadata.AddNestedType(privateChild, outer);
            var protectedChild = AddDefinition(metadata, TypeAttributes.NestedFamily, "", "Protected");
            metadata.AddNestedType(protectedChild, outer);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.Equal(
            [Name("N", "Outer`1"), Name("N", "Outer`1", "Inner`2")],
            inventory.GetDeclarations().Select(declaration => declaration.Name));
        Assert.Equal(6, inventory.GetDeclarations(includeAll: true).Count());
        Assert.DoesNotContain(inventory.Declarations, declaration => declaration.Name == Name("", "<Module>"));
        Assert.Contains(Name("", "<Module>"), inventory.Definitions);
        AssemblyTypeDeclaration hiddenChild =
            inventory.Declarations.Single(
                declaration =>
                    declaration.Name
                    == Name("N", "Hidden", "Child"));
        Assert.True(hiddenChild.IsDefinitionPublic);
        Assert.False(hiddenChild.IsPublicSurface);
        Assert.False(
            inventory.Declarations.Single(
                declaration =>
                    declaration.Name
                    == Name("N", "Outer`1", "Private"))
                .IsDefinitionPublic);
    }

    [Fact]
    public void EmptyInventory_IsACompleteEmptyDeclarationSet()
    {
        using var session = AssemblyInspectionSession.Open(Descriptor(BuildImage(_ => { })));
        Assert.Empty(Read(session).Declarations);
    }

    [Fact]
    public void NativeImage_IsRejectedRatherThanACompleteEmptyInventory()
    {
        byte[] native = InspectionAcquisitionPlanTests.BuildNativePeImage();
        var descriptor = ResolvedAssemblyReference.Create(
            Descriptor(BuildImage(_ => { })).Identity, path: null,
            () => new MemoryStream(native, writable: false),
            AssemblyResolutionProvenance.Local("native-inventory-fixture"));
        using var session = AssemblyInspectionSession.Open(descriptor);

        var rejected = Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
            session.TypeDeclarations());
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
    }

    [Fact]
    public void NestedForwarders_AreAdvertisedWithoutTargetResolutionOrVisibilityBits()
    {
        byte[] image = BuildImage(metadata =>
        {
            var root = metadata.AddExportedType(
                Forwarder, metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer`1"),
                AddReference(metadata, "NotAcquired"), 0);
            metadata.AddExportedType(
                0, default, metadata.GetOrAddString("Inner`2"), root, 0);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.Equal(2, inventory.GetDeclarations().Count());
        Assert.All(inventory.Declarations, declaration =>
        {
            Assert.Equal(AssemblyTypeDeclarationKind.Forwarder, declaration.Kind);
            Assert.Null(declaration.DefinitionKind);
            Assert.True(declaration.IsPublicSurface);
            Assert.Null(declaration.DiscoveryAttributes);
        });
        Assert.Equal(
            [Name("N", "Outer`1"), Name("N", "Outer`1", "Inner`2")],
            inventory.Forwarders);
    }

    [Fact]
    public void ModuleExports_AreExplicitRatherThanDroppedOrRelabeled()
    {
        byte[] image = BuildImage(metadata =>
        {
            var file = metadata.AddAssemblyFile(
                metadata.GetOrAddString("Other.netmodule"), default, containsMetadata: true);
            var root = metadata.AddExportedType(
                TypeAttributes.Public, metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Outer"), file, 1);
            metadata.AddExportedType(
                TypeAttributes.NestedPublic, default,
                metadata.GetOrAddString("Inner"), root, 2);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session);

        Assert.Empty(inventory.Forwarders);
        Assert.Equal(2, inventory.GetDeclarations().Count());
        Assert.All(inventory.Declarations, declaration =>
        {
            Assert.Equal(
                AssemblyTypeDeclarationKind.ModuleExport,
                declaration.Kind);
            Assert.Null(declaration.DefinitionKind);
            Assert.Null(declaration.DiscoveryAttributes);
        });
        Assert.Equal(Name("N", "Outer", "Inner"), inventory.Declarations[1].Name);
    }

    [Theory]
    [InlineData("DuplicateDefinition")]
    [InlineData("DuplicateForwarder")]
    [InlineData("DefinitionAndForwarder")]
    [InlineData("EmptyDefinition")]
    [InlineData("ExportCycle")]
    [InlineData("UnmarkedForwarder")]
    [InlineData("MissingTarget")]
    [InlineData("UnnamedModule")]
    public void InvalidDeclarations_RejectWholeInventoryOnBothEntryPoints(string scenario)
    {
        byte[] image = BuildImage(metadata =>
        {
            AddDefinition(metadata, TypeAttributes.Public, "N", "Healthy");
            switch (scenario)
            {
                case "DuplicateDefinition":
                    AddDefinition(metadata, TypeAttributes.Public, "N", "Healthy");
                    break;
                case "DuplicateForwarder":
                    var target = AddReference(metadata, "Target");
                    metadata.AddExportedType(Forwarder, default, metadata.GetOrAddString("Same"), target, 0);
                    metadata.AddExportedType(Forwarder, default, metadata.GetOrAddString("Same"), target, 0);
                    break;
                case "DefinitionAndForwarder":
                    metadata.AddExportedType(
                        Forwarder, metadata.GetOrAddString("N"), metadata.GetOrAddString("Healthy"),
                        AddReference(metadata, "Target"), 0);
                    break;
                case "EmptyDefinition":
                    AddDefinition(metadata, TypeAttributes.Public, "N", "");
                    break;
                case "ExportCycle":
                    metadata.AddExportedType(
                        Forwarder, default, metadata.GetOrAddString("Cycle"),
                        MetadataTokens.ExportedTypeHandle(1), 0);
                    break;
                case "UnmarkedForwarder":
                    metadata.AddExportedType(
                        0, default, metadata.GetOrAddString("Unmarked"),
                        AddReference(metadata, "Target"), 0);
                    break;
                case "MissingTarget":
                    metadata.AddExportedType(
                        Forwarder, default, metadata.GetOrAddString("Missing"),
                        MetadataTokens.AssemblyReferenceHandle(1), 0);
                    break;
                case "UnnamedModule":
                    metadata.AddExportedType(
                        TypeAttributes.Public, default, metadata.GetOrAddString("Export"),
                        metadata.AddAssemblyFile(default, default, containsMetadata: true), 1);
                    break;
                default:
                    throw new InvalidOperationException(scenario);
            }
        });
        var descriptor = Descriptor(image);
        using var session = AssemblyInspectionSession.Open(descriptor);
        foreach (var outcome in new[]
        {
            session.TypeDeclarations(),
            AssemblyTypeDeclarationInventoryReader.Read(descriptor),
        })
        {
            var rejected = Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(outcome);
            Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
            Assert.NotEmpty(rejected.Failure.Detail);
        }
    }

    [Theory]
    [InlineData("Outer<T>.Inner<T,U>", true)]
    [InlineData("Outer<T>.Inner<T>", false)]
    [InlineData("Outer`1+Inner`2", true)]
    [InlineData("Inner<T,U>", true)]
    [InlineData("Inner<T>", false)]
    [InlineData("N.Outer*.Inner*", true)]
    [InlineData("Other.Outer*.Inner*", false)]
    public void StructuredPattern_PreservesNestedGenericBoundaries(string pattern, bool expected)
    {
        MetadataTypeDefinitionName name = Name("N", "Outer`1", "Inner`2");
        Assert.Equal(expected, TypeMatcher.MatchesTypeFilter(name, pattern));
        Assert.NotEqual(name, Name("N", "Outer`1", "Inner`1"));
        Assert.NotEqual(name, Name("N", "Outer`1", "inner`2"));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PinnedReferencePack_BorrowedAndDescriptorInventoriesPreserveDeclarations()
    {
        using var http = new HttpClient();
        var client = new NuGetClient(http);
        await using var source = await client.DownloadAsync(
            "Microsoft.NETCore.App.Ref", "10.0.10",
            cancellationToken: TestContext.Current.CancellationToken);
        using var package = new MemoryStream();
        await source.CopyToAsync(package, TestContext.Current.CancellationToken);
        Assert.Equal(
            "995E22BDC4F70DB27044D152E55CF231A9712338E4E391A2B9B497A756A647C3",
            Convert.ToHexString(SHA256.HashData(package.ToArray())));
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        foreach (var (path, kind) in new[]
        {
            ("ref/net10.0/netstandard.dll", AssemblyTypeDeclarationKind.Forwarder),
            ("ref/net10.0/System.Runtime.dll", AssemblyTypeDeclarationKind.Definition),
        })
        {
            using var asset = archive.GetEntry(path)!.Open();
            using var bytes = new MemoryStream();
            await asset.CopyToAsync(bytes, TestContext.Current.CancellationToken);
            var descriptor = Descriptor(bytes.ToArray());
            using var context = PdbContext.OpenMetadataOnly(descriptor);
            using var session = AssemblyInspectionSession.Borrow(context);
            AssemblyTypeDeclarationInventory inventory = Read(session);
            var objectDeclaration = Assert.Single(inventory.GetDeclarations(),
                declaration => declaration.Name == Name("System", "Object"));
            Assert.Equal(kind, objectDeclaration.Kind);
            Assert.Contains(inventory.GetDeclarations(), declaration =>
                declaration.Name == Name("System", "Environment", "SpecialFolder")
                && declaration.Kind == kind);
            if (kind == AssemblyTypeDeclarationKind.Forwarder)
            {
                Assert.All(inventory.Declarations, declaration =>
                    Assert.Null(declaration.DiscoveryAttributes));
            }
            else
            {
                Assert.All(inventory.Declarations, declaration =>
                    Assert.NotNull(declaration.DiscoveryAttributes));
                Assert.Equal(
                    new TypeDeclarationDiscoveryAttributes(false, false),
                    objectDeclaration.DiscoveryAttributes);
                Assert.Equal(
                    new TypeDeclarationDiscoveryAttributes(true, false),
                    Assert.Single(inventory.Declarations, declaration =>
                        declaration.Name == Name(
                            "System.Runtime.CompilerServices",
                            "IsExternalInit")).DiscoveryAttributes);
                Assert.Equal(
                    new TypeDeclarationDiscoveryAttributes(false, true),
                    Assert.Single(inventory.Declarations, declaration =>
                        declaration.Name == Name(
                            "System",
                            "ExecutionEngineException")).DiscoveryAttributes);
                var span = Assert.Single(inventory.Declarations, declaration =>
                    declaration.Name == Name("System", "Span`1"));
                Assert.Equal(
                    new TypeDeclarationDiscoveryAttributes(false, false),
                    span.DiscoveryAttributes);
                AssertCompilerCompatibilityAttributes(
                    bytes.ToArray(), "System", "Span`1");
            }
            Assert.Equal(
                Project(Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
                    AssemblyTypeDeclarationInventoryReader.Read(descriptor)).Inventory),
                Project(inventory));
        }
    }

    static IEnumerable<(
        MetadataTypeDefinitionName Name,
        AssemblyTypeDeclarationKind Kind,
        AssemblyTypeDefinitionKind? DefinitionKind,
        bool Public,
        TypeDeclarationDiscoveryAttributes? DiscoveryAttributes)>
        Project(AssemblyTypeDeclarationInventory inventory) =>
        inventory.Declarations.Select(declaration =>
            (
                declaration.Name,
                declaration.Kind,
                declaration.DefinitionKind,
                declaration.IsPublicSurface,
                declaration.DiscoveryAttributes));

    static AssemblyTypeDeclarationInventory Read(AssemblyInspectionSession session) =>
        Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(
            session.TypeDeclarations()).Inventory;

    static MetadataTypeDefinitionName Name(string ns, params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(ns, [.. segments])).Name;

    static ResolvedAssemblyReference Descriptor(byte[] bytes, Func<Stream>? open = null)
    {
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        return ResolvedAssemblyReference.Create(
            AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader()),
            path: null, open ?? (() => new MemoryStream(bytes, writable: false)),
            AssemblyResolutionProvenance.Local("inventory-fixture"));
    }

    static AssemblyReferenceHandle AddReference(MetadataBuilder metadata, string name) =>
        metadata.AddAssemblyReference(
            metadata.GetOrAddString(name), new Version(1, 0, 0, 0),
            default, default, 0, default);

    static TypeDefinitionHandle AddDefinition(
        MetadataBuilder metadata,
        TypeAttributes attributes,
        string ns,
        string name,
        EntityHandle baseType = default) =>
        metadata.AddTypeDefinition(
            attributes, metadata.GetOrAddString(ns), metadata.GetOrAddString(name),
            baseType, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

    static byte[] BuildImage(Action<MetadataBuilder> addRows)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Inventory.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Inventory"), new Version(1, 0, 0, 0),
            default, default, 0, 0);
        AddDefinition(metadata, 0, "", "<Module>");
        addRows(metadata);
        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
