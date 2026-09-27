using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Analysis.Planning.Experiments;

// Experiment only: the three questions MethodClassificationScanner.Scan answers
// in one superset pass, asked with mixed terminals. Classification is exclusive
// in the legacy order: P/Invoke, then async, then pointer signature.
public static class ClassifiedScope
{
    public static bool IsScopedType(MetadataReader reader, TypeDefinition type) =>
        !reader.StringComparer.StartsWith(type.Name, "<");

    public static bool IsScopedMethod(MetadataReader reader, MethodDefinition method)
    {
        if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            return false;
        StringHandle name = method.Name;
        return !(reader.StringComparer.StartsWith(name, "get_")
            || reader.StringComparer.StartsWith(name, "set_")
            || reader.StringComparer.StartsWith(name, "add_")
            || reader.StringComparer.StartsWith(name, "remove_"));
    }

    public static bool IsPInvoke(MethodDefinition method) =>
        (method.Attributes & MethodAttributes.PinvokeImpl) != 0;

    public static bool IsAsync(MetadataReader reader, MethodDefinition method) =>
        MethodClassificationScanner.ClassifyAsyncMethod(reader, method) is not null;

    public static bool HasPointerSignature(MetadataReader reader, MethodDefinition method)
    {
        try
        {
            MethodSignature<bool> signature = method.DecodeSignature(PointerProvider.Instance, null);
            if (signature.ReturnType)
                return true;
            foreach (bool parameter in signature.ParameterTypes)
            {
                if (parameter)
                    return true;
            }

            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public enum Kind : byte { None, PInvoke, Async, Pointer }

    public static Kind Classify(MetadataReader reader, TypeDefinition type, MethodDefinition method)
    {
        if (!IsScopedType(reader, type) || !IsScopedMethod(reader, method))
            return Kind.None;
        if (IsPInvoke(method))
            return Kind.PInvoke;
        if (IsAsync(reader, method))
            return Kind.Async;
        return HasPointerSignature(reader, method) ? Kind.Pointer : Kind.None;
    }

    sealed class PointerProvider : ISignatureTypeProvider<bool, object?>
    {
        public static PointerProvider Instance { get; } = new();

        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public bool GetSZArrayType(bool elementType) => elementType;
        public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
        public bool GetByReferenceType(bool elementType) => elementType;
        public bool GetPointerType(bool elementType) => true;
        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
        {
            if (genericType)
                return true;
            foreach (bool argument in typeArguments)
            {
                if (argument)
                    return true;
            }

            return false;
        }
        public bool GetGenericMethodParameter(object? context, int index) => false;
        public bool GetGenericTypeParameter(object? context, int index) => false;
        public bool GetFunctionPointerType(MethodSignature<bool> signature) => true;
        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => modifier || unmodifiedType;
        public bool GetPinnedType(bool elementType) => elementType;
    }
}

/// <summary>Rows terminal: the P/Invoke method tokens.</summary>
public sealed class PInvokeRowsProducer : MethodDefinitionProducer<int, List<int>, ImmutableArray<int>>
{
    PInvokeRowsProducer() : base("Experiment.PInvokeRows", 1, 0, MethodDefinitionLayers.Declaration) { }

    public static PInvokeRowsProducer Instance { get; } = new();

    internal override int Visit(scoped MethodDefinitionView view) =>
        ClassifiedScope.IsScopedType(view.Reader, view.TypeDefinition)
        && ClassifiedScope.IsScopedMethod(view.Reader, view.MethodDefinition)
        && ClassifiedScope.IsPInvoke(view.MethodDefinition)
            ? view.Token
            : 0;

    internal override List<int> Seed() => [];

    internal override List<int> Accumulate(List<int> accumulator, int fact)
    {
        if (fact != 0)
            accumulator.Add(fact);
        return accumulator;
    }

    internal override ImmutableArray<int> Complete(List<int> accumulator, MethodDefinitionCompletionView completion) =>
        [.. accumulator];
}

/// <summary>Count terminal: async methods that are not P/Invoke.</summary>
public sealed class ClassifiedAsyncCountProducer : MethodDefinitionProducer<bool, int, int>
{
    ClassifiedAsyncCountProducer() : base("Experiment.ClassifiedAsyncCount", 1, 0, MethodDefinitionLayers.Declaration) { }

    public static ClassifiedAsyncCountProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        ClassifiedScope.IsScopedType(view.Reader, view.TypeDefinition)
        && ClassifiedScope.IsScopedMethod(view.Reader, view.MethodDefinition)
        && !ClassifiedScope.IsPInvoke(view.MethodDefinition)
        && ClassifiedScope.IsAsync(view.Reader, view.MethodDefinition);

    internal override int Seed() => 0;
    internal override int Accumulate(int accumulator, bool fact) => fact ? accumulator + 1 : accumulator;
    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
}

/// <summary>Exists terminal: a pointer-bearing signature on a method that is neither P/Invoke nor async.</summary>
public sealed class PointerSignaturePresenceProducer : MethodDefinitionProducer<bool, bool, bool>
{
    PointerSignaturePresenceProducer() : base("Experiment.PointerSignaturePresence", 1, 0, MethodDefinitionLayers.Declaration) { }

    public static PointerSignaturePresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        ClassifiedScope.Classify(view.Reader, view.TypeDefinition, view.MethodDefinition) == ClassifiedScope.Kind.Pointer;

    internal override bool Seed() => false;
    internal override bool Accumulate(bool accumulator, bool fact) => accumulator | fact;
    internal override bool Complete(bool accumulator, MethodDefinitionCompletionView completion) => accumulator;
    internal override bool Settles(bool fact) => fact;
}

public readonly record struct ClassifiedAnswer(int PInvokeRows, int AsyncCount, bool AnyPointer)
{
    public override string ToString() => $"p={PInvokeRows};a={AsyncCount};u={(AnyPointer ? 1 : 0)}";
}

public static class ClassifiedFusion
{
    static readonly WorkDescription s_fused = Plan(
        new(PInvokeRowsProducer.Instance),
        new(ClassifiedAsyncCountProducer.Instance),
        new(PointerSignaturePresenceProducer.Instance, ProducerTerminal.Exists));
    static readonly WorkDescription s_pinvoke = Plan(new ProducerRequest(PInvokeRowsProducer.Instance));
    static readonly WorkDescription s_async = Plan(new ProducerRequest(ClassifiedAsyncCountProducer.Instance));
    static readonly WorkDescription s_pointer = Plan(new ProducerRequest(PointerSignaturePresenceProducer.Instance, ProducerTerminal.Exists));

    static WorkDescription Plan(params ProducerRequest[] requests) =>
        ProducerPlanner.Plan(requests) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    static T Value<T>(MethodDefinitionExecution execution, ProducerDeclaration<T> producer)
    {
        ProducerResult<T> result = execution.ResultOf(producer);
        return result.HasValue ? result.Value! : throw new InvalidDataException(result.Failure?.Message);
    }

    public static ClassifiedAnswer PlannedFused(string path, PEReader peReader)
    {
        MethodDefinitionExecution execution = MethodDefinitionExecution.Execute(s_fused, path, peReader);
        return new(
            Value(execution, PInvokeRowsProducer.Instance).Length,
            Value(execution, ClassifiedAsyncCountProducer.Instance),
            Value(execution, PointerSignaturePresenceProducer.Instance));
    }

    public static ClassifiedAnswer PlannedSeparate(string path, PEReader peReader) =>
        new(
            Value(MethodDefinitionExecution.Execute(s_pinvoke, path, peReader), PInvokeRowsProducer.Instance).Length,
            Value(MethodDefinitionExecution.Execute(s_async, path, peReader), ClassifiedAsyncCountProducer.Instance),
            Value(MethodDefinitionExecution.Execute(s_pointer, path, peReader), PointerSignaturePresenceProducer.Instance));

    public static int PlannedAsyncOnly(string path, PEReader peReader) =>
        Value(MethodDefinitionExecution.Execute(s_async, path, peReader), ClassifiedAsyncCountProducer.Instance);

    /// <summary>The ideal for exactly this combination: one loop, type filter hoisted, pointer check stops once settled.</summary>
    public static ClassifiedAnswer HandFused(PEReader peReader)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        var pinvoke = new List<int>();
        int asyncCount = 0;
        bool anyPointer = false;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!ClassifiedScope.IsScopedType(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (!ClassifiedScope.IsScopedMethod(reader, method))
                    continue;
                if (ClassifiedScope.IsPInvoke(method))
                    pinvoke.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(methodHandle));
                else if (ClassifiedScope.IsAsync(reader, method))
                    asyncCount++;
                else if (!anyPointer && ClassifiedScope.HasPointerSignature(reader, method))
                    anyPointer = true;
            }
        }

