using System.Reflection.Metadata;
using ILReader = ILInspector.Instructions.ILReader;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Binds scoped PDB identities before raising. Debug ranges select candidate
/// uses, not lifetimes: definite assignment and raw control flow must separately
/// prove that replacing physical storage with logical locals is safe.
/// </summary>
internal static class ScopedLocalImport
{
    public static void Apply(IrFunction function, MethodBody body)
    {
        if (body.LocalDeclarations.IsDefaultOrEmpty && body.LocalDeclarationsAreComplete)
            return;

        function.PdbLocalNameCandidates =
            [.. Enumerable.Repeat<string?>(null, function.Locals.Length)];
        function.LocalDeclarationBindings =
            [.. Enumerable.Repeat<PdbLocalDeclaration?>(null, function.Locals.Length)];
        if (!body.LocalDeclarationsAreComplete)
        {
            AddLoss(function, null, "Portable PDB local declarations could not be completely decoded");
            return;
        }

        foreach (var group in body.LocalDeclarations
            .Where(d => (d.Attributes & LocalVariableAttributes.DebuggerHidden) == 0)
            .GroupBy(d => d.SlotIndex))
        {
            var declarations = group.OrderBy(d => d.Scope.StartOffset)
                .ThenBy(d => d.ScopeRowId).ThenBy(d => d.VariableRowId).ToArray();
            int slot = group.Key;
            string? failure = null;
            if ((uint)slot >= (uint)body.Locals.Length)
                failure = "the Portable PDB slot is outside the IL local signature";
            else if (declarations.Any(d => d.Scope.StartOffset < 0
                || d.Scope.EndOffset > body.IL.Length
                || d.Scope.EndOffset <= d.Scope.StartOffset))
                failure = "the Portable PDB scope is outside the method body or empty";
            else if (declarations.Length == 1)
            {
                function.LocalDeclarationBindings =
                    function.LocalDeclarationBindings.SetItem(slot, declarations[0]);
                continue;
            }
            else
                failure = TrySplit(function, declarations, body);

            if (failure is null)
                continue;
            if ((uint)slot < (uint)function.LocalNames.Length)
                function.LocalNames = function.LocalNames.SetItem(slot, null);
            if ((uint)slot < (uint)function.LocalDeclaredInNestedScope.Length)
                function.LocalDeclaredInNestedScope = function.LocalDeclaredInNestedScope.SetItem(slot, false);
            if ((uint)slot < (uint)function.PdbLocalNameCandidates.Length)
            {
                function.PdbLocalNameCandidates =
                    function.PdbLocalNameCandidates.SetItem(
                        slot,
                        declarations
                            .Where(declaration =>
                                declaration.Scope.StartOffset >= 0
                                && declaration.Scope.EndOffset
                                    <= body.IL.Length
                                && declaration.Scope.EndOffset
                                    > declaration.Scope.StartOffset
                                && CSharpNaming.IsUsableIdentifier(
                                    declaration.Name))
                            .Select(declaration => declaration.Name)
                            .FirstOrDefault());
            }
            foreach (var declaration in declarations)
                AddLoss(function, declaration, failure);
        }
    }

