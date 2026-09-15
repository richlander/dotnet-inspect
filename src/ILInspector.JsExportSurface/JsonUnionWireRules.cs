using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

internal static class JsonUnionWireRules
{
    internal static IEnumerable<ApiMember> CaseConstructors(ApiType type) =>
        type.Members.Where(member =>
            member.Kind == "constructor"
            && !member.IsStatic
            && member.Accessibility is null or "public"
            && member.SignatureModel is { Parameters.Count: 1 } signature
            && signature.Parameters[0].Modifier is not ("ref" or "in" or "out"));

    internal static JsExportUnion Describe(
        ApiType type,
        LibraryBodyIndex? bodyIndex,
        IReadOnlyDictionary<int, MethodIdentity> methods)
    {
        JsExportUnion Unsupported(string reason) => new()
        {
            Definition = type,
            SerializationUnsupportedReason = reason,
        };

        if (bodyIndex is null)
            return Unsupported("union case signature evidence is unavailable");
        if (type.Kind != "struct")
            return Unsupported("only value-type union conventions are supported");
        if (type.JsonConverterAttributeCount > 0
            || type.HasUnsupportedJsonWireAttributes)
        {
            return Unsupported("union has unsupported wire-shaping attributes");
        }
        if (type.JsonPropertyNamingPolicy == JsonWireNamingPolicy.Unsupported)
            return Unsupported("union serializer context options are unsupported");
        if (type.Members.Any(member =>
            member.Kind == "constructor"
            && !member.IsStatic
            && member.Accessibility is null or "public"
            && (member.SignatureModel is null
                || member.SignatureDecodeStatus == SignatureDecodeStatus.Degraded)))
        {
            return Unsupported("union constructor signature metadata is unavailable or degraded");
        }

        ApiMember[] valueProperties =
        [
            .. type.Members.Where(member =>
                member.Kind == "property"
                && member.Name == "Value"
                && !member.IsStatic
                && member.Accessibility is null or "public"
                && member.HasGetter == true
                && member.GetterAccessibility is null or "public"
                && member.SignatureModel is { ParameterCount: 0 }),
        ];
        if (valueProperties is not [var value]
            || value.GetterToken is not { } getterToken
            || !methods.TryGetValue(getterToken, out MethodIdentity? getter)
            || !IsOwnMethod(getter, type, bodyIndex)
            || getter.IsStatic
            || !getter.ParameterTypes.IsEmpty
            || !IsSystemObject(getter.ReturnType)
            || value.SignatureDecodeStatus == SignatureDecodeStatus.Degraded)
        {
            return Unsupported("union has no supported public object Value getter");
        }

        var cases = new List<TypeRef>();
        foreach (ApiMember constructor in CaseConstructors(type))
        {
            if (constructor.SignatureDecodeStatus == SignatureDecodeStatus.Degraded
                || constructor.MetadataToken is not { } token
                || !methods.TryGetValue(token, out MethodIdentity? method)
                || !IsOwnMethod(method, type, bodyIndex)
                || method.Name != ".ctor"
                || method.IsStatic
                || method.ParameterTypes is not [var caseType]
                || !IsSupportedCaseShape(caseType))
            {
                return Unsupported("union case signature evidence is unsupported");
            }
            cases.Add(caseType);
        }

        return cases.Count == 0
            ? Unsupported("union has no supported case constructors")
            : new JsExportUnion
            {
                Definition = type,
                CaseTypes = cases,
                IncludesNull = true,
            };
    }

    static bool IsOwnMethod(
        MethodIdentity method,
        ApiType type,
        LibraryBodyIndex bodyIndex) =>
        method.ModuleVersionId == bodyIndex.ModuleIdentity.ModuleVersionId
        && method.DeclaringType.Resolution is
        {
            Origin: TypeReferenceOrigin.CurrentAssembly,
            Type: { } definition,
        }
        && definition == type.DefinitionName;

    static bool IsSystemObject(TypeRef type) =>
        type.Kind == TypeRefKind.Definition
        && type.Namespace == "System"
        && type.Name == "Object"
        && type.TrustedFrameworkAssembly
        && type.Assembly == TypeRef.CoreLibrary;

    static bool IsSupportedCaseShape(TypeRef type)
    {
        var pending = new Stack<TypeRef>();
        pending.Push(type);
        while (pending.TryPop(out TypeRef? current))
        {
            if (current.Kind is not (TypeRefKind.Definition
                or TypeRefKind.GenericInstance
                or TypeRefKind.GenericParameter
                or TypeRefKind.SzArray))
            {
                return false;
            }
            if (current.ElementType is { } element)
                pending.Push(element);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
        }
        return true;
    }
}
