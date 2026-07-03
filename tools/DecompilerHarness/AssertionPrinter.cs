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
    public static string Dump(IrFunction function)
    {
        var sb = new StringBuilder();
        bool firstViolationFlagged = false;
        
        var executedPredicates = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var t in InverseLedger.NodeTypes(typeof(IrFunction).Assembly))
        {
            var attr = t.GetCustomAttribute<InverseOfAttribute>();
            if (attr?.Assumes != null && !executedPredicates.ContainsKey(attr.Assumes))
            {
                var method = typeof(InverseAssumptions).GetMethod(attr.Assumes, BindingFlags.Public | BindingFlags.Static);
                if (method != null)
                {
                    var result = method.Invoke(null, new object[] { function }) as IReadOnlyList<string>;
                    executedPredicates[attr.Assumes] = result ?? Array.Empty<string>();
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

            var related = allViolations.FirstOrDefault(v => v.Contains(desc));
            if (related != null)
            {
                var highlight = !firstViolationFlagged ? "  <-- FIRST UNSOUND REWRITE" : "";
                firstViolationFlagged = true;
                sb.Append(' ', indent * 2).AppendLine($"// ❌ UNSOUND: {related}{highlight}");
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
