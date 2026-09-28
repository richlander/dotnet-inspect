using System.Reflection.Metadata;

namespace ILInspector.Analysis;

internal static class SignatureTypeFacts
{
    internal static bool HasCompleteIdentity(TypeRef type)
    {
        var pending = new Stack<TypeRef>();
        var visited = new HashSet<TypeRef>(
            ReferenceEqualityComparer.Instance);
        pending.Push(type);
        while (pending.TryPop(out TypeRef? candidate))
        {
            if (!visited.Add(candidate))
                continue;

            if (candidate.Kind == TypeRefKind.Unsupported)
            {
                if (candidate.UnmodifiedType is { } unmodified)
                {
                    if (candidate.ModifierType is not { } modifier
                        || candidate.FunctionPointerSignature is not null)
                    {
                        return false;
                    }
                    pending.Push(unmodified);
                    pending.Push(modifier);
                    continue;
                }

                if (candidate.ModifierType is not null
                    || candidate.FunctionPointerSignature
                        is not { } function
                    || function.ParameterTypes.IsDefault
                    || function.GenericParameterCount < 0
                    || function.RequiredParameterCount < 0
                    || function.RequiredParameterCount
                        > function.ParameterTypes.Length)
                {
                    return false;
                }

                pending.Push(function.ReturnType);
                foreach (TypeRef parameter
                    in function.ParameterTypes)
                {
                    pending.Push(parameter);
                }
                continue;
            }

            if (candidate.ModifierType is not null
                || candidate.UnmodifiedType is not null
                || candidate.FunctionPointerSignature is not null)
            {
                return false;
            }
            if (candidate.ElementType is { } element)
                pending.Push(element);
            foreach (TypeRef argument in candidate.TypeArguments)
                pending.Push(argument);
        }

        return true;
    }

    internal static bool IsMalformed(
        TypeRef type,
        int typeParameterCount,
        int methodParameterCount)
    {
        var pending = new Stack<(
            TypeRef Type,
            int TypeParameterCount,
            int MethodParameterCount)>();
        pending.Push(
            (type, typeParameterCount, methodParameterCount));
        while (pending.TryPop(out var current))
        {
            TypeRef candidate = current.Type;
            if (candidate.Kind
                    == TypeRefKind.GenericParameter
                && (candidate.GenericParameterIndex < 0
                    || candidate.GenericParameterIndex
                        >= current.TypeParameterCount)
                || candidate.Kind
                    == TypeRefKind.MethodGenericParameter
                && (candidate.GenericParameterIndex < 0
                    || candidate.GenericParameterIndex
                        >= current.MethodParameterCount))
            {
                return true;
            }

            if (candidate.Kind == TypeRefKind.Unsupported)
            {
                if (candidate.UnmodifiedType is { } unmodified)
                {
                    if (candidate.ModifierType is not { } modifier)
                        return true;
                    pending.Push(
                        (unmodified,
                            current.TypeParameterCount,
                            current.MethodParameterCount));
                    pending.Push(
                        (modifier,
                            current.TypeParameterCount,
                            current.MethodParameterCount));
                    continue;
                }

                if (candidate.FunctionPointerSignature
                    is not { } function
                    || function.ParameterTypes.IsDefault
                    || function.GenericParameterCount < 0
                    || function.RequiredParameterCount < 0
                    || function.RequiredParameterCount
                        > function.ParameterTypes.Length)
                {
                    return true;
                }

                pending.Push(
                    (function.ReturnType,
                        current.TypeParameterCount,
                        current.MethodParameterCount));
                foreach (TypeRef parameter
                    in function.ParameterTypes)
                {
                    pending.Push(
                        (parameter,
                            current.TypeParameterCount,
                            current.MethodParameterCount));
                }
                continue;
            }

            if (candidate.Kind == TypeRefKind.Array
                && (candidate.Rank <= 0
                    || candidate.ArraySizes.Length
                        > candidate.Rank
                    || candidate.ArrayLowerBounds.Length
                        > candidate.Rank))
            {
                return true;
            }

            if (candidate.ElementType is { } element)
            {
                pending.Push(
                    (element,
                        current.TypeParameterCount,
                        current.MethodParameterCount));
            }
            foreach (TypeRef argument
                in candidate.TypeArguments)
            {
                pending.Push(
                    (argument,
                        current.TypeParameterCount,
                        current.MethodParameterCount));
            }
        }

        return false;
    }
}
