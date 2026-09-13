using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

sealed class ResearchPublicationBindingPolicy(
    int? blockAfterSelection = null,
    int? changeOnVersionRead = null) : IAssemblyBindingPolicy, IDisposable
{
    readonly ManualResetEventSlim _versionReadBlocked = new();
    readonly ManualResetEventSlim _continueVersionRead = new();
    AssemblyBindingPolicyVersion _version = new();
    int _blockNextVersionRead;
    int _selectionCount;
    int _versionReadCount;

    public AssemblyBindingPolicyVersion Version
    {
        get
        {
            int read = Interlocked.Increment(ref _versionReadCount);
            if (read == changeOnVersionRead)
                ReplaceVersion();

            // Model an in-flight read that remains associated with the old state
            // while another thread publishes the replacement.
            AssemblyBindingPolicyVersion version =
                Volatile.Read(ref _version);
            if (Interlocked.Exchange(ref _blockNextVersionRead, 0) != 0)
            {
                _versionReadBlocked.Set();
                if (!_continueVersionRead.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("The publication-drift probe was not released.");
            }
            return version;
        }
    }

    internal int SelectionCount => Volatile.Read(ref _selectionCount);

    public AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AssemblyBindingPolicyVersion version = Volatile.Read(ref _version);
        int selection = Interlocked.Increment(ref _selectionCount);
        if (selection == blockAfterSelection)
            Interlocked.Exchange(ref _blockNextVersionRead, 1);
        return new(
            version,
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable)));
    }

    internal bool WaitForVersionRead() =>
        _versionReadBlocked.Wait(TimeSpan.FromSeconds(30));

    internal void ReplaceVersion() =>
        Volatile.Write(ref _version, new AssemblyBindingPolicyVersion());

    internal void ContinueVersionRead() => _continueVersionRead.Set();

    public void Dispose()
    {
        _continueVersionRead.Set();
        _versionReadBlocked.Dispose();
        _continueVersionRead.Dispose();
    }
}
