using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

// The generated wwwroot/inspect-web-engine.js module binds exports.InspectionEngine.*, so this
// type stays in the global namespace. Its helpers live in InspectWeb.Engine.
using InspectWeb.Engine;

/// <summary>
/// The operations this engine does not answer. Each keeps the exact signature the browser bridge
/// binds, so engine initialization still succeeds, and each throws a
/// <see cref="NotSupportedException"/> naming the workspace query that does not exist. None of
/// them returns an empty or success-shaped payload; the site catches the failure and displays it.
/// </summary>
/// <remarks>
/// <para>
/// These are product API gaps, not host shortcuts. Inspecting a participant requires a session or
/// its image snapshot, and <c>AssemblyContextGroup</c>'s access to both is internal to
/// <c>DotnetInspector.Queries</c> and its companion query assembly. A consumer therefore inspects
/// only through a public query that owns those lifetimes, and everything below waits for its own
/// query rather than opening a session, a metadata source, an analysis index, or a retained
/// descriptor.
/// </para>
/// <para>
/// The exact queries required are listed in
/// <c>prototypes/inspect-web/README.md</c> under "Required workspace queries", and each has a
/// tracking issue named there.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class InspectionEngine
{
    const string NoPlatformProjection =
        "no group-scoped product query projects this evidence from a platform participant";

    static NotSupportedException Unavailable(string operation, string capability) =>
        new($"{operation} is not available in this engine build: {capability}");

    [JSExport]
    public static Task<string> QueryPackagePerformance(
        string packageId,
        string version,
        string targetFramework) =>
        throw Unavailable(
            "Package performance",
            "no group-scoped query ranks assembly-wide Analysis evidence");

    [JSExport]
    public static Task<string> QueryPlatformPerformance(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Platform performance", NoPlatformProjection);

    /// <summary>
    /// <c>MetadataImageQuery</c> exists but binds to a host-opened
    /// <c>AssemblyInspectionSession</c>, so it is not reachable through group ownership.
    /// </summary>
    [JSExport]
    public static Task<string> QueryPackageMetadata(
        string packageId,
        string version,
        string targetFramework) =>
        throw Unavailable(
            "Package metadata",
            "MetadataImageQuery binds to a host-opened session, not to an assembly context group");

    [JSExport]
    public static Task<string> QueryPlatformMetadata(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Platform metadata", NoPlatformProjection);

    [JSExport]
    public static Task<string> QueryPackageMetadataTable(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        int tableIndex,
        int startRowId,
        int maxRows) =>
        throw Unavailable(
            "Package metadata table",
            "no group-scoped query projects metadata table windows");

    [JSExport]
    public static Task<string> QueryPlatformMetadataTable(
        string targetFramework,
        string assemblyFileName,
        string pack,
        int tableIndex,
        int startRowId,
        int maxRows) =>
        throw Unavailable("Platform metadata table", NoPlatformProjection);

    [JSExport]
    public static Task<string> QueryPackageHeapEntries(
        string packageId,
        string version,
        string targetFramework,
        string assemblyFileName,
        string heap) =>
        throw Unavailable(
            "Package heap entries",
            "no group-scoped query projects metadata heap entries");

    [JSExport]
    public static Task<string> QueryPlatformHeapEntries(
        string targetFramework,
        string assemblyFileName,
        string pack,
        string heap) =>
        throw Unavailable("Platform heap entries", NoPlatformProjection);

    [JSExport]
    public static Task<string> QueryMemberFacts(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string memberSignature) =>
        throw Unavailable(
            "Member facts",
            "no group-scoped query projects method-scoped Analysis evidence");

}
