using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

public class CSharpPrinterTests
{
    static void RunStoreElementReceiverInlining(
        IrFunction function)
        => new StoreElementReceiverInliningPass()
            .Run(function, PassContext.None);

    static string PrintFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var result = CSharpPrinter.Print(function);
        Assert.True(result.Succeeded);
        return result.Output!.ReplaceLineEndings("\n");
    }

    [Fact]
    public void MergedTernary_DeclaresCommonBase_NotWhenTrueArm()
    {
        // A folded ternary feeding a declared slot: the arms are JoinDerived
        // and JoinBase, so the slot must declare as the common base JoinBase.
        // Without carrying the importer-merged type the ternary would narrow to
        // the WhenTrue arm (JoinDerived) and assign JoinBase to it — unsound.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MergedTernaryDeclaration));
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function);
        Assert.True(result.Succeeded);
        string output = result.Output!.ReplaceLineEndings("\n");

        Assert.Contains("new JoinDerived()", output);
        Assert.Contains("new JoinBase()", output);
        // The slot/local carrying the ternary declares as the common base,
        // never the WhenTrue arm — config-agnostic (Release spills a stack
        // slot, Debug keeps a named local).
        Assert.Matches(@"JoinBase \w+ = !flag \?", output);
        Assert.DoesNotContain("JoinDerived S_", output);
        Assert.DoesNotContain("JoinDerived V_", output);
    }

    [Fact]
    public void NullConditional_RaisesQuestionDot_AndSoundlyTypesSlot()
    {
        // The ?. lowering spills the receiver into a stack slot, null-tests it,
        // and reloads the spill for the member access — reusing one slot for the
        // receiver (JoinBase) and the result (string). NullConditionalPass raises
        // it to node?.Member, so the slot carries only the string result and the
        // receiver type never declares a slot/local.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var property = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullConditionalProperty));
        Assert.NotNull(property);
        var propertyResult = CSharpPrinter.PrintRaised(property);
        Assert.True(propertyResult.Succeeded);
        string propertyOutput = propertyResult.Output!.ReplaceLineEndings("\n");
        Assert.Contains("node?.Label", propertyOutput);
        // The reused slot must not declare as the receiver type — that is the
        // unsound slot-conflation the raise removes (config-agnostic spelling).
        Assert.DoesNotContain("JoinBase S_", propertyOutput);
        Assert.DoesNotContain("JoinBase V_", propertyOutput);

        var call = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.NullConditionalCall));
        Assert.NotNull(call);
        var callResult = CSharpPrinter.PrintRaised(call);
        Assert.True(callResult.Succeeded);
        string callOutput = callResult.Output!.ReplaceLineEndings("\n");
        Assert.Contains("node?.Shape()", callOutput);
    }

    [Fact]
    public void StraightLine_PrintsCurrentStyle()
    {
        Assert.Equal("return a + b;\n", PrintFixture(nameof(CfgSampleClass.Add)));
    }

    [Fact]
    public void Branches_PrintAsHonestLabelsAndGotos()
    {
        string output = PrintFixture(nameof(CfgSampleClass.AbsShort));

        Assert.Contains("goto IL_", output);
        Assert.Contains(":", output);
        Assert.DoesNotContain("/* ", output);  // every node has a rendering
    }

    [Fact]
    public void UnsignedConversion_CastsSignedSource()
    {
        // conv.r.un on a signed int: the source reads as unsigned, so the
        // C# spelling needs the (uint) cast or the value is wrong for
        // negative inputs.
        var convert = new Pipeline.Convert(
            TypeRef.CoreLib("System", "Double"), isChecked: false, isUnsigned: true,
            new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32")));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(convert));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Double"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (double)(uint)a;", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void TypedConstants_BoxedAndElementConstants_Retype()
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var boxed = new Box(boolType, new Constant(1, TypeRef.CoreLib("System", "Int32")));
        block.Add(new ExpressionStatement(boxed));
        block.Add(new StoreElement(boolType,
            new LoadArgument(0, "flags", TypeRef.SzArray(boolType)),
            new Constant(0, TypeRef.CoreLib("System", "Int32")),
            new Constant(1, TypeRef.CoreLib("System", "Int32"))));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("flags", TypeRef.SzArray(boolType))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        new TypedConstantsPass().Run(function, PassContext.None);

        Assert.Equal(true, ((Constant)boxed.Operand).Value);
        var store = function.Descendants.OfType<StoreElement>().Single();
        Assert.Equal(true, ((Constant)store.Value).Value);
        // The index constant stays int — element typing applies to the value.
        Assert.Equal(0, ((Constant)store.Index).Value);
        function.CheckInvariant();
    }

    [Fact]
    public void UnsignedOperations_CastSignedOperands_PlainWhenAlreadyUnsigned()
    {
        var signedInt = new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32"));
        var signedInt2 = new LoadArgument(1, "b", TypeRef.CoreLib("System", "Int32"));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: true, signedInt, signedInt2)));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "UInt32"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32")), new Parameter("b", TypeRef.CoreLib("System", "Int32"))],
            HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return (uint)a / (uint)b;", CSharpPrinter.Print(function).Output!.Trim());

        // Already-unsigned operands print plain — div.un's semantics are
        // already conveyed by the types.
        var unsignedArg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "UInt32"));
        var unsignedArg2 = new LoadArgument(1, "b", TypeRef.CoreLib("System", "UInt32"));
        var container2 = new BlockContainer();
        var block2 = new Block(0);
        container2.Add(block2);
        block2.Add(new Return(new Binary(BinaryKind.Divide, isChecked: false, isUnsigned: true, unsignedArg, unsignedArg2)));
        var function2 = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container2);

        Assert.Equal("return a / b;", CSharpPrinter.Print(function2).Output!.Trim());
    }

    [Fact]
    public void NumericBoundary_SameFamilySignChange_Casts()
    {
        // A uint value returned from an int-returning method needs (int): same
        // 4-byte stack family, but not implicitly convertible (CS0266).
        Assert.Equal("return (int)a;",
            ReturnOf("UInt32", "Int32"));
        // long into ulong return — same I8 family, sign change.
        Assert.Equal("return (ulong)a;",
            ReturnOf("Int64", "UInt64"));
    }

    [Fact]
    public void NumericBoundary_ImplicitWidening_NoCast()
    {
        // byte → int and ushort → int are implicit C# conversions; no cast.
        Assert.Equal("return a;", ReturnOf("Byte", "Int32"));
        Assert.Equal("return a;", ReturnOf("UInt16", "Int32"));
    }

    [Fact]
    public void NumericBoundary_StoreNarrowsToSlotType_Casts()
    {
        // A ushort value stored into a char local — both 2-byte, ushort→char is
        // not implicit, so the declaring store casts to the slot type.
        var arg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "UInt16"));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, TypeRef.CoreLib("System", "Char"), arg));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", TypeRef.CoreLib("System", "UInt16"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [TypeRef.CoreLib("System", "Char")], container);

        Assert.Contains("char V_0 = (char)a;", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void NumericBoundary_InRangeConstant_RendersBare()
    {
        // 5 is in uint's range, so C#'s constant-expression conversion applies —
        // no cast needed.
        Assert.Equal("return 5;", ReturnConstant(5, "Int32", "UInt32"));
    }

    [Fact]
    public void NumericBoundary_OutOfRangeConstant_UncheckedCast()
    {
        // -1 is out of uint's range (uint.MaxValue's ldc.i4.m1); a bare literal
        // is CS0031, so reinterpret the bits with an unchecked cast.
        Assert.Equal("return unchecked((uint)(-1));", ReturnConstant(-1, "Int32", "UInt32"));
    }

    [Fact]
    public void NumericBoundary_SameWidthConv_SingleCast()
    {
        // A conv.u2 (→ ushort) feeding a char slot renders as one (char) cast on
        // the conversion's operand, not the redundant (char)((ushort)x).
        var arg = new LoadArgument(0, "a", TypeRef.CoreLib("System", "Int32"));
        var conv = new ILInspector.Decompiler.Pipeline.Convert(TypeRef.CoreLib("System", "UInt16"), isChecked: false, isUnsigned: false, arg);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreLocal(0, TypeRef.CoreLib("System", "Char"), conv));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", TypeRef.CoreLib("System", "Int32"))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [TypeRef.CoreLib("System", "Char")], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("char V_0 = (char)a;", output);
        Assert.DoesNotContain("(ushort)", output);
    }

    [Fact]
    public void BackingField_RendersAsProperty()
    {
        // An auto-property backing field <Count>k__BackingField has no spellable
        // C# name; a store/load on `this` renders as this.Count (the property),
        // which also disambiguates a constructor parameter that shadows it.
        var intType = TypeRef.CoreLib("System", "Int32");
        var declType = TypeRef.CoreLib("Synthetic", "T");
        var backing = new FieldRef(declType, "<Count>k__BackingField", intType)
        {
            BackingPropertyName = "Count",
        };
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new StoreField(backing, new LoadArgument(0, "this", declType), new LoadArgument(1, "Count", intType)));
        block.Add(new Return(new LoadField(backing, new LoadArgument(0, "this", declType))));
        var signature = new MethodSignature(intType,
            [new Parameter("Count", intType)], HasThis: true, GenericParameterCount: 0);
        var function = new IrFunction("M", declType, signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("this.Count = Count;", output);
        Assert.Contains("return this.Count;", output);
        Assert.DoesNotContain("k__BackingField", output);
    }

    [Fact]
    public void ImportedAutoPropertyBackingField_RendersAsProperty()
    {
        string output = PrintFixture("get_CompoundProperty");

        Assert.Contains("return this.CompoundProperty;", output);
        Assert.DoesNotContain("k__BackingField", output);
    }

    [Fact]
    public void CallArgument_NumericMismatch_Casts()
    {
        // M(uint) into an int parameter: the argument casts to the parameter
        // type, the call-site counterpart of the return/store boundary casts.
        var intType = TypeRef.CoreLib("System", "Int32");
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var callee = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "M",
            TypeRef.CoreLib("System", "Void"), [intType], HasThis: false);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ExpressionStatement(new Call(callee, isVirtual: false, [new LoadArgument(0, "a", uintType)])));
        block.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", uintType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("F", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Contains("M((int)a);", CSharpPrinter.Print(function).Output!);
    }

    [Fact]
    public void ConvertOfOutOfRangeConstant_UsesUnchecked()
    {
        // conv.u8 of ldc.i4.m1 ZERO-extends the int32 source: the value is
        // 0x00000000FFFFFFFF = uint.MaxValue, NOT the sign-extended ulong.MaxValue.
        // A bare `(ulong)(-1)` sign-extends (= ulong.MaxValue) — a silent wrong
        // value — so the unsigned widening reinterprets through the unsigned source
        // sibling: `(ulong)(uint)(-1)` = uint.MaxValue, round-tripping to conv.u8 (#2101).
        var conv = new ILInspector.Decompiler.Pipeline.Convert(
            TypeRef.CoreLib("System", "UInt64"), isChecked: false, isUnsigned: false,
            new Constant(-1, TypeRef.CoreLib("System", "Int32")));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(conv));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "UInt64"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return unchecked((ulong)(uint)(-1));", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void SameWidthUnsignedReinterpretOfNegativeConstant_StaysBare()
    {
        // A same-width reinterpret (long -> ulong) has no zero/sign-extension
        // choice, so the fix must NOT insert a `(uint)` sibling — `(ulong)(-1)`
        // is the faithful bit reinterpret (#2101 boundary).
        var conv = new ILInspector.Decompiler.Pipeline.Convert(
            TypeRef.CoreLib("System", "UInt64"), isChecked: false, isUnsigned: false,
            new Constant(-1, TypeRef.CoreLib("System", "Int64")));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(conv));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "UInt64"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return unchecked((ulong)(-1));", CSharpPrinter.Print(function).Output!.Trim());
    }

    [Fact]
    public void ConvertU8OfSignedInt_InsertsUnsignedSibling()
    {
        // conv.u8 of a non-constant SIGNED int zero-extends the 32-bit stack value;
        // a bare `(ulong)i` sign-extends (wrong value for negative i). `(ulong)(uint)i`
        // reinterprets faithfully and round-trips to conv.u8 (#2336).
        Assert.Equal("return (ulong)(uint)i;", RenderConvertReturn("UInt64", "Int32"));
    }

    [Fact]
    public void ConvertU8OfSbyte_ZeroExtendsAtStackWidth_NotDeclaredWidth()
    {
        // A sub-int operand is sign-extended to int32 on the CIL stack BEFORE
        // conv.u8, so the zero-extension is at the stack width: `(ulong)(uint)s`,
        // never `(ulong)(byte)s` (= 255 for sbyte -1, the wrong value) (#2336).
        Assert.Equal("return (ulong)(uint)s;", RenderConvertReturn("UInt64", "SByte", "s"));
    }

    [Fact]
    public void ConvertU8OfUnsignedOperand_StaysBare()
    {
        // An already-unsigned operand's `(ulong)u` already zero-extends — inserting
        // a redundant `(uint)` would be churn, so the fix must not fire (#2336).
        Assert.Equal("return (ulong)u;", RenderConvertReturn("UInt64", "UInt32", "u"));
    }

    [Fact]
    public void ConvertToNuintOfSignedInt_InsertsUnsignedSibling()
    {
        // conv.u (native unsigned) of a signed int zero-extends the 32-bit stack
        // value on 64-bit; `(nuint)(uint)i` is faithful on both platform widths (#2336).
        Assert.Equal("return (nuint)(uint)i;", RenderConvertReturn("UIntPtr", "Int32"));
    }

    [Fact]
    public void ConvertU8OfLong_StaysBare()
    {
        // A long source is already the ulong stack width, so `(ulong)l` is a faithful
        // reinterpret with no zero/sign-extension choice — the fix must not fire (#2336).
        Assert.Equal("return (ulong)l;", RenderConvertReturn("UInt64", "Int64", "l"));
    }

    [Fact]
    public void SignedWidenToLong_StaysBare()
    {
        // A SIGNED target keeps sign-extension (conv.i8): `(long)i` is faithful, the
        // fix only applies to unsigned/native widening targets (#2336).
        Assert.Equal("return (long)i;", RenderConvertReturn("Int64", "Int32"));
    }

    [Fact]
    public void CheckedConvOvfU8OfSignedInt_StaysBare()
    {
        // conv.ovf.u8 (checked) of a signed int throws for a negative value, so
        // `checked((ulong)i)` already matches it; inserting `(uint)` would recompile
        // to conv.ovf.u4;conv.u8 (wrong opcodes), so the fix must not fire (#2336).
        var source = TypeRef.CoreLib("System", "Int32");
        var conv = new ILInspector.Decompiler.Pipeline.Convert(
            TypeRef.CoreLib("System", "UInt64"), isChecked: true, isUnsigned: false,
            new LoadArgument(0, "i", source));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(conv));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", "UInt64"), [new Parameter("i", source)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        Assert.Equal("return checked((ulong)i);", CSharpPrinter.Print(function).Output!.Trim());
    }

    static string RenderConvertReturn(string targetType, string sourceType, string paramName = "i")
    {
        var source = TypeRef.CoreLib("System", sourceType);
        var conv = new ILInspector.Decompiler.Pipeline.Convert(
            TypeRef.CoreLib("System", targetType), isChecked: false, isUnsigned: false,
            new LoadArgument(0, paramName, source));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(conv));
        var signature = new MethodSignature(
            TypeRef.CoreLib("System", targetType), [new Parameter(paramName, source)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string ReturnConstant(int value, string constType, string returnType)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new Constant(value, TypeRef.CoreLib("System", constType))));
        var signature = new MethodSignature(TypeRef.CoreLib("System", returnType), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    static string ReturnOf(string sourceType, string returnType)
    {
        var arg = new LoadArgument(0, "a", TypeRef.CoreLib("System", sourceType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(arg));
        var signature = new MethodSignature(TypeRef.CoreLib("System", returnType),
            [new Parameter("a", TypeRef.CoreLib("System", sourceType))], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    [Fact]
    public void Constructor_InitializerOnlyResult_RemainsSuccessful()
    {
        // A no-argument base-constructor call is implicit in C#, and the field
        // initializer (`_shadowed = 1`) the compiler emits before that base call
        // lifts to the field declaration — not the body, where it would
        // recompile to AFTER the base call.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, ".ctor");

        Assert.NotNull(function);
        var result = CSharpPrinter.Print(function);
        string output = result.Output!;
        Assert.True(result.Succeeded);
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);
        Assert.Empty(output);
        Assert.DoesNotContain("base(", output);
        Assert.DoesNotContain(".ctor", output);
        Assert.DoesNotContain("_shadowed", output);
        Assert.Contains(("_shadowed", "1"), result.FieldInitializers);
    }

    [Fact]
    public void UnsignedComparison_InverseCondition_KeepsUnsignedCasts()
    {
        // brfalse over an unsigned comparison folds to the inverse operator;
        // the unsigned operand casts must survive the fold.
        var intType = TypeRef.CoreLib("System", "Int32");
        var comparison = new Comparison(ComparisonKind.LessThan, isUnsigned: true,
            new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType));
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new ConditionalBranch(new LogicalNot(comparison), 4));
        var target = new Block(4);
        container.Add(target);
        target.Add(new Return(null));
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"),
            [new Parameter("a", intType), new Parameter("b", intType)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("if ((uint)a >= (uint)b) goto IL_0004;", output);
    }

    [Fact]
    public void StraightLineCoreLibMethod_RendersExpected()
    {
        // The simplest class: a method needing no raising at all.
        using var source = MetadataSource.Open(typeof(object).Assembly.Location);
        var function = IrImporter.Import(source, "System.Collections.Generic.List`1", "get_Count");
        Assert.NotNull(function);

        string candidate = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Equal("return _size;", candidate);
    }

    [Fact]
    public void StoreElement_InlinesSingleUseAddressReceiverTemp()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var stringArray = TypeRef.SzArray(stringType);
        var intType = TypeRef.CoreLib("System", "Int32");
        var charType = TypeRef.CoreLib("System", "Char");
        var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [charType]);
        var memoryExtensions = TypeRef.CoreLib("System", "MemoryExtensions");
        var trim = new MethodRef(memoryExtensions, "Trim", spanType, [spanType], HasThis: false);
        var toString = new MethodRef(spanType, "ToString", stringType, [], HasThis: true);
        var block = new Block(0);
        block.Add(new StoreLocal(0, spanType, new Call(trim, isVirtual: false, [new LoadArgument(2, "item", spanType)])));
        block.Add(new StoreElement(
            elementType: null,
            new LoadArgument(0, "items", stringArray),
            new LoadArgument(1, "i", intType),
            new Call(toString, isVirtual: true, [new LoadLocalAddress(0, spanType)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [
                new Parameter("items", stringArray),
                new Parameter("i", intType),
                new Parameter("item", spanType),
            ], HasThis: false, GenericParameterCount: 0),
            [spanType],
            body);

        RunStoreElementReceiverInlining(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        var facts = CSharpPrinter.CollectDataflowFacts(function);

        Assert.Equal("items[i] = MemoryExtensions.Trim(item).ToString();", output);
        Assert.Equal(["V_0 (eliminated)"], facts.LocalNames);
        Assert.DoesNotContain(
            0,
            LocalDeclarationPlan.Create(function, 1)
                .RetainedLocalSlots);
    }

    [Fact]
    public void StoreElement_KeepsAddressReceiverTempWithAdditionalUse()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var stringArray = TypeRef.SzArray(stringType);
        var intType = TypeRef.CoreLib("System", "Int32");
        var charType = TypeRef.CoreLib("System", "Char");
        var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [charType]);
        var memoryExtensions = TypeRef.CoreLib("System", "MemoryExtensions");
        var trim = new MethodRef(memoryExtensions, "Trim", spanType, [spanType], HasThis: false);
        var toString = new MethodRef(spanType, "ToString", stringType, [], HasThis: true);
        var consume = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "Consume", TypeRef.CoreLib("System", "Void"), [spanType], HasThis: false)
        {
            ParameterRefKinds = [ArgumentRefKind.In],
        };
        var block = new Block(0);
        block.Add(new StoreLocal(0, spanType, new Call(trim, isVirtual: false, [new LoadArgument(2, "item", spanType)])));
        block.Add(new StoreElement(
            elementType: null,
            new LoadArgument(0, "items", stringArray),
            new LoadArgument(1, "i", intType),
            new Call(toString, isVirtual: true, [new LoadLocalAddress(0, spanType)])));
        block.Add(new ExpressionStatement(new Call(consume, isVirtual: false, [new LoadLocalAddress(0, spanType)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [
                new Parameter("items", stringArray),
                new Parameter("i", intType),
                new Parameter("item", spanType),
            ], HasThis: false, GenericParameterCount: 0),
            [spanType],
            body);

        RunStoreElementReceiverInlining(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Contains("ReadOnlySpan<char> V_0 = MemoryExtensions.Trim(item);", output);
        Assert.Contains("items[i] = V_0.ToString();", output);
    }

    [Fact]
    public void StoreElement_KeepsAddressReceiverTempWhenTargetHasSideEffects()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var stringArray = TypeRef.SzArray(stringType);
        var intType = TypeRef.CoreLib("System", "Int32");
        var charType = TypeRef.CoreLib("System", "Char");
        var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [charType]);
        var memoryExtensions = TypeRef.CoreLib("System", "MemoryExtensions");
        var trim = new MethodRef(memoryExtensions, "Trim", spanType, [spanType], HasThis: false);
        var toString = new MethodRef(spanType, "ToString", stringType, [], HasThis: true);
        var getItems = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "GetItems", stringArray, [], HasThis: false);
        var block = new Block(0);
        block.Add(new StoreLocal(0, spanType, new Call(trim, isVirtual: false, [new LoadArgument(1, "item", spanType)])));
        block.Add(new StoreElement(
            elementType: null,
            new Call(getItems, isVirtual: false, []),
            new LoadArgument(0, "i", intType),
            new Call(toString, isVirtual: true, [new LoadLocalAddress(0, spanType)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [
                new Parameter("i", intType),
                new Parameter("item", spanType),
            ], HasThis: false, GenericParameterCount: 0),
            [spanType],
            body);

        RunStoreElementReceiverInlining(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Contains("ReadOnlySpan<char> V_0 = MemoryExtensions.Trim(item);", output);
        Assert.Contains("GetItems()[i] = V_0.ToString();", output);
    }

    [Fact]
    public void StoreElement_KeepsAddressReceiverTempWhenInitializerReferencesTarget()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var stringArray = TypeRef.SzArray(stringType);
        var intType = TypeRef.CoreLib("System", "Int32");
        var charType = TypeRef.CoreLib("System", "Char");
        var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [charType]);
        var toString = new MethodRef(spanType, "ToString", stringType, [], HasThis: true);
        var next = new MethodRef(TypeRef.CoreLib("Synthetic", "T"), "Next", spanType, [intType], HasThis: false);
        var block = new Block(0);
        block.Add(new StoreLocal(0, spanType, new Call(next, isVirtual: false, [new LoadArgument(1, "i", intType)])));
        block.Add(new StoreElement(
            elementType: null,
            new LoadArgument(0, "items", stringArray),
            new LoadArgument(1, "i", intType),
            new Call(toString, isVirtual: true, [new LoadLocalAddress(0, spanType)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [
                new Parameter("items", stringArray),
                new Parameter("i", intType),
            ], HasThis: false, GenericParameterCount: 0),
            [spanType],
            body);

        RunStoreElementReceiverInlining(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Contains("ReadOnlySpan<char> V_0 = Next(i);", output);
        Assert.Contains("items[i] = V_0.ToString();", output);
    }

    [Fact]
    public void StoreElement_InlinedUnsafeReceiverTempCarriesUnsafeContext()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var stringArray = TypeRef.SzArray(stringType);
        var intType = TypeRef.CoreLib("System", "Int32");
        var intPointer = TypeRef.Pointer(intType);
        var toString = new MethodRef(intType, "ToString", stringType, [], HasThis: true);
        var block = new Block(0);
        block.Add(new StoreLocal(0, intType, new LoadIndirect(intType, new LoadArgument(2, "p", intPointer))));
        block.Add(new StoreElement(
            elementType: null,
            new LoadArgument(0, "items", stringArray),
            new LoadArgument(1, "i", intType),
            new Call(toString, isVirtual: true, [new LoadLocalAddress(0, intType)])));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [
                new Parameter("items", stringArray),
                new Parameter("i", intType),
                new Parameter("p", intPointer),
            ], HasThis: false, GenericParameterCount: 0),
            [intType],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        RunStoreElementReceiverInlining(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");

        Assert.Contains("unsafe", output);
        Assert.Contains("items[i] = (*p).ToString();", output);
        Assert.DoesNotContain("int V_0", output);
    }

    [Fact]
    public void StoreElement_KeepsAddressReceiverTempCapturedByLocalFunction()
    {
        var stringType = TypeRef.CoreLib("System", "String");
        var stringArray = TypeRef.SzArray(stringType);
        var intType = TypeRef.CoreLib("System", "Int32");
        var charType = TypeRef.CoreLib("System", "Char");
        var spanType = TypeRef.GenericInstance(TypeRef.CoreLib("System", "ReadOnlySpan`1"), [charType]);
        var memoryExtensions = TypeRef.CoreLib("System", "MemoryExtensions");
        var trim = new MethodRef(memoryExtensions, "Trim", spanType, [spanType], HasThis: false);
        var toString = new MethodRef(spanType, "ToString", stringType, [], HasThis: true);
        var block = new Block(0);
        block.Add(new StoreLocal(0, spanType, new Call(trim, isVirtual: false, [new LoadArgument(2, "item", spanType)])));
        block.Add(new StoreElement(
            elementType: null,
            new LoadArgument(0, "items", stringArray),
            new LoadArgument(1, "i", intType),
            new Call(toString, isVirtual: true, [new LoadLocalAddress(0, spanType)])));
        var nestedBody = new BlockContainer();
        var nestedBlock = new Block(0);
        nestedBody.Add(nestedBlock);
        nestedBlock.Add(new Return(new LoadLocal(0, spanType)));
        block.Add(new LocalFunctionStatement(
            "Capture",
            spanType,
            [],
            isStatic: false,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            nestedBody));
        block.Add(new Return(null));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "T"),
            new MethodSignature(TypeRef.CoreLib("System", "Void"), [
                new Parameter("items", stringArray),
                new Parameter("i", intType),
                new Parameter("item", spanType),
            ], HasThis: false, GenericParameterCount: 0),
            [spanType],
            body);

        RunStoreElementReceiverInlining(function);
        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();

        Assert.Contains("ReadOnlySpan<char> V_0 = MemoryExtensions.Trim(item);", output);
        Assert.Contains("items[i] = V_0.ToString();", output);
    }
}
