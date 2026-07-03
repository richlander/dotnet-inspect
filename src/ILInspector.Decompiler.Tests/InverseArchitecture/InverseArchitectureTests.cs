using System.Reflection;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Pipeline.InverseArchitecture;

using Xunit;

namespace ILInspector.Decompiler.Tests.InverseArchitecture;

// Machinery for the inverse architecture (docs/design/inverse-architecture.md):
// proves the [InverseOf]/[NotInverted] annotations, the InverseAssumptions
// predicates, the ledger reflector, and the coverage/drift gates work end to end
// — without annotating any product node, so this change touches no IrNodes.cs and
// the value-typed-emission owner applies the real annotations in their own flow.
// The sample nodes below stand in for real IR nodes to exercise the pipeline.
public class InverseArchitectureTests
{
    readonly ITestOutputHelper _output;

    public InverseArchitectureTests(ITestOutputHelper output) => _output = output;

    // In-domain product nodes that MUST carry [InverseOf] or [NotInverted]. The
    // conversion family is the first annotated slice (docs/design/inverse-architecture.md);
    // the set grows as the ledger's declared domain grows.
    static readonly string[] EnforcedProductNodes = ["AddressOfMethod", "ArrayLength", "AwaitExpression", "Binary", "Box", "Call", "CallIndirect", "CastClass", "CaughtException", "Coalesce", "Coerce", "Comparison", "Conditional", "Constant", "Convert", "DelegateCreation", "IncrementDecrement", "IndexFromEnd", "InlineArraySpanConversion", "IsInstance", "Lambda", "LoadArgument", "LoadArgumentAddress", "LoadElement", "LoadElementAddress", "LoadField", "LoadFieldAddress", "LoadFunctionPointer", "LoadIndirect", "LoadLocal", "LoadLocalAddress", "LoadProperty", "LoadStackSlot", "LoadToken", "LocalFunctionInvocation", "LogicalBinary", "LogicalNot", "NewArray", "NewObject", "NullConditional", "RangeExpression", "SizeOf", "SliceExpression", "SpanLiteral", "StackAllocArray", "StackAllocate", "SwitchExpression", "TypeOf", "Unary", "Unbox", "UnboxAny", "UnionSwitchExpression", "UnsupportedNode"];

    static Assembly ProductAssembly => typeof(IrFunction).Assembly;
    Assembly TestAssembly => GetType().Assembly;

    [Fact]
    public void Reflector_RendersAnnotatedNode()
    {
        var sample = Assert.Single(
            InverseLedger.Rows(TestAssembly),
            row => row.Node == nameof(SampleInverseNode));

        Assert.Equal("BoundConversion (sample)", sample.ForwardName);
        Assert.Equal(Oracle.RyuJitStackNormalization, sample.Oracle);
        Assert.Equal(NameProvenance.Native, sample.Naming);

        var markdown = InverseLedger.RenderMarkdown(TestAssembly);
        Assert.Contains("| `SampleInverseNode` | BoundConversion (sample) | RyuJitStackNormalization | Native |", markdown);
    }

    [Fact]
    public void Coverage_DetectsAnnotatedAndUnannotated()
    {
        var unannotated = InverseLedger.Unannotated(TestAssembly);
        Assert.Contains(nameof(SampleUnannotatedNode), unannotated);
        Assert.DoesNotContain(nameof(SampleInverseNode), unannotated);
        Assert.DoesNotContain(nameof(SampleBoundaryNode), unannotated);
    }

    [Fact]
    public void Reflector_RendersBoundary()
    {
        var boundary = Assert.Single(
            InverseLedger.Boundaries(TestAssembly),
            b => b.Node == nameof(SampleBoundaryNode));
        Assert.Equal("sample: outside the checkable domain", boundary.Reason);

        var markdown = InverseLedger.RenderMarkdown(TestAssembly);
        Assert.Contains("## Declared non-inverse boundaries", markdown);
        Assert.Contains("| `SampleBoundaryNode` | sample: outside the checkable domain |", markdown);
    }

