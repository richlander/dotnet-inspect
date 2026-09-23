using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Navigation state equality, independent of its transport projection and
/// action generations. Owner-issued identities retain their owner's equality;
/// producer rows and sequence-bearing evidence compare by complete value.
/// </summary>
internal static class NavigationWorkspaceSnapshotEquality
{
    internal static bool Equals(NavigationWorkspaceSnapshot left, NavigationWorkspaceSnapshot right) =>
        ReferenceEquals(left, right)
        || Scope(left.Scope, right.Scope)
        && left.Workspace == right.Workspace
        && left.ActiveOccurrence == right.ActiveOccurrence
        && left.ActiveSubject == right.ActiveSubject
        && left.RetainedContext == right.RetainedContext
        && left.TypeInventoryLibraryContext == right.TypeInventoryLibraryContext
        && Sequence(left.Packages, right.Packages)
        && Sequence(left.Hierarchy, right.Hierarchy)
        && Sequence(left.Libraries, right.Libraries)
        && Sequence(left.Types, right.Types, (a, b) =>
            a.State == b.State && a.IsActive == b.IsActive && a.IsRetained == b.IsRetained && TypeRow(a.Row, b.Row))
        && Sequence(left.Members, right.Members, (a, b) =>
            a.State == b.State && a.IsActive == b.IsActive && a.IsRetained == b.IsRetained && MemberRow(a.Row, b.Row))
        && Sequence(left.Lenses, right.Lenses)
        && Sequence(left.DescendantLenses, right.DescendantLenses)
        && left.LensOutcome == right.LensOutcome
        && Inventory(left.Inventory, right.Inventory);

    // PublicationBase and Closure.Identity identify observations, not changed
    // Navigation facts. Membership and physical evidence keep their exact bindings.
    static bool Scope(WorkspaceScopeSnapshot a, WorkspaceScopeSnapshot b) =>
        a.Revision.Identity == b.Revision.Identity
        && a.Revision.Workspace == b.Revision.Workspace
        && Sequence(a.Revision.Packages, b.Revision.Packages)
        && a.Revision.Limits.MaxPackages == b.Revision.Limits.MaxPackages
        && a.PhysicalComposition == b.PhysicalComposition
        && Sequence(a.Packages, b.Packages, (x, y) => x.Occurrence == y.Occurrence && x.Realization == y.Realization)
        && a.Closure.Revision == b.Closure.Revision
        && a.Closure.State == b.Closure.State
        && Same(a.Preparing, b.Preparing, (x, y) =>
            x.Operation == y.Operation && x.Kind == y.Kind
            && x.RequestedPackageCount == y.RequestedPackageCount && x.Deadline == y.Deadline
            && x.Cancellation.Workspace == y.Cancellation.Workspace
            && x.Cancellation.Operation == y.Cancellation.Operation);

    static bool Inventory(NavigationSubjectInventory? a, NavigationSubjectInventory? b) =>
        Same(a, b, (x, y) => x.Package == y.Package
            && Sequence(x.Libraries, y.Libraries, (first, second) =>
                first.Subject == second.Subject && first.IsPrimary == second.IsPrimary
                && TypeInventory(first.Types, second.Types))
            && TypeInventory(x.Types, y.Types)
            && Sequence(x.InitialCandidates, y.InitialCandidates));

    static bool TypeInventory(NavigationTypeInventoryOutcome a, NavigationTypeInventoryOutcome b) =>
        ((a, b) is (NavigationTypeInventoryOutcome.Available, NavigationTypeInventoryOutcome.Available)
            or (NavigationTypeInventoryOutcome.Unavailable, NavigationTypeInventoryOutcome.Unavailable)
            or (NavigationTypeInventoryOutcome.Failed, NavigationTypeInventoryOutcome.Failed))
        && Sequence(a.Rows, b.Rows, TypeRow)
        && Sequence(a.Evidence, b.Evidence, Evidence);

