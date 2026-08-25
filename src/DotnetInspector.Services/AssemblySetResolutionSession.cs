using ILInspector.Metadata;

namespace DotnetInspector.Services;

/// <summary>
/// Owns one resolution catalog and source-relative binding policy for an
/// acquired assembly set.
/// </summary>
public sealed class AssemblySetResolutionSession : IDisposable
{
    readonly TypeResolutionCatalog _catalog = new();
    readonly IReadOnlyList<Participant> _participants;
    readonly IReadOnlyList<AssemblySetAcquisitionFailure>
        _acquisitionFailures;
    readonly SourceRelativeAssemblyGroupBindingPolicy? _policy;
    readonly Action<string>? _log;

    public AssemblySetResolutionSession(
        AssemblySet assemblySet,
        Action<string>? log = null)
        : this(
            (assemblySet
                ?? throw new ArgumentNullException(
                    nameof(assemblySet)))
                .Assemblies.Select(entry =>
                new ParticipantInput(
                    entry.Path,
                    ProvenanceFor(entry))),
            log)
    {
    }

    public AssemblySetResolutionSession(
        IReadOnlyList<string> assemblyPaths,
        Action<string>? log = null)
        : this(
            (assemblyPaths
                ?? throw new ArgumentNullException(
                    nameof(assemblyPaths)))
                .Select(path =>
                new ParticipantInput(
                    path,
                    AssemblyResolutionProvenance.Local(
                        "assembly-set API extraction"))),
            log)
    {
    }

    AssemblySetResolutionSession(
        IEnumerable<ParticipantInput> inputs,
        Action<string>? log)
    {
        _log = log;
        var participants = new List<Participant>();
        var failures = new List<AssemblySetAcquisitionFailure>();
        foreach (ParticipantInput input in inputs)
        {
            ResolvedAssemblyReference? assembly =
                TryCreateManagedAssembly(
                    input.Path,
                    input.Provenance,
                    out string? failure);
            if (assembly is null)
            {
                failures.Add(
                    new AssemblySetAcquisitionFailure(
                        input.Path,
                        failure!));
                continue;
            }

            participants.Add(
                new Participant(
                    input.Path,
                    assembly,
                    new AssemblyDependencyResolver(
                        new AssemblyDependencyResolutionOptions(
                            input.Path)
                        {
                            PreferImplementationAssemblies = true,
                            AllowPlatformAssemblyVersionRollForward = true,
                        })));
        }

        _participants = participants;
        _acquisitionFailures = failures;
        if (participants.Count > 0)
        {
            _policy =
                new SourceRelativeAssemblyGroupBindingPolicy(
                    participants.Select(participant => (
                        participant.Assembly,
                        Policy:
                            (IAssemblyBindingPolicy)
                                participant.Policy)));
        }
    }

    public IReadOnlyList<ResolvedAssemblyReference> AssemblyReferences =>
        _participants
            .Select(static participant => participant.Assembly)
            .ToArray();

    /// <summary>
    /// Required inputs that could not mint an acquisition descriptor.
    /// These remain visible even when other participants were retained.
    /// </summary>
    public IReadOnlyList<AssemblySetAcquisitionFailure>
        AcquisitionFailures => _acquisitionFailures;