        ImmutableArray<int> rows = [.. pinvoke];
        return new(rows.Length, asyncCount, anyPointer);
    }

    /// <summary>No fusion: three hand-rolled loops.</summary>
    public static ClassifiedAnswer HandSeparate(PEReader peReader)
    {
        MetadataReader reader = peReader.GetMetadataReader();
        var pinvoke = new List<int>();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!ClassifiedScope.IsScopedType(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (ClassifiedScope.IsScopedMethod(reader, method) && ClassifiedScope.IsPInvoke(method))
                    pinvoke.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(methodHandle));
            }
        }

        int asyncCount = 0;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (!ClassifiedScope.IsScopedType(reader, type))
                continue;
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (ClassifiedScope.IsScopedMethod(reader, method) && !ClassifiedScope.IsPInvoke(method) && ClassifiedScope.IsAsync(reader, method))
                    asyncCount++;
            }
        }

        bool anyPointer = false;
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                if (ClassifiedScope.Classify(reader, type, reader.GetMethodDefinition(methodHandle)) == ClassifiedScope.Kind.Pointer)
                {
                    anyPointer = true;
                    goto done;
                }
            }
        }

    done:
        ImmutableArray<int> rows = [.. pinvoke];
        return new(rows.Length, asyncCount, anyPointer);
    }

    /// <summary>Superset fusion as the CLI consumes it: classify every row, then filter and count.</summary>
    public static ClassifiedAnswer Legacy(PEReader peReader)
    {
        List<ClassifiedMethodInfo> rows = MethodClassificationScanner.Scan(peReader);
        return new(
            rows.Count(static m => m.Classification == MethodClassification.PInvoke),
            rows.Count(static m => m.Classification is MethodClassification.RuntimeAsync or MethodClassification.StateMachineAsync),
            rows.Any(static m => m.Classification == MethodClassification.Unsafe));
    }
}

