namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Optional sink that records, per block container, why <see cref="StructuringPass"/>
/// left it flat (or that it structured cleanly). It exists only to make the
/// "forward branch to a common exit past the region" docket
/// (docs/design/control-flow-structuring.md) reproducible on demand: a normal
/// run leaves <see cref="PassContext.StructuringDiagnostics"/> null and the pass
/// records nothing, so there is zero behavioral or allocation cost outside the
/// <c>--structuring-bails</c> diagnostic.
///
/// <para>One sink is created per method run; each bailed container contributes
/// one reason and each structured container increments <see cref="Structured"/>.
/// The reason is the deepest direct cause: <c>Validate</c> records the first
/// (innermost) labelled bail it hits, so propagation up the recursion does not
/// overwrite it.</para>
/// </summary>
public sealed class StructuringDiagnostics
{
    readonly List<string> _bails = [];

    /// <summary>One reason string per container the pass left flat.</summary>
    public IReadOnlyList<string> Bails => _bails;

    /// <summary>The number of containers the pass structured into nested if/diamond regions.</summary>
    public int Structured { get; private set; }

    /// <summary>Record that a container was left flat for the given reason.</summary>
    public void RecordBail(string reason) => _bails.Add(reason);

    /// <summary>Record that a container structured cleanly.</summary>
    public void RecordStructured() => Structured++;
}
