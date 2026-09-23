using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text;
using CSharpText;

namespace ILInspector.Metadata;

public static partial class ApiSurfaceExtractor
{

    internal static long CountRetainedText(ApiType type)
    {
        long count = 0;
        AddText(ref count, type.Namespace);
        AddText(ref count, type.Name);
        AddText(ref count, type.MetadataName);
        AddText(ref count, type.DefinitionName);
        AddText(ref count, type.Accessibility);
        AddText(ref count, type.Kind);
        AddText(ref count, type.Attributes);
        AddText(ref count, type.EnumUnderlyingType);
        AddText(ref count, type.BaseType);
        AddText(ref count, type.BaseTypeReference?.Assembly);
        AddText(ref count, type.BaseTypeReference?.FullName);
        AddText(ref count, type.BaseTypeReference?.DefinitionName);
        AddText(
            ref count,
            type.JsonPolymorphism?.TypeDiscriminatorPropertyName);
        AddText(ref count, type.JsonPolymorphism?.UnsupportedReason);
        if (type.JsonPolymorphism is { } polymorphism)
        {
            foreach (ApiJsonDerivedType derivedType
                in polymorphism.DerivedTypes)
            {
                AddText(ref count, derivedType.Type.Assembly);
                AddText(ref count, derivedType.Type.FullName);
                AddText(ref count, derivedType.Type.DefinitionName);
                AddText(ref count, derivedType.TypeDiscriminator);
            }
        }
        if (type.MemorySafety is { } memorySafety)
        {
            foreach (var observation in memorySafety.Rules.Observations)
                AddText(ref count, observation.Detail);
            if (memorySafety.Rules is MemorySafetyRulesResult.Unavailable unavailable)
                AddText(ref count, unavailable.Failure.Detail);
        }
        foreach (ApiJsonSerializableRoot root
            in type.JsonSerializableRoots)
        {
            AddText(ref count, root.ElementType?.Assembly);
            AddText(ref count, root.ElementType?.FullName);
            AddText(ref count, root.ElementType?.DefinitionName);
            AddText(ref count, root.Type);
            AddText(ref count, root.UnsupportedReason);
            AddText(ref count, root.TypeInfoPropertyName);
        }
        foreach (ApiJsExportJsonInputDeclaration declaration
            in type.JsExportJsonInputDeclarations)
        {
            AddText(ref count, declaration.AttributeAssembly);
            AddText(ref count, declaration.MethodName);
            AddText(ref count, declaration.ParameterName);
            AddText(ref count, declaration.WireType);
            AddText(ref count, declaration.UnsupportedReason);
        }
        AddText(ref count, type.Interfaces);
        foreach (ApiTypeReferenceIdentity reference
            in type.InterfaceReferences)
        {
            AddText(ref count, reference.Assembly);
            AddText(ref count, reference.FullName);
            AddText(ref count, reference.DefinitionName);
        }
        foreach (FilteredJsonPropertyNameFact fact
            in type.FilteredJsonPropertyNameFacts)
        {
            AddText(ref count, fact.AssociatedMemberName);
            foreach (string? propertyName in fact.PropertyNames)
                AddText(ref count, propertyName);
        }
        foreach (FilteredRuntimeJsExportFact fact
            in type.FilteredRuntimeJsExportFacts)
        {
            AddText(ref count, fact.MethodName);
        }
        foreach (TypeParameter parameter in type.TypeParameters)
            AddText(ref count, parameter);
        return count;
    }

