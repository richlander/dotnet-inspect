using ILInspector.CSharp;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Extracts neutral <see cref="ConstructorBodyFacts"/> from a decompiled constructor's
/// IR so ReturnToSender and the C# seam can reconstruct constructor-chain initializers
/// and primary-constructor shells without reading Decompiler IR themselves. This is the
/// single owner of the IR pattern-matches those consumers previously duplicated in the
/// harness; the facts it returns carry only strings and indices.
/// </summary>
public static class ConstructorBodyFactExtractor
{
    /// <summary>
    /// Produces the chain-call and primary-constructor-prologue facts for
    /// <paramref name="function"/>. Callers should only apply the constructor-shaped
    /// facts to actual constructors; the extraction itself is body-shape driven.
    /// </summary>
    public static ConstructorBodyFacts Extract(IrFunction function)
        => new(ChainParameterTypes(function), PrimaryConstructorPrologue(function));

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
