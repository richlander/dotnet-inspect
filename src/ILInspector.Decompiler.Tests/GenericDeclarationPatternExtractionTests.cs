using System.Text.RegularExpressions;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Coverage for #2831/#2862: csc's unconstrained/struct-constrained generic
/// declaration-pattern extraction (<c>if (Subject is T t)</c>) cannot store the
/// <c>isinst</c> result through an <c>as T</c> local, so it re-tests the value
/// inline — <c>UnboxAny T(IsInstance T(...))</c> — instead of the usual
/// <c>StoreLocal(IsInstance) + null-test</c> shape <c>IsPatternPass</c>
/// already recovers.
///
/// <para>#2862 raises the structured, provable shape back to a declaration-pattern
/// binding: <c>IsPatternPass</c> mints the pattern local and inlines the
/// lowering-only subject temp, recovering <c>if (Subject is T t)</c>. When the
/// guard is negated/flat (e.g. <c>is not T</c> feeding a <c>goto</c>), no legal
/// binding exists, so <c>CSharpPrinter.UnboxAnyOperand</c>'s <c>(object)</c>
/// bridge (the box+unbox.any generic-math intermediary) remains the fallback,
/// rather than routing the nested test through the general <c>is</c>/<c>as</c>
/// expression rule (invalid for a non-class-constrained <c>T</c> either way —
/// CS0030 for <c>is</c>, CS0413 for <c>as</c>).</para>
/// </summary>
public class GenericDeclarationPatternExtractionTests
{
    static string Print(Type declaringType, string methodName)
    {
        using var source = MetadataSource.Open(declaringType.Assembly.Location);
        var function = IrImporter.Import(source, declaringType.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function!).Output!;
    }

    // #2862 slice 1+2: a structured `if (Subject is T t)` whose extractions are
    // all proven by #2856's boundary is raised back to a declaration-pattern
    // binding, and the lowering-only subject temp is inlined, recovering the
    // original source rather than exposing csc's re-test/unbox lowering.
    [Fact]
    public void UnconstrainedDeclarationPattern_RaisesToDeclarationBinding()
    {
        var output = Print(
            typeof(GenericDeclarationPatternSpecimens<,>),
            nameof(GenericDeclarationPatternSpecimens<object, object>.Unconstrained));

        AssertRaisedBinding(output);
    }

    [Fact]
    public void StructConstrainedDeclarationPattern_RaisesToDeclarationBinding()
    {
        var output = Print(
            typeof(GenericDeclarationPatternSpecimensStruct<,>),
            nameof(GenericDeclarationPatternSpecimensStruct<int, object>.StructConstrained));

        AssertRaisedBinding(output);
    }

    // #2872: when the binding is read more than once csc caches the single
    // extraction in a real, source-named local; the pattern binds that local
    // directly (recovering its name) with no redundant copy and no `(object)`
    // bridge left behind.
    [Fact]
    public void MultipleUsesDeclarationPattern_RaisesBinding()
    {
        var output = Print(
            typeof(GenericDeclarationPatternSpecimens<,>),
            nameof(GenericDeclarationPatternSpecimens<object, object>.MultipleUses));

        Assert.Matches(@"if \(Subject is T t\)", output);
        Assert.Equal(2, Regex.Matches(output, @"Console\.WriteLine\(t\)").Count);
        // No redundant `t = V_N;` copy and no stray up-front declaration of the
        // cache local — the pattern binding owns it.
        Assert.DoesNotMatch(@"t = V_\d+", output);
        Assert.DoesNotMatch(@"(?m)^\s*T t;\s*$", output);
        Assert.DoesNotContain("(object)", output);
        Assert.DoesNotContain("as T", output);
    }

    // #2862 slice-2 boundary: the subject cache is reused after the guard, so the
    // binding is raised but the lowering-only subject temp must be retained (not
    // inlined into the pattern).
    [Fact]
    public void ReusedSubjectDeclarationPattern_RaisesBindingButRetainsTemp()
    {
        var output = Print(
            typeof(SubjectReusedDeclarationPatternSpecimens<,>),
            nameof(SubjectReusedDeclarationPatternSpecimens<object, object>.ReusedSubject));

        var match = Regex.Match(output, @"if \((\w+) is T (V_\d+)\)");
        Assert.True(match.Success, output);
        var subject = match.Groups[1].Value;
        // The subject temp survives (assigned once, read by the pattern and the
        // trailing use) rather than being inlined.
        Assert.Contains($"{subject} = Subject;", output);
        Assert.Contains($"Console.WriteLine({subject})", output);
        Assert.Contains($"Console.WriteLine({match.Groups[2].Value})", output);
        Assert.DoesNotContain("(object)", output);
        Assert.DoesNotContain("as T", output);
    }

