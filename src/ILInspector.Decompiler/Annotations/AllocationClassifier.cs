using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// Surfaces heap allocations the C# source hides — boxing, object and array
/// construction, closures, state machines, delegates, and reference-type
/// enumerators. A pure, read-only walk over the freshly imported IR: it runs at
/// the <see cref="AnnotationStage.Imported"/> stage on purpose, before
/// DelegateConstructionPass and the raise fold <c>newobj</c>s into higher-level
/// shapes, so every allocation is still a literal node.
///
/// Positive-only: it reports the allocations it can see and stays silent
/// otherwise. It never claims a method is allocation-free.
/// </summary>
public sealed class AllocationClassifier : IHiddenFactClassifier
{
    static readonly AnnotationDescriptor Box = new("alloc.box", AnnotationCategory.Allocation, "boxes a value type");
    static readonly AnnotationDescriptor Array = new("alloc.array", AnnotationCategory.Allocation, "allocates an array");
    static readonly AnnotationDescriptor NewObj = new("alloc.new", AnnotationCategory.Allocation, "allocates an object");
    static readonly AnnotationDescriptor Closure = new("alloc.closure", AnnotationCategory.Allocation, "allocates a closure");
    static readonly AnnotationDescriptor StateMachine = new("alloc.statemachine", AnnotationCategory.Allocation, "allocates a state machine");
    static readonly AnnotationDescriptor Delegate = new("alloc.delegate", AnnotationCategory.Allocation, "allocates a delegate");
    static readonly AnnotationDescriptor Enumerator = new("alloc.enumerator", AnnotationCategory.Allocation, "allocates an enumerator");

    public AnnotationCategory Category => AnnotationCategory.Allocation;

    public AnnotationStage Stage => AnnotationStage.Imported;