/// <summary>
/// Step A: the shared classification computed once per unit, cheapest first,
/// in the legacy order. The three questions read it as a same-unit fact.
/// </summary>
public enum MethodKind : byte { OutOfScope, PInvoke, Async, Other }

public sealed class MethodKindProducer : MethodDefinitionProducer<MethodKind, int, int>
{
    MethodKindProducer() : base("Experiment.MethodKind", 1, 0, MethodDefinitionLayers.Declaration) { }

    public static MethodKindProducer Instance { get; } = new();

    internal override MethodKind Visit(scoped MethodDefinitionView view)
    {
        if (!ClassifiedScope.IsScopedType(view.Reader, view.TypeDefinition)
            || !ClassifiedScope.IsScopedMethod(view.Reader, view.MethodDefinition))
        {
            return MethodKind.OutOfScope;
        }

        if (ClassifiedScope.IsPInvoke(view.MethodDefinition))
            return MethodKind.PInvoke;
        return ClassifiedScope.IsAsync(view.Reader, view.MethodDefinition)
            ? MethodKind.Async
            : MethodKind.Other;
    }

    internal override int Seed() => 0;
    internal override int Accumulate(int accumulator, MethodKind fact) => fact == MethodKind.OutOfScope ? accumulator : accumulator + 1;
    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
    internal override bool ClassifiesUnits => true;
    internal override int UnitClass(MethodKind fact) => (int)fact;
}

public sealed class SharedPInvokeRowsProducer : MethodDefinitionProducer<int, List<int>, ImmutableArray<int>>
{
    SharedPInvokeRowsProducer()
        : base("Experiment.SharedPInvokeRows", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(MethodKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit)])
    {
    }

    public static SharedPInvokeRowsProducer Instance { get; } = new();

    internal override int Visit(scoped MethodDefinitionView view) =>
        view.FactOf(MethodKindProducer.Instance) == MethodKind.PInvoke ? view.Token : 0;

    internal override List<int> Seed() => [];

    internal override List<int> Accumulate(List<int> accumulator, int fact)
    {
        if (fact != 0)
            accumulator.Add(fact);
        return accumulator;
    }

    internal override ImmutableArray<int> Complete(List<int> accumulator, MethodDefinitionCompletionView completion) =>
        [.. accumulator];
}

