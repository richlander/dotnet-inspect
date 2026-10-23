using System.Text;

using InertText;

namespace ILInspector.Metadata;

/// <summary>
/// The diagnostic renderer of a key's parts
/// (<c>docs/design/symbol-key.md#diagnostics</c>): markers and integers as
/// owner-authored ASCII, each Name contained under <see cref="TextPolicy.Field"/>
/// through the reader its view issues, each Blob as hex. The rendering is for
/// test failures and logs; it is never compared, parsed, or presented as
/// identity.
/// </summary>
public static class SymbolKeyText
{
    public static string Describe<TDomain>(SymbolKey<TDomain> key)
        where TDomain : unmanaged, Enum
    {
        if (key.IsDefault)
            return "<default>";
        var text = new StringBuilder();
        Render(text, key.Parts);
        return text.ToString();
    }

    public static string Describe<TDomain>(StagedSymbolKey<TDomain> key)
        where TDomain : unmanaged, Enum
        => key.IsDefault ? "<default>" : Describe(key.Detach());

    static void Render<TDomain>(StringBuilder text, SymbolKeyPartReader<TDomain> parts)
        where TDomain : unmanaged, Enum
    {
        bool first = true;
        while (parts.Read())
        {
            if (!first)
                text.Append(' ');
            first = false;
            switch (parts.Kind)
            {
                case SymbolKeyPartKind.Marker:
                    text.Append(parts.Marker);
                    break;
                case SymbolKeyPartKind.Int:
                    text.Append(parts.Int);
                    break;
                case SymbolKeyPartKind.Name:
                    text.Append('"')
                        .Append(new InertUtf8String(TextPolicy.Field, parts.Name.Read()).ToInertString().ToString())
                        .Append('"');
                    break;
                case SymbolKeyPartKind.Key:
                    text.Append('(');
                    Render(text, parts.Key.Parts);
                    text.Append(')');
                    break;
                case SymbolKeyPartKind.Blob:
                    text.Append("0x");
                    OpaqueByteRange blob = parts.Blob;
                    for (int i = 0; i < blob.Length; i++)
                        text.Append(blob.ByteAt(i).ToString("X2"));
                    break;
            }
        }
    }
}
