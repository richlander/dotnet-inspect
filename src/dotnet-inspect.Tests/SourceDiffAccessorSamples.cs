namespace DotnetInspector.Tests;

public interface ISourceDiffAccessorNames
{
    int Value { get; set; }
    event Action Changed;
}

public sealed class SourceDiffAccessorNamesSample : ISourceDiffAccessorNames
{
    int _value;
    Action? _changed;

    int ISourceDiffAccessorNames.Value
    {
        get => _value;
        set => _value = value;
    }

    event Action ISourceDiffAccessorNames.Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }
}
