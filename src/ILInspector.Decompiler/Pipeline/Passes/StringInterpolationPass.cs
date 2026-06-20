namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the simple csc <c>DefaultInterpolatedStringHandler</c> lowering into a
/// C# interpolated string. Scoped to straight-line handler construction followed
/// by <c>AppendLiteral(string)</c> / <c>AppendFormatted&lt;T&gt;(T)</c> calls and a
/// final <c>ToStringAndClear()</c> return in the same block.
/// </summary>
public sealed class StringInterpolationPass : IIrPass
{
    public string Name => "string-interpolation";

    sealed record Match(StoreLocal StoreHandler, List<ExpressionStatement> Appends, Return ReturnStatement);

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
    {
        foreach (var block in function.Descendants.OfType<Block>().ToList())
        {
            var children = block.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (TryMatch(function, children, i) is not { } match)
                    continue;

                var consumed = new IrNode[] { match.StoreHandler, match.ReturnStatement, };
                consumed = [.. consumed, .. match.Appends];
                if (!ReferencedOnlyWithin(function, match.StoreHandler.Index, consumed))
                    continue;

                var parts = new List<InterpolatedStringPart>();
                var values = new List<IrExpression>();
                foreach (var append in match.Appends)
                {
                    var call = (Call)append.Expression;
                    if (call.Callee.Name == "AppendLiteral")
                    {
                        parts.Add(InterpolatedStringPart.LiteralText((string)((Constant)call.Arguments[1]).Value!));
                    }
                    else
                    {
                        var callParts = call.DetachChildren().Cast<IrExpression>().ToList();
                        var value = callParts[1];
                        parts.Add(InterpolatedStringPart.FormattedValue(values.Count));
                        values.Add(value);
                    }
                }

                var interpolation = new InterpolatedStringExpression(parts, values);
                stepper.StepOver("raise DefaultInterpolatedStringHandler sequence to interpolated string", match.StoreHandler);
                match.ReturnStatement.ReplaceWith(new Return(interpolation));
                foreach (var append in match.Appends)
                    append.Detach();
                match.StoreHandler.Detach();
                return true;
            }
        }
        return false;
    }

    static Match? TryMatch(IrFunction function, IReadOnlyList<IrNode> statements, int start)
    {
        if (statements[start] is not StoreLocal { Value: NewObject handlerCtor } storeHandler
            || HasSourceLocalName(function, storeHandler.Index)
            || !IsDefaultInterpolatedStringHandlerCtor(handlerCtor))
        {
            return null;
        }

        var appends = new List<ExpressionStatement>();
        int i = start + 1;
        while (i < statements.Count
            && statements[i] is ExpressionStatement { Expression: Call append } expression
            && IsAppend(append, storeHandler.Index))
        {
            appends.Add(expression);
            i++;
        }

        if (appends.Count == 0
            || !CtorArgumentsMatchAppends(handlerCtor, appends)
            || i >= statements.Count
            || statements[i] is not Return { Value: Call toString } ret
            || !IsToStringAndClear(toString, storeHandler.Index))
        {
            return null;
        }

        return new Match(storeHandler, appends, ret);
    }

    static bool IsDefaultInterpolatedStringHandlerCtor(NewObject newObject)
        => newObject.Constructor is
            {
                Name: ".ctor",
                DeclaringType:
                {
                    Namespace: "System.Runtime.CompilerServices",
                    Name: "DefaultInterpolatedStringHandler",
                    Assembly: TypeRef.CoreLibrary or "System.Runtime",
                },
            };

    static bool HasSourceLocalName(IrFunction function, int index)
        => index >= 0
            && index < function.LocalNames.Length
            && !string.IsNullOrWhiteSpace(function.LocalNames[index]);

    static bool CtorArgumentsMatchAppends(NewObject handlerCtor, IReadOnlyList<ExpressionStatement> appends)
    {
        if (handlerCtor.Arguments is not
            [
                Constant { Value: int literalLength },
                Constant { Value: int formattedCount },
            ])
        {
            return false;
        }

        int actualLiteralLength = 0;
        int actualFormattedCount = 0;
        foreach (var append in appends)
        {
            var call = (Call)append.Expression;
            if (call.Callee.Name == "AppendLiteral")
                actualLiteralLength += ((string)((Constant)call.Arguments[1]).Value!).Length;
            else
                actualFormattedCount++;
        }

        return literalLength == actualLiteralLength && formattedCount == actualFormattedCount;
    }

    static bool IsAppend(Call call, int handlerSlot)
    {
        if (!IsHandlerCall(call)
            || call.Arguments is not [LoadLocalAddress receiver, ..]
            || receiver.Index != handlerSlot)
        {
            return false;
        }

        return call switch
        {
            { Callee.Name: "AppendLiteral", Arguments: [_, Constant { Value: string }] }
                when call.Callee.ParameterTypes is [{ Namespace: "System", Name: "String" }] => true,
            { Callee.Name: "AppendFormatted", Arguments.Count: 2 }
                when call.Callee.ParameterTypes.Length == 1 => true,
            _ => false,
        };
    }

    static bool IsToStringAndClear(Call call, int handlerSlot)
    {
        if (!IsHandlerCall(call)
            || call.Callee.Name != "ToStringAndClear"
            || call.Callee.ParameterTypes.Length != 0
            || call.Callee.ReturnType is not { Namespace: "System", Name: "String" }
            || call.Arguments is not [LoadLocalAddress receiver])
        {
            return false;
        }

        return receiver.Index == handlerSlot;
    }

    static bool IsHandlerCall(Call call)
        => call.Callee is
        {
            HasThis: true,
            DeclaringType:
            {
                Namespace: "System.Runtime.CompilerServices",
                Name: "DefaultInterpolatedStringHandler",
                Assembly: TypeRef.CoreLibrary or "System.Runtime",
            },
        };

    static bool ReferencedOnlyWithin(IrFunction function, int index, IrNode[] allowed)
    {
        foreach (var node in function.Descendants)
        {
            bool references = node switch
            {
                LoadLocal load => load.Index == index,
                StoreLocal store => store.Index == index,
                LoadLocalAddress address => address.Index == index,
                _ => false,
            };
            if (references && !allowed.Any(root => IsInside(node, root)))
                return false;
        }
        return true;
    }

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
