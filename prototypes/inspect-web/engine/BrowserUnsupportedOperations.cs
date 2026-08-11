using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

// The bridge in wwwroot/engine.js binds exports.BrowserInspectionEngine.*, so this type stays
// in the global namespace. Its helpers live in InspectWeb.Engine.
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
public static partial class BrowserInspectionEngine
{
    const string NoPlatformWorkspace =
        "no product acquisition owner produces runtime-pack participants from content, so no "
        + "platform workspace can be opened in a browser";

    const string NoResearchSourceQuery =
        "no group-scoped query resolves SourceLink or decompiled whole-member source, and no "
        + "browser workspace acquires a symbol package";

    static NotSupportedException Unavailable(string operation, string capability) =>
        new($"{operation} is not available in this engine build: {capability}");

    /// <summary>
    /// Authored and decompiled whole-member source. No browser workspace acquires a symbol
    /// package, and <c>SourceLinkDocumentsQuery</c> and <c>PdbAcquisitionService</c> take
    /// filesystem paths rather than group participants.
    /// </summary>
    [JSExport]
    public static Task<string> QueryMemberSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string memberName,
        string memberSignature,
        string styleOptionsJson) =>
        throw Unavailable("Member source", NoResearchSourceQuery);

    /// <inheritdoc cref="QueryMemberSource"/>
    [JSExport]
    public static Task<string> QueryTypeSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeId,
        string styleOptionsJson) =>
        throw Unavailable("Type source", NoResearchSourceQuery);

    /// <inheritdoc cref="QueryMemberSource"/>
    [JSExport]
    public static Task<string> QueryTypeMemberSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeName,
        string memberName,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson) =>
        throw Unavailable("Type member source", NoResearchSourceQuery);

    /// <summary>
    /// Declared NuGet dependency groups plus the active assembly's direct references.
    /// <c>AssemblyReferencesQuery</c> exists but binds to a host-opened
    /// <c>AssemblyInspectionSession</c> rather than to an <c>AssemblyContextGroup</c>, and the
    /// .nuspec dependency-group projection has no query at all.
    /// </summary>
    [JSExport]
    public static Task<string> QueryPackageDependencies(
        string packageId,
        string version,
        string targetFramework,
        string assemblyId) =>
        throw Unavailable(
            "Package dependencies",
            "AssemblyReferencesQuery binds to a host-opened session, not to an assembly context "
            + "group, and no query projects declared dependency groups");

    [JSExport]
    public static Task<string> QueryPlatformIntegrations(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Platform integrations", NoPlatformWorkspace);

    [JSExport]
    public static Task<string> QueryPlatformOpportunities(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Platform opportunities", NoPlatformWorkspace);

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
        throw Unavailable("Platform performance", NoPlatformWorkspace);

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
        throw Unavailable("Platform metadata", NoPlatformWorkspace);

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
        throw Unavailable("Platform metadata table", NoPlatformWorkspace);

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
        throw Unavailable("Platform heap entries", NoPlatformWorkspace);

    [JSExport]
    public static Task<string> ExpandPlatformCallGraph(
        string targetFramework,
        string assembly,
        string typeFullName,
        string memberName,
        string selectorKey,
        int metadataToken) =>
        throw Unavailable("Platform call graph expansion", NoPlatformWorkspace);

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

    [JSExport]
    public static Task<string> LoadRuntimePack(string targetFramework) =>
        throw Unavailable("Runtime pack load", NoPlatformWorkspace);

    [JSExport]
    public static Task<string> LoadRuntimePackAssembly(
        string targetFramework,
        string assemblyFileName,
        string pack) =>
        throw Unavailable("Runtime pack assembly load", NoPlatformWorkspace);
}
