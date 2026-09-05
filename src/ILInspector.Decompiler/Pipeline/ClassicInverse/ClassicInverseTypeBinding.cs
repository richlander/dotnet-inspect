using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed record ClassicInverseTypeBinding(ImmutableArray<TypeRef> Arguments)
{
    internal static readonly ClassicInverseTypeBinding Identity = new([]);
    internal string? Failure { get; private set; }

    internal static ClassicInverseTypeBinding FromRequest(ClassicInverseRequest request, ClassicInverseBudget budget)
    {
        if (!budget.Charge())
            return Identity;
        TypeRef machine = request.KickoffBody.Locals[request.StateMachineLocal];
        int arity = request.ExecutionBody.DeclaringTypeGenericParameterNames.Length;
        if (machine.Kind != TypeRefKind.GenericInstance)
            return arity == 0 ? Identity : Invalid();
        if (request.Relationship is null || machine.TypeArguments.Length != arity)
            return Invalid();
        var seen = new HashSet<TypeRef>();
        foreach (TypeRef argument in machine.TypeArguments)
        {
            if (!budget.Charge())
                return Identity;
            int count = argument.Kind switch
            {
                TypeRefKind.GenericParameter => request.KickoffBody.DeclaringTypeGenericParameterNames.Length,
                TypeRefKind.MethodGenericParameter => request.KickoffBody.Signature.GenericParameterCount,
                _ => 0,
            };
            if (argument.GenericParameterIndex < 0 || argument.GenericParameterIndex >= count
                || !seen.Add(argument))
                return Invalid();
        }
        return new(machine.TypeArguments);

        static ClassicInverseTypeBinding Invalid()
            => new([]) { Failure = "the authenticated kickoff and execution generic contexts do not bind completely" };
    }

    internal TypeRef Type(TypeRef type, ClassicInverseBudget budget)
    {
        if (Arguments.IsDefaultOrEmpty)
            return type;
        if (!Admit(type, budget))
            return type;
        return type.Instantiate(Arguments, []);
    }

    internal TypeRef? OptionalType(TypeRef? type, ClassicInverseBudget budget)
        => type is null ? null : Type(type, budget);

    internal FieldRef Field(FieldRef field, ClassicInverseBudget budget)
        => Arguments.IsDefaultOrEmpty ? field : field with
        {
            DeclaringType = Type(field.DeclaringType, budget),
            Type = Type(field.Type, budget),
        };

    internal MethodRef Method(MethodRef method, ClassicInverseBudget budget)
        => Arguments.IsDefaultOrEmpty ? method : method with
        {
            DeclaringType = Type(method.DeclaringType, budget),
            ReturnType = Type(method.ReturnType, budget),
            ParameterTypes = Types(method.ParameterTypes, budget),
            TypeArguments = Types(method.TypeArguments, budget),
        };

    internal IrTypeFactSnapshot Facts(IrTypeFactSnapshot facts, ClassicInverseBudget budget)
        => Arguments.IsDefaultOrEmpty ? facts : facts with
        {
            TypeShapes = Map(facts.TypeShapes, budget),
            TypeFactIdentities = Map(facts.TypeFactIdentities, budget),
            AmbiguousTypeFacts = Set(facts.AmbiguousTypeFacts, budget),
            EnumMembers = Map(facts.EnumMembers, budget),
            EnumUnderlyingTypes = Map(facts.EnumUnderlyingTypes, budget, bindValue: type => Type(type, budget)),
            CollectionInitializerTypes = Set(facts.CollectionInitializerTypes, budget),
            UnionTypes = Set(facts.UnionTypes, budget),
            ByRefLikeTypes = Set(facts.ByRefLikeTypes, budget),
            InterfaceTypes = Set(facts.InterfaceTypes, budget),
        };

    internal ImmutableArray<TypeRef> Types(ImmutableArray<TypeRef> types, ClassicInverseBudget budget)
    {
        if (types.IsDefaultOrEmpty || Arguments.IsDefaultOrEmpty)
            return types;
        var result = ImmutableArray.CreateBuilder<TypeRef>();
        foreach (TypeRef type in types)
        {
            if (budget.Exhausted || Failure is not null)
                break;
            result.Add(Type(type, budget));
        }
        return result.ToImmutable();
    }

    ImmutableDictionary<TypeRef, T> Map<T>(
        IReadOnlyDictionary<TypeRef, T> values,
        ClassicInverseBudget budget,
        Func<T, T>? bindValue = null)
    {
        var result = ImmutableDictionary.CreateBuilder<TypeRef, T>();
        if (budget.Exhausted || Failure is not null)
            return result.ToImmutable();
        foreach (var (key, value) in values)
        {
            if (!budget.Charge())
                break;
            TypeRef bound = Type(key, budget);
            T boundValue = bindValue is null ? value : bindValue(value);
            if (budget.Exhausted || Failure is not null)
                break;
            if (!result.TryAdd(bound, boundValue))
            {
                Failure = "generic output binding aliases distinct type-fact keys";
                break;
            }
        }
        return result.ToImmutable();
    }

    ImmutableHashSet<TypeRef> Set(IEnumerable<TypeRef> values, ClassicInverseBudget budget)
    {
        var result = ImmutableHashSet.CreateBuilder<TypeRef>();
        if (budget.Exhausted || Failure is not null)
            return result.ToImmutable();
        foreach (TypeRef type in values)
        {
            TypeRef bound = Type(type, budget);
            if (budget.Exhausted || Failure is not null)
                break;
            result.Add(bound);
        }
        return result.ToImmutable();
    }

    bool Admit(TypeRef type, ClassicInverseBudget budget)
    {
        if (!budget.Charge())
            return false;
        if (type.Kind == TypeRefKind.GenericParameter
            && (type.GenericParameterIndex < 0 || type.GenericParameterIndex >= Arguments.Length))
        {
            Failure = "an execution type refers outside the authenticated machine generic context";
            return false;
        }
        if (type.ElementType is { } element && !Admit(element, budget))
            return false;
        foreach (TypeRef argument in type.TypeArguments)
            if (!Admit(argument, budget))
                return false;
        foreach (var modifier in type.CustomModifiers)
            if (!Admit(modifier.Modifier, budget))
                return false;
        return true;
    }
}
