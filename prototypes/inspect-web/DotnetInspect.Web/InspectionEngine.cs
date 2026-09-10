using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

// The generated wwwroot/inspect-web-host.js module binds exports.InspectionEngine.*, so this type
// stays in the global namespace. Its helpers live in DotnetInspect.Web.
using DotnetInspect.Web;

/// <summary>
/// The browser host's own exported surface: Browser/Wasm lifecycle, host configuration, and build
/// identity.
/// </summary>
/// <remarks>
/// <para>
/// Every inspection capability lives in its own export assembly and generated module. This host
/// assembly references all of them, declares the production facade set in
/// <see cref="InspectWebJsExportContext"/>, and owns the one <c>runEntryPoint()</c> the
/// application calls.
/// </para>
/// <para>
/// It inspects no assembly, opens no workspace, and publishes no capability result. Shared browser
/// policy — the MSDL proxy origin <see cref="ConfigureHost"/> configures — belongs to
/// <c>DotnetInspect.Web.Core</c> and is applied before the entry point starts application work.
/// </para>
/// </remarks>
namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
public static partial class InspectionEngine
{
    const uint ManagedCpuCanaryRounds = 150_000_000;

    /// <summary>
    /// A deterministic awaited operation used by the paired deployment smoke.
    /// </summary>
    [JSExport]
    public static async Task<string> AsyncLoweringCanary()
    {
        await Task.Yield();
        return "inspect-web-async-lowering-ok";
    }

    /// <summary>
    /// Fixed synchronous managed work used to prove that Worker placement isolates the DOM event
    /// loop. The returned checksum keeps the loop observable without using elapsed time to size it.
    /// </summary>
    [JSExport]
    public static string ManagedCpuCanary()
    {
        uint state = 0x811C9DC5;
        for (uint index = 0; index < ManagedCpuCanaryRounds; index++)
        {
            state = unchecked((state ^ (index + 0x9E3779B9)) * 0x01000193);
            state = (state << 7) | (state >> 25);
        }

        return state.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>Version, source revision, and build time embedded in this browser engine.</summary>
    [JSExport]
    public static string BuildIdentity() => JsonSerializer.Serialize(
        BrowserBuildIdentityReader.Read(typeof(InspectionEngine).Assembly),
        BrowserHostJsonContext.Default.BrowserBuildIdentity);

    /// <summary>
    /// Configures shared <c>DotnetInspect.Web.Core</c> host policy before the entry point starts
    /// application work.
    /// </summary>
    [JSExport]
    public static void ConfigureHost(string origin) =>
        BrowserPackageWorkspace.ConfigureMsdlProxy(origin);
}
