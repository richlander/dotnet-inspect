namespace ILInspector.Decompiler.Tests;

/// <summary>Reference-typed indexer for indexer <c>??=</c> fixtures.</summary>
public sealed class StringIndexer
{
    private readonly string?[] _slots = new string?[8];

    public string? this[int index]
    {
        get => _slots[index];
        set => _slots[index] = value;
    }
}

/// <summary>Numeric indexer for indexer compound-assignment fixtures.</summary>
public sealed class CounterIndexer
{
    private readonly int[] _counts = new int[8];

    public int this[int index]
    {
        get => _counts[index];
        set => _counts[index] = value;
    }
}