    // Positive, flat-CFG variant (#2831 real-world shape): when the pattern's
    // result feeds a later use rather than an immediate return, StructuringPass
    // leaves the region as raw blocks and a `ConditionalBranch` — the exact
    // shape FluentAssertions' `Be`/`BeOfType`/etc. compile to (a value merged
    // from a ternary-like `cond ? isinst-extract : default`, not an early
    // return) — instead of an `IfStatement`. `IsProvenByFlatGuard` must still
    // recognize the single-predecessor, opposite-polarity guard and bridge it.
    [Fact]
    public void UnconstrainedDeclarationPattern_FlatGuard_BridgesExtractionThroughObject()
    {
        var output = Print(
            typeof(FlatGuardDeclarationPatternSpecimens<,>),
            nameof(FlatGuardDeclarationPatternSpecimens<object, object>.ExtractThenUse));

        AssertBridgedLocal(output, negatedGuard: true);
        Assert.DoesNotContain("as T", output);
    }

    // Negative: class-constrained T never reaches UnboxAny at all (a class-
    // constrained `as T` result is already the pattern's type — no unboxing
    // round-trip). #1752's flat `T t = (T)((Subject) as T); if (t is not null)`
    // form must stay exactly as-is.
    [Fact]
    public void ClassConstrainedDeclarationPattern_StaysUnaffected()
    {
        var output = Print(
            typeof(GenericDeclarationPatternSpecimensClass<,>),
            nameof(GenericDeclarationPatternSpecimensClass<object, object>.ClassConstrained));

        Assert.Contains("as T", output);
        Assert.DoesNotContain("(object)", output);
    }

    // Negative: a concrete reference type resolves through IsValueTypeTarget and
    // never nests IsInstance inside UnboxAny (there is no UnboxAny at all) — the
    // pre-existing `as string` + null-test form must stay exactly as-is.
    [Fact]
    public void ConcreteReferenceTypeDeclarationPattern_StaysUnaffected()
    {
        var output = Print(
            typeof(ConcreteDeclarationPatternSpecimens),
            nameof(ConcreteDeclarationPatternSpecimens.ConcreteReference));

        Assert.Contains("as string", output);
        Assert.DoesNotContain("(object)", output);
    }

    // Negative: a concrete value type's UnboxAny operand is the stored object
    // local directly (no nested IsInstance duplicate — csc reuses the temp), so
    // the new same-target bridge must not fire; the existing bare `(int)V_1`
    // form must stay exactly as-is.
    [Fact]
    public void ConcreteValueTypeDeclarationPattern_StaysUnaffected()
    {
        var output = Print(
            typeof(ConcreteDeclarationPatternSpecimens),
            nameof(ConcreteDeclarationPatternSpecimens.ConcreteValue));

        Assert.Matches(@"\(int\)V_\d+", output);
        Assert.DoesNotContain("(object)", output);
    }

    // Negative: an else arm alongside the proven branch — the same-target test
    // still gates only its own Then, so the fallthrough extraction after the
    // early return (which happens to be the same object local, not the
    // duplicated IsInstance shape) renders as it always did.
    [Fact]
    public void MismatchTargetWithElseArm_StaysUnaffected()
    {
        var output = Print(
            typeof(MismatchDeclarationPatternSpecimens<>),
            nameof(MismatchDeclarationPatternSpecimens<object>.MismatchWithElse));

        var guardedLocal = Regex.Match(output, @"if \((V_\d+) is T\)");
        Assert.True(guardedLocal.Success, output);
        Assert.Contains($"(T){guardedLocal.Groups[1].Value}", output);
        Assert.DoesNotContain("as T", output);
    }

