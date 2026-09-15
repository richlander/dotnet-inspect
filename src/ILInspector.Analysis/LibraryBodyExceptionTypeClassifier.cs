using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal readonly record struct ExceptionTypeQualification(
    bool? IsException,
    MetadataTypeDefinitionAddress Definition = default);

internal sealed class LibraryBodyExceptionTypeClassifier(
    MetadataReader reader,
    Func<AssemblyReferenceIdentity, AssemblyResolutionScope,
        MetadataTypeDefinitionName,
        (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?>
        resolveExternal)
{
    readonly object _gate = new();
    readonly Dictionary<ResolvableTypeReference, ExceptionTypeQualification>
        _classifications = [];
    bool? _isReferenceAssembly;

    internal bool IsReferenceAssembly
    {
        get
        {
            lock (_gate)
            {
                return _isReferenceAssembly ??= reader.IsAssembly
                    && AttributeReader.HasAttribute(
                        reader,
                        reader.GetAssemblyDefinition().GetCustomAttributes(),
                        "System.Runtime.CompilerServices.ReferenceAssemblyAttribute");
            }
        }
    }

    internal ExceptionTypeQualification Qualify(TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition
            || type.Resolution is not { } reference)
            return new(null);

        // All keys originate in this classifier's primary image. The retained
        // reference, unlike TypeRef's facade-normalized equality, keeps origin.
        lock (_gate)
        {
            if (_classifications.TryGetValue(reference, out ExceptionTypeQualification result))
                return result;
            result = QualifyCore(type);
            _classifications.Add(reference, result);
            return result;
        }
    }

    ExceptionTypeQualification QualifyCore(TypeRef type)
    {
        if (LibraryBodyTypeDefinitionResolution.Resolve(
                reader, type, resolveExternal) is not { } target)
            return new(null);
        if (target.DefiningReader.GetTypeDefinition(target.Definition)
                .GetGenericParameters().Count != 0)
            return new(null);

        TypeRef definition = TypeRefDecoder.Instance.GetTypeFromDefinition(
            target.DefiningReader, target.Definition, 0);
        bool? classification = Classify(target.DefiningReader, definition);
        if (classification != true)
            return new(classification);
        if (definition.Resolution is not { } reference
            || MetadataTypeDeclarationProbe.ProbeDefinition(
                target.DefiningReader, reference.Type)
                is not TypeDeclarationResult.Defined declared
            || declared.IsInterface
            || declared.IsValueType
            || declared.Definition.Value != MetadataTokens.GetToken(target.Definition))
            return new(null);
        Guid mvid = target.DefiningReader.GetGuid(
            target.DefiningReader.GetModuleDefinition().Mvid);
        return mvid == Guid.Empty
            ? new(null)
            : new(true, new(mvid, declared.Definition));
    }

    bool? Classify(MetadataReader currentReader, TypeRef type)
    {
        var visited = new HashSet<(MetadataReader, TypeDefinitionHandle)>();
        for (int count = 0; count < MetadataSafetyPolicy.MaxRelationshipNodes; count++)
        {
            if (type.Kind == TypeRefKind.Definition)
            {
                if (FrameworkIdentity.IsCoreLibraryType(type, "System", "Exception"))
                    return true;
                if (FrameworkIdentity.IsCoreLibraryType(type, "System", "Object")
                    || FrameworkIdentity.IsCoreLibraryType(type, "System", "ValueType")
                    || FrameworkIdentity.IsCoreLibraryType(type, "System", "Enum"))
                    return false;
            }

            if (LibraryBodyTypeDefinitionResolution.Resolve(
                    currentReader, type, resolveExternal) is not { } resolved
                || !visited.Add(resolved))
                return null;

            TypeDefinition definition = resolved.DefiningReader
                .GetTypeDefinition(resolved.Definition);
            if (definition.BaseType.IsNil)
                return false;
            currentReader = resolved.DefiningReader;
            type = LibraryBodyTypeDefinitionResolution.Decode(
                currentReader, definition.BaseType);
        }
        return null;
    }
}
