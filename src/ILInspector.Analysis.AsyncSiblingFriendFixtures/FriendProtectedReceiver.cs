using ILInspector.Analysis.AsyncSiblingFriendBaseFixtures;

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
}
