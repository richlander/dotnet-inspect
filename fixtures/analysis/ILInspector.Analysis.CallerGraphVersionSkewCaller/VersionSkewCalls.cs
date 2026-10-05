extern alias V1;
extern alias V2;

namespace ILInspector.Analysis.CallerGraphVersionSkewCaller;

public static class VersionSkewCalls
{
    public static void CallVersion1() =>
        V1::VersionSkewTarget.Api.Ping();

    public static void CallVersion2() =>
        V2::VersionSkewTarget.Api.Ping();
}
