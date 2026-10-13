namespace DotnetInspector.Diff;

/// <summary>
/// One typed, resource-free diff request (diff-system.md#request). The
/// operation alone resolves versions, opens points, pairs Libraries, and runs
/// the request's content.
/// </summary>
public abstract class DiffRequest
{
    private protected DiffRequest()
    {
    }
}

/// <summary>
/// A Diff History request, which the operation runs in its per-point census
/// mode: it evaluates each point on its own, then compares adjacent results.
/// </summary>
public abstract class DiffHistoryRequest : DiffRequest
{
    private protected DiffHistoryRequest(DiffHistoryCountRequest? count)
    {
        Count = count;
    }

    /// <summary>The Changed Versions Count to bind, when one was requested.</summary>
    public DiffHistoryCountRequest? Count { get; }
}
