using InertText;

namespace DotnetInspect.Cli.Views;

internal static class DependencyEvidenceViewText
{
    public static InertString Field(string? value) =>
        new(TextPolicy.Field, value ?? "");

    public static InertString? Optional(string? value) =>
        value is null ? null : Field(value);
}
