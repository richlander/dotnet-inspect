using DotnetInspector.Fixtures;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

using Convert = ILInspector.Decompiler.Pipeline.Convert;
using LegacyFixedBuffer = ILInspector.Decompiler.Fixtures.LegacyUnsafe.FixedBufferResiduals;
using LegacyPrimitiveFixedBuffer = ILInspector.Decompiler.Fixtures.LegacyUnsafe.FixedBufferPrimitiveResiduals;
using NewFixedBuffer = ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferResiduals;
using NewPrimitiveFixedBuffer = ILInspector.Decompiler.Fixtures.NewUnsafe.FixedBufferPrimitiveResiduals;

namespace ILInspector.Decompiler.Tests;

public class FixedBufferElementAccessPassTests
{
    static readonly string NewUnsafePath = FixtureCatalog.DecompilerUnsafeNew.AssemblyPath();
    static readonly string LegacyUnsafePath = FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath();
    static readonly string NewFixedBufferType = typeof(NewFixedBuffer).FullName!;
    static readonly string LegacyFixedBufferType = typeof(LegacyFixedBuffer).FullName!;
    static readonly string NewPrimitiveFixedBufferType = typeof(NewPrimitiveFixedBuffer).FullName!;
    static readonly string LegacyPrimitiveFixedBufferType = typeof(LegacyPrimitiveFixedBuffer).FullName!;

