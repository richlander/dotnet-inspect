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
        Projection> _projections = [];
    private string? _currentEcosystem;

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
        _projections.TryAdd(
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
        RegisterSource(
            match.Declaration.Source.Coordinate,
            source,
            ecosystem);
        return _session.Publish(match);
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
        ExactLibrarySourceCoordinate coordinate =
            finding.Match.Declaration.Source.Coordinate;
        Projection projection = _projections.TryGetValue(
                coordinate,
                out Projection? candidate)
            ? candidate
            : throw new InvalidOperationException(
                "A semantic Member finding has no CLI source projection.");
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
        _writer!.Write(
            FindDiscoveryOutput.Project(Project(member)));
        _writer.Flush();
    }

    private sealed record Projection(
        SearchAssemblySource Source,
        string Ecosystem);

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