    // Synthetic (#2831 proof-boundary): the same UnboxAny(IsInstance(x, T)) shape
    // with no enclosing guard at all. Nothing proves the isinst already
    // succeeded, so the bridge must not fire — bridging unconditionally would
    // hide a real failure (NullReferenceException on unbox vs. a silently
    // different outcome) behind an always-succeeds cast. The prior fallback
    // (`as T`, already an existing, separately-tracked limitation for this
    // ungated shape) is left untouched rather than papered over.
    [Fact]
    public void UnboxOfIsInstance_WithoutGuardingIf_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        var test = new IsInstance(generic, new LoadArgument(0, "value", objectType));
        var extraction = new Box(generic, new UnboxAny(generic, test));

        var block = new Block();
        block.Add(new Return(extraction));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("value", objectType)], HasThis: false, GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    // Synthetic (#2831 proof-boundary): the extraction sits in the guard's Else
    // arm — the known-failed branch — rather than its Then. Bridging there would
    // claim a test that is known NOT to have succeeded; must not bridge.
    [Fact]
    public void UnboxOfIsInstance_InElseArm_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression Test() => new IsInstance(generic, new LoadArgument(0, "value", objectType));
        var extraction = new Box(generic, new UnboxAny(generic, Test()));

        var thenBlock = new Block();
        thenBlock.Add(new Return(new Constant(null, objectType)));
        var elseBlock = new Block();
        elseBlock.Add(new Return(extraction));
        var ifStatement = new IfStatement(Test(), thenBlock, elseBlock);

        var outer = new Block();
        outer.Add(ifStatement);
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("value", objectType)], HasThis: false, GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    // Synthetic (#2831 proof-boundary): the guard tests argument 0 but the
    // extraction reads argument 1 — a structurally different value, so the
    // duplicate-read proof cannot hold even though both use the same type. Must
    // not bridge.
    [Fact]
    public void UnboxOfIsInstance_MismatchedGuardOperand_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        var guardTest = new IsInstance(generic, new LoadArgument(0, "a", objectType));
        var extractionTest = new IsInstance(generic, new LoadArgument(1, "b", objectType));
        var extraction = new Box(generic, new UnboxAny(generic, extractionTest));

        var thenBlock = new Block();
        thenBlock.Add(new Return(extraction));
        var ifStatement = new IfStatement(guardTest, thenBlock, null);

        var outer = new Block();
        outer.Add(ifStatement);
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("a", objectType), new Parameter("b", objectType)], HasThis: false, GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    // Synthetic (#2831 proof-boundary): a store to the tested local between the
    // guard and the extraction invalidates the "same unmutated read" proof even
    // though the two IsInstance operands are structurally identical locals —
    // the local could hold a different value by the time of extraction. Must
    // not bridge.
    [Fact]
    public void UnboxOfIsInstance_WithInterveningWrite_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);
        var guardTest = new IsInstance(generic, ReadLocal());
        var extraction = new Box(generic, new UnboxAny(generic, new IsInstance(generic, ReadLocal())));

        var thenBlock = new Block();
        thenBlock.Add(new StoreLocal(0, objectType, new Constant(null, objectType)));
        thenBlock.Add(new Return(extraction));
        var ifStatement = new IfStatement(guardTest, thenBlock, null);

        var outer = new Block();
        outer.Add(ifStatement);
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_WithNestedInterveningWrite_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var innerThen = new Block();
        innerThen.Add(new StoreLocal(
            0,
            objectType,
            new Constant(null, objectType)));
        innerThen.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));
        var outerThen = new Block();
        outerThen.Add(new IfStatement(
            new Constant(true, boolType),
            innerThen,
            null));
        var outer = new Block();
        outer.Add(new IfStatement(
            new IsInstance(generic, ReadLocal()),
            outerThen,
            null));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_WithinNestedStructuredBlock_Bridges()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var boolType = TypeRef.CoreLib("System", "Boolean");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var innerThen = new Block();
        innerThen.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));
        var outerThen = new Block();
        outerThen.Add(new IfStatement(
            new Constant(true, boolType),
            innerThen,
            null));
        var outer = new Block();
        outer.Add(new IfStatement(
            new IsInstance(generic, ReadLocal()),
            outerThen,
            null));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("(T)(object)V_0", output);
        Assert.DoesNotContain("as T", output);
    }

    // Synthetic (#2862 pass-level positive): a provable single-guard extraction,
    // run through the full pipeline, is raised by IsPatternPass to a
    // declaration-pattern binding rather than left on the printer's bridge.
    [Fact]
    public void ProvenGuard_RaisesThroughPipeline()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var then = new Block();
        then.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));
        var outer = new Block();
        outer.Add(new IfStatement(new IsInstance(generic, ReadLocal()), then, null));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = RunPipelineAndPrint(function);

        Assert.Matches(@"V_0 is T V_\d+", output);
        Assert.DoesNotContain("(object)", output);
        Assert.DoesNotContain("as T", output);
    }

    // Synthetic (#2862 pass-level negative): a write to the tested local between
    // the guard and the extraction breaks the "same unmutated value" invariant,
    // so IsPatternPass must not raise a binding (one bound value cannot faithfully
    // stand in for a re-read that may have changed); the bridge is retained.
    [Fact]
    public void InterveningWrite_DoesNotRaiseThroughPipeline()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var then = new Block();
        then.Add(new StoreLocal(0, objectType, new Constant(null, objectType)));
        then.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));
        var outer = new Block();
        outer.Add(new IfStatement(new IsInstance(generic, ReadLocal()), then, null));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = RunPipelineAndPrint(function);

        // The guard is left as a bare type test with no binding: the pass must
        // not mint a pattern local when the tested value may have changed.
        Assert.Contains("if (V_0 is T)", output);
        Assert.DoesNotMatch(@"is T V_\d+", output);
    }

    // Synthetic (#2862 cross-guard soundness): an outer guard tests the value,
    // then the tested local is REASSIGNED, then an inner guard re-tests and an
    // extraction reads the post-write value. The extraction is proven only by
    // the inner guard. The outer guard must NOT be raised to bind the pre-write
    // value and steal the inner extraction (that would return the stale value);
    // only a guard with no intervening write to the site may bind it.
    [Fact]
    public void CrossGuardInterveningWrite_DoesNotRaiseStaleOuterBinding()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");
        IrExpression ReadLocal() => new LoadLocal(0, generic);
        IrExpression TestLocal() => new IsInstance(generic, new Box(generic, ReadLocal()));

        var innerThen = new Block();
        innerThen.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, new Box(generic, ReadLocal()))))));
        var outerThen = new Block();
        outerThen.Add(new StoreLocal(0, generic, new LoadArgument(0, "newValue", generic)));
        outerThen.Add(new IfStatement(TestLocal(), innerThen, null));
        var outer = new Block();
        outer.Add(new IfStatement(TestLocal(), outerThen, null));
        outer.Add(new Return(new Constant(null, objectType)));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("newValue", generic)], HasThis: false, GenericParameterCount: 1),
            [generic],
            body);

        var output = RunPipelineAndPrint(function);

        // The outer guard (before the `V_0 = newValue` write) must not bind a local
        // that is then returned across the write; that would return the stale value.
        Assert.DoesNotMatch(
            new Regex(@"is T (V_\d+)\).*V_0 = newValue.*return \1", RegexOptions.Singleline),
            output);
    }

    // Synthetic (#2862 nested-scope soundness, Gemini review): a provable generic
    // declaration-pattern guard inside a nested lambda must NOT be raised. The
    // pattern local is minted on the ROOT function, but the printer scopes the
    // lambda with its own (smaller) local pool — a root-allocated index would
    // dangle and crash LocalName. The guard must stay on the #2856 object-bridge.
    [Fact]
    public void GuardInsideNestedLambda_IsNotRaised_AndDoesNotCrash()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");
        var funcType = TypeRef.Definition("System.Private.CoreLib", "System", "Func`1");
        IrExpression Test() => new IsInstance(generic, new Box(generic, new LoadLocal(0, generic)));

        var lambdaThen = new Block();
        lambdaThen.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, new Box(generic, new LoadLocal(0, generic)))))));
        var lambdaBlock = new Block();
        lambdaBlock.Add(new IfStatement(Test(), lambdaThen, null));
        lambdaBlock.Add(new Return(new Constant(null, objectType)));
        var lambdaBody = new BlockContainer();
        lambdaBody.Add(lambdaBlock);
        var lambda = new Lambda(funcType, [], [generic], [null], false, false, lambdaBody);

        var block = new Block();
        block.Add(new StoreLocal(0, funcType, lambda));
        block.Add(new Return(new Constant(null, objectType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [generic],
            body);

        // Must not throw (a dangling root-scoped pattern local would crash the
        // lambda's independent printer scope) and must leave the guard bridged.
        var output = RunPipelineAndPrint(function);

        Assert.DoesNotMatch(@"is T V_\d+", output);
    }

    // Synthetic (#2872 gate): the cache local is read AFTER the guard, so binding
    // it in the pattern (which scopes it to the guarded arm) would leave the
    // trailing read referencing an out-of-scope variable. The copy must survive.
    [Fact]
    public void CacheReadAfterGuard_DoesNotBindToCacheLocal()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");
        IrExpression Subject() => new LoadArgument(0, "subject", generic);

        var then = new Block();
        then.Add(new StoreLocal(0, generic, new LoadLocal(1, generic)));
        then.Add(new Return(new Box(generic, new LoadLocal(0, generic))));
        var block = new Block();
        block.Add(new IfStatement(new IsPattern(Subject(), generic, 1), then, null));
        block.Add(new Return(new Box(generic, new LoadLocal(0, generic))));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("subject", generic)], HasThis: false, GenericParameterCount: 1),
            [generic, generic],
            body);

        var output = RunPipelineAndPrint(function);

        // The pattern keeps its synthesized binding and the local-to-local copy
        // survives, because the cache local escapes the guarded arm.
        Assert.Matches(@"is T V_\d+", output);
        Assert.Matches(@"\bV_\d+ = V_\d+;", output);
    }

    // Synthetic (#2872 gate): the cache local is assigned twice inside the guarded
    // arm, so it is not a single-assignment temp; re-pointing the pattern to it
    // would drop the second assignment's value. The copy must survive.
    [Fact]
    public void CacheAssignedTwice_DoesNotBindToCacheLocal()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");

        var then = new Block();
        then.Add(new StoreLocal(0, generic, new LoadLocal(1, generic)));
        then.Add(new StoreLocal(0, generic, new LoadArgument(0, "other", generic)));
        then.Add(new Return(new Box(generic, new LoadLocal(0, generic))));
        var block = new Block();
        block.Add(new IfStatement(
            new IsPattern(new LoadArgument(0, "other", generic), generic, 1), then, null));
        block.Add(new Return(new Constant(null, objectType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("other", generic)], HasThis: false, GenericParameterCount: 1),
            [generic, generic],
            body);

        var output = RunPipelineAndPrint(function);

        Assert.Matches(@"is T V_\d+", output);
        Assert.Matches(@"\bV_\d+ = V_\d+;", output);
    }

    // Synthetic (#2872 gate, Issue 1 re-review): the cache local's address is
    // forwarded through a by-ref-returning receiver call — `p = ref c.Ref()` —
    // and stored into a ref local that outlives the arm. The `LoadLocalAddress`
    // sits directly under the `Call`, so a gate that only inspects the address's
    // direct parent (Store/Return) would miss the escape and fold. The pointer
    // still escapes once the binding narrows to the pattern's scope, so the fold
    // must be rejected and the copy must survive. This exercises the
    // in-place-receiver allow-list's non-by-ref-result clause: a by-ref-returning
    // receiver call is not an in-place consumer.
    [Fact]
    public void CacheAddressForwardedThroughByRefReturningCall_DoesNotBindToCacheLocal()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");
        var byRef = TypeRef.ByRef(generic);
        IrExpression Subject() => new LoadArgument(0, "subject", generic);

        // A by-ref-returning instance method on the cache local; the returned
        // reference forwards the receiver's address to the caller.
        var refReturn = new MethodRef(generic, "Ref", byRef, [], HasThis: true);

        var then = new Block();
        then.Add(new StoreLocal(0, generic, new LoadLocal(1, generic)));
        then.Add(new StoreLocal(2, byRef,
            new Call(refReturn, isVirtual: false, [new LoadLocalAddress(0, generic)])));
        var block = new Block();
        block.Add(new IfStatement(new IsPattern(Subject(), generic, 1), then, null));
        block.Add(new Return(new Constant(null, objectType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("subject", generic)], HasThis: false, GenericParameterCount: 1),
            [generic, generic, byRef],
            body);

        var output = RunPipelineAndPrint(function);

        Assert.Matches(@"is T V_\d+", output);
        Assert.Matches(@"\bV_\d+ = V_\d+;", output);
    }
    // captured into a ref local (`ref T p = ref c;`) that outlives the guarded
    // arm. The address load sits inside the arm, so confinement alone is
    // satisfied, but re-pointing the binding narrows `c` to the pattern's scope
    // and leaves the escaped reference dangling. The copy must survive.
    [Fact]
    public void CacheAddressCapturedToRefLocal_DoesNotBindToCacheLocal()
    {
        var generic = TypeRef.GenericParameter(0, "T");
        var objectType = TypeRef.CoreLib("System", "Object");
        var byRef = TypeRef.ByRef(generic);
        IrExpression Subject() => new LoadArgument(0, "subject", generic);

        var then = new Block();
        then.Add(new StoreLocal(0, generic, new LoadLocal(1, generic)));
        then.Add(new StoreLocal(2, byRef, new LoadLocalAddress(0, generic)));
        var block = new Block();
        block.Add(new IfStatement(new IsPattern(Subject(), generic, 1), then, null));
        block.Add(new Return(new Constant(null, objectType)));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("subject", generic)], HasThis: false, GenericParameterCount: 1),
            [generic, generic, byRef],
            body);

        var output = RunPipelineAndPrint(function);

        Assert.Matches(@"is T V_\d+", output);
        Assert.Matches(@"\bV_\d+ = V_\d+;", output);
    }

    static string RunPipelineAndPrint(IrFunction function)
    {
        IrPasses.Run(function);
        function.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }
    // "jump-target-is-the-success-path" polarity — `if (x is T) goto Success;`
    // — that IsProvenByFlatGuard's non-negated case handles, exercised directly
    // since it does not arise in any compiled fixture in this file. A single
    // predecessor reaches the extraction block only via that jump, so the
    // bridge must fire.
    [Fact]
    public void UnboxOfIsInstance_FlatGuard_JumpTargetPolarity_Bridges()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var guardBlock = new Block(0);
        guardBlock.Add(new StoreLocal(0, objectType, new LoadArgument(0, "value", objectType)));
        guardBlock.Add(new ConditionalBranch(new IsInstance(generic, ReadLocal()), targetOffset: 50));

        var falseBlock = new Block(10);
        falseBlock.Add(new Return(new Constant(null, objectType)));

        var successBlock = new Block(50);
        successBlock.Add(new Return(new Box(generic, new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));

        var body = new BlockContainer();
        body.Add(guardBlock);
        body.Add(falseBlock);
        body.Add(successBlock);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("value", objectType)], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("(object)", output);
        Assert.DoesNotContain("as T", output);
    }

    // Synthetic (#2831 proof-boundary, flat-guard negative): a second block
    // branches directly into the extraction block, bypassing the guard
    // entirely — the single-predecessor requirement must reject this even
    // though the guard's own edge still has the right polarity, because some
    // runtime path reaches the extraction without ever evaluating the test.
    [Fact]
    public void UnboxOfIsInstance_FlatGuard_WithAlternatePredecessor_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var guardBlock = new Block(0);
        guardBlock.Add(new StoreLocal(0, objectType, new LoadArgument(0, "value", objectType)));
        guardBlock.Add(new ConditionalBranch(new LogicalNot(new IsInstance(generic, ReadLocal())), targetOffset: 100));

        var siteBlock = new Block(10);
        siteBlock.Add(new Return(new Box(generic, new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));

        var bypassBlock = new Block(20);
        bypassBlock.Add(new Branch(targetOffset: 10));

        var failBlock = new Block(100);
        failBlock.Add(new Return(new Constant(null, objectType)));

        var body = new BlockContainer();
        body.Add(guardBlock);
        body.Add(siteBlock);
        body.Add(bypassBlock);
        body.Add(failBlock);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [new Parameter("value", objectType)], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_FlatGuard_AtEntryWithBackedge_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var voidType = TypeRef.CoreLib("System", "Void");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var siteBlock = new Block(0);
        siteBlock.Add(new StoreLocal(
            1,
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal()))));
        siteBlock.Add(new Branch(targetOffset: 10));

        var guardBlock = new Block(10);
        guardBlock.Add(new ConditionalBranch(
            new IsInstance(generic, ReadLocal()),
            targetOffset: 0));

        var exitBlock = new Block(20);
        exitBlock.Add(new Return(null));

        var body = new BlockContainer();
        body.Add(siteBlock);
        body.Add(guardBlock);
        body.Add(exitBlock);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(voidType, [], HasThis: false, GenericParameterCount: 1),
            [objectType, generic],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_WithAddressTakenLocal_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var thenBlock = new Block();
        thenBlock.Add(new InitObject(objectType, new LoadLocalAddress(0, objectType)));
        thenBlock.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal())))));

        var outer = new Block();
        outer.Add(new IfStatement(new IsInstance(generic, ReadLocal()), thenBlock, null));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(objectType, [], HasThis: false, GenericParameterCount: 1),
            [objectType],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_WithAddressTakenArgument_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadArgument() => new LoadArgument(0, "value", objectType);

        var thenBlock = new Block();
        thenBlock.Add(new InitObject(
            objectType,
            new LoadArgumentAddress(0, "value", objectType)));
        thenBlock.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadArgument())))));

        var outer = new Block();
        outer.Add(new IfStatement(
            new IsInstance(generic, ReadArgument()),
            thenBlock,
            null));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                objectType,
                [new Parameter("value", objectType)],
                HasThis: false,
                GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_WithAddressTakenInlineLocalFunctionArgument_DoesNotBridge()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadArgument() => new LoadArgument(0, "value", objectType);

        var thenBlock = new Block();
        thenBlock.Add(new InitObject(
            objectType,
            new LoadArgumentAddress(0, "value", objectType)));
        thenBlock.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadArgument())))));

        var localFunctionBlock = new Block();
        localFunctionBlock.Add(new IfStatement(
            new IsInstance(generic, ReadArgument()),
            thenBlock,
            null));
        var localFunctionBody = new BlockContainer();
        localFunctionBody.Add(localFunctionBlock);
        var localFunction = new LocalFunctionStatement(
            "Local",
            objectType,
            [new Parameter("value", objectType)],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localFunctionBody);

        var outer = new Block();
        outer.Add(localFunction);
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.DoesNotContain("(object)", output);
    }

    [Fact]
    public void UnboxOfIsInstance_OuterAddressDoesNotBlockInlineLocalFunction()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadArgument() => new LoadArgument(0, "value", objectType);

        var thenBlock = new Block();
        thenBlock.Add(new Return(new Box(
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadArgument())))));

        var localFunctionBlock = new Block();
        localFunctionBlock.Add(new IfStatement(
            new IsInstance(generic, ReadArgument()),
            thenBlock,
            null));
        var localFunctionBody = new BlockContainer();
        localFunctionBody.Add(localFunctionBlock);
        var localFunction = new LocalFunctionStatement(
            "Local",
            objectType,
            [new Parameter("value", objectType)],
            isStatic: true,
            [],
            [],
            usesUpdatedMemorySafetyRules: false,
            skipLocalsInit: false,
            localFunctionBody);

        var outer = new Block();
        outer.Add(new ExpressionStatement(
            new LoadArgumentAddress(0, "outer", objectType)));
        outer.Add(localFunction);
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [new Parameter("outer", objectType)],
                HasThis: false,
                GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("(T)(object)value", output);
    }

    [Fact]
    public void UnboxOfIsInstance_FlatGuardInsideLoopRegion_Bridges()
    {
        var objectType = TypeRef.CoreLib("System", "Object");
        var generic = TypeRef.GenericParameter(0, "T");
        IrExpression ReadLocal() => new LoadLocal(0, objectType);

        var guardBlock = new Block(0);
        guardBlock.Add(new ConditionalBranch(
            new LogicalNot(new IsInstance(generic, ReadLocal())),
            targetOffset: 100));
        var siteBlock = new Block(10);
        siteBlock.Add(new StoreLocal(
            1,
            generic,
            new UnboxAny(generic, new IsInstance(generic, ReadLocal()))));
        var failBlock = new Block(100);
        var guardedRegion = new BlockContainer();
        guardedRegion.Add(guardBlock);
        guardedRegion.Add(siteBlock);
        guardedRegion.Add(failBlock);

        var catchBody = new BlockContainer();
        catchBody.Add(new Block(200));
        var loopBody = new Block();
        loopBody.Add(new TryCatch(
            guardedRegion,
            [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody)]));
        var outer = new Block();
        outer.Add(new WhileLoop(
            new Constant(true, TypeRef.CoreLib("System", "Boolean")),
            loopBody));
        var body = new BlockContainer();
        body.Add(outer);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Void"),
                [],
                HasThis: false,
                GenericParameterCount: 1),
            [objectType, generic],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("(T)(object)V_0", output);
        Assert.DoesNotContain("as T", output);
    }

    static void AssertBridgedLocal(string output, bool negatedGuard)
    {
        string condition = negatedGuard ? "is not T" : "is T";
        var guardedLocal = Regex.Match(
            output,
            $@"if \(\((V_\d+)\) {condition}\){(negatedGuard ? " goto" : "")}");
        Assert.True(guardedLocal.Success, output);
        Assert.Contains($"(T)(object)({guardedLocal.Groups[1].Value})", output);
    }

    // #2862: a raised generic declaration pattern binds a local in the guard and
    // uses only that binding in the guarded body, with the subject inlined (no
    // `(object)` bridge, no `as T`, no lowering-only subject temp store).
    static void AssertRaisedBinding(string output)
    {
        var match = Regex.Match(output, @"if \(Subject is T (V_\d+)\)");
        Assert.True(match.Success, output);
        Assert.Contains($"Console.WriteLine({match.Groups[1].Value})", output);
        Assert.DoesNotContain("(object)", output);
        Assert.DoesNotContain("as T", output);
        Assert.DoesNotContain("= Subject;", output);
    }
}

