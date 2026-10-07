using DotnetInspector.Libraries;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using Inspector.Findings;
using AuthoredSourceHouse = DotnetInspector.SourceHouse.SourceHouse;

namespace DotnetInspector.Queries;

public static partial class AssemblyContextSourceQuery
{
    /// <summary>
    /// Creates a lazy authored-member session for one assembly-context
    /// participant. The first valid request admits one Library and prepares one
    /// bounded SourceHouse session; later requests issue fresh operation leases
    /// while reusing that assembly, API, PDB, and SourceLink preparation.
    /// </summary>
    public static AssemblyMemberSourceSession OpenMemberSession(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextSourceQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(context);
        return new(group, participant, context);
    }

    public sealed class AssemblyMemberSourceSession : IAsyncDisposable
    {
        readonly AssemblyContextGroup _group;
        readonly AssemblyContextParticipant _participant;
        readonly AssemblyContextSourceQueryContext _context;
        readonly AssemblyContextSubject _subject;
        readonly AssemblyBindingPolicyVersion _bindingPolicyVersion;
        MemberTargetIndex? _targetIndex;
        AssemblyContextLibraryAdapterResult.Completed? _retainedLibrary;
        AuthoredSourceHouse.AuthoredSession? _authoredSession;
        AssemblyContextLibraryAdapterResult.Terminal? _libraryFailure;
        Exception? _pdbAcquisitionFailure;
        AssemblyPdbSourceProvenance? _provenance;
        bool _initializationSettled;
        bool _disposed;
        int _operationInProgress;

        internal AssemblyMemberSourceSession(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyContextSourceQueryContext context)
        {
            _group = group;
            _participant = participant;
            _context = context;
            _subject = new(participant.Assembly);
            _bindingPolicyVersion = group.BindingPolicyVersion;
        }

        public async Task<AssemblyMemberSourceEntry> ExecuteAsync(
            AssemblyMemberSourceRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Interlocked.CompareExchange(
                    ref _operationInProgress,
                    1,
                    comparand: 0)
                != 0)
            {
                throw new InvalidOperationException(
                    "An authored-member source session supports one operation at a time.");
            }

            try
            {
                return await ExecuteCoreAsync(
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _operationInProgress, 0);
            }
        }

