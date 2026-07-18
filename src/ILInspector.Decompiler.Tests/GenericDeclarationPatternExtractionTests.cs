using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Coverage for #2831: csc's unconstrained/struct-constrained generic
/// declaration-pattern extraction (<c>if (Subject is T t)</c>) cannot store the
/// <c>isinst</c> result through an <c>as T</c> local, so it re-tests the value
/// inline — <c>UnboxAny T(IsInstance T(...))</c> — instead of the usual
/// <c>StoreLocal(IsInstance) + null-test</c> shape <c>IsPatternPass</c>
/// already recovers. <c>CSharpPrinter.UnboxAnyOperand</c> must recognize
/// the exact same-target test/extract relationship and bridge it through the
/// same <c>(object)</c> intermediary the box+unbox.any generic-math idiom uses,
/// rather than routing the nested test through the general <c>is</c>/<c>as</c>
/// expression rule (invalid for a non-class-constrained <c>T</c> either way —
/// CS0030 for <c>is</c>, CS0413 for <c>as</c>).
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

    [Fact]
    public void UnconstrainedDeclarationPattern_BridgesExtractionThroughObject()
    {
        var output = Print(
            typeof(GenericDeclarationPatternSpecimens<,>),
            nameof(GenericDeclarationPatternSpecimens<object, object>.Unconstrained));

        Assert.Contains("if ((V_1) is T)", output);
        Assert.Contains("(T)(object)(V_1)", output);
        Assert.DoesNotContain("as T", output);
    }

    [Fact]
    public void StructConstrainedDeclarationPattern_BridgesExtractionThroughObject()
    {
        var output = Print(
            typeof(GenericDeclarationPatternSpecimensStruct<,>),
            nameof(GenericDeclarationPatternSpecimensStruct<int, object>.StructConstrained));

        Assert.Contains("if ((V_1) is T)", output);
        Assert.Contains("(T)(object)(V_1)", output);
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

        Assert.Contains("if ((V_1) is not T) goto", output);
        Assert.Contains("(T)(object)(V_1)", output);
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

        Assert.Contains("(int)V_1", output);
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

        Assert.Contains("if (V_1 is T)", output);
        Assert.Contains("(T)V_1", output);
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

    // Synthetic (#2831 proof-boundary, flat-guard positive): the
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
