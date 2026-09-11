namespace Inspector.Resources;

/// <summary>
/// Declares that a type carries one resource ownership and release obligation.
/// </summary>
/// <remarks>
/// This attribute supplies metadata to resource lifecycle analysis. It does not
/// provide compiler-enforced move, borrow, or release semantics.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ResourceOwnershipAttribute : Attribute
{
}

/// <summary>
/// Declares that an instance method consumes ownership of its resource receiver.
/// </summary>
/// <remarks>
/// This attribute supplies metadata to resource lifecycle analysis. Current C#
/// does not invalidate the caller's receiver after the method returns.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ConsumesResourceReceiverAttribute : Attribute
{
}

/// <summary>
/// A stack-only read-only view of one resource during a snapshot callback.
/// </summary>
/// <typeparam name="TResource">The borrowed resource type.</typeparam>
public readonly ref struct ReadOnlyResourceSnapshotView<TResource>
    where TResource : notnull
{
    /// <summary>Creates a view over the resource borrowed by the caller.</summary>
    public ReadOnlyResourceSnapshotView(TResource value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    /// <summary>
    /// The borrowed resource. It is valid only for the synchronous callback
    /// invocation that supplied this view.
    /// </summary>
    public TResource Value { get; }
}

/// <summary>
/// Produces detached or independently owned data from one synchronous
/// read-only resource snapshot.
/// </summary>
/// <typeparam name="TResource">The borrowed resource type.</typeparam>
/// <typeparam name="TState">Caller-supplied callback state.</typeparam>
/// <typeparam name="TResult">The callback result type.</typeparam>
/// <param name="snapshot">The stack-only resource view.</param>
/// <param name="state">Caller-supplied callback state.</param>
/// <returns>Detached or independently owned callback output.</returns>
public delegate TResult ResourceSnapshotCallback<
    TResource,
    TState,
    TResult>(
    scoped ReadOnlyResourceSnapshotView<TResource> snapshot,
    TState state)
    where TResource : notnull;

/// <summary>
/// Supplies one owner-controlled synchronous read-only resource snapshot.
/// </summary>
/// <typeparam name="TResource">The borrowed resource type.</typeparam>
public interface IResourceSnapshotSource<TResource>
    where TResource : notnull
{
    /// <summary>
    /// Invokes <paramref name="callback"/> exactly once while the resource
    /// borrow is active, then ends the borrow before returning or propagating
    /// the callback failure.
    /// </summary>
    /// <typeparam name="TState">Caller-supplied callback state.</typeparam>
    /// <typeparam name="TResult">The callback result type.</typeparam>
    /// <param name="state">State supplied to the callback.</param>
    /// <param name="callback">The synchronous snapshot callback.</param>
    /// <returns>Detached or independently owned callback output.</returns>
    TResult Snapshot<TState, TResult>(
        TState state,
        ResourceSnapshotCallback<TResource, TState, TResult> callback);
}
