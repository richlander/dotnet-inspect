using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class MethodDefinitionRequestSetTests
{
    [Fact]
    public void MethodQuerySource_ImageExposureAbsentWhenUndeclaredOrUnread()
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                CountingProducer.Instance,
                ProducerTerminal.Count);
        MethodDefinitionSourceAssociation silent =
            Association(
                ImageExposureProbe.Silent,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([undeclared, silent]));
        MethodDefinitionSourceCoverage physical =
            Assert.Single(execution.GroupReceipts).PhysicalCoverage;

        Assert.Same(PublicMethodRootCoverage.Empty, physical.PublicMethodRoots);
        Assert.Same(
            ReferenceAssemblyStatusCoverage.Empty,
            physical.ReferenceAssemblyStatus);
        foreach (MethodDefinitionSourceAssociation association
            in new[] { undeclared, silent })
        {
            MethodDefinitionSourceCoverage coverage =
                execution.ResultOf(association).SourceReceipt.Coverage;
            Assert.Same(
                PublicMethodRootCoverage.Empty,
                coverage.PublicMethodRoots);
            Assert.Same(
                ReferenceAssemblyStatusCoverage.Empty,
                coverage.ReferenceAssemblyStatus);
        }
        ImmutableArray<ProducerLayerParticipation> layers =
            execution.ResultOf(silent).WorkReceipt.Producers[0].Layers;
        Assert.Contains(
            new ProducerLayerParticipation(
                nameof(MethodDefinitionLayers.PublicMethodRoots),
                0),
            layers);
        Assert.Contains(
            new ProducerLayerParticipation(
                nameof(MethodDefinitionLayers.ReferenceAssemblyStatus),
                0),
            layers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodQuerySource_ImageExposureRequiresDeclaration(
        bool readsRoots)
    {
        MethodDefinitionSourceAssociation undeclared =
            Association(
                new UndeclaredImageExposureProbe(readsRoots),
                ProducerTerminal.Count);

        ProducerContractException exception =
            Assert.Throws<ProducerContractException>(
                () => Execute(AcceptedPlan([undeclared])));

        Assert.Contains(
            readsRoots
                ? "public MethodDef root support"
                : "reference-assembly status support",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MethodQuerySource_ImageExposureMatchesOwnersOutsideBreadth()
    {
        string path = typeof(MethodDefinitionRequestSetTests).Assembly.Location;
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        int methods = reader.MethodDefinitions.Count;
        PublicMethodRootInventory expected =
            PublicMethodRootInventoryReader.Read(
                reader,
                new PublicMethodRootInventoryLimits(
                    reader.TypeDefinitions.Count,
                    methods,
                    methods));
        int attributeRows = 0;
        ReferenceAssemblyState expectedStatus =
            LibraryEnablementFacts.ClassifyReferenceAssembly(
                reader,
                ref attributeRows);
        MethodDefinitionHandle only = reader.MethodDefinitions.First();

        MethodDefinitionSourceAssociation exact =
            Association(
                ImageExposureProbe.Reading,
                ProducerTerminal.Rows,
                MethodDefinitionSourceBreadth.ExactMethods(only));
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([exact]));
        ImageExposure exposure =
            ResultOf<ImageExposure>(execution, exact).Value!;
        MethodDefinitionSourceCoverage coverage =
            execution.ResultOf(exact).SourceReceipt.Coverage;

        PublicMethodRootInventory inventory =
            Assert.IsType<PublicMethodRootsResult.Read>(exposure.Roots)
                .Inventory;
        Assert.True(inventory.IsComplete);
        Assert.Equal(expected.ModuleVersionId, inventory.ModuleVersionId);
        Assert.Equal(expected.Roots, inventory.Roots);
        Assert.Equal(expected.Receipt, inventory.Receipt);
        Assert.Equal(ReferenceAssemblyState.Implementation, expectedStatus);
        Assert.Equal(expectedStatus, exposure.Status);

        // Whole-image facts are receipted apart from the one selected method.
        Assert.Equal(1, coverage.MethodsSelected.Count);
        Assert.Equal(0, coverage.BodiesAcquired.Count);
        Assert.Equal(0, coverage.ModuleLookupMethods.Count);
        Assert.Equal(
            new PublicMethodRootCoverage(
                Constructed: true,
                expected.Receipt,
                Failed: false,
                Reads: 1),
            coverage.PublicMethodRoots);
        Assert.Equal(
            new ReferenceAssemblyStatusCoverage(
                Constructed: true,
                attributeRows,
                Reads: 1),
            coverage.ReferenceAssemblyStatus);
        Assert.True(attributeRows > 0);
        Assert.Contains(
            new ProducerLayerParticipation(
                nameof(MethodDefinitionLayers.PublicMethodRoots),
                1),
            execution.ResultOf(exact).WorkReceipt.Producers[0].Layers);
    }

    [Fact]
    public void MethodQuerySource_ImageExposureComputedOnceAcrossLanes()
    {
        MethodDefinitionSourceAssociation first =
            Association(
                ImageExposureProbe.Reading,
                ProducerTerminal.Rows);
        MethodDefinitionSourceAssociation second =
            Association(
                ImageExposureProbe.SecondReading,
                ProducerTerminal.Rows);

        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([first, second]));
        MethodDefinitionSourceCoverage physical =
            Assert.Single(execution.GroupReceipts).PhysicalCoverage;
        MethodDefinitionSourceCoverage firstCoverage =
            execution.ResultOf(first).SourceReceipt.Coverage;
        MethodDefinitionSourceCoverage secondCoverage =
            execution.ResultOf(second).SourceReceipt.Coverage;

        // Both lanes see one shared result.
        Assert.Same(
            ResultOf<ImageExposure>(execution, first).Value!.Roots,
            ResultOf<ImageExposure>(execution, second).Value!.Roots);

        // One computation, charged to the lane whose read created it.
        Assert.True(physical.PublicMethodRoots.Constructed);
        Assert.True(firstCoverage.PublicMethodRoots.Constructed);
        Assert.False(secondCoverage.PublicMethodRoots.Constructed);
        Assert.NotNull(physical.PublicMethodRoots.Receipt);
        Assert.Equal(
            physical.PublicMethodRoots.Receipt,
            firstCoverage.PublicMethodRoots.Receipt);
        Assert.Null(secondCoverage.PublicMethodRoots.Receipt);
        Assert.True(physical.ReferenceAssemblyStatus.Constructed);
        Assert.True(firstCoverage.ReferenceAssemblyStatus.Constructed);
        Assert.False(secondCoverage.ReferenceAssemblyStatus.Constructed);
        Assert.Equal(
            physical.ReferenceAssemblyStatus.AssemblyAttributeRows,
            firstCoverage.ReferenceAssemblyStatus.AssemblyAttributeRows);
        Assert.Equal(
            0,
            secondCoverage.ReferenceAssemblyStatus.AssemblyAttributeRows);

        // Each lane receipts its own reads; the group receipts all of them.
        Assert.Equal(1, firstCoverage.PublicMethodRoots.Reads);
        Assert.Equal(1, secondCoverage.PublicMethodRoots.Reads);
        Assert.Equal(2, physical.PublicMethodRoots.Reads);
        Assert.Equal(2, physical.ReferenceAssemblyStatus.Reads);
    }

    [Theory]
    [InlineData(ExposureImageShape.Implementation)]
    [InlineData(ExposureImageShape.Reference)]
    [InlineData(ExposureImageShape.UndecidableReference)]
    [InlineData(ExposureImageShape.MalformedNesting)]
    public void MethodQuerySource_ImageExposureClassifiesAsOwnersDo(
        ExposureImageShape shape)
    {
        ImmutableArray<byte> image = BuildExposureImage(shape);
        using var pe = new PEReader(
            new MemoryStream(image.ToArray(), writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        int attributeRows = 0;
        ReferenceAssemblyState expectedStatus =
            LibraryEnablementFacts.ClassifyReferenceAssembly(
                reader,
                ref attributeRows);
        int methods = reader.MethodDefinitions.Count;
        PublicMethodRootInventory? expectedRoots = null;
        try
        {
            expectedRoots = PublicMethodRootInventoryReader.Read(
                reader,
                new PublicMethodRootInventoryLimits(
                    reader.TypeDefinitions.Count,
                    methods,
                    methods));
        }
        catch (BadImageFormatException)
        {
        }

        MethodDefinitionSourceAssociation association =
            Association(
                ImageExposureProbe.Reading,
                ProducerTerminal.Rows);
        MethodDefinitionSourceRequestSetExecution execution =
            Execute(AcceptedPlan([association]), image);
        ImageExposure exposure =
            ResultOf<ImageExposure>(execution, association).Value!;
        MethodDefinitionSourceCoverage coverage =
            execution.ResultOf(association).SourceReceipt.Coverage;

        Assert.Equal(
            shape switch
            {
                ExposureImageShape.Reference => ReferenceAssemblyState.Reference,
                ExposureImageShape.UndecidableReference =>
                    ReferenceAssemblyState.Undecidable,
                _ => ReferenceAssemblyState.Implementation,
            },
            expectedStatus);
        Assert.Equal(expectedStatus, exposure.Status);
        Assert.Equal(
            attributeRows,
            coverage.ReferenceAssemblyStatus.AssemblyAttributeRows);
        if (shape == ExposureImageShape.MalformedNesting)
        {
            Assert.Null(expectedRoots);
            Assert.IsType<PublicMethodRootsResult.Failed>(exposure.Roots);
            Assert.Equal(
                new PublicMethodRootCoverage(
                    Constructed: true,
                    Receipt: null,
                    Failed: true,
                    Reads: 1),
                coverage.PublicMethodRoots);
        }
        else
        {
            PublicMethodRootInventory inventory =
                Assert.IsType<PublicMethodRootsResult.Read>(exposure.Roots)
                    .Inventory;
            Assert.NotNull(expectedRoots);
            Assert.Equal(expectedRoots.Roots, inventory.Roots);
            Assert.Equal(expectedRoots.Receipt, inventory.Receipt);
            Assert.Single(inventory.Roots);
            Assert.Equal(
                expectedRoots.Receipt,
                coverage.PublicMethodRoots.Receipt);
        }
    }

    public enum ExposureImageShape
    {
        Implementation,
        Reference,
        UndecidableReference,
        MalformedNesting,
    }

    /// <summary>
    /// One public type with one public method. The shape adds the
    /// ReferenceAssembly attribute, an assembly attribute whose constructor row
    /// does not exist, or a nested public type with no enclosing type.
    /// </summary>
    static ImmutableArray<byte> BuildExposureImage(ExposureImageShape shape)
    {
        var metadata = new MetadataBuilder();
        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        AssemblyDefinitionHandle assembly = metadata.AddAssembly(
            metadata.GetOrAddString("Exposure"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Exposure.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        AssemblyReferenceHandle corlib = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(10, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            corlib,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, static ret => ret.Void(), static _ => { });
        BlobHandle constructorSignature =
            metadata.GetOrAddBlob(voidSignature);
        if (shape == ExposureImageShape.Reference)
        {
            TypeReferenceHandle referenceAttribute =
                metadata.AddTypeReference(
                    corlib,
                    metadata.GetOrAddString("System.Runtime.CompilerServices"),
                    metadata.GetOrAddString("ReferenceAssemblyAttribute"));
            metadata.AddCustomAttribute(
                assembly,
                metadata.AddMemberReference(
                    referenceAttribute,
                    metadata.GetOrAddString(".ctor"),
                    constructorSignature),
                metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }
        else if (shape == ExposureImageShape.UndecidableReference)
        {
            // A constructor row past the MemberRef table: its type cannot be
            // read, so the image may or may not be a reference assembly.
            metadata.AddCustomAttribute(
                assembly,
                MetadataTokens.MemberReferenceHandle(0x100),
                metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 }));
        }

        var body = new InstructionEncoder(new BlobBuilder());
        body.OpCode(ILOpCode.Ret);
        int bodyOffset = bodies.AddMethodBody(body);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("Fx"),
            metadata.GetOrAddString("Exposed"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Run"),
            constructorSignature,
            bodyOffset,
            default);
        if (shape == ExposureImageShape.MalformedNesting)
        {
            // Nested visibility with no NestedClass row: the inventory
            // cannot decide the type's exposure.
            metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic | TypeAttributes.Class,
                default,
                metadata.GetOrAddString("Orphan"),
                objectType,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(2));
        }

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            il);
        var output = new BlobBuilder();
        peBuilder.Serialize(output);
        return [.. output.ToArray()];
    }

    /// <summary>Both exposure facts, as a completing producer reads them.</summary>
    sealed record ImageExposure(
        PublicMethodRootsResult Roots,
        ReferenceAssemblyState Status);

    /// <summary>Reads both exposure facts at completion, or declares and reads nothing.</summary>
    sealed class ImageExposureProbe
        : MethodDefinitionProducer<int, int, ImageExposure?>
    {
        readonly bool _read;

        ImageExposureProbe(string identity, bool read)
            : base(
                identity,
                version: 1,
                tier: 0,
                MethodDefinitionLayers.PublicMethodRoots
                    | MethodDefinitionLayers.ReferenceAssemblyStatus)
        {
            _read = read;
        }

        public static ImageExposureProbe Reading { get; } =
            new("ImageExposureProbe", read: true);

        public static ImageExposureProbe SecondReading { get; } =
            new("SecondImageExposureProbe", read: true);

        public static ImageExposureProbe Silent { get; } =
            new("SilentImageExposureProbe", read: false);

        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override ImageExposure? Complete(
            int accumulator,
            MethodDefinitionCompletionView completion) =>
            _read
                ? new(
                    completion.GetPublicMethodRoots(),
                    completion.GetReferenceAssemblyStatus())
                : null;
    }

    sealed class UndeclaredImageExposureProbe(bool readsRoots)
        : MethodDefinitionProducer<int, int, int>(
            readsRoots
                ? "UndeclaredPublicRootsProbe"
                : "UndeclaredReferenceStatusProbe",
            version: 1,
            tier: 0,
            MethodDefinitionLayers.Flags)
    {
        internal override int Visit(scoped MethodDefinitionView view) => 1;

        internal override int Seed() => 0;

        internal override int Accumulate(int accumulator, int fact) =>
            accumulator + fact;

        internal override int Complete(
            int accumulator,
            MethodDefinitionCompletionView completion)
        {
            if (readsRoots)
                _ = completion.GetPublicMethodRoots();
            else
                _ = completion.GetReferenceAssemblyStatus();
            return accumulator;
        }
    }
}
