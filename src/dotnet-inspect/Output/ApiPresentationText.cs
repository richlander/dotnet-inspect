using System.Buffers;
using System.Text;
using InertText;

namespace DotnetInspector.Output;

internal static class ApiPresentationText
{
    public static InertString CSharpField(string value)
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

        return InertString.FromEncoded(
            TextPolicy.Field,
            builder.ToString());
    }

    public static InertString EncodedField(string value) =>
        InertString.FromEncoded(TextPolicy.Field, value);
}
