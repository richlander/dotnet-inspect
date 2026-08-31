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
    readonly IReadOnlyList<AcquisitionFailure> _acquisitionFailures;
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
        var failures = new List<AcquisitionFailure>();
        foreach (ParticipantInput input in inputs)
        {
            ResolvedAssemblyReference? assembly =
                TryCreateManagedAssembly(
                    input.Path,
                    input.Provenance,
                    out string? failure,
                    out CandidateOpenFailure? typedFailure);
            if (assembly is null)
            {
                failures.Add(
                    new AcquisitionFailure(
                        input.Path,
                        failure!,
                        typedFailure));
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
        foreach (AcquisitionFailure failure
            in _acquisitionFailures)
        {
            if (failure.TypedFailure is { } typedFailure)
            {
                merged.InspectionFailures.Add(
                    new ApiSurfaceInspectionFailure(
                        "acquire API surface",
                        0,
                        MetadataTypeNameFailureMechanism.Metadata,
                        AcquisitionFailureKind(typedFailure),
                        typedFailure.Detail)
                    {
                        SourceAssemblyPath = failure.Path,
                    });
                sink?.Invoke(
                    $"  ! {Path.GetFileName(failure.Path)}: "
                        + typedFailure.Detail);
                continue;
            }

            ApiSurface? moduleSurface =
                AssemblyReader.ExtractModuleApiSurface(
                    failure.Path,
                    includeAll);
            if (moduleSurface is not null)
            {
                MergeSurface(
                    merged,
                    moduleSurface,
                    failure.Path,
                    sink);
                readSurface = true;
                continue;
            }

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
        out string? failure,
        out CandidateOpenFailure? typedFailure)
    {
        try
        {
            ResolvedAssemblyReference? assembly =
                ResolvedAssemblyReference.CreateFromPathIfManaged(
                    path,
                    provenance);
            failure = assembly is null
                ? "The selected file does not contain managed metadata."
                : null;
            typedFailure = null;
            return assembly;
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            failure = ex.Message;
            typedFailure = new CandidateOpenFailure(
                CandidateOpenFailureKind.UnsupportedMetadataFormat,
                ex.Message);
            return null;
        }
        catch (MalformedMetadataRootException ex)
        {
            failure = ex.Message;
            typedFailure = new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                ex.Message)
            {
                MetadataRootReason = ex.Reason,
            };
            return null;
        }
        catch (OverflowException)
        {
            failure = "The selected assembly metadata is invalid.";
            typedFailure = new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                failure);
            return null;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or NotSupportedException
                or IndexOutOfRangeException)
        {
            failure =
                "The selected assembly could not be acquired.";
            typedFailure = null;
            return null;
        }
    }

    static string AcquisitionFailureKind(
        CandidateOpenFailure failure) =>
        failure.Kind switch
        {
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                nameof(UnsupportedMetadataFormatException),
            CandidateOpenFailureKind.InvalidImage
                when failure.MetadataRootReason is not null =>
                    nameof(MalformedMetadataRootException),
            _ => failure.Kind.ToString(),
        };

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
            _ => AssemblyResolutionProvenance.Local(
                string.IsNullOrWhiteSpace(entry.Source)
                    ? "assembly-set API extraction"
                    : entry.Source),
        };

    public void Dispose() => _catalog.Dispose();

    sealed record ParticipantInput(
        string Path,
        AssemblyResolutionProvenance Provenance);

    sealed record AcquisitionFailure(
        string Path,
        string Detail,
        CandidateOpenFailure? TypedFailure);

    sealed record Participant(
        string Path,
        ResolvedAssemblyReference Assembly,
        AssemblyDependencyResolver Policy);
}
