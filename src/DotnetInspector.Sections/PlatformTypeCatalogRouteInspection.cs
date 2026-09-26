using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public enum PlatformTypeCatalogRouteTargetKind
{
    Type,
    Member,
}

/// <summary>
/// One exact user query and the Type or member target supplied to the Platform
/// catalog query.
/// </summary>
public sealed record PlatformTypeCatalogRouteRequest
{
    public PlatformTypeCatalogRouteRequest(
        string originalQuery,
        string typePattern,
        string? memberSelector)
    {
        ArgumentNullException.ThrowIfNull(originalQuery);
        ArgumentNullException.ThrowIfNull(typePattern);

        string expectedQuery;
        if (memberSelector is null)
        {
            expectedQuery = typePattern;
            TargetKind = PlatformTypeCatalogRouteTargetKind.Type;
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(memberSelector);
            expectedQuery = $"{typePattern}.{memberSelector}";
            TargetKind = PlatformTypeCatalogRouteTargetKind.Member;
        }
        if (!string.Equals(
                originalQuery.Trim(),
                expectedQuery.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The original query must match the supplied Type and member target after trimming surrounding whitespace.",
                nameof(originalQuery));
        }

        OriginalQuery = originalQuery;
        TypePattern = typePattern;
        MemberSelector = memberSelector;
    }

    public string OriginalQuery { get; }

    public string TypePattern { get; }

    public PlatformTypeCatalogRouteTargetKind TargetKind { get; }

    public string? MemberSelector { get; }
}

/// <summary>Detached exact target selected for one Platform catalog query.</summary>
public sealed record PlatformTypeCatalogRouteTarget(
    PlatformFamily Family,
    string TargetFramework,
    string Version);

/// <summary>One detached structured Platform declaration candidate.</summary>
public sealed record PlatformTypeCatalogRouteCandidate(
    MetadataTypeDefinitionName Type,
    ExactTypeAssemblyIdentity Assembly,
    AssemblyTypeDeclarationKind DeclarationKind);

/// <summary>
/// Typed terminal content for one Platform catalog route inspection.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Resolved), "resolved")]
[JsonDerivedType(typeof(Ambiguous), "ambiguous")]
[JsonDerivedType(typeof(Missing), "missing")]
[JsonDerivedType(typeof(Rejected), "rejected")]
public abstract record PlatformTypeCatalogRouteOutcome
{
    private protected PlatformTypeCatalogRouteOutcome()
    {
    }

    public sealed record Resolved(
        PlatformTypeCatalogRouteRequest Request,
        PlatformTypeCatalogRouteTarget Target,
        PlatformTypeCatalogRouteCandidate Candidate)
        : PlatformTypeCatalogRouteOutcome;

    public sealed record Ambiguous(
        PlatformTypeCatalogRouteRequest Request,
        PlatformTypeCatalogRouteTarget Target,
        ImmutableArray<PlatformTypeCatalogRouteCandidate> Candidates)
        : PlatformTypeCatalogRouteOutcome
    {
        public ImmutableArray<PlatformTypeCatalogRouteCandidate> Candidates
        { get; init; } = Candidates.IsDefault ? [] : Candidates;
    }

    public sealed record Missing(
        PlatformTypeCatalogRouteRequest Request,
        PlatformTypeCatalogRouteTarget Target)
        : PlatformTypeCatalogRouteOutcome;

    public sealed record Rejected(
        PlatformTypeCatalogRouteRequest Request,
        PlatformTypeCatalogRouteTarget Target,
        PlatformTypeCatalogQueryRejectionKind Rejection)
        : PlatformTypeCatalogRouteOutcome;
}

/// <summary>
/// Materializes one Platform catalog query and its exact route correspondence
/// into a completed host-neutral inspection envelope.
/// </summary>
public static class PlatformTypeCatalogRouteInspection
{
    public static InspectionEnvelope<PlatformTypeCatalogRouteOutcome> Execute(
        PlatformTypeCatalog catalog,
        PlatformTypeCatalogRouteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        PlatformTypeCatalogQueryOutcome query =
            PlatformTypeCatalogQuery.Execute(
                catalog,
                PlatformTypeCatalogQuery.ResolvePattern(
                    request.TypePattern,
                    cancellationToken),
                cancellationToken);
        PlatformTypeCatalogRouteTarget target = Snapshot(catalog.Target);
        PlatformTypeCatalogRouteOutcome content = query switch
        {
            PlatformTypeCatalogQueryOutcome.Resolved resolved =>
                new PlatformTypeCatalogRouteOutcome.Resolved(
                    request,
                    target,
                    Snapshot(resolved.Candidate)),
            PlatformTypeCatalogQueryOutcome.Ambiguous ambiguous =>
                new PlatformTypeCatalogRouteOutcome.Ambiguous(
                    request,
                    target,
                    [
                        .. ambiguous.Candidates.Select(Snapshot),
                    ]),
            PlatformTypeCatalogQueryOutcome.Missing =>
                new PlatformTypeCatalogRouteOutcome.Missing(
                    request,
                    target),
            PlatformTypeCatalogQueryOutcome.Rejected rejected =>
                new PlatformTypeCatalogRouteOutcome.Rejected(
                    request,
                    target,
                    rejected.Kind),
            _ => throw new InvalidOperationException(
                "Unknown Platform type catalog query outcome."),
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(
            content,
            new InspectionShare.NonProjectable(
                "platform-type-catalog-route/share",
                "Platform catalog routes do not yet have a canonical Workspace Share projection."));
    }

    private static PlatformTypeCatalogRouteTarget Snapshot(
        PlatformFamilyTarget target) =>
        new(
            target.Family,
            target.TargetFramework.ToString(),
            target.Version.Value);

    private static PlatformTypeCatalogRouteCandidate Snapshot(
        PlatformTypeCatalogEntry candidate)
    {
        ManagedMetadataIdentity.Assembly assembly =
            candidate.ApiContent.AssemblyIdentity
            ?? throw new InvalidOperationException(
                "A Platform type catalog candidate requires a managed assembly identity.");
        return new(
            candidate.Name,
            new ExactTypeAssemblyIdentity(
                assembly.Identity,
                candidate.ModuleVersionId),
            candidate.Kind);
    }
}
