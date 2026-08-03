using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Reports whether the test assembly's PDB — the one the product will actually accept —
/// carries a SourceLink blob.
/// </summary>
/// <remarks>
/// Several command tests inspect the test assembly itself and expect source-backed
/// sections to return rows. Those rows exist only when the build stamped SourceLink into
/// the PDB, which is a property of the build environment rather than of the product.
///
/// A test that hits an empty result reports "produced no data", naming the symptom rather
/// than the cause, which reads exactly like a product regression — the misdiagnosis
/// recorded in issue #3658. Assertions that depend on this data append
/// <see cref="FailureHint"/> so the environment is ruled in or out at the point of failure.
///
/// The probe deliberately goes through <see cref="PdbContext"/> rather than
/// <c>SourceLinkReader</c>: only <see cref="PdbContext"/> validates that the adjacent PDB
/// actually belongs to the assembly. A stale or foreign PDB carrying its own SourceLink map
/// would otherwise satisfy this probe while the product rejected it, suppressing the hint in
/// precisely the case that needs it.
///
/// The tests still fail rather than skip: absent SourceLink is a broken environment the
/// developer can repair, not an unavailable optional tool, and skipping would drop genuine
/// coverage of every source-backed section.
///
/// <see cref="TestAssemblySourceLinkTests"/> is the gate. It drives
/// <see cref="DescribeUnavailability"/> over a real available assembly and over two
/// constructed unavailable ones, so neither a probe stuck at "available" — which would
/// silently retire the diagnostic — nor one stuck at "unavailable" can survive.
/// </remarks>
internal static class TestAssemblySourceLink
{
    private static readonly Lazy<string?> LazyUnavailableReason =
        new(() => DescribeUnavailability(typeof(TestAssemblySourceLink).Assembly.Location));

    /// <summary>True when the PDB the product accepts for the test assembly carries SourceLink.</summary>
    public static bool IsAvailable => LazyUnavailableReason.Value is null;

    /// <summary>Why SourceLink is unavailable, or null when it is available.</summary>
    public static string? UnavailableReason => LazyUnavailableReason.Value;

    /// <summary>
    /// A sentence to append to a failure message from a SourceLink-dependent assertion.
    /// Empty when SourceLink is present, so appending it is always safe.
    /// </summary>
    /// <remarks>
    /// The probe observes only that SourceLink is absent; it does not establish why. The hint
    /// reports the observation as fact and names the known environmental cause as a candidate
    /// rather than asserting it.
    ///
    /// Diagnosis and repair deliberately live in issue #3658, not here. They are shell commands
    /// no test can execute, so nothing would catch them drifting, and the surface is unbounded:
    /// the correct advice varies by ref-storage backend, by whether the invocation is at the
    /// repository root, and by which of `gc.packRefs` and `maintenance.pack-refs.enabled` the
    /// repository honours. Six review rounds found six separate defects in that text and none in
    /// this code. A failing assertion needs to say what is missing and that the environment,
    /// not the product, is implicated; the issue can carry the recipes and stay current as git
    /// and the SDK move.
    /// </remarks>
    public static string FailureHint =>
        UnavailableReason is { } reason
            ? $" NOTE: this assertion needs SourceLink in the test assembly's PDB, and {reason}. "
              + "That implicates the build environment rather than a product regression, though "
              + "this check does not establish which cause applies. The known one under a .NET 11 "
              + "SDK is issue #3658: in a linked worktree, `Microsoft.Build.Tasks.Git` looks for "
              + "`packed-refs` and the reftable stack in `$GIT_DIR` while both live in "
              + "`$GIT_COMMON_DIR`, resolves no revision, and the build emits no SourceLink "
              + "without warning. See https://github.com/richlander/dotnet-inspect/issues/3658 to "
              + "confirm whether that is the cause here and to repair it. Other causes — a source "
              + "archive with no `.git`, or SourceLink explicitly disabled — need no repair."
            : string.Empty;

    /// <summary>
    /// Describes why <paramref name="assemblyPath"/> has no usable SourceLink, or null when it has.
    /// Takes a path so the gate can drive it over constructed unavailable cases.
    /// </summary>
    internal static string? DescribeUnavailability(string assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            return "the test assembly has no readable file location";

        List<string> log = [];
        try
        {
            using var context = PdbContext.Open(assemblyPath, log.Add);
            if (context.HasSourceLink)
                return null;

            if (context.HasPdb)
                return "its PDB carries no SourceLink blob";

            var mismatch = log.FirstOrDefault(entry =>
                entry.Contains("identity mismatch", StringComparison.OrdinalIgnoreCase));
            return mismatch is null
                ? "no PDB the product accepts was found next to it"
                : $"the adjacent PDB is not its own ({mismatch})";
        }
        catch (Exception ex)
        {
            return $"its PDB could not be read ({ex.GetType().Name}: {ex.Message})";
        }
    }
}