public sealed class SharedAsyncCountProducer : MethodDefinitionProducer<bool, int, int>
{
    SharedAsyncCountProducer()
        : base("Experiment.SharedAsyncCount", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(MethodKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit)])
    {
    }

    public static SharedAsyncCountProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        view.FactOf(MethodKindProducer.Instance) == MethodKind.Async;

    internal override int Seed() => 0;
    internal override int Accumulate(int accumulator, bool fact) => fact ? accumulator + 1 : accumulator;
    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
}

public sealed class SharedPointerPresenceProducer : MethodDefinitionProducer<bool, bool, bool>
{
    SharedPointerPresenceProducer()
        : base("Experiment.SharedPointerPresence", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(MethodKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit)])
    {
    }

    public static SharedPointerPresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        view.FactOf(MethodKindProducer.Instance) == MethodKind.Other
        && ClassifiedScope.HasPointerSignature(view.Reader, view.MethodDefinition);

    internal override bool Seed() => false;
    internal override bool Accumulate(bool accumulator, bool fact) => accumulator | fact;
    internal override bool Complete(bool accumulator, MethodDefinitionCompletionView completion) => accumulator;
    internal override bool Settles(bool fact) => fact;
}

public static class SharedClassifiedFusion
{
    static readonly WorkDescription s_fused = Plan(
        new ProducerRequest(SharedPInvokeRowsProducer.Instance),
        new ProducerRequest(SharedAsyncCountProducer.Instance),
        new ProducerRequest(SharedPointerPresenceProducer.Instance, ProducerTerminal.Exists));

    static WorkDescription Plan(params ProducerRequest[] requests) =>
        ProducerPlanner.Plan(requests) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    static T Value<T>(MethodDefinitionExecution execution, ProducerDeclaration<T> producer)
    {
        ProducerResult<T> result = execution.ResultOf(producer);
        return result.HasValue ? result.Value! : throw new InvalidDataException(result.Failure?.Message);
    }

    public static ClassifiedAnswer PlannedFused(string path, PEReader peReader)
    {
        MethodDefinitionExecution execution = MethodDefinitionExecution.Execute(s_fused, path, peReader);
        return new(
            Value(execution, SharedPInvokeRowsProducer.Instance).Length,
            Value(execution, SharedAsyncCountProducer.Instance),
            Value(execution, SharedPointerPresenceProducer.Instance));
    }
}

/// <summary>Step D: the three questions declare scope guards on the kind instead of reading it.</summary>
public sealed class GuardedPInvokeRowsProducer : MethodDefinitionProducer<int, List<int>, ImmutableArray<int>>
{
    GuardedPInvokeRowsProducer()
        : base("Experiment.GuardedPInvokeRows", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(MethodKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit, 1UL << (int)MethodKind.PInvoke)])
    {
    }

    public static GuardedPInvokeRowsProducer Instance { get; } = new();

    internal override int Visit(scoped MethodDefinitionView view) => view.Token;
    internal override List<int> Seed() => [];

    internal override List<int> Accumulate(List<int> accumulator, int fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ImmutableArray<int> Complete(List<int> accumulator, MethodDefinitionCompletionView completion) => [.. accumulator];
}

public sealed class GuardedAsyncCountProducer : MethodDefinitionProducer<bool, int, int>
{
    GuardedAsyncCountProducer()
        : base("Experiment.GuardedAsyncCount", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(MethodKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit, 1UL << (int)MethodKind.Async)])
    {
    }

    public static GuardedAsyncCountProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) => true;
    internal override int Seed() => 0;
    internal override int Accumulate(int accumulator, bool fact) => fact ? accumulator + 1 : accumulator;
    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
}

public sealed class GuardedPointerPresenceProducer : MethodDefinitionProducer<bool, bool, bool>
{
    GuardedPointerPresenceProducer()
        : base("Experiment.GuardedPointerPresence", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(MethodKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit, 1UL << (int)MethodKind.Other)])
    {
    }

    public static GuardedPointerPresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        ClassifiedScope.HasPointerSignature(view.Reader, view.MethodDefinition);

    internal override bool Seed() => false;
    internal override bool Accumulate(bool accumulator, bool fact) => accumulator | fact;
    internal override bool Complete(bool accumulator, MethodDefinitionCompletionView completion) => accumulator;
    internal override bool Settles(bool fact) => fact;
}

