using DotnetInspector.Libraries;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse;

public static partial class SourceHouse
{
    /// <summary>
    /// Opens a serial authored-source session that reuses one bounded assembly,
    /// API-surface, Portable PDB, and SourceLink preparation across member requests.
    /// </summary>
    public static AuthoredSession OpenAuthoredSession(
        LibraryReference library,
        LibraryContentReference selectedAssembly,
        SourceHouseLimits limits) =>
        new(
            library,
            selectedAssembly,
            limits);

    public sealed class AuthoredSession : IAsyncDisposable
    {
        private int _operationInProgress;
        private bool _disposed;

        internal AuthoredSession(
            LibraryReference library,
            LibraryContentReference selectedAssembly,
            SourceHouseLimits limits)
        {
            ArgumentNullException.ThrowIfNull(library);
            ArgumentNullException.ThrowIfNull(selectedAssembly);
            ArgumentNullException.ThrowIfNull(limits);

            Library = library;
            SelectedAssembly = selectedAssembly;
            Limits = limits;
        }

        internal LibraryReference Library { get; }
        internal LibraryContentReference SelectedAssembly { get; }
        internal SourceHouseLimits Limits { get; }
        internal DetachedInputs? Detached { get; set; }
        internal ResolvedAssemblyReference? Descriptor { get; set; }
        internal AssemblyInspectionSession? Inspection { get; set; }
        internal ApiSurface? Surface { get; set; }
        internal AuthoredTargetIndex? TargetIndex { get; set; }
        internal ApiSurface? CompilerGeneratedSurface { get; set; }
        internal AuthoredTargetIndex? CompilerGeneratedTargetIndex { get; set; }
        internal AuthoredSourcePreparation? SourcePreparation { get; set; }
        internal ProvisionalOutcome? Terminal { get; set; }

        public async ValueTask<SourceHouseOutcome> ExecuteAsync(
            SourceHouseAuthoredRequest request,
            LibraryOperationLease operationLease,
            CancellationToken cancellationToken = default)
        {
            EnterOperation();
            try
            {
                return await ExecuteAndCompleteAsync(
                        request,
                        operationLease,
                        this,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(
                    ref _operationInProgress,
                    0);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            if (Interlocked.CompareExchange(
                    ref _operationInProgress,
                    1,
                    0)
                != 0)
            {
                throw new InvalidOperationException(
                    "Cannot dispose an authored-source session while an operation is running.");
            }

            _disposed = true;
            Exception? sourceFailure = null;
            Exception? inspectionFailure = null;
            try
            {
                if (SourcePreparation?.Source.DisposeWithFailure()
                    is { } failure)
                {
                    sourceFailure = failure;
                }
            }
            finally
            {
                try
                {
                    Inspection?.Dispose();
                }
                catch (Exception exception)
                {
                    inspectionFailure = exception;
                }
                finally
                {
                    SourcePreparation = null;
                    Inspection = null;
                    Descriptor = null;
                    Surface = null;
                    TargetIndex = null;
                    CompilerGeneratedSurface = null;
                    CompilerGeneratedTargetIndex = null;
                    Detached = null;
                    Volatile.Write(
                        ref _operationInProgress,
                        0);
                }
            }

            if (sourceFailure is not null
                && inspectionFailure is not null)
            {
                throw new AggregateException(
                    "Authored-source session disposal failed.",
                    sourceFailure,
                    inspectionFailure);
            }
            if (sourceFailure is not null)
            {
                throw new IOException(
                    "Authored-source session SourceLink disposal failed.",
                    sourceFailure);
            }
            if (inspectionFailure is not null)
            {
                throw new IOException(
                    "Authored-source session metadata disposal failed.",
                    inspectionFailure);
            }

            return ValueTask.CompletedTask;
        }

        private void EnterOperation()
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);
            if (Interlocked.CompareExchange(
                    ref _operationInProgress,
                    1,
                    0)
                != 0)
            {
                throw new InvalidOperationException(
                    "Authored-source sessions execute one operation at a time.");
            }
        }
    }

    internal sealed class AuthoredSourcePreparation(
        SourceLinkService source,
        SourceLinkMapAudit map,
        SourceHousePdbContributionKind contributionKind,
        LibraryContentReference? companion,
        long portablePdbBytesObserved,
        SourceHouseWorkCharge work,
        IReadOnlyList<SourceHouseNativeObservation> observations)
    {
        internal SourceLinkService Source { get; } = source;
        internal SourceLinkMapAudit Map { get; } = map;
        internal SourceHousePdbContributionKind ContributionKind { get; } =
            contributionKind;
        internal LibraryContentReference? Companion { get; } = companion;
        internal long PortablePdbBytesObserved { get; } =
            portablePdbBytesObserved;
        internal SourceHouseWorkCharge Work { get; } = work;
        internal IReadOnlyList<SourceHouseNativeObservation> Observations { get; } =
            observations;
        internal FindingInspection<SourceDocumentObservation>? Documents { get; set; }
    }

    internal sealed class AuthoredTargetIndex
    {
        private readonly Dictionary<
            MetadataTypeDefinitionName,
            ApiType[]> _types;
        private readonly Dictionary<
            (MetadataTypeDefinitionName Type, int MetadataToken),
            ApiMember[]> _directMembers;
        private readonly Dictionary<
            (MetadataTypeDefinitionName Type, int MetadataToken),
            ApiMember[]> _accessors;

        internal AuthoredTargetIndex(ApiSurface surface)
        {
            _types = surface.Types
                .Where(
                    static type =>
                        type.DefinitionName is not null)
                .GroupBy(static type => type.DefinitionName)
                .ToDictionary(
                    static group => group.Key!,
                    static group => group.ToArray());
            _directMembers = surface.Types
                .SelectMany(
                    static type => type.Members
                        .Where(
                            member =>
                                type.DefinitionName
                                    is not null
                                && member.MetadataToken
                                    is not null)
                        .Select(
                            member => (
                                DefinitionName:
                                    type.DefinitionName!,
                                MetadataToken:
                                    member.MetadataToken
                                        .GetValueOrDefault(),
                                Member: member)))
                .GroupBy(
                    static entry => (
                        entry.DefinitionName,
                        entry.MetadataToken))
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(static entry => entry.Member)
                        .ToArray());
            _accessors = surface.Types
                .SelectMany(
                    static type => type.Members.SelectMany(
                        member => ApiMemberAccessors.Create(
                                member,
                                type)
                            .Where(
                                accessor =>
                                    type.DefinitionName
                                        is not null
                                    && accessor.MetadataToken
                                        is not null)
                            .Select(
                                accessor => (
                                    DefinitionName:
                                        type.DefinitionName!,
                                    MetadataToken:
                                        accessor.MetadataToken
                                            .GetValueOrDefault(),
                                    Member: accessor))))
                .GroupBy(
                    static entry => (
                        entry.DefinitionName,
                        entry.MetadataToken))
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(static entry => entry.Member)
                        .ToArray());
        }

        internal ApiType[] Types(
            MetadataTypeDefinitionName type) =>
            _types.GetValueOrDefault(type)
            ?? [];

        internal ApiMember[] DirectMembers(
            MetadataTypeDefinitionName type,
            int metadataToken) =>
            _directMembers.GetValueOrDefault(
                (type, metadataToken))
            ?? [];

        internal ApiMember[] Accessors(
            MetadataTypeDefinitionName type,
            int metadataToken) =>
            _accessors.GetValueOrDefault(
                (type, metadataToken))
            ?? [];
    }
}