public class GenericDeclarationPatternSpecimens<T, TSubject>
{
    public TSubject Subject { get; set; } = default!;

    // #2831 minimal repro: unconstrained T cannot store `Subject as T` (CS0413),
    // so csc re-tests+unboxes at the use site instead.
    public void Unconstrained()
    {
        if (Subject is T t)
        {
            System.Console.WriteLine(t);
        }
    }

    // #2862 robustness: the binding is read at two dominated sites; csc re-tests
    // and unboxes at each, so both extractions must collapse onto one raised
    // pattern local.
    public void MultipleUses()
    {
        if (Subject is T t)
        {
            System.Console.WriteLine(t);
            System.Console.WriteLine(t);
        }
    }
}

// #2862 slice-2 boundary: the subject cache is read again after the guard, so
// the lowering-only temp cannot be inlined into the pattern (it is not
// single-use). The binding is still raised, but the `TSubject V = Subject;`
// store must survive.
public class SubjectReusedDeclarationPatternSpecimens<T, TSubject>
{
    public TSubject Subject { get; set; } = default!;

    public void ReusedSubject()
    {
        TSubject subject = Subject;
        if (subject is T t)
        {
            System.Console.WriteLine(t);
        }

        System.Console.WriteLine(subject);
    }
}

public class GenericDeclarationPatternSpecimensStruct<T, TSubject> where T : struct
{
    public TSubject Subject { get; set; } = default!;

