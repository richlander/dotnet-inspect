using System;
using System.Linq;
using System.Reflection;
using System.Text;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Pipeline.InverseArchitecture;
using ILInspector.Decompiler.Tests.InverseArchitecture;

namespace ILInspector.DecompilerHarness;

public static class AssertionPrinter
{
    // Need a stateful object to track stage position + the first unsound survivor
    // across pipeline stages. An undischarged typing assertion at a non-final
    // stage is an OBLIGATION (informational): the rewrite accrued a typing claim
    // that a downstream pass is contracted to discharge (e.g. coercion insertion
    // wrapping the sink). Only an assertion that survives to the FINAL stage is
    // UNSOUND (error) — nothing downstream remains to discharge it, so the
    // rendered output leans on an unproven claim. See issue #2269.
    public sealed class StatefulPrinter
    {
        readonly int _finalStage;
        readonly IReadOnlyDictionary<string, string> _dischargePassByStageIdentity;
        readonly Dictionary<string, Dictionary<IrNode, int>> _occurrenceOrdinals = new(StringComparer.Ordinal);
        IrFunction? _function;
        int _stage;
        bool _firstUnsoundFlagged;

        /// <param name="totalStages">
        /// The number of projection calls the pipeline will make — the importer
        /// boundary plus one per pass (<c>IrPasses.Default.Length + 1</c>). The
        /// last call is the final stage, where an undischarged assertion is
        /// <c>UNSOUND</c> rather than an <c>OBLIGATION</c>. Defaults to 1 (every
        /// stage treated as final) for single-stage callers.
        /// </param>
        /// <param name="dischargePassByStageIdentity">
        /// Optional precomputed map from pass-independent assertion identity to
        /// the pass that discharged it. When provided, non-final obligations can
        /// name their observed discharger instead of forcing readers to infer it
        /// from later stages.
        /// </param>
        public StatefulPrinter(
            int totalStages = 1,
            IReadOnlyDictionary<string, string>? dischargePassByStageIdentity = null)
        {
            _finalStage = totalStages;
            _dischargePassByStageIdentity = dischargePassByStageIdentity ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        public string Dump(IrFunction function)
        {
            if (_function is null)
                _function = function;
            else if (!ReferenceEquals(_function, function))
                throw new InvalidOperationException("AssertionPrinter.StatefulPrinter is single-method; create a new instance for each IrFunction.");

            _stage++;
            bool isFinalStage = _stage >= _finalStage;
            var sb = new StringBuilder();

            var allViolations = AssertionEvaluator.EvaluateAssumptions(function)
                .SelectMany(predicate => predicate.Violations.Select(violation =>
                {
                    string node = violation.Node.GetType().Name;
                    string sinkType = AssertionScan.SinkType(violation.Message);
                    string identity = $"{predicate.Name}|{node}|{sinkType}|{violation.Message}";
                    int ordinal = OccurrenceOrdinal(identity, violation.Node);
                    return new AssertionMarker(violation.Node, violation.Message, $"{identity}#{ordinal}");
                }))
                .ToList();
            var unannotated = InverseLedger.Unannotated(typeof(IrFunction).Assembly).ToHashSet(StringComparer.Ordinal);

            void Append(IrNode node, int indent)
            {
                var desc = node.Describe();
                sb.Append(' ', indent * 2).AppendLine(desc);

                var t = node.GetType();
                var attr = t.GetCustomAttribute<InverseOfAttribute>();
                if (attr != null)
                {
                    var forwardName = attr.ForwardName ?? attr.Forward.ToString();
                    sb.Append(' ', indent * 2).Append($"// inverse: {forwardName} (oracle: {attr.Oracle}, naming: {attr.Naming})");
                    if (attr.Assumes != null)
                    {
                        sb.Append($", assumes: {attr.Assumes}");
                    }
                    sb.AppendLine();
                }
                else if (node is IrExpression && unannotated.Contains(t.Name))
                {
                    sb.Append(' ', indent * 2).AppendLine("// inverse: (unannotated)");
                }

                // Match violation by exact reference identity, not a lossy substring of Describe()
                var related = allViolations.FirstOrDefault(v => ReferenceEquals(v.Node, node));
                if (related is not null)
                {
                    if (isFinalStage)
                    {
                        // Survived to the final stage: nothing downstream remains
                        // to discharge it — a real soundness failure.
                        var highlight = !_firstUnsoundFlagged ? "  <-- FIRST UNSOUND SURVIVOR" : "";
                        _firstUnsoundFlagged = true;
                        sb.Append(' ', indent * 2).AppendLine($"// ❌ UNSOUND (error): {related.Message}{highlight}");
                    }
                    else
                    {
                        // Stage-relative: a downstream pass is contracted to
                        // discharge this typing obligation before the final stage.
                        string suffix = _dischargePassByStageIdentity.TryGetValue(related.StageIdentity, out var discharger)
                            && discharger.Length > 0
                            ? $"; discharged by {discharger}"
                            : "";
                        sb.Append(' ', indent * 2).AppendLine($"// OBLIGATION (informational{suffix}): {related.Message}");
                    }
                }

                foreach (var child in node.Children)
                    Append(child, indent + 1);
            }

            Append(function, 0);

            foreach (var diagnostic in function.Diagnostics)
                sb.AppendLine($"// {diagnostic}");
            sb.AppendLine($"// fidelity: {function.Fidelity}");
            return sb.ToString();
        }

        int OccurrenceOrdinal(string identity, IrNode node)
        {
            if (!_occurrenceOrdinals.TryGetValue(identity, out var byNode))
                _occurrenceOrdinals[identity] = byNode = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
            if (byNode.TryGetValue(node, out int ordinal))
                return ordinal;
            ordinal = byNode.Count;
            byNode.Add(node, ordinal);
            return ordinal;
        }

        sealed record AssertionMarker(IrNode Node, string Message, string StageIdentity);
    }
}