    static bool TypeRow(NavigationTypeInventoryRow a, NavigationTypeInventoryRow b) =>
        a.Subject == b.Subject && a.Accessibility == b.Accessibility
        && Type(a.ProducerRow, b.ProducerRow) && Sequence(a.Members, b.Members, MemberRow);

    static bool MemberRow(NavigationMemberInventoryRow a, NavigationMemberInventoryRow b) =>
        a.Subject == b.Subject && a.ContainingType == b.ContainingType && Member(a.ProducerRow, b.ProducerRow);

    static bool Evidence(NavigationInventoryEvidence a, NavigationInventoryEvidence b) =>
        a.Library == b.Library && (a, b) switch
        {
            (NavigationInventoryEvidence.InspectionFailed x, NavigationInventoryEvidence.InspectionFailed y) =>
                InspectionFailure(x.Failure, y.Failure),
            (NavigationInventoryEvidence.TypeIdentityMissing x, NavigationInventoryEvidence.TypeIdentityMissing y) =>
                Type(x.ProducerRow, y.ProducerRow),
            (NavigationInventoryEvidence.ProjectedMemberIdentityFailure x, NavigationInventoryEvidence.ProjectedMemberIdentityFailure y) =>
                x.Kind == y.Kind && Type(x.ContainingType, y.ContainingType) && Member(x.ProducerRow, y.ProducerRow),
            // These records contain owner-issued exact subjects, scalar values,
            // or opaque diagnostic objects whose equality belongs to that owner.
            _ => a == b,
        };

    static bool InspectionFailure(ApiSurfaceInspectionFailure a, ApiSurfaceInspectionFailure b) =>
        a.Operation == b.Operation && a.SubjectToken == b.SubjectToken && a.Mechanism == b.Mechanism
        && a.Kind == b.Kind && a.Detail == b.Detail && a.SubjectAssembly == b.SubjectAssembly
        && a.DependencyAssembly == b.DependencyAssembly && a.SourceAssemblyPath == b.SourceAssemblyPath
        && a.OwningTypeToken == b.OwningTypeToken && a.OwningTypeDefinition == b.OwningTypeDefinition
        && Sequence(a.AffectedTypeDefinitions, b.AffectedTypeDefinitions);

