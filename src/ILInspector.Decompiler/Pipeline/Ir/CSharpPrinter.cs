using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpText;
using ILInspector.CSharp;
using ILInspector.ControlFlow;
using ILInspector.Metadata;
using Inspector.Text;
using static ILInspector.Decompiler.Pipeline.PointerArithmetic;
using static ILInspector.Decompiler.Pipeline.PlaceIdentity;
using DecisionKey = (string RuleId, string Category, string Subject, string Detail, string OldValue, string NewValue, string DedupDiscriminator);

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// First C# projection of the IR: honest lowered output. Structure that has
/// not been raised renders as what it is — flat blocks with labels and
/// gotos — never as guessed sugar. Formatting is deliberately plain (bare
/// this-members, V_N/S_N names, trimmed trailing return) so a diff over the
/// output measures structural distance, not whitespace noise. The raising
/// passes close the goto gap from here; this printer is where completeness
/// starts.
/// </summary>
public sealed partial class CSharpPrinter
{
    readonly IrFunction _function;

    /// <summary>
    /// True when the source module opts into the updated memory-safety rules
    /// (see <see cref="IrFunction.UsesUpdatedMemorySafetyRules"/>). When set, the
    /// printer wraps unsafe operations in explicit <c>unsafe { }</c> blocks.
    /// </summary>
    readonly bool _newMemorySafetyRules;
    readonly bool _containsAwaitSyntax;
    readonly bool _fullyQualifyTypeNames;

    /// <summary>
    /// True when the method body skips locals initialization (<see
    /// cref="IrFunction.SkipLocalsInit"/>) — the extra condition that makes a
    /// <c>stackalloc</c>-to-<c>Span</c> conversion unsafe under the new rules.
    /// </summary>
    readonly bool _skipLocalsInit;

    /// <summary>
    /// Nesting depth of emitted <c>unsafe { }</c> blocks. Non-zero means the
    /// current statements are already in an unsafe context, so inner operations
    /// are not wrapped again (no redundant nested blocks).
    /// </summary>
    int _unsafeDepth;

    /// <summary>
    /// Indentation level of the statement currently being emitted by <see
    /// cref="AppendStatement"/>. A multi-statement lambda block body found while
    /// rendering that statement's expression tree (however deeply nested inside
    /// argument lists, assignments, etc.) expands its braces to this level rather
    /// than staying on one line, matching how every other statement block prints.
    /// </summary>
    int _statementIndent;

    readonly PrinterOptions _options;
    readonly HashSet<string> _reservedScopeNames;
    readonly HashSet<string> _capturedScopeNames;
    readonly List<DecompilerDecision> _decisions;
    readonly HashSet<DecisionKey> _decisionKeys;
    readonly List<ConsumedMemberEvidence> _consumedMembers = [];

    CSharpPrinter(
        IrFunction function,
        PrinterOptions? options = null,
        IEnumerable<string>? reservedScopeNames = null,
        List<DecompilerDecision>? decisions = null,
        HashSet<DecisionKey>? decisionKeys = null,
        bool fullyQualifyTypeNames = false)
    {
        _function = function;
        _options = options ?? PrinterOptions.Default;
        _fullyQualifyTypeNames = fullyQualifyTypeNames;
        _newMemorySafetyRules = function.UsesUpdatedMemorySafetyRules;
        _containsAwaitSyntax = UnsafeAwaitOperand.ContainsAwait(function);
        _skipLocalsInit = function.SkipLocalsInit;
        _reservedScopeNames = reservedScopeNames is null
            ? []
            : new HashSet<string>(reservedScopeNames, StringComparer.Ordinal);
        if (function.HasAccessorStorageBinding)
            _reservedScopeNames.Add("field");
        _capturedScopeNames = new HashSet<string>(
            CSharpSpellability
                .ExternalArgumentNamesInScope(
                    function,
                    function.Signature.Parameters)
                .Where(_reservedScopeNames.Contains),
            StringComparer.Ordinal);
        _decisions = decisions ?? [];
        _decisionKeys = decisionKeys ?? [];
    }

    // The output-path pass context: stepping off, plus the optional cross-method
    // import seam so a pass can reach a sibling body (lambda raising) and the
    // optional type-disjointness oracle a disjointness-gated raise needs.
    static PassContext RaiseContext(
        Func<MethodRef, IrFunction?>? importMethodBody,
        Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint = null)
        => importMethodBody is null && typesProvablyDisjoint is null
            ? PassContext.None
            : new PassContext(new Stepper(enabled: false), importMethodBody: importMethodBody, typesProvablyDisjoint: typesProvablyDisjoint);