    public IReadOnlyList<Annotation> Classify(IrFunction function)
    {
        var cachedDelegates = FindCachedDelegates(function);
        var annotations = new List<Annotation>();

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Box box:
                    annotations.Add(Make(Box, box, box.Type.ToDisplayString()));
                    break;

                case NewArray array:
                    annotations.Add(Make(Array, array, $"{array.ElementType.ToDisplayString()}[]"));
                    break;

                case NewObject newObject:
                    if (ClassifyNewObject(newObject, cachedDelegates, function.TypeShapes) is { } allocation)
                        annotations.Add(allocation);
                    break;

                // The foreach enumerator allocation is a call-return fact, not an
                // alloc opcode: callvirt GetEnumerator hands back a reference-type
                // enumerator. A struct enumerator (List<T>.Enumerator) returns by
                // value and allocates nothing, so the value-type hint gates this.
                case Call call when AllocatesEnumerator(call):
                    annotations.Add(Make(Enumerator, call, call.Callee.ReturnType.ToDisplayString()));
                    break;
            }
        }

        return annotations;
    }

    static Annotation? ClassifyNewObject(NewObject node, HashSet<NewObject> cachedDelegates,
        IReadOnlyDictionary<TypeRef, TypeShape> shapes)
    {
        var type = node.Constructor.DeclaringType;

        // A value-type newobj constructs in place (a struct constructor, e.g.
        // new Span<int>(...) or new KeyValuePair<int, int>(...)) — no heap
        // allocation, so it is not an allocation fact. Suppressing it keeps the
        // layer honest; a wrong allocation claim would undermine the descriptive
        // contract. Value-type-ness is known here for generic/signature contexts
        // (the VALUETYPE hint) and for same-assembly types (the resolved shape);
        // a bare cross-assembly struct token (new DateTime(...)) carries neither
        // and is a known gap pending metadata-aware resolution.
        if (IsValueType(type, shapes))
            return null;

        string name = MetadataName(type);

        // The delegate constructor signature — .ctor(object, native int) — is an
        // exact structural marker, so user delegates are caught too, not only
        // Func/Action. A cached stateless lambda materialises at most once.
        if (IsDelegateConstructor(node.Constructor))
        {
            var conditionality = cachedDelegates.Contains(node)
                ? AnnotationConditionality.CachedOnce
                : AnnotationConditionality.Always;
            return Make(Delegate, node, type.ToDisplayString(), conditionality);
        }

        if (name.Contains("c__DisplayClass"))
            return Make(Closure, node, type.ToDisplayString());

        if (name.Contains(">d__"))
            return Make(StateMachine, node, type.ToDisplayString());

        return Make(NewObj, node, type.ToDisplayString());
    }

    static Annotation Make(AnnotationDescriptor descriptor, IrNode node, string? detail,
        AnnotationConditionality conditionality = AnnotationConditionality.Always)
        => new(descriptor, node.SourceOffset, detail, conditionality, node);

    /// <summary>
    /// True when the constructed type is known to be a value type — from the
    /// VALUETYPE hint (present in generic/signature contexts) or the resolved
    /// same-assembly shape. A bare cross-assembly struct token carries neither,
    /// so this is precision-limited, never wrong: it suppresses only confirmed
    /// value types.
    /// </summary>
    static bool IsValueType(TypeRef type, IReadOnlyDictionary<TypeRef, TypeShape> shapes)
        => type.DeclaredValueTypeHint == ValueTypeHint.ValueType
            || (shapes is not null && shapes.TryGetValue(type, out var shape) && shape is TypeShape.ValueType or TypeShape.Enum);

    static bool AllocatesEnumerator(Call call)
    {
        if (call.Callee.Name != "GetEnumerator")
            return false;

        var returnType = call.Callee.ReturnType;
        return returnType.DeclaredValueTypeHint switch
        {
            // A struct enumerator returns by value: no heap allocation.
            ValueTypeHint.ValueType => false,
            ValueTypeHint.ReferenceType => true,
            // Outside a signature context the hint is absent; fall back to the
            // interface name, which only the reference-type enumerators carry.
            _ => MetadataName(returnType).StartsWith("IEnumerator", StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// The metadata name of the named definition, unwrapping a generic instance
    /// (whose own <see cref="TypeRef.Name"/> is empty — identity lives on the
    /// element type).
    /// </summary>
    static string MetadataName(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance ? type.ElementType?.Name ?? "" : type.Name;

    static bool IsDelegateConstructor(MethodRef constructor)
    {
        var parameters = constructor.ParameterTypes;
        return parameters.Length == 2
            && IsSystemType(parameters[0], "Object")
            && IsSystemType(parameters[1], "IntPtr");
    }

    static bool IsSystemType(TypeRef type, string name)
        => type.Kind == TypeRefKind.Definition && type.Namespace == "System" && type.Name == name;

    /// <summary>
    /// Finds the delegate allocations the C# compiler caches: a stateless lambda
    /// is built once and stowed in a <c>&lt;&gt;9__</c> cache field. <c>dup</c>
    /// spills through a stack slot at import, so the cached <c>newobj</c> is
    /// reached as <c>StoreStackSlot(slot, newobj)</c> paired with
    /// <c>StoreField(&lt;&gt;9__, LoadStackSlot(slot))</c>; an uncached store
    /// holds the <c>newobj</c> directly.
    /// </summary>
    static HashSet<NewObject> FindCachedDelegates(IrFunction function)
    {
        var slotValues = new Dictionary<int, IrExpression>();
        var cacheStores = new List<StoreField>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreStackSlot store:
                    slotValues[store.Slot] = store.Value;
                    break;
                case StoreField field when field.Field.Name.StartsWith("<>9", StringComparison.Ordinal):
                    cacheStores.Add(field);
                    break;
            }
        }

        var cached = new HashSet<NewObject>();
        foreach (var store in cacheStores)
        {
            var source = store.Value switch
            {
                NewObject direct => direct,
                LoadStackSlot load when slotValues.TryGetValue(load.Slot, out var value) => value as NewObject,
                _ => null,
            };
            if (source is not null)
                cached.Add(source);
        }
        return cached;
    }
}