    internal static bool Type(ApiType a, ApiType b) =>
        ReferenceEquals(a, b)
        || a.Namespace == b.Namespace && a.Name == b.Name && a.MetadataToken == b.MetadataToken
        && a.MetadataName == b.MetadataName && a.DefinitionName == b.DefinitionName
        && Sequence(a.IntroducedTypeParameterCounts, b.IntroducedTypeParameterCounts)
        && a.Accessibility == b.Accessibility && a.Kind == b.Kind && Sequence(a.Attributes, b.Attributes)
        && a.HasUnionAttribute == b.HasUnionAttribute && a.Layout == b.Layout && a.LayoutDetails == b.LayoutDetails
        && Same(a.MemorySafety, b.MemorySafety, (x, y) =>
            x.ModuleVersionId == y.ModuleVersionId && Rules(x.Rules, y.Rules))
        && a.EnumUnderlyingType == b.EnumUnderlyingType && a.IsFlagsEnum == b.IsFlagsEnum
        && a.FlagsAttributeCount == b.FlagsAttributeCount && a.HasMalformedFlagsAttribute == b.HasMalformedFlagsAttribute
        && a.HasJsonStringEnumConverter == b.HasJsonStringEnumConverter
        && a.JsonConverterAttributeCount == b.JsonConverterAttributeCount
        && a.HasUnsupportedJsonWireAttributes == b.HasUnsupportedJsonWireAttributes
        && a.JsonSerializableAttributeCount == b.JsonSerializableAttributeCount
        && Sequence(a.JsonSerializableRoots, b.JsonSerializableRoots)
        && a.JsonPropertyNamingPolicy == b.JsonPropertyNamingPolicy
        && a.JsonSourceGenerationMode == b.JsonSourceGenerationMode
        && a.HasSystemTextJsonSourceGenerationMarker == b.HasSystemTextJsonSourceGenerationMarker
        && Sequence(a.FilteredJsonPropertyNameFacts, b.FilteredJsonPropertyNameFacts, (x, y) =>
            x.Kind == y.Kind && x.AssociatedMemberName == y.AssociatedMemberName
            && x.MetadataToken == y.MetadataToken
            && Sequence(x.PropertyNames, y.PropertyNames, StringComparer.Ordinal.Equals))
        && Sequence(a.FilteredRuntimeJsExportFacts, b.FilteredRuntimeJsExportFacts)
        && a.IsSealed == b.IsSealed && a.IsAbstract == b.IsAbstract && a.IsStatic == b.IsStatic
        && a.IsByRefLike == b.IsByRefLike && a.IsReadOnly == b.IsReadOnly
        && a.BaseType == b.BaseType && a.BaseTypeReference == b.BaseTypeReference
        && Sequence(a.Interfaces, b.Interfaces)
        && Sequence(a.InterfaceReferences, b.InterfaceReferences)
        && Sequence(a.DerivedTypes, b.DerivedTypes)
        && Sequence(a.TypeParameters, b.TypeParameters, TypeParameter)
        && Sequence(a.Members, b.Members, Member)
        && a.SourceFilePath == b.SourceFilePath && a.SourceUrl == b.SourceUrl && a.GitHubBrowseUrl == b.GitHubBrowseUrl
        && a.SourceLineNumber == b.SourceLineNumber && Sequence(a.SourceChecksum, b.SourceChecksum)
        && a.SourceChecksumAlgorithm == b.SourceChecksumAlgorithm && a.SourceResolution == b.SourceResolution
        && Sequence(a.AdditionalSourceFiles, b.AdditionalSourceFiles, (x, y) =>
            x.FilePath == y.FilePath && x.SourceUrl == y.SourceUrl && x.GitHubBrowseUrl == y.GitHubBrowseUrl
            && x.SourceChecksumAlgorithm == y.SourceChecksumAlgorithm && Sequence(x.SourceChecksum, y.SourceChecksum))
        && a.IsForwarded == b.IsForwarded && a.SourceAssemblyPath == b.SourceAssemblyPath
        && Documentation(a.Documentation, b.Documentation);

