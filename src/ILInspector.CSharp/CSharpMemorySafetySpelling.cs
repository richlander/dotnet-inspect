using ILInspector.Metadata;

namespace ILInspector.CSharp;

/// <summary>
/// Output-language capabilities, independent of the inspected module's rules.
/// Selecting a capability does not migrate the module to another rules model.
/// </summary>
public enum CSharpMemorySafetyLanguage
{
    /// <summary>Pointer declarations require a lexical unsafe context.</summary>
    Legacy,

    /// <summary>Pointer declarations need no context; updated contracts are unavailable.</summary>
    RelaxedPointerSyntax,

    /// <summary>Relaxed pointer syntax and updated caller-contract declarations are available.</summary>
    UpdatedCallerContracts
}

internal readonly record struct CSharpMemorySafetyDecision(
    string? Modifier,
    string? Failure);

internal static class CSharpMemorySafetySpelling
{
    internal static string? TypeFailure(
        ApiType type,
        CSharpMemorySafetyLanguage language,
        int primaryConstructorParameterCount = 0)
    {
        if (type.Kind is "delegate" or "enum")
            return $"Type '{type.FullName}': model-aware {type.Kind} spelling is not supported.";
        if (primaryConstructorParameterCount > 0)
        {
            return $"Type '{type.FullName}': model-aware primary-constructor spelling is not supported; "
                + "supply the selected explicit fields and ordinary constructor.";
        }
        if (ModuleFailure(type, language) is { } moduleFailure)
            return moduleFailure;
        var rules = (MemorySafetyRulesResult.Available)type.MemorySafety!.Rules;
        if (rules.State == MemorySafetyRulesState.Updated
            && type.Layout == ApiTypeLayout.Extended)
        {
            return $"Type '{type.FullName}': extended layout has no supported C# layout-attribute spelling.";
        }
        if (rules.State == MemorySafetyRulesState.Updated
            && type.Layout == ApiTypeLayout.Explicit)
        {
            if (type.Kind is not ("class" or "struct"))
                return $"Type '{type.FullName}': explicit layout is unavailable for C# type kind '{type.Kind}'.";
            if (type.MetadataToken is not int typeToken)
                return $"Type '{type.FullName}': the defining TypeDef token is unavailable for explicit-layout spelling.";
            if (type.LayoutDetails is not { } layout)
                return $"Type '{type.FullName}': explicit-layout details are unavailable.";
            if (layout.ModuleVersionId != type.MemorySafety.ModuleVersionId)
                return $"Type '{type.FullName}': explicit-layout evidence comes from a different module.";
            if (layout.TypeToken != typeToken)
                return $"Type '{type.FullName}': explicit-layout evidence identifies a different TypeDef.";
            if (layout.Size < 0)
                return $"Type '{type.FullName}': explicit-layout size '{layout.Size}' is not representable in C#.";
            if (!IsSupportedPackingSize(layout.PackingSize))
                return $"Type '{type.FullName}': explicit-layout packing size '{layout.PackingSize}' is not representable in C#.";
        }
        return null;
    }

    static string? ModuleFailure(
        ApiType type,
        CSharpMemorySafetyLanguage language)
    {
        if (type.MemorySafety is null)
            return $"Type '{type.FullName}': module memory-safety facts are unavailable.";
        if (type.MemorySafety.Rules is not MemorySafetyRulesResult.Available
            {
                State: MemorySafetyRulesState.Legacy or MemorySafetyRulesState.Updated
            } rules)
        {
            return $"Type '{type.FullName}': module memory-safety rules are unavailable or unrecognized.";
        }
        if (rules.State == MemorySafetyRulesState.Updated
            && language != CSharpMemorySafetyLanguage.UpdatedCallerContracts)
        {
            return $"Type '{type.FullName}': the selected language cannot replay updated caller contracts.";
        }
        return null;
    }

