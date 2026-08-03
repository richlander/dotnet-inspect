using System.Reflection;
using SourceLinkFetch;

namespace DotnetInspector.Tests;

/// <summary>
/// Reports whether the test assembly's PDB carries a SourceLink blob.
/// </summary>
/// <remarks>
/// Several command tests inspect the test assembly itself and expect source-backed
/// sections — <c>Source Locations</c> above all — to return rows. Those rows exist only
/// when the build stamped SourceLink into the PDB, which is a property of the build
/// environment rather than of the product.
///
/// The environment can silently fail to supply it. Under a .NET 11 SDK,
/// <c>Microsoft.Build.Tasks.Git</c> looks for <c>packed-refs</c> in <c>$GIT_DIR</c>
/// instead of <c>$GIT_COMMON_DIR</c>; those are the same directory for a primary
/// checkout and different for a linked worktree, so a worktree whose branch ref has been
/// packed by <c>git gc</c> resolves no revision and the build emits no SourceLink —
/// with no warning and a green build. Fixed upstream by dotnet/sourcelink#1657, which
/// the .NET 11 SDK has not ingested yet. See issue #3658.
///
/// A test that hits this reports "produced no data", naming the symptom rather than the
/// cause, which reads exactly like a product regression. Assertions that depend on this
/// data append <see cref="FailureHint"/> so the real cause is named at the point of
/// failure. The tests still fail rather than skip: absent SourceLink is a broken
/// environment the developer can repair, not an unavailable optional tool, and skipping
/// would drop genuine coverage of every source-backed section.
/// </remarks>
internal static class TestAssemblySourceLink
{
    private static readonly Lazy<string?> LazyUnavailableReason = new(Probe);

    /// <summary>True when the test assembly's PDB carries a SourceLink blob.</summary>
    public static bool IsAvailable => LazyUnavailableReason.Value is null;

    /// <summary>Why SourceLink is unavailable, or null when it is available.</summary>
    public static string? UnavailableReason => LazyUnavailableReason.Value;

    /// <summary>
    /// A sentence to append to a failure message from a SourceLink-dependent assertion.
    /// Empty when SourceLink is present, so appending it is always safe.
    /// </summary>
    public static string FailureHint =>
        UnavailableReason is { } reason
            ? $" NOTE: this assertion needs SourceLink in the test assembly's PDB, and {reason}. " +
              "The build emits none when a linked worktree's branch ref lives only in .git/packed-refs " +
              "(see issue #3658); this is an environment defect, not a product regression. " +
              "Repair with `git update-ref refs/heads/$(git rev-parse --abbrev-ref HEAD) HEAD`, " +
              "and prevent recurrence with `git config gc.packRefs false`."
            : string.Empty;

    private static string? Probe()
    {
        var assemblyPath = typeof(TestAssemblySourceLink).Assembly.Location;
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            return "the test assembly has no readable file location";

        try
        {
            using var reader = SourceLinkReader.Open(assemblyPath);
            if (!reader.HasPdb)
                return "no PDB was found next to it";

            return reader.HasSourceLink ? null : "its PDB carries no SourceLink blob";
        }
        catch (Exception ex)
        {
            return $"its PDB could not be read ({ex.GetType().Name}: {ex.Message})";
        }
    }
}
