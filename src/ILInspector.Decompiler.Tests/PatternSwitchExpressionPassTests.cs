using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Issue #3022: <see cref="PatternSwitchExpressionPass"/> raises the non-union
/// nested type-pattern <c>as</c>/null-test if/return cascade over a plain
/// receiver — with a single-level property-subpattern arm, <c>when</c> guards,
/// and a <c>_ =&gt; false</c> default — back into a <c>switch</c> expression.
///
/// Positive coverage is compiler-backed: a compiled fixture
/// (<see cref="PatternSwitchSample.Classify"/>) and the product method the raise
/// was written for (<c>YieldBreakLoopIteratorReconstruction.TryNormalizeContinueCondition</c>),
/// so the recognized shape is exactly what csc emits. The synthetic negatives
/// pin the safety guards that keep the fold from misfiring: every no-match and
/// guard-fail path must yield the identical default, and the switch value must
/// be a re-evaluable place read once.
/// </summary>
[Trait("Area", "Pass")]
public class PatternSwitchExpressionPassTests
{
    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Node = TypeRef.CoreLib("Synthetic", "Node");
    static readonly TypeRef Leaf = TypeRef.CoreLib("Synthetic", "Leaf");
    static readonly TypeRef Branch = TypeRef.CoreLib("Synthetic", "Branch");

    static Constant False() => new(false, Bool);

