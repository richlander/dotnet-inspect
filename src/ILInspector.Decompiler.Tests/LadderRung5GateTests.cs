using System.Reflection.PortableExecutable;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The rung 5 guard for the decompiler product quality ladder (#1599): C# 8-9
/// syntax surface. It decompiles the in-repo <see cref="LadderRung5.Program"/>
/// fixture and pins the scoped rung 5 bar that already holds, so a regression
/// below it fails fast in PR CI:
/// <list type="bullet">
/// <item>the fixture exposes exactly the rung 5 source-visible member set — no
/// construct is silently dropped;</item>
/// <item><b>no invalid <c>Full</c></b>: every member that imports as <c>Full</c>
/// renders as valid C# — zero malformed <c>Full</c> methods and zero non-noise
/// semantic-binding defects (the core ladder bar);</item>
/// <item>the constructs that already meet the rung 5 bar — index/range operators,
/// <c>using</c> declarations, property/type patterns, scalar relational switch
/// expressions, and init-only object initializers — render recognizably;</item>
/// <item>the compiler-synthesized record members render at the rung 5 bar:
/// <c>Deconstruct</c> qualifies its shadowed instance reads (<c>X = this.X;</c>,
/// #1632) and the positional-record primary constructor renders valid
/// <c>Full</c> with the implicit base call elided (no owned base-ctor residual,
/// #1639).</item>
/// <item>C# 11 checked user-defined operators render their use with a
/// <c>checked(...)</c> context (<c>checked(a + b)</c>) rather than an explicit
/// <c>op_Checked*</c> method call, which is CS0571 invalid <c>Full</c> (#1706).</item>
/// </list>
/// </summary>
public class LadderRung5GateTests
{
    static string FixturePath => typeof(LadderRung5.Program).Assembly.Location;
    static readonly string ProgramType = typeof(LadderRung5.Program).FullName!;
    static readonly string RecordType = typeof(LadderRung5.Point).FullName!;
    static readonly string CheckedOperatorType = typeof(LadderRung5.CheckedMeters).FullName!;
    static readonly string PrimaryCtorType = typeof(LadderRung5.PrimaryCounter).FullName!;
    static readonly string ModernSyntaxType = typeof(LadderRung5.ModernSyntax).FullName!;

    // The exact rung 5 source-visible Program member set. Locked so a future
    // fixture edit that drops a construct fails loudly instead of silently
    // shrinking the measured scope.
    static readonly string[] ExpectedProgramMembers =
    [
        ".ctor", "Describe", "FromEnd", "IsOrigin", "IsRealString", "LastElement",
        "MakeScaled", "MiddleSlice", "Prefix", "Quadrant", "Shift", "Size",
        "UsingDeclaration",
    ];

    // The record's compiler-synthesized member set (primary + copy ctor, Clone,
    // Deconstruct, the two Equals overloads, GetHashCode, PrintMembers, ToString,
    // EqualityContract, accessors, equality operators). Locked because rung 5
    // ("records/init where visible") measures these — Point.Deconstruct (#1632)
    // and the positional primary constructor (#1639) are asserted green in
    // Rung5Fixture_RendersRecordMembers — so a future importer/printer change that
    // drops a synthesized member must fail here rather than silently shrink scope.
    static readonly string[] ExpectedRecordMembers =
    [
        ".ctor", ".ctor", "<Clone>$", "Deconstruct", "Equals", "Equals",
        "GetHashCode", "PrintMembers", "ToString", "get_EqualityContract",
        "get_Magnitude", "get_X", "get_Y", "op_Equality", "op_Inequality",
        "set_Magnitude", "set_X", "set_Y",
    ];

    [Fact]
    public void Rung5Fixture_ExposesExactProgramMemberSet()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == ProgramType).ToList();
        Assert.Equal(
            ExpectedProgramMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Rung5Fixture_ExposesExactRecordMemberSet()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == RecordType).ToList();
        Assert.Equal(
            ExpectedRecordMembers,
            members.Select(m => m.Name).Order(StringComparer.Ordinal).ToArray());
    }

    // The core ladder bar for rung 5: every member that imports as Full must
    // render valid C#, and no rung 5 member should be malformed after the #1630
    // with-expression fix removed the only Partial-malformed fixture member.
    [Fact]
    public void Rung5Fixture_HasNoInvalidFull()
    {
        var results = ValidityCheck.Evaluate(FixturePath)
            .Where(r => r.TypeName.StartsWith("LadderRung5.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(results);

        var malformedFull = results
            .Where(r => r.IsFull && r.IsMalformed)
            .Select(r => $"{r.Id}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformedFull.Length == 0,
            "Rung 5 requires no invalid Full; malformed Full: " + string.Join("; ", malformedFull));

        var malformed = results
            .Where(r => r.IsMalformed)
            .Select(r => $"{r.Id}: {r.MalformedDiagnostics[0].Id} {r.MalformedDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(malformed.Length == 0,
            "Rung 5 requires zero malformed fixture members; malformed: " + string.Join("; ", malformed));

        var semantic = results
            .Where(r => r.HasSemanticDefect)
            .Select(r => $"{r.Id}: {r.SemanticDiagnostics[0].Id} {r.SemanticDiagnostics[0].Message}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(semantic.Length == 0,
            "Rung 5 requires zero semantic-binding defects; defects: " + string.Join("; ", semantic));

        // Teeth: every Full member must actually have been bound (not skipped by
        // the cap), so the malformed/semantic assertions are not vacuous.
        Assert.All(
            results.Where(r => r.IsFull),
            r => Assert.True(r.SemanticChecked, $"Full method {r.Id} was not semantically bound."));
    }

    // The rung 5 constructs that already meet the bar render recognizably. These
    // catch intra-fixture misrenders that the validity shell filters as
    // member-resolution noise.
    [Fact]
    public void Rung5Fixture_RendersWorkingConstructs()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == ProgramType).ToList();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        // Index-from-end and range operators.
        Assert.Equal("return values[^1];", Body("LastElement"));
        Assert.Equal("return values[^offset];", Body("FromEnd"));
        Assert.Equal("return values[1..^1];", Body("MiddleSlice"));
        Assert.Equal("return values[..2];", Body("Prefix"));

        // Property pattern.
        Assert.Contains("point is not null && point.X == 0 && point.Y == 0", Body("IsOrigin"));

        // Type pattern with a negated constant pattern. Pin the negation, not just
        // the `is string` test and the `""` literal: a decompile that dropped the
        // `not ""` (e.g. `value is string s && s == ""`) reverses the semantics and
        // must fail here.
        var isRealString = Body("IsRealString");
        Assert.Contains("value is string", isRealString);
        Assert.Matches(@"!\(\s*\w+ == """"\s*\)|!= """"", isRealString);

        // Scalar relational/type switch expressions lower to recognizable
        // branches with the right arms.
        var size = Body("Size");
        Assert.Contains("return \"negative\";", size);
        Assert.Contains("return \"small\";", size);
        Assert.Contains("return \"big\";", size);

        var describe = Body("Describe");
        Assert.Contains("return s;", describe);
        Assert.Contains("\"positive int\"", describe);
        Assert.Contains("\"null\"", describe);

        var quadrant = Body("Quadrant");
        Assert.DoesNotContain("goto", quadrant);
        Assert.Contains("if (x > 0)", quadrant);
        Assert.Contains("if (x < 0)", quadrant);
        Assert.Contains("if (y > 0)", quadrant);
        Assert.Contains("if (y < 0)", quadrant);
        Assert.Contains("return \"I\";", quadrant);
        Assert.Contains("return \"II\";", quadrant);
        Assert.Contains("return \"III\";", quadrant);
        Assert.Contains("return \"IV\";", quadrant);
        Assert.Contains("return \"axis\";", quadrant);

        // using declaration over a disposable local.
        Assert.Contains("using (MemoryStream stream = new MemoryStream(bytes))", Body("UsingDeclaration"));

        // init-only property set through an object initializer on a record.
        Assert.Contains("new Point(x, y) { Magnitude = x + y }", Body("MakeScaled"));

        // nondestructive mutation (with-expression) on a record.
        Assert.Contains("return point with { X = point.X + dx };", Body("Shift"));
    }

    // The compiler-synthesized record members render at the rung 5 bar. These pin
    // the two fixes that closed the modern-syntax burndown rows (#1632, #1639) so
    // they cannot silently regress.
    [Fact]
    public void Rung5Fixture_RendersRecordMembers()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == RecordType).ToList();
        string Body(IrFunction function) =>
            CSharpPrinter.PrintRaised(function).Output?.Trim() ?? "";

        // #1632: Deconstruct(out int X, out int Y) reads this.X/this.Y. The out
        // parameters shadow the X/Y properties, so the receiver must be qualified;
        // a dropped receiver renders the self-assigning no-op `X = X;`.
        var deconstruct = Body(members.Single(m => m.Name == "Deconstruct").Function);
        Assert.Contains("X = this.X;", deconstruct);
        Assert.Contains("Y = this.Y;", deconstruct);
        Assert.DoesNotContain("X = X;", deconstruct);
        Assert.DoesNotContain("Y = Y;", deconstruct);

        // #1639: the positional-record primary constructor stores its parameters
        // into the backing fields and then calls the implicit parameterless base
        // constructor. It must render valid Full with the base call elided — no
        // owned base-ctor residual.
        var primaryCtor = members
            .Where(m => m.Name == ".ctor" && m.Function.Signature.Parameters.Length == 2)
            .Select(m => m.Function)
            .Single();
        var primaryBody = Body(primaryCtor);
        Assert.Contains("this.X = X;", primaryBody);
        Assert.Contains("this.Y = Y;", primaryBody);
        Assert.DoesNotContain("Unsupported", primaryBody);
        Assert.DoesNotContain("/*", primaryBody);

        // The copy constructor stays clean: instance reads from the source record,
        // base call elided.
        var copyCtor = members
            .Where(m => m.Name == ".ctor" && m.Function.Signature.Parameters.Length == 1)
            .Select(m => m.Function)
            .Single();
        var copyBody = Body(copyCtor);
        Assert.Contains("this.X = original.X;", copyBody);
        Assert.DoesNotContain("Unsupported", copyBody);
    }

    // C# 11 checked user-defined operators (#1706). The USE of a checked operator
    // must force the checked overload with a checked(...) context; a bare method
    // spelling (CheckedMeters.op_CheckedAddition(a, b)) is CS0571 invalid Full.
    [Fact]
    public void Rung5Fixture_RendersCheckedOperators()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == CheckedOperatorType).ToList();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        // The checked use renders checked(a + b), NOT an explicit op_Checked* call.
        var addChecked = Body("AddChecked");
        Assert.Equal("return checked(a + b);", addChecked);
        Assert.DoesNotContain("op_Checked", addChecked);

        // The unchecked sibling stays a bare operator with no checked wrapper.
        var addUnchecked = Body("AddUnchecked");
        Assert.Equal("return a + b;", addUnchecked);
        Assert.DoesNotContain("checked", addUnchecked);
        Assert.DoesNotContain("op_Addition", addUnchecked);

        // #1712: a user-defined ++/-- use folds back to the operator, never the
        // CS0571 op_Increment(a) method call. The checked use forces checked(...).
        Assert.Equal("return ++a;", Body("PreIncrement"));
        Assert.Equal("return a++;", Body("PostIncrement"));
        Assert.Equal("return checked(++a);", Body("PreIncrementChecked"));
        Assert.Equal("return --a;", Body("PreDecrement"));
        foreach (var name in new[] { "PreIncrement", "PostIncrement", "PreIncrementChecked", "PreDecrement" })
            Assert.DoesNotContain("op_", Body(name));

        // The statement form (value discarded) folds to a++; the checked statement
        // uses a checked { ... } block (checked(a++) is CS0201 in statement form).
        var stmt = Body("StatementIncrement");
        Assert.Contains("a++;", stmt);
        Assert.DoesNotContain("op_", stmt);
        var stmtChecked = Body("StatementIncrementChecked");
        Assert.Contains("checked { a++; }", stmtChecked);
        Assert.DoesNotContain("op_", stmtChecked);

        // A checked for-loop over a class loop variable wraps the whole loop in
        // checked { } so the header i++ selects the checked overload.
        var classLoop = CSharpPrinter.PrintRaised(
            LoadRaisedMembers().Single(m => m.Type == typeof(LadderRung5.CheckedStep).FullName && m.Name == "CheckedForLoop").Function)
            .Output ?? "";
        Assert.Contains("checked", classLoop);
        Assert.Contains("for (", classLoop);
        Assert.Contains("i++", classLoop);
        Assert.DoesNotContain("op_", classLoop);

        // An unchecked increment inside the checked-wrapped loop body renders an
        // unchecked { j++; } block (valid statement, correct unchecked overload).
        var mixedLoop = CSharpPrinter.PrintRaised(
            LoadRaisedMembers().Single(m => m.Type == typeof(LadderRung5.CheckedStep).FullName && m.Name == "CheckedForLoopMixed").Function)
            .Output ?? "";
        Assert.Contains("unchecked { j++; }", mixedLoop);
        Assert.DoesNotContain("op_", mixedLoop);
    }

    // C# 12 primary constructor on a class. The captured parameters lift into
    // unspeakable <param>P fields; the decompiler must render them as the
    // parameter name — a declared lowered field plus this.-qualified constructor
    // stores and bare reads — never the raw <seed>P, which is invalid Full.
    [Fact]
    public void Rung5Fixture_RendersPrimaryConstructorCaptures()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == PrimaryCtorType).ToList();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        // Capture reads de-mangle to the parameter name, never the <seed>P field.
        Assert.Equal("return seed + step;", Body("Next"));
        Assert.Equal("return seed + extra;", Body("SeedPlus"));

        // The constructor stores the captured parameters into the de-mangled
        // fields with this.-qualification (the parameter shadows the field).
        var ctor = Body(".ctor");
        Assert.Contains("this.seed = seed;", ctor);
        Assert.Contains("this.step = step;", ctor);

        // The composed type declares the capture fields under the parameter name,
        // so the bodies' reads bind. No leftover unspeakable <…>P leaks.
        var source = ComposeType(PrimaryCtorType);
        Assert.Contains("private int seed;", source);
        Assert.Contains("private int step;", source);
        Assert.DoesNotContain(">P", source);
        Assert.DoesNotContain("<seed>", source);
    }

    // Other straightforward C# 11-12 modern-syntax shapes render recognizably: a
    // collection-expression spread recovers to [..head, tail]; a list pattern and
    // a required-member object initializer render as recognizable C#.
    [Fact]
    public void Rung5Fixture_RendersModernSyntaxConstructs()
    {
        var members = LoadRaisedMembers().Where(m => m.Type == ModernSyntaxType).ToList();
        string Body(string name) =>
            CSharpPrinter.PrintRaised(members.Single(m => m.Name == name).Function).Output?.Trim() ?? "";

        Assert.Equal("return [..head, tail];", Body("Spread"));
        Assert.Contains("xs.Length == 3", Body("IsOneTwoThree"));
        Assert.Contains("xs[0] == 1", Body("IsOneTwoThree"));
        Assert.Contains("new ModernHolder { Name = \"n\", Count = 3 }", Body("MakeHolder"));
    }

    static string ComposeType(string fullName)
    {
        using var pe = new PEReader(File.OpenRead(FixturePath));
        var api = ApiSurfaceExtractor.Extract(pe);
        var type = Assert.Single(api.Types, t => t.FullName == fullName);
        var source = TypeSourceComposer.Compose(type, FixturePath, pdbPath: null);
        Assert.NotNull(source);
        return source!;
    }

    static List<(string Type, string Name, IrFunction Function)> LoadRaisedMembers()
    {
        var members = new List<(string Type, string Name, IrFunction Function)>();
        using var source = MetadataSource.Open(FixturePath);
        foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
        {
            if (!typeName.StartsWith("LadderRung5.", StringComparison.Ordinal))
                continue;
            IrPasses.Run(function);
            members.Add((typeName, methodName, function));
        }
        return members;
    }
}
