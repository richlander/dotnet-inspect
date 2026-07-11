using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ILInspector.Findings;

/// <summary>
/// A stable caller-supplied address in an ordered inspection history.
/// </summary>
public sealed record FindingVersion
{
    public FindingVersion(string key, string display, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(display);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Key = key;
        Display = display;
        Position = position;
    }

    public string Key { get; }
    public string Display { get; }
    public int Position { get; }
}

/// <summary>
/// One evaluated address and its complete inspection outcome.
/// </summary>
public sealed record VersionedFindingInspection<T>(
    FindingVersion Version,
    FindingInspection<T> Inspection)
    where T : notnull
{
    public FindingVersion Version { get; }
        = Version ?? throw new ArgumentNullException(nameof(Version));

    public FindingInspection<T> Inspection { get; }
        = Inspection ?? throw new ArgumentNullException(nameof(Inspection));
}

/// <summary>
/// One occurrence labelled with the address where it was observed.
/// </summary>
public sealed record VersionedFinding<T>(
    FindingVersion Version,
    Finding<T> Finding)
    where T : notnull
{
    public FindingVersion Version { get; }
        = Version ?? throw new ArgumentNullException(nameof(Version));

    public Finding<T> Finding { get; }
        = Finding ?? throw new ArgumentNullException(nameof(Finding));
}

/// <summary>
/// The exact identity selected for correlation.
/// </summary>
public sealed record FindingCorrelationKey(
    FindingSubject Subject,
    FindingDescriptor Descriptor,
    FindingKey Key)
{
    public FindingSubject Subject { get; }
        = Subject ?? throw new ArgumentNullException(nameof(Subject));

    public FindingDescriptor Descriptor { get; }
        = Descriptor ?? throw new ArgumentNullException(nameof(Descriptor));

    public FindingKey Key { get; }
        = Key.IdentityKey is null
            ? throw new ArgumentException("Finding key must be initialized.", nameof(Key))
            : Key;

    public static FindingCorrelationKey From<T>(Finding<T> finding)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new(finding.Subject, finding.Descriptor, finding.Key);
    }

    internal bool Matches<T>(Finding<T> finding)
        where T : notnull
        => Subject.Key == finding.Subject.Key
            && Descriptor.Id == finding.Descriptor.Id
            && Key == finding.Key;
}

/// <summary>
/// Durable N-address history for one exact finding identity. Only observed occurrences belong
/// here; missing, absent, failed, and unevaluated addresses are not findings.
/// </summary>
public sealed record CorrelatedFinding<T>
    where T : notnull
{
    public CorrelatedFinding(
        FindingCorrelationKey key,
        ImmutableArray<VersionedFinding<T>> occurrences)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (occurrences.IsDefault)
            throw new ArgumentException("Occurrences must be initialized.", nameof(occurrences));
        if (occurrences.Any(occurrence => occurrence is null))
            throw new ArgumentException("Occurrences must not contain null values.", nameof(occurrences));

        for (int i = 0; i < occurrences.Length; i++)
        {
            if (!key.Matches(occurrences[i].Finding))
                throw new ArgumentException("Every occurrence must match the correlated identity.", nameof(occurrences));
            if (i > 0 && occurrences[i - 1].Version.Position >= occurrences[i].Version.Position)
                throw new ArgumentException("Occurrences must have unique ascending positions.", nameof(occurrences));
            if (occurrences.Take(i).Any(previous => previous.Version.Key == occurrences[i].Version.Key))
                throw new ArgumentException($"Version key '{occurrences[i].Version.Key}' is duplicated.", nameof(occurrences));
        }

        Key = key;
        Occurrences = occurrences;
    }

    public FindingCorrelationKey Key { get; }
    public ImmutableArray<VersionedFinding<T>> Occurrences { get; }
}

/// <summary>
/// The presence of one correlated identity at an evaluated address. Missing means a successful
/// census did not contain the identity; subject absence and inspection failure remain distinct.
/// </summary>
[Union]
public sealed record FindingCorrelationPoint<T>
    where T : notnull
{
    public FindingCorrelationPoint(Present value) => Value = Guard(value);
    public FindingCorrelationPoint(Missing value) => Value = Guard(value);
    public FindingCorrelationPoint(SubjectAbsent value) => Value = Guard(value);
    public FindingCorrelationPoint(Failed value) => Value = Guard(value);

    static object Guard(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    public object? Value { get; }

    public sealed record Present
    {
        public Present(FindingVersion version, Finding<T> finding)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Finding = finding ?? throw new ArgumentNullException(nameof(finding));
        }

        public FindingVersion Version { get; }
        public Finding<T> Finding { get; }
    }

    public sealed record Missing
    {
        public Missing(FindingVersion version)
            => Version = version ?? throw new ArgumentNullException(nameof(version));

        public FindingVersion Version { get; }
    }

    public sealed record SubjectAbsent
    {
        public SubjectAbsent(FindingVersion version, string? detail)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Detail = detail;
        }

        public FindingVersion Version { get; }
        public string? Detail { get; }
    }

    public sealed record Failed
    {
        public Failed(FindingVersion version, InspectionError error)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        public FindingVersion Version { get; }
        public InspectionError Error { get; }
    }
}

