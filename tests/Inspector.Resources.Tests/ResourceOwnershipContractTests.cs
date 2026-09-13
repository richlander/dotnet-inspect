using Inspector.Resources;

namespace Inspector.Resources.Tests;

public class ResourceOwnershipContractTests
{
    [Fact]
    public void ResourceOwnershipAttribute_DeclaresOneNonInheritedTypeRole()
    {
        AttributeUsageAttribute usage =
            typeof(ResourceOwnershipAttribute)
                .GetCustomAttributes(
                    typeof(AttributeUsageAttribute),
                    inherit: false)
                .Cast<AttributeUsageAttribute>()
                .Single();

        Assert.Equal(
            AttributeTargets.Class
                | AttributeTargets.Struct
                | AttributeTargets.Interface,
            usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void ConsumesResourceReceiverAttribute_DeclaresOneNonInheritedMethodRole()
    {
        AttributeUsageAttribute usage =
            typeof(ConsumesResourceReceiverAttribute)
                .GetCustomAttributes(
                    typeof(AttributeUsageAttribute),
                    inherit: false)
                .Cast<AttributeUsageAttribute>()
                .Single();

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void SnapshotView_IsStackOnlyAndExposesTheBorrowedResource()
    {
        Assert.True(typeof(ReadOnlyResourceSnapshotView<string>).IsByRefLike);

        var view = new ReadOnlyResourceSnapshotView<string>("resource");

        Assert.Equal("resource", view.Value);
    }

    [Fact]
    public void Snapshot_InvokesCallbackOnceAndReturnsItsDetachedResult()
    {
        var source = new TestSnapshotSource("resource");
        int calls = 0;

        int length = source.Snapshot(
            "prefix:",
            (snapshot, prefix) =>
            {
                calls++;
                Assert.Equal(1, source.ActiveBorrows);
                return (prefix + snapshot.Value).Length;
            });

        Assert.Equal("prefix:resource".Length, length);
        Assert.Equal(1, calls);
        Assert.Equal(0, source.ActiveBorrows);
    }

    [Fact]
    public void Snapshot_EndsBorrowBeforePropagatingCallbackFailure()
    {
        var source = new TestSnapshotSource("resource");
        var failure = new InvalidOperationException("callback failed");

        InvalidOperationException thrown = Assert.Throws<
            InvalidOperationException>(
            () => source.Snapshot<object?, object?>(
                state: null,
                (snapshot, state) =>
                {
                    Assert.Equal("resource", snapshot.Value);
                    Assert.Null(state);
                    Assert.Equal(1, source.ActiveBorrows);
                    throw failure;
                }));

        Assert.Same(failure, thrown);
        Assert.Equal(0, source.ActiveBorrows);
    }

    [Fact]
    public void Snapshot_RejectsUseAfterOwnerRelease()
    {
        var source = new TestSnapshotSource("resource");
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => source.Snapshot(
                state: 0,
                static (snapshot, state) => snapshot.Value.Length + state));
    }

    [ResourceOwnership]
    sealed class TestSnapshotSource(string value) :
        IResourceSnapshotSource<string>,
        IDisposable
    {
        bool _disposed;

        internal int ActiveBorrows { get; private set; }

        public TResult Snapshot<TState, TResult>(
            TState state,
            ResourceSnapshotCallback<string, TState, TResult> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            ObjectDisposedException.ThrowIf(_disposed, this);

            ActiveBorrows++;
            try
            {
                return callback(
                    new ReadOnlyResourceSnapshotView<string>(value),
                    state);
            }
            finally
            {
                ActiveBorrows--;
            }
        }

        public void Dispose() => _disposed = true;
    }
}
