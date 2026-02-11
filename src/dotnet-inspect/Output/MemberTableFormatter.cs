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
    public abstract string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs, bool showSelect);
    public abstract IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs, bool showSelect);

    public static MemberTableFormatter Create(Verbosity verbosity) => verbosity switch
    {
        Verbosity.Quiet => new QuietMemberFormatter(),
        Verbosity.Minimal => new MinimalMemberFormatter(),
        _ => new DetailedMemberFormatter()
    };

    /// <summary>
    /// Builds the -m/--params string needed to target a specific member.
    /// When the member name is unique (no overloads), returns just "-m Name".
    /// When overloads exist, appends "--params type1,type2" using simple type names.
    /// </summary>
    protected static string BuildSelectString(ApiMember member, bool hasOverloads)
    {
        var name = member.Name;
        if (!hasOverloads || member.Kind is "property" or "field" or "event")
            return $"-m {name}";

        var paramTypes = SignatureParser.ExtractParamTypes(member.Signature);
        if (paramTypes.Count == 0)
            return $"-m {name} --params \"\"";

        var simpleTypes = paramTypes.Select(SimplifyTypeName).ToList();
        return $"-m {name} --params {string.Join(",", simpleTypes)}";
    }

    /// <summary>
    /// Extracts the simple (unqualified) name from a fully-qualified type name.
    /// "System.Text.Json.JsonDocument" → "JsonDocument". Preserves generic suffixes.
    /// </summary>
    private static string SimplifyTypeName(string fullTypeName)
    {
        int depth = 0;
        int lastDot = -1;
        for (int i = 0; i < fullTypeName.Length; i++)
        {
            if (fullTypeName[i] == '<') depth++;
            else if (fullTypeName[i] == '>') depth--;
            else if (fullTypeName[i] == '.' && depth == 0) lastDot = i;
        }

        return lastDot > 0 ? fullTypeName[(lastDot + 1)..] : fullTypeName;
    }
}

/// <summary>
/// Quiet: group by unique name within each kind, kind-specific columns.
/// showSelect is ignored (rows are grouped, not per-overload).
/// </summary>
internal sealed class QuietMemberFormatter : MemberTableFormatter
{
    public override string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs, bool showSelect)
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

    public override IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs, bool showSelect)
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
    public override string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs, bool showSelect)
    {
        List<string> headers = ["Name", "Signature"];
        if (showSelect) headers.Add("Select");
        if (showDocs) headers.Add("Description");
        return headers.ToArray();
    }

    public override IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs, bool showSelect)
    {
        var overloadCounts = members.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.Count());

        return members
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Signature)
            .Select(m =>
            {
                var sig = SignatureParser.AbbreviateSignature(m.Signature ?? m.ReturnType ?? "");
                List<string> row = [m.Name, $"`{sig}`"];
                if (showSelect) row.Add($"`{BuildSelectString(m, overloadCounts[m.Name] > 1)}`");
                if (showDocs) row.Add(m.Documentation.Summary ?? "");
                return row.ToArray();
            });
    }
}

/// <summary>
/// Normal/Detailed: one row per member with full signatures.
/// </summary>
internal sealed class DetailedMemberFormatter : MemberTableFormatter
{
    public override string[] GetHeaders(string kind, List<ApiMember> members, bool showDocs, bool showSelect)
    {
        List<string> headers = ["Name", "Signature"];
        if (showSelect) headers.Add("Select");
        if (showDocs) headers.Add("Description");
        return headers.ToArray();
    }

    public override IEnumerable<string[]> FormatRows(string kind, List<ApiMember> members, bool showDocs, bool showSelect)
    {
        var overloadCounts = members.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.Count());

        return members
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Signature)
            .Select(m =>
            {
                var sig = m.Signature ?? m.ReturnType ?? "";
                List<string> row = [m.Name, $"`{sig}`"];
                if (showSelect) row.Add($"`{BuildSelectString(m, overloadCounts[m.Name] > 1)}`");
                if (showDocs) row.Add(m.Documentation.Summary ?? "");
                return row.ToArray();
            });
    }
}
