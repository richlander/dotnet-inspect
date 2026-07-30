using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

/// <summary>
/// The projection that hands a member's positions to a consumer outside this
/// process. <see cref="ResearchViews.MemberProjectionResult.AnnotatedSource"/>
/// answers "what does this member look like annotated?" by baking one gesture
/// into a string; the body map answers "where is everything?" and leaves the
/// gesture to whoever renders. A browser needs the second, because the choice of
/// side comment, caret, or category focus belongs to the reader.
/// </summary>
public class PrintedBodyMapProjectionTests
{
    static PrintedBodyMap Map(string method)
    {
        using var source = MetadataSource.Open(typeof(BodyMapFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(BodyMapFixture).FullName!,
            method,
            BodyMap: true));
        return Assert.IsType<PrintedBodyMap>(projection.BodyMap);
    }

    [Fact]
    public void TheProjectionIsOffUnlessAsked()
    {
        using var source = MetadataSource.Open(typeof(BodyMapFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(BodyMapFixture).FullName!,
            nameof(BodyMapFixture.AllocatesTwice)));

        Assert.Null(projection.BodyMap);
    }

    // The claim the payload exists to make: a position addresses characters that
    // are really there. An off-by-one in the line split, or a column measured
    // against a different string than the one shipped, would leave a consumer
    // underlining past the end of a line.
    [Fact]
    public void EveryPlacedSpanAddressesCharactersThatExistOnTheLineItNames()
    {
        var map = Map(nameof(BodyMapFixture.AllocatesTwice));

        Assert.NotEmpty(map.Annotations);
        Assert.NotEmpty(map.Nodes);

        foreach (var node in map.Nodes)
        {
            Assert.InRange(node.Line, 0, map.Lines.Count - 1);
            Assert.InRange(node.Column, 0, map.Lines[node.Line].Length);
            Assert.True(
                node.Column + node.Length <= map.Lines[node.Line].Length,
                $"{node.Kind} claims [{node.Column}..{node.Column + node.Length}) on a line of {map.Lines[node.Line].Length} characters.");
        }

        foreach (var fact in map.Annotations)
        {
            Assert.InRange(fact.Line, 0, map.Lines.Count - 1);
            if (fact.Length < 0)
                continue;
            Assert.True(
                fact.Column + fact.Length <= map.Lines[fact.Line].Length,
                $"{fact.Descriptor} claims [{fact.Column}..{fact.Column + fact.Length}) on a line of {map.Lines[fact.Line].Length} characters.");
        }
    }

    // A cached delegate allocates once for the life of the process; an uncached
    // one allocates per call. The difference is carried only by Conditionality --
    // AnnotationText renders it as "cached-once" -- so a consumer holding the
    // payload alone would otherwise promote a cached allocation to an
    // unconditional one. CachedOnce is the only non-default value the pipeline
    // actually produces today: AllocationFrequency.PerIteration is declared but
    // never assigned, so pinning it here would gate nothing.
    [Fact]
    public void ConditionalitySurvivesTheProjection()
    {
        var map = Map(nameof(BodyMapFixture.CachesADelegate));

        Assert.Contains(
            map.Annotations,
            fact => fact.Conditionality == AnnotationConditionality.CachedOnce);
    }

    // The hazard this projection introduced: a byte-divergent style lens rewrites
    // the IR in place, so a body map printed from the function the other
    // projections read would leave its rewrites on their graph. It takes its own
    // import, and is produced before them, so asking for it must change nothing
    // they report.
    //
    // The semantics overlay is what this checks, not the annotated render: that
    // render already takes its own import for the same reason, so it would
    // survive the bug and gate nothing. The overlay prints `imported` directly,
    // so a graph rewritten under it renders the lens's spelling instead of the
    // shipped one.
    [Fact]
    public void AskingForTheMapLeavesEveryOtherProjectionIdentical()
    {
        static string Overlay(bool alsoBodyMap)
        {
            using var source = MetadataSource.Open(typeof(BodyMapFixture).Assembly.Location);
            var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(BodyMapFixture).FullName!,
                nameof(BodyMapFixture.GuardBothArmsVariable),
                SemanticsOverlay: true,
                BodyMap: alsoBodyMap,
                PrinterOptions: new PrinterOptions { PreferConditionalExpressionReturn = true }));
            return Assert.IsType<string>(Assert.IsType<DecompilerResult>(projection.SemanticsOverlay).Output);
        }

        string without = Overlay(alsoBodyMap: false);

        // The overlay is not asked for the lens, so it must show the flat guard.
        // If this stops holding the test is measuring nothing, so it is asserted
        // rather than assumed.
        Assert.Contains("if (flag)", without);

        Assert.Equal(without, Overlay(alsoBodyMap: true));
    }

    // The map has to describe the text the same request would render. A consumer
    // showing a style lens while holding positions measured against the shipped
    // spelling would underline the wrong characters.
    [Fact]
    public void PrinterOptionsReachTheMap()
    {
        static PrintedBodyMap Lensed(PrinterOptions? options)
        {
            using var source = MetadataSource.Open(typeof(BodyMapFixture).Assembly.Location);
            var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(BodyMapFixture).FullName!,
                nameof(BodyMapFixture.GuardBothArmsVariable),
                BodyMap: true,
                PrinterOptions: options));
            return Assert.IsType<PrintedBodyMap>(projection.BodyMap);
        }

        var shipped = Lensed(null);
        var lensed = Lensed(new PrinterOptions { PreferConditionalExpressionReturn = true });

        Assert.DoesNotContain(shipped.Lines, line => line.Contains('?') && line.Contains(':'));
        Assert.Contains(lensed.Lines, line => line.Contains('?') && line.Contains(':'));
    }

    // The payload's reason for existing is that it can leave the process, and the
    // process it is going to (a wasm browser engine) has no reflection-based
    // serializer. Passing the source-generated JsonTypeInfo explicitly is what
    // makes this a test of that path rather than of the reflection fallback the
    // test host happens to allow.
    [Fact]
    public void TheMapSerializesThroughASourceGeneratedContext()
    {
        var map = Map(nameof(BodyMapFixture.AllocatesInALoop));

        string json = JsonSerializer.Serialize(map, BodyMapJsonContext.Default.PrintedBodyMap);
        var replayed = JsonSerializer.Deserialize(json, BodyMapJsonContext.Default.PrintedBodyMap);

        Assert.NotNull(replayed);
        Assert.Equal(map.Lines, replayed.Lines);
        Assert.Equal(map.Nodes, replayed.Nodes);
        Assert.Equal(map.Annotations, replayed.Annotations);
    }
}

[JsonSerializable(typeof(PrintedBodyMap))]
internal sealed partial class BodyMapJsonContext : JsonSerializerContext;

public sealed class BodyMapFixture
{
    public static object[] AllocatesTwice()
    {
        var first = new object();
        var second = new object[] { first };
        return second;
    }

    public static List<object> AllocatesInALoop(int count)
    {
        var sink = new List<object>();
        for (int i = 0; i < count; i++)
        {
            sink.Add(new object());
        }

        return sink;
    }

    // Non-capturing, so Roslyn caches the delegate instance in a static field and
    // the allocation happens once rather than per call.
    public static IEnumerable<string> CachesADelegate(IEnumerable<string> items)
        => items.Where(item => item.Length > 0);

    // Both arms variable and returned directly, which is the shape the
    // byte-divergent conditional-return lens rewrites.
    public static bool GuardBothArmsVariable(bool flag, bool first, bool second)
    {
        if (flag)
        {
            return first;
        }

        return second;
    }
}
