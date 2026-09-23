using System.Collections.Immutable;
using System.Reflection.Metadata;
using InertText;

namespace ILInspector.Metadata;

internal sealed class MetadataTypeIdentityProjector(
    Action beforeCreateNode,
    Func<string, InertString> retain,
    Func<string, Exception> reject)
{
    readonly Action _beforeCreateNode =
        beforeCreateNode
        ?? throw new ArgumentNullException(nameof(beforeCreateNode));
    readonly Func<string, InertString> _retain =
        retain
        ?? throw new ArgumentNullException(nameof(retain));
    readonly Func<string, Exception> _reject =
        reject
        ?? throw new ArgumentNullException(nameof(reject));

    internal MetadataTypeIdentity Project(TypeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _beforeCreateNode();
        return node switch
        {
            PrimitiveTypeNode primitive =>
                new MetadataTypeIdentity.Primitive(
                    _retain(primitive.Name)),
            NamedTypeNode named =>
                new MetadataTypeIdentity.Named(
                    ProjectNamed(named),
                    IsValueType: !named.IsReferenceType),
            GenericTypeNode generic =>
                new MetadataTypeIdentity.GenericInstance(
                    ProjectNamed(generic),
                    IsValueType: !generic.IsReferenceType,
                    generic.Arguments
                        .Select(Project)
                        .ToImmutableArray()),
            GenericParameterNode parameter =>
                new MetadataTypeIdentity.GenericParameter(
                    parameter.IsMethodParameter,
                    parameter.Index),
            SZArrayTypeNode array =>
                new MetadataTypeIdentity.SzArray(
                    Project(array.ElementType)),
            MDArrayTypeNode array =>
                new MetadataTypeIdentity.Array(
                    Project(array.ElementType),
                    array.Rank,
                    array.ArraySizes,
                    array.ArrayLowerBounds),
            PointerTypeNode pointer =>
                new MetadataTypeIdentity.Pointer(
                    Project(pointer.ElementType)),
            ByRefTypeNode byReference =>
                new MetadataTypeIdentity.ByReference(
                    Project(byReference.ElementType)),
            FunctionPointerTypeNode functionPointer =>
                new MetadataTypeIdentity.FunctionPointer(
                    ProjectSignature(functionPointer.Signature)),
            ModifiedTypeNode modified =>
                new MetadataTypeIdentity.Modified(
                    Project(modified.Modifier),
                    Project(modified.Inner),
                    modified.IsRequired),
            PinnedTypeNode pinned =>
                new MetadataTypeIdentity.Pinned(
                    Project(pinned.Inner)),
            _ => throw _reject(
                "A decoded type tree cannot be detached completely."),
        };
    }

    MetadataMethodSignatureIdentity ProjectSignature(
        MethodSignature<TypeNode> signature)
    {
        _beforeCreateNode();
        var parameters =
            ImmutableArray.CreateBuilder<MetadataTypeIdentity>(
                signature.ParameterTypes.Length);
        foreach (TypeNode parameter in signature.ParameterTypes)
            parameters.Add(Project(parameter));
        return new(
            signature.Header.RawValue,
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            Project(signature.ReturnType),
            parameters.MoveToImmutable());
    }

    MetadataNamedTypeIdentity ProjectNamed(TypeNode node)
    {
        _beforeCreateNode();
        MetadataTypeNameParts parts = node switch
        {
            NamedTypeNode { MetadataName: { } name } => name,
            GenericTypeNode { MetadataName: { } name } => name,
            _ => throw _reject(
                "A named type lacks a complete structured metadata name."),
        };
        MetadataTypeScopeDescriptor scope =
            node.ExactScope
            ?? throw _reject(
                "A named type lacks an exact metadata scope.");
        var segments =
            ImmutableArray.CreateBuilder<InertString>(
                parts.Segments.Count);
        var introducedCounts =
            ImmutableArray.CreateBuilder<int>(
                parts.Segments.Count);
        foreach (string segment in parts.Segments)
            segments.Add(_retain(segment));
        for (int index = 0; index < parts.Segments.Count; index++)
        {
            introducedCounts.Add(
                parts.IntroducedTypeParameterCounts is { } trusted
                    && trusted.Count == parts.Segments.Count
                    ? trusted[index]
                    : MetadataNameArity.OfSegment(
                        parts.Segments[index]));
        }
        return new(
            ProjectScope(scope),
            _retain(parts.Namespace),
            segments.MoveToImmutable(),
            introducedCounts.MoveToImmutable());
    }

    internal MetadataTypeScopeIdentity ProjectScope(
        MetadataTypeScopeDescriptor scope)
    {
        _beforeCreateNode();
        MetadataAssemblyIdentity? assembly = null;
        if (scope.Assembly is not null)
        {
            _beforeCreateNode();
            assembly = new MetadataAssemblyIdentity(
                _retain(scope.Assembly.Name),
                scope.Assembly.Version,
                scope.Assembly.Culture is null
                    ? null
                    : _retain(scope.Assembly.Culture),
                scope.Assembly.PublicKeyToken is null
                    ? null
                    : _retain(scope.Assembly.PublicKeyToken));
        }

        return new(
            scope.Kind,
            scope.ModuleVersionId,
            scope.ModuleName is null
                ? null
                : _retain(scope.ModuleName),
            assembly);
    }
}