    static string? TrySplit(IrFunction function, PdbLocalDeclaration[] declarations, MethodBody body)
    {
        int slot = declarations[0].SlotIndex;
        for (int i = 1; i < declarations.Length; i++)
            if (declarations[i - 1].Scope.EndOffset > declarations[i].Scope.StartOffset)
                return "multiple Portable PDB declarations overlap for the same physical slot";

        var boundaries = new HashSet<int> { 0 };
        var reader = new ILReader(body.IL.AsSpan());
        while (reader.HasNext)
        {
            if (!reader.TrySkip(reader.ReadILOpcode()) || reader.Offset > body.IL.Length)
                return "the raw instruction boundaries could not be established";
            boundaries.Add(reader.Offset);
        }
        if (declarations.Any(d => !boundaries.Contains(d.Scope.StartOffset)
            || !boundaries.Contains(d.Scope.EndOffset)))
            return "a scope boundary does not identify a raw IL instruction boundary";

        if (function.Locals[slot].Kind is TypeRefKind.Pinned or TypeRefKind.ByRef
            || function.Locals[slot].ContainsUnsupported)
            return "splitting pinned, ref, or unsupported local storage is not proven";
        if (!function.Regions.IsDefaultOrEmpty
            || function.Descendants.Any(n => n is UnsupportedNode))
            return "the raw exception or unsupported control flow does not prove independent storage";

        var blocks = function.Body.Blocks;
        var edges = Cfg.Build(blocks);
        // A backwards edge could carry the other scope's last store into a
        // previously assigned logical local. Definite assignment alone cannot
        // disprove that; this bounded importer deliberately declines re-entry.
        for (int i = 0; i < edges.Count; i++)
            if (edges[i].LeavesRegion || edges[i].ExternalTargets.Count != 0
                || edges[i].Successors.Any(target => target <= i))
                return "backwards or unmodeled control flow can re-enter reused storage";

        var references = function.Descendants.Where(n => LocalIndex(n) == slot).ToArray();
        if (references.Length == 0)
            return "no raw local uses establish a binding for the available scoped declarations";

        var owners = new Dictionary<IrNode, int>();
        foreach (var reference in references)
        {
            int owner = Array.FindIndex(declarations,
                d => Contains(d.Scope, reference.SourceOffset));
            if (owner < 0)
                return "a raw local use lies outside every declaration scope";
            owners.Add(reference, owner);
        }
        if (owners.Values.Distinct().Count() != declarations.Length)
            return "a declaration has no raw local uses establishing its separate binding";

        if (references.Any(n => n is LoadLocalAddress))
        {
            // No call signature establishes general escape freedom. Address-taken
            // storage is accepted only in mutually exclusive raw paths, and only
            // for immediate uses, never a retained alias or returned reference.
            if (references.OfType<LoadLocalAddress>().Any(a => !IsImmediateAddressUse(a))
                || !HaveExclusivePaths(blocks, edges, owners))
                return "address-taken storage may communicate between scoped declarations";
        }

        var indices = new int[declarations.Length];
        indices[0] = slot;
        for (int i = 1; i < indices.Length; i++)
            indices[i] = function.Locals.Length + i - 1;

        // Clone only for a read-only proof: Clone shares Diagnostics and other
        // non-child fields. Immutable local tables are replaced, never mutated;
        // no pass, diagnostic writer, or parameter allocator runs on the trial.
        var trial = (IrFunction)function.Clone();
        for (int i = 1; i < declarations.Length; i++)
            trial.AddLocal(function.Locals[slot]);
        Rewrite(trial, slot, declarations, indices);
        var facts = new DataflowFacts();
        var readEarly = DefiniteAssignment.Compute(
            trial, blocks.Select(b => b.StartOffset).ToHashSet(), facts);
        if (facts.Bailed || indices.Any(readEarly.Contains))
            return "a scoped read is not definitely assigned by its own logical storage";

        // Commit only after every proof succeeds. Postorder replacement preserves
        // rewritten children when a StoreLocal contains another local reference.
        for (int i = 1; i < declarations.Length; i++)
            function.AddLocal(function.Locals[slot]);
        Rewrite(function, slot, declarations, indices);
        for (int i = 0; i < declarations.Length; i++)
        {
            int index = indices[i];
            function.LocalNames = function.LocalNames.SetItem(index, declarations[i].Name);
            function.LocalDeclarationBindings =
                function.LocalDeclarationBindings.SetItem(index, declarations[i]);
            var nested = function.LocalDeclaredInNestedScope;
            while (nested.Length < function.Locals.Length)
                nested = nested.Add(false);
            function.LocalDeclaredInNestedScope =
                nested.SetItem(index, !declarations[i].Scope.CoversMethodBody(body.IL.Length));
        }
        return null;
    }

