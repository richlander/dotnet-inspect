using System.Buffers;
using System.Text;

using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Presentation;

/// <summary>One exact-Type identity document ready for direct TSV output.</summary>
/// <remarks>
/// <see cref="Type"/> is the display spelling, whose segments carry no arity suffix that
/// <see cref="IntroducedTypeParameterCounts"/> spells as parameter lists.
/// </remarks>
public sealed record ExactTypeInfoTsvDocument(
    MetadataContainedTypeDefinitionName Type,
    string Kind,
    IReadOnlyList<string> Modifiers,
    int InterfaceCount,
    string Assembly,
    string Source)
{
    /// <summary>The generic parameters in declaration order.</summary>
    public IReadOnlyList<ExactTypeInfoTsvTypeParameter> TypeParameters { get; init; } = [];

    /// <summary>
    /// The number of <see cref="TypeParameters"/> each Type segment introduces, or empty for
    /// a non-generic Type.
    /// </summary>
    public IReadOnlyList<int> IntroducedTypeParameterCounts { get; init; } = [];
}

/// <summary>One generic parameter of an exact-Type identity document.</summary>
/// <param name="Variance">The C# variance keyword, if any.</param>
/// <param name="Name">The contained parameter name.</param>
/// <param name="Constraints">The C# constraint keywords in declaration order.</param>
public sealed record ExactTypeInfoTsvTypeParameter(
    string? Variance,
    InertUtf8String Name,
    IReadOnlyList<string> Constraints);

/// <summary>Writes the bounded exact-Type identity document as UTF-8 TSV.</summary>
public static class ExactTypeInfoTsvPresentation
{
    /// <summary>Writes one complete document without creating a UTF-16 document.</summary>
    public static void Write(
        IBufferWriter<byte> output,
        ExactTypeInfoTsvDocument document,
        bool showHeader)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(document.Type);
        ArgumentNullException.ThrowIfNull(document.Kind);
        ArgumentNullException.ThrowIfNull(document.Modifiers);
        ArgumentNullException.ThrowIfNull(document.Assembly);
        ArgumentNullException.ThrowIfNull(document.Source);
        ArgumentOutOfRangeException.ThrowIfNegative(
            document.InterfaceCount);
        ArgumentNullException.ThrowIfNull(document.TypeParameters);
        ArgumentNullException.ThrowIfNull(
            document.IntroducedTypeParameterCounts);
        ValidateTypeParameters(document);

        if (showHeader)
            WriteBytes(output, "field\tvalue\n"u8);

        WriteBytes(output, "Type\t"u8);
        WriteType(output, document);
        WriteByte(output, (byte)'\n');

        WriteRow(output, "Kind"u8, document.Kind);
        if (document.Modifiers.Count > 0)
        {
            WriteBytes(output, "Modifiers\t"u8);
            for (int i = 0; i < document.Modifiers.Count; i++)
            {
                if (i > 0)
                    WriteBytes(output, ", "u8);
                WriteString(output, document.Modifiers[i]);
            }
            WriteByte(output, (byte)'\n');
        }
        if (document.TypeParameters.Count > 0)
        {
            WriteBytes(output, "Type Parameters\t"u8);
            WriteTypeParameters(output, document.TypeParameters);
            WriteByte(output, (byte)'\n');
        }
        if (document.InterfaceCount > 0)
        {
            WriteBytes(output, "Interfaces\t"u8);
            WriteString(
                output,
                document.InterfaceCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            WriteByte(output, (byte)'\n');
        }
        WriteRow(output, "Library"u8, document.Assembly);
        WriteRow(output, "Source"u8, document.Source);
    }

    private static void ValidateTypeParameters(
        ExactTypeInfoTsvDocument document)
    {
        IReadOnlyList<int> counts =
            document.IntroducedTypeParameterCounts;
        if (counts.Count == 0)
        {
            if (document.TypeParameters.Count > 0)
            {
                throw new ArgumentException(
                    "Generic parameters require their introduced counts.",
                    nameof(document));
            }
            return;
        }

        if (counts.Count != document.Type.Segments.Length)
        {
            throw new ArgumentException(
                "Each Type segment requires one introduced count.",
                nameof(document));
        }
        long total = 0;
        foreach (int count in counts)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            total += count;
        }
        if (total != document.TypeParameters.Count)
        {
            throw new ArgumentException(
                "The introduced counts must cover every generic parameter.",
                nameof(document));
        }
        foreach (ExactTypeInfoTsvTypeParameter parameter
            in document.TypeParameters)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            ArgumentNullException.ThrowIfNull(parameter.Constraints);
        }
    }

    private static void WriteType(
        IBufferWriter<byte> output,
        ExactTypeInfoTsvDocument document)
    {
        MetadataContainedTypeDefinitionName type = document.Type;
        if (type.Namespace.Length > 0)
        {
            type.Namespace.WriteTo(output);
            WriteByte(output, (byte)'.');
        }

        IReadOnlyList<int> counts =
            document.IntroducedTypeParameterCounts;
        int parameter = 0;
        for (int i = 0; i < type.Segments.Length; i++)
        {
            if (i > 0)
                WriteByte(output, (byte)'.');
            type.Segments[i].WriteTo(output);

            int count = counts.Count == 0 ? 0 : counts[i];
            if (count == 0)
                continue;

            WriteByte(output, (byte)'<');
            for (int j = 0; j < count; j++)
            {
                if (j > 0)
                    WriteBytes(output, ", "u8);
                document.TypeParameters[parameter++].Name.WriteTo(output);
            }
            WriteByte(output, (byte)'>');
        }
    }

    private static void WriteTypeParameters(
        IBufferWriter<byte> output,
        IReadOnlyList<ExactTypeInfoTsvTypeParameter> parameters)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
                WriteBytes(output, ", "u8);

            ExactTypeInfoTsvTypeParameter parameter = parameters[i];
            if (parameter.Variance is { } variance)
            {
                WriteString(output, variance);
                WriteByte(output, (byte)' ');
            }
            parameter.Name.WriteTo(output);
            for (int j = 0; j < parameter.Constraints.Count; j++)
            {
                WriteBytes(output, j == 0 ? " : "u8 : ", "u8);
                WriteString(output, parameter.Constraints[j]);
            }
        }
    }

    private static void WriteRow(
        IBufferWriter<byte> output,
        ReadOnlySpan<byte> label,
        string value)
    {
        WriteBytes(output, label);
        WriteByte(output, (byte)'\t');
        WriteString(output, value);
        WriteByte(output, (byte)'\n');
    }

    private static void WriteString(
        IBufferWriter<byte> output,
        string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> destination = output.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value, destination);
        output.Advance(written);
    }

    private static void WriteByte(
        IBufferWriter<byte> output,
        byte value)
    {
        Span<byte> destination = output.GetSpan(1);
        destination[0] = value;
        output.Advance(1);
    }

    private static void WriteBytes(
        IBufferWriter<byte> output,
        ReadOnlySpan<byte> value)
    {
        value.CopyTo(output.GetSpan(value.Length));
        output.Advance(value.Length);
    }
}
