namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Extracts the set of namespaces a decompiled body references from its IR so
/// ReturnToSender and the fidelity harness can assemble <c>using</c> directives
/// without reading Decompiler IR themselves. This is the single owner of the IR type
/// walk those consumers previously duplicated in the harness; the facts it returns are
/// plain namespace strings and no Decompiler type crosses the boundary.
/// </summary>
public static class BodyNamespaceExtractor
{
    /// <summary>
    /// The distinct namespaces of every type referenced by <paramref name="function"/>
    /// and its descendant nodes, in ordinal-sorted order. Element/argument types of
    /// generic instances, arrays, by-refs, pointers, and pinned types are unwrapped so
    /// only their underlying definition namespaces are reported; the global namespace
    /// (empty string) is excluded.
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
}