    internal static bool Member(ApiMember a, ApiMember b) =>
        ReferenceEquals(a, b)
        || a.Name == b.Name && a.Kind == b.Kind && Sequence(a.Attributes, b.Attributes)
        && a.MethodSemantics == b.MethodSemantics && a.FieldLayout == b.FieldLayout
        && a.ReturnType == b.ReturnType && a.Signature == b.Signature && a.Digest == b.Digest
        && a.CanonicalSignature == b.CanonicalSignature && Same(a.SignatureModel, b.SignatureModel, Signature)
        && a.IndexParameterCount == b.IndexParameterCount && a.SignatureDecodeStatus == b.SignatureDecodeStatus
        && a.MetadataToken == b.MetadataToken && a.GenericArity == b.GenericArity
        && a.DeclarationMetadataToken == b.DeclarationMetadataToken
        && a.GetterToken == b.GetterToken && a.SetterToken == b.SetterToken
        && a.GetterHasMethodBody == b.GetterHasMethodBody && a.SetterHasMethodBody == b.SetterHasMethodBody
        && a.HasGetter == b.HasGetter && a.GetterAccessibility == b.GetterAccessibility
        && a.HasSetter == b.HasSetter && a.SetterAccessibility == b.SetterAccessibility
        && a.AdderToken == b.AdderToken && a.RemoverToken == b.RemoverToken
        && a.AdderHasMethodBody == b.AdderHasMethodBody && a.RemoverHasMethodBody == b.RemoverHasMethodBody
        && a.IsStatic == b.IsStatic && a.IsVirtual == b.IsVirtual && a.IsAbstract == b.IsAbstract
        && a.IsOverride == b.IsOverride && a.IsSealed == b.IsSealed && a.IsFinalizer == b.IsFinalizer
        && a.IsReadOnly == b.IsReadOnly && a.IsConst == b.IsConst && a.IsUnsafe == b.IsUnsafe && a.IsAsync == b.IsAsync
        && a.MemorySafety == b.MemorySafety && OptionalArray(a.AccessorMemorySafety, b.AccessorMemorySafety)
        && Same(a.BackingStorage, b.BackingStorage, (x, y) =>
            x.ModuleVersionId == y.ModuleVersionId && x.Convention == y.Convention
            && x.State == y.State && Sequence(x.Candidates, y.Candidates))
        && a.HasMethodBody == b.HasMethodBody && a.MethodImplementation == b.MethodImplementation
        && OptionalArray(a.AccessorImplementations, b.AccessorImplementations)
        && a.HasRuntimeJsExportWrapperCandidate == b.HasRuntimeJsExportWrapperCandidate
        && Sequence(a.RuntimeJsExportWrapperCandidates, b.RuntimeJsExportWrapperCandidates)
        && a.Accessibility == b.Accessibility && a.IsExtension == b.IsExtension
        && a.IsCompilerGenerated == b.IsCompilerGenerated && a.HasJsonInclude == b.HasJsonInclude
        && a.HasMalformedJsonInclude == b.HasMalformedJsonInclude
        && Sequence(a.JsonIgnoreConditions, b.JsonIgnoreConditions)
        && a.JsonPropertyName == b.JsonPropertyName && Sequence(a.JsonPropertyNameAttributeValues, b.JsonPropertyNameAttributeValues)
        && a.JsonConverterAttributeCount == b.JsonConverterAttributeCount
        && a.HasUnsupportedJsonWireAttributes == b.HasUnsupportedJsonWireAttributes
        && a.HasRuntimeJsExport == b.HasRuntimeJsExport
        && a.RuntimeJsExportAttributeCount == b.RuntimeJsExportAttributeCount
        && a.HasMalformedRuntimeJsExportAttribute == b.HasMalformedRuntimeJsExportAttribute
        && Sequence(a.JsonStringEnumMemberNameAttributeValues, b.JsonStringEnumMemberNameAttributeValues)
        && a.IsObsolete == b.IsObsolete && a.ObsoleteMessage == b.ObsoleteMessage
        && a.ObsoleteIsError == b.ObsoleteIsError
        && a.ExtendedType == b.ExtendedType && a.DeclaringType == b.DeclaringType
        && a.DeclaringTypeCanonicalName == b.DeclaringTypeCanonicalName
        && a.DeclaringTypeDefinitionName == b.DeclaringTypeDefinitionName
        && a.DeclaringOverloadIndex == b.DeclaringOverloadIndex && a.SelectorOverloadIndex == b.SelectorOverloadIndex
        && a.EnumValue == b.EnumValue && a.EnumValueLiteral == b.EnumValueLiteral
        && a.SourceFilePath == b.SourceFilePath && a.SourceUrl == b.SourceUrl
        && a.SourceLineNumber == b.SourceLineNumber && a.SourceEndLineNumber == b.SourceEndLineNumber
        && Sequence(a.SourceChecksum, b.SourceChecksum) && a.SourceChecksumAlgorithm == b.SourceChecksumAlgorithm
        && Documentation(a.Documentation, b.Documentation);

    static bool Rules(MemorySafetyRulesResult a, MemorySafetyRulesResult b) =>
        Sequence(a.Observations, b.Observations) && (a, b) switch
        {
            (MemorySafetyRulesResult.Available x, MemorySafetyRulesResult.Available y) => x.State == y.State,
            (MemorySafetyRulesResult.Unavailable x, MemorySafetyRulesResult.Unavailable y) => x.Failure == y.Failure,
            _ => false,
        };

