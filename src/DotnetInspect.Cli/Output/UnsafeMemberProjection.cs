using System.Collections.Immutable;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Views;
using ILInspector.Analysis;
using Markout;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// The <c>Unsafe Code</c> section's projection, as
/// <c>docs/design/unsafe-code-section.md</c> defines it: one row per Type
/// that Finding Unsafe Any answers at Library scope, one row per census
/// finding at Type scope, one row per evidence item at Member scope, the
/// stderr disclosure of an incomplete answer, and the Library JSON shape.
/// </summary>
internal static class UnsafeMemberProjection
{
    /// <summary>The findings an available census observed, complete or not.</summary>
    public static ImmutableArray<UnsafeMemberFinding> Findings(
        UnsafeMemberFindingInspection? inspection) =>
        inspection switch
        {
            UnsafeMemberFindingInspection.Complete complete =>
                Payloads(complete.Inspection),
            UnsafeMemberFindingInspection.Incomplete incomplete =>
                Payloads(incomplete.Inspection),
            _ => [],
        };

    public static bool IsIncomplete(UnsafeMemberFindingInspection? inspection) =>
        inspection is UnsafeMemberFindingInspection.Incomplete;

    /// <summary>
    /// The Library rows: one per Type Finding Unsafe Any answers, ordered by
    /// the rendered Type.
    /// </summary>
    public static List<UnsafeCodeTypeRow> TypeRows(UnsafeCodeTypes? types) =>
        [.. LibraryTypes(types).Select(type => new UnsafeCodeTypeRow(type.Type, type.State))];

    /// <summary>The Library State of a Type with a finding.</summary>
    public const string UnsafeState = "unsafe";

    /// <summary>
    /// The Type rows, ordered by the rendered member and then by metadata
    /// token so overloads keep definition order.
    /// </summary>
    public static List<UnsafeMemberRow> Rows(
        UnsafeMemberFindingInspection? inspection) =>
        [
            .. Findings(inspection)
                .Select(finding => (
                    Member: MemberText(finding.Member),
                    Finding: finding))
                .OrderBy(row => row.Member, StringComparer.Ordinal)
                .ThenBy(row => row.Finding.Member.MetadataToken)
                .Select(row => new UnsafeMemberRow(
                    MarkoutInline.Code(row.Member),
                    ExposureText(row.Finding.Exposure),
                    ContractText(row.Finding),
                    UnsafeMemberFindingSummary.SafeBoundaryText(row.Finding.SafeBoundary),
                    EvidenceText(row.Finding.Evidence),
                    PartialText(row.Finding))),
        ];

    /// <summary>
    /// The Member scope's finding for one method, or null when no evidence was
    /// attributed to it.
    /// </summary>
    public static UnsafeMemberFinding? MemberFinding(
        UnsafeMemberFindingInspection? inspection) =>
        Findings(inspection) switch
        {
            [] => null,
            [var finding] => finding,
            var findings => throw new InvalidOperationException(
                $"A Member census attributed {findings.Length} findings to one method."),
        };

    /// <summary>
    /// The Member rows: one per evidence item in inventory order, each with
    /// the member's exposure, contract, and safe-boundary state.
    /// </summary>
    public static List<UnsafeCodeEvidenceRow> EvidenceRows(UnsafeMemberFinding? finding) =>
        finding is null
            ? []
            :
            [
                .. finding.Evidence.Select(evidence => new UnsafeCodeEvidenceRow(
                    ExposureText(finding.Exposure),
                    ContractText(finding),
                    UnsafeMemberFindingSummary.SafeBoundaryText(finding.SafeBoundary),
                    UnsafeMemberFindingSummary.RoleText(evidence.Kind),
                    ApiOutputFormatter.FormatMethod(evidence.Body),
                    ILText(evidence),
                    string.IsNullOrEmpty(evidence.Detail) ? null : evidence.Detail,
                    PartialText(finding))),
            ];

    /// <summary>
    /// The default stderr disclosure of an incomplete census: the number of
    /// limitations and, when nothing was observed, that no finding was
    /// observed rather than that none exists.
    /// </summary>
    public static string Warning(UnsafeMemberFindingInspection.Incomplete incomplete)
    {
        int count = incomplete.Limitations.Length;
        string limitations = count == 1
            ? "1 limitation"
            : $"{count} limitations";
        string observed = incomplete.Inspection.Findings.IsEmpty
            ? "no findings were observed, but "
            : string.Empty;
        return $"Unsafe Code inspection incomplete: {observed}the census has "
            + $"{limitations}; rerun with --verbose for details.";
    }

