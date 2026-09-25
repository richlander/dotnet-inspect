using System.Runtime.Versioning;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using Inspector.Findings;
using ILInspector.SourceLink;

namespace DotnetInspect.Web.Interop.Source;

[SupportedOSPlatform("browser")]
internal static class BrowserSourceComparisonProjection
{
    internal static BrowserSourceComparison Project(
        BrowserSourceComparisonRequest request,
        AssemblyMemberSourcePairResult pair,
        BrowserWorkspaceParticipant before,
        BrowserWorkspaceParticipant after)
    {
        MemberSourcePairDiffPresentationResult projected =
            MemberSourcePairDiffPresentationAdapter.Create(pair);
        BrowserSourceDiff? diff = projected switch
        {
            MemberSourcePairDiffPresentationResult.Available available =>
                BrowserSourceDiffProjection.Project(available.Presentation),
            _ => null,
        };
        string? failure = projected switch
        {
            MemberSourcePairDiffPresentationResult.Failed failed =>
                failed.Detail,
            _ => pair.Failure?.Detail
                ?? (pair.Comparison is FindingComparison<string>.Failed failed
                    ? failed.Failure
                    : null),
        };
        bool retainEndpointText = diff is null;
        BrowserSourceComparisonEndpoint beforeEndpoint =
            Endpoint(pair.Before, before, retainEndpointText);
        BrowserSourceComparisonEndpoint afterEndpoint =
            Endpoint(pair.After, after, retainEndpointText);
        return new(
            request,
            pair.Status.ToString(),
            pair.IsExact,
            beforeEndpoint,
            afterEndpoint,
            diff,
            failure);
    }

    static BrowserSourceComparisonEndpoint Endpoint(
        AssemblyMemberSourcePairEndpoint endpoint,
        BrowserWorkspaceParticipant participant,
        bool retainText)
    {
        string state;
        string? detail = null;
        string? text = null;
        string? browseUrl = null;
        AssemblyPdbSourceProvenance? provenance = null;
        AssemblyMemberSourceRequest? request = null;
        switch (endpoint)
        {
            case AssemblyMemberSourcePairEndpoint.Unrequested:
                state = "Unrequested";
                break;
            case AssemblyMemberSourcePairEndpoint.Resolved resolved:
                request = resolved.Request;
                switch (resolved.Source)
                {
                    case AssemblyMemberPdbSourceAttempt.Available available:
                        state = "Available";
                        text = retainText ? available.Inspection.Text : null;
                        if (available.Inspection.ChecksumVerification is
                            SourceChecksumVerification.Exact
                                or SourceChecksumVerification.LineEndingNormalized)
                        {
                            browseUrl = BrowserSourceDiffProjection.BrowseUrl(
                                available.Inspection.Document?.ResolvedUrl);
                        }
                        provenance = available.Provenance;
                        break;
                    case AssemblyMemberPdbSourceAttempt.Unavailable unavailable:
                        (state, detail) = unavailable.Inspection.Lines.Value switch
                        {
                            FindingInspection<string>.Absent absent =>
                                ("Unavailable", $"{unavailable.Inspection.Outcome}: {absent.Detail}"),
                            FindingInspection<string>.Failed failed =>
                                ("Failed", $"{unavailable.Inspection.Outcome}: {failed.Error.Reason}"),
                            _ => throw new InvalidOperationException(
                                "Unavailable Source carried complete evidence."),
                        };
                        break;
                    default:
                        throw new InvalidOperationException("Unknown PDB Source attempt.");
                }
                break;
            case AssemblyMemberSourcePairEndpoint.NotFound missing:
                state = "NotFound";
                detail = $"{missing.Failure.Kind}: {missing.Failure.Detail}";
                break;
            case AssemblyMemberSourcePairEndpoint.Rejected rejected:
                state = "Rejected";
                detail = $"{rejected.Failure.Kind}: {rejected.Failure.Detail}";
                break;
            case AssemblyMemberSourcePairEndpoint.Failed failed:
                state = "Failed";
                detail = $"{failed.Failure.Kind}: {failed.Failure.Detail}";
                break;
            default:
                throw new InvalidOperationException("Unknown Source comparison endpoint.");
        }
        return new(
            participant.Coordinate.PackageId,
            participant.Coordinate.Version,
            participant.Coordinate.Framework,
            participant.Asset.AssemblyName,
            participant.Asset.Path,
            endpoint.Subject.Registration.ModuleVersionId?.ToString("D"),
            endpoint.Subject.Identity.ToString(),
            request is null ? null
                : $"{request.Type.ToEscapedFullName()}::{request.Member.StableSelector}",
            request?.MetadataToken,
            state, detail, text, browseUrl, provenance?.RepositoryUrl, provenance?.Revision);
    }
}
