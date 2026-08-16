using DotnetInspector.Core;

namespace DotnetInspector.Tests;

/// <summary>
/// Initializes the process-global core cache without repointing in-process command tests.
/// </summary>
static class TestCache
{
    /// <summary>
    /// Non-isolated tests share the production cache root used by the in-process CLI harness.
    /// Tests that need another root must use the serialized Console collection.
    /// </summary>
    /// <remarks>
    /// Gate: <see cref="CommandExecutionTests.LibraryCommand_DiscoverEffective_GroupsSourceLinkUnderSourceLinkDoor"/>
    /// reinitializes through this method between symbol-cache warming and discovery.
    /// </remarks>
    public static void InitializeSharedCore() =>
        CoreCache.Initialize("dotnet-inspect");
}
