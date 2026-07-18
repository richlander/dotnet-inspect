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
    static readonly TypeRef String = TypeRef.CoreLib("System", "String");
    static readonly TypeRef UInt32 = TypeRef.CoreLib("System", "UInt32");
    static readonly TypeRef NInt = TypeRef.CoreLib("System", "IntPtr");
    static readonly TypeRef NUInt = TypeRef.CoreLib("System", "UIntPtr");
    static readonly TypeRef Void = TypeRef.CoreLib("System", "Void");
    static readonly TypeRef Owner = TypeRef.Definition("Synthetic", "Samples", "FixedBufferOwner");
    static readonly TypeRef Backing = TypeRef.Definition("Synthetic", "Samples", "<Data>e__FixedBuffer");
    static readonly TypeRef OtherBacking = TypeRef.Definition("Synthetic", "Samples", "<Other>e__FixedBuffer");

    public static TheoryData<string, string> FixedBufferFixtures => new()
    {
        { NewUnsafePath, NewFixedBufferType },
        { LegacyUnsafePath, LegacyFixedBufferType },
    };

    public static TheoryData<string, string, string, string> FixedBufferIndexZeroFixtures
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var (assemblyPath, typeName) in new[]
            {
                (NewUnsafePath, NewFixedBufferType),
                (LegacyUnsafePath, LegacyFixedBufferType),
            })
            {
                data.Add(assemblyPath, typeName, "ReadFirst", "return Data[0];");
                data.Add(assemblyPath, typeName, "WriteFirst", "Data[0] = value;");
                data.Add(assemblyPath, typeName, "RefFirst", "return ref Data[0];");
                data.Add(assemblyPath, typeName, "PassFirstByRef", "Increment(ref Data[0]);");
                data.Add(assemblyPath, typeName, "RefLocalFirstIncrement", "ref int value = ref Data[0];");
                data.Add(assemblyPath, typeName, "PointerLocalFirstValue", "&value.Data[0]");
                data.Add(assemblyPath, typeName, "PointerReturnFirst", "return &value.Data[0];");
                data.Add(assemblyPath, typeName, "PointerArgumentFirst", "ConsumePointer(&value.Data[0]);");
            }

            return data;
        }
    }

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
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferRefReturn_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "RefAt");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("return ref Data[index];", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferRefArgument_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "PassByRef");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("Increment(ref Data[index]);", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferRefLocal_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "RefLocalIncrement");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("ref int value = ref Data[index];", output);
        Assert.Contains("value++;", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
        Assert.DoesNotContain("Unsafe.NullRef", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferPointerLocal_RendersSourceAddressAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "PointerLocalValue");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("&value.Data[index]", output);
        Assert.Contains("*p = 42;", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferPointerReturn_RendersSourceAddressAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "PointerReturn");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("return &value.Data[index];", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferPointerArgument_RendersSourceAddressAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "PointerArgument");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("ConsumePointer(&value.Data[index]);", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferIndexZeroFixtures))]
    public void CompilerFixedBufferIndexZero_RendersBareElementAddressAsSourceIndexZero(
        string assemblyPath,
        string typeName,
        string methodName,
        string expected)
    {
        var function = Raised(assemblyPath, typeName, methodName);
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains(expected, output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferInstanceReceiver_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "FormatValue");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("return Values[index].ToString();", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
    }

    [Theory]
    [MemberData(nameof(FixedBufferFixtures))]
    public void CompilerFixedBufferIndexZeroInstanceReceiver_RendersSourceIndexingAndReachesFull(string assemblyPath, string typeName)
    {
        var function = Raised(assemblyPath, typeName, "FirstValueHashCode");
        var output = CSharpPrinter.Print(function).Output!;

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Contains("return Values[0].GetHashCode();", output);
        Assert.DoesNotContain("FixedElementField", output);
        Assert.DoesNotContain("Unsafe.Add", output);
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
    public void ByRefReturnElementMismatch_IsNotRaised()
    {
        var function = SyntheticRefReturn(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            returnElement: Int64);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void ByRefLocalElementMismatch_IsNotRaised()
    {
        var function = SyntheticRefLocal(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            localElement: Int64);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void ByRefCallParameterElementMismatch_IsNotRaised()
    {
        var function = SyntheticByRefCall(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            parameterElement: Int64,
            parameterFactsKnown: true);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void CrossAssemblyByRefCallWithoutRefKindFacts_IsNotRaised()
    {
        var function = SyntheticByRefCall(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            parameterElement: Int32,
            parameterFactsKnown: false);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void InstanceReceiverDeclaringTypeMismatch_IsNotRaised()
    {
        var function = SyntheticInstanceCall(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            declaringType: Int64);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void StaticCallArgumentShape_IsNotRaisedAsReceiver()
    {
        var function = SyntheticStaticCallArgumentShape(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void MissingReceiverInstanceCall_IsNotRaised()
    {
        var function = SyntheticMissingReceiverInstanceCall(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void ZeroArgumentInstanceCall_DoesNotRaiseOrThrow()
    {
        var function = SyntheticZeroArgumentInstanceCall();

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        function.CheckInvariant();
    }

    [Fact]
    public void NativeAddressCastWithoutTypedElementConsumer_IsNotRaised()
    {
        var function = SyntheticNativeAddressCast(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void BareGeneratedNameLookalikeWithoutFixedBufferAttribute_IsNotRaised()
    {
        var function = SyntheticBareRead(
            BufferField(fixedBuffer: null),
            ElementField(Backing, Int32));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void BareMismatchedBackingType_IsNotRaised()
    {
        var function = SyntheticBareRead(
            BufferField(FixedBuffer(Int32)),
            ElementField(OtherBacking, Int32));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void BareMismatchedElementType_IsNotRaised()
    {
        var function = SyntheticBareRead(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int64));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void PointerLocalElementMismatch_IsNotRaised()
    {
        var function = SyntheticPointerLocal(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            pointerElement: Int64);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void PointerReturnVoidPointer_IsNotRaised()
    {
        var function = SyntheticPointerReturn(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            pointerElement: Void);

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void PointerArgumentElementMismatch_IsNotRaised()
    {
        var function = SyntheticPointerCall(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            pointerElement: Int64);

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

    [Theory]
    [InlineData(8L, 2)]
    [InlineData(-4L, -1)]
    public void PortableIntPtrConvertedConstantOffset_IsRaised(long offset, int expectedIndex)
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                NInt,
                isChecked: false,
                isUnsigned: false,
                new Constant(offset, Int64)));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        var address = Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        var constant = Assert.IsType<Constant>(address.Index);
        Assert.Equal(expectedIndex, constant.Value);
        function.CheckInvariant();
    }

    [Fact]
    public void PortableUIntPtrConvertedConstantOffset_IsRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                NUInt,
                isChecked: false,
                isUnsigned: true,
                new Constant(12L, Int64)));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        var address = Assert.Single(function.Descendants.OfType<FixedBufferElementAddress>());
        var constant = Assert.IsType<Constant>(address.Index);
        Assert.Equal(3, constant.Value);
        function.CheckInvariant();
    }

    [Fact]
    public void IntPtrConvertedConstantOutsidePortableRange_IsNotRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                NInt,
                isChecked: false,
                isUnsigned: false,
                new Constant((long)int.MaxValue + 1, Int64)));

        new FixedBufferElementAccessPass().Run(function, PassContext.None);

        Assert.Empty(function.Descendants.OfType<FixedBufferElementAddress>());
        Assert.Single(function.Descendants.OfType<LoadFieldAddress>(), a => a.Field.Name == "FixedElementField");
        function.CheckInvariant();
    }

    [Fact]
    public void UIntPtrConvertedConstantOutsidePortableRange_IsNotRaised()
    {
        var function = SyntheticReadWithOffset(
            BufferField(FixedBuffer(Int32)),
            ElementField(Backing, Int32),
            new Convert(
                NUInt,
                isChecked: false,
                isUnsigned: true,
                new Constant((long)uint.MaxValue + 1, Int64)));

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

    static IrFunction SyntheticBareRead(FieldRef bufferField, FieldRef elementField, TypeRef? observedType = null)
    {
        var block = new Block();
        block.Add(new Return(new LoadIndirect(observedType ?? Int32, BareFixedElementAddress(bufferField, elementField))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Int32, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

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

    static IrFunction SyntheticRefReturn(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef returnElement)
    {
        var block = new Block();
        block.Add(new Return(FixedElementAddress(bufferField, elementField, FixedElementOffset(4))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(TypeRef.ByRef(returnElement), [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticRefLocal(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef localElement)
    {
        var block = new Block();
        block.Add(new StoreLocal(
            0,
            TypeRef.ByRef(localElement),
            FixedElementAddress(bufferField, elementField, FixedElementOffset(4))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [TypeRef.ByRef(localElement)],
            body);
    }

    static IrFunction SyntheticByRefCall(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef parameterElement,
        bool parameterFactsKnown)
    {
        var callee = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Consumer"),
            "Consume",
            Void,
            [TypeRef.ByRef(parameterElement)],
            HasThis: false)
        {
            ParameterRefKinds = parameterFactsKnown ? [ArgumentRefKind.Ref] : [],
            ParameterRefKindsFacts = parameterFactsKnown ? ParameterRefKindFacts.Known : ParameterRefKindFacts.Unknown,
        };
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            callee,
            isVirtual: false,
            [FixedElementAddress(bufferField, elementField, FixedElementOffset(4))])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticInstanceCall(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef declaringType)
    {
        var callee = new MethodRef(
            declaringType,
            "ToString",
            String,
            [],
            HasThis: true);
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            callee,
            isVirtual: false,
            [FixedElementAddress(bufferField, elementField, FixedElementOffset(4))])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticStaticCallArgumentShape(FieldRef bufferField, FieldRef elementField)
    {
        var callee = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Consumer"),
            "Static",
            Void,
            [],
            HasThis: false);
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            callee,
            isVirtual: false,
            [FixedElementAddress(bufferField, elementField, FixedElementOffset(4))])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticMissingReceiverInstanceCall(FieldRef bufferField, FieldRef elementField)
    {
        var callee = new MethodRef(
            Int32,
            "Consume",
            Void,
            [TypeRef.ByRef(Int32)],
            HasThis: true)
        {
            ParameterRefKinds = [ArgumentRefKind.Ref],
            ParameterRefKindsFacts = ParameterRefKindFacts.Known,
        };
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            callee,
            isVirtual: false,
            [FixedElementAddress(bufferField, elementField, FixedElementOffset(4))])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticZeroArgumentInstanceCall()
    {
        var callee = new MethodRef(
            Int32,
            "ToString",
            String,
            [],
            HasThis: true);
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false, [])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticNativeAddressCast(FieldRef bufferField, FieldRef elementField)
    {
        var block = new Block();
        block.Add(new Return(new Convert(
            NInt,
            isChecked: false,
            isUnsigned: false,
            FixedElementAddress(bufferField, elementField, FixedElementOffset(4)))));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(NInt, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticPointerLocal(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef pointerElement)
    {
        var pointerType = TypeRef.Pointer(pointerElement);
        var block = new Block();
        block.Add(new StoreLocal(
            0,
            pointerType,
            FixedElementPointerAddress(bufferField, elementField)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [pointerType],
            body);
    }

    static IrFunction SyntheticPointerReturn(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef pointerElement)
    {
        var pointerType = TypeRef.Pointer(pointerElement);
        var block = new Block();
        block.Add(new Return(FixedElementPointerAddress(bufferField, elementField)));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(pointerType, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static IrFunction SyntheticPointerCall(
        FieldRef bufferField,
        FieldRef elementField,
        TypeRef pointerElement)
    {
        var callee = new MethodRef(
            TypeRef.Definition("Synthetic", "Samples", "Consumer"),
            "ConsumePointer",
            Void,
            [TypeRef.Pointer(pointerElement)],
            HasThis: false);
        var block = new Block();
        block.Add(new ExpressionStatement(new Call(
            callee,
            isVirtual: false,
            [FixedElementPointerAddress(bufferField, elementField)])));
        var body = new BlockContainer();
        body.Add(block);
        return new IrFunction(
            "M",
            Owner,
            new MethodSignature(Void, [new Parameter("index", Int32)], HasThis: true, GenericParameterCount: 0),
            [],
            body);
    }

    static Binary FixedElementAddress(FieldRef bufferField, FieldRef elementField, IrExpression offset)
    {
        var sourceFieldAddress = new LoadFieldAddress(bufferField, new LoadArgument(0, "this", Owner));
        var elementFieldAddress = new LoadFieldAddress(elementField, sourceFieldAddress);
        return new Binary(BinaryKind.Add, isChecked: false, isUnsigned: false, elementFieldAddress, offset);
    }

    static LoadFieldAddress BareFixedElementAddress(FieldRef bufferField, FieldRef elementField)
    {
        var sourceFieldAddress = new LoadFieldAddress(bufferField, new LoadArgument(0, "this", Owner));
        return new LoadFieldAddress(elementField, sourceFieldAddress);
    }

    static Convert FixedElementPointerAddress(FieldRef bufferField, FieldRef elementField)
        => new(
            NUInt,
            isChecked: false,
            isUnsigned: true,
            FixedElementAddress(bufferField, elementField, FixedElementOffset(4)));

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
