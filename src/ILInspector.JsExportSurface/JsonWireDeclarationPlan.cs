using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface;

public readonly record struct JsonWireDeclarationIdentity(
    ApiType Type,
    JsonWireDirection Direction,
    bool IsSplit);

public sealed class JsonWireDeclarationPlan
{
    const string InertStringFullName = "InertText.InertString";
    const string ImmutableArrayFullName =
        "System.Collections.Immutable.ImmutableArray`1";
    const string JsonElementFullName = "System.Text.Json.JsonElement";

    readonly IReadOnlySet<ApiType> _splitTypes;
    readonly IReadOnlyDictionary<ApiType, JsonWireDeclarationIdentity>
        _singleDeclarations;
    readonly IReadOnlyDictionary<
        (ApiType Type, JsonWireDirection Direction),
        JsonWireDeclarationIdentity> _splitDeclarations;

    JsonWireDeclarationPlan(
        ApiType[] types,
        JsonWireDeclarationIdentity[] declarations,
        IReadOnlySet<ApiType> splitTypes,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
    {
        Types = types;
        Declarations = declarations;
        DeclaredTypesByScopedIdentity =
            declaredTypesByScopedIdentity;
        _splitTypes = splitTypes;
        _singleDeclarations = declarations
            .Where(declaration => !declaration.IsSplit)
            .ToDictionary(declaration => declaration.Type);
        _splitDeclarations = declarations
            .Where(declaration => declaration.IsSplit)
            .ToDictionary(declaration => (
                declaration.Type,
                declaration.Direction));
    }

    public ApiType[] Types { get; }

    public JsonWireDeclarationIdentity[] Declarations { get; }

    public IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
        DeclaredTypesByScopedIdentity { get; }

    public static JsonWireDeclarationPlan Create(JsExportSurface surface)
    {
        ApiType[] declarationTypes = GetDeclarationTypes(surface);
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity =
                BuildDeclaredTypesByScopedIdentity(
                    surface,
                    TypeInventory(
                        surface,
                        declarationTypes));
        HashSet<ApiType> recordTypes = surface.Records.ToHashSet();
        Dictionary<ApiType, JsExportUnion> unionsByDefinition =
            surface.Unions.ToDictionary(union => union.Definition);
        var splitTypes = new HashSet<ApiType>(
            declarationTypes.Where(type =>
                surface.WireDirections.GetValueOrDefault(
                    type,
                    JsonWireDirection.Both)
                    == JsonWireDirection.Both
                && recordTypes.Contains(type)
                && HasSupportedDirectionalDifference(
                    surface,
                    type,
                    declaredTypesByScopedIdentity)));
        var typesByDefinitionName = declarationTypes
            .Where(type => type.DefinitionName is not null)
            .GroupBy(type => type.DefinitionName!)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

        bool changed;
        do
        {
            changed = false;
            foreach (ApiType type in declarationTypes)
            {
                if (splitTypes.Contains(type)
                    || surface.WireDirections.GetValueOrDefault(
                        type,
                        JsonWireDirection.Both)
                        != JsonWireDirection.Both)
                {
                    continue;
                }

                bool referencesSplit = recordTypes.Contains(type)
                    ? HasSupportedDirectionalMembers(
                            surface,
                            type,
                            declaredTypesByScopedIdentity,
                            out _)
                        && type.Members.Any(member =>
                            JsonWireMemberRules.GetPresence(
                                surface,
                                type,
                                member,
                                JsonWireDirection.Serialize,
                                surface.AssemblyIdentity,
                                declaredTypesByScopedIdentity)
                                is JsonWireMemberPresence.Present
                                    or JsonWireMemberPresence.Conditional
                            && (ReferencesSplitType(
                                    member.SignatureModel?.ReturnTypeShape,
                                    declaredTypesByScopedIdentity,
                                    splitTypes)
                                || member.SignatureModel
                                    ?.ReturnTypeReferences.Any(
                                        reference =>
                                            declaredTypesByScopedIdentity
                                                .TryGetValue(
                                                    reference,
                                                    out ApiType? referenced)
                                            && splitTypes.Contains(referenced))
                                    == true))
                    : unionsByDefinition.TryGetValue(
                        type,
                        out JsExportUnion? union)
                    && union.CaseTypes.Any(caseType =>
                        ReferencesSplitType(
                            caseType,
                            surface.AssemblyIdentity,
                            typesByDefinitionName,
                            splitTypes));
                if (referencesSplit)
                {
                    splitTypes.Add(type);
                    changed = true;
                }
            }
        }
        while (changed);

        var declarations = new List<JsonWireDeclarationIdentity>();
        foreach (ApiType type in declarationTypes)
        {
            JsonWireDirection directions =
                surface.WireDirections.GetValueOrDefault(
                    type,
                    JsonWireDirection.Both);
            if (splitTypes.Contains(type))
            {
                declarations.Add(new JsonWireDeclarationIdentity(
                    type,
                    JsonWireDirection.Deserialize,
                    IsSplit: true));
                declarations.Add(new JsonWireDeclarationIdentity(
                    type,
                    JsonWireDirection.Serialize,
                    IsSplit: true));
            }
            else
            {
                declarations.Add(new JsonWireDeclarationIdentity(
                    type,
                    directions,
                    IsSplit: false));
            }
        }

        return new JsonWireDeclarationPlan(
            declarationTypes,
            [.. declarations],
            splitTypes,
            declaredTypesByScopedIdentity);
    }

    public JsonWireDeclarationIdentity Resolve(
        ApiType type,
        JsonWireDirection direction)
    {
        if (!_splitTypes.Contains(type))
            return _singleDeclarations[type];
        if (direction is not JsonWireDirection.Serialize
            and not JsonWireDirection.Deserialize)
        {
            throw new InvalidOperationException(
                $"Split type '{type.FullName}' requires one wire direction.");
        }
        return _splitDeclarations[(type, direction)];
    }

    public bool Contains(ApiType type) =>
        _splitTypes.Contains(type)
        || _singleDeclarations.ContainsKey(type);

    static bool HasSupportedDirectionalDifference(
        JsExportSurface surface,
        ApiType type,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity)
        => HasSupportedDirectionalMembers(
            surface,
            type,
            declaredTypesByScopedIdentity,
            out bool differs)
        && differs;

    static bool HasSupportedDirectionalMembers(
        JsExportSurface surface,
        ApiType type,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity,
        out bool differs)
    {
        differs = false;
        foreach (ApiMember member in type.Members)
        {
            JsonWireMemberPresence serialize =
                JsonWireMemberRules.GetPresence(
                    surface,
                    type,
                    member,
                    JsonWireDirection.Serialize,
                    surface.AssemblyIdentity,
                    declaredTypesByScopedIdentity);
            JsonWireMemberPresence deserialize =
                JsonWireMemberRules.GetPresence(
                    surface,
                    type,
                    member,
                    JsonWireDirection.Deserialize,
                    surface.AssemblyIdentity,
                    declaredTypesByScopedIdentity);
            if (serialize == JsonWireMemberPresence.Unsupported
                || deserialize == JsonWireMemberPresence.Unsupported)
            {
                return false;
            }
            differs |= serialize != deserialize;
        }

        return true;
    }

    static ApiType[] GetDeclarationTypes(JsExportSurface surface)
    {
        foreach (JsExportUnion union in surface.Unions.Where(
            union => ShouldDeclare(surface, union.Definition)))
        {
            JsonWireDirection directions =
                surface.WireDirections.GetValueOrDefault(
                    union.Definition,
                    JsonWireDirection.Both);
            string? reason = (directions & JsonWireDirection.Deserialize) != 0
                ? union.DeserializationUnsupportedReason
                : union.SerializationUnsupportedReason;
            if (reason is not null
                || union.IncludesNull is null
                || union.CaseTypes.Count == 0)
            {
                throw new UnsupportedJsExportSurfaceException(
                    union.Definition.FullName,
                    reason
                        ?? "union case or null evidence is unavailable");
            }
        }
        foreach (JsExportPolymorphicUnion union
            in surface.PolymorphicUnions.Where(
                union => ShouldDeclare(surface, union.Definition)))
        {
            string? reason = union.UnsupportedReason;
            if (reason is not null
                || union.TypeDiscriminatorPropertyName is null
                || union.Cases.Count == 0)
            {
                throw new UnsupportedJsExportSurfaceException(
                    $"{union.Definition.FullName} JSON polymorphism",
                    reason
                        ?? "discriminator property or case evidence is unavailable");
            }

            JsonWireDirection directions =
                surface.WireDirections.GetValueOrDefault(
                    union.Definition,
                    JsonWireDirection.Both);
            if ((directions & JsonWireDirection.Deserialize)
                != JsonWireDirection.None)
            {
                throw new UnsupportedJsExportSurfaceException(
                    $"{union.Definition.FullName} JSON polymorphism",
                    "polymorphic deserialization is unsupported");
            }
        }
        ValidateUnionCycles(surface);
        return [
            .. surface.Records
                .Concat(surface.Enums)
                .Concat(surface.Unions.Select(union => union.Definition))
                .Concat(surface.PolymorphicUnions.Select(
                    union => union.Definition))
                .Concat(surface.PolymorphicUnions.SelectMany(
                    union => union.Cases.Select(
                        @case => @case.Definition)))
                .Where(type => ShouldDeclare(surface, type)),
        ];
    }

    static void ValidateUnionCycles(JsExportSurface surface)
    {
        var unions = surface.Unions
            .Where(union =>
                ShouldDeclare(surface, union.Definition)
                && union.Definition.DefinitionName is not null)
            .ToDictionary(
                union => union.Definition.DefinitionName!,
                union => union);
        var completed = new HashSet<JsExportUnion>();
        var active = new HashSet<JsExportUnion>();
        var pending = new Stack<(JsExportUnion Union, bool Exit)>();
        foreach (JsExportUnion root in unions.Values)
        {
            pending.Push((root, false));
            while (pending.TryPop(out var next))
            {
                if (next.Exit)
                {
                    active.Remove(next.Union);
                    completed.Add(next.Union);
                    continue;
                }
                if (completed.Contains(next.Union))
                    continue;
                if (!active.Add(next.Union))
                {
                    throw new UnsupportedJsExportSurfaceException(
                        next.Union.Definition.FullName,
                        "recursive union case aliases are unsupported");
                }
                pending.Push((next.Union, true));
                var types = new Stack<TypeRef>(next.Union.CaseTypes);
                while (types.TryPop(out TypeRef? type))
                {
                    if (MatchesContainingAssembly(
                            type,
                            surface.AssemblyIdentity)
                        && type.Resolution?.Type is { } definition
                        && unions.TryGetValue(
                            definition,
                            out JsExportUnion? referenced))
                    {
                        pending.Push((referenced, false));
                    }
                    if (type.ElementType is { } element)
                        types.Push(element);
                    foreach (TypeRef argument in type.TypeArguments)
                        types.Push(argument);
                }
            }
        }
    }

    static IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
        BuildDeclaredTypesByScopedIdentity(
            JsExportSurface surface,
            IEnumerable<ApiType> types) =>
        types
            .SelectMany(type =>
                surface.ReferencedTypeDefinitions
                    .Where(candidate =>
                        ReferenceEquals(candidate.Value, type))
                    .Select(candidate => (
                        Identity: candidate.Key,
                        Type: type))
                    .Concat(
                        surface.AssemblyIdentity is { } assembly
                            ? [
                                (
                                    Identity: new ApiTypeReferenceIdentity(
                                        assembly,
                                        type.FullName,
                                        type.DefinitionName),
                                    Type: type),
                            ]
                            : []))
                .GroupBy(candidate => candidate.Identity)
                .Where(group => group
                    .Select(candidate => candidate.Type)
                    .Distinct()
                    .Count() == 1)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Type);

