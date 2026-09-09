using DotnetInspector.PackageQueries;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Projects <see cref="PackageAssemblyQuery.Plan"/> argument failures into the CLI's own stable
/// diagnostics.
/// </summary>
/// <remarks>
/// <para>
/// The planner is host-neutral and reports plan violations through the ordinary
/// <see cref="ArgumentException"/> contract, so its product-authored sentence arrives with the
/// framework's parameter-name line appended. That line names an internal parameter, and under
/// invariant/trimmed resource lookup it renders as the raw resource key
/// (<c>Arg_ParamName_Name, packageCoordinates</c>). Neither belongs in CLI output, so this host
/// keeps only the product-authored sentence rather than changing the planner's contract for one
/// consumer.
/// </para>
/// <para>
/// Framework-authored argument failures carry no product sentence at all, so the CLI states the
/// missing-argument requirement itself before planning instead of surfacing one.
/// </para>
/// </remarks>
static class PackageAssemblyQueryDiagnostics
{
    /// <summary>
    /// The CLI's own diagnostic for the one <c>--literal</c> argument the planner would otherwise
    /// reject without a product-authored message.
    /// </summary>
    internal const string MissingTargetFramework =
        "--literal requires an explicit --tfm (for example --tfm net10.0).";

    // The exact text ArgumentException.Message appends around a parameter name, measured from the
    // running framework rather than guessed, so a translated "(Parameter 'x')" and an untranslated
    // "Arg_ParamName_Name, x" are both removed exactly.
    static readonly (string Prefix, string Suffix) ParamNameAffixes = MeasureParamNameAffixes();

    /// <summary>
    /// Returns the product-authored sentence a planning <see cref="ArgumentException"/> carries.
    /// </summary>
    internal static string Describe(ArgumentException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string message = exception.Message;
        if (exception.ParamName is not { Length: > 0 } paramName)
            return message;

        string appended = ParamNameAffixes.Prefix + paramName + ParamNameAffixes.Suffix;
        return message.EndsWith(appended, StringComparison.Ordinal)
            ? message[..^appended.Length]
            : message;
    }

    static (string Prefix, string Suffix) MeasureParamNameAffixes()
    {
        const string sentinel = "\uFFFF";
        string template = new ArgumentException(string.Empty, sentinel).Message;
        int index = template.IndexOf(sentinel, StringComparison.Ordinal);
        return index < 0
            ? (string.Empty, string.Empty)
            : (template[..index], template[(index + sentinel.Length)..]);
    }
}