    static IrFunction Raised(System.Type fixtureType, string methodName)
    {
        using var source = MetadataSource.Open(fixtureType.Assembly.Location);
        var function = IrImporter.Import(source, fixtureType.FullName!, methodName);
        Assert.NotNull(function);
        // Match the product path: wire the cross-method import seam and the
        // type-disjointness oracle (from the assembly's open metadata) so the
        // disjointness-gated switch-expression raise fires exactly as it does when
        // the shipped `member` command renders this method.
        var context = new PassContext(
            new Stepper(enabled: false),
            importMethodBody: method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        IrPasses.Run(function!, IrPasses.Default, context);
        function!.CheckInvariant();
        return function;
    }

    static void RunPass(IrFunction function) => new PatternSwitchExpressionPass().Run(function, PassContext.None);

    static void RunPass(IrFunction function, Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint)
        => new PatternSwitchExpressionPass().Run(
            function,
            new PassContext(new Stepper(enabled: false), typesProvablyDisjoint: typesProvablyDisjoint));

    // ── Compiler-backed positives ──────────────────────────────────────────

    [Fact]
    public void CompiledFixture_RaisesToPatternSwitchExpression()
    {
        var function = Raised(typeof(PatternSwitchSample), nameof(PatternSwitchSample.Classify));

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(2, switchExpression.Arms.Count);

        var bare = switchExpression.Arms[0];
        Assert.Contains("Leaf", bare.PatternType.ToDisplayString());
        Assert.Null(bare.Subpattern);
        Assert.NotNull(bare.LocalIndex);
        Assert.True(bare.HasGuard);

        var subpatternArm = switchExpression.Arms[1];
        Assert.Contains("Wrapper", subpatternArm.PatternType.ToDisplayString());
        Assert.NotNull(subpatternArm.Subpattern);
        Assert.Equal("Inner", subpatternArm.Subpattern!.PropertyName);
        Assert.Contains("Leaf", subpatternArm.Subpattern.PatternType.ToDisplayString());
        Assert.True(subpatternArm.HasGuard);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return node switch", output);
        Assert.Contains("Leaf leaf when Exceeds(leaf.Weight, threshold) => Capture(leaf.Weight, out result),", output);
        Assert.Contains("Wrapper { Inner: Leaf inner } when Exceeds(inner.Weight, threshold) => Capture(-inner.Weight, out result),", output);
        Assert.Contains("_ => false,", output);
    }

    [Fact]
    public void ProductTargetMethod_SelfRaisesToPatternSwitchExpression()
    {
        // The method the raise was written for: import it straight from the
        // shipped decompiler assembly and prove its own lowering round-trips.
        var function = Raised(
            typeof(IrFunction).Assembly.GetType("ILInspector.Decompiler.Pipeline.YieldBreakLoopIteratorReconstruction", throwOnError: true)!,
            "TryNormalizeContinueCondition");

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Empty(function.Descendants.OfType<IfStatement>());
        Assert.True(switchExpression.HasDefault);
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.Contains("Comparison", switchExpression.Arms[0].PatternType.ToDisplayString());
        Assert.NotNull(switchExpression.Arms[1].Subpattern);
        Assert.Equal("Operand", switchExpression.Arms[1].Subpattern!.PropertyName);

        string output = CSharpPrinter.Print(function).Output!.ReplaceLineEndings("\n").TrimEnd();
        Assert.Contains("return expression switch", output);
        Assert.Contains("Comparison comparison when ReadsLoopField(comparison.Left, loopFieldName)", output);
        // Issue #3033: both mutually-exclusive arms bind a source pattern variable
        // named `comparison`. Each arm is its own scope, so the second arm reuses
        // the source spelling rather than the printer's global name-dedup renaming
        // it to a synthetic `V_1`.
        Assert.Contains("LogicalNot { Operand: Comparison comparison } when ReadsLoopField(comparison.Left, loopFieldName)", output);
        Assert.DoesNotContain("Comparison V_", output);
        Assert.Contains("_ => false,", output);
    }

    // ── Synthetic shape + negative guards ──────────────────────────────────

    // Minimal recognized shape (post-#3003 structuring): `sv = <place>; k = sv
    // as T; if (!k) { return <no-match default>; } [if (!g) { return <guard-fail
    // default>; }] return <value>;`. The matched body trails the dispatch test as
    // sibling statements; the no-match remainder (here just the default) nests in
    // the dispatch test's then-branch. A null guard omits the guard test.
    static IrFunction SingleArm(
        IrExpression switchValue,
        IrExpression? guard,
        IrExpression armValue,
        IrExpression noMatchDefault,
        IrExpression? guardFailDefault = null)
    {
        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, switchValue));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, new LoadLocal(5, Node))));

        var dispatchThen = new Block();
        dispatchThen.Add(new Return(noMatchDefault));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), dispatchThen, null));

        if (guard is not null)
        {
            var guardThen = new Block();
            guardThen.Add(new Return(guardFailDefault!));
            block.Add(new IfStatement(new LogicalNot(guard), guardThen, null));
        }

        block.Add(new Return(armValue));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Leaf, Bool, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_MatchingShape_Raises()
    {
        var function = SingleArm(
            switchValue: new LoadArgument(0, "node", Node),
            guard: null,
            armValue: new Constant(true, Bool),
            noMatchDefault: False());

        RunPass(function);

        var switchExpression = Assert.Single(function.Descendants.OfType<PatternSwitchExpression>());
        Assert.Single(switchExpression.Arms);
        Assert.True(switchExpression.HasDefault);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void Synthetic_GuardFailYieldsNonDefault_DoesNotRaise()
    {
        // The no-match path yields false (the default), but the guard-fail path
        // yields 99. In a switch expression a failed `when` guard falls to the
        // default arm, so both no-match and guard-fail paths must yield the same
        // value; a divergent guard-fail value means this is not a faithful
        // switch and must be left as-is.
        var guard = new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);
        var function = SingleArm(
            switchValue: new LoadArgument(0, "node", Node),
            guard: guard,
            armValue: new Constant(true, Bool),
            noMatchDefault: False(),
            guardFailDefault: new Constant(99, Int32));

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_SideEffectingSwitchValue_DoesNotRaise()
    {
        // The switch value is a method call, not a re-evaluable place. The
        // lowering reads the value once per arm test; folding a side-effecting
        // value into a single evaluation would change behavior, so decline.
        var effect = new MethodRef(Node, "Produce", Node, [], HasThis: false);
        var function = SingleArm(
            switchValue: new Call(effect, isVirtual: false, []),
            guard: null,
            armValue: new Constant(true, Bool),
            noMatchDefault: False());

        RunPass(function);

        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // ── Adversarial-review regression guards (PR #3034) ────────────────────

    // A two-arm cascade: `sv = node; k0 = sv as arm0Type; if (!k0) { k1 = sv as
    // arm1Type; if (!k1) { return default; } return arm1Value; } [if (!g) {
    // return default; }] return arm0Value;`. arm0 is a guarded, non-last arm; a
    // guard-fail on arm0 routes to the default in this cascade but to arm1 in a
    // switch expression — so raising is sound only when arm0Type and arm1Type
    // cannot both match.
    static IrFunction TwoArm(
        IrExpression? arm0Guard,
        IrExpression arm0Value,
        IrExpression arm1Value,
        IrExpression defaultValue,
        TypeRef arm0Type,
        TypeRef arm1Type)
    {
        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, new LoadArgument(0, "node", Node)));
        block.Add(new StoreLocal(0, arm0Type, new IsInstance(arm0Type, new LoadLocal(5, Node))));

        var rest = new Block();
        rest.Add(new StoreLocal(1, arm1Type, new IsInstance(arm1Type, new LoadLocal(5, Node))));
        var arm1Dispatch = new Block();
        arm1Dispatch.Add(new Return((IrExpression)defaultValue.Clone()));
        rest.Add(new IfStatement(new LogicalNot(new LoadLocal(1, arm1Type)), arm1Dispatch, null));
        rest.Add(new Return(arm1Value));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, arm0Type)), rest, null));

        if (arm0Guard is not null)
        {
            var guardThen = new Block();
            guardThen.Add(new Return((IrExpression)defaultValue.Clone()));
            block.Add(new IfStatement(new LogicalNot(arm0Guard), guardThen, null));
        }
        block.Add(new Return(arm0Value));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(arm0Type, arm1Type, Bool, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_GuardedNonLastArm_RaisesOnlyWhenTypesProvablyDisjoint()
    {
        // arm0 (`Leaf leaf when Ok(leaf)`) precedes arm1 (`Branch`). If Leaf and
        // Branch overlapped, a value matching both that failed arm0's guard would
        // reach the default here but arm1 in a switch expression. The pass must
        // consult the disjointness oracle and only raise when disjointness is
        // proven.
        static IrFunction Build() => TwoArm(
            arm0Guard: new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]),
            arm0Value: new Constant(true, Bool),
            arm1Value: new Constant(true, Bool),
            defaultValue: False(),
            arm0Type: Leaf,
            arm1Type: Branch);

        // No oracle available: cannot prove disjointness → decline.
        var withoutOracle = Build();
        RunPass(withoutOracle, typesProvablyDisjoint: null);
        Assert.Empty(withoutOracle.Descendants.OfType<PatternSwitchExpression>());

        // Oracle reports the types could overlap → decline.
        var overlapping = Build();
        RunPass(overlapping, typesProvablyDisjoint: (_, _) => false);
        Assert.Empty(overlapping.Descendants.OfType<PatternSwitchExpression>());

        // Oracle proves disjointness → raise both arms.
        var disjoint = Build();
        RunPass(disjoint, typesProvablyDisjoint: (_, _) => true);
        var switchExpression = Assert.Single(disjoint.Descendants.OfType<PatternSwitchExpression>());
        Assert.Equal(2, switchExpression.Arms.Count);
        Assert.True(switchExpression.HasDefault);
    }

    [Fact]
    public void Synthetic_PatternLocalLeaksIntoSiblingArm_DoesNotRaise()
    {
        // arm1's value reads arm0's pattern local (`leaf`). In the lowered
        // cascade that local is still in scope, but each switch arm scopes its
        // own pattern variable, so the raised C# would reference an out-of-scope
        // name (CS0103). Decline even with disjointness proven.
        var leaked = TwoArm(
            arm0Guard: null,
            arm0Value: new Call(new MethodRef(Leaf, "V", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]),
            arm1Value: new Call(new MethodRef(Leaf, "V", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]),
            defaultValue: False(),
            arm0Type: Leaf,
            arm1Type: Branch);
        RunPass(leaked, typesProvablyDisjoint: (_, _) => true);
        Assert.Empty(leaked.Descendants.OfType<PatternSwitchExpression>());

        // Control: arm1 reads its own local (`k1`) instead — raises.
        var scoped = TwoArm(
            arm0Guard: null,
            arm0Value: new Call(new MethodRef(Leaf, "V", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]),
            arm1Value: new Call(new MethodRef(Branch, "V", Bool, [Branch], HasThis: false), isVirtual: false, [new LoadLocal(1, Branch)]),
            defaultValue: False(),
            arm0Type: Leaf,
            arm1Type: Branch);
        RunPass(scoped, typesProvablyDisjoint: (_, _) => true);
        Assert.Single(scoped.Descendants.OfType<PatternSwitchExpression>());
    }

    // A single positive-form guarded arm: `sv = node; k0 = sv as Leaf; if (!k0)
    // { return default; } if (g) { return value; } [return default;]`. When g
    // fails, control falls through the `if (g)`; only an explicit trailing
    // `return default` routes that fall-through to the default.
    static IrFunction SinglePositiveGuardArm(bool withDefaultTail)
    {
        var block = new Block(0);
        block.Add(new StoreLocal(5, Node, new LoadArgument(0, "node", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, new LoadLocal(5, Node))));

        var dispatchThen = new Block();
        dispatchThen.Add(new Return(False()));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), dispatchThen, null));

        var guard = new Call(new MethodRef(Leaf, "Ok", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]);
        var guardThen = new Block();
        guardThen.Add(new Return(new Constant(true, Bool)));
        block.Add(new IfStatement(guard, guardThen, null));

        if (withDefaultTail)
            block.Add(new Return(False()));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", Node, signature, ImmutableArray.Create(Leaf, Leaf, Bool, Node, Node, Node), container);
    }

    [Fact]
    public void Synthetic_PositiveGuardMissingDefaultTail_DoesNotRaise()
    {
        // Without the explicit trailing `return false`, the guard-fail fall
        // through would drop out of the block rather than reach the default;
        // accepting the empty tail would silently reroute that path.
        var missing = SinglePositiveGuardArm(withDefaultTail: false);
        RunPass(missing);
        Assert.Empty(missing.Descendants.OfType<PatternSwitchExpression>());

        // Control: with the trailing default present the arm raises.
        var present = SinglePositiveGuardArm(withDefaultTail: true);
        RunPass(present);
        Assert.Single(present.Descendants.OfType<PatternSwitchExpression>());
    }

    // ── Adversarial-review regression guards (PR #3082) ────────────────────

    [Fact]
    public void Synthetic_UnreadPatternLocalLeaksIntoSiblingArm_DoesNotRaise()
    {
        // arm0 does NOT reference its own bound local (`arm0Value` is a
        // constant), so the arm renders no pattern variable (`LocalIndex` is
        // null). arm1's value, however, reads arm0's bound local. Scope
        // validation must track the arm's `isinst` local even when it is not
        // rendered; otherwise the sibling read escapes and the raised C# emits an
        // out-of-scope reference (CS0103). Reported independently by Gemini
        // (sibling read) — the unrendered-local variant of the #3034 leak guard.
        var leaked = TwoArm(
            arm0Guard: null,
            arm0Value: new Constant(true, Bool),
            arm1Value: new Call(new MethodRef(Leaf, "V", Bool, [Leaf], HasThis: false), isVirtual: false, [new LoadLocal(0, Leaf)]),
            defaultValue: False(),
            arm0Type: Leaf,
            arm1Type: Branch);
        RunPass(leaked, typesProvablyDisjoint: (_, _) => true);
        Assert.Empty(leaked.Descendants.OfType<PatternSwitchExpression>());

        // Control: arm1 reads its own local instead of arm0's — raises.
        var scoped = TwoArm(
            arm0Guard: null,
            arm0Value: new Constant(true, Bool),
            arm1Value: new Call(new MethodRef(Branch, "V", Bool, [Branch], HasThis: false), isVirtual: false, [new LoadLocal(1, Branch)]),
            defaultValue: False(),
            arm0Type: Leaf,
            arm1Type: Branch);
        RunPass(scoped, typesProvablyDisjoint: (_, _) => true);
        Assert.Single(scoped.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_UnreadPatternLocalLeaksIntoDefault_DoesNotRaise()
    {
        // Neither arm references its own bound local, so no arm renders a
        // pattern variable. The default expression, however, reads arm0's bound
        // local. In the lowered cascade that local holds the matched value; in
        // the raised switch the default arm cannot see it, so the raise would
        // read `default(T)` — a behavior change. Scope validation must reject a
        // default that reads any arm's `isinst` local even when unrendered.
        // Reported independently by GPT (default read).
        var leakedDefault = TwoArm(
            arm0Guard: null,
            arm0Value: new Constant(true, Bool),
            arm1Value: new Constant(true, Bool),
            defaultValue: new LoadLocal(0, Leaf),
            arm0Type: Leaf,
            arm1Type: Branch);
        RunPass(leakedDefault, typesProvablyDisjoint: (_, _) => true);
        Assert.Empty(leakedDefault.Descendants.OfType<PatternSwitchExpression>());

        // Control: the default reads no pattern local — raises.
        var cleanDefault = TwoArm(
            arm0Guard: null,
            arm0Value: new Constant(true, Bool),
            arm1Value: new Constant(true, Bool),
            defaultValue: False(),
            arm0Type: Leaf,
            arm1Type: Branch);
        RunPass(cleanDefault, typesProvablyDisjoint: (_, _) => true);
        Assert.Single(cleanDefault.Descendants.OfType<PatternSwitchExpression>());
    }

    [Fact]
    public void Synthetic_ArmLocalAliasesSwitchValueTemp_DoesNotRaise()
    {
        // arm0 binds its `isinst` result into the very slot that holds the
        // switch value. The store overwrites the receiver, so arm1 tests the
        // `isinst` result rather than the original value — for a Branch argument
        // the cascade returns the default while `node switch { Leaf => .., Branch
        // => .., _ => false }` would match Branch. Reported by GPT at the fixed
        // head. Every arm intro reads (and here rebinds) the sv slot, so the
        // existing ownership/scoping checks pass; the alias must be rejected.
        var block = new Block(0);
        // sv slot (0) := arg, then arm0's isinst overwrites the same slot (0).
        block.Add(new StoreLocal(0, Node, new LoadArgument(0, "node", Node)));
        block.Add(new StoreLocal(0, Leaf, new IsInstance(Leaf, new LoadLocal(0, Node))));
        var rest = new Block();
        rest.Add(new StoreLocal(1, Branch, new IsInstance(Branch, new LoadLocal(0, Leaf))));
        var arm1Dispatch = new Block();
        arm1Dispatch.Add(new Return(False()));
        rest.Add(new IfStatement(new LogicalNot(new LoadLocal(1, Branch)), arm1Dispatch, null));
        rest.Add(new Return(new Constant(true, Bool)));
        block.Add(new IfStatement(new LogicalNot(new LoadLocal(0, Leaf)), rest, null));
        block.Add(new Return(new Constant(true, Bool)));

        var container = new BlockContainer();
        container.Add(block);
        var signature = new MethodSignature(Bool, [new Parameter("node", Node)], HasThis: false, GenericParameterCount: 0);
        var function = new IrFunction("M", Node, signature, ImmutableArray.Create(Node, Branch, Bool, Node, Node, Node), container);

        RunPass(function, typesProvablyDisjoint: (_, _) => true);
        Assert.Empty(function.Descendants.OfType<PatternSwitchExpression>());
    }

    // ── Disjointness oracle guards (PR #3082, Finding 2) ───────────────────

    static readonly string TestAssembly =
        typeof(PatternSwitchExpressionPassTests).Assembly.GetName().Name!;

    static readonly string FixtureNamespace = typeof(DisjointSiblingA).Namespace!;

    static TypeRef FixtureType(string name)
        => TypeRef.Definition(TestAssembly, FixtureNamespace, name);

    [Fact]
    public void AreProvablyDisjoint_NonGenericSiblings_ProvenDisjoint()
    {
        // Two unrelated non-generic classes whose base chains both resolve to
        // object share no instance: the positive control that the oracle does
        // issue proofs when it soundly can.
        using var source = MetadataSource.Open(typeof(PatternSwitchExpressionPassTests).Assembly.Location);
        Assert.True(source.AreProvablyDisjoint(FixtureType("DisjointSiblingA"), FixtureType("DisjointSiblingB")));
    }

    [Fact]
    public void AreProvablyDisjoint_AncestorPair_NotDisjoint()
    {
        // A value of the derived type is also the base type, so the two overlap;
        // the oracle must not claim disjointness. `DerivedC`'s base chain
        // contains `BaseC`, so containment is observed.
        using var source = MetadataSource.Open(typeof(PatternSwitchExpressionPassTests).Assembly.Location);
        Assert.False(source.AreProvablyDisjoint(FixtureType("DisjointDerivedC"), FixtureType("DisjointBaseC")));
        Assert.False(source.AreProvablyDisjoint(FixtureType("DisjointBaseC"), FixtureType("DisjointDerivedC")));
    }

    [Fact]
    public void AreProvablyDisjoint_OpenGenericDefinition_NeverProvenDisjoint()
    {
        // `GenericDerived<T> : GenericBase<T>`. The derived type's base appears
        // in its base chain as a closed generic instance (`GenericBase<T>`),
        // which never equals the open definition `GenericBase`1`, so ancestry
        // through a generic supertype cannot be observed. The oracle must
        // conservatively decline for open generic definitions rather than
        // falsely prove an overlapping pair disjoint.
        using var source = MetadataSource.Open(typeof(PatternSwitchExpressionPassTests).Assembly.Location);
        var genericBase = FixtureType("DisjointGenericBase`1");
        var genericDerived = FixtureType("DisjointGenericDerived`1");
        Assert.False(source.AreProvablyDisjoint(genericDerived, genericBase));
        Assert.False(source.AreProvablyDisjoint(genericBase, genericDerived));
        // Even against a genuinely unrelated non-generic type, an open generic
        // definition is declined (documented hard guarantee).
        Assert.False(source.AreProvablyDisjoint(genericBase, FixtureType("DisjointSiblingA")));
        Assert.False(source.AreProvablyDisjoint(FixtureType("DisjointSiblingA"), genericBase));
    }
}

// Self-contained type shapes exercised by the AreProvablyDisjoint guard tests.
// Compiled into the test assembly so the oracle resolves their real base chains
// (same-assembly) without depending on BCL internals.
internal class DisjointSiblingA { }
internal class DisjointSiblingB { }
internal class DisjointBaseC { }
internal class DisjointDerivedC : DisjointBaseC { }
internal class DisjointGenericBase<T> { }
internal class DisjointGenericDerived<T> : DisjointGenericBase<T> { }
