using ILInspector.Decompiler.Pipeline;

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
/// message is for humans and carries no contract. Use
/// <see cref="DecompilerFindings.InspectFidelityCauses"/> for the complete
/// identity-bearing census of fidelity-lowering sites.
/// </summary>
public readonly record struct DecompilerDiagnostic(string Id, string Message)
{
    public override string ToString() => $"{Id}: {Message}";
}

/// <summary>
/// Product-owned decompiler rendering choices. Hosts and harnesses can inspect
/// the effective options instead of reverse-engineering taste decisions from
/// rendered text.
/// </summary>
public sealed record DecompilerOptions
{
    /// <summary>
    /// Local names without PDB evidence may render as synthesized readable names.
    /// Off by default so the shipped output remains IL/PDB-aligned.
    /// </summary>
    public bool ReadableLocalNames { get; init; }

    /// <summary>
    /// Framework type names may render in imported/simple form when the C# file
    /// shape supplies the namespace. This is the shipped taste choice.
    /// </summary>
    public bool PreferFrameworkTypeImports { get; init; } = true;

    /// <summary>
    /// Expression-bodied members keep the arrow on the declaration line by
    /// default; callers may opt into wrapping it onto the next line.
    /// </summary>
    public ExpressionBodyArrowPlacement ExpressionBodyArrowPlacement { get; init; } = ExpressionBodyArrowPlacement.SameLine;

    /// <summary>
    /// Long splittable expressions (short-circuit <c>&amp;&amp;</c>/<c>||</c>
    /// chains) may wrap one operand per continuation line instead of a single
    /// wide line. Off by default; a whitespace-only tiebreaker that leaves the
    /// tokens and IL unchanged.
    /// </summary>
    public bool WrapSplittableExpressions { get; init; }

    /// <summary>
    /// Instance fields accessed through <c>this</c> may render the explicit
    /// <c>this.</c> qualifier even where the bare name is unambiguous. Off by
    /// default; an IL-identical spelling choice. Mirrors
    /// <c>dotnet_style_qualification_for_field</c>.
    /// </summary>
    public bool QualifyFieldAccess { get; init; }

    /// <summary>
    /// Instance properties accessed through <c>this</c> may render the explicit
    /// <c>this.</c> qualifier even where the bare name is unambiguous. Off by
    /// default; an IL-identical spelling choice. Mirrors
    /// <c>dotnet_style_qualification_for_property</c>.
    /// </summary>
    public bool QualifyPropertyAccess { get; init; }

    public static DecompilerOptions Default { get; } = new();
}

/// <summary>
/// A typed explanation for an intentional decompiler rendering choice. This is
/// product evidence: harnesses may project it, but should not own the rule.
/// </summary>
public sealed record DecompilerDecision(
    string RuleId,
    string Category,
    string Subject,
    string Detail)
{
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}

