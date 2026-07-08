namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Product-owned typed member evidence consumed by raised and lowered IR nodes.
/// Consumers such as ReturnToSender route these references instead of switching
/// over C# surface shapes or reconstructing member names.
/// </summary>
public readonly record struct ConsumedMemberEvidence(
    MethodRef? Method = null,
    FieldRef? Field = null,
    TypeRef? RecordShellType = null,
    bool AllowTargetRoot = false)
{
    /// <summary>
    /// Whether this consumed member may seed a closure member requirement on the
    /// target root itself. Construction/initialization contexts set
    /// <see cref="AllowTargetRoot"/> directly (a constructor or initializer setter
    /// is legitimately re-seeded there); otherwise membership follows the member
    /// kind — constructors (reserved metadata names) and property accessors
    /// (<see cref="MethodRef.AccessorKind"/>) are owned by their type/property and
    /// are not re-seeded, every other member is. Member kind comes from typed
    /// metadata evidence, so consumers such as ReturnToSender need no C#
    /// name-prefix sniffing.
    /// </summary>
    public bool EffectiveAllowTargetRoot
        => AllowTargetRoot || (Method is { } method && AllowsTargetRootMember(method));

    static bool AllowsTargetRootMember(MethodRef method)
        => method.Name is not ".ctor" and not ".cctor" && !IsPropertyAccessor(method);

    // Typed accessor evidence is authoritative. Only when metadata is unresolved
    // (AccessorKind.Unknown) fall back to the reserved get_/set_ name prefixes,
    // mirroring PropertySugarPass's accessor-evidence handling — so an unresolved
    // cross-assembly accessor is not misclassified as an ordinary member and
    // re-seeded on the target root.
    static bool IsPropertyAccessor(MethodRef method)
        => method.AccessorKind is AccessorKind.PropertyGet or AccessorKind.PropertySet
            || (method.AccessorKind is AccessorKind.Unknown
                && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                    || method.Name.StartsWith("set_", StringComparison.Ordinal)));

    public static void AddFrom(IrNode node, List<ConsumedMemberEvidence> evidence)
    {
        switch (node)
        {
            case Call call:
                evidence.Add(new(Method: call.Callee));
                break;
            case NewObject creation:
                evidence.Add(new(Method: creation.Constructor, AllowTargetRoot: true));
                break;
            case LoadProperty load:
                evidence.Add(new(Method: load.Accessor));
                break;
            case StoreProperty store:
                evidence.Add(new(Method: store.Accessor));
                break;
            case EventSubscription subscription:
                evidence.Add(new(Method: subscription.Accessor));
                break;
            case LoadFunctionPointer load:
                evidence.Add(new(Method: load.Method));
                break;
            case AddressOfMethod address:
                evidence.Add(new(Method: address.Method));
                break;
            case DelegateCreation creation:
                evidence.Add(new(Method: creation.Method));
                break;
            case IncrementDecrement { ConsumedMethod: { } operatorMethod }:
                evidence.Add(new(Method: operatorMethod));
                break;
            case RecursivePropertyDeclarationPattern pattern:
                evidence.Add(new(Method: pattern.Accessor));
                break;
            case NullCoalescingPropertyAssignment assignment:
                evidence.Add(new(Method: assignment.Setter));
                break;
            case DeconstructionAssignment deconstruction:
                if (deconstruction.ConsumedDeconstructMethod is { } deconstruct)
                    evidence.Add(new(Method: deconstruct));
                foreach (var target in deconstruction.Targets)
                {
                    if (target.Accessor is { } accessor)
                        evidence.Add(new(Method: accessor));
                    if (target.Field is { } field)
                        evidence.Add(new(Field: field));
                }
                break;
            case ForeachStatement foreachStatement:
                foreach (var method in foreachStatement.ConsumedMemberRefs)
                    evidence.Add(new(Method: method));
                break;
            case UsingStatement usingStatement:
                foreach (var method in usingStatement.ConsumedMemberRefs)
                    evidence.Add(new(Method: method));
                break;
            case LoadField load:
                evidence.Add(new(Field: load.Field));
                break;
            case StoreField store:
                evidence.Add(new(Field: store.Field));
                break;
            case LoadFieldAddress address:
                evidence.Add(new(Field: address.Field));
                break;
            case NullCoalescingFieldAssignment assignment:
                evidence.Add(new(Field: assignment.Field));
                break;
            case NullCoalescingFieldAssignmentExpression assignment:
                evidence.Add(new(Field: assignment.Field));
                break;
            case ObjectInitializerExpression initializer:
                evidence.Add(new(Method: initializer.Creation.Constructor, AllowTargetRoot: true));
                AddFromInitializerEntries(initializer.Entries, evidence);
                break;
            case WithExpression withExpression:
                evidence.Add(new(RecordShellType: withExpression.ResultType));
                AddFromInitializerEntries(withExpression.Entries, evidence);
                break;
        }
    }

    static void AddFromInitializerEntries(IEnumerable<InitializerEntry> entries, List<ConsumedMemberEvidence> evidence)
    {
        foreach (var entry in entries)
        {
            if (entry.ConsumedMethod is { } method)
                evidence.Add(new(Method: method, AllowTargetRoot: true));
            if (entry.ConsumedField is { } field)
                evidence.Add(new(Field: field));
            foreach (var block in entry.Arguments.OfType<InitializerBlock>())
                AddFromInitializerEntries(block.Entries, evidence);
        }
    }
}
