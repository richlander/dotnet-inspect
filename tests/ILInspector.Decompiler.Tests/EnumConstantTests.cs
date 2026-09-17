using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using MethodDefinitionHandle = System.Reflection.Metadata.MethodDefinitionHandle;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Enum-constant naming: an integer flowing into an enum position retypes to
/// the enum and prints as EnumType.Member from the resolved same-assembly
/// member map. A value with no single member casts to the enum rather than
/// rendering a bare int (which would be CS0266).
/// </summary>
public class EnumConstantTests
{
    [Fact]
    public void GenericArgumentEnumShape_RegistersEnumMembers()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReadOnlySpanEnumFirst));
        Assert.NotNull(function);

        var enumType = function!.Signature.Parameters[0].Type.TypeArguments[0];

        Assert.Equal(TypeShape.Enum, function.TypeShapes.GetValueOrDefault(enumType));
        Assert.True(function.EnumMembers.TryGetValue(enumType, out var members));
        Assert.Contains(members!, member => member.Value == nameof(CfgPriority.High));
    }

    [Fact]
    public void EnumArgument_RendersMemberName()
    {
        // TakesPriority(CfgPriority.High) — the ldc.i4.2 names as High, not 2.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CallWithHighPriority), source);

        Assert.Contains("CfgPriority.High", output);
        Assert.DoesNotContain("TakesPriority(2)", output);
    }

    [Fact]
    public void HighBitUnsignedEnumMember_Names()
    {
        // CfgFlags.Top = 0x80000000 (uint) emits as int -2147483648. The
        // member-map key must reinterpret the uint as a signed int to match,
        // or this falls back to the raw -2147483648.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.CallWithTopFlag), source);

        Assert.Contains("CfgFlags.Top", output);
        Assert.DoesNotContain("-2147483648", output);
    }

    [Fact]
    public void ExactMember_Names_UnmatchedValue_Casts()
    {
        var enumType = TypeRef.Definition("asm", "NS", "Color");
        var members = new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>
        {
            [enumType] = new Dictionary<long, string> { [1] = "Red", [2] = "Green" },
        };

        Assert.Equal("Color.Red", PrintEnumConstant(1, enumType, members));
        Assert.Equal("Color.Green", PrintEnumConstant(2, enumType, members));
        // 3 names no member (a composite flag value); a bare int would be CS0266,
        // so it casts. Naming flag combinations is a later slice.
        Assert.Equal("(Color)3", PrintEnumConstant(3, enumType, members));
        // A negative high-bit value must be parenthesized after the cast (CS0075).
        Assert.Equal("(Color)(-2147483648)", PrintEnumConstant(int.MinValue, enumType, members));
    }

    [Fact]
    public void EnumWithNoResolvedMembers_Casts()
    {
        // A cross-assembly enum absent from the member map still casts (the
        // value can't be named, but a bare int into the enum is CS0266).
        var enumType = TypeRef.Definition("other", "NS", "Mystery");
        Assert.Equal("(Mystery)5", PrintEnumConstant(5, enumType, new Dictionary<TypeRef, IReadOnlyDictionary<long, string>>()));
    }

    [Fact]
    public void EnumReturn_NamesMember_FromSeededSignature()
    {
        // The enum is reached only via the return-type signature; the constant
        // names CfgPriority.High only because ResolveTypeInfo seeds it.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ReturnsHighPriority), source);

        Assert.Contains("return CfgPriority.High;", output);
        Assert.DoesNotContain("return 2;", output);
    }

    [Fact]
    public void EnumBitwiseOperand_NamesMember()
    {
        // p & CfgPriority.High — the ldc.i4.2 beside the enum retypes, so the
        // mask is not the CS0019 `p & 2`.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.MaskHighPriority), source);

        Assert.Contains("CfgPriority.High", output);
        Assert.DoesNotContain("& 2", output);
    }

    [Fact]
    public void NonConstantIntToEnum_InsertsCast()
    {
        // (CfgPriority)value — a non-constant int into an enum-typed position has
        // no IL conv to lean on, so the printer must spell the explicit cast;
        // a bare `return value;` is CS0266 (int is not implicitly an enum).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.ToPriority), source);

        Assert.Contains("return (CfgPriority)value;", output);
    }

    [Fact]
    public void ValueTypeThisRead_RendersAsThis()
    {
        // `ldarg.0; ldobj` reads the value through the `this` managed pointer;
        // C#'s `this` already denotes the value, so it renders bare — `*this`
        // would be CS0193 (`this` is not an unmanaged pointer).
        using var source = MetadataSource.Open(typeof(CfgSelf).Assembly.Location);
        string output = new RaisingPassTestsAccessor().Print(
            typeof(CfgSelf).FullName!, nameof(CfgSelf.Identity), source);

        Assert.Contains("return this;", output);
        Assert.DoesNotContain("*this", output);
    }

    [Fact]
    public void TernaryIntoEnum_CastsWholeTernary()
    {
        // `StringComparison V_0 = flag ? 1 : 0;` is int->enum (CS0266). A ternary
        // of integer arms into an enum local must take the enum cast on the whole
        // merge — `(MyEnum)(flag ? 1 : 0)` — even though Coerce otherwise
        // leaves merge nodes uncast (the bail's CS0030 risk is type-parameter-only,
        // not a concrete enum).
        var enumType = TypeRef.Definition("asm", "NS", "MyEnum");
        var intType = TypeRef.CoreLib("System", "Int32");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var ternary = new Conditional(new LoadArgument(0, "flag", boolType),
            new Constant(1, intType), new Constant(0, intType));
        var block = new Block(0);
        block.Add(new StoreLocal(0, enumType, ternary));
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [enumType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains(")(flag ? 1 : 0)", output);
        Assert.DoesNotContain("= flag ? 1 : 0;", output);
    }

    [Fact]
    public void IndirectStoreThroughTypedPointer_CastsToPointerElement()
    {
        // `*(uint*)p = (int)x` is int->uint (CS0266): a primitive `stind.i4`
        // carries `int`, but the C# lvalue `*p` is typed by the pointer (uint).
        // The store must cast to the pointer's element type, not the opcode's —
        // here a uint value into a uint* needs no cast at all.
        var uintType = TypeRef.CoreLib("System", "UInt32");
        var intType = TypeRef.CoreLib("System", "Int32");
        var address = new LoadLocal(0, TypeRef.Pointer(uintType));
        var value = new LoadLocal(1, uintType);
        var block = new Block(0);
        block.Add(new StoreIndirect(intType, address, value));
        block.Add(new Return(null));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [TypeRef.Pointer(uintType), uintType], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("*V_0 = V_1;", output);
        Assert.DoesNotContain("(int)", output);
    }

    [Fact]
    public void NotOverEqualityOperatorCall_FoldsToDirectComparison()
    {
        // !(a != b) is an exact logical negation of a != b for any type (including
        // NaN-like partial orders: NaN != NaN is true, so !(NaN != NaN) and
        // NaN == NaN agree), so the printer folds it directly to `a == b` rather
        // than parenthesizing the un-folded operator-spelled call (#2955).
        var type = TypeRef.CoreLib("System", "Type");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var callee = new MethodRef(type, "op_Inequality", boolType, [type, type], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var inequality = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", type), new LoadArgument(1, "b", type)]);
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(inequality)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("a == b", output);
        Assert.DoesNotContain("!(a != b)", output);
        Assert.DoesNotContain("!a != b", output);
    }

    [Fact]
    public void NotOverRelationalOperatorCall_OutsideTotalOrderAllowlist_Parenthesizes()
    {
        // !(a > b) — a relational operator-spelled call renders as a compound
        // `a > b`, so the enclosing `!` must parenthesize it; a bare `!a > b`
        // binds as `(!a) > b` (CS0023). System.Type is NOT in the total-order
        // relational allowlist (MemberIdentity.IsTotalOrderRelationalOperator —
        // decimal/DateTime/DateTimeOffset/TimeSpan/DateOnly/TimeOnly), so this
        // relational call is NOT folded and the un-folded node must still be
        // built and parenthesized directly (#2955).
        var type = TypeRef.CoreLib("System", "Type");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var callee = new MethodRef(type, "op_GreaterThan", boolType, [type, type], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var greaterThan = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", type), new LoadArgument(1, "b", type)]);
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(greaterThan)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("!(a > b)", output);
        Assert.DoesNotContain("!a > b", output);
    }

    [Fact]
    public void NotOverEqualityOperatorCall_WithoutOperatorMetadata_DoesNotFold()
    {
        // A method literally named "op_Equality" that is NOT flagged as an
        // operator (no specialname/operator metadata, not a recognized core
        // operator type) must not be mistaken for the real operator and must
        // not be folded or spelled with `==` (#2955 scope guard).
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "NotAnOperatorLibrary");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var type = TypeRef.CoreLib("System", "Type");
        var callee = new MethodRef(declaring, "op_Equality", boolType, [type, type], HasThis: false)
        {
            IsSpecialName = false,
            IsOperator = MetadataFactState.No,
        };
        var equality = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", type), new LoadArgument(1, "b", type)]);
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(equality)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.DoesNotContain("a != b", output);
        Assert.Contains("op_Equality", output);
    }

    [Fact]
    public void NotOverEqualityOperatorCall_OnUserDefinedType_DoesNotFold()
    {
        // C# requires `==`/`!=` to be declared as a pair, but does NOT
        // require their implementations to be logical inverses of each
        // other — a user-defined `operator ==`/`operator !=` pair could
        // legally implement unrelated semantics. The BCL guarantees
        // String/Type's op_Equality/op_Inequality genuinely are exact
        // inverses (MemberIdentity.IsKnownCoreLibraryOperator), but an
        // arbitrary user type carrying real specialname/operator metadata
        // is NOT in that trusted set, so `!(a == b)` must stay un-folded
        // and parenthesized here even though it still spells as `a == b`
        // via the pre-existing operator-call spelling (#2955 scope guard).
        var declaring = TypeRef.Definition("UserAssembly", "UserAssembly", "Vector");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var callee = new MethodRef(declaring, "op_Equality", boolType, [declaring, declaring], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var equality = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", declaring), new LoadArgument(1, "b", declaring)]);
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(equality)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("!(a == b)", output);
        Assert.DoesNotContain("a != b", output);
    }

    static string PrintNegatedRelationalReturn(TypeRef declaringType, string operatorName)
    {
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var callee = new MethodRef(declaringType, operatorName, boolType, [declaringType, declaringType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var call = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", declaringType), new LoadArgument(1, "b", declaringType)]);
        var block = new Block(0);
        block.Add(new Return(new LogicalNot(call)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);
        return CSharpPrinter.Print(function).Output!.Trim();
    }

    [Theory]
    [InlineData("op_LessThan", "a >= b")]
    [InlineData("op_LessThanOrEqual", "a > b")]
    [InlineData("op_GreaterThan", "a <= b")]
    [InlineData("op_GreaterThanOrEqual", "a < b")]
    public void NotOverRelationalOperatorCall_OnTotalOrderType_FoldsToDualComparison(string operatorName, string expected)
    {
        // For a total-order core-library value type (here System.DateTime), the
        // four relational operators are exact De Morgan duals for every input —
        // there is no unordered/NaN pair — so the printer folds a negated
        // relational operator-spelled call directly to the dual comparison:
        // !(a < b) -> a >= b, !(a <= b) -> a > b, !(a > b) -> a <= b,
        // !(a >= b) -> a < b (#2955).
        string output = PrintNegatedRelationalReturn(TypeRef.CoreLib("System", "DateTime"), operatorName);

        Assert.Contains($"return {expected};", output);
        Assert.DoesNotContain("op_", output);
        Assert.DoesNotContain("!(", output);
    }

    [Fact]
    public void NotOverRelationalOperatorCall_OnDecimal_FoldsToDualComparison()
    {
        // decimal is a total order (no NaN), so !(a < b) is exactly a >= b.
        // Confirms the fold is keyed on the whole allowlist, not one type (#2955).
        string output = PrintNegatedRelationalReturn(TypeRef.CoreLib("System", "Decimal"), "op_LessThan");

        Assert.Contains("return a >= b;", output);
        Assert.DoesNotContain("!(a < b)", output);
    }

    [Fact]
    public void NotOverRelationalOperatorCall_InCondition_FoldsToDualComparison()
    {
        // The value-position (return) and condition-position (if) switches share
        // the same fold; an `if (!(a < b))` on a total-order type spells
        // `if (a >= b)` (#2955).
        var timeSpan = TypeRef.CoreLib("System", "TimeSpan");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var voidType = TypeRef.CoreLib("System", "Void");
        var callee = new MethodRef(timeSpan, "op_LessThan", boolType, [timeSpan, timeSpan], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Yes,
        };
        var call = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", timeSpan), new LoadArgument(1, "b", timeSpan)]);
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        var then = new Block(4);
        then.Add(new Return(null));
        block.Add(new IfStatement(new LogicalNot(call), then, null));
        block.Add(new Return(null));
        var signature = new MethodSignature(voidType,
            [new Parameter("a", timeSpan), new Parameter("b", timeSpan)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("if (a >= b)", output);
        Assert.DoesNotContain("!(a < b)", output);
    }

    [Fact]
    public void NotOverRelationalOperatorCall_OnHalf_DoesNotFold()
    {
        // System.Half uses operator CALLS (not native clt/cgt) AND is an
        // IEEE-754 partial order: NaN is unordered, so when a or b is NaN,
        // !(a < b) is true while a >= b is false. Folding !(a < b) to a >= b
        // would therefore change behavior, so Half is deliberately EXCLUDED
        // from the total-order allowlist and the negated relational call must
        // stay un-folded and parenthesized (#2955 soundness guard).
        string output = PrintNegatedRelationalReturn(TypeRef.CoreLib("System", "Half"), "op_LessThan");

        Assert.Contains("!(a < b)", output);
        Assert.DoesNotContain("a >= b", output);
    }

    [Fact]
    public void NotOverRelationalOperatorCall_OnUserDefinedType_DoesNotFold()
    {
        // A user-defined type's operator < and >= need not be exact duals (they
        // can implement a partial order or unrelated semantics), so an arbitrary
        // user type carrying real specialname/operator metadata is NOT in the
        // trusted total-order allowlist; the negated relational call must stay
        // un-folded and parenthesized even though it still spells `a < b` via the
        // operator-call spelling (#2955 scope guard).
        var declaring = TypeRef.Definition("UserAssembly", "UserAssembly", "Vector");
        string output = PrintNegatedRelationalReturn(declaring, "op_LessThan");

        Assert.Contains("!(a < b)", output);
        Assert.DoesNotContain("a >= b", output);
    }

    [Fact]
    public void ResolvedNonOperatorOpAddition_RendersAsMethodCall()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "OperatorLikeLibrary");
        var callee = new MethodRef(declaring, "op_Addition", intType, [intType, intType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.No,
        };
        var call = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains(".op_Addition(a, b)", output);
        Assert.DoesNotContain("a + b", output);
    }

    [Fact]
    public void ResolvedNonOperatorOpImplicit_RendersAsMethodCall()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "OperatorLikeLibrary");
        var callee = new MethodRef(declaring, "op_Implicit", intType, [intType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.No,
        };
        var call = new Call(callee, isVirtual: false, [new LoadArgument(0, "value", intType)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains(".op_Implicit(value)", output);
        Assert.DoesNotContain("return (int)value;", output);
    }

    [Fact]
    public void UnresolvedNameInferredOpAddition_PreservesOperatorFallback()
    {
        var intType = TypeRef.CoreLib("System", "Int32");
        var declaring = TypeRef.Definition("ExternalFacts.Library", "ExternalFacts", "OperatorLikeLibrary");
        var callee = new MethodRef(declaring, "op_Addition", intType, [intType, intType], HasThis: false)
        {
            IsSpecialName = true,
            IsOperator = MetadataFactState.Unknown,
        };
        var call = new Call(callee, isVirtual: false,
            [new LoadArgument(0, "a", intType), new LoadArgument(1, "b", intType)]);
        var block = new Block(0);
        block.Add(new Return(call));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("return a + b;", output);
        Assert.DoesNotContain("op_Addition", output);
    }

    [Fact]
    public void IsInstanceValueType_RendersAsIs()
    {
        // `isinst <valuetype>` is `obj is T`, not `obj as T`: `as` on a
        // non-nullable value type is CS0077. Built directly so the rendering
        // is tested independent of csc codegen.
        var objType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var block = new Block(0);
        block.Add(new Return(new IsInstance(byteType, new LoadArgument(0, "obj", objType))));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(boolType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [byteType] = TypeShape.ValueType },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("obj is byte", output);
        Assert.DoesNotContain(" as byte", output);
    }

    [Fact]
    public void IsInstanceValueType_AsCondition_NoIntCompare()
    {
        // A value-type `isinst` used as a branch test is already boolean
        // (`obj is byte`); wrapping it in `!= 0` would be `bool != int`
        // (CS0019), so the condition must render bare.
        var objType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var byteType = TypeRef.CoreLib("System", "Byte");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new IsInstance(byteType, new LoadArgument(0, "obj", objType)), then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType], container)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [byteType] = TypeShape.ValueType },
        };

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("obj is byte", output);
        Assert.DoesNotContain("!= 0", output);
    }

    [Fact]
    public void IsInstanceUnknownShape_AsCondition_RendersIs()
    {
        // A cross-assembly value type (e.g. BigInteger, Guid) whose shape the
        // printer could not resolve is still `obj is T` when tested as a branch
        // condition — `as` would be CS0077. The bug in issue #932: with no shape
        // entry the printer fell back to `as`, producing `if (obj as BigInteger)`.
        var objType = TypeRef.CoreLib("System", "Object");
        var intType = TypeRef.CoreLib("System", "Int32");
        var bigIntType = TypeRef.Definition("System.Runtime.Numerics", "System.Numerics", "BigInteger");
        var then = new Block(0);
        then.Add(new Return(new Constant(1, intType)));
        var entry = new Block(0);
        entry.Add(new IfStatement(new IsInstance(bigIntType, new LoadArgument(0, "obj", objType)), then, null));
        entry.Add(new Return(new Constant(0, intType)));
        var container = new BlockContainer();
        container.Add(entry);
        var signature = new MethodSignature(intType, [], HasThis: false, GenericParameterCount: 0);
        // No TypeShapes entry for BigInteger: shape is unresolved, as for a real
        // cross-assembly struct.
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [objType], container);

        string output = CSharpPrinter.Print(function).Output!.Trim();

        Assert.Contains("obj is BigInteger", output);
        Assert.DoesNotContain(" as BigInteger", output);
    }

    static string PrintEnumConstant(int value, TypeRef enumType, IReadOnlyDictionary<TypeRef, IReadOnlyDictionary<long, string>> members)
    {
        var block = new Block(0);
        block.Add(new Return(new Constant(value, enumType)));
        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(enumType, [], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", TypeRef.CoreLib("Synthetic", "T"), signature, [], container)
        {
            EnumMembers = members,
            // The real pipeline only gives a constant an enum type when that
            // type resolved as an enum shape; mirror that so the cast path fires.
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [enumType] = TypeShape.Enum },
        };
        // Strip the leading "return " and trailing ";" to get the operand text.
        string output = CSharpPrinter.Print(function).Output!.Trim();
        return output["return ".Length..].TrimEnd(';');
    }

    sealed class RaisingPassTestsAccessor
    {
        public string Print(string typeName, string methodName, MetadataSource source)
        {
            var function = IrImporter.Import(source, typeName, methodName);
            Assert.NotNull(function);
            IrPasses.Run(function);
            return CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n");
        }
    }
}
