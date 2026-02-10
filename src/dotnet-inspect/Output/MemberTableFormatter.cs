using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;

namespace DotnetInspector.Output;

/// <summary>
/// Strategy for formatting per-kind member tables at different verbosity levels.
/// </summary>
internal abstract class MemberTableFormatter
{
    public abstract string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs);
    public abstract IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs);

    public static MemberTableFormatter Create(Verbosity verbosity) => verbosity switch
    {
        Verbosity.Quiet => new QuietMemberFormatter(),
        Verbosity.Minimal => new MinimalMemberFormatter(),
        _ => new DetailedMemberFormatter()
    };
}

/// <summary>
/// Quiet: group by unique name within each kind, kind-specific columns.
/// </summary>
internal sealed class QuietMemberFormatter : MemberTableFormatter
{
    public override string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs)
    {
        var byName = members.GroupBy(m => m.Name);
        bool hasOverloads = byName.Any(g => g.Count() > 1);

        List<string> headers = ["Name"];

        switch (kind)
        {
            case "constructor":
                if (hasOverloads) headers.Add("Overloads");
                break;
            case "method":
                headers.Add("Return Type");
                if (hasOverloads) headers.Add("Overloads");
                break;
            case "property":
                headers.Add("Return Type");
                headers.Add("Accessors");
                break;
            case "event":
                headers.Add("Type");
                break;
            default: // field
                headers.Add("Return Type");
                break;
        }

        if (showDocs) headers.Add("Description");
        return headers.ToArray();
    }

    public override IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs)
    {
        var byName = members.GroupBy(m => m.Name).OrderBy(g => g.Key).ToList();
        bool hasOverloads = byName.Any(g => g.Count() > 1);

        return byName.Select(g =>
        {
            switch (kind)
            {
                case "constructor":
                case "method":
                {
                    List<string> row = [g.Key];
                    if (kind == "method")
                        row.Add(SignatureParser.ExtractReturnType(g.First().Signature));
                    if (hasOverloads)
                        row.Add(g.Count().ToString());
                    if (showDocs)
                        row.Add(FirstDocSummary(g));
                    return row.ToArray();
                }
                case "property":
                {
                    var m = g.First();
                    List<string> row =
                    [
                        g.Key,
                        SignatureParser.ExtractReturnType(m.Signature),
                        SignatureParser.ExtractAccessors(m.Signature)
                    ];
                    if (showDocs) row.Add(FirstDocSummary(g));
                    return row.ToArray();
                }
                case "event":
                {
                    var m = g.First();
                    List<string> row = [g.Key, m.ReturnType ?? m.Signature ?? ""];
                    if (showDocs) row.Add(FirstDocSummary(g));
                    return row.ToArray();
                }
                default: // field
                {
                    var m = g.First();
                    List<string> row = [g.Key, m.ReturnType ?? ""];
                    if (showDocs) row.Add(FirstDocSummary(g));
                    return row.ToArray();
                }
            }
        });
    }

    private static string FirstDocSummary(IGrouping<string, ApiMember> group) =>
        group.Select(m => m.Documentation.Summary).FirstOrDefault(s => s != null) ?? "";
}

/// <summary>
/// Minimal: one row per member with abbreviated signatures.
/// </summary>
internal sealed class MinimalMemberFormatter : MemberTableFormatter
{
    public override string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs) =>
        showDocs
            ? ["Name", "Signature", "Description"]
            : ["Name", "Signature"];

    public override IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs)
    {
        return members
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Signature)
            .Select(m =>
            {
                var sig = SignatureParser.AbbreviateSignature(m.Signature ?? m.ReturnType ?? "");
                return showDocs
                    ? new[] { m.Name, $"`{sig}`", m.Documentation.Summary ?? "" }
                    : new[] { m.Name, $"`{sig}`" };
            });
    }
}

/// <summary>
/// Normal/Detailed: one row per member with full signatures.
/// </summary>
internal sealed class DetailedMemberFormatter : MemberTableFormatter
{
    public override string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs) =>
        showDocs
            ? ["Name", "Signature", "Description"]
            : ["Name", "Signature"];

    public override IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs)
    {
        return members
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Signature)
            .Select(m =>
            {
                var sig = m.Signature ?? m.ReturnType ?? "";
                return showDocs
                    ? new[] { m.Name, $"`{sig}`", m.Documentation.Summary ?? "" }
                    : new[] { m.Name, $"`{sig}`" };
            });
    }
}
