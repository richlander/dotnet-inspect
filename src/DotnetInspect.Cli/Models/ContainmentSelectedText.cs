using System.Globalization;
using InertText;

namespace DotnetInspect.Cli.Models;

public sealed class ContainmentSelectedText
{
    private readonly string _text;

    private ContainmentSelectedText(
        string text,
        ContainmentDiagnostic? diagnostic)
    {
        _text = text;
        Diagnostic = diagnostic;
    }

    internal static ContainmentSelectedText FromClassification(
        InertString classified,
        string safeText,
        InertString containmentText,
        ContainmentDiagnostic? diagnostic = null)
    {
        if (classified.RequiredContainment && diagnostic is null)
        {
            throw new InvalidOperationException(
                "Containment-selected text requires a diagnostic.");
        }

        return new(
            classified.RequiredContainment
                ? containmentText.ToString()
                : safeText,
            classified.RequiredContainment ? diagnostic : null);
    }

    internal ContainmentDiagnostic? Diagnostic { get; }

    public override string ToString() => _text;
}

internal sealed record ContainmentDiagnostic(
    string Source,
    int TotalRangeCount,
    IReadOnlyList<ContainmentConcernRange> Ranges);

internal sealed record ContainmentConcernRange(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    int Scalar,
    UnicodeCategory Category,
    int ScalarCount);
