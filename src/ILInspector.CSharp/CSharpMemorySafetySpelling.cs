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

        string? typeFailure = isStandaloneMember
                && member.Kind == "extension-method"
            ? ModuleFailure(type, selectedLanguage)
            : TypeFailure(type, selectedLanguage);
        if (typeFailure is not null)
            return Refuse(typeFailure);
        if (member.Kind is not ("method" or "extension-method" or "explicit-interface-implementation" or "constructor" or "finalizer" or "field")
            || member.SignatureModel?.Accessors is { Count: > 0 })
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
        if (member.SignatureModel is null
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
        int? declarationToken = member.Kind == "field"
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
