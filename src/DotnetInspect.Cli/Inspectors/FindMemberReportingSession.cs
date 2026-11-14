using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Inspectors;

internal sealed class FindMemberReportingSession :
    IEcosystemFindFindingSink
{
    private readonly FindDiscoveryTsvWriter? _writer;

    internal FindMemberReportingSession(
        FindResultRowPlan resultRows,
        FindDiscoveryTsvWriter? writer)
    {
        _writer = writer;
        ArgumentNullException.ThrowIfNull(resultRows);
        ResultSession = new(
            resultRows,
            writer is null ? null : this);
    }

    internal EcosystemFindResultSession ResultSession { get; }

    internal MemberFindResult Project(
        EcosystemFindFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        FindSemanticFinding.Member member =
            finding.Finding as FindSemanticFinding.Member
            ?? throw new InvalidOperationException(
                "The Member reporting adapter received a Type finding.");
        SearchAssemblySource source =
            SearchAssemblySource.FromSemantic(
                member.Match.Declaration.Source);
        return MemberSearchService.ProjectSemanticMatch(
                member.Match,
                source)
            with
        {
            Ecosystem = MembershipDisplay(finding.Memberships),
        };
    }

    void IEcosystemFindFindingSink.Publish(
        EcosystemFindFinding finding)
    {
        try
        {
            _writer!.Write(
                FindDiscoveryOutput.Project(
                    Project(finding)));
            _writer.Flush();
        }
        catch (Exception failure)
        {
            throw new FindMemberReportPublicationException(
                failure);
        }
    }

    private static string MembershipDisplay(
        IReadOnlyList<EcosystemFindMembership> memberships) =>
        string.Join(
            ", ",
            memberships.Select(static membership =>
                membership.Basis
                    is EcosystemFindMembershipBasisKind.PackagePrefix
                    ? $"{membership.Ecosystem.Value} (prefix)"
                    : membership.Ecosystem.Value));
}

internal sealed class FindMemberReportPublicationException(
    Exception innerException)
    : Exception(
        "Member Find report publication failed.",
        innerException);
