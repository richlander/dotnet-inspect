using System.Runtime.Versioning;

namespace InspectWeb.ManagedOperationBridge.BrowserCanary.Host;

internal static class Program
{
    [SupportedOSPlatform("browser")]
    private static void Main()
    {
        Console.WriteLine(
            "Managed-operation bridge Browser/Wasm canary loaded: "
            + typeof(
                global::InspectWeb.ManagedOperationBridge.BrowserCanary.Exports)
                .Assembly
                .GetName()
                .Name);
    }
}