    /// <summary>The product path: runs the default raising passes, then prints. <see cref="Print"/> alone renders whatever tree it is given — right for stage dumps, wrong for output paths.</summary>
    public static DecompilerResult PrintRaised(IrFunction function)
        => PrintRaised(function, importMethodBody: null);

    /// <summary>As <see cref="PrintRaised(IrFunction)"/>, with <paramref name="importMethodBody"/> wiring the cross-method import seam (e.g. for lambda raising); null leaves cross-method passes as no-ops. <paramref name="typesProvablyDisjoint"/> wires the type-disjointness oracle (from the assembly's open metadata) a disjointness-gated raise needs; null makes those raises conservatively decline. <paramref name="options"/> defaults to the shipped output.</summary>
    public static DecompilerResult PrintRaised(
        IrFunction function,
        Func<MethodRef, IrFunction?>? importMethodBody,
        PrinterOptions? options = null,
        Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint = null)
        => PrintRaisedCore(
            function,
            importMethodBody,
            options,
            typesProvablyDisjoint,
            fullyQualifyTypeNames: false);

    /// <summary>
    /// Runs the product raising path and prints its body with fully qualified
    /// type names for composition into a host-owned declaration artifact.
    /// </summary>
    public static DecompilerResult PrintRaisedFullyQualified(
        IrFunction function,
        Func<MethodRef, IrFunction?>? importMethodBody,
        PrinterOptions? options = null,
        Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint = null)
        => PrintRaisedCore(
            function,
            importMethodBody,
            options,
            typesProvablyDisjoint,
            fullyQualifyTypeNames: true);

