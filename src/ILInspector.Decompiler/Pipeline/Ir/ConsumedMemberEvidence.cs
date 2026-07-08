namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Product-owned typed member evidence consumed by raised and lowered IR nodes.
/// Consumers such as ReturnToSender route these references instead of switching
/// over C# surface shapes or reconstructing member names.
/// </summary>
public sealed record ConsumedMemberEvidence(
    MethodRef? Method = null,
    FieldRef? Field = null,
    TypeRef? RecordShellType = null,
    bool AllowTargetRoot = false)
{
    public static IEnumerable<ConsumedMemberEvidence> From(IrNode node)
    {
        switch (node)
        {
            case Call call:
                yield return new(Method: call.Callee);
                break;
            case NewObject creation:
                yield return new(Method: creation.Constructor, AllowTargetRoot: true);
                break;
            case LoadProperty load:
                yield return new(Method: load.Accessor);
                break;
            case StoreProperty store:
                yield return new(Method: store.Accessor);
                break;
            case EventSubscription subscription:
                yield return new(Method: subscription.Accessor);
                break;
            case LoadFunctionPointer load:
                yield return new(Method: load.Method);
                break;
            case AddressOfMethod address:
                yield return new(Method: address.Method);
                break;
            case DelegateCreation creation:
                yield return new(Method: creation.Method);
                break;
            case IncrementDecrement { ConsumedMethod: { } operatorMethod }:
                yield return new(Method: operatorMethod);
                break;
            case RecursivePropertyDeclarationPattern pattern:
                yield return new(Method: pattern.Accessor);
                break;
            case NullCoalescingPropertyAssignment assignment:
                yield return new(Method: assignment.Setter);
                break;
            case DeconstructionAssignment deconstruction:
                if (deconstruction.ConsumedDeconstructMethod is { } deconstruct)
                    yield return new(Method: deconstruct);
                foreach (var target in deconstruction.Targets)
                {
                    if (target.Accessor is { } accessor)
                        yield return new(Method: accessor);
                    if (target.Field is { } field)
                        yield return new(Field: field);
                }
                break;
            case ForeachStatement foreachStatement:
                foreach (var method in foreachStatement.ConsumedMemberRefs)
                    yield return new(Method: method);
                break;
            case UsingStatement usingStatement:
                foreach (var method in usingStatement.ConsumedMemberRefs)
                    yield return new(Method: method);
                break;
            case LoadField load:
                yield return new(Field: load.Field);
                break;
            case StoreField store:
                yield return new(Field: store.Field);
                break;
            case LoadFieldAddress address:
                yield return new(Field: address.Field);
                break;
            case NullCoalescingFieldAssignment assignment:
                yield return new(Field: assignment.Field);
                break;
            case NullCoalescingFieldAssignmentExpression assignment:
                yield return new(Field: assignment.Field);
                break;
            case ObjectInitializerExpression initializer:
                yield return new(Method: initializer.Creation.Constructor, AllowTargetRoot: true);
                foreach (var evidence in FromInitializerEntries(initializer.Entries))
                    yield return evidence;
                break;
            case WithExpression withExpression:
                yield return new(RecordShellType: withExpression.ResultType);
                foreach (var evidence in FromInitializerEntries(withExpression.Entries))
                    yield return evidence;
                break;
        }
    }

    static IEnumerable<ConsumedMemberEvidence> FromInitializerEntries(IEnumerable<InitializerEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.ConsumedMethod is { } method)
                yield return new(Method: method, AllowTargetRoot: true);
            if (entry.ConsumedField is { } field)
                yield return new(Field: field);
            foreach (var block in entry.Arguments.OfType<InitializerBlock>())
                foreach (var evidence in FromInitializerEntries(block.Entries))
                    yield return evidence;
        }
    }
}
