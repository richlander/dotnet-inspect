namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Locates each node that caps a function's fidelity below
/// <see cref="DecompilationFidelity.Full"/>, pairing it with the stable
/// <c>DEC####</c> code and a human reason — the optimization-remarks analog
/// (LLVM <c>-Rpass</c> / opt-viewer) for the decompiler (issue #637). The walk
/// applies the same predicate as <see cref="IrFunction.Fidelity"/>, so a remark
/// exists for every site that lowers fidelity and the view cannot drift
/// from the score. Fidelity is computed from the final tree (never asserted by a
/// pass), so a remark names the <em>IR site</em> and cause rather than a pass.
/// </summary>
public static class FidelityRemarks
{
    /// <summary>
    /// Compatibility view over the typed fidelity-cause census. Local and
    /// signature causes have no IL coordinate and retain the historical -1.
    /// </summary>
    public sealed record Remark(string Code, int Offset, string Node, string Reason);

    public static IReadOnlyList<Remark> Collect(IrFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Enumerate(function)
            .Select(static cause => new Remark(
                cause.Code,
                cause.Location.ILOffset ?? -1,
                cause.Node,
                cause.Reason))
            .ToArray();
    }

    internal static IReadOnlyList<DecompilerFidelityCause> CollectCauses(IrFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Enumerate(function).ToArray();
    }

    internal static bool HasAny(IrFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return Enumerate(function).Any();
    }

    static IEnumerable<DecompilerFidelityCause> Enumerate(IrFunction function)
    {
        foreach (var node in function.Descendants.Prepend(function))
        {
            switch (node)
            {
                case UnsupportedNode u:
                    yield return Cause(
                        DiagnosticIds.UnsupportedConstruct,
                        u.ILOffset >= 0
                            ? DecompilerFidelityLocation.AtIlOffset(u.ILOffset)
                            : LocationOf(u),
                        u,
                        $"{u.Opcode}: {u.Reason}",
                        u.Opcode);
                    continue;  // its own type checks are noise next to the explicit reason
                case LoadFunctionPointer:
                    yield return Cause(
                        DiagnosticIds.UnsupportedFunctionPointer,
                        LocationOf(node),
                        node,
                        "bare function-pointer load (ldftn/ldvirtftn) with no C# spelling");
                    break;
                case Call { HasUnverifiedByRefArgument: true }:
                case NewObject { HasUnverifiedByRefArgument: true }:
                    yield return Cause(
                        DiagnosticIds.UnverifiedByRefArgument,
                        LocationOf(node),
                        node,
                        "by-ref argument rendered against an unknown call-site ref-kind (out/in cannot be distinguished from ref)");
                    break;
                case LoadToken { Kind: not RuntimeTokenKind.Type }:
                    yield return Cause(
                        DiagnosticIds.UnsupportedRuntimeToken,
                        LocationOf(node),
                        node,
                        "runtime method/field token load (ldtoken) with no C# expression spelling");
                    break;
                case EndFilter:
                    yield return Cause(
                        DiagnosticIds.UnsupportedExceptionFilter,
                        LocationOf(node),
                        node,
                        "residual exception filter boundary (endfilter) with no standalone C# spelling");
                    break;
                case Continue:
                    yield return Cause(
                        DiagnosticIds.UnverifiedContinue,
                        LocationOf(node),
                        node,
                        "residual continue is not currently proven opcode-exact");
                    break;
                case LoadIndirect { IsVolatile: true }:
                case StoreIndirect { IsVolatile: true }:
                    yield return Cause(
                        DiagnosticIds.VolatileIndirectAccess,
                        LocationOf(node),
                        node,
                        "volatile. indirect access (volatile. ldind/stind) renders as a bare *p, dropping the acquire/release ordering — no faithful plain-C# spelling");
                    break;
            }

            List<TypeRef>? unsupportedTypes = null;
            foreach (var type in node.DirectTypes)
            {
                if (type.ContainsUnsupported
                    && (unsupportedTypes is null || !unsupportedTypes.Contains(type)))
                {
                    (unsupportedTypes ??= []).Add(type);
                }
            }
            if ((node as IrExpression)?.ResultType is { ContainsUnsupported: true } resultType
                && (unsupportedTypes is null || !unsupportedTypes.Contains(resultType)))
            {
                (unsupportedTypes ??= []).Add(resultType);
            }
            if (unsupportedTypes is not null)
            {
                string types = string.Join("; ", unsupportedTypes.Select(UnrepresentableTypeText));
                string discriminator = string.Join(
                    "; ",
                    unsupportedTypes
                        .SelectMany(static type => type.UnsupportedReasons())
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal));
                yield return Cause(
                    DiagnosticIds.UnsupportedType,
                    LocationOf(node),
                    node,
                    $"references an unrepresentable type ({types})",
                    discriminator);
            }

            if (CSharpSpellability.UnrepresentableMetadataNameReason(node) is { } nameReason)
            {
                yield return Cause(
                    DiagnosticIds.UnrepresentableMetadataName,
                    LocationOf(node),
                    node,
                    nameReason);
            }

            if (node is IrExpression { ResultType: null })
            {
                yield return Cause(
                    DiagnosticIds.UnknownResultType,
                    LocationOf(node),
                    node,
                    "expression result type is unknown (e.g. a slot merged from conflicting types)");
            }
        }