/// <summary>
/// A caller-assembled sparse correlation. It performs no probing or search: callers choose which
/// addresses to inspect and may project any two evaluated cells through the existing pair fold.
/// </summary>
public sealed class FindingCorrelation<T>
    where T : notnull
{
    readonly ImmutableDictionary<string, FindingInspection<T>> _inspections;

    FindingCorrelation(
        FindingCorrelationKey key,
        ImmutableArray<VersionedFindingInspection<T>> inspections,
        ImmutableArray<FindingCorrelationPoint<T>> timeline,
        CorrelatedFinding<T> finding)
    {
        Key = key;
        Inspections = inspections;
        Timeline = timeline;
        Finding = finding;
        _inspections = inspections.ToImmutableDictionary(item => item.Version.Key, item => item.Inspection);
    }

    public FindingCorrelationKey Key { get; }
    public ImmutableArray<VersionedFindingInspection<T>> Inspections { get; }
    public ImmutableArray<FindingCorrelationPoint<T>> Timeline { get; }
    public CorrelatedFinding<T> Finding { get; }

    public static FindingCorrelation<T> Create(
        FindingCorrelationKey key,
        IEnumerable<VersionedFindingInspection<T>> inspections)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(inspections);

        var evaluated = inspections.ToImmutableArray();
        if (evaluated.Any(item => item is null))
            throw new ArgumentException("Inspections must not contain null values.", nameof(inspections));

        var duplicateKey = evaluated
            .GroupBy(item => item.Version.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null)
            throw new ArgumentException($"Version key '{duplicateKey.Key}' is duplicated.", nameof(inspections));

        var duplicatePosition = evaluated
            .GroupBy(item => item.Version.Position)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePosition is not null)
            throw new ArgumentException($"Version position {duplicatePosition.Key} is duplicated.", nameof(inspections));

        var ordered = evaluated.OrderBy(item => item.Version.Position).ToImmutableArray();
        var timeline = ImmutableArray.CreateBuilder<FindingCorrelationPoint<T>>(ordered.Length);
        var occurrences = ImmutableArray.CreateBuilder<VersionedFinding<T>>();

        foreach (var item in ordered)
        {
            switch (item.Inspection)
            {
                case FindingInspection<T>.Complete complete:
                    var matches = complete.Findings.Where(key.Matches).Take(2).ToArray();
                    if (matches.Length > 1)
                    {
                        throw new ArgumentException(
                            $"Inspection '{item.Version.Key}' contains duplicate occurrences for the correlated identity.",
                            nameof(inspections));
                    }

                    if (matches.Length == 1)
                    {
                        occurrences.Add(new(item.Version, matches[0]));
                        timeline.Add(new FindingCorrelationPoint<T>.Present(item.Version, matches[0]));
                    }
                    else
                    {
                        timeline.Add(new FindingCorrelationPoint<T>.Missing(item.Version));
                    }
                    break;

                case FindingInspection<T>.Absent absent:
                    timeline.Add(new FindingCorrelationPoint<T>.SubjectAbsent(item.Version, absent.Detail));
                    break;

                case FindingInspection<T>.Failed failed:
                    timeline.Add(new FindingCorrelationPoint<T>.Failed(item.Version, failed.Error));
                    break;
            }
        }

        return new FindingCorrelation<T>(
            key,
            ordered,
            timeline.MoveToImmutable(),
            new CorrelatedFinding<T>(key, occurrences.ToImmutable()));
    }

    public FindingComparison<T> Compare(
        string oldVersionKey,
        string newVersionKey,
        FindingMatchOptions? matchOptions = null,
        int acceptanceThreshold = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldVersionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(newVersionKey);

        if (!_inspections.TryGetValue(oldVersionKey, out var oldInspection))
            throw new ArgumentException($"Version '{oldVersionKey}' has not been evaluated.", nameof(oldVersionKey));
        if (!_inspections.TryGetValue(newVersionKey, out var newInspection))
            throw new ArgumentException($"Version '{newVersionKey}' has not been evaluated.", nameof(newVersionKey));

        return FindingComparison.Compare(
            oldInspection,
            newInspection,
            matchOptions,
            acceptanceThreshold);
    }
}