    internal static long CountRetainedText(ApiMember member)
    {
        long count = 0;
        AddText(ref count, member.Name);
        AddText(ref count, member.Kind);
        AddText(ref count, member.Attributes);
        AddText(ref count, member.ReturnType);
        AddText(ref count, member.Signature);
        AddText(ref count, member.CanonicalSignature);
        AddText(ref count, member.SignatureModel);
        AddText(ref count, member.Accessibility);
        AddText(ref count, member.ObsoleteMessage);
        AddText(ref count, member.ExtendedType);
        AddText(ref count, member.DeclaringType);
        AddText(ref count, member.DeclaringTypeCanonicalName);
        AddText(ref count, member.DeclaringTypeDefinitionName);
        AddText(ref count, member.EnumValueLiteral);
        AddText(ref count, member.ConstantValueLiteral);
        AddText(ref count, member.JsonPropertyName);
        AddText(ref count, member.GetterAccessibility);
        AddText(ref count, member.SetterAccessibility);
        AddMemorySafetyText(ref count, member.MemorySafety);
        if (member.AccessorMemorySafety is { } accessors)
        {
            foreach (var accessor in accessors)
                AddMemorySafetyText(ref count, accessor);
        }
        if (member.BackingStorage is { } backing)
        {
            foreach (var candidate in backing.Candidates)
                AddText(ref count, candidate.MatchedName);
        }
        foreach (string? propertyName
            in member.JsonPropertyNameAttributeValues)
        {
            AddText(ref count, propertyName);
        }
        foreach (string? enumMemberName
            in member.JsonStringEnumMemberNameAttributeValues)
        {
            AddText(ref count, enumMemberName);
        }
        return count;
    }

    static void AddMemorySafetyText(
        ref long count,
        ApiMemberMemorySafetyFacts? facts)
    {
        if (facts?.CallerContract is MemorySafetyMemberContractResult.Unavailable unavailable)
            AddText(ref count, unavailable.Failure.Detail);
    }

    static long CountRetainedText(ApiSurfaceInspectionFailure failure)
    {
        long count = 0;
        AddText(ref count, failure.Operation);
        AddText(ref count, failure.Kind);
        AddText(ref count, failure.Detail);
        return count;
    }

    static long CountRetainedText(TypeForwarder forwarder)
    {
        long count = 0;
        AddText(ref count, forwarder.DefinitionName);
        AddText(ref count, forwarder.TypeName);
        AddText(ref count, forwarder.TargetAssembly);
        return count;
    }

    static void ObserveText(ApiParameter parameter, Action<string>? observe)
    {
        if (observe is null)
            return;
        foreach (string attribute in parameter.Attributes)
            observe(attribute);
        ObserveText(parameter.Name, observe);
        ObserveText(parameter.Type, observe);
        ObserveText(parameter.CanonicalType, observe);
        ObserveText(parameter.StructuralType, observe);
        ObserveText(parameter.Modifier, observe);
        ObserveText(parameter.DefaultValueText, observe);
    }

    static void ObserveText(string? text, Action<string> observe)
    {
        if (text is not null)
            observe(text);
    }

    static void RetainAssemblyIdentity(
        ApiAssemblyIdentity? identity,
        Action<string>? observeText)
    {
        if (identity is null || observeText is null)
            return;

        observeText(identity.Name);
        if (identity.Culture is not null)
            observeText(identity.Culture);
        if (identity.PublicKeyToken is not null)
            observeText(identity.PublicKeyToken);
    }

    static void AddText(ref long count, ApiSignature? signature)
    {
        if (signature is null)
            return;
        AddText(ref count, signature.ReturnType);
        AddText(ref count, signature.CanonicalReturnType);
        AddText(ref count, signature.StructuralReturnType);
        if (signature.XmlDocumentationParameterTypes is { } xmlParameters)
            AddText(ref count, xmlParameters);
        AddText(ref count, signature.XmlDocumentationReturnType);
        AddText(ref count, signature.ReturnTypeShape);
        AddText(
            ref count,
            signature.ReturnTypeDefinitionReference?.Assembly);
        AddText(
            ref count,
            signature.ReturnTypeDefinitionReference?.FullName);
        AddText(
            ref count,
            signature.ReturnTypeDefinitionReference?.DefinitionName);
        foreach (ApiTypeReferenceIdentity reference
            in signature.ReturnTypeReferences)
        {
            AddText(ref count, reference.Assembly);
            AddText(ref count, reference.FullName);
            AddText(ref count, reference.DefinitionName);
        }
        AddText(ref count, signature.ReturnAttributes);
        AddText(ref count, signature.MemberName);
        AddText(ref count, signature.ExtensionReceiverType);
        foreach (TypeParameter parameter in signature.TypeParameters)
            AddText(ref count, parameter);
        foreach (ApiParameter parameter in signature.Parameters)
        {
            AddText(ref count, parameter.Attributes);
            AddText(ref count, parameter.Name);
            AddText(ref count, parameter.Type);
            AddText(ref count, parameter.CanonicalType);
            AddText(ref count, parameter.StructuralType);
            foreach (ApiTypeReferenceIdentity reference
                in parameter.TypeReferences)
            {
                AddText(ref count, reference.Assembly);
                AddText(ref count, reference.FullName);
                AddText(ref count, reference.DefinitionName);
            }
            AddText(ref count, parameter.Modifier);
            AddText(ref count, parameter.DefaultValueText);
        }
        foreach (ApiAccessor accessor in signature.Accessors)
        {
            AddText(ref count, accessor.Kind);
            AddText(ref count, accessor.Accessibility);
            AddText(ref count, accessor.ReturnAttributes);
            AddText(ref count, accessor.Name);
            AddText(ref count, accessor.StructuralReturnType);
        }
    }

