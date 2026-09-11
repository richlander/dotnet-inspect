using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(
    "ILInspector.Analysis.AsyncSiblingFriendFixtures")]

namespace ILInspector.Analysis.AsyncSiblingFriendBaseFixtures;

public class ProtectedSiblingBase
{
    protected internal int Read() => 42;

    protected Task<int> ReadAsync()
        => Task.FromResult(42);

    public int PublicRead() => 42;

    public Task<int> PublicReadAsync()
        => Task.FromResult(42);

    internal int InternalRead() => 42;

    internal Task<int> InternalReadAsync()
        => Task.FromResult(42);
}

public sealed class FriendSiblingGrantor
{
    public int Read() => 42;

    internal Task<int> ReadAsync()
        => Task.FromResult(42);
}
