using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;

namespace ILInspector.Decompiler.Tests;

public sealed class SlotResidualBoundaryTests
{
    const string FixtureType = "ILInspector.Decompiler.Fixtures.PointerElementUpdateSamples";

    [Fact]
    public void RoslynPointerSpillsAreRemovedBeforeMaterialization()
    {
        using var source = MetadataSource.Open(typeof(Compilation).Assembly.Location);
        var function = IrImporter.Import(source, "System.IO.Hashing.XxHashShared", "Accumulate512Inlined");
        Assert.NotNull(function);

        var measurement = SlotResidualCensus.Measure(function, method => IrImporter.Import(source, method));

        Assert.Equal(2, measurement.AfterF2.StoreCount);
        Assert.Equal(4, measurement.AfterF2.LoadCount);
        Assert.Equal(2, measurement.AfterF2.Slots.Count);
        Assert.Empty(measurement.BeforeMaterialization.Slots);
        Assert.Empty(measurement.AfterMaterialization.Slots);
        Assert.Empty(measurement.Decisions);
        Assert.Equal(2, function.Descendants.OfType<PointerElementCompoundAssignment>().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompilerProducedPointerUpdatesBelongToTheInterveningRaise(bool updated)
    {
        using var source = MetadataSource.Open(FixturePath(updated));
        var function = IrImporter.Import(source, FixtureType, "Accumulate");
        Assert.NotNull(function);

        var measurement = SlotResidualCensus.Measure(function, method => IrImporter.Import(source, method));

        Assert.Equal(2, measurement.AfterF2.Slots.Count);
        Assert.Empty(measurement.BeforeMaterialization.Slots);
        Assert.Empty(measurement.AfterMaterialization.Slots);
        Assert.Empty(measurement.Decisions);
        Assert.Equal(2, function.Descendants.OfType<PointerElementCompoundAssignment>().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclinedPointerUpdatesStillBelongToMaterialization(bool updated)
    {
        using var source = MetadataSource.Open(FixturePath(updated));
        var function = IrImporter.Import(source, FixtureType, "LongIndex");
        Assert.NotNull(function);

        var measurement = SlotResidualCensus.Measure(function, method => IrImporter.Import(source, method));

        Assert.Single(measurement.AfterF2.Slots);
        Assert.Equal(measurement.AfterF2.Slots.Keys, measurement.BeforeMaterialization.Slots.Keys);
        Assert.True(Assert.Single(measurement.Decisions).WillMaterialize);
        Assert.Empty(measurement.AfterMaterialization.Slots);
        Assert.Empty(function.Descendants.OfType<PointerElementCompoundAssignment>());
    }

    static string FixturePath(bool updated)
        => (updated ? FixtureCatalog.DecompilerUnsafeNew : FixtureCatalog.DecompilerUnsafeLegacy).AssemblyPath();
}