    internal static CSharpMemorySafetyDecision Member(
        ApiType type,
        ApiMember member,
        CSharpMemorySafetyLanguage? language,
        bool isExtern = false,
        bool requiresUnsafeContext = false,
        bool isStandaloneMember = false)
    {
        if (isExtern && ((type.Kind == "interface"
                && !(isStandaloneMember
                    && member.Kind == "extension-method"))
            || member.Kind is not ("method" or "extension-method" or "explicit-interface-implementation" or "constructor")
            || member.SignatureModel?.Accessors is { Count: > 0 }
            || member.IsAbstract || member.IsAsync || member.IsFinalizer || member.Name == ".cctor"))
        {
            return Refuse("the selected declaration cannot use the extern form.");
        }
        if (language is not { } selectedLanguage)
            return new(member.IsUnsafe || requiresUnsafeContext ? "unsafe" : null, null);
        if (member.Kind == "extension-method" && !isStandaloneMember)
        {
            return Refuse(
                "a projected extension can be rendered only as a standalone member of its defining static type.");
        }

        bool isStandaloneEnumMember =
            IsStandaloneEnumMember(type, member, isStandaloneMember);
        bool isStandaloneProperty =
            isStandaloneMember && member.Kind == "property";
        string? typeFailure = isStandaloneMember
                && (member.Kind == "extension-method"
                    || isStandaloneEnumMember)
            ? ModuleFailure(type, selectedLanguage)
            : TypeFailure(type, selectedLanguage);
        if (typeFailure is not null)
            return Refuse(typeFailure);
        if (member.Kind is not ("method" or "extension-method"
                or "explicit-interface-implementation" or "constructor"
                or "finalizer" or "field" or "property")
            || (member.Kind == "property" && !isStandaloneProperty)
            || (member.Kind != "property"
                && member.SignatureModel?.Accessors is { Count: > 0 }))
        {
            return Refuse($"model-aware {member.Kind} spelling is not supported.");
        }
        if (member.Kind is "method" or "extension-method"
                or "explicit-interface-implementation" or "constructor"
                or "finalizer")
        {
            if (member.MethodSemantics is null)
                return Refuse("MethodSemantics evidence is unavailable.");
            if (member.MethodSemantics != ApiMethodSemanticsKind.None)
                return Refuse("model-aware accessor spelling is not supported.");
        }
        if ((member.SignatureModel is null
                && !isStandaloneEnumMember)
            || member.SignatureDecodeStatus == SignatureDecodeStatus.Degraded)
        {
            return Refuse("a complete structured signature is unavailable.");
        }
        if (member.MemorySafety is not { } facts)
            return Refuse("member memory-safety facts are unavailable.");
        var rules = (MemorySafetyRulesResult.Available)type.MemorySafety!.Rules;
        if (facts.ModuleVersionId != type.MemorySafety.ModuleVersionId)
        {
            return Refuse("the declaring module's memory-safety evidence does not match the member.");
        }
        int? declarationToken = member.Kind is "field" or "property"
            ? member.DeclarationMetadataToken
            : member.MetadataToken;
        if (declarationToken is not int memberToken)
            return Refuse("the defining metadata token is unavailable.");
        if (facts.CallerContract.Evidence.MemberToken != memberToken)
            return Refuse("the caller-contract evidence identifies a different declaration.");
        if (facts.CallerContract.Evidence.RulesState != rules.State)
            return Refuse("the declaring module's rules state does not match the member.");
        if (facts.CallerContract is MemorySafetyMemberContractResult.Unavailable unavailable)
            return Refuse($"caller contract is unavailable: {unavailable.Failure.Detail}");

        if (isStandaloneEnumMember)
        {
            if (facts.CallerContract is not MemorySafetyMemberContractResult.None)
                return Refuse("an enum member cannot carry a caller contract.");
            if (facts.SignaturePointer != MemorySafetyPointerEvidence.Absent)
                return Refuse("enum-member pointer evidence is unavailable or invalid.");
            if (member.EnumValueLiteral is null)
                return Refuse("the enum value literal is unavailable.");
            return new(null, null);
        }

        if (isStandaloneProperty)
        {
            if (requiresUnsafeContext)
            {
                return Refuse(
                    "standalone property spelling does not support a body-owned unsafe context.");
            }
            if (facts.CallerContract is not MemorySafetyMemberContractResult.None)
                return Refuse("standalone property spelling requires a contract-neutral property declaration.");
            if (facts.SignaturePointer != MemorySafetyPointerEvidence.Absent)
                return Refuse("standalone property spelling requires pointer-free property evidence.");
            if (type.Layout is null)
                return Refuse("the declaring type's layout is unavailable.");
            if (type.Layout is ApiTypeLayout.Explicit or ApiTypeLayout.Extended)
            {
                return Refuse(
                    "standalone property spelling is unavailable for explicit- or extended-layout types.");
            }
            if (member.SignatureModel is not { } propertyModel
                || string.IsNullOrWhiteSpace(propertyModel.ReturnType)
                || propertyModel.ReturnTypeShape is null
                || string.IsNullOrWhiteSpace(propertyModel.MemberName)
                || propertyModel.MemberName == "this[]"
                || propertyModel.MemberName.Contains('.', StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(member.Name)
                || member.Name.Contains('.', StringComparison.Ordinal)
                || member.IndexParameterCount != 0
                || propertyModel.Parameters.Count != 0)
            {
                return Refuse(
                    "a complete non-indexed ordinary property signature is unavailable.");
            }
            if (propertyModel.ReturnTypeShape is
                {
                    Kind: ApiTypeShapeKind.Primitive,
                    Primitive: ApiPrimitiveType.Void,
                })
            {
                return Refuse("a property cannot have a void return type.");
            }
            if (propertyModel.Accessors is not { Count: > 0 } accessors)
                return Refuse("a complete structured property accessor shape is unavailable.");
            if (accessors.Any(
                    static accessor =>
                        accessor.Kind is not ("get" or "set")))
            {
                return Refuse("the structured property contains an unsupported accessor kind.");
            }
            if (accessors.Any(
                    static accessor =>
                        accessor.SignatureMatchesProperty is not true))
            {
                return Refuse(
                    "an accessor callable signature does not correspond to the property signature.");
            }
            if (accessors.Any(
                    static accessor =>
                        accessor.AccessibilityIsRepresentable is not true))
            {
                return Refuse(
                    "an accessor accessibility is unavailable or not representable in C#.");
            }
            if (accessors.Any(
                    static accessor =>
                        accessor.IsExplicitInterfaceImplementation is not false))
            {
                return Refuse(
                    "the structured property contains an unsupported explicit-interface accessor.");
            }
            if (accessors.Any(static accessor => accessor.IsReadOnly))
            {
                return Refuse(
                    "the structured property contains an unsupported readonly accessor.");
            }
            if (accessors.Any(
                    static accessor =>
                        accessor.StructuralReturnType is not null))
            {
                return Refuse(
                    "the structured property contains an unsupported accessor return shape.");
            }
            if (accessors.GroupBy(static accessor => accessor.Kind)
                    .Any(static group => group.Count() != 1))
            {
                return Refuse("the structured property accessor shape is ambiguous.");
            }
            if (!PropertyAccessibilityIsRepresentable(
                    member.Accessibility,
                    accessors))
            {
                return Refuse(
                    "the property accessor accessibility combination is not representable in C#.");
            }

            int[] accessorTokens = accessors.Select(accessor => accessor.Kind switch
                {
                    "get" => member.GetterToken,
                    "set" => member.SetterToken,
                    _ => null,
                })
                .OfType<int>()
                .Distinct()
                .ToArray();
            if (accessorTokens.Length != accessors.Count)
                return Refuse("the defining accessor tokens are unavailable or ambiguous.");
            if (member.AccessorMemorySafety is not { } accessorFacts
                || accessorFacts.Length != accessorTokens.Length)
            {
                return Refuse("complete accessor memory-safety facts are unavailable.");
            }
            foreach (ApiMemberMemorySafetyFacts accessorFact in accessorFacts)
            {
                if (accessorFact.ModuleVersionId != type.MemorySafety.ModuleVersionId)
                    return Refuse("accessor memory-safety evidence comes from a different module.");
                MemorySafetyMemberContractEvidence evidence =
                    accessorFact.CallerContract.Evidence;
                if (!accessorTokens.Contains(evidence.MemberToken))
                    return Refuse("accessor memory-safety evidence identifies a different declaration.");
                if (evidence.RulesState != rules.State)
                    return Refuse("the declaring module's rules state does not match an accessor.");
                if (accessorFact.CallerContract
                    is MemorySafetyMemberContractResult.Unavailable accessorUnavailable)
                {
                    return Refuse(
                        $"accessor caller contract is unavailable: {accessorUnavailable.Failure.Detail}");
                }
                if (accessorFact.CallerContract
                    is not MemorySafetyMemberContractResult.None)
                {
                    return Refuse(
                        "standalone property spelling does not support accessor caller contracts.");
                }
                if (accessorFact.SignaturePointer
                    != MemorySafetyPointerEvidence.Absent)
                {
                    return Refuse(
                        "standalone property spelling requires pointer-free accessor evidence.");
                }
            }
            if (accessorFacts.Select(
                    static accessor =>
                        accessor.CallerContract.Evidence.MemberToken)
                .Distinct()
                .Count() != accessorTokens.Length)
            {
                return Refuse("accessor memory-safety evidence is ambiguous.");
            }
            return new(null, null);
        }

        if (rules.State == MemorySafetyRulesState.Legacy)
        {
            if (facts.CallerContract is not (MemorySafetyMemberContractResult.None or MemorySafetyMemberContractResult.Implicit))
                return Refuse("the caller contract cannot be represented under legacy module rules.");
            if (facts.SignaturePointer == MemorySafetyPointerEvidence.Present
                && facts.CallerContract is MemorySafetyMemberContractResult.None)
            {
                return Refuse("an ordinary pointer declaration would introduce an implicit legacy caller contract.");
            }
            if (requiresUnsafeContext)
                return new("unsafe", null);
            if (selectedLanguage != CSharpMemorySafetyLanguage.Legacy)
                return new(null, null);
            return facts.SignaturePointer switch
            {
                MemorySafetyPointerEvidence.Present => new("unsafe", null),
                MemorySafetyPointerEvidence.Absent => new(null, null),
                _ => Refuse("signature-pointer evidence is unavailable for lexical-context spelling.")
            };
        }

        if (member.Kind == "field"
            && !member.IsStatic
            && !member.IsConst
            && type.Layout == ApiTypeLayout.Explicit)
        {
            if (type.LayoutDetails is not { } typeLayout)
                return Refuse("explicit-layout type evidence is unavailable.");
            if (member.DeclarationMetadataToken is not int fieldToken)
                return Refuse("the defining FieldDef token is unavailable for explicit-layout spelling.");
            if (member.FieldLayout is not { } fieldLayout)
                return Refuse("explicit field-layout evidence is unavailable.");
            if (fieldLayout.ModuleVersionId != type.MemorySafety.ModuleVersionId)
                return Refuse("field-layout evidence comes from a different module.");
            if (fieldLayout.DeclaringTypeToken != typeLayout.TypeToken)
                return Refuse("field-layout evidence identifies a different declaring TypeDef.");
            if (fieldLayout.FieldToken != fieldToken)
                return Refuse("field-layout evidence identifies a different FieldDef.");
            if (fieldLayout.Offset is not int offset || offset < 0)
                return Refuse("a usable explicit field offset is unavailable.");
        }

        if (requiresUnsafeContext)
        {
            return Refuse("a member modifier cannot establish an updated-rules body context; "
                + "the body producer must supply that context.");
        }
        if (facts.CallerContract is MemorySafetyMemberContractResult.Explicit)
        {
            return member.IsFinalizer || member.Name == ".cctor"
                ? Refuse("this declaration position cannot carry an updated unsafe contract.")
                : new("unsafe", null);
        }
        if (facts.CallerContract is not MemorySafetyMemberContractResult.None)
            return Refuse("the caller contract cannot be represented under updated module rules.");
        if (isExtern)
            return new("safe", null);
        if (member.Kind == "field" && !member.IsStatic && !member.IsConst)
        {
            if (type.Layout is null)
                return Refuse("the declaring type's instance-storage layout is unavailable.");
            if (type.Layout is ApiTypeLayout.Explicit or ApiTypeLayout.Extended)
                return new("safe", null);
        }
        return new(null, null);

        CSharpMemorySafetyDecision Refuse(string reason)
            => new(null, $"Member '{type.FullName}.{member.Name}': {reason}");
    }

    static bool PropertyAccessibilityIsRepresentable(
        string? propertyAccessibility,
        IReadOnlyList<ApiAccessor> accessors)
    {
        string property = propertyAccessibility ?? "public";
        if (!IsCSharpAccessibility(property))
            return false;

        ApiAccessor[] modified =
        [
            .. accessors.Where(
                static accessor => accessor.Accessibility is not null),
        ];
        if (modified.Length == 0)
            return true;
        if (accessors.Count != 2 || modified.Length != 1)
            return false;

        return IsStrictlyMoreRestrictive(
            modified[0].Accessibility!,
            property);
    }

    static bool IsCSharpAccessibility(string accessibility) =>
        accessibility is
            "public"
            or "protected internal"
            or "protected"
            or "internal"
            or "private protected"
            or "private";

    static bool IsStrictlyMoreRestrictive(
        string accessor,
        string property) =>
        property switch
        {
            "public" => accessor is
                "protected internal"
                or "protected"
                or "internal"
                or "private protected"
                or "private",
            "protected internal" => accessor is
                "protected"
                or "internal"
                or "private protected"
                or "private",
            "protected" => accessor is "private protected" or "private",
            "internal" => accessor is "private protected" or "private",
            "private protected" => accessor is "private",
            _ => false,
        };

    internal static bool IsStandaloneEnumMember(
        ApiType type,
        ApiMember member,
        bool isStandaloneMember)
        => isStandaloneMember
            && type.Kind == "enum"
            && member.Kind == "field"
            && member.IsStatic
            && member.IsConst;

    internal static string? TypeLayoutAttribute(
        ApiType type,
        CSharpMemorySafetyLanguage? language)
    {
        if (!UsesUpdatedCallerContracts(type, language)
            || type.Layout != ApiTypeLayout.Explicit
            || type.LayoutDetails is not { } layout)
        {
            return null;
        }

        var arguments = new List<string>
        {
            "global::System.Runtime.InteropServices.LayoutKind.Explicit"
        };
        if (layout.Size > 0)
            arguments.Add($"Size = {layout.Size}");
        if (layout.PackingSize > 0)
            arguments.Add($"Pack = {layout.PackingSize}");
        return $"global::System.Runtime.InteropServices.StructLayoutAttribute({string.Join(", ", arguments)})";
    }

    internal static string? FieldLayoutAttribute(
        ApiType type,
        ApiMember member,
        CSharpMemorySafetyLanguage? language)
        => UsesUpdatedCallerContracts(type, language)
            && type.Layout == ApiTypeLayout.Explicit
            && member.Kind == "field"
            && !member.IsStatic
            && !member.IsConst
            && member.FieldLayout?.Offset is int offset
                ? $"global::System.Runtime.InteropServices.FieldOffsetAttribute({offset})"
                : null;

    static bool IsSupportedPackingSize(int packingSize)
        => packingSize is 0 or 1 or 2 or 4 or 8 or 16 or 32 or 64 or 128;

    static bool UsesUpdatedCallerContracts(
        ApiType type,
        CSharpMemorySafetyLanguage? language)
        => language == CSharpMemorySafetyLanguage.UpdatedCallerContracts
            && type.MemorySafety?.Rules is MemorySafetyRulesResult.Available
            {
                State: MemorySafetyRulesState.Updated
            };
}
