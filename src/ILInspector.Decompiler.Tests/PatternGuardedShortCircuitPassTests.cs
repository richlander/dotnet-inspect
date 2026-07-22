using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Coverage for #2877: after <see cref="IsPatternPass"/> raises an
/// <c>is</c>-pattern guard, <see cref="PatternGuardedShortCircuitPass"/> inlines
/// the arm-local <c>default(T)</c> temp and folds the guarded single-store
/// diamond back into the <c>&amp;&amp;</c> short-circuit it was lowered from —
/// recovering <c>target = subject is T x &amp;&amp; x.CompareTo(default(T)) &gt; 0</c>
/// (the FluentAssertions <c>BePositive</c> shape).
/// </summary>
[Trait("Area", "Pass")]
public class PatternGuardedShortCircuitPassTests
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

    // The BePositive shape: an `is`-pattern guard whose true arm compares the
    // bound value to default(T) and whose false arm yields false. The whole
    // guard must collapse to a single `&&`, with the default temp inlined as
    // `default(T)` rather than surfacing as a separate zero-inited local.
    [Fact]
    public void PatternGuardedAndDiamond_FoldsToShortCircuitWithInlinedDefault()
    {
        var output = Print(
            typeof(ShortCircuitPatternSpecimens<,>),
            nameof(ShortCircuitPatternSpecimens<int, object>.BePositiveLike));

        Assert.Matches(@"Subject is T (\w+) && \1\.CompareTo\(default\(T\)\) > 0", output);
        Assert.DoesNotContain("initobj", output);
        // No separate `if`/`else` diamond and no standalone `= default` temp.
        Assert.DoesNotContain("else", output);
    }

    // A guard whose arms both store non-constant values is a genuine ternary,
    // not a short-circuit; it must not be rewritten into `&&`/`||`.
    [Fact]
    public void NonConstantArms_DoNotFold()
    {
        var output = Print(
            typeof(ShortCircuitPatternSpecimens<,>),
            nameof(ShortCircuitPatternSpecimens<int, object>.TernaryNotShortCircuit));

        Assert.DoesNotContain("&&", output);
        Assert.DoesNotContain("||", output);
    }

    static readonly TypeRef Bool = TypeRef.CoreLib("System", "Boolean");
    static readonly TypeRef Int = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef ObjectType = TypeRef.CoreLib("System", "Object");

    // A pattern local that escapes the guard (referenced after the diamond) is
    // not confined, so folding to `&&` would leave it use-before-assignment
    // (CS0165); the diamond must be left intact.
    [Fact]
    public void PatternLocalUsedAfterGuard_DoesNotFold()
    {
        // if (arg0 is T V_1) S_256 = V_1; else S_256 = false;
        // <use of V_1 after the diamond>
        var pattern = new IsPattern(new LoadArgument(0, "arg", ObjectType), Int, localIndex: 1);
        var then = new Block();
        then.Add(new StoreStackSlot(256, new LoadLocal(1, Int)));
        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(false, Bool)));
        var ifs = new IfStatement(pattern, then, elseArm);

        var body = new Block();
        body.Add(ifs);
        body.Add(new ExpressionStatement(new LoadLocal(1, Int)));

        var function = Function(body);
        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);

        // Still an if/else diamond — the escaping pattern local blocked the fold.
        Assert.NotEmpty(Descendants(function).OfType<IfStatement>());
    }

    // A default temp preamble whose storage is taken by address elsewhere in the
    // arm may be observed or mutated, so it is not a pure default(T) and the arm
    // must not fold.
    [Fact]
    public void DefaultTempTakenByAddressInArm_DoesNotFold()
    {
        // if (arg0 is T V_1) { V_2 = default; escape(&V_2); S_256 = V_2 > 0; }
        // else S_256 = false;
        var pattern = new IsPattern(new LoadArgument(0, "arg", ObjectType), Int, localIndex: 1);
        var then = new Block();
        then.Add(new InitObject(Int, new LoadLocalAddress(2, Int)));
        then.Add(new ExpressionStatement(new LoadLocalAddress(2, Int)));
        then.Add(new StoreStackSlot(256, new LoadLocal(2, Int)));
        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(false, Bool)));
        var ifs = new IfStatement(pattern, then, elseArm);

        var function = Function(WithStatement(ifs));
        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);

        // The extra address-of blocked default recovery, so the diamond stands
        // and no default(T) leaf was minted.
        Assert.NotEmpty(Descendants(function).OfType<IfStatement>());
        Assert.Empty(Descendants(function).OfType<DefaultValue>());
    }

    // An arm whose preamble is an ordinary side effect (not a default init) is
    // not a pure `&&` operand and must not fold.
    [Fact]
    public void ArmWithNonDefaultPreamble_DoesNotFold()
    {
        var pattern = new IsPattern(new LoadArgument(0, "arg", ObjectType), Int, localIndex: 1);
        var then = new Block();
        then.Add(new ExpressionStatement(new LoadArgument(0, "arg", ObjectType)));
        then.Add(new StoreStackSlot(256, new LoadLocal(1, Int)));
        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(false, Bool)));
        var ifs = new IfStatement(pattern, then, elseArm);

        var function = Function(WithStatement(ifs));
        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);

        Assert.NotEmpty(Descendants(function).OfType<IfStatement>());
    }

    // A diamond whose store target is not boolean (the else arm merely stores
    // the integer 0) must not be read as a `&&`: folding would produce the
    // invalid `t = cond && <int>`.
    [Fact]
    public void NonBooleanArm_DoesNotFold()
    {
        // if (arg0 is T V_1) S_256 = arg1; else S_256 = 0;  (int target)
        var pattern = new IsPattern(new LoadArgument(0, "arg", ObjectType), Int, localIndex: 1);
        var then = new Block();
        then.Add(new StoreStackSlot(256, new LoadArgument(1, "n", Int)));
        var elseArm = new Block();
        elseArm.Add(new StoreStackSlot(256, new Constant(0, Int)));
        var ifs = new IfStatement(pattern, then, elseArm);

        var function = Function(WithStatement(ifs));
        new PatternGuardedShortCircuitPass().Run(function, PassContext.None);

        Assert.NotEmpty(Descendants(function).OfType<IfStatement>());
        Assert.Empty(Descendants(function).OfType<LogicalBinary>());
    }

    static Block WithStatement(IrNode statement)
    {
        var block = new Block();
        block.Add(statement);
        return block;
    }

    static IEnumerable<IrNode> Descendants(IrFunction function) => function.Descendants;

    static IrFunction Function(params Block[] blocks)
    {
        var container = new BlockContainer();
        foreach (var block in blocks)
            container.Add(block);

        var signature = new MethodSignature(TypeRef.CoreLib("System", "Void"), ImmutableArray<Parameter>.Empty, HasThis: false, GenericParameterCount: 0);
        return new IrFunction("M", TypeRef.CoreLib("Tests", "Owner"), signature, [Int], container);
    }
}

public class ShortCircuitPatternSpecimens<T, TSubject> where T : struct, System.IComparable<T>
{
    public TSubject Subject { get; set; } = default!;

    // Mirrors FluentAssertions NumericAssertionsBase.BePositive: the pattern
    // binding gates a CompareTo(default) > 0 test, materialized into a bool the
    // caller consumes so the lowering is a slot-store diamond.
    public string BePositiveLike()
    {
        bool positive = Subject is T subject && subject.CompareTo(default) > 0;
        return positive.ToString();
    }

    // Both arms produce non-constant values, so this is a real ternary and must
    // not be mistaken for a short-circuit.
    public string TernaryNotShortCircuit(int a, int b)
    {
        int value = Subject is T ? a : b;
        return value.ToString();
    }
}
