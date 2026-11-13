using DotnetInspector.Sections;
using InertText;

namespace DotnetInspector.Presentation;

public static class FindDiscoveryPresentation
{
    public static FindDiscoveryRow Project(ApiFindRow row) => row switch
    {
        TypeFindResult type => Project(type),
        MemberFindResult member => Project(member),
        _ => throw new ArgumentException("Unknown Find row.", nameof(row)),
    };

    public static FindDiscoveryRow Project(TypeFindResult row) => new(
        Field(row.FullName.Length > 0
            ? row.FullName
            : string.IsNullOrEmpty(row.Namespace)
                ? row.Type
                : $"{row.Namespace}.{row.Type}"),
        Field("type"),
        Source(row.Source, row.SourceVersion),
        Field(row.Library),
        Field(row.Pattern),
        Field(row.Kind),
        Field(""),
        Field(row.Match.ToString().ToLowerInvariant()),
        Field(row.Ecosystem ?? ""));

    public static FindDiscoveryRow Project(MemberFindResult row) => new(
        Field($"{row.DeclaringType}.{row.Member}"),
        Field("member"),
        Source(row.Source, row.SourceVersion),
        Field(row.Library),
        Field(row.Pattern),
        Field(row.Kind),
        Field(row.Signature ?? ""),
        Field(row.Match.ToString().ToLowerInvariant()),
        Field(row.Ecosystem ?? ""));

    private static InertString Field(string value) => new(TextPolicy.Field, value);

    private static InertString Source(string source, string? version) =>
        string.IsNullOrEmpty(version)
            ? Field(source)
            : InertString.Format(TextPolicy.Field, $"{source}@{version}");
}
