using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Findings;

namespace InspectWeb.Engine.SourceFacade;

[SupportedOSPlatform("browser")]
internal static class BrowserSourceComparisonProjection
{
    internal static BrowserSourceComparison Project(
        BrowserSourceComparisonRequest request,
        AssemblyMemberSourcePairResult pair,
        BrowserWorkspaceParticipant before,
        BrowserWorkspaceParticipant after) =>
        new(
            request,
            pair.Status.ToString(),
            pair.IsExact,
            Endpoint(pair.Before, before),
            Endpoint(pair.After, after),
            pair.Comparison is FindingComparison<string>.Complete complete
                ? [.. complete.Pairs.Select(Line)]
                : [],
            pair.Failure?.Detail
                ?? (pair.Comparison is FindingComparison<string>.Failed failed
                    ? failed.Failure
                    : null));

    static BrowserSourceComparisonEndpoint Endpoint(
        AssemblyMemberSourcePairEndpoint endpoint,
        BrowserWorkspaceParticipant participant)
    {
        string state;
        string? detail = null;
        string? text = null;
        string? sourceUrl = null;
        AssemblyPdbSourceProvenance? provenance = null;
        AssemblyMemberSourceRequest? request = null;
        switch (endpoint)
        {
            case AssemblyMemberSourcePairEndpoint.Resolved resolved:
                request = resolved.Request;
                switch (resolved.Source)
                {
                    case AssemblyMemberPdbSourceAttempt.Available available:
                        state = "Available";
                        text = available.Inspection.Text;
                        sourceUrl = available.Inspection.Document?.ResolvedUrl;
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
                        sourceUrl = unavailable.Inspection.Document?.ResolvedUrl;
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
            state, detail, text, sourceUrl, provenance?.RepositoryUrl, provenance?.Revision);
    }

    static BrowserSourceComparisonLine Line(PairFinding<string> pair)
    {
        return pair switch
        {
            PairFinding<string>.Added added =>
                Row(null, added.New),
            PairFinding<string>.Removed removed =>
                Row(removed.Old, null),
            PairFinding<string>.Present present =>
                Row(present.Old, present.New),
            PairFinding<string>.Changed changed =>
                Row(changed.Old, changed.New),
        };

        BrowserSourceComparisonLine Row(Finding<string>? before, Finding<string>? after) =>
            new(
                pair.Kind.ToString(), pair.Difference.ToString(),
                before?.Ordinal + 1, before?.Payload,
                after?.Ordinal + 1, after?.Payload);
    }
}