    [Fact]
    public void Reflector_EscapesPipesInCells()
    {
        var markdown = InverseLedger.RenderMarkdown(TestAssembly);

        // Type-assertions row: every literal `|` is escaped to the `&#124;`
        // entity, so the six data columns stay intact (a raw `|` would open
        // phantom columns; a `\|` backslash escape would show a literal
        // backslash inside a code span and double-escape non-idempotently).
        Assert.Contains(
            "| `SamplePipeNode` | BoundBinaryOperator (a &#124; b) | RyuJitImporter | Inherited | operands share a type; the &#124; operator maps to or | InverseArchitectureTests |",
            markdown);

        // Boundaries row: Cell() escapes [NotInverted] Reason too.
        Assert.Contains(
            "| `SamplePipeBoundaryNode` | outside the checkable domain &#124; see design notes |",
            markdown);

        // No raw pipe from an annotation leaks into either table body.
        Assert.DoesNotContain("(a | b)", markdown);
        Assert.DoesNotContain("domain | see", markdown);
    }

    // Rule #4 (structural): every [InverseOf(assumes: ...)] must name a runnable,
    // release-capable predicate on InverseAssumptions with the Check(IrFunction)
    // shape — this gate resolves each and confirms it is invocable. It proves
    // resolve-and-invoke, not non-vacuity: per-predicate *behavior* is covered by
    // each predicate's own tests (e.g. CoercionInvariantTests for
    // SinkDistinguishableFromStack), not re-proven here.
    [Fact]
    public void EveryAssumes_ResolvesToARunnablePredicate()
    {
        var probes = new[] { EmptyProbeFunction() };
        int validated = 0;

        foreach (var assembly in new[] { ProductAssembly, TestAssembly })
        {
            foreach (var nodeType in InverseLedger.NodeTypes(assembly))
            {
                if (nodeType.GetCustomAttribute<InverseOfAttribute>() is not { Assumes: { } assumes })
                    continue;

                var predicate = typeof(InverseAssumptions).GetMethod(
                    assumes, BindingFlags.Public | BindingFlags.Static, [typeof(IrFunction)]);

                Assert.True(predicate is not null,
                    $"{nodeType.Name}: assumes '{assumes}' has no InverseAssumptions.{assumes}(IrFunction).");
                Assert.True(typeof(IReadOnlyList<AssumptionViolation>).IsAssignableFrom(predicate!.ReturnType),
                    $"{nodeType.Name}: assumes '{assumes}' must return IReadOnlyList<AssumptionViolation>.");

                foreach (var probe in probes)
                    Assert.IsAssignableFrom<IReadOnlyList<AssumptionViolation>>(predicate.Invoke(null, [probe]));

                validated++;
            }
        }

        Assert.True(validated > 0, "Expected at least one assumes: predicate to validate (the sample node).");
    }

