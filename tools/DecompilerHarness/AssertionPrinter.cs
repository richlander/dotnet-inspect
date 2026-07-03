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
        int _stage;
        bool _firstUnsoundFlagged;

        /// <param name="totalStages">
        /// The number of projection calls the pipeline will make — the importer
        /// boundary plus one per pass (<c>IrPasses.Default.Length + 1</c>). The
        /// last call is the final stage, where an undischarged assertion is
        /// <c>UNSOUND</c> rather than an <c>OBLIGATION</c>. Defaults to 1 (every
        /// stage treated as final) for single-stage callers.
        /// </param>
        public StatefulPrinter(int totalStages = 1)
            => _finalStage = totalStages;

        public string Dump(IrFunction function)
        {
            _stage++;
            bool isFinalStage = _stage >= _finalStage;
            var sb = new StringBuilder();

            var allViolations = AssertionEvaluator.EvaluateAssumptions(function)
                .SelectMany(r => r.Violations)
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
                if (related.Node != null)
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
                        sb.Append(' ', indent * 2).AppendLine($"// OBLIGATION (informational): {related.Message}");
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
    }
}
