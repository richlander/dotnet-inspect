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
}
#pragma warning restore CA1822
