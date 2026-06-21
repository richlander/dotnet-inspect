namespace ILInspector.Decompiler;

/// <summary>
/// Fidelity of a decompilation result, ordered from worst to best
/// (docs/decompiler.md). The intermediate levels exist for the
/// pipeline's honest degradation when output is imperfect but usable.
/// </summary>
public enum DecompilationFidelity
{
    /// <summary>No output could be produced.</summary>
    Failed,

    /// <summary>No C# rendering; IL projections are still available.</summary>
    IlOnly,

    /// <summary>Structured control flow over low-level expressions.</summary>
    StructuredOnly,

    /// <summary>C# containing explicit unrepresentable nodes.</summary>
    Partial,

    /// <summary>Every construct raised; representable C#.</summary>
    Full,
}

/// <summary>
/// A diagnostic with a stable machine-readable identifier. Identifiers drive
/// fallback routing and CI triage, so they are stable across releases; the
/// message is for humans and carries no contract.
/// </summary>
public readonly record struct DecompilerDiagnostic(string Id, string Message)
{
    public override string ToString() => $"{Id}: {Message}";
}

/// <summary>Stable diagnostic identifiers. Never renumber or reuse.</summary>
public static class DiagnosticIds
{
    /// <summary>The pipeline threw while decompiling.</summary>
    public const string InternalError = "DEC0001";

    /// <summary>A method body context could not be created.</summary>
    public const string ContextUnavailable = "DEC0002";

    /// <summary>A projection that always has output for a method with a body produced none (e.g. IL views). Body-only C# projections legitimately render empty bodies and do not use this.</summary>
    public const string EmptyOutput = "DEC0003";

    /// <summary>IL the pipeline does not represent; rendered explicitly, lowers fidelity.</summary>
    public const string UnsupportedConstruct = "DEC0004";

    /// <summary>
    /// A type the slice cannot represent (a function pointer, a custom modifier,
    /// or an unspellable implementation-detail type) appears in a signature,
    /// local, or node — lowering fidelity. Distinct from
    /// <see cref="UnsupportedConstruct"/> (an opcode in a body): the cause is the
    /// type surface, not the instruction stream, and it otherwise carries no
    /// node-level diagnostic of its own.
    /// </summary>
    public const string UnsupportedType = "DEC0005";

    /// <summary>
    /// A bare function-pointer load (<c>ldftn</c>/<c>ldvirtftn</c>) that no pass
    /// consumed — it fed something other than a delegate constructor (a
    /// <c>calli</c>, a native callback registration). C# has no spelling for it,
    /// so it renders as a comment and lowers fidelity. The delegate-construction
    /// pattern is raised away before this is recorded; only the residue remains.
    /// </summary>
    public const string UnsupportedFunctionPointer = "DEC0006";

    /// <summary>
    /// A by-ref argument rendered against an unknown call-site ref-kind. The
    /// callee resolved as a MemberReference (a cross-assembly reference, or a
    /// same-assembly call on a generic type instance), which carries no
    /// parameter rows, so <c>out</c>/<c>in</c> cannot be distinguished from
    /// <c>ref</c>. The printer spells the managed-pointer argument with its
    /// default <c>ref</c> (or none), which is wrong for an <c>out</c>/<c>in</c>
    /// parameter (CS1620/CS1615) — an unverifiable spelling, so it lowers
    /// fidelity instead of claiming a faithful render.
    /// </summary>
    public const string UnverifiedByRefArgument = "DEC0007";

    /// <summary>
    /// An expression whose result type the pipeline could not determine (a join
    /// slot merged from conflicting types, an unresolved operand). The node
    /// renders, but the unknown type caps fidelity at
    /// <see cref="DecompilationFidelity.Partial"/>. Distinct from
    /// <see cref="UnsupportedType"/> (a known-but-unrepresentable type): here the
    /// type itself is unknown.
    /// </summary>
    public const string UnknownResultType = "DEC0008";

    /// <summary>
    /// A metadata type, method, field, property, or generic parameter name would
    /// render as invalid C# if emitted bare (for example a residual compiler-
    /// generated <c>&lt;&gt;c</c> holder or <c>&lt;M&gt;b__0_0</c> lambda method).
    /// The output is still useful, but cannot claim Full fidelity until the
    /// shape is raised or given a legal spelling.
    /// </summary>
    public const string UnrepresentableMetadataName = "DEC0009";

    /// <summary>
    /// A runtime token load (<c>ldtoken</c>) survived raising in a value position
    /// where C# has no expression spelling. Type tokens can render as
    /// <c>typeof(T)</c>; residual method and field tokens render only as comments
    /// and therefore cap fidelity until a pass consumes them.
    /// </summary>
    public const string UnsupportedRuntimeToken = "DEC0010";
}

/// <summary>
/// The result of a decompilation: output, diagnostics, and a fidelity level.
/// The Roslyn-shaped contract (<c>Compilation.Emit</c> returns an
/// <c>EmitResult</c>): failures are values, never silently swallowed and
/// never forced on callers as exceptions.
/// </summary>
public sealed record DecompilerResult(
    string? Output,
    DecompilationFidelity Fidelity,
    IReadOnlyList<DecompilerDiagnostic> Diagnostics)
{
    public bool Succeeded => Output is not null;

    /// <summary>
    /// For a constructor whose body opens with an explicit chain call, the C#
    /// initializer — <c>base(args)</c> or <c>this(args)</c> — lifted out of
    /// <see cref="Output"/> (a base/this call is valid only as an initializer
    /// on the signature, never as a body statement). The formatter that renders
    /// the signature places it; null when there is none.
    /// </summary>
    public string? ConstructorChain { get; init; }

    /// <summary>
    /// For a constructor, the field initializers (<c>field</c>, <c>value</c>
    /// pairs) lifted out of <see cref="Output"/> — <c>this.field = value</c>
    /// stores the IL placed before the base call, which C# spells on the field
    /// declaration, not in the body. The formatter that renders the field
    /// declarations places them; empty when there are none.
    /// </summary>
    public IReadOnlyList<(string Field, string Value)> FieldInitializers { get; init; } = [];

    /// <summary>
    /// A telemetry-free record of what the decompilation observed — its fidelity
    /// outcome, the symbol source it used, and its diagnostics — for a host to
    /// convert into its own diagnostics. Null for projections that do not build
    /// one (only the C# render entry points populate it today).
    /// </summary>
    public DecompilerTrace? Trace { get; init; }

    public static DecompilerResult Success(string output)
        => new(output, DecompilationFidelity.Full, []);

    public static DecompilerResult Failure(string diagnosticId, string message)
        => new(null, DecompilationFidelity.Failed, [new DecompilerDiagnostic(diagnosticId, message)]);

    /// <summary>
    /// Runs a pipeline entry point, converting exceptions into diagnostics.
    /// Empty-output policy is projection-specific: IL projections always have
    /// output for a real body, so emptiness is a failure there; a body-only
    /// C# projection legitimately renders an empty body (e.g. <c>void M() { }</c>).
    /// </summary>
    internal static DecompilerResult Run(Func<string> pipeline, bool emptyOutputIsFailure = false)
    {
        string output;
        try
        {
            output = pipeline();
        }
        catch (Exception ex)
        {
            return Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return emptyOutputIsFailure && string.IsNullOrWhiteSpace(output)
            ? Failure(DiagnosticIds.EmptyOutput, "projection produced no output for a method with a body")
            : Success(output);
    }
}
