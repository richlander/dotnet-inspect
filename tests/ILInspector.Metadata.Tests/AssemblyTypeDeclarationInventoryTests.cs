using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class AssemblyTypeDeclarationInventoryTests
{
    const TypeAttributes Forwarder = (TypeAttributes)0x00200000;

    [Fact]
    public void Definitions_PreserveDeclaredVisibilityAndStructuredGenericNames()
    {
        byte[] image = BuildImage(metadata =>
        {
            var outer = Define(metadata, TypeAttributes.Public, "N", "Outer`1");
            var inner = Define(metadata, TypeAttributes.NestedPublic, "", "Inner`2");
            metadata.AddNestedType(inner, outer);
            Define(metadata, TypeAttributes.Public, "N", "Outer.Inner`2");
            Define(metadata, TypeAttributes.Public, "N", "Outer");
            var hidden = Define(metadata, TypeAttributes.NotPublic, "N", "Hidden");
            var nested = Define(metadata, TypeAttributes.NestedPublic, "", "Child");
            metadata.AddNestedType(nested, hidden);
            Define(metadata, TypeAttributes.Public, "N", "<Generated>");
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session.TypeDeclarations(TestContext.Current.CancellationToken));

        Assert.Equal(8, inventory.TypeDefinitions.Length);
        Assert.Equal(inventory.Definitions, inventory.TypeDefinitions.Select(entry => entry.Name));
        Assert.Equal(6, inventory.TypeDefinitions.Count(entry => entry.IsPublic));
        Assert.Equal(5, inventory.MeaningfulPublicTypeCount);
        Assert.Contains(inventory.TypeDefinitions,
            entry => entry.IsPublic && entry.Name == Name("N", "Outer`1", "Inner`2"));
        Assert.Contains(inventory.TypeDefinitions,
            entry => !entry.IsPublic && entry.Name == Name("N", "Hidden"));
        Assert.Contains(inventory.TypeDefinitions,
            entry => entry.IsPublic && entry.Name == Name("N", "Hidden", "Child"));
        Assert.NotEqual(Name("N", "Outer`1", "Inner`2"), Name("N", "Outer.Inner`2"));
        Assert.NotEqual(Name("N", "Outer`1"), Name("N", "Outer"));
    }

    [Fact]
    public void Declarations_PreserveDuplicatesAndDefinitionForwarderDistinction()
    {
        byte[] image = BuildImage(metadata =>
        {
            Define(metadata, TypeAttributes.Public, "N", "Same");
            Define(metadata, TypeAttributes.NotPublic, "N", "Same");
            AssemblyReferenceHandle target = Target(metadata);
            Export(metadata, Forwarder, "N", "Same", target);
            Export(metadata, Forwarder, "N", "Same", target);
            var outer = Export(metadata, Forwarder, "N", "Outer`1", target);
            Export(metadata, TypeAttributes.NestedPublic, "", "Inner`2", outer);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session.TypeDeclarations(TestContext.Current.CancellationToken));

        Assert.Equal([true, false], inventory.TypeDefinitions
            .Where(entry => entry.Name == Name("N", "Same")).Select(entry => entry.IsPublic));
        Assert.Equal(2, inventory.Definitions.Count(name => name == Name("N", "Same")));
        Assert.Equal(2, inventory.Forwarders.Count(name => name == Name("N", "Same")));
        Assert.Contains(Name("N", "Outer`1", "Inner`2"), inventory.Forwarders);
        Assert.Empty(inventory.ModuleExports);
    }

    [Fact]
    public void ModuleExports_PreserveUnsupportedDeclarationEvidence()
    {
        byte[] image = BuildImage(metadata =>
        {
            var file = metadata.AddAssemblyFile(metadata.GetOrAddString("Other.netmodule"),
                default, containsMetadata: true);
            var outer = Export(metadata, TypeAttributes.Public, "N", "Outer`1", file);
            Export(metadata, TypeAttributes.NestedPublic, "", "Inner`2", outer);
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssemblyTypeDeclarationInventory inventory = Read(session.TypeDeclarations(TestContext.Current.CancellationToken));

        Assert.Equal([Name("N", "Outer`1"), Name("N", "Outer`1", "Inner`2")],
            inventory.ModuleExports);
        Assert.Empty(inventory.Forwarders);
        Assert.Equal(inventory.ModuleExports,
            Read(AssemblyTypeDeclarationInventoryReader.Read(Descriptor(image))).ModuleExports);
    }

    [Theory]
    [InlineData("definition-empty")]
    [InlineData("definition-cycle")]
    [InlineData("forwarder-empty")]
    [InlineData("forwarder-cycle")]
    [InlineData("missing-forwarder-flag")]
    [InlineData("module-empty")]
    [InlineData("nil-export")]
    [InlineData("missing-target")]
    [InlineData("missing-file")]
    [InlineData("forwarder-file")]
    public void MalformedDeclarations_RejectTheWholeInventoryOnBothPaths(string kind)
    {
        byte[] image = BuildImage(metadata =>
        {
            Define(metadata, TypeAttributes.Public, "N", "ValidBeforeFailure");
            switch (kind)
            {
                case "definition-empty":
                    Define(metadata, TypeAttributes.Public, "N", "");
                    break;
                case "definition-cycle":
                    var cycle = Define(metadata, TypeAttributes.NestedPublic, "", "Cycle");
                    metadata.AddNestedType(cycle, cycle);
                    break;
                case "forwarder-empty":
                    Export(metadata, Forwarder, "N", "", Target(metadata));
                    break;
                case "forwarder-cycle":
                    Export(metadata, TypeAttributes.NestedPublic, "", "Cycle",
                        MetadataTokens.ExportedTypeHandle(1));
                    break;
                case "missing-forwarder-flag":
                    Export(metadata, TypeAttributes.Public, "N", "Invalid", Target(metadata));
                    break;
                case "module-empty":
                    Export(metadata, TypeAttributes.Public, "N", "",
                        metadata.AddAssemblyFile(metadata.GetOrAddString("Other.netmodule"),
                            default, containsMetadata: true));
                    break;
                case "nil-export":
                    Export(metadata, Forwarder, "N", "Invalid",
                        MetadataTokens.AssemblyReferenceHandle(0));
                    break;
                case "missing-target":
                    Export(metadata, Forwarder, "N", "Invalid",
                        MetadataTokens.AssemblyReferenceHandle(1));
                    break;
                case "missing-file":
                    Export(metadata, TypeAttributes.Public, "N", "Invalid",
                        MetadataTokens.AssemblyFileHandle(1));
                    break;
                case "forwarder-file":
                    Export(metadata, Forwarder, "N", "Invalid",
                        metadata.AddAssemblyFile(metadata.GetOrAddString("Other.netmodule"),
                            default, containsMetadata: true));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        });
        using var session = AssemblyInspectionSession.Open(Descriptor(image));
        AssertRejected(session.TypeDeclarations(TestContext.Current.CancellationToken));
        AssertRejected(AssemblyTypeDeclarationInventoryReader.Read(Descriptor(image)));
    }

    [Fact]
    public void BorrowedInventory_DetachesWithoutReopeningOrClosingTheLender()
    {
        byte[] image = BuildImage(metadata => Define(metadata, TypeAttributes.Public, "N", "Type"));
        int opens = 0;
        var descriptor = ResolvedAssemblyReference.Create(
            new("Inventory", new(1, 0, 0, 0), null, null),
            path: null,
            () =>
            {
                opens++;
                return new MemoryStream(image, writable: false);
            },
            AssemblyResolutionProvenance.Local("inventory-test"));
        using var lender = PdbContext.Open(descriptor);
        using var session = AssemblyInspectionSession.Borrow(lender);
        AssemblyTypeDeclarationInventory inventory = Read(session.TypeDeclarations(TestContext.Current.CancellationToken));
        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.TypeDeclarations(TestContext.Current.CancellationToken));
        Assert.Equal("Inventory", lender.ExtractAssemblyInfo().AssemblyName);
        using var second = AssemblyInspectionSession.Borrow(lender);
        lender.Dispose();
        Assert.Throws<ObjectDisposedException>(() => second.TypeDeclarations(TestContext.Current.CancellationToken));

        Assert.Equal(1, opens);
        Assert.Contains(Name("N", "Type"), inventory.Definitions);
        Assert.Equal(1, inventory.MeaningfulPublicTypeCount);
    }

    [Fact]
    public void Cancellation_DoesNotPublishAnInventory()
    {
        using var session = AssemblyInspectionSession.Open(Descriptor(BuildImage(_ => { })));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(cancellation.Token,
            Assert.Throws<OperationCanceledException>(
                () => session.TypeDeclarations(cancellation.Token)).CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonAssemblyImages_RejectRatherThanReturnAnEmptyInventory(bool native)
    {
        byte[] image = native
            ? InspectionAcquisitionPlanTests.BuildNativePeImage()
            : InspectionAcquisitionPlanTests.BuildModuleImage();
        using var session = AssemblyInspectionSession.Open(Descriptor(image));

        AssertRejected(session.TypeDeclarations(TestContext.Current.CancellationToken));
    }

    static void AssertRejected(AssemblyTypeDeclarationInventoryOutcome outcome) =>
        Assert.Equal(CandidateOpenFailureKind.InvalidImage,
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(outcome).Failure.Kind);

    static AssemblyTypeDeclarationInventory Read(AssemblyTypeDeclarationInventoryOutcome outcome) =>
        Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Read>(outcome).Inventory;

    static MetadataTypeDefinitionName Name(string ns, params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(ns, [.. segments])).Name;

    static ResolvedAssemblyReference Descriptor(byte[] image) =>
        ResolvedAssemblyReference.Create(
            new("Inventory", new(1, 0, 0, 0), null, null),
            path: null, () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("inventory-test"));

    static TypeDefinitionHandle Define(
        MetadataBuilder metadata, TypeAttributes attributes, string ns, string name) =>
        metadata.AddTypeDefinition(attributes, metadata.GetOrAddString(ns),
            metadata.GetOrAddString(name), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

    static ExportedTypeHandle Export(
        MetadataBuilder metadata, TypeAttributes attributes, string ns, string name,
        EntityHandle implementation) =>
        metadata.AddExportedType(attributes, metadata.GetOrAddString(ns),
            metadata.GetOrAddString(name), implementation, typeDefinitionId: 0);

    static AssemblyReferenceHandle Target(MetadataBuilder metadata) =>
        metadata.AddAssemblyReference(metadata.GetOrAddString("UnavailableTarget"),
            new(1, 0, 0, 0), default, default, default, default);

    static byte[] BuildImage(Action<MetadataBuilder> add)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Inventory.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Inventory"),
            new(1, 0, 0, 0), default, default, default, default);
        Define(metadata, TypeAttributes.NotPublic, "", "<Module>");
        add(metadata);
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
