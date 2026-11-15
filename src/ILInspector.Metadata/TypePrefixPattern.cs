using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// The literal Type-prefix discovery grammar, independent of exact selection.
/// Matches the dotted full name or dotted nested name, with a nonempty suffix.
/// </summary>
public sealed class TypePrefixPattern
{
    private readonly string _prefix;

    public TypePrefixPattern(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        _prefix = FqnParser.NormalizeTypeName(query.Trim());
    }

    public bool Matches(MetadataTypeDefinitionName candidate) =>
        Matches(candidate.ToMetadataFullName(), string.Join('.', candidate.Segments));

    public bool Matches(string fullName, string name) =>
        IsPrefix(fullName) || IsPrefix(name);

    private bool IsPrefix(string candidate) =>
        candidate.Length > _prefix.Length
        && candidate.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
}