    static void AddText(ref long count, ApiTypeShape? shape)
    {
        if (shape is null)
            return;

        var pending = new Stack<ApiTypeShape>();
        pending.Push(shape);
        while (pending.Count > 0)
        {
            ApiTypeShape current = pending.Pop();
            if (current.Definition is { } definition)
            {
                AddText(ref count, definition.Assembly);
                AddText(ref count, definition.FullName);
                AddText(ref count, definition.DefinitionName);
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
    }

    static void AddText(ref long count, TypeParameter parameter)
    {
        AddText(ref count, parameter.Name);
        AddText(ref count, parameter.Variance);
        AddText(ref count, parameter.Constraints);
        if (parameter.StructuredConstraints is not null)
        {
            foreach (TypeParameterConstraint constraint in parameter.StructuredConstraints)
                AddText(ref count, constraint.Value);
        }
        if (parameter.ConstraintTypeDefinitionNames is not null)
        {
            foreach (MetadataTypeDefinitionName name
                in parameter.ConstraintTypeDefinitionNames)
                AddText(ref count, name);
        }
    }

    static void AddText(ref long count, MetadataTypeDefinitionName? name)
    {
        if (name is null)
            return;
        AddText(ref count, name.Namespace);
        foreach (string segment in name.Segments)
            AddText(ref count, segment);
    }

    static void AddText(ref long count, IEnumerable<string> values)
    {
        foreach (string value in values)
            AddText(ref count, value);
    }

    static void AddText(
        ref long count,
        ApiAssemblyIdentity? identity)
    {
        if (identity is null)
            return;
        count = count > long.MaxValue
                - identity.RetainedCharacterCount
            ? long.MaxValue
            : count + identity.RetainedCharacterCount;
    }

    static void AddText(ref long count, string? value)
    {
        if (value is null)
            return;
        count = count > long.MaxValue - value.Length
            ? long.MaxValue
            : count + value.Length;
    }

    /// <summary>
    /// The running retention count of one bounded extraction.
    /// </summary>
    /// <remarks>
    /// Members and retained text are counted as they are built but committed only when their type
    /// is, so a rejected type spends no retention budget. The exact retained total is gated by
    /// <c>ApiSurfaceExtractorBoundsTests.RetainedTextBudget_IsExact</c>. A separate monotonic
    /// extraction-wide decode-work estimate may reject allocation-amplifying input before its
    /// expanded model exists; the hostile-shape allocation tests in that class gate that safety
    /// boundary.
    /// </remarks>
    private sealed class ExtractionBudget(ApiSurfaceExtractionBounds bounds)
    {
        const int DecodeWorkWeight = 16;
        const int RetainedTextDecodeWorkCreditWeight = DecodeWorkWeight * 4;
        // Small exact retention budgets still need enough work room to decode one ordinary type.
        // It is granted once per extraction; retained model text then earns bounded additional
        // work so rejected or amplification-heavy candidates cannot rearm the floor.
        const int MinimumDecodeWorkLimit = 32_000_000;
        int _types;
        int _members;
        int _pendingMembers;
        int _inspectionFailures;
        int _typeForwarders;
        int _retainedTextCharacters;
        int _pendingTextCharacters;
        int _pendingObservedTextCharacters;
        long _decodeWork;

        public int RetainedTextCharacters => _retainedTextCharacters;

        /// <summary>Starts work that may determine whether a type is retained.</summary>
        public void BeginTypeCandidate()
        {
            _pendingMembers = 0;
            _pendingTextCharacters = 0;
            _pendingObservedTextCharacters = 0;
        }

        /// <summary>Admits a retained type before its model or members are built.</summary>
        public void BeginType()
        {
            if (_types >= bounds.MaxTypes)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Types);
        }

        /// <summary>Counts one member of the type currently being built.</summary>
        public void RetainMember(ApiMember member)
        {
            if (_members + _pendingMembers >= bounds.MaxMembers)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Members);
            RetainPendingText(CountRetainedText(member));
            _pendingMembers++;
        }

        /// <summary>Commits the type currently being built and its members.</summary>
        public void RetainType(ApiType type)
        {
            if (_types >= bounds.MaxTypes)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Types);
            RetainPendingText(CountRetainedText(type));
            _types++;
            _members += _pendingMembers;
            _retainedTextCharacters += _pendingTextCharacters;
            _pendingMembers = 0;
            _pendingTextCharacters = 0;
            _pendingObservedTextCharacters = 0;
        }

