using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Pipeline.InverseArchitecture;
using ILInspector.Decompiler.Tests.InverseArchitecture;

namespace ILInspector.DecompilerHarness;

public static class AssertionPrinter
{
    // Need a stateful object to track the first violation across pipeline stages
    public sealed class StatefulPrinter
    {
        bool _firstViolationFlagged;

        public string Dump(IrFunction function)
        {
            var sb = new StringBuilder();

            var executedPredicates = new Dictionary<string, IReadOnlyList<AssumptionViolation>>();
            foreach (var t in InverseLedger.NodeTypes(typeof(IrFunction).Assembly))
            {
                var attr = t.GetCustomAttribute<InverseOfAttribute>();
                if (attr?.Assumes != null && !executedPredicates.ContainsKey(attr.Assumes))
                {
                    var method = typeof(InverseAssumptions).GetMethod(attr.Assumes, BindingFlags.Public | BindingFlags.Static);
                    if (method != null)
                    {
                        try
                        {
                            var result = method.Invoke(null, new object[] { function }) as IReadOnlyList<AssumptionViolation>;
                            executedPredicates[attr.Assumes] = result ?? Array.Empty<AssumptionViolation>();
                        }
                        catch
                        {
                            // A throwing predicate shouldn't crash the dump
                            executedPredicates[attr.Assumes] = new[] { new AssumptionViolation(function, $"Predicate {attr.Assumes} threw an exception") };
                        }
                    }
                }
            }

            var allViolations = executedPredicates.Values.SelectMany(v => v).ToList();

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

                // Match violation by exact reference identity, not a lossy substring of Describe()
                var related = allViolations.FirstOrDefault(v => ReferenceEquals(v.Node, node));
                if (related.Node != null)
                {
                    var highlight = !_firstViolationFlagged ? "  <-- FIRST UNSOUND REWRITE" : "";
                    _firstViolationFlagged = true;
                    sb.Append(' ', indent * 2).AppendLine($"// ❌ UNSOUND: {related.Message}{highlight}");
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
