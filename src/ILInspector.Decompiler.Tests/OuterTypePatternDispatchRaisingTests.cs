using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Issue #3113: <see cref="PatternSwitchExpressionPass"/> raises the outer
/// type-pattern dispatch of <see cref="ScatteredReturnDispatchSample.Dispatch"/>
/// — the #2978 witness — all the way back to its original nested
/// <c>switch</c> expression. #3094 raised only the inner <c>s.Length</c>
/// comparison-chain arm; this closes the outer dispatch, which needs three new
/// recognizer capabilities the compiler emits here:
/// <list type="bullet">
/// <item>a <em>diamond intro arm</em> — the matched value lives in the <c>if</c>'s
///   <c>else</c> and the remaining arms in its <c>then</c> (csc's shape when an
///   arm value spans multiple blocks, here the nested switch expression);</item>
/// <item>a <em>value-type arm</em> — <c>if (!(o is int)) return default; i =
///   (int)o;</c>, an isinst test then a separate unbox.any bind with no isinst
///   intro local;</item>
/// <item>a <em>flipped-comparison guard</em> — a <c>when i &gt; 0</c> whose
///   failure lowers to <c>if (i &lt;= 0) return default;</c>, recovered by
///   negating the relational.</item>
/// </list>
/// The shared default <c>_ =&gt; Fail(out x)</c> is a method call reached by
/// several identical <c>return Fail(out x)</c> paths, so folding also needs
/// structural call identity (SameSinkValue), not just re-evaluable-place identity.
///
/// The compiler-backed positive is the witness itself. The synthetic negatives
/// pin the new discriminators: a value-type arm whose unbox type disagrees with
/// its isinst test, a guard whose failure diverges from the default, a diamond
/// whose matched arm does not reach the shared default, a nullable test type
/// (illegal as a declaration pattern), a bound-slot type that disagrees with the
/// pattern type, un-spellable test types (pointer/function-pointer/bare
/// <c>Nullable`1</c>/<c>void</c>/static class), a stack-only value type
/// (<c>Span&lt;int&gt;</c>, <c>TypedReference</c>) which resolves to a value-type
/// shape but is illegal as a boxed pattern, and a user-defined <c>ref struct</c>
/// caught through the imported <c>[IsByRefLike]</c> fact. A synthetic positive
/// pins that a generic-parameter arm (<c>isinst T; unbox.any T</c>, which csc
/// does emit) still raises. The last group is from GPT review of #3124.
/// </summary>
[Trait("Area", "Pass")]
public class OuterTypePatternDispatchRaisingTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Node = TypeRef.CoreLib("Synthetic", "Node");
    static readonly TypeRef Leaf = TypeRef.CoreLib("Synthetic", "Leaf");

    static IrFunction Raised(System.Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        IrPasses.Run(function!, IrPasses.Default, context);
        function!.CheckInvariant();
        return function;
    }

    static void RunPass(IrFunction function) => new PatternSwitchExpressionPass().Run(function, PassContext.None);

    // ── Compiler-backed positive: the #2978 witness ─────────────────────────

    [Fact]
    public void Witness_FullyRaisesToNestedSwitchExpression()
    {
        var function = Raised(typeof(ScatteredReturnDispatchSample), nameof(ScatteredReturnDispatchSample.Dispatch));

        // Outer type-pattern dispatch is a single pattern switch expression; no
        // if/statement residue survives.
        var patternSwitch = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(patternSwitch.HasDefault);
        Assert.Equal(2, patternSwitch.Arms.Count);

        // Arm 0: `string s` — unguarded reference arm whose value is the inner
        // (value-constant) switch expression raised by #3094.
        Assert.Contains("string", patternSwitch.Arms[0].PatternType.ToDisplayString());
        Assert.False(patternSwitch.Arms[0].HasGuard);
        Assert.Single(function.Descendants.OfType<SwitchExpression>());

        // Arm 1: `int i when i > 0` — the guarded value-type arm.
        Assert.Contains("int", patternSwitch.Arms[1].PatternType.ToDisplayString());
        Assert.True(patternSwitch.Arms[1].HasGuard);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return o switch", output);
        Assert.Contains("string s => s.Length switch { 0 => Fail(out x), 1 => Win(1, out x), _ => Fail(out x) },", output);
        Assert.Contains("int i when i > 0 => Win(i, out x),", output);
        Assert.Contains("_ => Fail(out x),", output);
    }

    // ── Synthetic builder mirroring the witness's outer dispatch ────────────

    // Builds the witness's outer temp-form cascade:
    //   V9 = <switch value>;
    //   L0 = V9 as Leaf;
    //   if (!L0) { if (!(V9 is <unboxType-as-testType>)) return <default>;
    //              L1 = (int)V9;
    //              if (L1 <= 0) return <guardFailDefault>;
    //              return Win(L1); }
    //   else     { return <leafValue> <plus optional dead tail> }
    // The Leaf arm is the diamond intro arm; the int arm is the value-type arm
    // with a flipped `L1 <= 0` guard. Knobs break exactly one invariant.
    static IrFunction DiamondValueTypeCascade(
        TypeRef valueTypeTest,
        IrExpression noMatchDefault,
        IrExpression guardFailDefault,
        IrExpression leafValue,
        bool elseDeadTail = false,
        TypeRef? unboxType = null,
        TypeRef? bindType = null)
    {
        IrExpression Win() => new Call(
            new MethodRef(Node, "Win", Bool, [Int32], HasThis: false),
            isVirtual: false,
            [new LoadLocal(1, Int32)]);

        var vtDispatchThen = new Block();
        vtDispatchThen.Add(new Return(noMatchDefault));
        var vtDispatch = new IfStatement(
            new LogicalNot(new IsInstance(valueTypeTest, new LoadLocal(9, Node))),
            vtDispatchThen,
            null);

        var guardThen = new Block();
        guardThen.Add(new Return(guardFailDefault));
        var guard = new IfStatement(
            new Comparison(ComparisonKind.LessThanOrEqual, isUnsigned: false, new LoadLocal(1, Int32), new Constant(0, Int32)),
            guardThen,
            null);

        var then = new Block();
        then.Add(vtDispatch);
        then.Add(new StoreLocal(1, bindType ?? Int32, new UnboxAny(unboxType ?? Int32, new LoadLocal(9, Node))));
        then.Add(guard);
        then.Add(new Return(Win()));

        var elseBlock = new Block();
        elseBlock.Add(new Return(leafValue));
        if (elseDeadTail)
            elseBlock.Add(new Return(new Constant(true, Bool)));

        var block = new Block(0);
        block.Add(new StoreLocal(9, Node, new LoadArgument(0, "o", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, new LoadLocal(9, Node))));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), then, elseBlock));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("o", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Int32, Bool, Node, Node, Node, Node, Node, Node, Node), container);
    }

    static Call Default() => new(new MethodRef(Node, "Fail", Bool, [], HasThis: false), isVirtual: false, []);

    [Fact]
    public void Synthetic_DiamondValueTypeGuard_Raises()
    {
        // All invariants intact: the value-type unbox matches its isinst test, the
        // guard failure returns the same call as the default, and the diamond's
        // else reaches the default. Folds to a two-arm switch expression.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool));

        RunPass(function);

        var patternSwitch = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, patternSwitch.Arms.Count);
        Assert.True(patternSwitch.HasDefault);
        Assert.Contains("Leaf", patternSwitch.Arms[0].PatternType.ToDisplayString());
        Assert.False(patternSwitch.Arms[0].HasGuard);
        Assert.Contains("int", patternSwitch.Arms[1].PatternType.ToDisplayString());
        Assert.True(patternSwitch.Arms[1].HasGuard);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_ValueTypeUnboxTypeDiffersFromTest_DoesNotRaise()
    {
        // The value-type arm's isinst tests `Leaf` but the unbox binds `int`. A
        // switch arm's pattern is one type; a test/unbox type disagreement is not
        // a value-type pattern bind, so IsValueTypeArm declines and the cascade
        // stays if/return.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Leaf,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_GuardFailDivergesFromDefault_DoesNotRaise()
    {
        // The value-type arm's `when` guard failure returns `Other()`, a different
        // call than the shared `Fail()` default. In a switch expression a failed
        // guard falls to the default arm, so a divergent guard-fail value means
        // the fold is not faithful — SameSinkValue distinguishes the two calls and
        // the cascade is left as-is.
        var other = new Call(new MethodRef(Node, "Other", Bool, [], HasThis: false), isVirtual: false, []);
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: other,
            leafValue: new Constant(true, Bool));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_DiamondElseDoesNotReachDefault_DoesNotRaise()
    {
        // The diamond's `else` (the Leaf arm value) is followed by a second
        // statement, so the matched body does not reach the shared default tail.
        // The arm value must be a single reachable run; an unreached tail means
        // this is not the diamond arm shape, so decline.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            elseDeadTail: true);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    public static IEnumerable<object[]> UnspellablePatternTypes()
    {
        // A bare `Nullable`1` definition (open, no type argument): CS0723 as a pattern.
        yield return [TypeRef.CoreLib("System", "Nullable`1")];
        // A pointer type: not a pattern type (`int*` is a syntax error in a pattern).
        yield return [TypeRef.Pointer(Int32)];
        // A function-pointer type: CS8521 as a pattern.
        yield return [TypeRef.FunctionPointer(Int32, ImmutableArray<TypeRef>.Empty, "")];
        // System.Void: not a matchable value type (CS1547).
        yield return [TypeRef.CoreLib("System", "Void")];
        // A static/reference class definition (no value-type shape): CS0723/CS8121 as a pattern.
        yield return [TypeRef.CoreLib("System", "Math")];
    }

    [Theory]
    [MemberData(nameof(UnspellablePatternTypes))]
    public void Synthetic_UnspellableValueTypePattern_DoesNotRaise(TypeRef testType)
    {
        // GPT review (#3124): the value-type arm accepted any non-`Nullable<T>`
        // test type, so a bare `Nullable`1` definition, a pointer, a function
        // pointer, `System.Void`, or a static/reference class raised to a switch
        // arm — none of which is a legal C# declaration pattern. A test type must
        // now prove it is a known non-nullable value type (or a generic
        // parameter), so each of these declines and the cascade stays if/return.
        // Test, unbox, and bind types agree, isolating the pattern-eligibility
        // gate from the unbox/bind checks.
        var function = DiamondValueTypeCascade(
            valueTypeTest: testType,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: testType,
            bindType: testType);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    public static IEnumerable<object[]> GenericParameterPatternTypes()
    {
        yield return [TypeRef.GenericParameter(0, "T")];
        yield return [TypeRef.MethodGenericParameter(0, "T")];
    }

    [Theory]
    [MemberData(nameof(GenericParameterPatternTypes))]
    public void Synthetic_GenericParameterValueTypeArm_Raises(TypeRef testType)
    {
        // GPT review (#3124): csc emits `isinst T; unbox.any T; stloc T` for an
        // unconstrained (or struct-constrained) generic parameter, and `T t` is a
        // legal C# declaration pattern. An over-strict kind whitelist wrongly
        // declined these; IsSpellableValueTypePattern now admits generic
        // parameters directly, so the value-type arm raises — matching the shape
        // the compiler itself produces.
        var function = DiamondValueTypeCascade(
            valueTypeTest: testType,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: testType,
            bindType: testType);

        RunPass(function);

        var patternSwitch = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, patternSwitch.Arms.Count);
        Assert.True(patternSwitch.HasDefault);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_FrameworkRefStructWithValueShape_DoesNotRaise()
    {
        // GPT review (#3124): a ref struct (`Span<int>`) resolves to a value-type
        // shape but is illegal as a boxed pattern (CS8121) and is never a
        // compiler-produced value-type arm — a ref struct cannot be boxed. Even
        // stamped with a value-type hint (so the general value-type gate would
        // admit it), IsStackOnlyValueType rejects `Span`/`ReadOnlySpan` by name,
        // so the cascade stays if/return rather than raising invalid `Full` C#.
        var spanDef = TypeRef.CoreLib("System", "Span`1").WithValueTypeHint(ValueTypeHint.ValueType);
        var span = TypeRef.GenericInstance(spanDef, ImmutableArray.Create(Int32));
        var function = DiamondValueTypeCascade(
            valueTypeTest: span,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: span,
            bindType: span);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_TypedReferenceValueTypeArm_DoesNotRaise()
    {
        // GPT review round 4 (#3124): `System.TypedReference` sits in the corelib
        // value-type list, so the general value-type gate admits it, yet it is a
        // stack-only by-ref-like type — illegal as a boxed pattern (CS8121) and
        // never a compiler-produced value-type arm. IsStackOnlyValueType rejects
        // it by name (its by-ref-like nature lives on the cross-assembly corelib
        // definition, not the TypeRef), so the cascade stays if/return.
        var typedRef = TypeRef.CoreLib("System", "TypedReference");
        var function = DiamondValueTypeCascade(
            valueTypeTest: typedRef,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: typedRef,
            bindType: typedRef);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_UserRefStructValueTypeArm_DoesNotRaise()
    {
        // GPT review round 4 (#3124): a user-defined `ref struct` in the inspected
        // assembly resolves to a ValueType shape (indistinguishable from an
        // ordinary struct by shape alone), so the general value-type gate admits
        // it. A ref struct cannot be boxed, so the value-type arm over one is
        // never compiler-produced and `T t` over it is illegal (CS8121). The
        // `[IsByRefLike]` fact is recovered at import into `function.ByRefLikeTypes`
        // (populated here to mirror that import); IsByRefLike consults it and
        // declines, so the cascade stays if/return. State unreachable via csc — a
        // synthetic fixture is the appropriate proof for this defensive gate.
        var userRefStruct = TypeRef.CoreLib("Synthetic", "UserRefStruct").WithValueTypeHint(ValueTypeHint.ValueType);
        var function = DiamondValueTypeCascade(
            valueTypeTest: userRefStruct,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: userRefStruct,
            bindType: userRefStruct);
        function.ByRefLikeTypes = ImmutableHashSet.Create(userRefStruct);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_NullableValueTypeTest_DoesNotRaise()
    {
        // GPT review (#3124): a `Nullable<int>` isinst+unbox arm is consistent —
        // test, unbox, and bind types all agree — but `int?` is illegal as a
        // C# declaration-pattern type (CS8116), so raising it would emit invalid
        // C# under a `Full` label. IsValueTypeArm must decline any nullable test
        // type; the cascade stays if/return.
        var nullableInt = TypeRef.GenericInstance(TypeRef.CoreLib("System", "Nullable`1"), ImmutableArray.Create(Int32));
        var function = DiamondValueTypeCascade(
            valueTypeTest: nullableInt,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: nullableInt,
            bindType: nullableInt);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_BindTypeDiffersFromTest_DoesNotRaise()
    {
        // GPT review (#3124): isinst+unbox both name `int`, but the bound slot is
        // declared `bool`. Raising declares an `int` pattern var for a `bool` slot
        // (CS0029), so the pattern type must equal the bound local's declared type.
        // IsValueTypeArm declines when they disagree; the cascade stays if/return.
        var function = DiamondValueTypeCascade(
            valueTypeTest: Int32,
            noMatchDefault: Default(),
            guardFailDefault: Default(),
            leafValue: new Constant(true, Bool),
            unboxType: Int32,
            bindType: Bool);

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }
}