        /// <summary>Counts one member attached to a type that is already committed.</summary>
        public void RetainAttachedMember(ApiMember member)
        {
            if (_members >= bounds.MaxMembers)
                throw new ExtractionBoundExceededException(ApiSurfaceExtractionBound.Members);
            RetainCommittedText(CountRetainedText(member));
            _members++;
        }

        public void RetainSurfaceFilteredRuntimeJsExportFact(
            FilteredRuntimeJsExportFact fact) =>
            RetainCommittedText(fact.MethodName);

        /// <summary>Counts one retained metadata-row rejection.</summary>
        public void RetainInspectionFailure(ApiSurfaceInspectionFailure failure)
        {
            if (_inspectionFailures >= bounds.MaxInspectionFailures)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.InspectionFailures);
            }
            RetainCommittedText(CountRetainedText(failure));
            _inspectionFailures++;
        }

        /// <summary>Refuses before a type-forwarder model is built.</summary>
        public void BeginTypeForwarder()
        {
            if (_typeForwarders >= bounds.MaxTypeForwarders)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.TypeForwarders);
            }
        }

        /// <summary>Counts one retained type forwarder.</summary>
        public void RetainTypeForwarder(TypeForwarder forwarder)
        {
            if (_typeForwarders >= bounds.MaxTypeForwarders)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.TypeForwarders);
            }
            RetainCommittedText(CountRetainedText(forwarder));
            _typeForwarders++;
        }

        public void RetainCommittedText(string text) => RetainCommittedText(text.Length);

        public void ObservePendingText(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            ObservePendingText(text.Length);
        }

        public void ObservePendingDecodeWork(int encodedCharacters)
        {
            if (encodedCharacters < 0)
                throw new ArgumentOutOfRangeException(nameof(encodedCharacters));
            long next =
                (long)encodedCharacters * DecodeWorkWeight + _decodeWork;
            long creditedCharacters =
                (long)_retainedTextCharacters + _pendingTextCharacters;
            long limit =
                MinimumDecodeWorkLimit
                + creditedCharacters * RetainedTextDecodeWorkCreditWeight;
            if (next > limit || next < 0)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _decodeWork = next;
        }

        void RetainPendingText(long characters)
        {
            long next = characters + _pendingTextCharacters;
            if (next > bounds.MaxRetainedTextCharacters - _retainedTextCharacters
                || next < 0)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _pendingTextCharacters += (int)characters;
        }

        void ObservePendingText(long characters)
        {
            long next = characters + _pendingObservedTextCharacters;
            if (next > bounds.MaxRetainedTextCharacters - _retainedTextCharacters
                || next < 0)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _pendingObservedTextCharacters += (int)characters;
        }

        void RetainCommittedText(long characters)
        {
            if (characters > bounds.MaxRetainedTextCharacters - _retainedTextCharacters)
            {
                throw new ExtractionBoundExceededException(
                    ApiSurfaceExtractionBound.RetainedTextCharacters);
            }
            _retainedTextCharacters += (int)characters;
        }
    }

    /// <summary>
    /// The abandonment signal of a bounded extraction. It is private to this extractor and caught
    /// by <see cref="ExtractBounded"/>, so a bound never surfaces as an exception to a caller.
    /// </summary>
    private sealed class ExtractionBoundExceededException(ApiSurfaceExtractionBound bound)
        : Exception("The API-surface extraction exceeded a declared retention bound.")
    {
        public ApiSurfaceExtractionBound Bound { get; } = bound;
    }
}
