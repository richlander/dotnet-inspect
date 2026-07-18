using ILInspector.CSharp;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Answers neutral reconstruction questions about a decompiled member body's IR so
/// ReturnToSender and the fidelity harness never read Decompiler IR themselves. Each
/// query owns an IR walk those consumers previously duplicated in the harness; every
/// fact returned carries only plain strings and indices, so no Decompiler type crosses
/// the boundary. New body-reconstruction questions belong here as additional methods.
/// </summary>
public static class MemberBodyFacts
{
    /// <summary>
    /// The distinct namespaces of every type referenced by <paramref name="function"/>
    /// and its descendant nodes, so the harness can assemble <c>using</c> directives.
    /// Element/argument types of generic instances, arrays, by-refs, pointers, and
    /// pinned types are unwrapped so only their underlying definition namespaces are
    /// reported; a function pointer's return type and parameter types are unwrapped
    /// the same way; the global namespace (empty string) is excluded.
    /// <para>
    /// Order: ordinal-sorted (case-sensitive), because the harness builds a stable,
    /// deterministic <c>using</c> block from this set.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> ReferencedNamespaces(IrFunction function)
    {
        var namespaces = new SortedSet<string>(StringComparer.Ordinal);

        void Add(TypeRef? type)
        {
            switch (type?.Kind)
            {
                case TypeRefKind.Definition:
                    if (type.Namespace.Length > 0)
                        namespaces.Add(type.Namespace);
                    break;
                case TypeRefKind.GenericInstance:
                    Add(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        Add(argument);
                    break;
                case TypeRefKind.SzArray or TypeRefKind.Array
                    or TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.Pinned:
                    Add(type.ElementType);
                    break;
                case TypeRefKind.FunctionPointer:
                    Add(type.ElementType);
                    foreach (var parameter in type.TypeArguments)
                        Add(parameter);
                    break;
            }
        }

        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                Add(type);
            if (node is IrExpression expression)
                Add(expression.ResultType);
        }

        return namespaces;
    }

    /// <summary>
    /// The chain-call and primary-constructor-prologue facts for
    /// <paramref name="function"/>, so ReturnToSender and the C# seam can reconstruct
    /// constructor-chain initializers and primary-constructor shells. Callers should
    /// only apply the constructor-shaped facts to actual constructors; the extraction
    /// itself is body-shape driven.
    /// <para>
    /// Order: <see cref="ConstructorBodyFacts.ChainParameterTypes"/> follows the chain
    /// call's parameter order, and <see cref="ConstructorBodyFacts.PrimaryConstructorPrologue"/>
    /// follows the entry block's field-store order (source-argument order).
    /// </para>
    /// </summary>
    public static ConstructorBodyFacts Constructor(IrFunction function)
        => new(ChainParameterTypes(function), PrimaryConstructorPrologue(function));

    /// <summary>
    /// The backing fields <paramref name="function"/> reads or writes, so the harness
    /// can synthesize the auto-property/field member requirements the body depends on.
    /// Field loads (<c>ldfld</c>/<c>ldflda</c>) report <see cref="BackingFieldAccess.Read"/>;
    /// instance stores whose receiver is <c>this</c> (argument 0) report
    /// <see cref="BackingFieldAccess.InstanceWrite"/> and static stores report
    /// <see cref="BackingFieldAccess.StaticWrite"/>; instance stores to any other
    /// receiver are omitted. A reference whose declaring type does not resolve to a type
    /// definition is omitted, since it can never be a self-type member.
    /// <para>
    /// Order: all reads first (every <c>ldfld</c> in IR descendant order, then every
    /// <c>ldflda</c> in IR descendant order), then all writes in IR descendant order.
    /// The order is preserved because downstream de-duplication keeps the first member
    /// it sees for a given property name.
    /// </para>
    /// </summary>
    public static IReadOnlyList<BackingFieldReference> BackingFieldReferences(IrFunction function)
    {
        var references = new List<BackingFieldReference>();

        foreach (var load in function.Descendants.OfType<LoadField>())
            Add(load.Field, BackingFieldAccess.Read);
        foreach (var address in function.Descendants.OfType<LoadFieldAddress>())
            Add(address.Field, BackingFieldAccess.Read);
        foreach (var store in function.Descendants.OfType<StoreField>())
        {
            BackingFieldAccess? access = store switch
            {
                { HasInstance: true, Instance: LoadArgument { Index: 0 } } => BackingFieldAccess.InstanceWrite,
                { HasInstance: false } => BackingFieldAccess.StaticWrite,
                _ => null,
            };
            if (access is { } write)
                Add(store.Field, write);
        }

        return references;

        void Add(FieldRef field, BackingFieldAccess access)
        {
            var declaring = field.DeclaringType.Kind == TypeRefKind.GenericInstance
                ? field.DeclaringType.ElementType ?? field.DeclaringType
                : field.DeclaringType;
            if (declaring.Kind != TypeRefKind.Definition)
                return;

            references.Add(new BackingFieldReference(
                declaring.Namespace, declaring.Name, field.Name, field.BackingPropertyName, access));
        }
    }

    /// <summary>
    /// The parameter type display names of the <c>this</c>/<c>base</c> <c>.ctor</c>
    /// chain call in the entry block, or null when the body has no chain call. Mirrors
    /// the chain-call detection so the shell can verify the chained-to constructor was
    /// reconstructed before emitting the initializer.
    /// </summary>
    static IReadOnlyList<string>? ChainParameterTypes(IrFunction function)
    {
        if (function.Body.Blocks is not [{ } entry, ..])
            return null;

        foreach (var child in entry.Children)
        {
            if (child is ExpressionStatement
                {
                    Expression: Call { Callee: { Name: ".ctor", HasThis: true } } call
                }
                && call.Arguments is [_, ..])
            {
                return [.. call.Callee.ParameterTypes.Select(type => type.ToDisplayString())];
            }
        }

        return null;
    }

    /// <summary>
    /// The ordered <c>this.field = argN;</c> assignments in a primary-constructor
    /// prologue: an entry block whose leading statements are field stores from method
    /// arguments, followed by a parameterless <c>this</c>/<c>base</c> chain call and
    /// nothing but returns. Null when the body is not primary-constructor-prologue
    /// shaped. Metadata correspondence (which parameter, which field, field types) is
    /// resolved by the caller, not here.
    /// </summary>
    static IReadOnlyList<PrimaryConstructorFieldStore>? PrimaryConstructorPrologue(IrFunction function)
    {
        if (function.Body.Blocks is not [{ } entry, ..])
            return null;

        int? chainIndex = null;
        for (int i = 0; i < entry.Children.Count; i++)
        {
            if (entry.Children[i] is ExpressionStatement
                {
                    Expression: Call { Callee: { Name: ".ctor", HasThis: true }, Arguments: [LoadArgument { Index: 0 }] }
                })
            {
                chainIndex = i;
                break;
            }
        }

        if (chainIndex is not > 0)
            return null;
        if (entry.Children.Skip(chainIndex.Value + 1).Any(node => node is not Return))
            return null;

        var stores = new List<PrimaryConstructorFieldStore>();
        foreach (var node in entry.Children.Take(chainIndex.Value))
        {
            if (node is not StoreField
                {
                    HasInstance: true,
                    Instance: LoadArgument { Index: 0 },
                    Value: LoadArgument { Index: > 0 } value
                } store)
            {
                return null;
            }

            stores.Add(new PrimaryConstructorFieldStore(value.Index, store.Field.Name, store.Field.BackingPropertyName));
        }

        return stores.Count == 0 ? null : stores;
    }
}