    static readonly TypeRef Byte = TypeRef.CoreLib("System", "Byte");
    static readonly TypeRef SByte = TypeRef.CoreLib("System", "SByte");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Int64 = TypeRef.CoreLib("System", "Int64");
    static readonly TypeRef UInt32 = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef NInt = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "FixedBufferOwner");
    static readonly TypeRef Backing = TypeRef.Definition("Synthetic", "Samples", "<Data>e__FixedBuffer");
    static readonly TypeRef OtherBacking = TypeRef.Definition("Synthetic", "Samples", "<Other>e__FixedBuffer");

    public static TheoryData<string, string> FixedBufferFixtures => new()
    {
        { NewUnsafePath, NewFixedBufferType },
        { LegacyUnsafePath, LegacyFixedBufferType },
    };

    public static TheoryData<string, string, string, string> PrimitiveFixedBufferFixtures
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var (assemblyPath, typeName) in new[]
            {
                (NewUnsafePath, NewPrimitiveFixedBufferType),
                (LegacyUnsafePath, LegacyPrimitiveFixedBufferType),
            })
            {
                data.Add(assemblyPath, typeName, "Bool", "Bools");
                data.Add(assemblyPath, typeName, "Byte", "Bytes");
                data.Add(assemblyPath, typeName, "SByte", "SBytes");
                data.Add(assemblyPath, typeName, "Char", "Chars");
                data.Add(assemblyPath, typeName, "Short", "Shorts");
                data.Add(assemblyPath, typeName, "UShort", "UShorts");
                data.Add(assemblyPath, typeName, "Int", "Ints");
                data.Add(assemblyPath, typeName, "UInt", "UInts");
                data.Add(assemblyPath, typeName, "Long", "Longs");
                data.Add(assemblyPath, typeName, "ULong", "ULongs");
                data.Add(assemblyPath, typeName, "Float", "Floats");
                data.Add(assemblyPath, typeName, "Double", "Doubles");
            }

            return data;
        }
    }

    public static TheoryData<string, string, string> WideIndexFixtures
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var (assemblyPath, typeName) in new[]
            {
                (NewUnsafePath, NewPrimitiveFixedBufferType),
                (LegacyUnsafePath, LegacyPrimitiveFixedBufferType),
            })
            {
                data.Add(assemblyPath, typeName, "Long");
                data.Add(assemblyPath, typeName, "UInt");
                data.Add(assemblyPath, typeName, "ULong");
            }

            return data;
        }
    }

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

    [Theory]
    [MemberData(nameof(PrimitiveFixedBufferFixtures))]
    public void CompilerFixedBufferPrimitiveRead_RendersSourceIndexingAndReachesFull(
        string assemblyPath,
        string typeName,
        string methodSuffix,
        string fieldName)
    {
        var function = Raised(assemblyPath, typeName, $"Read{methodSuffix}");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains($"{fieldName}[index]", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(PrimitiveFixedBufferFixtures))]
    public void CompilerFixedBufferPrimitiveWrite_RendersSourceIndexingAndReachesFull(
        string assemblyPath,
        string typeName,
        string methodSuffix,
        string fieldName)
    {
        var function = Raised(assemblyPath, typeName, $"Write{methodSuffix}");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains($"{fieldName}[index] = value;", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(WideIndexFixtures))]
    public void CompilerFixedBufferReadWithWideIndex_RendersOriginalIndexExpression(
        string assemblyPath,
        string typeName,
        string indexSuffix)
    {
        var function = Raised(assemblyPath, typeName, $"ReadIntAt{indexSuffix}");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("return Ints[", output);
        Assert.Contains("index", output);
        Assert.DoesNotContain("FixedElementField", output);
    }

    [Theory]
    [MemberData(nameof(WideIndexFixtures))]
    public void CompilerFixedBufferWriteWithWideIndex_RendersOriginalIndexExpression(
        string assemblyPath,
        string typeName,
        string indexSuffix)
    {
        var function = Raised(assemblyPath, typeName, $"WriteIntAt{indexSuffix}");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("Ints[", output);
        Assert.Contains("index", output);
        Assert.Contains("] = value;", output);
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

    [Fact]
    public void ReadStorageSignednessMismatch_IsNotRaised()
    {
        var function = SyntheticRead(
            BufferField(FixedBuffer(Byte)),
            ElementField(Backing, Byte),
            scale: 1,
            observedType: SByte);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void WriteStorageWidthMismatch_IsNotRaised()
    {
        var function = SyntheticWrite(
            BufferField(FixedBuffer(UInt32)),
            ElementField(Backing, UInt32),
            scale: 4,
            observedType: Int64,
            valueType: UInt32);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void PinnedSourceStorageAlias_IsNotRaised()
    {
        var function = SyntheticPinnedSource(
            BufferField(FixedBuffer(Byte)),
            ElementField(Backing, Byte),
            scale: 1,
            pinnedElementType: SByte);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void OuterNativeConversionOverScaledWideIndex_IsRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                NInt,
                isChecked: false,
                isUnsigned: false,
                new Binary(
                    BinaryKind.Multiply,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadArgument(1, "index", Int64),
                    new Constant(4L, Int64))));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        var address = Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Equal(Int64, address.Index.ResultType);
        Assert.IsType<LoadArgument>(address.Index);
        function.CheckInvariant();
    }

    [Fact]
    public void OuterNativeConversionWithWrongScale_IsNotRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                NInt,
                isChecked: false,
                isUnsigned: false,
                new Binary(
                    BinaryKind.Multiply,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadArgument(1, "index", Int64),
                    new Constant(8L, Int64))));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void NonNativeOuterConversion_IsNotRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                Int64,
                isChecked: false,
                isUnsigned: false,
                new Binary(
                    BinaryKind.Multiply,
                    isChecked: false,
                    isUnsigned: false,
                    new LoadArgument(1, "index", Int64),
                    new Constant(4L, Int64))));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void SignednessChangingConvertedConstantOffset_IsNotRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                UInt32,
                isChecked: false,
                isUnsigned: true,
                new Constant(-4, Int32)));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void TruncatingConvertedConstantOffset_IsNotRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                SByte,
                isChecked: false,
                isUnsigned: false,
                new Constant(260, Int32)));

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

    static IrFunction SyntheticRead(FieldRef bufferField, FieldRef elementField, int scale, TypeRef? observedType = null)
        => SyntheticReadWithOffset(
            bufferField,
            elementField,
            FixedElementOffset(scale),
            observedType);

    static IrFunction SyntheticReadWithOffset(
        FieldRef bufferField,
        FieldRef elementField,
        IrExpression offset,
        TypeRef? observedType = null)
    {
        var block = new Block();
        block.Add(new Return(new LoadIndirect(observedType ?? Int32, FixedElementAddress(bufferField, elementField, offset))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Int32, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticWrite(
        FieldRef bufferField,
        FieldRef elementField,
        int scale,
        TypeRef observedType,
        TypeRef valueType)
    {
        var block = new Block();
        block.Add(new StoreIndirect(
            observedType,
            FixedElementAddress(bufferField, elementField, FixedElementOffset(scale)),
            new LoadArgument(2, "value", valueType)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32), new Parameter("value", valueType)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticPinnedSource(
        FieldRef bufferField,
        FieldRef elementField,
        int scale,
        TypeRef pinnedElementType)
    {
        var block = new Block();
        block.Add(new StoreLocal(
            0,
            TypeRef.Pinned(TypeRef.ByRef(pinnedElementType)),
            FixedElementAddress(bufferField, elementField, FixedElementOffset(scale))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [TypeRef.Pinned(TypeRef.ByRef(pinnedElementType))],
            body);
    }

    static Binary FixedElementAddress(FieldRef bufferField, FieldRef elementField, IrExpression offset)
    {
        var sourceFieldAddress = new LoadFieldAddress(bufferField, new LoadArgument(0, "this", Owner));
        var elementFieldAddress = new LoadFieldAddress(elementField, sourceFieldAddress);
        return new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, elementFieldAddress, offset);
    }

    static Binary FixedElementOffset(int scale)
        => new(
            BinaryKind.Multiply,
            isChecked: false,
            isUnsigned: false,
            new Convert(NInt, isChecked: false, isUnsigned: false, new LoadArgument(1, "index", Int32)),
            new Constant(scale, Int32));

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