    static DecompilerResult PrintRaisedCore(
        IrFunction function,
        Func<MethodRef, IrFunction?>? importMethodBody,
        PrinterOptions? options,
        Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint,
        bool fullyQualifyTypeNames)
    {
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        List<DecompilerDecision> appliedLenses;
        try
        {
            appliedLenses = RaiseWithStyleLenses(function, importMethodBody, options, typesProvablyDisjoint);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return WithAppliedLenses(
            Print(function, options, fullyQualifyTypeNames),
            appliedLenses);
    }

    /// <summary>
    /// Runs the default raising pipeline, then any opt-in byte-divergent style
    /// lens, and returns the decisions for the lenses that actually rewrote.
    /// Shared by both <see cref="PrintRaised(IrFunction, Func{MethodRef, IrFunction}, PrinterOptions, Func{TypeRef, TypeRef, bool})"/>
    /// and the statement-line-map overload the annotated view uses, so the two
    /// paths raise and apply taste identically instead of drifting apart.
    /// </summary>
    static List<DecompilerDecision> RaiseWithStyleLenses(
        IrFunction function,
        Func<MethodRef, IrFunction?>? importMethodBody,
        PrinterOptions? options,
        Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint)
    {
        var appliedLenses = new List<DecompilerDecision>();
        var context = RaiseContext(importMethodBody, typesProvablyDisjoint);
        IrPasses.Run(function, IrPasses.Default, context);
        // Opt-in tier-3 style lens (#3138), byte-divergent by design: runs
        // only when requested, after the byte-faithful default pipeline has
        // left the guarded bool return flat. The oracle-endorsed ternary runs
        // first so that, when both lenses are enabled, it wins the shared shape
        // (it consumes the guarded return, leaving the branchless pass a no-op).
        // When a lens actually rewrites, record a byte-divergent applied-lens
        // decision so a host can surface that the render is no longer
        // opcode-faithful (the #3127 signal) instead of inferring it from prose.
        if (options?.PreferConditionalExpressionReturn == true)
        {
            var pass = new PreferConditionalReturnPass();
            pass.Run(function, context);
            if (pass.Rewrites > 0)
                appliedLenses.Add(new DecompilerDecision(
                    "style-lens.prefer-conditional-return",
                    DecompilerDecisionCategories.StyleLens,
                    function.Name,
                    "Rewrote a guarded boolean return as a conditional expression "
                        + "(dotnet_style_prefer_conditional_expression_over_return, IDE0046). "
                        + "Behavior-preserving but byte-divergent: the render no longer "
                        + "reproduces the original branch opcodes.")
                {
                    OldValue = "if (c) return A; return B;",
                    NewValue = "return c ? A : B;",
                });
        }
        if (options?.PreferBranchlessBoolean == true)
        {
            var pass = new PreferBranchlessBooleanPass();
            pass.Run(function, context);
            if (pass.Rewrites > 0)
                appliedLenses.Add(new DecompilerDecision(
                    "style-lens.prefer-branchless-boolean",
                    DecompilerDecisionCategories.StyleLens,
                    function.Name,
                    "Folded a guarded boolean return into the compact short-circuit "
                        + "\"bool hack\" (dotnet_inspect_style_prefer_branchless_boolean; not "
                        + "oracle-endorsed). Behavior-preserving but byte-divergent: the render "
                        + "no longer reproduces the original branch opcodes.")
                {
                    OldValue = "if (c) return false; return B;",
                    NewValue = "return !c && B;",
                });
        }
        return appliedLenses;
    }

    static DecompilerResult WithAppliedLenses(DecompilerResult result, List<DecompilerDecision> appliedLenses)
        => appliedLenses.Count == 0 || result.Output is null
            ? result
            : result with
            {
                Metadata = result.Metadata with
                {
                    Decisions = [.. result.Metadata.Decisions, .. appliedLenses],
                },
            };

    /// <summary>
    /// The product path with a printed-range map: same output as
    /// <see cref="PrintRaised(IrFunction)"/>, plus a record of which characters
    /// of that output each statement node emitted. Line- and range-anchored
    /// overlays (the annotated C# view) splice onto those positions; the printer
    /// itself stays annotation-agnostic. The map is empty on failure.
    /// </summary>
    public static DecompilerResult PrintRaised(IrFunction function, out PrintedRangeMap printedRanges)
        => PrintRaised(function, out printedRanges, importMethodBody: null);

    /// <inheritdoc cref="PrintRaised(IrFunction, out PrintedRangeMap)"/>
    /// <remarks>
    /// <paramref name="options"/> applies the same taste as the plain
    /// <see cref="PrintRaised(IrFunction, Func{MethodRef, IrFunction}, PrinterOptions, Func{TypeRef, TypeRef, bool})"/>
    /// path, so a line-anchored overlay never renders a different spelling than
    /// the Source view for the same member. When a byte-divergent style lens
    /// actually rewrites, the result carries a
    /// <see cref="DecompilerDecisionCategories.StyleLens"/> decision: the render
    /// no longer reproduces the original opcodes, so an overlay that interleaves
    /// raw IL must consult those decisions rather than presenting the two as
    /// corresponding.
    /// </remarks>
    public static DecompilerResult PrintRaised(
        IrFunction function, out PrintedRangeMap printedRanges, Func<MethodRef, IrFunction?>? importMethodBody,
        Func<TypeRef, TypeRef, bool>? typesProvablyDisjoint = null,
        PrinterOptions? options = null)
    {
        printedRanges = PrintedRangeMap.Empty;
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        List<DecompilerDecision> appliedLenses;
        try
        {
            appliedLenses = RaiseWithStyleLenses(function, importMethodBody, options, typesProvablyDisjoint);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var sink = new PrintedRangeMap();
            var printer = new CSharpPrinter(function, options)
            {
                _printedRanges = sink,
                _printedRangeMetadata = sink,
                _expressionText = [],
            };
            string output = printer.PrintBody(function);
            printedRanges = sink.Complete(output);
            return WithAppliedLenses(printer.Result(output, function), appliedLenses);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the <see cref="IrPasses.Lowered"/> pipeline (the default minus the
    /// cosmetic statement-sugar passes), then prints — the lowered-C# view
    /// (issue #636). Like <see cref="PrintRaised"/> this is an output path, so it
    /// owns the pass run; the result is valid, recompilable C# at a lower
    /// altitude than the shipped output.
    /// </summary>
    public static DecompilerResult PrintLowered(IrFunction function)
        => PrintLowered(function, importMethodBody: null);

    /// <summary>As <see cref="PrintLowered(IrFunction)"/>, with <paramref name="importMethodBody"/> wiring the cross-method import seam for non-cosmetic lowered passes such as lambda, local-function, and iterator reconstruction.</summary>
    public static DecompilerResult PrintLowered(IrFunction function, Func<MethodRef, IrFunction?>? importMethodBody)
    {
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        try
        {
            IrPasses.Run(function, IrPasses.Lowered, RaiseContext(importMethodBody));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return Print(function);
    }

    /// <summary>
    /// As <see cref="PrintLowered(IrFunction)"/>, but also yields the
    /// statement-to-output-line table the mixed-source view uses to anchor fact
    /// comments and interleaved IL onto the lowered C# (the lowered analogue of
    /// <see cref="PrintRaised(IrFunction, out PrintedRangeMap)"/>).
    /// </summary>
    public static DecompilerResult PrintLowered(IrFunction function, out PrintedRangeMap printedRanges)
        => PrintLowered(function, out printedRanges, importMethodBody: null);

    /// <inheritdoc cref="PrintLowered(IrFunction, out PrintedRangeMap)"/>
    /// <remarks>
    /// <paramref name="options"/> applies the printer's byte-preserving spelling
    /// and layout knobs so the lowered annotated view spells a member the same way
    /// the Source view does. The byte-divergent style lenses are deliberately not
    /// run here: they are raised-altitude sugar, and the lowered pipeline exists to
    /// show the shape below that sugar.
    /// </remarks>
    public static DecompilerResult PrintLowered(
        IrFunction function, out PrintedRangeMap printedRanges, Func<MethodRef, IrFunction?>? importMethodBody,
        PrinterOptions? options = null)
    {
        printedRanges = PrintedRangeMap.Empty;
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        try
        {
            IrPasses.Run(function, IrPasses.Lowered, RaiseContext(importMethodBody));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var sink = new PrintedRangeMap();
            var printer = new CSharpPrinter(function, options)
            {
                _printedRanges = sink,
                _printedRangeMetadata = sink,
                _expressionText = [],
            };
            string output = printer.PrintBody(function);
            printedRanges = sink.Complete(output);
            return printer.Result(output, function);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the print analysis on an already-raised tree purely to capture the
    /// definite-assignment dataflow facts — the per-block <c>in</c>/<c>out</c>
    /// sets that decide which locals keep <c>= default</c>. The same walk that
    /// produces the output fills the sink, so the facts are the shipped
    /// analysis, not a parallel model. The rendered C# is discarded.
    /// </summary>
    public static DataflowFacts CollectDataflowFacts(IrFunction function)
    {
        var facts = new DataflowFacts();
        var printer = new CSharpPrinter(function) { _facts = facts };
        printer.PrintBody(function);
        return facts;
    }

    public static DecompilerResult Print(IrFunction function, PrinterOptions? options = null)
    {
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        try
        {
            var printer = new CSharpPrinter(function, options);
            string output = printer.PrintBody(function);
            return printer.Result(output, function);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static DecompilerResult Print(
        IrFunction function,
        PrinterOptions? options,
        bool fullyQualifyTypeNames)
    {
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        try
        {
            var printer = new CSharpPrinter(
                function,
                options,
                fullyQualifyTypeNames: fullyQualifyTypeNames);
            string output = printer.PrintBody(function);
            return printer.Result(output, function);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(
                DiagnosticIds.InternalError,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static DecompilerResult Print(
        IrFunction function, out PrintedRangeMap printedRanges, PrinterOptions? options = null)
    {
        printedRanges = PrintedRangeMap.Empty;
        if (MemorySafetyModeUnavailableResult(function) is { } unavailable)
            return unavailable;

        try
        {
            var sink = new PrintedRangeMap();
            var printer = new CSharpPrinter(function, options)
            {
                _printedRanges = sink,
                _printedRangeMetadata = sink,
                _expressionText = [],
            };
            string output = printer.PrintBody(function);
            printedRanges = sink.Complete(output);
            return printer.Result(output, function);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static DecompilerResult? MemorySafetyModeUnavailableResult(
        IrFunction function)
    {
        if (MemorySafetyModeUnavailableResult(function.MemorySafetyMode)
            is not { } unavailable)
        {
            return null;
        }

        return function.Diagnostics.Count == 0
            ? unavailable
            : unavailable with
            {
                Diagnostics =
                [
                    .. function.Diagnostics,
                    .. unavailable.Diagnostics,
                ],
            };
    }

    internal static DecompilerResult? MemorySafetyModeUnavailableResult(
        MemorySafetyModeDecision decision)
        => decision
            is MemorySafetyModeDecision.Unavailable unavailable
                ? DecompilerResult.Failure(
                    DiagnosticIds.MemorySafetyModeUnavailable,
                    MemorySafetyModeDecision.DescribeUnavailable(
                        unavailable.Rules))
                : null;

    DecompilerResult Result(string output, IrFunction function)
        => new(output, function.Fidelity, [.. function.Diagnostics])
        {
            ConstructorChain = _constructorChain,
            FieldInitializers = _fieldInitializers,
            RequiresAsyncBodyModifier = function.RequiresAsyncBodyModifier,
            RequiresUnsafeBodyModifier = !function.UsesUpdatedMemorySafetyRules
                && !_containsAwaitSyntax
                && function.Descendants.Prepend(function).Any(NeedsUnsafeBodyModifier),
            ContainsAwaitExpression = _containsAwaitSyntax,
            BodyIsSingleExpressionBody = BodyIsSingleExpressionBody(function, output),
            BodyIsDestructor = function.IsDestructor,
            Metadata = new DecompilerResultMetadata(
                EffectiveDecompilerOptions(),
                [.. _decisions])
            {
                ParameterNames = RequiresParameterNameComposition(function)
                        ? [
                            .. function.Signature.Parameters.Select(
                                parameter => parameter.DisplayName),
                        ]
                        : [],
            },
        };

    static bool RequiresParameterNameComposition(IrFunction function)
    {
        if (function.Signature.Parameters.Any(
            parameter => parameter.DisplayName != parameter.Name))
        {
            return true;
        }

        int separator = function.Name.LastIndexOf('.');
        ReadOnlySpan<char> simpleName = function.Name.AsSpan(separator + 1);
        bool hasImplicitValueBinder = simpleName.StartsWith(
                "set_",
                StringComparison.Ordinal)
            || simpleName.StartsWith("add_", StringComparison.Ordinal)
            || simpleName.StartsWith("remove_", StringComparison.Ordinal);
        return hasImplicitValueBinder
            && function.Signature.Parameters is [.., var valueParameter]
            && valueParameter.DisplayName != "value";
    }

    /// <summary>
    /// True when the printed body is exactly one top-level statement whose whole
    /// printed form is a single multi-line expression — a
    /// <c>return &lt;expression&gt;;</c> (a raised multi-line switch return, issue
    /// #3088; a wrapped fluent chain or other wrapped single expression, issue
    /// #3084) or a single void <c>&lt;expression&gt;;</c> statement (a wrapped
    /// fluent call chain, issue #3084). The member layer renders such a body as an
    /// expression-bodied member (<c>head =&gt; &lt;expr&gt;;</c>) instead of a
    /// brace block. The single-statement shape is structural (the emitted
    /// top-level statement list plus the lifts the printer already tracked), never
    /// a re-parse of the rendered text: because the body is exactly one statement
    /// with no lifted declarations, its whole printed form is that one
    /// <c>&lt;expr&gt;;</c>, so folding it to <c>head =&gt; &lt;expr&gt;;</c> is a
    /// language-guaranteed equivalence. The only text-derived input is whether
    /// that statement actually wrapped: a single-line body already folds on the
    /// <see cref="CSharpExpressionBody.FromSingleStatement"/> path, so this signal
    /// stays reserved for the multi-line case the flat helper cannot recover.
    /// </summary>
    bool BodyIsSingleExpressionBody(IrFunction function, string output)
        => !_emittedDeclarations
            && !_topLevelHasLabel
            && _constructorChain is null
            && _fieldInitializers.Count == 0
            && !function.RequiresAsyncBodyModifier
            && !NeedsUnsupportedFallbackReturn(function)
            && IsFoldableSingleStatement(_topLevelStatements, output);

    /// <summary>
    /// True when <paramref name="statements"/> is exactly one foldable statement
    /// and <paramref name="output"/> is its multi-line printed form. Keeps
    /// <see cref="BodyIsSingleExpressionBody"/> aligned with the downstream
    /// extractor (<see cref="CSharpExpressionBody.MultilineExpressionBodyLines"/>)
    /// so the typed flag is never set for a statement the printer expanded into
    /// several lines that would not fold. The shared guards require a multi-line
    /// body that terminates in <c>;</c> — the exact shape the extractor accepts:
    /// <list type="bullet">
    /// <item>A lone <see cref="Return"/> whose value the printer lifts into a
    /// leading local declaration (a <c>stackalloc</c>-to-pointer return inside an
    /// <c>unsafe</c> block) prints a decl first, not a bare <c>return </c>, so the
    /// keyword prefix keeps the flag off.</item>
    /// <item>A single <see cref="ExpressionStatement"/> has no leading-decl lift
    /// path, so its whole printed form is that one statement — but under the new
    /// memory-safety rules the printer may wrap a lone unsafe statement as
    /// <c>unsafe { &lt;stmt&gt;; }</c>, whose printed form ends in <c>}</c>, not
    /// <c>;</c>. The trailing-<c>;</c> guard keeps the flag off for that wrapper
    /// (the extractor rejects it too), so no keyword prefix is needed to
    /// discriminate the un-wrapped case.</item>
    /// </list>
    /// </summary>
    static bool IsFoldableSingleStatement(IReadOnlyList<IrNode> statements, string output)
    {
        if (statements is not [var only])
            return false;
        var trimmed = output.AsSpan().Trim();
        if (!trimmed.Contains('\n') || trimmed[^1] != ';')
            return false;
        return only switch
        {
            Return { Value: not null } => trimmed.StartsWith("return ", StringComparison.Ordinal),
            ExpressionStatement => true,
            _ => false,
        };
    }

    DecompilerOptions EffectiveDecompilerOptions()
        => DecompilerOptions.FromPrinterOptions(_options);

    void AddDecision(string ruleId, string category, string subject, string detail, string? oldValue = null, string? newValue = null, string? dedupDiscriminator = null)
    {
        var key = new DecisionKey(
            ruleId,
            category,
            subject,
            detail,
            oldValue ?? "",
            newValue ?? "",
            dedupDiscriminator ?? "");
        if (_decisionKeys.Add(key))
        {
            _decisions.Add(new DecompilerDecision(ruleId, category, subject, detail)
            {
                OldValue = oldValue,
                NewValue = newValue,
            });
        }
    }

    /// <summary>Stores that double as declarations: the local's first program-order reference, at statement level in the entry block.</summary>
    readonly HashSet<IrNode> _declaringStores = [];
    LocalDeclarationPlan? _localDeclarationPlan;

    /// <summary>Locals that may be read before they are definitely assigned, so their declaration must keep its `= default` zero-initializer (a bare declaration would be CS0165).</summary>
    HashSet<int> _readBeforeAssign = [];

    /// <summary>Optional sink for the definite-assignment dataflow facts; null on the shipped print path (the analysis records nothing then).</summary>
    DataflowFacts? _facts;

    /// <summary>Offsets some surviving goto targets — labels print wherever the block lives, top-level or inside a flat EH body.</summary>
    HashSet<int> _labelTargets = [];

    HashSet<int> _emittedLabels = [];
    string _labelScopeSuffix = "";
    int _nextNestedLabelScopeOrdinal;

    readonly Dictionary<SwitchBranch, string> _switchTemps = [];

    /// <summary>
    /// True while rendering inside an emitted <c>checked(...)</c> expression. A
    /// checked operation (overflow binary or conversion) nested in this context
    /// needs no <c>checked</c> wrapper of its own — the enclosing one already
    /// establishes the overflow context, and Roslyn keeps the same <c>.ovf</c>
    /// opcodes — so the printer collapses <c>checked((byte)(checked(a + b)))</c>
    /// to <c>checked((byte)(a + b))</c>. Saved/restored around each checked node.
    /// </summary>
    bool _checkedContext;

    /// <summary>An explicit base/this chain call lifted out of a constructor body to its signature initializer (base/this calls are invalid as body statements).</summary>
    string? _constructorChain;
    IrNode? _chainStatement;
    IrNode? _constructorInitializerStatement;

    /// <summary>Field initializers (<c>this.f = value</c> stores preceding the base call) lifted out of a constructor body to the field declarations, keyed in source order.</summary>
    readonly List<(string Field, string Value)> _fieldInitializers = [];
    readonly HashSet<IrNode> _fieldInitStores = [];

    /// <summary>
    /// True once <see cref="PrintBody"/> has emitted at least one up-front local
    /// declaration (before the body statements). A body with declarations is not
    /// a single-expression body, so it disqualifies the
    /// <see cref="DecompilerResult.BodyIsSingleExpressionBody"/> signal.
    /// </summary>
    bool _emittedDeclarations;

    /// <summary>
    /// The top-level statements <see cref="AppendContainer"/> actually emitted
    /// (chain/field-initializer lifts and the trimmed trailing <c>return;</c>
    /// excluded), plus whether any top-level label was emitted. Together they let
    /// <see cref="Result"/> recognize the single-statement expression body
    /// that <see cref="DecompilerResult.BodyIsSingleExpressionBody"/> reports —
    /// a structural fact, not a re-parse of the rendered text.
    /// </summary>
    readonly List<IrNode> _topLevelStatements = [];
    bool _topLevelHasLabel;

    /// <summary>Pinned local slots a <see cref="Fixed"/> statement owns: declared by the fixed header (skipped up front) and read as a pointer of the fixed's element type.</summary>
    readonly HashSet<int> _fixedLocals = [];

    /// <summary>Synthesized stack-slot names a <see cref="Fixed"/> statement owns and declares in its header.</summary>

    /// <summary>Resource local slots a <see cref="UsingStatement"/> owns: declared by the using header, not up front.</summary>
    readonly HashSet<int> _usingLocals = [];

    /// <summary>Iteration variable local slots declared by a <see cref="ForeachStatement"/> header.</summary>
    readonly HashSet<int> _foreachLocals = [];

    /// <summary>Pattern variable slots bound by pattern expressions: declared by the pattern, not up front.</summary>
    readonly HashSet<int> _isPatternLocals = [];

    /// <summary>Verified first out arguments that declare repeated exact-name locals.</summary>
    readonly HashSet<LoadLocalAddress> _outVariableDeclarations = [];
    readonly HashSet<int> _outArgumentLocals = [];

    /// <summary>Local slots declared by a tuple deconstruction header.</summary>
    readonly HashSet<int> _deconstructionLocals = [];

    /// <summary>Exception local slots declared by a catch clause header.</summary>
    readonly HashSet<int> _catchLocals = [];

    /// <summary>Ref-struct locals whose hoisted declaration must spell <c>scoped</c>: a <c>stackalloc</c>-initialized span whose declaration was split from its assignment (out of the unsafe block) would otherwise warn CS9081. A stackalloc result is always scoped, so this is faithful, not a guess.</summary>
    readonly HashSet<int> _scopedLocals = [];

    /// <summary>Optional sink recording which characters of the output each printed statement node emitted; null on the shipped print path. Drives line-anchored and range-anchored overlays (annotated views) without the printer knowing what they are.</summary>
    PrintedRangeMap? _printedRanges;

    /// <summary>
    /// Metadata sink retained while shared-scope lambda bodies compose into a
    /// temporary builder. Coordinates are disabled there, but expression text
    /// and syntax kinds are still captured for rebasing by the enclosing
    /// statement once the lambda text reaches its final location.
    /// </summary>
    PrintedRangeMap? _printedRangeMetadata;

    /// <summary>The empty-locals lambda currently sharing this printer's local scope.</summary>
    Lambda? _sharedScopeLambda;

    TypeRef? _returnTypeOverride;

    TypeRef CurrentReturnType
        => _returnTypeOverride ?? _function.Signature.ReturnType;

    /// <summary>
    /// Text captured per expression node while
    /// <see cref="_printedRangeMetadata"/> is collecting, so
    /// <see cref="RecordExpressionRanges"/> can bind expressions to characters.
    /// It remains active while a temporary lambda builder disables coordinate
    /// recording, but is null on the shipped print path.
    /// </summary>
    Dictionary<IrNode, string>? _expressionText;

    List<(IrNode Node, int Start, int End)>? _contextRanges;

    /// <summary>
    /// Context-added syntax roots, such as a target conversion around an
    /// existing expression. One operand may acquire multiple nested contexts;
    /// each context kind keeps the last speculative rendering.
    /// </summary>
    Dictionary<(IrExpression Operand, string Kind), ContextualExpressionCapture>?
        _contextualExpressions;

    readonly record struct ContextualExpressionCapture(
        SynthesizedRenderedExpression Node,
        IrExpression Operand,
        string Text);

    string PrintBody(IrFunction function)
    {
        var sb = new StringBuilder();
        PrepareBody(function);

        // A constructor prologue is leading field-initializer stores
        // (this.f = value) followed by the base(...)/this(...) chain call. C#
        // emits field initializers before the base call, so a this-field store
        // preceding the chain call is a field initializer — not a body
        // assignment — and the chain call is invalid as a body statement
        // (CS0175). Lift both out of the body so they render where they belong:
        // the initializers on the field declarations, the chain on the
        // signature.
        if (function.Body.Blocks is [{ } entry, ..]
            && ChainCallIndex(entry) is { } chainIndex
            && entry.Children.Take(chainIndex).All(IsFieldInitializerStore))
        {
            foreach (var store in entry.Children.Take(chainIndex).Cast<StoreField>())
            {
                _fieldInitStores.Add(store);
                _fieldInitializers.Add((store.Field.Name, Expression(store.Value)));
            }

            var chainCall = (Call)((ExpressionStatement)entry.Children[chainIndex]).Expression;
            _constructorInitializerStatement = entry.Children[chainIndex];
            if (ConstructorChainText(chainCall.Callee, chainCall) is { } chain)
            {
                _constructorChain = chain.TrimEnd(';');
                _chainStatement = entry.Children[chainIndex];
            }
        }

        // Remaining locals and slots declare up front, current-style.
        foreach (var declaration in CollectDeclarations(function))
            sb.AppendLf(declaration);
        if (sb.Length > 0)
        {
            _emittedDeclarations = true;
            sb.AppendLf();
        }

        AppendContainer(sb, function.Body, 0, topLevel: true);
        if (NeedsUnsupportedFallbackReturn(function))
            sb.AppendLf("return default;");
        return sb.ToString().TrimEnd() is { Length: > 0 } text ? text + "\n" : "";
    }

    void PrepareBody(IrFunction function)
    {
        EnsureNoResidualStackSlots(function);
        _labelTargets = CollectBranchTargets(function);
        _localDeclarationPlan =
            LocalDeclarationPlan.Create(
                function,
                function.Locals.Length,
                _options,
                _reservedScopeNames);
        _usingLocals.UnionWith(_localDeclarationPlan.UsingLocals);
        _foreachLocals.UnionWith(_localDeclarationPlan.ForeachLocals);
        _isPatternLocals.UnionWith(_localDeclarationPlan.PatternLocals);
        _deconstructionLocals.UnionWith(
            _localDeclarationPlan.DeconstructionLocals);
        _fixedLocals.UnionWith(_localDeclarationPlan.FixedLocals);
        _catchLocals.UnionWith(_localDeclarationPlan.CatchLocals);
        _outArgumentLocals.UnionWith(
            _localDeclarationPlan.OutArgumentLocals);
        _outVariableDeclarations.UnionWith(
            _localDeclarationPlan.OutVariableDeclarations);
        _scopedLocals.UnionWith(_localDeclarationPlan.ScopedLocals);
        _declaringStores.UnionWith(
            _localDeclarationPlan.DeclaringNodes);
        foreach (var binding in _localDeclarationPlan.Bindings)
        {
            if (binding.Provenance
                != LocalBindingNameProvenance.ApproximatePdb)
            {
                continue;
            }

            AddDecision(
                "approximate-pdb-local-name",
                DecompilerDecisionCategories.Taste,
                $"V_{binding.LocalIndex}",
                $"Used Portable PDB name '{binding.PreferredIdentifier}' as an approximate display name "
                    + $"for physical local slot {binding.LocalIndex}; exact row/scope identity remains "
                    + "unrepresented and fidelity is unchanged "
                    + "(dotnet_inspect_style_approximate_pdb_local_names).",
                newValue: binding.Identifier,
                dedupDiscriminator:
                    $"{_labelScopeSuffix}\0{binding.LocalIndex.ToString(CultureInfo.InvariantCulture)}");
        }
        _readBeforeAssign = DefiniteAssignment.Compute(function, _labelTargets, _facts);
        if (_facts is not null)
            _facts.LocalNames = [
                .. Enumerable.Range(0, function.Locals.Length)
                    .Select(LocalFactLabel),
            ];
    }

    /// <summary>
    /// The thin-writer boundary for storage: every stack-slot web is a
    /// pipeline-issued local before presentation (materialized, or bound by
    /// <see cref="ResidualSlotBindingPass"/>). Unconditional in Release and
    /// independent of <see cref="IrInvariants"/>: a surviving slot node is a
    /// pipeline failure, never success-shaped output.
    /// </summary>
    static void EnsureNoResidualStackSlots(IrFunction function)
    {
        foreach (var node in function.DescendantsOutsideNestedFunctions)
        {
            int? slot = node switch
            {
                StoreStackSlot store => store.Slot,
                LoadStackSlot load => load.Slot,
                _ => null,
            };
            if (slot is { } residual)
            {
                throw new InvalidOperationException(
                    $"Stack slot {residual} reached C# emission without residual storage binding.");
            }
        }
    }

    static bool NeedsUnsupportedFallbackReturn(IrFunction function)
        => NeedsUnsupportedFallbackReturn(function.Signature.ReturnType, function.RequiresAsyncBodyModifier, function);

    static bool AsyncReturnForbidsValue(IrFunction function)
        => AsyncReturnForbidsValue(function.Signature.ReturnType, function.RequiresAsyncBodyModifier);

    static bool NeedsUnsupportedFallbackReturn(TypeRef returnType, bool requiresAsyncBodyModifier, IrNode bodyRoot)
        => returnType is not { Namespace: "System", Name: "Void" }
            && returnType.Kind != TypeRefKind.ByRef
            && !AsyncReturnForbidsValue(returnType, requiresAsyncBodyModifier)
            && !bodyRoot.DescendantsOutsideNestedFunctions.Any(static n => n is YieldReturn or YieldBreak)
            && bodyRoot.DescendantsOutsideNestedFunctions.Any(static n => n is UnsupportedNode)
            && !bodyRoot.DescendantsOutsideNestedFunctions.Any(static n => n is Return);

    static bool AsyncReturnForbidsValue(TypeRef type, bool requiresAsyncBodyModifier)
    {
        if (!requiresAsyncBodyModifier)
            return false;

        if (type is { Kind: TypeRefKind.Definition, Namespace: "System.Threading.Tasks", Name: "Task" or "ValueTask" })
            return true;
        if (type is { Kind: TypeRefKind.GenericInstance, ElementType: { Namespace: "System.Collections.Generic", Name: "IAsyncEnumerable`1" or "IAsyncEnumerator`1" } })
            return true;

        return false;
    }
}