public sealed record DecompilerResultMetadata(
    DecompilerOptions EffectiveOptions,
    IReadOnlyList<DecompilerDecision> Decisions)
{
    public static DecompilerResultMetadata Default { get; } = new(DecompilerOptions.Default, []);
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

    /// <summary>
    /// Exception filter machinery survived raising. C# has filter syntax
    /// (<c>catch (...) when (...)</c>) but no standalone expression or statement
    /// spelling for the raw <c>endfilter</c> IL boundary; left flat it renders as
    /// comments/gotos and must not claim Full fidelity.
    /// </summary>
    public const string UnsupportedExceptionFilter = "DEC0011";

    /// <summary>
    /// A <c>volatile.</c>-prefixed indirect access (<c>volatile. ldind</c>/
    /// <c>volatile. stind</c>) survived raising. The acquire/release ordering is
    /// real, but a bare <c>*p</c> dereference drops it and has no faithful
    /// plain-C# spelling (a volatile read/write through a pointer or by-ref is not
    /// expressible without <c>Volatile.Read</c>/<c>Volatile.Write</c>), so it must
    /// not claim Full fidelity. <c>volatile.</c> on a <em>field</em> access stays
    /// faithful — the volatility lives on the field declaration.
    /// </summary>
    public const string VolatileIndirectAccess = "DEC0012";

    /// <summary>
    /// A residual <c>continue</c> whose source-like spelling is not currently
    /// proven opcode-exact.
    /// </summary>
    public const string UnverifiedContinue = "DEC0013";

    /// <summary>
    /// A referenced <c>pinned</c> local survived without an owning
    /// <c>fixed</c> statement and has no faithful C# declaration spelling.
    /// </summary>
    public const string UnraisedPinnedLocal = "DEC0014";
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
    /// True when classic state-machine reconstruction installed an async body
    /// contract. The printer uses this to shape fallback returns. Final C# member
    /// modifiers are body-gated metadata facts owned by <c>ILInspector.CSharp</c>,
    /// not inferred from this projection result.
    /// </summary>
    public bool RequiresAsyncBodyModifier { get; init; }

    /// <summary>
    /// True when the rendered body contains operations that require an unsafe
    /// member context. Full-body consumers carry this typed projection fact to
    /// the C# declaration formatter instead of recovering it from rendered text.
    /// </summary>
    public bool RequiresUnsafeBodyModifier { get; init; }

    /// <summary>True when the rendered IR contains at least one recovered <c>await</c> expression.</summary>
    public bool ContainsAwaitExpression { get; init; }

    /// <summary>
    /// True when the whole body is exactly one multi-line single-statement
    /// expression — a <c>return &lt;expression&gt;;</c> or a single void
    /// <c>&lt;expression&gt;;</c> statement — a single wrapped expression with
    /// nothing else in the body. The member layer
    /// (<see cref="ILInspector.CSharp.CSharpMemberLayout"/>) consumes this typed
    /// structural fact to render the member expression-bodied
    /// (<c>head =&gt; &lt;expr&gt;;</c>) instead of a brace block wrapping the lone
    /// statement — a raised multi-line switch return (issue #3088) or any other
    /// wrapped single expression such as a fluent chain in return or void
    /// expression-statement position (issue #3084). It is a body-shape fact the
    /// printer proves from the emitted statement tree, so consumers never re-parse
    /// the rendered text to recover it.
    /// </summary>
    public bool BodyIsSingleExpressionBody { get; init; }

    /// <summary>
    /// A telemetry-free record of what the decompilation observed — its fidelity
    /// outcome, the symbol source it used, and its diagnostics — for a host to
    /// convert into its own diagnostics. Null for projections that do not build
    /// one (only the C# render entry points populate it today).
    /// </summary>
    public DecompilerTrace? Trace { get; init; }

    /// <summary>
    /// Product-owned metadata that explains intentional render choices. The
    /// holder keeps this evolving evidence off <see cref="DecompilerResult"/>'s
    /// historical record equality surface.
    /// </summary>
    public DecompilerResultMetadata Metadata { get; init; } = DecompilerResultMetadata.Default;

    /// <summary>The product options in force for this result.</summary>
    public DecompilerOptions EffectiveOptions => Metadata.EffectiveOptions;

    /// <summary>Product-owned decision evidence explaining intentional render choices.</summary>
    public IReadOnlyList<DecompilerDecision> Decisions => Metadata.Decisions;

    public static DecompilerResult Success(string output)
        => new(output, DecompilationFidelity.Full, []);

    public static DecompilerResult Failure(string diagnosticId, string message)
        => new(null, DecompilationFidelity.Failed, [new DecompilerDiagnostic(diagnosticId, message)]);

    public bool Equals(DecompilerResult? other)
        => other is not null
            && EqualityComparer<string?>.Default.Equals(Output, other.Output)
            && Fidelity == other.Fidelity
            && EqualityComparer<IReadOnlyList<DecompilerDiagnostic>>.Default.Equals(Diagnostics, other.Diagnostics)
            && EqualityComparer<string?>.Default.Equals(ConstructorChain, other.ConstructorChain)
            && EqualityComparer<IReadOnlyList<(string Field, string Value)>>.Default.Equals(FieldInitializers, other.FieldInitializers)
            && RequiresAsyncBodyModifier == other.RequiresAsyncBodyModifier
            && RequiresUnsafeBodyModifier == other.RequiresUnsafeBodyModifier
            && ContainsAwaitExpression == other.ContainsAwaitExpression
            && BodyIsSingleExpressionBody == other.BodyIsSingleExpressionBody
            && EqualityComparer<DecompilerTrace?>.Default.Equals(Trace, other.Trace);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Output);
        hash.Add(Fidelity);
        hash.Add(Diagnostics);
        hash.Add(ConstructorChain);
        hash.Add(FieldInitializers);
        hash.Add(RequiresAsyncBodyModifier);
        hash.Add(RequiresUnsafeBodyModifier);
        hash.Add(ContainsAwaitExpression);
        hash.Add(BodyIsSingleExpressionBody);
        hash.Add(Trace);
        return hash.ToHashCode();
    }

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
