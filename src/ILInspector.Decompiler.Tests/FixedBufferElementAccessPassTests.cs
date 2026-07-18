using DotnetInspector.Fixtures;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

using Convert = ILInspector.Decompiler.Pipeline.Convert;
using LegacyFixedBuffer = ILInspector.Decompiler.Fixtures.LegacyUnsafe.FixedBufferResiduals;
using NewFixedBuffer = ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals;

namespace ILInspector.Decompiler.Tests;

public class FixedBufferElementAccessPassTests
{
    static readonly string NewUnsafePath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
    static readonly string LegacyUnsafePath = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
    static readonly string NewFixedBufferType = typeof(NewFixedBuffer).FullName!;
    static readonly string LegacyFixedBufferType = typeof(LegacyFixedBuffer).FullName!;

    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef NInt = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "FixedBufferOwner");
    static readonly TypeRef Backing = TypeRef.Definition("Synthetic", "Samples", "<Data>e__FixedBuffer");
    static readonly TypeRef OtherBacking = TypeRef.Definition("Synthetic", "Samples", "<Other>e__FixedBuffer");

    public static TheoryData<string, string> FixedBufferFixtures => new()
    {
        { NewUnsafePath, NewFixedBufferType },
        { LegacyUnsafePath, LegacyFixedBufferType },
    };

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferRead_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "ReadAt");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("return Data[index];", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferWrite_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "WriteAt");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("Data[index] = value;", output);
        Assert.DoesNotContain("FixedElementField", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferAddressInFixedInitializer_RendersSourceAddress(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "ReadAtThroughFixedAddress");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("fixed (int* ", output);
        Assert.Contains(" = &Data[index])", output);
        Assert.DoesNotContain("FixedElementField", output);
    }

    [Fact]
    public void GeneratedNameLookalikeWithoutFixedBufferAttribute_IsNotRaised()
    {
        var function = SyntheticRead(
            BufferField(fixedBuffer: null),
            ElementField(Backing, Int32),
            scale: 4);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void MismatchedBackingType_IsNotRaised()
    {
        var function = SyntheticRead(
            BufferField(FixedBuffer(Int32)),
            ElementField(OtherBacking, Int32),
            scale: 4);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void MismatchedElementType_IsNotRaised()
    {
        var function = SyntheticRead(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int64),
            scale: 4);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void MismatchedElementScale_IsNotRaised()
    {
        var function = SyntheticRead(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            scale: 8);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    static IrFunction Raised(string assemblyPath, string typeName, string methodName)
    {
        using var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, typeName, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    static IrFunction SyntheticRead(FieldRef bufferField, FieldRef elementField, int scale)
    {
        var block = new Block();
        block.Add(new Return(new LoadIndirect(Int32, FixedElementAddress(bufferField, elementField, scale))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Int32, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static Binary FixedElementAddress(FieldRef bufferField, FieldRef elementField, int scale)
    {
        var sourceFieldAddress = new LoadFieldAddress(bufferField, new LoadArgument(0, "this", Owner));
        var elementFieldAddress = new LoadFieldAddress(elementField, sourceFieldAddress);
        var offset = new Binary(
            BinaryKind.Multiply,
            isChecked: false,
            isUnsigned: false,
            new Convert(NInt, isChecked: false, isUnsigned: false, new LoadArgument(1, "index", Int32)),
            new Constant(scale, Int32));
        return new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, elementFieldAddress, offset);
    }

    static FieldRef BufferField(FixedBufferFieldInfo? fixedBuffer)
        => new(Owner, "Data", Backing)
        {
            FixedBuffer = fixedBuffer,
        };

    static FieldRef ElementField(TypeRef declaringType, TypeRef elementType)
        => new(declaringType, "FixedElementField", elementType);

    static FixedBufferFieldInfo FixedBuffer(TypeRef elementType)
        => new(elementType, 4);
}
