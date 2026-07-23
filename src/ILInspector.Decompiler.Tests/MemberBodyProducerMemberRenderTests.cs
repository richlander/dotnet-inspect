using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the per-member whole render (<see cref="MemberBodyProducer.ProduceMember"/>)
/// against the whole-type listing (<see cref="MemberBodyProducer.Project"/>): the
/// text produced for a single member is byte-identical to that member's segment
/// in the listing — one composition, no drift. This is the #2996 output-contract
/// enabler: CSharp-owned signature + decompiler-owned body, per member.
/// </summary>
[Trait("Area", "RoundTrip")]
public sealed class MemberBodyProducerMemberRenderTests
{
    static string AssemblyPath => typeof(MemberBodyProducerMemberRenderTests).Assembly.Location;

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(MemberRenderSpecimen).FullName);
    }

    [Fact]
    public void ProduceMember_ByteIdenticalToWholeTypeSegment_ForEveryMember()
    {
        var type = Specimen();
        var listing = MemberBodyProducer.Project(type, AssemblyPath, pdbPath: null).Output;
        Assert.NotNull(listing);

        Assert.NotEmpty(type.Members);
        foreach (var member in type.Members)
        {
            var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

            Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
            Assert.True(rendered.IsComplete);
            Assert.NotNull(rendered.Text);
            // The per-member text is exactly the member's segment in the
            // whole-type listing — no separate signature implementation.
            Assert.Contains(rendered.Text!, listing);
        }
    }

    [Fact]
    public void ProduceMembers_BatchIsByteIdenticalToPerMember_ForEveryMember()
    {
        var type = Specimen();
        var batch = MemberBodyProducer.ProduceMembers(type, AssemblyPath, pdbPath: null);

        Assert.NotEmpty(type.Members);
        foreach (var member in type.Members)
        {
            var single = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);
            Assert.True(batch.TryGetValue(member, out var batched),
                $"batch render missing member {member.Name}");

            // The batch entry is byte-identical to the per-member render — same
            // status, text, and imports. The batch only amortizes the assembly
            // open and type-map build; it must not change any member's output.
            Assert.Equal(single.Status, batched.Status);
            Assert.Equal(single.Text, batched.Text);
            Assert.Equal(single.Namespaces, batched.Namespaces);
        }
    }

    [Fact]
    public void ProduceMember_RendersExpressionBodiedArrow()
    {
        var type = Specimen();
        var increment = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.Increment));

        var rendered = MemberBodyProducer.ProduceMember(type, increment, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // Whole member: CSharp-owned signature + decompiler body, arrow layout.
        Assert.Contains("int Increment(", rendered.Text);
        Assert.Contains("=> ", rendered.Text);
    }

    [Fact]
    public void ProduceMember_WrapsExpressionBodiedArrow_WhenRequested()
    {
        var type = Specimen();
        var increment = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.Increment));

        var rendered = MemberBodyProducer.ProduceMember(
            type,
            increment,
            AssemblyPath,
            pdbPath: null,
            printerOptions: new PrinterOptions { ExpressionBodyArrowPlacement = ExpressionBodyArrowPlacement.NextLine });

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Equal("    public int Increment(int n)\n        => n + 1;", rendered.Text!.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProduceMember_RendersThrowStubAsExpressionBody()
    {
        var type = Specimen();
        var stub = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.ThrowStub));

        var rendered = MemberBodyProducer.ProduceMember(type, stub, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // The canonical #2996 case: a throwing stub is one expression-bodied
        // member spelled by the shared CSharp layout, not a block.
        Assert.Contains("=> throw", rendered.Text);
    }

    [Fact]
    public void ProduceMember_RendersBlockBodiedMethod()
    {
        var type = Specimen();
        var log = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.Log));

        var rendered = MemberBodyProducer.ProduceMember(type, log, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Contains("void Log(", rendered.Text);
        // Two side-effecting statements cannot fold to an expression body.
        Assert.Contains("{", rendered.Text);
        Assert.DoesNotContain("=>", rendered.Text);
    }

    [Fact]
    public void ProduceMember_PreservesQualifiedNameInsideStringLiteral()
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.QuotedTypeName));

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // Name-shortening must never reach inside a string literal. An escaped
        // quote must not flip in-literal parity and shorten System.String to
        // String, which would corrupt the ldstr operand and induce a false
        // compile-back OperandDiff (#3062).
        Assert.Contains("System.String", rendered.Text);
    }

    [Fact]
    public void ProduceMember_PreservesQualifiedNameInsideInterpolationHoleLiteral()
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == nameof(MemberRenderSpecimen.InterpolatedQuotedTypeName));

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // The body re-sugars to an interpolated string; guard that the shape is
        // actually recovered so the hole scan is exercised.
        Assert.Contains("$\"", rendered.Text);
        // Name-shortening scans a hole's code but must copy nested literals
        // verbatim: System.String inside the hole's "System.String" constant
        // must survive, not be mis-segmented and shortened to String (#3064).
        Assert.Contains("\"System.String\"", rendered.Text);
    }

    [Theory]
    [InlineData(nameof(MemberRenderSpecimen.AliasQualifiedShadow))]
    [InlineData(nameof(MemberRenderSpecimen.AliasQualifiedShadowInHole))]
    public void ProduceMember_PreservesAliasQualifiedNameUnderShadowing(string memberName)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // The printer emits global::System.Math to escape the shadowing Math
        // parameter. Shortening must keep the full alias-qualified path, not
        // strip it to the invalid global::Math (CS0400) — in a hole or not.
        Assert.Contains("global::System.Math", rendered.Text);
        Assert.DoesNotContain("global::Math", rendered.Text);
    }

    [Theory]
    [InlineData(nameof(MemberRenderSpecimen.EscapedAliasQualifiedShadow),
        "global::@event.Models.TypeNameShadow", "global::@TypeNameShadow")]
    [InlineData(nameof(MemberRenderSpecimen.SystemEscapedAliasQualifiedShadow),
        "global::System.@event.Models.SystemNameShadow", "global::System.@SystemNameShadow")]
    public void ProduceMember_PreservesAliasQualifiedNameWithEscapedKeywordNamespace(
        string memberName, string expectedFullPath, string corruptedForm)
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == memberName);

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        // A namespace segment that is a keyword is printed @-escaped, so an '@'
        // sits between the '::' alias qualifier and the matched metadata
        // namespace. For a System-rooted namespace the System.-stripped prefix
        // even matches mid-chain (after "System.@"), so the guard must walk the
        // whole qualified run back to its '::' root and decline — not strip to
        // the invalid @-escaped form (a stray escape on a name that does not
        // bind, CS0400/CS0234) (#3064 review).
        Assert.Contains(expectedFullPath, rendered.Text);
        Assert.DoesNotContain(corruptedForm, rendered.Text);
    }
}

