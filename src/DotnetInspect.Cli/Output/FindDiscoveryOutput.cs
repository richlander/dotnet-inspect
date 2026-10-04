using DotnetInspect.Cli.Models;
using DotnetInspector.Presentation;
using InertText;

namespace DotnetInspect.Cli.Output;

internal static class FindDiscoveryOutput
{
    internal static FindDiscoveryRow Project(TypeFindResult row) => new(
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

    internal static FindDiscoveryRow Project(MemberFindResult row) => new(
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