    public void StructConstrained()
    {
        if (Subject is T t)
        {
            System.Console.WriteLine(t);
        }
    }
}

// #2831 flat-CFG real-world shape: the pattern's result feeds a later
// statement (not an immediate return), so StructuringPass leaves the region
// as raw blocks/ConditionalBranch instead of nesting an IfStatement — mirrors
// FluentAssertions' `Be`/`BeOfType`/etc.
public class FlatGuardDeclarationPatternSpecimens<T, TSubject>
{
    public TSubject Subject { get; set; } = default!;

    public T ExtractThenUse()
    {
        T typed = Subject is T t ? t : default!;
        System.Console.WriteLine("after");
        return typed;
    }
}

public class GenericDeclarationPatternSpecimensClass<T, TSubject> where T : class
{
    public TSubject Subject { get; set; } = default!;

    public void ClassConstrained()
    {
        if (Subject is T t)
        {
            System.Console.WriteLine(t);
        }
    }
}

public class ConcreteDeclarationPatternSpecimens
{
    public object Subject { get; set; } = default!;

    public void ConcreteReference()
    {
        if (Subject is string t)
        {
            System.Console.WriteLine(t);
        }
    }

    public void ConcreteValue()
    {
        if (Subject is int t)
        {
            System.Console.WriteLine(t);
        }
    }
}

public class MismatchDeclarationPatternSpecimens<T>
{
    public object Subject { get; set; } = default!;

    public void MismatchWithElse()
    {
        if (Subject is T t)
        {
            System.Console.WriteLine(t);
            return;
        }
        System.Console.WriteLine("no");
    }
}
