using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Services;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// Invocation-owned explicit Find scope. Ordered sources are acquired lazily
/// and retained so classification passes reuse the same resolved evidence.
/// </summary>
internal sealed class ExplicitFindSearchWorkspace : IAsyncDisposable
{
    readonly AssemblySetInspectionWorkspace _workspace;
    readonly SourceScope[] _sources;

    internal ExplicitFindSearchWorkspace(
        FindOptions options,
        HttpClient httpClient,
        Action<string>? log,
        CancellationToken cancellationToken)
        : this(
            options,
            request => AssemblySetResolver.CollectAsync(
                httpClient,
                request with
                {
                    CancellationToken = cancellationToken,
                },
                log))
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    internal ExplicitFindSearchWorkspace(
        FindOptions options,
        Func<AssemblySetRequest, Task<AssemblySet>> collect)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(collect);
        _workspace = new(FindSourceCollector.CreateWorkspacePlan(options));
        _sources =
        [
            .. FindSourceCollector.BuildOrderedSourceRequests(options)
                .Select(request => new SourceScope(request, collect)),
        ];
    }

    internal async Task RunPerAssemblyAsync<TValue>(
        InspectionQuery<AssemblyContextResult<TValue>> query,
        Func<AssemblyContextGroup, AssemblyContextResult<TValue>> execute,
        Action<AssemblySetEntry, AssemblyContextEntry<TValue>> consume,
        Action<AssemblySetEntry, string> unavailable,
        Action markFailure,
        Func<bool>? stop = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(consume);
        ArgumentNullException.ThrowIfNull(unavailable);
        ArgumentNullException.ThrowIfNull(markFailure);

        foreach (SourceScope source in _sources)
        {
            if (stop?.Invoke() == true)
                return;

            AssemblySet assemblySet = await source.GetAsync()
                .ConfigureAwait(false);
            if (source.WriteDiagnostics())
            {
                AssemblySetDiagnosticWriter.Write(assemblySet);
                if (assemblySet.Diagnostics.Count > 0)
                    markFailure();
            }

            _workspace.RunPerAssembly(
                assemblySet,
                query,
                execute,
                consume,
                unavailable,
                stop);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _workspace.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            foreach (SourceScope source in _sources)
                source.Dispose();
        }
    }

    private sealed class SourceScope : IDisposable
    {
        readonly AssemblySetRequest _request;
        readonly Func<AssemblySetRequest, Task<AssemblySet>> _collect;
        AssemblySet? _assemblySet;
        bool _diagnosticsWritten;

        internal SourceScope(
            AssemblySetRequest request,
            Func<AssemblySetRequest, Task<AssemblySet>> collect)
        {
            _request = request;
            _collect = collect;
        }

        internal async Task<AssemblySet> GetAsync() =>
            _assemblySet ??=
                await _collect(_request).ConfigureAwait(false);

        internal bool WriteDiagnostics()
        {
            if (_diagnosticsWritten)
                return false;
            _diagnosticsWritten = true;
            return true;
        }

        public void Dispose() => _assemblySet?.Dispose();
    }
}