    static bool Signature(ApiSignature a, ApiSignature b) =>
        a.ReturnType == b.ReturnType && a.CanonicalReturnType == b.CanonicalReturnType
        && a.StructuralReturnType == b.StructuralReturnType
        && Sequence(a.ReturnTypeReferences, b.ReturnTypeReferences)
        && a.ReturnTypeDefinitionReference == b.ReturnTypeDefinitionReference
        && object.Equals(a.ReturnTypeShape, b.ReturnTypeShape)
        && Sequence(a.ReturnAttributes, b.ReturnAttributes)
        && a.MemberName == b.MemberName && a.IsRequired == b.IsRequired
        && Sequence(a.TypeParameters, b.TypeParameters, TypeParameter)
        && Sequence(a.Parameters, b.Parameters, (x, y) =>
            Sequence(x.Attributes, y.Attributes) && x.Name == y.Name && x.Type == y.Type
            && x.CanonicalType == y.CanonicalType && Sequence(x.TypeReferences, y.TypeReferences)
            && x.StructuralType == y.StructuralType && x.Modifier == y.Modifier
            && x.HasDefault == y.HasDefault && x.DefaultValueText == y.DefaultValueText)
        && Sequence(a.Accessors, b.Accessors, (x, y) =>
            x.Kind == y.Kind && x.Accessibility == y.Accessibility && Sequence(x.ReturnAttributes, y.ReturnAttributes)
            && x.AccessibilityIsRepresentable == y.AccessibilityIsRepresentable
            && x.DeclarationModifiersMatchProperty == y.DeclarationModifiersMatchProperty
            && x.DeclarationModifiersAreRepresentable == y.DeclarationModifiersAreRepresentable
            && x.IsReadOnly == y.IsReadOnly && x.IsExplicitInterfaceImplementation == y.IsExplicitInterfaceImplementation
            && x.Name == y.Name && x.StructuralReturnType == y.StructuralReturnType
            && x.SignatureMatchesProperty == y.SignatureMatchesProperty);

    static bool TypeParameter(TypeParameter a, TypeParameter b) =>
        a.Name == b.Name && a.Variance == b.Variance && a.TypeKind == b.TypeKind
        && Sequence(a.Constraints, b.Constraints)
        && Sequence(a.StructuredConstraints, b.StructuredConstraints)
        && Sequence(
            a.ConstraintTypeDefinitionNames,
            b.ConstraintTypeDefinitionNames);

    static bool Documentation(DocComment a, DocComment b) =>
        a.Summary == b.Summary && a.Remarks == b.Remarks && a.Returns == b.Returns
        && Sequence(a.Parameters?.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            b.Parameters?.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        && Sequence(a.Samples, b.Samples, (x, y) =>
            x.RelativePath == y.RelativePath && x.Description == y.Description
            && x.Region == y.Region && x.ResolvedUrl == y.ResolvedUrl && x.Content == y.Content);

    static bool Same<T>(T? a, T? b, Func<T, T, bool> equal) where T : class =>
        ReferenceEquals(a, b) || a is not null && b is not null && equal(a, b);

    static bool OptionalArray<T>(ImmutableArray<T>? a, ImmutableArray<T>? b) =>
        a.HasValue == b.HasValue && (!a.HasValue || Sequence(a.Value, b!.Value));

    static bool Sequence<T>(ImmutableArray<T> a, ImmutableArray<T> b, Func<T, T, bool>? equal = null) =>
        a.IsDefault == b.IsDefault
        && (a.IsDefault || Sequence((IEnumerable<T>)a, b, equal));

    static bool Sequence<T>(IEnumerable<T>? a, IEnumerable<T>? b, Func<T, T, bool>? equal = null)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        using IEnumerator<T> left = a.GetEnumerator();
        using IEnumerator<T> right = b.GetEnumerator();
        while (left.MoveNext())
        {
            if (!right.MoveNext()
                || !(equal?.Invoke(left.Current, right.Current)
                    ?? EqualityComparer<T>.Default.Equals(left.Current, right.Current)))
            {
                return false;
            }
        }
        return !right.MoveNext();
    }
}