public static class GuardedClassifiedFusion
{
    static readonly WorkDescription s_fused =
        ProducerPlanner.Plan(
        [
            new ProducerRequest(GuardedPInvokeRowsProducer.Instance),
            new ProducerRequest(GuardedAsyncCountProducer.Instance),
            new ProducerRequest(GuardedPointerPresenceProducer.Instance, ProducerTerminal.Exists),
        ]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    static T Value<T>(MethodDefinitionExecution execution, ProducerDeclaration<T> producer)
    {
        ProducerResult<T> result = execution.ResultOf(producer);
        return result.HasValue ? result.Value! : throw new InvalidDataException(result.Failure?.Message);
    }

    public static ClassifiedAnswer PlannedFused(string path, PEReader peReader)
    {
        MethodDefinitionExecution execution = MethodDefinitionExecution.Execute(s_fused, path, peReader);
        return new(
            Value(execution, GuardedPInvokeRowsProducer.Instance).Length,
            Value(execution, GuardedAsyncCountProducer.Instance),
            Value(execution, GuardedPointerPresenceProducer.Instance));
    }
}

public sealed class TypeScopedKindProducer : MethodDefinitionProducer<MethodKind, int, int>
{
    TypeScopedKindProducer() : base("Experiment.TypeScopedKind", 1, 0, MethodDefinitionLayers.Declaration) { }

    public static TypeScopedKindProducer Instance { get; } = new();

    internal override bool HasTypeScope => true;

    internal override bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        ClassifiedScope.IsScopedType(reader, type);

    internal override MethodKind Visit(scoped MethodDefinitionView view)
    {
        if (!ClassifiedScope.IsScopedMethod(view.Reader, view.MethodDefinition))
            return MethodKind.OutOfScope;
        if (ClassifiedScope.IsPInvoke(view.MethodDefinition))
            return MethodKind.PInvoke;
        return ClassifiedScope.IsAsync(view.Reader, view.MethodDefinition)
            ? MethodKind.Async
            : MethodKind.Other;
    }

    internal override int Seed() => 0;
    internal override int Accumulate(int accumulator, MethodKind fact) => fact == MethodKind.OutOfScope ? accumulator : accumulator + 1;
    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
    internal override bool ClassifiesUnits => true;
    internal override int UnitClass(MethodKind fact) => (int)fact;
}

/// <summary>Type-scope step: the same guarded questions over a kind producer with a type-scope predicate.</summary>
public sealed class TypeScopedPInvokeRowsProducer : MethodDefinitionProducer<int, List<int>, ImmutableArray<int>>
{
    TypeScopedPInvokeRowsProducer()
        : base("Experiment.TypeScopedPInvokeRows", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(TypeScopedKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit, 1UL << (int)MethodKind.PInvoke)])
    {
    }

    public static TypeScopedPInvokeRowsProducer Instance { get; } = new();

    internal override int Visit(scoped MethodDefinitionView view) => view.Token;
    internal override List<int> Seed() => [];

    internal override List<int> Accumulate(List<int> accumulator, int fact)
    {
        accumulator.Add(fact);
        return accumulator;
    }

    internal override ImmutableArray<int> Complete(List<int> accumulator, MethodDefinitionCompletionView completion) => [.. accumulator];
}

public sealed class TypeScopedAsyncCountProducer : MethodDefinitionProducer<bool, int, int>
{
    TypeScopedAsyncCountProducer()
        : base("Experiment.TypeScopedAsyncCount", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(TypeScopedKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit, 1UL << (int)MethodKind.Async)])
    {
    }

    public static TypeScopedAsyncCountProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) => true;
    internal override int Seed() => 0;
    internal override int Accumulate(int accumulator, bool fact) => fact ? accumulator + 1 : accumulator;
    internal override int Complete(int accumulator, MethodDefinitionCompletionView completion) => accumulator;
}

public sealed class TypeScopedPointerPresenceProducer : MethodDefinitionProducer<bool, bool, bool>
{
    TypeScopedPointerPresenceProducer()
        : base("Experiment.TypeScopedPointerPresence", 1, 0, MethodDefinitionLayers.Declaration,
            static () => [new ProducerDependency(TypeScopedKindProducer.Instance, ProducerDependencyKind.VisitNeedsVisit, 1UL << (int)MethodKind.Other)])
    {
    }

    public static TypeScopedPointerPresenceProducer Instance { get; } = new();