    [Fact]
    public void EnforcedProductNodes_AreAnnotated()
    {
        var annotated = InverseLedger.NodeTypes(ProductAssembly)
            .Where(t => t.GetCustomAttribute<InverseOfAttribute>() is not null
                     || t.GetCustomAttribute<NotInvertedAttribute>() is not null)
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var node in EnforcedProductNodes)
            Assert.True(annotated.Contains(node),
                $"In-domain node '{node}' must carry [InverseOf] or [NotInverted].");
    }

    // Advisory only: surface the annotation backlog. Never fails — new nodes added
    // by other work show up here but must not red this gate.
    [Fact]
    public void ProductBacklog_IsReported()
    {
        var backlog = InverseLedger.Unannotated(ProductAssembly);
        _output.WriteLine($"Inverse ledger backlog: {backlog.Count} IR node(s) await [InverseOf]/[NotInverted].");
        foreach (var node in backlog)
            _output.WriteLine($"  - {node}");
    }

    // The committed generated ledger must match the reflector output. The match is
    // content-normalized (line-ending- and trailing-whitespace-insensitive via
    // Normalize), not raw byte-exact — so the gate is portable across checkouts and
    // editors while still catching any content drift in the ledger.
    [Fact]
    public void GeneratedProductLedger_MatchesCommittedFile()
    {
        var root = FindRepoRoot();
        Assert.SkipUnless(root is not null,
            "Repo source tree is not co-located with the test binary; the drift gate is a source-tree check.");

        var path = Path.Combine(root!, "docs", "design", "inverse-ledger.generated.md");
        string regenHint = $"Regenerate it from the repository root via `./eng/regen-inverse-ledger.sh`.";
        Assert.True(File.Exists(path),
            $"Missing {path}. {regenHint}");

        string expected = Normalize(InverseLedger.RenderMarkdown(ProductAssembly));
        string actual = Normalize(File.ReadAllText(path));
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"Generated inverse ledger drifted: {path}. {regenHint}");
    }

    static IrFunction EmptyProbeFunction()
    {
        var container = new BlockContainer();
        container.Add(new Block(0));
        return new IrFunction(
            "Probe",
            TypeRef.Definition("synthetic", "", "Holder"),
            new MethodSignature(TypeRef.CoreLib("System", "Int32"), [], HasThis: false, GenericParameterCount: 0),
            [],
            container);
    }

    static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    // Walk up from the running binary's location to the repo root (identified by the
    // framing doc). Runtime-relative, not [CallerFilePath]: a built exe executed on a
    // different machine than it was built keeps working, and when the source tree is
    // absent the drift gate skips rather than false-failing.
    static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "docs", "design", "inverse-architecture.md")))
                return dir.FullName;
        return null;
    }
}

// A stand-in IR node exercising the full annotation pipeline (reflection, ledger
// rendering, assumes resolution + invocation) without touching a product node.
[InverseOf(
    Forward.RoslynBoundConversion,
    naming: NameProvenance.Native,
    oracle: Oracle.RyuJitStackNormalization,
    forwardName: "BoundConversion (sample)",
    precondition: "sample: sink distinguishable from stack",
    assumes: nameof(InverseAssumptions.SinkDistinguishableFromStack),
    witness: "InverseArchitectureTests")]
sealed class SampleInverseNode : IrExpression
{
    public override TypeRef? ResultType => null;
    public override string Describe() => nameof(SampleInverseNode);
}

// A stand-in boundary node: proves [NotInverted] renders in the generated ledger.
[NotInverted("sample: outside the checkable domain")]
sealed class SampleBoundaryNode : IrExpression
{
    public override TypeRef? ResultType => null;
    public override string Describe() => nameof(SampleBoundaryNode);
}

// Deliberately unannotated: proves the coverage backlog detection.
sealed class SampleUnannotatedNode : IrExpression
{
    public override TypeRef? ResultType => null;
    public override string Describe() => nameof(SampleUnannotatedNode);
}

// A stand-in node whose annotation strings carry literal `|` characters: proves
// the reflector escapes them so a bitwise-`|` operator or IL sequence cannot
// split the ledger row into phantom Markdown columns (#2197). Pipes are kept in
// plain text (not a code span), the case the entity escape renders cleanly.
[InverseOf(
    Forward.RoslynBoundBinaryOperator,
    naming: NameProvenance.Inherited,
    oracle: Oracle.RyuJitImporter,
    forwardName: "BoundBinaryOperator (a | b)",
    precondition: "operands share a type; the | operator maps to or",
    witness: "InverseArchitectureTests")]
sealed class SamplePipeNode : IrExpression
{
    public override TypeRef? ResultType => null;
    public override string Describe() => nameof(SamplePipeNode);
}

// A stand-in boundary whose reason carries a literal `|`: exercises Cell() on
// the boundaries table (Boundary.Reason), not just the type-assertions table.
[NotInverted("outside the checkable domain | see design notes")]
sealed class SamplePipeBoundaryNode : IrExpression
{
    public override TypeRef? ResultType => null;
    public override string Describe() => nameof(SamplePipeBoundaryNode);
}
