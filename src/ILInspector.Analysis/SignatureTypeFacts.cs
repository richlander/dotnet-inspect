using System.Reflection.Metadata;

namespace ILInspector.Analysis;

internal static class SignatureTypeFacts
{
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
                        function.GenericParameterCount));
                foreach (TypeRef parameter
                    in function.ParameterTypes)
                {
                    pending.Push(
                        (parameter,
                            current.TypeParameterCount,
                            function.GenericParameterCount));
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