#pragma warning disable CA1822 // members are instance to exercise real signatures
public sealed class MemberRenderSpecimen
{
    public MemberRenderSpecimen(int seed) => Value = seed;

    public int Value { get; }

    public string Name { get; set; } = "";

    public int Increment(int n) => n + 1;

    public void Log(int n)
    {
        Console.WriteLine(n);
        Console.WriteLine(n + 1);
    }

    public void ThrowStub() => throw new NotImplementedException();

    // A string constant whose value contains a double-quote followed by a
    // fully-qualified type name. The rendered literal escapes the quote (\"),
    // so a name-shortener that splits on '"' without honoring escapes flips its
    // in-literal parity and mutates System.String inside the constant (#3062).
    public string QuotedTypeName() => "a \"System.String\" b";

    static string Echo(string value) => value;

    // An interpolated string whose hole contains a nested string literal that
    // is itself a fully-qualified type name. The decompiler re-sugars this to
    // $"…{Echo("System.String")}…", so a name-shortener that treats the outer
    // $"…" as one literal mis-segments the hole and shortens System.String
    // inside the nested constant, corrupting the ldstr operand (#3064).
    public string InterpolatedQuotedTypeName(int n) => $"n={n} t={Echo("System.String")}";

    // A parameter named Math shadows System.Math, so the printer emits the
    // alias-qualified global::System.Math to disambiguate. Shortening must not
    // strip it to global::Math, which re-introduces the collision and does not
    // bind (CS0400) — both inside an interpolation hole and in plain code (#3064).
    public static int AliasQualifiedShadow(int Math) => System.Math.Abs(Math) + Math;

    public static string AliasQualifiedShadowInHole(int Math) => $"v={System.Math.Abs(Math) + Math}";

    // The referenced type lives in @event.Models, a namespace whose first
    // segment is a keyword, so the printer escapes it. A parameter named
    // TypeNameShadow shadows the type, forcing the alias-qualified
    // global::@event.Models.TypeNameShadow. Shortening must keep the full path,
    // not strip it to the invalid global::@TypeNameShadow: the '@' sits between
    // the '::' alias qualifier and the raw metadata namespace, so the guard has
    // to skip the escape (#3064 review).
    public static int EscapedAliasQualifiedShadow(int TypeNameShadow)
        => @event.Models.TypeNameShadow.M(TypeNameShadow);

    // Same hazard, but the namespace is System-rooted with a keyword segment
    // (System.@event.Models). The printer emits global::System.@event.Models.
    // TypeNameShadow; the System.-stripped prefix "event.Models" matches
    // mid-chain after "System.@", so a guard that only inspects the characters
    // just before the match cannot see the '::' root. Shortening must still be
    // declined, not corrupted to global::System.@TypeNameShadow (CS0234).
    public static int SystemEscapedAliasQualifiedShadow(int SystemNameShadow)
        => System.@event.Models.SystemNameShadow.M(SystemNameShadow);
}
#pragma warning restore CA1822
