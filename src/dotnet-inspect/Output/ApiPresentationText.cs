using System.Buffers;
using System.Text;
using InertText;

namespace DotnetInspector.Output;

internal static class ApiPresentationText
{
    public static CSharpPresentationText CSharpField(string value) =>
        CSharpPresentationText.Create(value);

    public static InertString EncodedField(string value) =>
        InertString.FromEncoded(TextPolicy.Field, value);
}

/// <summary>
/// Carries exact, contained rendered-C# spelling alongside canonical inert evidence.
/// </summary>
/// <remarks>
/// <c>CSharpField_MixedCSharpAndVisualEscapes_PreservesSpellingWithInertEvidence</c>
/// gates the distinction between C# escape syntax and the canonical evidence codec.
/// <c>CSharpCodeText_PreservesContainmentEvidence</c> gates concern provenance
/// when trusted presentation markup is added.
/// </remarks>
public readonly struct CSharpPresentationText
{
    private readonly string? _text;

    private CSharpPresentationText(string text, InertString evidence)
    {
        _text = text;
        Evidence = evidence;
    }

    /// <summary>Gets the exact contained C# spelling for presentation.</summary>
    public string Text => _text ?? string.Empty;

    /// <summary>Gets canonical evidence for the untreated rendered value.</summary>
    public InertString Evidence { get; }

    internal CSharpPresentationText WithPresentationText(string text)
        => new(text, Evidence);

    internal static CSharpPresentationText Create(string value)
    {
        var builder = new StringBuilder(value.Length);
        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out int consumed);
            if (status != OperationStatus.Done)
                consumed = 1;

            ReadOnlySpan<char> scalar = remaining[..consumed];
            if (InertString.IsPermitted(TextPolicy.Field, scalar))
                builder.Append(scalar);
            else
                builder.Append(new InertString(TextPolicy.Field, scalar));

            remaining = remaining[consumed..];
        }

        return new CSharpPresentationText(
            builder.ToString(),
            new InertString(TextPolicy.Field, value));
    }

    public override string ToString() => Text;
}
