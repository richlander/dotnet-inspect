using System.Runtime.InteropServices;

namespace ILInspector.Analysis.Fixtures;

public static class RequestSetCollapseFixtures
{
    public static object CreateAnonymousValue() => new { Value = 1 };

    [DllImport("__dotnet_inspect_request_set_fixture__")]
    public static extern int Invoke(string value);
}