    static IEnumerable<ApiType> TypeInventory(
        JsExportSurface surface,
        ApiType[] declarationTypes) =>
        (surface.AllTypes.Count > 0
            ? surface.AllTypes
            : declarationTypes)
        .Concat(surface.ReferencedTypeDefinitions.Values)
        .Distinct();

    static bool ReferencesSplitType(
        ApiTypeShape? shape,
        IReadOnlyDictionary<ApiTypeReferenceIdentity, ApiType>
            declaredTypesByScopedIdentity,
        IReadOnlySet<ApiType> splitTypes)
    {
        if (shape is null)
            return false;

        var pending = new Stack<ApiTypeShape>();
        pending.Push(shape);
        while (pending.TryPop(out ApiTypeShape? current))
        {
            if (current.Definition is { } definition
                && declaredTypesByScopedIdentity.TryGetValue(
                    definition,
                    out ApiType? type)
                && splitTypes.Contains(type))
            {
                return true;
            }
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int index = current.TypeArguments.Length - 1;
                index >= 0;
                index--)
            {
                pending.Push(current.TypeArguments[index]);
            }
        }

        return false;
    }

    static bool ReferencesSplitType(
        TypeRef type,
        ApiAssemblyIdentity? assemblyIdentity,
        IReadOnlyDictionary<MetadataTypeDefinitionName, ApiType>
            typesByDefinitionName,
        IReadOnlySet<ApiType> splitTypes)
    {
        var pending = new Stack<TypeRef>();
        pending.Push(type);
        while (pending.TryPop(out TypeRef? current))
        {
            TypeRef definition = current.Kind == TypeRefKind.GenericInstance
                ? current.ElementType!
                : current;
            if (MatchesContainingAssembly(
                    definition,
                    assemblyIdentity)
                && definition.Resolution?.Type is { } definitionName
                && typesByDefinitionName.TryGetValue(
                    definitionName,
                    out ApiType? resolved)
                && splitTypes.Contains(resolved))
            {
                return true;
            }
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            for (int index = current.TypeArguments.Length - 1;
                index >= 0;
                index--)
            {
                pending.Push(current.TypeArguments[index]);
            }
        }

        return false;
    }

    static bool ShouldDeclare(
        JsExportSurface surface,
        ApiType type) =>
        type.FullName is not InertStringFullName
        and not ImmutableArrayFullName
        and not JsonElementFullName
        && (!surface.WireDirections.TryGetValue(
            type,
            out JsonWireDirection directions)
        || directions != JsonWireDirection.None);

    static bool MatchesContainingAssembly(
        TypeRef type,
        ApiAssemblyIdentity? containingAssembly)
    {
        if (containingAssembly is null
            || string.IsNullOrEmpty(containingAssembly.Name)
            || containingAssembly.Version is null
            || type.Resolution?.Origin is not TypeReferenceOrigin origin)
        {
            return false;
        }

        var expected = new AssemblyReferenceIdentity(
            containingAssembly.Name,
            containingAssembly.Version,
            containingAssembly.Culture,
            containingAssembly.PublicKeyToken);
        return origin switch
        {
            TypeReferenceOrigin.CurrentAssembly current =>
                current.Assembly is not null
                && current.Assembly.Version is not null
                && current.Assembly.IsEquivalentTo(expected),
            TypeReferenceOrigin.AssemblyReference reference =>
                reference.Assembly.Version is not null
                && reference.Assembly.IsEquivalentTo(expected),
            _ => false,
        };
    }
}
