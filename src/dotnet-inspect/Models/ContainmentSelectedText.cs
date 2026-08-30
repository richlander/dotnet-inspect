using InertText;

namespace DotnetInspector.Models;

public sealed class ContainmentSelectedText
{
    private readonly string _text;

    private ContainmentSelectedText(string text)
    {
        _text = text;
    }

    internal static ContainmentSelectedText FromClassification(
        InertString classified,
        string safeText,
        InertString containmentText)
        => new(classified.RequiredContainment
            ? containmentText.ToString()
            : safeText);

    public override string ToString() => _text;
}
