using ILInspector.Analysis.AsyncSiblingFriendBaseFixtures;
using System.Runtime.CompilerServices;

namespace ILInspector.Analysis.AsyncSiblingFriendFixtures;

public sealed class FriendProtectedReceiver
    : ProtectedSiblingBase
{
    public async Task<int> AnalyzeAsync(
        ProtectedSiblingBase other)
    {
        await Task.Yield();
        return other.Read();
    }

    public async Task<int> PublicAnalyzeAsync(
        ProtectedSiblingBase other)
    {
        await Task.Yield();
        return other.PublicRead();
    }

    public async Task<int> InternalAnalyzeAsync(
        ProtectedSiblingBase other)
    {
        await Task.Yield();
        return other.InternalRead();
    }
}

public static class FriendSiblingConsumer
{
    public static async Task<int> AnalyzeAsync(
        FriendSiblingGrantor grantor)
    {
        await Task.Yield();
        return grantor.Read();
    }
}

public static class MalformedAsyncSourceFixture
{
    [AsyncStateMachine(null!)]
    public static Task AnalyzeAsync()
        => Task.CompletedTask;
}
