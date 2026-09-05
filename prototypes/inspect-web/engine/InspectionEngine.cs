using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

// The generated wwwroot/inspect-web-host.js module binds exports.InspectionEngine.*, so this type
// stays in the global namespace. Its helpers live in InspectWeb.Engine.
using InspectWeb.Engine;

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
/// <c>InspectWeb.Engine.Core</c> and is applied before the entry point starts application work.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class InspectionEngine
{
    /// <summary>
    /// A deterministic awaited operation used by the paired deployment smoke.
    /// </summary>
    [JSExport]
    public static async Task<string> AsyncLoweringCanary()
    {
        await Task.Yield();
        return "inspect-web-async-lowering-ok";
    }

    /// <summary>Version, source revision, and build time embedded in this browser engine.</summary>
    [JSExport]
    public static string BuildIdentity() => JsonSerializer.Serialize(
        BrowserBuildIdentityReader.Read(typeof(InspectionEngine).Assembly),
        BrowserHostJsonContext.Default.BrowserBuildIdentity);

    /// <summary>
    /// Configures shared <c>InspectWeb.Engine.Core</c> host policy before the entry point starts
    /// application work.
    /// </summary>
    [JSExport]
    public static void ConfigureHost(string origin) =>
        BrowserPackageWorkspace.ConfigureMsdlProxy(origin);
}
