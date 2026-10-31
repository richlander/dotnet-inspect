using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.Inspectors;

internal sealed class FindMemberReportingSession :
    IFindSemanticFindingSink
{
    private readonly FindDiscoveryTsvWriter? _writer;
    private readonly FindSemanticResultSession _session;
    private readonly Dictionary<
        ExactLibrarySourceCoordinate,
        Projection> _sourceProjections = [];
    private readonly Dictionary<
        MemberFindSemanticMatch,
        Projection> _findingProjections =
            new(ReferenceEqualityComparer.Instance);
    private string? _currentEcosystem;
    private ActivePublication? _activePublication;

    internal FindMemberReportingSession(
        int? maximumFindings,
        FindDiscoveryTsvWriter? writer)
    {
        _writer = writer;
        _session = new(
            maximumFindings,
            writer is null ? null : this);
    }

    internal FindSemanticResultSession SemanticSession => _session;

    internal IDisposable BeginAttribution(string ecosystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ecosystem);
        string? previous = _currentEcosystem;
        _currentEcosystem = ecosystem;
        return new AttributionScope(this, previous);
    }

    internal void RegisterSource(
        ExactLibrarySourceCoordinate coordinate,
        SearchAssemblySource source,
        string ecosystem)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(ecosystem);
        _sourceProjections.TryAdd(
            coordinate,
            new(source, ecosystem));
    }

    internal FindSemanticFindingPublication Publish(
        MemberFindSemanticMatch match,
        SearchAssemblySource source)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(source);
        string ecosystem =
            _currentEcosystem
            ?? throw new InvalidOperationException(
                "Semantic Member publication requires Ecosystem attribution.");
        var projection = new Projection(source, ecosystem);
        RegisterSource(
            match.Declaration.Source.Coordinate,
            source,
            ecosystem);
        _activePublication = new(match, projection);
        try
        {
            FindSemanticFindingPublication publication =
                _session.Publish(match);
            if (publication is FindSemanticFindingPublication.Published)
                _findingProjections.TryAdd(match, projection);
            return publication;
        }
        finally
        {
            _activePublication = null;
        }
    }

    internal FindSemanticResult Complete(
        FindSemanticResultCompletion completion,
        IEnumerable<FindSemanticResultFailure>? failures = null,
        IEnumerable<string>? unsearched = null) =>
        _session.Complete(completion, failures, unsearched);

    internal MemberFindResult Project(
        FindSemanticFinding.Member finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        Projection projection = ProjectionFor(finding);
        return MemberSearchService.ProjectSemanticMatch(
                finding.Match,
                projection.Source)
            with
        {
            Ecosystem = projection.Ecosystem,
        };
    }

    void IFindSemanticFindingSink.Publish(
        FindSemanticFinding finding)
    {
        FindSemanticFinding.Member member =
            finding as FindSemanticFinding.Member
            ?? throw new InvalidOperationException(
                "The Member reporting sink received a Type finding.");
        try
        {
            Projection projection = ProjectionFor(member);
            _writer!.Write(
                FindDiscoveryOutput.Project(
                    MemberSearchService.ProjectSemanticMatch(
                            member.Match,
                            projection.Source)
                        with
                    {
                        Ecosystem = projection.Ecosystem,
                    }));
            _writer.Flush();
            _findingProjections.TryAdd(
                member.Match,
                projection);
        }
        catch (Exception failure)
        {
            throw new FindMemberReportPublicationException(
                failure);
        }
    }

    private sealed record Projection(
        SearchAssemblySource Source,
        string Ecosystem);

    private sealed record ActivePublication(
        MemberFindSemanticMatch Match,
        Projection Projection);

    private Projection ProjectionFor(
        FindSemanticFinding.Member finding)
    {
        if (_activePublication is { } active
            && ReferenceEquals(active.Match, finding.Match))
        {
            return active.Projection;
        }
        if (_findingProjections.TryGetValue(
                finding.Match,
                out Projection? retained))
        {
            return retained;
        }
        ExactLibrarySourceCoordinate coordinate =
            finding.Match.Declaration.Source.Coordinate;
        return _sourceProjections.TryGetValue(
                coordinate,
                out Projection? source)
            ? source
            : throw new InvalidOperationException(
                "A semantic Member finding has no CLI source projection.");
    }

    private sealed class AttributionScope(
        FindMemberReportingSession owner,
        string? previous) : IDisposable
    {
        private FindMemberReportingSession? _owner = owner;

        public void Dispose()
        {
            FindMemberReportingSession? current =
                Interlocked.Exchange(ref _owner, null);
            if (current is not null)
                current._currentEcosystem = previous;
        }
    }
}

internal sealed class FindMemberReportPublicationException(
    Exception innerException)
    : Exception(
        "Member Find report publication failed.",
        innerException);
