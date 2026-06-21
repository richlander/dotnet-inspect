using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Reconstructs the generic-parameter <c>where</c> clauses for the validity-check
/// method shell. The shell declares a body's generic parameters (<c>__M&lt;TOther&gt;</c>)
/// but, with no constraints, a constrained call — <c>byte.TryConvertFromTruncating&lt;TOther&gt;</c>
/// needs <c>where TOther : INumberBase&lt;TOther&gt;</c> — spuriously fails CS0314, which
/// is a shell limitation, not a decompiler defect. Reading the real constraints from
/// metadata (type- and method-level) lets the shell bind the same code the runtime
/// accepts, so genuine defects are not drowned by ~100 phantom CS0314s.
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
            var typeParams = typeDef.GetGenericParameters();
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var methodParams = method.GetGenericParameters();
                if (typeParams.Count == 0 && methodParams.Count == 0)
                    continue;

                // The decoder resolves a constraint's `!N`/`!!N` references against the
                // type's and method's parameter names — the same scope the importer uses.
                var scope = new GenericScope(Names(reader, typeParams), Names(reader, methodParams));
                string key = $"{typeName}::{reader.GetString(method.Name)}::{methodParams.Count}";
                if (!map.TryGetValue(key, out var clauses))
                    map[key] = clauses = new Dictionary<string, string>(StringComparer.Ordinal);
                AddClauses(reader, scope, typeParams, clauses);
                AddClauses(reader, scope, methodParams, clauses);
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

    static ImmutableArray<string> Names(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        var builder = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            builder.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return builder.MoveToImmutable();
    }

    static void AddClauses(MetadataReader reader, GenericScope scope,
        GenericParameterHandleCollection handles, Dictionary<string, string> clauses)
    {
        foreach (var handle in handles)
        {
            var gp = reader.GetGenericParameter(handle);
            string name = reader.GetString(gp.Name);
            if (clauses.ContainsKey(name))
                continue;
            if (Clause(reader, scope, gp) is { } clause)
                clauses[name] = clause;
        }
    }

    static string? Clause(MetadataReader reader, GenericScope scope, GenericParameter gp)
    {
        var attributes = gp.Attributes;
        bool isClass = (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
        bool isStruct = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
        bool hasNew = (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

        // C# ordering: class/struct, then base/interface constraints, then new().
        var parts = new List<string>();
        if (isClass)
            parts.Add("class");
        if (isStruct)
            parts.Add("struct");

        foreach (var constraintHandle in gp.GetConstraints())
        {
            var constraint = reader.GetGenericParameterConstraint(constraintHandle);
            if (Decode(reader, scope, constraint.Type) is not { } type)
                continue;
            // Implicit/redundant constraints C# forbids spelling: every type satisfies
            // object, and `struct` already implies the System.ValueType base.
            if (IsCoreType(type, "Object") || (isStruct && IsCoreType(type, "ValueType")))
                continue;
            if (Render(type) is { } text)
                parts.Add(text);
        }

        // `struct` already implies a public parameterless constructor.
        if (hasNew && !isStruct)
            parts.Add("new()");

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    static TypeRef? Decode(MetadataReader reader, GenericScope scope, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, scope, (TypeSpecificationHandle)handle, 0),
        _ => null,
    };

    static bool IsCoreType(TypeRef type, string name)
        => type.Kind == TypeRefKind.Definition && type.Namespace == "System" && type.Name == name;

    /// <summary>Renders a constraint type, or null when it does not resolve to a spellable name (an unresolved `!` slot — better skipped than emitted as the illegal `where T : object`).</summary>
    static string? Render(TypeRef type)
    {
        string text = type.ToDisplayString();
        return string.IsNullOrWhiteSpace(text) || text.Contains('!') ? null : text;
    }
}
