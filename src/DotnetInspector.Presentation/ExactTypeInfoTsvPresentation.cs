using System.Buffers;
using System.Text;

using ILInspector.Metadata;

namespace DotnetInspector.Presentation;

/// <summary>One exact-Type identity document ready for direct TSV output.</summary>
public sealed record ExactTypeInfoTsvDocument(
    MetadataContainedTypeDefinitionName Type,
    string Kind,
    IReadOnlyList<string> Modifiers,
    int InterfaceCount,
    string Assembly,
    string Source);

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

        if (showHeader)
            WriteBytes(output, "field\tvalue\n"u8);

        WriteBytes(output, "Type\t"u8);
        WriteType(output, document.Type);
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

    private static void WriteType(
        IBufferWriter<byte> output,
        MetadataContainedTypeDefinitionName type)
    {
        if (type.Namespace.Length > 0)
        {
            type.Namespace.WriteTo(output);
            WriteByte(output, (byte)'.');
        }

        for (int i = 0; i < type.Segments.Length; i++)
        {
            if (i > 0)
                WriteByte(output, (byte)'.');
            type.Segments[i].WriteTo(output);
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