        foreach (int localIndex in UnraisedPinnedLocals(function))
        {
            yield return new DecompilerFidelityCause(
                DiagnosticIds.UnraisedPinnedLocal,
                DecompilerFidelityLocation.AtLocal(localIndex),
                "PinnedLocal",
                $"Pinned local V_{localIndex}",
                "referenced pinned local has no owning fixed statement and no faithful C# declaration",
                function.Locals[localIndex].ToDisplayString());
        }
    }

    static DecompilerFidelityCause Cause(
        string code,
        DecompilerFidelityLocation location,
        IrNode node,
        string reason,
        string? discriminator = null)
        => new(
            code,
            location,
            node.GetType().Name,
            node.Describe(),
            reason,
            discriminator);

    static IEnumerable<int> UnraisedPinnedLocals(IrFunction function)
    {
        HashSet<int>? pinned = null;
        for (int i = 0; i < function.Locals.Length; i++)
        {
            if (function.Locals[i].Kind == TypeRefKind.Pinned)
                (pinned ??= []).Add(i);
        }
        if (pinned is null)
            yield break;

        var fixedOwned = function.Descendants
            .OfType<Fixed>()
            .Select(static fixedStatement => fixedStatement.LocalIndex)
            .ToHashSet();
        var reported = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            int slot = node switch
            {
                LoadLocal load => load.Index,
                StoreLocal store => store.Index,
                LoadLocalAddress address => address.Index,
                _ => -1,
            };
            if (slot >= 0
                && pinned.Contains(slot)
                && !fixedOwned.Contains(slot)
                && reported.Add(slot))
            {
                yield return slot;
            }
        }
    }

    static string UnrepresentableTypeText(TypeRef type)
    {
        string display = type.ToDisplayString();
        string raw = RawTypeText(type);
        return raw == display ? display : $"{display}; metadata: {raw}";
    }

    static string RawTypeText(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => type.Namespace.Length == 0 ? type.Name : $"{type.Namespace}.{type.Name}",
            TypeRefKind.GenericInstance => $"{RawTypeText(type.ElementType!)}<{string.Join(", ", type.TypeArguments.Select(RawTypeText))}>",
            TypeRefKind.SzArray => $"{RawTypeText(type.ElementType!)}[]",
            TypeRefKind.Array => $"{RawTypeText(type.ElementType!)}[{new string(',', type.Rank - 1)}]",
            TypeRefKind.ByRef => $"ref {RawTypeText(type.ElementType!)}",
            TypeRefKind.Pointer => $"{RawTypeText(type.ElementType!)}*",
            TypeRefKind.Pinned => $"pinned {RawTypeText(type.ElementType!)}",
            _ => type.ToDisplayString(),
        };

    static DecompilerFidelityLocation LocationOf(IrNode node)
    {
        if (node.SourceOffset >= 0)
            return DecompilerFidelityLocation.AtIlOffset(node.SourceOffset);

        for (IrNode? n = node; n is not null; n = n.Parent)
            if (n is Block b)
                return DecompilerFidelityLocation.AtIlOffset(b.StartOffset);
        return DecompilerFidelityLocation.Signature;
    }
}
