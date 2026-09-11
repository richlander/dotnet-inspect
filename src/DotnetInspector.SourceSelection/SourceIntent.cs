namespace DotnetInspector.SourceSelection;

public sealed class SourceIntent
{
    private SourceIntent(SourceSelector[] selectors) =>
        Selectors = Array.AsReadOnly(selectors);

    public static SourceIntent Empty { get; } = new([]);

    public IReadOnlyList<SourceSelector> Selectors { get; }

    public static SourceIntent Create(IEnumerable<SourceSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        SourceSelector[] snapshot = selectors.ToArray();
        foreach (SourceSelector selector in snapshot)
            ArgumentNullException.ThrowIfNull(selector, nameof(selectors));

        return snapshot.Length == 0 ? Empty : new(snapshot);
    }

    public SourceIntent Append(SourceSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new([.. Selectors, selector]);
    }
}
