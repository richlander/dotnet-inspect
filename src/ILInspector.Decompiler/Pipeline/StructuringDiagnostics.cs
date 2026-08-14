namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Optional sink that records, per block container, why <see cref="StructuringPass"/>
/// left it flat, structured it cleanly, or accepted/declined a retained-label
/// region. It exists only to make the
/// "forward branch to a common exit past the region" docket
/// (docs/design/control-flow-structuring.md) reproducible on demand: a normal
/// run leaves <see cref="PassContext.StructuringDiagnostics"/> null and the pass
/// records nothing, so there is zero behavioral or allocation cost outside the
/// <c>--structuring-stops</c> diagnostic.
///
/// <para>One sink is created per method run; each stopped container contributes
/// one reason and each structured container increments <see cref="Structured"/>.
/// Retained-region attempts use stable reason codes in
/// <see cref="RetainedDeclines"/> and successful ranges increment
/// <see cref="RetainedRegions"/>.
/// The reason is the deepest direct cause: <c>Validate</c> records the first
/// (innermost) labelled stop it hits, so propagation up the recursion does not
/// overwrite it.</para>
/// </summary>
public sealed class StructuringDiagnostics
{
    readonly List<string> _stops = [];
    readonly List<string> _retainedDeclines = [];

    /// <summary>One reason string per container the pass left flat.</summary>
    public IReadOnlyList<string> Stops => _stops;

    /// <summary>The number of containers the pass structured into nested if/diamond regions.</summary>
    public int Structured { get; private set; }

    /// <summary>The number of forward regions structured while retaining their merge labels.</summary>
    public int RetainedRegions { get; private set; }

    /// <summary>Stable reason codes for retained-region candidates that were declined.</summary>
    public IReadOnlyList<string> RetainedDeclines => _retainedDeclines;

    /// <summary>Record that a container was left flat for the given reason.</summary>
    public void RecordStop(string reason) => _stops.Add(reason);

    /// <summary>Record that a container structured cleanly.</summary>
    public void RecordStructured() => Structured++;

    /// <summary>Record that one retained-label region structured successfully.</summary>
    public void RecordRetainedRegion() => RetainedRegions++;

    /// <summary>Record why one retained-label candidate was declined.</summary>
    public void RecordRetainedDecline(string reason) => _retainedDeclines.Add(reason);
}