        async Task<AssemblyMemberSourceEntry> ExecuteCoreAsync(
            AssemblyMemberSourceRequest request,
            CancellationToken cancellationToken)
        {
            AssemblyImageAccessResult<MemberInspectionSeed> access;
            try
            {
                access = _group.UseAssemblySession(
                    _participant,
                    cancellationToken,
                    (session, retained) =>
                    {
                        _targetIndex ??=
                            new MemberTargetIndex(
                                session
                                    .CompatibilityApiSurface(
                                        includeAll: true));
                        return new MemberInspectionSeed(
                            retained,
                            _targetIndex.Resolve(
                                session,
                                request));
                    });
                cancellationToken.ThrowIfCancellationRequested();
                EnsureBindingPolicyVersion(
                    _participant,
                    _bindingPolicyVersion);
            }
            catch (Exception ex) when (IsInspectionFailure(ex))
            {
                return new AssemblyMemberSourceEntry.Unavailable(
                    _subject,
                    request,
                    InspectionFailure(ex));
            }

            if (access
                is AssemblyImageAccessResult<
                    MemberInspectionSeed>.Rejected rejected)
            {
                return new AssemblyMemberSourceEntry.Rejected(
                    _subject,
                    request,
                    rejected.Failure);
            }
            if (access
                is not AssemblyImageAccessResult<
                    MemberInspectionSeed>.Available available)
            {
                throw new InvalidOperationException(
                    "Unknown assembly image access result.");
            }
            if (available.Value.Target is not { }
                && (request.AllowDecompiledFallback
                    || !RequiresCompilerGeneratedSurface(request)))
            {
                return new AssemblyMemberSourceEntry.Unavailable(
                    _subject,
                    request,
                    TargetNotFound(
                        "The selected participant does not declare the requested method."));
            }

            try
            {
                if (!_initializationSettled)
                {
                    MemberPdbInspection initialized =
                        await InspectMemberPdbAsync(
                                _group,
                                _participant,
                                request,
                                _context,
                                available.Value.Retained,
                                _bindingPolicyVersion,
                                _context.MemberSourceLimits,
                                _context.MemberSourceTimeout,
                                cancellationToken,
                                retainLibrary: true,
                                retainAuthoredSession: true,
                                retainedOperationLimits:
                                    _context.MemberDecompilationLimits)
                            .ConfigureAwait(false);
                    _retainedLibrary =
                        initialized.RetainedLibrary;
                    _authoredSession =
                        initialized
                            .RetainedAuthoredSession;
                    _libraryFailure =
                        initialized.LibraryFailure;
                    if (_retainedLibrary is null
                        && _libraryFailure is null)
                    {
                        _pdbAcquisitionFailure =
                            initialized.AcquisitionFailure
                            ?? throw new InvalidOperationException(
                                "The authored-member source session settled without a retained Library or a terminal failure.");
                    }
                    _provenance = initialized.Provenance;
                    _initializationSettled = true;
                    return CreateMemberSourceEntry(
                        _subject,
                        request,
                        initialized);
                }

                if (_libraryFailure is { } terminal)
                {
                    var findingSubject = new FindingSubject(
                        "member",
                        request.Member.Format(
                            MemberAnchorFormat.Qualified));
                    PdbMemberSourceInspection terminalInspection =
                        UnsuccessfulMemberInspection(
                            findingSubject,
                            terminal
                                is AssemblyContextLibraryAdapterResult
                                    .Incomplete
                                ? PdbMemberSourceOutcome
                                    .SourceLimitExceeded
                                : PdbMemberSourceOutcome
                                    .InspectionFailed,
                            AdmissionDetail(terminal),
                            failed: true);
                    return CreateMemberSourceEntry(
                        _subject,
                        request,
                        new(
                            terminalInspection,
                            Provenance: null)
                        {
                            LibraryFailure = terminal,
                        });
                }
                if (_pdbAcquisitionFailure is { } acquisitionFailure)
                {
                    var findingSubject = new FindingSubject(
                        "member",
                        request.Member.Format(
                            MemberAnchorFormat.Qualified));
                    PdbMemberSourceInspection terminalInspection =
                        PdbSourceInspectionProjection
                            .MemberAcquisitionFailed(
                                findingSubject,
                                acquisitionFailure);
                    return CreateMemberSourceEntry(
                        _subject,
                        request,
                        new(
                            terminalInspection,
                            Provenance: null)
                        {
                            AcquisitionFailure =
                                acquisitionFailure,
                        });
                }

                MemberPdbInspection inspection =
                    await ExecuteRetainedMemberAsync(
                            _participant,
                            request,
                            _context,
                            _retainedLibrary
                                ?? throw new InvalidOperationException(
                                    "The authored-member source session settled without a retained Library."),
                            _authoredSession
                                ?? throw new InvalidOperationException(
                                    "The authored-member source session settled without a reusable SourceHouse session."),
                            _bindingPolicyVersion,
                            _provenance,
                            cancellationToken)
                        .ConfigureAwait(false);
                return CreateMemberSourceEntry(
                    _subject,
                    request,
                    inspection);
            }
            catch (Exception ex) when (IsInspectionFailure(ex))
            {
                return new AssemblyMemberSourceEntry.Unavailable(
                    _subject,
                    request,
                    InspectionFailure(ex));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            if (Interlocked.CompareExchange(
                    ref _operationInProgress,
                    1,
                    comparand: 0)
                != 0)
            {
                throw new InvalidOperationException(
                    "The authored-member source session cannot be disposed while an operation is active.");
            }

            _disposed = true;
            Exception? primaryFailure = null;
            try
            {
                if (_authoredSession is { } authoredSession)
                {
                    try
                    {
                        await authoredSession
                            .DisposeAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure)
                    {
                        primaryFailure = failure;
                    }
                }
                if (_retainedLibrary is { } completed)
                {
                    await RetireSourceHouseLibraryAsync(
                            completed,
                            primaryFailure)
                        .ConfigureAwait(false);
                }
                if (primaryFailure is not null)
                    throw primaryFailure;
            }
            finally
            {
                _authoredSession = null;
                _retainedLibrary = null;
                _pdbAcquisitionFailure = null;
                _targetIndex = null;
                Volatile.Write(ref _operationInProgress, 0);
            }
        }
    }

    sealed class MemberTargetIndex
    {
        readonly Dictionary<
            MetadataTypeDefinitionName,
            ApiType[]> _types;
        readonly Dictionary<
            (MetadataTypeDefinitionName Type, int MetadataToken),
            ApiMember[]> _directMembers;
        readonly Dictionary<
            (MetadataTypeDefinitionName Type, int MetadataToken),
            ApiMember[]> _accessors;

