namespace ILInspector.Decompiler.Pipeline;

/// <summary>Retains lexical blocks needed to spell disjoint exact local names.</summary>
public sealed class PdbLocalScopePass : IIrPass
{
    public string Name => "pdb-local-scopes";

    public void Run(IrFunction function, PassContext context)
    {
        var duplicates = Enumerable.Range(0, function.LocalNames.Length)
            .Where(index => function.LocalNames[index] is { } name
                && CSharpNaming.IsUsableIdentifier(name))
            .GroupBy(index => function.LocalNames[index]!, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Where(index =>
                !function.EliminatedLocalSlots.Contains(index)
                && IrFunction.LocalSlotReferencesInScope(function, index).Any()).ToArray())
            .Where(group => group.Length > 1)
            .ToArray();
        if (duplicates.Length == 0)
            return;

        var reserved = ExactLocalNameAllocation.ReservedNames(
            function, function.Signature.Parameters, function.Signature.GenericParameterNames);
        foreach (var group in duplicates)
        {
            if (reserved.Contains(function.LocalNames[group[0]]!))
                continue;
            foreach (int index in group)
            {
                if (!function.IsLocalDeclaredInNestedScope(index))
                    continue;
                var scopes = CSharpPrinter.LocalDeclarationScopes(function, function.Locals.Length);
                if (!group.Any(other => other != index
                    && ExactLocalNameAllocation.ScopesOverlap(scopes[index], scopes[other])))
                {
                    continue;
                }
                TryRetainBlock(function, index, group, context);
            }
        }
    }

    static void TryRetainBlock(IrFunction function, int index, int[] sameName, PassContext context)
    {
        var references = IrFunction.LocalSlotReferencesInScope(function.Body, index).ToArray();
        if (references.Length == 0)
            return;
        IrNode? declaration = references[0] switch
        {
            StoreLocal store when !store.Value.DescendantsAndSelfOutsideNestedFunctions.Any(node =>
                node is LoadLocal load && load.Index == index
                || node is LoadLocalAddress address && address.Index == index) => store,
            LoadLocalAddress address when address.Parent is InitObject init
                && ReferenceEquals(init.Address, address) => init,
            LoadLocalAddress address => OutArgumentStatement(address, index),
            IsPattern pattern => PatternStatement(pattern, references),
            RecursivePropertyDeclarationPattern pattern => PatternStatement(pattern, references),
            _ => null,
        };
        if (declaration?.Parent is not Block block)
        {
            return;
        }
        int first = declaration.ChildIndex;
        int last = first;
        foreach (var reference in references)
        {
            IrNode? statement = reference;
            while (statement is not null && !ReferenceEquals(statement.Parent, block))
                statement = statement.Parent;
            if (statement is null || statement.ChildIndex < first)
                return;
            last = Math.Max(last, statement.ChildIndex);
        }
        if (first == 0 && last == block.Children.Count - 1
            && block.Parent is not BlockContainer)
        {
            return;
        }

        var range = block.Children.Skip(first).Take(last - first + 1).ToArray();
        if (ReferenceOwnership.RewriteWouldInvalidateLabels(function, range, []))
        {
            return;
        }
        bool Inside(IrNode node) => range.Any(statement => ExactLocalNameAllocation.Contains(statement, node));
        if (sameName.Any(other => other != index
            && IrFunction.LocalSlotReferencesInScope(function.Body, other).Any(Inside)))
        {
            return;
        }
        // Header/pattern binders have an intrinsic declaration scope. A wrapper
        // must not strand one of their surviving uses outside that scope.
        for (int other = 0; other < function.Locals.Length; other++)
        {
            var uses = IrFunction.LocalSlotReferencesInScope(function.Body, other).ToArray();
            if (uses.Any(node => Inside(node) && node is not (StoreLocal or LoadLocal or LoadLocalAddress))
                && uses.Any(node => !Inside(node)))
            {
                return;
            }
        }

        var statements = block.DetachChildren();
        var lexical = new Block(declaration.SourceOffset);
        for (int position = 0; position < statements.Count; position++)
        {
            if (position == first)
                block.Add(lexical);
            if (position >= first && position <= last)
                lexical.Add(statements[position]);
            else
                block.Add(statements[position]);
        }
        context.Stepper.StepOver($"retain scope for local {index}", lexical);
    }

    static IrNode? OutArgumentStatement(LoadLocalAddress address, int index)
    {
        MethodRef? callee;
        int parameterIndex;
        switch (address.Parent)
        {
            case Call call:
                callee = call.Callee;
                parameterIndex = address.ChildIndex - (callee.HasThis ? 1 : 0);
                break;
            case NewObject creation:
                callee = creation.Constructor;
                parameterIndex = address.ChildIndex;
                break;
            default:
                return null;
        }
        return callee.TryGetVerifiedOutLocal(parameterIndex, address, out int local)
            && local == index
                ? StatementInBlock(address)
                : null;
    }

    static IrNode? StatementInBlock(IrNode node)
    {
        while (node.Parent is not null and not Block)
            node = node.Parent;
        return node.Parent is Block ? node : null;
    }

    static IrNode? PatternStatement(IrNode pattern, IrNode[] references)
    {
        IrNode? statement = StatementInBlock(pattern);
        return statement is not null
            && references.All(reference =>
                ExactLocalNameAllocation.Contains(statement, reference))
                ? statement
                : null;
    }

    internal static IReadOnlyList<IrNode> WithoutLexicalBlocks(IReadOnlyList<IrNode> statements)
    {
        if (!statements.Any(statement => statement is Block))
            return statements;
        var flattened = new List<IrNode>();
        foreach (var statement in statements)
        {
            if (statement is Block block)
                flattened.AddRange(WithoutLexicalBlocks(block.Children));
            else
                flattened.Add(statement);
        }
        return flattened;
    }
}