    internal override bool Visit(scoped MethodDefinitionView view) =>
        ClassifiedScope.HasPointerSignature(view.Reader, view.MethodDefinition);

    internal override bool Seed() => false;
    internal override bool Accumulate(bool accumulator, bool fact) => accumulator | fact;
    internal override bool Complete(bool accumulator, MethodDefinitionCompletionView completion) => accumulator;
    internal override bool Settles(bool fact) => fact;
}

public static class TypeScopedClassifiedFusion
{
    static readonly WorkDescription s_fused =
        ProducerPlanner.Plan(
        [
            new ProducerRequest(TypeScopedPInvokeRowsProducer.Instance),
            new ProducerRequest(TypeScopedAsyncCountProducer.Instance),
            new ProducerRequest(TypeScopedPointerPresenceProducer.Instance, ProducerTerminal.Exists),
        ]) is ProducerPlanResult.Accepted accepted
            ? accepted.Description
            : throw new InvalidOperationException("The experiment request must plan.");

    static T Value<T>(MethodDefinitionExecution execution, ProducerDeclaration<T> producer)
    {
        ProducerResult<T> result = execution.ResultOf(producer);
        return result.HasValue ? result.Value! : throw new InvalidDataException(result.Failure?.Message);
    }

    public static ClassifiedAnswer PlannedFused(string path, PEReader peReader)
    {
        MethodDefinitionExecution execution = MethodDefinitionExecution.Execute(s_fused, path, peReader);
        return new(
            Value(execution, TypeScopedPInvokeRowsProducer.Instance).Length,
            Value(execution, TypeScopedAsyncCountProducer.Instance),
            Value(execution, TypeScopedPointerPresenceProducer.Instance));
    }
}

/// <summary>K4: the classified request as a typed fused kernel.</summary>
internal struct MethodKindClassifier : IUnitClassifier<MethodKind>
{
    public bool TypeInScope(MetadataReader reader, TypeDefinition type) =>
        ClassifiedScope.IsScopedType(reader, type);

    public MethodKind Classify(ref MethodDefinitionUnit unit)
    {
        MetadataReader reader = unit.Reader;
        MethodDefinition method = unit.MethodDefinition;
        if (!ClassifiedScope.IsScopedMethod(reader, method))
            return MethodKind.OutOfScope;
        if (ClassifiedScope.IsPInvoke(method))
            return MethodKind.PInvoke;
        return ClassifiedScope.IsAsync(reader, method) ? MethodKind.Async : MethodKind.Other;
    }
}

internal struct PInvokeRowsSink : IFusedSink<MethodKind>
{
    public ImmutableArray<int>.Builder Rows;

    public readonly bool IsDone => false;

    public void Accept(ref MethodDefinitionUnit unit, MethodKind key)
    {
        if (key == MethodKind.PInvoke)
            Rows.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(unit.MethodHandle));
    }
}

internal struct AsyncCountSink : IFusedSink<MethodKind>
{
    public int Count;

    public readonly bool IsDone => false;

    public void Accept(ref MethodDefinitionUnit unit, MethodKind key)
    {
        if (key == MethodKind.Async)
            Count++;
    }
}

internal struct PointerExistsSink : IFusedSink<MethodKind>
{
    public bool Found;

    public readonly bool IsDone => Found;

    public void Accept(ref MethodDefinitionUnit unit, MethodKind key)
    {
        if (key == MethodKind.Other && ClassifiedScope.HasPointerSignature(unit.Reader, unit.MethodDefinition))
            Found = true;
    }
}

public static class TypedClassifiedFusion
{
    public static ClassifiedAnswer Run(PEReader peReader)
    {
        var sinks = new FusedPair<PInvokeRowsSink, FusedPair<AsyncCountSink, PointerExistsSink, MethodKind>, MethodKind>
        {
            First = new PInvokeRowsSink { Rows = ImmutableArray.CreateBuilder<int>() },
        };
        FusedKernel.Run<MethodKindClassifier, MethodKind, FusedPair<PInvokeRowsSink, FusedPair<AsyncCountSink, PointerExistsSink, MethodKind>, MethodKind>>(
            peReader,
            ref sinks);
        ImmutableArray<int> rows = sinks.First.Rows.DrainToImmutable();
        return new(rows.Length, sinks.Second.First.Count, sinks.Second.Second.Found);
    }
}