        internal MemberTargetIndex(ApiSurface surface)
        {
            _types = surface.Types
                .Where(
                    static type =>
                        type.DefinitionName is not null)
                .GroupBy(
                    static type =>
                        type.DefinitionName)
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
                        .Select(
                            static entry =>
                                entry.Member)
                        .ToArray());
            _accessors = surface.Types
                .SelectMany(
                    static type => type.Members
                        .SelectMany(
                            member =>
                                ApiMemberAccessors
                                    .Create(
                                        member,
                                        type)
                                    .Where(
                                        accessor =>
                                            type.DefinitionName
                                                is not null
                                            && accessor
                                                .MetadataToken
                                                is not null)
                                    .Select(
                                        accessor => (
                                            DefinitionName:
                                                type.DefinitionName!,
                                            MetadataToken:
                                                accessor
                                                    .MetadataToken
                                                    .GetValueOrDefault(),
                                            Member:
                                                accessor))))
                .GroupBy(
                    static entry => (
                        entry.DefinitionName,
                        entry.MetadataToken))
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .Select(
                            static entry =>
                                entry.Member)
                        .ToArray());
        }

        internal (ApiType Type, ApiMember Member)?
            Resolve(
            AssemblyInspectionSession session,
            AssemblyMemberSourceRequest request)
        {
            ApiType[] types =
                _types.GetValueOrDefault(
                    request.Type)
                ?? [];
            if (types.Length != 1)
                return null;

            ApiType type = types[0];
            ApiMember[] direct =
                Matches(
                    session,
                    type,
                    request,
                    _directMembers
                        .GetValueOrDefault(
                            (
                                request.Type,
                                request.MetadataToken))
                    ?? []);
            if (direct.Length == 1)
                return (type, direct[0]);
            if (direct.Length > 1)
                return null;

            ApiMember[] accessors =
                Matches(
                    session,
                    type,
                    request,
                    _accessors
                        .GetValueOrDefault(
                            (
                                request.Type,
                                request.MetadataToken))
                    ?? []);
            return accessors.Length == 1
                ? (type, accessors[0])
                : null;
        }

        static ApiMember[] Matches(
            AssemblyInspectionSession session,
            ApiType type,
            AssemblyMemberSourceRequest request,
            IEnumerable<ApiMember> candidates) =>
        [
            .. candidates.Where(
                candidate =>
                    session.MethodAnchorMatches(
                        request.Type,
                        request.MetadataToken,
                        request.Member)
                    || ApiMemberIdentity
                        .GetMemberAnchor(
                            type,
                            candidate)
                        == request.Member),
        ];
    }

    static async Task<MemberPdbInspection>
        ExecuteRetainedMemberAsync(
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            AssemblyContextLibraryAdapterResult.Completed completed,
            AuthoredSourceHouse.AuthoredSession authoredSession,
            AssemblyBindingPolicyVersion bindingPolicyVersion,
            AssemblyPdbSourceProvenance? provenance,
            CancellationToken cancellationToken)
    {
        var findingSubject = new FindingSubject(
            "member",
            request.Member.Format(MemberAnchorFormat.Qualified));
        const string OperationName = "member-source";
        SourceHouseOperationPlanIdentity operationIdentity =
            SourceHouseOperationPlanIdentity.Create(OperationName);
        SourceHousePolicyGeneration policyGeneration =
            SourceHousePolicyGeneration.Create(
                $"{OperationName}-v1");
        var plan = new SourceHouseOperationPlan(
            operationIdentity,
            policyGeneration,
            context.MemberSourceLimits,
            DateTimeOffset.UtcNow.Add(
                context.MemberSourceTimeout),
            AssemblyContextSourceCapabilities.Create(context));
        var target = new SourceHouseTarget.MemberTarget(
            request.Type,
            request.Member,
            request.MetadataToken,
            request.IncludeAuthoredParts
                ? SourceHouseMemberSourceForm.DocumentParts
                : SourceHouseMemberSourceForm.DeclarationText);
        var houseRequest = new SourceHouseAuthoredRequest(
            SourceHouseRequestIdentity.Create(OperationName),
            completed.Reference,
            completed.Reference.ImplementationAssembly!,
            target,
            plan);
        if (completed.Owner.IssueOperationLease(
                completed.Reference)
            is not LibraryOperationLeaseIssueOutcome.Issued issued)
        {
            throw new InvalidOperationException(
                "The admitted Library could not issue its source operation lease.");
        }

        SourceHouseOutcome outcome =
            await authoredSession.ExecuteAsync(
                    houseRequest,
                    issued.Lease,
                    cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBindingPolicyVersion(
            participant,
            bindingPolicyVersion);

        PdbMemberSourceInspection inspection =
            ProjectMemberAuthored(
                outcome,
                findingSubject);
        return new(
            inspection,
            inspection.IsComplete
                ? provenance
                : null)
        {
            HouseOutcome = outcome,
            RetainedLibrary = completed,
        };
    }

    static AssemblyMemberSourceEntry CreateMemberSourceEntry(
        AssemblyContextSubject subject,
        AssemblyMemberSourceRequest request,
        MemberPdbInspection pdb)
    {
        if (pdb.Inspection.IsComplete
            && pdb.Inspection.Text is { } pdbText
            && pdb.Provenance is { } provenance)
        {
            return new AssemblyMemberSourceEntry.Available(
                subject,
                request,
                new AssemblyMemberSource.Pdb(
                    pdbText,
                    pdb.Inspection,
                    provenance)
                {
                    MemberDocument =
                        (pdb.HouseOutcome
                            as SourceHouseOutcome.Available)
                        ?.Source.MemberDocument,
                })
            {
                HouseOutcome = pdb.HouseOutcome,
                LibraryFailure = pdb.LibraryFailure,
            };
        }

        AssemblySourceFailure failure =
            request.IncludeAuthoredParts
                ? new(
                    AssemblySourceFailureKind
                        .AuthoredMemberPartsUnavailable,
                    "The requested verified authored member parts are unavailable.")
                : new(
                    AssemblySourceFailureKind
                        .AuthoredMemberUnavailable,
                    "The requested verified authored member source is unavailable.");
        return new AssemblyMemberSourceEntry.Unavailable(
            subject,
            request,
            failure,
            pdb.Inspection)
        {
            HouseOutcome = pdb.HouseOutcome,
            LibraryFailure = pdb.LibraryFailure,
        };
    }
}
