using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ProvenanceTests
{
    static IrFunction ImportFixture(string methodName)
    {
        var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function!;
    }

    [Fact]
    public void Import_StampsSourceOffsetsFromOriginatingInstructions()
    {
        // Twice(int x) { var t = x + x; return t; } — every operation node maps
        // to a real IL instruction, so the import should stamp provenance.
        var function = ImportFixture(nameof(CfgSampleClass.Twice));
        var stamped = function.Descendants.Where(n => n.SourceOffset >= 0).ToList();

        Assert.NotEmpty(stamped);
        // Offsets are real IL offsets into a tiny method, never garbage.
        Assert.All(stamped, n => Assert.InRange(n.SourceOffset, 0, 0xFFFF));
        // The terminator is a `ret` — it must carry an offset of its own.
        var ret = Assert.Single(function.Descendants.OfType<Return>());
        Assert.True(ret.SourceOffset >= 0, "the return node should be stamped with the ret offset");
    }

    [Fact]
    public void SourceOffset_SurvivesTheDefaultPipeline()
    {
        // Passes move and re-parent existing nodes rather than recreate them, so
        // stamped provenance should survive the whole pipeline. Nodes a pass
        // synthesizes without inheriting an offset stay at -1 (filtered here);
        // crucially, no surviving offset is fabricated outside the imported set.
        var function = ImportFixture(nameof(CfgSampleClass.Twice));
        var before = function.Descendants
            .Where(n => n.SourceOffset >= 0)
            .Select(n => n.SourceOffset)
            .ToHashSet();
        Assert.NotEmpty(before);

        IrPasses.Run(function);

        var after = function.Descendants
            .Where(n => n.SourceOffset >= 0)
            .Select(n => n.SourceOffset)
            .ToHashSet();
        Assert.NotEmpty(after);
        Assert.All(after, offset => Assert.Contains(offset, before));
    }

    [Fact]
    public void Clone_CarriesSourceOffsetForward()
    {
        // Clone is MemberwiseClone-based, so provenance rides along for free —
        // the property that lets inlining passes duplicate a node without losing
        // its IL keying.
        var function = ImportFixture(nameof(CfgSampleClass.Twice));
        var stamped = function.Descendants.First(n => n.SourceOffset >= 0);

        var copy = stamped.Clone();

        Assert.Equal(stamped.SourceOffset, copy.SourceOffset);
    }

    [Fact]
    public void InheritSourceOffset_OnlyFillsAnUnstampedNode()
    {
        // The inheritance primitive for synthesized nodes: adopt a source offset
        // only when the node has none, never overwrite an existing one.
        var function = ImportFixture(nameof(CfgSampleClass.Twice));
        var donor = function.Descendants.First(n => n.SourceOffset >= 0);

        // A fresh synthetic node has no provenance; it inherits the donor's.
        var synthetic = new Block();
        Assert.Equal(-1, synthetic.SourceOffset);
        synthetic.InheritSourceOffset(donor);
        Assert.Equal(donor.SourceOffset, synthetic.SourceOffset);

        // A second inherit from a different offset must not overwrite.
        var other = function.Descendants.First(n => n.SourceOffset >= 0 && n.SourceOffset != donor.SourceOffset);
        synthetic.InheritSourceOffset(other);
        Assert.Equal(donor.SourceOffset, synthetic.SourceOffset);
    }
}