    static bool IsImmediateAddressUse(LoadLocalAddress address) => address.Parent switch
    {
        Call call => call.Callee.ReturnType is { Namespace: "System", Name: "Void" }
            && call.Arguments.Any(argument => ReferenceEquals(argument, address)),
        InitObject init => ReferenceEquals(init.Address, address),
        LoadIndirect load => ReferenceEquals(load.Address, address),
        StoreIndirect store => ReferenceEquals(store.Address, address),
        _ => false,
    };

    static bool HaveExclusivePaths(
        IReadOnlyList<Block> blocks,
        IReadOnlyList<ILInspector.ControlFlow.BlockEdges> edges,
        Dictionary<IrNode, int> owners)
    {
        var reaching = Enumerable.Range(0, blocks.Count).Select(_ => new HashSet<int>()).ToArray();
        for (int i = 0; i < blocks.Count; i++)
        {
            var own = blocks[i].Descendants.Where(owners.ContainsKey)
                .Select(n => owners[n]).Distinct().ToArray();
            if (own.Length > 1
                || own.Length == 1 && reaching[i].Any(owner => owner != own[0]))
                return false;
            reaching[i].UnionWith(own);
            foreach (int target in edges[i].Successors)
                reaching[target].UnionWith(reaching[i]);
        }
        return true;
    }

    static void Rewrite(IrFunction function, int slot, PdbLocalDeclaration[] declarations, int[] indices)
    {
        foreach (var node in function.Descendants.Where(n => LocalIndex(n) == slot).Reverse().ToArray())
        {
            int owner = Array.FindIndex(declarations, d => Contains(d.Scope, node.SourceOffset));
            int index = indices[owner];
            IrNode replacement = node switch
            {
                LoadLocal load => new LoadLocal(index, load.Type),
                LoadLocalAddress address => new LoadLocalAddress(index, address.Type),
                StoreLocal store => new StoreLocal(
                    index, store.Type, (IrExpression)store.DetachChildren()[0]),
                _ => throw new InvalidOperationException("Unexpected raw local reference."),
            };
            replacement.InheritSourceOffset(node);
            node.ReplaceWith(replacement);
        }
    }

    static int LocalIndex(IrNode node) => node switch
    {
        LoadLocal load => load.Index,
        LoadLocalAddress address => address.Index,
        StoreLocal store => store.Index,
        _ => -1,
    };

    static bool Contains(LocalSlotScope scope, int offset)
        => offset >= scope.StartOffset && offset < scope.EndOffset;

    static void AddLoss(IrFunction function, PdbLocalDeclaration? declaration, string reason)
    {
        var location = declaration is { SlotIndex: >= 0 }
            ? DecompilerFidelityLocation.AtLocal(declaration.SlotIndex)
            : DecompilerFidelityLocation.Signature;
        string method = function.MetadataToken != 0
            ? $"{function.DeclaringType.Name}.{function.Name} (MethodDef 0x{function.MetadataToken:x8})"
            : $"{function.DeclaringType.Name}.{function.Name}";
        string local = declaration is null
            ? "Portable PDB local declarations"
            : $"PDB LocalVariable row {declaration.VariableRowId}, LocalScope row {declaration.ScopeRowId}, slot {declaration.SlotIndex}, scope [{declaration.Scope.StartOffset},{declaration.Scope.EndOffset})";
        string identity = $"{method}: {local}";
        function.LocalNameImportCauses = function.LocalNameImportCauses.Add(new DecompilerFidelityCause(
            DiagnosticIds.UnrepresentableMetadataName,
            location,
            nameof(PdbLocalDeclaration),
            identity,
            $"{identity}: {reason}",
            DecompilerFidelityDiscriminators.ScopedLocalNameUnavailable));
    }
}
