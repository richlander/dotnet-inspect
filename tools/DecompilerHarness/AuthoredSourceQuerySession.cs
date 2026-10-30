using System.Runtime.ExceptionServices;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

sealed class AuthoredSourceQuerySession : IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly AssemblyContextGroup _group;
    readonly AssemblyContextSourceQuery
        .AssemblyMemberSourceSession _session;
    readonly IReadOnlyDictionary<int, AssemblyMemberSourceRequest>
        _requestsByMethodToken;
    bool _disposed;

    AuthoredSourceQuerySession(
        InspectionWorkspace workspace,
        AssemblyContextGroup group,
        AssemblyContextSourceQuery
            .AssemblyMemberSourceSession session,
        IReadOnlyDictionary<int, AssemblyMemberSourceRequest>
            requestsByMethodToken)
    {
        _workspace = workspace;
        _group = group;
        _session = session;
        _requestsByMethodToken = requestsByMethodToken;
    }

    internal static AuthoredSourceQuerySession Open(
        string assemblyPath,
        HttpClient symbolClient,
        SourceFetch sourceFetch,
        IReadOnlyList<string>? repositoryPaths = null,
        string? packageName = null,
        string? packageVersion = null,
        IPdbStore? pdbStore = null,
        NuGetSourceOptions? sourceOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(symbolClient);
        ArgumentNullException.ThrowIfNull(sourceFetch);

        IPdbStore effectivePdbStore =
            pdbStore
            ?? new InMemoryPdbStore();
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                packageName is not null
                    && packageVersion is not null
                    ? AssemblyResolutionProvenance.Package(
                        packageName,
                        packageVersion,
                        tfm: null,
                        rid: null)
                    : AssemblyResolutionProvenance.Local(
                        "decompiler authored-source query"));
        var bindingPolicy =
            new AssemblyDependencyResolver(
                new AssemblyDependencyResolutionOptions(
                    assemblyPath)
                {
                    PreferImplementationAssemblies = true,
                    AllowPlatformAssemblyVersionRollForward =
                        true,
                });
        var participant =
            new AssemblyContextParticipant(
                assembly,
                bindingPolicy);
        var workspace = new InspectionWorkspace();
        AssemblyContextGroup? group = null;
        AssemblyContextSourceQuery
            .AssemblyMemberSourceSession? session = null;
        try
        {
            group =
                workspace.CreateAssemblyContextGroup(
                    [participant]);
            var context =
                new AssemblyContextSourceQueryContext(
                    symbolClient,
                    effectivePdbStore,
                    new SourcePolicyPackageSourceAuthorization(
                        sourceOptions),
                    sourceFetch)
                {
                    RepositoryPaths = repositoryPaths,
                    NuGetSourceOptions = sourceOptions,
                    PdbFallbackPackage =
                        packageName is not null
                        && packageVersion is not null
                            ? new PackageCoordinate(
                                packageName,
                                packageVersion)
                            : null,
                    AllowLocalSourceReads = true,
                    AllowAdjacentPdbReads = true,
                };
            session =
                AssemblyContextSourceQuery.OpenMemberSession(
                    group,
                    participant,
                    context);
            return new(
                workspace,
                group,
                session,
                BuildRequests(assembly));
        }
        catch (Exception failure)
        {
            List<Exception> failures = [failure];
            if (session is not null)
            {
                try
                {
                    session.DisposeAsync()
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception cleanup)
                {
                    failures.Add(cleanup);
                }
            }
            try
            {
                group?.Dispose();
            }
            catch (Exception cleanup)
            {
                failures.Add(cleanup);
            }
            try
            {
                workspace.DisposeAsync()
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception cleanup)
            {
                failures.Add(cleanup);
            }

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failure).Throw();
            throw new AggregateException(failures);
        }
    }

    internal async Task<PdbMemberSourceInspection> AcquireAsync(
        int metadataToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_requestsByMethodToken.TryGetValue(
                metadataToken,
                out AssemblyMemberSourceRequest? request))
        {
            throw new InvalidOperationException(
                $"MethodDef token 0x{metadataToken:X8} has no metadata-issued member identity.");
        }

        AssemblyMemberSourceEntry entry =
            await _session.ExecuteAsync(
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
        return entry switch
        {
            AssemblyMemberSourceEntry.Available
            {
                Source: AssemblyMemberSource.Pdb pdb,
            } => pdb.Inspection,
            AssemblyMemberSourceEntry.Unavailable
            {
                PdbAttempt: { } attempt,
            } => attempt,
            AssemblyMemberSourceEntry.Unavailable unavailable =>
                throw new IOException(
                    unavailable.Failure.Detail,
                    unavailable.Failure.Error),
            AssemblyMemberSourceEntry.Rejected rejected =>
                throw new IOException(
                    rejected.Failure.Detail),
            _ => throw new InvalidOperationException(
                "Authored-only member settlement returned an unexpected source shape."),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        List<Exception> failures = [];
        try
        {
            await _session.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            _group.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            await _workspace.DisposeAsync()
                .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException(failures);
    }

    static IReadOnlyDictionary<int, AssemblyMemberSourceRequest>
        BuildRequests(
            ResolvedAssemblyReference assembly)
    {
        using AssemblyInspectionSession inspection =
            AssemblyInspectionSession.Open(assembly);
        ApiSurface surface =
            inspection.CompatibilityApiSurface(
                includeAll: true);
        var requests =
            new Dictionary<int, AssemblyMemberSourceRequest>();
        foreach (ApiType type in surface.Types)
        {
            foreach (ApiMember member in type.Members)
            {
                Add(member);
                foreach (ApiMember accessor
                    in ApiMemberAccessors.Create(
                        member,
                        type))
                {
                    Add(accessor);
                }

                void Add(ApiMember requestMember)
                {
                    if (requestMember.MetadataToken
                        is not { } methodToken)
                    {
                        return;
                    }
                    AssemblyMemberSourceRequest request =
                        AssemblyMemberSourceRequest.From(
                                type,
                                requestMember)
                            .WithoutDecompiledFallback();
                    if (requests.TryGetValue(
                            methodToken,
                            out AssemblyMemberSourceRequest? existing)
                        && (existing.Type != request.Type
                            || existing.Member
                                != request.Member))
                    {
                        throw new InvalidDataException(
                            $"MethodDef token 0x{methodToken:X8} maps to multiple API members.");
                    }
                    requests[methodToken] = request;
                }
            }
        }

        return requests;
    }
}
