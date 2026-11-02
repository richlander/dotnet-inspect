using System.Collections.Immutable;

using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Views;
using ILInspector.Analysis;
using Markout;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// The <c>Unsafe Members</c> section's projection of the unsafe member census
/// at Library and Type scope, as <c>docs/design/unsafe-members-section.md</c>
/// defines it: one row per finding, the stderr disclosure of an incomplete
/// census, and its JSON shape.
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
    /// The rows, ordered by the rendered member and then by metadata token so
    /// overloads keep definition order.
    /// </summary>
    public static List<UnsafeMemberRow> Rows(
        UnsafeMemberFindingInspection? inspection,
        bool includeDeclaringType) =>
        [
            .. Findings(inspection)
                .Select(finding => (
                    Member: MemberText(finding.Member, includeDeclaringType),
                    Finding: finding))
                .OrderBy(row => row.Member, StringComparer.Ordinal)
                .ThenBy(row => row.Finding.Member.MetadataToken)
                .Select(row => new UnsafeMemberRow(
                    MarkoutInline.Code(row.Member),
                    ExposureText(row.Finding.Exposure),
                    row.Finding.HasExplicitUnsafeContract ? "explicit" : null,
                    EvidenceText(row.Finding.Evidence),
                    row.Finding.HasPartialEvidence ? "partial" : null)),
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
        return $"Unsafe Members inspection incomplete: {observed}the census has "
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
            CommandError.WriteWarning($"Unsafe Members limitation: {LimitationText(limitation)}");
    }

    public static string LimitationText(UnsafeMemberLimitation limitation)
        => limitation.Describe(ApiOutputFormatter.FormatMethod);

    public static List<UnsafeMemberFindingJson>? Json(
        UnsafeMemberFindingInspection? inspection) =>
        inspection is UnsafeMemberFindingInspection.Complete
            or UnsafeMemberFindingInspection.Incomplete
            ?
            [
                .. Findings(inspection)
                    .OrderBy(
                        finding => ApiOutputFormatter.FormatMethod(finding.Member),
                        StringComparer.Ordinal)
                    .ThenBy(finding => finding.Member.MetadataToken)
                    .Select(finding => new UnsafeMemberFindingJson
                    {
                        Member = ApiOutputFormatter.FormatMethod(finding.Member),
                        Exposure = ExposureText(finding.Exposure),
                        ExplicitContract = finding.HasExplicitUnsafeContract,
                        Partial = finding.HasPartialEvidence,
                        Evidence =
                        [
                            .. finding.Evidence.Select(evidence =>
                                new UnsafeMemberEvidenceJson
                                {
                                    Role = UnsafeMemberFindingSummary.RoleText(evidence.Kind),
                                    Body = ApiOutputFormatter.FormatMethod(evidence.Body),
                                    IL = evidence.ILOffset is { } offset
                                        ? $"IL_{offset:X4}"
                                        : null,
                                    Detail = evidence.Detail,
                                    ContractSource = evidence.ContractSource?.ToString(),
                                }),
                        ],
                    }),
            ]
            : null;

    public static List<string>? LimitationsJson(
        UnsafeMemberFindingInspection? inspection) =>
        inspection switch
        {
            UnsafeMemberFindingInspection.Complete => [],
            UnsafeMemberFindingInspection.Incomplete incomplete =>
                [.. incomplete.Limitations.Select(LimitationText)],
            _ => null,
        };

    static ImmutableArray<UnsafeMemberFinding> Payloads(
        Inspector.Findings.FindingInspection<UnsafeMemberFinding>.Complete inspection) =>
        [.. inspection.Findings.Select(static finding => finding.Payload)];

    static string MemberText(MethodIdentity member, bool includeDeclaringType) =>
        includeDeclaringType
            ? ApiOutputFormatter.FormatMethod(member)
            : ApiOutputFormatter.FormatMember(null, member.Name, member.ParameterTypes, []);

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
