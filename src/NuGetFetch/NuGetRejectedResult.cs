namespace NuGetFetch;

internal interface INuGetRejectedResult
{
    void Reject();
}

internal static class NuGetRejectedResult
{
    internal static void RejectIfOwned<T>(T result)
    {
        if (result is INuGetRejectedResult owned)
            owned.Reject();
    }
}
