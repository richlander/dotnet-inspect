namespace ILInspector.Decompiler.Tests;

public sealed class IteratorReceiverNameSamples
{
    readonly IEnumerable<DateTime> _dates;
    readonly int _offset;

    public IteratorReceiverNameSamples(IEnumerable<DateTime> dates, int offset)
    {
        _dates = dates;
        _offset = offset;
    }

    public IEnumerable<int> YieldYears()
    {
        foreach (var date in _dates)
            yield return date.Year;
    }

    public IEnumerable<int> YieldAdjustedYears(IEnumerable<DateTime> dates)
    {
        foreach (var date in dates)
            yield return date.Year + _offset;
    }

    public IEnumerable<int> YieldCombinedYears(IteratorReceiverNameSamples other)
    {
        foreach (var date in _dates)
            yield return date.Year + _offset + other._offset;
    }
}

public readonly struct IteratorValueReceiverNameSamples(IEnumerable<DateTime> dates)
{
    readonly IEnumerable<DateTime> _dates = dates;

    public IEnumerable<int> YieldYears()
    {
        foreach (var date in _dates)
            yield return date.Year;
    }
}
