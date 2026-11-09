using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using ILInspector.Metadata;

namespace DotnetInspector.Sections;

/// <summary>
/// Writes the Library symbol document layout
/// (docs/design/library-symbol-document.md#layout): one JSON object whose
/// header scalars, Type entries, Member records, Type collisions, and lookup
/// entries each occupy exactly one line, with one space after <c>:</c> and
/// <c>,</c>, fields in the order the Records section names them, and strings
/// escaped so no value can break a record across lines.
/// </summary>
internal sealed class LibrarySymbolDocumentWriter
{
    readonly StreamWriter _output;
    readonly StringBuilder _line = new();

    internal LibrarySymbolDocumentWriter(Stream destination)
    {
        _output = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 64 * 1024,
            leaveOpen: true)
        {
            NewLine = "\n",
        };
    }

    internal void Raw(string line) => _output.Write(line + "\n");

    internal void Flush() => _output.Flush();

    // ---- Lines ---------------------------------------------------------------

    internal void TypeLine(
        MetadataSymbolType type,
        TypeFamilies families,
        RecordDocumentation documentation,
        bool last)
    {
        _line.Clear();
        String(type.DocumentationId);
        _line.Append(": ");
        TypeObject(type, families, documentation);
        End(last);
    }

    internal void CollisionLine(
        MetadataSymbolType type,
        TypeFamilies families,
        RecordDocumentation documentation,
        bool last)
    {
        _line.Clear();
        _line.Append("{\"documentation_id\": ");
        String(type.DocumentationId);
        _line.Append(", \"record\": ");
        TypeObject(type, families, documentation);
        _line.Append('}');
        End(last);
    }

    internal void MemberLine(
        MetadataSymbolMember member,
        MetadataSymbolType declaringType,
        TypeFamilies families,
        RecordDocumentation documentation,
        bool last)
    {
        _line.Clear();
        _line.Append('{');
        Field("documentation_id", member.DocumentationId, first: true);
        Field("canonical_signature", member.CanonicalSignature);
        Field("display_signature", member.DisplaySignature);
        Field("declaring_type", declaringType.DocumentationId);
        Field("namespace", declaringType.Namespace);
        Field("declaring_type_suffix_family", families.SuffixFamily);
        Field("declaring_type_two_word_suffix_family", families.TwoWordSuffixFamily);
        Field("declaring_type_name_words", families.NameWords);
        Field("kind", member.Kind);
        Field("name", member.Name);
        Field("accessibility", member.Accessibility);
        Field("is_static", member.IsStatic);
        Field("is_abstract", member.IsAbstract);
        Field("is_virtual", member.IsVirtual);
        Field("is_override", member.IsOverride);
        Field("is_sealed", member.IsSealed);
        Field("is_extension", member.IsExtension);
        Field("generic_parameters", member.GenericParameters);
        Field("return_type", member.ReturnType);
        Field("return_type_mentions", member.ReturnTypeMentions);
        Field("produced_types", member.ProducedTypes);
        Name("parameters");
        _line.Append('[');
        for (int index = 0; index < member.Parameters.Length; index++)
        {
            if (index > 0)
                _line.Append(", ");
            MetadataSymbolParameter parameter = member.Parameters[index];
            _line.Append('{');
            Field("name", parameter.Name, first: true);
            Field("type", parameter.Type);
            Field("type_mentions", parameter.TypeMentions);
            Field("modifier", parameter.Modifier);
            Field("has_default_value", parameter.HasDefaultValue);
            _line.Append('}');
        }
        _line.Append(']');
        Field("overrides", member.Overrides);
        Field("attribute_types", member.AttributeTypes);
        Status("documentation", documentation.Status, "summary", documentation.Summary);
        SourceStatus(families.SourceStatus, typeLevel: false);
        Name("findings");
        _line.Append("[]");
        Field("metadata_token", member.MetadataToken);
        _line.Append('}');
        End(last);
    }

    internal void LookupLine(string key, IReadOnlyCollection<string> values, bool last)
    {
        _line.Clear();
        String(key);
        _line.Append(": ");
        Array(values);
        End(last);
    }

    void TypeObject(
        MetadataSymbolType type,
        TypeFamilies families,
        RecordDocumentation documentation)
    {
        _line.Append('{');
        Field("name", type.Name, first: true);
        Field("metadata_name", type.MetadataName);
        Field("namespace", type.Namespace);
        Field("declaring_type", type.DeclaringType);
        Field(
            "declaration_kind",
            type.DeclarationKind is MetadataSymbolDeclarationKind.Forwarder
                ? "forwarder"
                : "definition");
        Field("forwarded_to_assembly", type.ForwardedToAssembly);
        Field("definition_kind", type.DefinitionKind);
        Field("accessibility", type.Accessibility);
        Field("visible_outside_library", type.VisibleOutsideLibrary);
        Field("is_static", type.IsStatic);
        Field("is_abstract", type.IsAbstract);
        Field("is_sealed", type.IsSealed);
        Field("constructor_accessibility", type.ConstructorAccessibility);
        Field("generic_parameters", type.GenericParameters);
        Field("base_type", type.BaseType);
        Field("interfaces", type.Interfaces);
        Field("required_overrides", type.RequiredOverrides);
        Field("inaccessible_required_overrides", type.InaccessibleRequiredOverrides);
        Field("required_overrides_complete", type.RequiredOverridesComplete);
        Field("attribute_types", type.AttributeTypes);
        Field("suffix_family", families.SuffixFamily);
        Field("two_word_suffix_family", families.TwoWordSuffixFamily);
        Field("name_words", families.NameWords);
        Status("documentation", documentation.Status, "summary", documentation.Summary);
        SourceStatus(families.SourceStatus, typeLevel: true);
        Name("findings");
        _line.Append("[]");
        Field("metadata_token", type.MetadataToken);
        _line.Append('}');
    }

    void Status(string name, string status, string valueName, string value)
    {
        Name(name);
        _line.Append('{');
        Field("status", status, first: true);
        Field(valueName, value);
        _line.Append('}');
    }

    void SourceStatus(string status, bool typeLevel)
    {
        Name("source");
        _line.Append('{');
        Field("status", status, first: true);
        if (typeLevel)
        {
            Name("urls");
            _line.Append("[]");
        }
        else
        {
            Field("url", "");
            Field("first_statement_line", 0);
        }
        _line.Append('}');
    }

    void End(bool last)
    {
        if (!last)
            _line.Append(',');
        _line.Append('\n');
        _output.Write(_line);
    }

    // ---- Values --------------------------------------------------------------

    void Name(string name, bool first = false)
    {
        if (!first)
            _line.Append(", ");
        String(name);
        _line.Append(": ");
    }

    void Field(string name, string value, bool first = false)
    {
        Name(name, first);
        String(value);
    }

    void Field(string name, bool value, bool first = false)
    {
        Name(name, first);
        _line.Append(value ? "true" : "false");
    }

    void Field(string name, int value, bool first = false)
    {
        Name(name, first);
        _line.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    void Field(string name, IReadOnlyCollection<string> values, bool first = false)
    {
        Name(name, first);
        Array(values);
    }

    void Array(IReadOnlyCollection<string> values)
    {
        _line.Append('[');
        bool first = true;
        foreach (string value in values)
        {
            if (!first)
                _line.Append(", ");
            first = false;
            String(value);
        }
        _line.Append(']');
    }

    void String(string value)
    {
        _line.Append('"');
        AppendEscaped(_line, value);
        _line.Append('"');
    }

    internal static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        AppendEscaped(builder, value);
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Escapes only what JSON or the one-line layout requires: quote,
    /// backslash, control characters, the Unicode line and paragraph
    /// separators, and unpaired surrogates. Every other character is written
    /// as itself, so identities such as <c>JsonConverter`1</c> stay greppable.
    /// </summary>
    static void AppendEscaped(StringBuilder builder, string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            switch (current)
            {
                case '"':
                    builder.Append("\\\"");
                    continue;
                case '\\':
                    builder.Append("\\\\");
                    continue;
                case '\n':
                    builder.Append("\\n");
                    continue;
                case '\r':
                    builder.Append("\\r");
                    continue;
                case '\t':
                    builder.Append("\\t");
                    continue;
                case '\b':
                    builder.Append("\\b");
                    continue;
                case '\f':
                    builder.Append("\\f");
                    continue;
            }

            // U+2028 and U+2029 end a line for some readers.
            if (current < ' ' || current is (char)0x2028 or (char)0x2029)
            {
                Unicode(builder, current);
            }
            else if (char.IsHighSurrogate(current)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                builder.Append(current).Append(value[++index]);
            }
            else if (char.IsSurrogate(current))
            {
                Unicode(builder, current);
            }
            else
            {
                builder.Append(current);
            }
        }
    }

    static void Unicode(StringBuilder builder, char value) =>
        builder.Append("\\u").Append(((int)value).ToString("x4", CultureInfo.InvariantCulture));
}

/// <summary>
/// The name-family and source facts a Type record carries beside its
/// declaration, and that its Members repeat; each is explicitly empty when
/// its section is not produced.
/// </summary>
internal sealed record TypeFamilies(
    string SuffixFamily,
    string TwoWordSuffixFamily,
    ImmutableArray<string> NameWords,
    string SourceStatus)
{
    internal static TypeFamilies NotRequested { get; } =
        new("", "", [], "not_requested");

    internal static TypeFamilies Forwarder { get; } =
        new("", "", [], "forwarder");
}

/// <summary>
/// A record's own documentation: its status and summary, the summary
/// <c>""</c> unless the status is <c>complete</c> (#source-and-documentation).
/// </summary>
internal readonly record struct RecordDocumentation(string Status, string Summary)
{
    internal static RecordDocumentation NotRequested { get; } = new("not_requested", "");

    internal static RecordDocumentation Forwarder { get; } = new("forwarder", "");
}
