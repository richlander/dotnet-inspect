using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

/// <summary>
/// CLI host for sequential typed-query execution over resolved assembly sets.
/// Ordinary scans use one-participant groups so retained image memory is bounded
/// by the largest current participant rather than the entire search set.
/// </summary>
internal sealed class AssemblySetInspectionWorkspace : IDisposable
{
    private readonly InspectionWorkspace _workspace = new();

    internal long PeakRetainedImageBytes { get; private set; }

    internal void RunGroup(
        AssemblySet assemblySet,
        Action<AssemblyContextGroup, AssemblyContextEntryMap> execute,
        Action<AssemblySetEntry, string> unavailable)
    {
        ArgumentNullException.ThrowIfNull(assemblySet);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(unavailable);

        var resolved =
            new List<(AssemblySetEntry Entry, ResolvedAssemblyReference Assembly)>();
        foreach (AssemblySetEntry entry in assemblySet.Assemblies)
        {
            ResolvedAssemblyReference? assembly =
                TryCreateManagedAssembly(entry, out string? failure);
            if (assembly is null)
            {
                unavailable(entry, failure!);
                continue;
            }

            resolved.Add((entry, assembly));
        }

        if (resolved.Count == 0)
            return;

        var sourcePolicies = resolved
            .Select(candidate => (
                candidate.Assembly,
                Policy: (IAssemblyBindingPolicy)new AssemblyDependencyResolver(
                    new AssemblyDependencyResolutionOptions(
                        candidate.Entry.Path))))
            .ToArray();
        var groupPolicy =
            new SourceRelativeAssemblyGroupBindingPolicy(sourcePolicies);
        var participants = resolved
            .Select(candidate =>
                new AssemblyContextParticipant(
                    candidate.Assembly,
                    groupPolicy))
            .ToArray();
        using AssemblyContextGroup group =
            _workspace.CreateAssemblyContextGroup(participants);
        execute(
            group,
            new AssemblyContextEntryMap(
                resolved.Select(candidate => (
                    candidate.Assembly.Registration,
                    candidate.Entry))));
    }

    internal void RunPerAssembly<TValue>(
        AssemblySet assemblySet,
        InspectionQuery<AssemblyContextResult<TValue>> query,
        Func<AssemblyContextGroup, AssemblyContextResult<TValue>> execute,
        Action<AssemblySetEntry, AssemblyContextEntry<TValue>> consume,
        Action<AssemblySetEntry, string> unavailable,
        Func<bool>? stop = null)
    {
        ArgumentNullException.ThrowIfNull(assemblySet);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(consume);
        ArgumentNullException.ThrowIfNull(unavailable);

        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(query, execute);
        foreach (AssemblySetEntry entry in assemblySet.Assemblies)
        {
            if (stop?.Invoke() == true)
                break;

            ResolvedAssemblyReference? assembly =
                TryCreateManagedAssembly(entry, out string? failure);
            if (assembly is null)
            {
                unavailable(entry, failure!);
                continue;
            }

            var policy = new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(entry.Path));
            using AssemblyContextGroup group =
                _workspace.CreateAssemblyContextGroup(
                    [new AssemblyContextParticipant(assembly, policy)]);
            AssemblyContextResult<TValue> result =
                registry.Run([query], group).Get(query);
            PeakRetainedImageBytes = Math.Max(
                PeakRetainedImageBytes,
                group.RetainedImageBytes);
            if (result.Assemblies.Length != 1)
            {
                throw new InspectionQueryException(
                    $"Query '{query.Name}' produced {result.Assemblies.Length} results for a one-participant group.");
            }

            consume(entry, result.Assemblies[0]);
        }
    }

    private static ResolvedAssemblyReference? TryCreateManagedAssembly(
        AssemblySetEntry entry,
        out string? failure)
    {
        try
        {
            ResolvedAssemblyReference? assembly =
                ResolvedAssemblyReference.CreateFromPathIfManaged(
                    entry.Path,
                    ProvenanceFor(entry));
            failure = assembly is null
                ? "The selected file does not contain managed metadata."
                : null;
            return assembly;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException)
        {
            failure = ex.Message;
            return null;
        }
    }

    private static AssemblyResolutionProvenance ProvenanceFor(
        AssemblySetEntry entry)
        => entry.SourceKind switch
        {
            AssemblySetSourceKind.Package
                when !string.IsNullOrWhiteSpace(entry.Version) =>
                AssemblyResolutionProvenance.Package(
                    entry.Source,
                    entry.Version,
                    entry.Tfm,
                    rid: null),
            AssemblySetSourceKind.PlatformAssembly
                or AssemblySetSourceKind.PlatformFramework =>
                AssemblyResolutionProvenance.Platform(
                    entry.Source,
                    entry.Version,
                    "assembly-set search"),
            AssemblySetSourceKind.Project =>
                AssemblyResolutionProvenance.Local(
                    "restored project asset"),
            _ => AssemblyResolutionProvenance.Local(
                entry.Source),
        };

    public void Dispose() => _workspace.Dispose();
}

internal sealed class AssemblyContextEntryMap
{
    private readonly Dictionary<
        AssemblyAcquisitionRegistration,
        AssemblySetEntry> _entries;

    internal AssemblyContextEntryMap(
        IEnumerable<(
            AssemblyAcquisitionRegistration Registration,
            AssemblySetEntry Entry)> entries)
    {
        _entries = new Dictionary<
            AssemblyAcquisitionRegistration,
            AssemblySetEntry>(
                ReferenceEqualityComparer.Instance);
        foreach (var (registration, entry) in entries)
            _entries.Add(registration, entry);
    }

    internal AssemblySetEntry EntryFor(
        AssemblyContextSubject subject)
        => _entries.TryGetValue(
                subject.Registration,
                out AssemblySetEntry? entry)
            ? entry
            : throw new InspectionQueryException(
                $"No assembly-set entry corresponds to '{subject.Identity.Name}'.");
}
