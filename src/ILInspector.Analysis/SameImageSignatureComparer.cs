using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class SameImageSignatureComparer(
    AssemblyReferenceIdentity? currentAssembly,
    string? currentModuleName)
{
    internal bool CanResolveToCurrentModule(TypeRef type)
    {
        TypeReferenceOrigin? origin = Definition(type).Resolution?.Origin;
        return origin is null || IsCurrentModule(origin);
    }

    bool IsCurrentModule(TypeReferenceOrigin origin)
        => origin switch
        {
            TypeReferenceOrigin.CurrentAssembly => true,
            TypeReferenceOrigin.AssemblyReference reference =>
                currentAssembly is { } current
                && reference.Assembly.IsEquivalentTo(current),
            TypeReferenceOrigin.ModuleReference module =>
                module.ModuleName.Equals(
                    currentModuleName,
                    StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    internal bool Matches(TypeRef left, TypeRef right)
    {
        if (!TypeRef.ExactSignatureEquals(left, right))
            return false;

        var pending = new Stack<(TypeRef Left, TypeRef Right)>();
        var visited = new HashSet<(TypeRef Left, TypeRef Right)>(
            TypeRefPairReferenceComparer.Instance);
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            (TypeRef currentLeft, TypeRef currentRight) = pending.Pop();
            if (!visited.Add((currentLeft, currentRight)))
                continue;
            if (!OriginsMatch(currentLeft, currentRight))
                return false;

            if (currentLeft.ElementType is not null)
                pending.Push((currentLeft.ElementType, currentRight.ElementType!));
            if (currentLeft.ModifierType is not null)
                pending.Push((currentLeft.ModifierType, currentRight.ModifierType!));
            if (currentLeft.UnmodifiedType is not null)
                pending.Push((currentLeft.UnmodifiedType, currentRight.UnmodifiedType!));
            for (int i = 0; i < currentLeft.TypeArguments.Length; i++)
                pending.Push((currentLeft.TypeArguments[i], currentRight.TypeArguments[i]));
            if (currentLeft.FunctionPointerSignature is { } leftSignature)
            {
                MethodSignature<TypeRef> rightSignature =
                    currentRight.FunctionPointerSignature!.Value;
                pending.Push((leftSignature.ReturnType, rightSignature.ReturnType));
                for (int i = 0; i < leftSignature.ParameterTypes.Length; i++)
                {
                    pending.Push((
                        leftSignature.ParameterTypes[i],
                        rightSignature.ParameterTypes[i]));
                }
            }
        }

        return true;
    }

    bool OriginsMatch(TypeRef left, TypeRef right)
    {
        TypeRef leftDefinition = Definition(left);
        TypeRef rightDefinition = Definition(right);
        if (leftDefinition.Assembly == TypeRef.CoreLibrary
            && rightDefinition.Assembly == TypeRef.CoreLibrary)
        {
            return leftDefinition.TrustedFrameworkAssembly
                && rightDefinition.TrustedFrameworkAssembly;
        }

        TypeReferenceOrigin? leftOrigin = leftDefinition.Resolution?.Origin;
        TypeReferenceOrigin? rightOrigin = rightDefinition.Resolution?.Origin;
        if (leftOrigin is null || rightOrigin is null)
            return true;
        if (IsCurrentModule(leftOrigin) && IsCurrentModule(rightOrigin))
            return true;

        return (leftOrigin, rightOrigin) switch
        {
            (TypeReferenceOrigin.AssemblyReference leftReference,
                TypeReferenceOrigin.AssemblyReference rightReference) =>
                leftReference.Assembly.IsEquivalentTo(rightReference.Assembly),
            (TypeReferenceOrigin.IntrinsicCoreLibrary,
                TypeReferenceOrigin.IntrinsicCoreLibrary) => true,
            (TypeReferenceOrigin.ModuleReference leftModule,
                TypeReferenceOrigin.ModuleReference rightModule) =>
                leftModule.ModuleName == rightModule.ModuleName,
            _ => false,
        };
    }

    static TypeRef Definition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    sealed class TypeRefPairReferenceComparer
        : IEqualityComparer<(TypeRef Left, TypeRef Right)>
    {
        internal static TypeRefPairReferenceComparer Instance { get; } = new();

        public bool Equals(
            (TypeRef Left, TypeRef Right) x,
            (TypeRef Left, TypeRef Right) y)
            => ReferenceEquals(x.Left, y.Left)
                && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode((TypeRef Left, TypeRef Right) pair)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(pair.Left),
                RuntimeHelpers.GetHashCode(pair.Right));
    }
}
