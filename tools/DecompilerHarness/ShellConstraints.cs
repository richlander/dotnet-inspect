using System.Collections.Generic;
using System.Text;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Supplies the generic-parameter <c>where</c> clauses for the validity-check
/// method shell. The shell declares a body's generic parameters (<c>__M&lt;TOther&gt;</c>)
/// but, with no constraints, a constrained call — <c>byte.TryConvertFromTruncating&lt;TOther&gt;</c>
/// needs <c>where TOther : INumberBase&lt;TOther&gt;</c> — spuriously fails CS0314, which
/// is a shell limitation, not a decompiler defect. The constraint clause bodies come
/// from the product (<see cref="MetadataDeclarationQuery.GetGenericConstraintClauses"/>),
/// so the harness carries no C# constraint-ordering or redundancy knowledge — it only
/// keys the product facts by the shell's identity and renders the clauses.
/// </summary>
internal static class ShellConstraints
{
    /// <summary>
    /// Per assembly: <c>"{typeName}::{methodName}::{methodGenericArity}"</c> to a map of
    /// generic-parameter name to its <c>where</c> clause body (the text after <c>name :</c>).
    /// The key mirrors <see cref="IrImporter.ImportAssembly"/>'s identity; overloads sharing it
    /// keep the first reading, which constraints are stable across.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Build(MetadataSource source)
    {
        var reader = source.Reader;
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string typeName = reader.GetFullTypeName(typeDef);
            bool typeIsGeneric = typeDef.GetGenericParameters().Count > 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                int methodArity = method.GetGenericParameters().Count;
                if (!typeIsGeneric && methodArity == 0)
                    continue;

                string key = $"{typeName}::{reader.GetString(method.Name)}::{methodArity}";
                if (!map.TryGetValue(key, out var clauses))
                    map[key] = clauses = new Dictionary<string, string>(StringComparer.Ordinal);
                // Overloads sharing the key can declare different generic parameter
                // names; accumulate the union, keeping the first clause seen per name
                // (constraints are stable across overloads that share a name).
                foreach (var (name, clause) in MetadataDeclarationQuery.GetGenericConstraintClauses(reader, typeDef, method))
                    clauses.TryAdd(name, clause);
            }
        }
        return map;
    }

    /// <summary>The <c>where</c> clauses for the shell's declared generics, in their declared order.</summary>
    public static string Clauses(IReadOnlyDictionary<string, Dictionary<string, string>> map,
        string typeName, string methodName, IrFunction function, IReadOnlyList<string> shellGenerics)
    {
        string key = $"{typeName}::{methodName}::{function.Signature.GenericParameterCount}";
        if (!map.TryGetValue(key, out var clauses))
            return "";

        var sb = new StringBuilder();
        foreach (var name in shellGenerics)
            if (clauses.TryGetValue(name, out var clause))
                sb.Append(" where ").Append(name).Append(" : ").Append(clause);
        return sb.ToString();
    }
}
