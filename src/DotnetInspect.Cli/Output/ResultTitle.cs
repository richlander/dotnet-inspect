using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Composes a result title from the subject identity and the owner-issued
/// <see cref="ResultProperty"/> values, per
/// <c>docs/design/section-shapes.md#properties</c>. The title is
/// <c>identity (value; value; ...)</c> in issued order, or the bare identity
/// when no property was issued. This is the one renderer for every command's
/// title line; a command that wants a context line issues properties instead
/// of formatting its own.
/// </summary>
internal static class ResultTitle
{
    public static string Compose(
        string identity,
        IReadOnlyList<ResultProperty> properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(properties);
        if (properties.Count == 0)
            return identity;
        return identity
            + " ("
            + string.Join(
                "; ",
                properties.Select(
                    static property => property.Value.ToString()))
            + ")";
    }
}