    public ApiSurface? BuildApiSurface(
        bool includeAll = false,
        string? name = null,
        string? tfm = null,
        Action<string>? log = null)
    {
        Action<string>? sink = log ?? _log;
        var merged = new ApiSurface
        {
            Name = name,
            Tfm = tfm,
        };
        bool readSurface = false;
        foreach (AssemblySetAcquisitionFailure failure
            in _acquisitionFailures)
        {
            merged.InspectionFailures.Add(
                new ApiSurfaceInspectionFailure(
                    "acquire API surface",
                    0,
                    MetadataTypeNameFailureMechanism.Metadata,
                    "Unavailable",
                    failure.Detail)
                {
                    SourceAssemblyPath = failure.Path,
                });
            sink?.Invoke(
                $"  ! {Path.GetFileName(failure.Path)}: "
                    + failure.Detail);
        }

        foreach (Participant participant in _participants)
        {
            if (!participant.Assembly.IsAssembly)
            {
                ApiSurface? moduleSurface =
                    AssemblyReader.ExtractModuleApiSurface(
                        participant.Assembly,
                        includeAll);
                if (moduleSurface is not null)
                {
                    MergeSurface(
                        merged,
                        moduleSurface,
                        participant.Path,
                        sink);
                    readSurface = true;
                }
                continue;
            }

            ResolutionAwareApiSurfaceOutcome outcome =
                _catalog.ExtractApiSurface(
                    participant.Assembly,
                    _policy!,
                    includeAll);
            if (outcome
                is ResolutionAwareApiSurfaceOutcome.Rejected rejected)
            {
                merged.InspectionFailures.Add(
                    new ApiSurfaceInspectionFailure(
                        "extract API surface",
                        0,
                        MetadataTypeNameFailureMechanism.Metadata,
                        rejected.Failure.Kind.ToString(),
                        rejected.Failure.Detail,
                        participant.Assembly.Identity)
                    {
                        SourceAssemblyPath =
                            participant.Path,
                    });
                sink?.Invoke(
                    $"  ! {Path.GetFileName(participant.Path)}: "
                        + rejected.Failure.Detail);
                continue;
            }

            ApiSurface surface =
                ((ResolutionAwareApiSurfaceOutcome.Read)outcome)
                    .Surface;
            surface.SetInspectionSourceAssembly(
                participant.Assembly);
            MergeSurface(
                merged,
                surface,
                participant.Path,
                sink);
            readSurface = true;
        }

        merged.Types = merged.Types
            .OrderBy(static type => type.FullName)
            .ToList();
        return !readSurface
            && merged.InspectionFailures.Count == 0
                ? null
                : merged;
    }

    static void MergeSurface(
        ApiSurface merged,
        ApiSurface surface,
        string path,
        Action<string>? log)
    {
        surface.SetInspectionSourceAssemblyPath(path);
        log?.Invoke(
            $"  + {Path.GetFileNameWithoutExtension(path)}: "
                + $"{surface.PublicTypeCount} types");
        merged.Types.AddRange(surface.Types);
        merged.TypeForwarders.AddRange(surface.TypeForwarders);
        merged.IsTypeForwardingAssembly |=
            surface.IsTypeForwardingAssembly;
        merged.MergeInspectionFailuresFrom(surface);
        merged.PublicTypeCount += surface.PublicTypeCount;
        merged.PublicMethodCount += surface.PublicMethodCount;
        merged.PublicPropertyCount += surface.PublicPropertyCount;
        merged.PublicEventCount += surface.PublicEventCount;
        merged.PublicFieldCount += surface.PublicFieldCount;
    }

    static ResolvedAssemblyReference? TryCreateManagedAssembly(
        string path,
        AssemblyResolutionProvenance provenance,
        out string? failure)
    {
        try
        {
            ResolvedAssemblyReference? assembly =
                ResolvedAssemblyReference
                    .CreateInspectionReferenceFromPathIfManaged(
                    path,
                    provenance);
            failure = assembly is null
                ? "The selected file does not contain managed metadata."
                : null;
            return assembly;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException
                or OverflowException
                or IndexOutOfRangeException)
        {
            failure =
                "The selected assembly could not be acquired.";
            return null;
        }
    }

    static AssemblyResolutionProvenance ProvenanceFor(
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
                    "assembly-set API extraction"),
            AssemblySetSourceKind.Project =>
                AssemblyResolutionProvenance.Local(
                    "restored project asset"),
            AssemblySetSourceKind.Assembly =>
                AssemblyResolutionProvenance.Designated(
                    string.IsNullOrWhiteSpace(entry.Source)
                        ? entry.Path
                        : entry.Source),
            _ => AssemblyResolutionProvenance.Local(
                string.IsNullOrWhiteSpace(entry.Source)
                    ? "assembly-set API extraction"
                    : entry.Source),
        };

    public void Dispose() => _catalog.Dispose();

    sealed record ParticipantInput(
        string Path,
        AssemblyResolutionProvenance Provenance);

    sealed record Participant(
        string Path,
        ResolvedAssemblyReference Assembly,
        AssemblyDependencyResolver Policy);
}

/// <summary>
/// Describes one required assembly-set input that could not be acquired.
/// </summary>
/// <param name="Path">The diagnostic coordinate supplied for the input.</param>
/// <param name="Detail">The classified acquisition failure.</param>
public sealed record AssemblySetAcquisitionFailure(
    string Path,
    string Detail);