    /// <summary>Discloses an incomplete census on stderr, with each limitation under --verbose.</summary>
    public static void Disclose(
        UnsafeMemberFindingInspection? inspection,
        bool verbose)
    {
        if (inspection is not UnsafeMemberFindingInspection.Incomplete incomplete)
            return;

        CommandError.WriteWarning(Warning(incomplete));
        if (!verbose)
            return;

        foreach (UnsafeMemberLimitation limitation in incomplete.Limitations)
            CommandError.WriteWarning($"Unsafe Code limitation: {LimitationText(limitation)}");
    }

    public static string LimitationText(UnsafeMemberLimitation limitation)
        => limitation.Describe(ApiOutputFormatter.FormatMethod);

    /// <summary>
    /// The default stderr disclosure of an incomplete Library answer: the
    /// number of limitations and, when no Type was observed, that none was
    /// observed rather than that none has unsafe code.
    /// </summary>
    public static string Warning(UnsafeCodeTypes types)
    {
        int count = types.Limitations.Length;
        string limitations = count == 1
            ? "1 limitation"
            : $"{count} limitations";
        string observed = types.Types.IsEmpty
            ? "no unsafe code was observed, but "
            : string.Empty;
        return $"Unsafe Code inspection incomplete: {observed}the search has "
            + $"{limitations}; rerun with --verbose for details.";
    }

    /// <summary>Discloses an incomplete Library answer on stderr, with each limitation under --verbose.</summary>
    public static void Disclose(UnsafeCodeTypes types, bool verbose)
    {
        if (types.IsComplete)
            return;

        CommandError.WriteWarning(Warning(types));
        if (!verbose)
            return;

        foreach (UnsafeMemberLimitation limitation in types.Limitations)
            CommandError.WriteWarning($"Unsafe Code limitation: {LimitationText(limitation)}");
    }

    public static List<UnsafeCodeTypeJson>? Json(UnsafeCodeLibraryInspection? inspection) =>
        inspection is UnsafeCodeLibraryInspection.Available available
            ?
            [
                .. LibraryTypes(available.Types).Select(type => new UnsafeCodeTypeJson
                {
                    Type = type.Type,
                    State = type.State,
                }),
            ]
            : null;

    public static List<string>? LimitationsJson(UnsafeCodeLibraryInspection? inspection) =>
        inspection is UnsafeCodeLibraryInspection.Available available
            ? [.. available.Types.Limitations.Select(LimitationText)]
            : null;

    static ImmutableArray<UnsafeMemberFinding> Payloads(
        Inspector.Findings.FindingInspection<UnsafeMemberFinding>.Complete inspection) =>
        [.. inspection.Findings.Select(static finding => finding.Payload)];

    static IEnumerable<(string Type, string State)> LibraryTypes(UnsafeCodeTypes? types) =>
        (types?.Types ?? [])
            .Select(type => (Type: TypeText(type.DeclaringType), State: StateText(type.State)))
            .OrderBy(type => type.Type, StringComparer.Ordinal);

    static string StateText(UnsafeCodeTypeState state) =>
        state switch
        {
            UnsafeCodeTypeState.Unsafe => UnsafeState,
            UnsafeCodeTypeState.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    // The metadata spelling keeps generic arity, so GCHandle and GCHandle`1
    // are distinct rows, and it is a name the type command resolves.
    static string TypeText(TypeRef type) =>
        type.Resolution?.Type.ToMetadataFullName()
            ?? (type.Namespace.Length == 0 ? type.Name : $"{type.Namespace}.{type.Name}");

    static string MemberText(MethodIdentity member) =>
        ApiOutputFormatter.FormatMember(null, member.Name, member.ParameterTypes, []);

    static string? ContractText(UnsafeMemberFinding finding) =>
        finding.HasExplicitUnsafeContract ? "explicit" : null;

    static string? PartialText(UnsafeMemberFinding finding) =>
        finding.HasPartialEvidence ? "partial" : null;

    static string? ILText(UnsafeMemberFindingEvidence evidence) =>
        evidence.ILOffset is { } offset ? $"IL_{offset:X4}" : null;

    static string ExposureText(UnsafeMemberExposure exposure) =>
        UnsafeMemberFindingSummary.ExposureText(exposure);

    // Roles in inventory order, a count suffix for a repeated role, and the
    // distinct callees of an explicit-contract call role, from the same
    // provenance-free summary that Diff compares.
    static string EvidenceText(ImmutableArray<UnsafeMemberFindingEvidence> evidence) =>
        string.Join(
            "; ",
            UnsafeMemberFindingSummary.RolesOf(evidence)
                .Select(static role => UnsafeMemberFindingSummary.RoleText(
                    role,
                    MarkoutInline.Code)));
}
