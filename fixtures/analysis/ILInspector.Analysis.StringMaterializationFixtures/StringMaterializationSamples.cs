using System.Text;
using System.Text.Json;

namespace ILInspector.Analysis.StringMaterializationFixtures;

public static class StringMaterializationSamples
{
    public static string Concatenate(string left, string right)
        => left + right;

    public static string Interpolate(string value, int number)
        => $"{value}:{number}";

    public static string Join(string[] values)
        => string.Join(", ", values);

    public static string Build(string value)
        => new StringBuilder()
            .Append(value)
            .Append('!')
            .ToString();

    public static string Construct(int count)
        => new('x', count);

    public static string Decode(byte[] value)
        => Encoding.UTF8.GetString(value);

    public static byte[] Encode(string value)
        => Encoding.UTF8.GetBytes(value);

    public static void WriteUtf8(
        Utf8JsonWriter writer,
        string value)
        => writer.WriteStringValue(value);
}
