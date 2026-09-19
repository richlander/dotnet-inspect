using System.Collections.Immutable;
using CSharpText;
using DotnetInspector.SourceHouse;

namespace DotnetInspector.Sections;

public enum MemberSourcePartKind
{
    Member,
    XmlDocs,
    Attributes,
    Signature,
    Body,
}

public sealed record MemberSourcePart(
    MemberSourcePartKind Kind,
    ImmutableArray<MemberTextPart> Spans);

public static class MemberSourcePartsProjection
{
    public static IReadOnlyList<MemberSourcePart> CreateCatalog(MemberTextParts parts)
    {
        List<MemberSourcePart> result = [new(MemberSourcePartKind.Member, [parts.Member])];
        if (!parts.XmlDocumentation.IsEmpty)
            result.Add(new(MemberSourcePartKind.XmlDocs, parts.XmlDocumentation));
        if (!parts.Attributes.IsEmpty)
            result.Add(new(MemberSourcePartKind.Attributes, parts.Attributes));
        result.Add(new(MemberSourcePartKind.Signature, [parts.Signature]));
        if (parts.Body is { } body)
            result.Add(new(MemberSourcePartKind.Body, [body]));
        return result;
    }

    public static string GetText(SourceHouseAuthoredMemberDocument document, MemberSourcePart part) =>
        string.Join("\n", part.Spans.Select(span => document.Text.Substring(span.Start, span.Length)));

    public static string Name(MemberSourcePartKind kind) => kind switch
    {
        MemberSourcePartKind.Member => "member",
        MemberSourcePartKind.XmlDocs => "xml-docs",
        MemberSourcePartKind.Attributes => "attributes",
        MemberSourcePartKind.Signature => "signature",
        MemberSourcePartKind.Body => "body",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string name, out MemberSourcePartKind kind)
    {
        foreach (MemberSourcePartKind candidate in Enum.GetValues<MemberSourcePartKind>())
        {
            if (Name(candidate).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }
        kind = default;
        return false;
    }
}
