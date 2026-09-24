using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal static partial class MetadataRelationInspection
{
    private static MetadataRelationFamilyResult<
        MetadataExtensionRelationEvidence> ScanExtensions(
        PEReader image,
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        MetadataOperationContext operation,
        CancellationToken cancellationToken)
    {
        var evidence =
            ImmutableArray.CreateBuilder<
                MetadataExtensionRelationEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        ExtensionCandidatePopulation? population = null;
        var admittedDeclarations = new HashSet<int>();
        bool scanCompleted = false;
        try
        {
            population =
                ExtensionCandidates(
                    reader,
                    request,
                    cancellationToken);
            foreach (ExtensionMethodInfo extension
                in ExtensionMethodScanner.FindAllExtensions(
                    image,
                    request.IncludeNonPublic))
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                if (!request.IncludesType(
                        reader,
                        extension.DeclaringTypeHandle))
                {
                    continue;
                }
                if (extension.DeclaringTypeDefinition is null
                    || extension.Anchor is null
                    || extension.DeclaringTypeHandle.IsNil
                    || extension.ReceiverContextTypeHandle.IsNil
                    || extension.ReceiverMethodHandle.IsNil)
                {
                    diagnostics.Add(
                        UnsupportedDiagnostic(
                            MetadataRelationFamily.Extensions,
                            null,
                            "An extension declaration lacks exact Metadata identity."));
                    continue;
                }

                TypeDefinition receiverContext =
                    reader.GetTypeDefinition(
                        extension.ReceiverContextTypeHandle);
                MethodDefinition receiverMethod =
                    reader.GetMethodDefinition(
                        extension.ReceiverMethodHandle);
                MetadataMethodSignatureDecodeResult signature =
                    MetadataTypeIdentityDecoder.DecodeMethod(
                        reader,
                        receiverContext,
                        receiverMethod,
                        operation);
                if (signature
                    is MetadataMethodSignatureDecodeResult.Rejected rejected)
                {
                    diagnostics.Add(
                        UnsupportedDiagnostic(
                            MetadataRelationFamily.Extensions,
                            extension.DeclarationMetadataToken,
                            rejected.Detail));
                    continue;
                }
                MetadataMethodSignatureIdentity identity =
                    ((MetadataMethodSignatureDecodeResult.Decoded)
                        signature).Signature;
                if ((identity.ParameterTypes.Length != 1
                        && extension.Kind == "property")
                    || identity.ParameterTypes.IsEmpty)
                {
                    diagnostics.Add(
                        UnsupportedDiagnostic(
                            MetadataRelationFamily.Extensions,
                            extension.DeclarationMetadataToken,
                            "An extension declaration has no exact receiver parameter."));
                    continue;
                }

                operation.Charge(
                    MetadataOperationDimension.RelationshipEdges);
                admittedDeclarations.Add(
                    extension.DeclarationMetadataToken);
                evidence.Add(
                    new(
                        MetadataTypeDefinitionAddress.FromHandle(
                            reader,
                            extension.DeclaringTypeHandle),
                        extension.DeclaringTypeDefinition,
                        extension.DeclarationMetadataToken,
                        MetadataMethodAddress.Create(
                            reader,
                            extension.ReceiverMethodHandle),
                        extension.Anchor,
                        identity.ParameterTypes[0]));
            }
            scanCompleted = true;
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.Extensions,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.Extensions,
                    null,
                    exception.Message));
        }

        if (scanCompleted)
        {
            AuditRejectedExtensionCandidates(
                reader,
                request,
                admittedDeclarations,
                diagnostics,
                cancellationToken);
        }

        if (population is null)
        {
            return CompleteOrPartial(
                evidence,
                diagnostics,
                new(1, 0, 0, 1, 0));
        }

        bool limited = diagnostics.Any(static diagnostic =>
            diagnostic.Kind == MetadataRelationDiagnosticKind.Limit);
        int unavailable = diagnostics
            .Where(static diagnostic =>
                diagnostic.Kind != MetadataRelationDiagnosticKind.Limit
                && diagnostic.MetadataToken is not null)
            .Select(static diagnostic =>
                diagnostic.MetadataToken!.Value)
            .Distinct()
            .Count(population.Included.Contains);
        int remaining =
            population.Included.Count
            - admittedDeclarations.Count
            - unavailable;
        if (!limited)
            unavailable += remaining;
        return CompleteOrPartial(
            evidence,
            diagnostics,
            new(
                population.Included.Count + population.Excluded,
                admittedDeclarations.Count,
                population.Excluded,
                unavailable,
                limited ? remaining : 0));
    }

    private sealed record ExtensionCandidatePopulation(
        HashSet<int> Included,
        int Excluded);

    private static ExtensionCandidatePopulation ExtensionCandidates(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        CancellationToken cancellationToken)
    {
        var included = new HashSet<int>();
        int excluded = 0;
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.IncludesType(reader, typeHandle))
                continue;

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            TypeAttributes attributes = type.Attributes;
            bool isStatic =
                (attributes & TypeAttributes.Sealed) != 0
                && (attributes & TypeAttributes.Abstract) != 0;
            if (!isStatic
                || !AttributeReader.HasExtensionAttribute(
                    reader,
                    type.GetCustomAttributes()))
            {
                continue;
            }

            bool typeExcluded =
                !request.IncludeNonPublic
                && AttributeReader.HasHiddenAttribute(
                    reader,
                    type.GetCustomAttributes());
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Static) == 0
                    || !AttributeReader.HasExtensionAttribute(
                        reader,
                        method.GetCustomAttributes()))
                {
                    continue;
                }

                bool methodExcluded =
                    typeExcluded
                    || (!request.IncludeNonPublic
                        && ((method.Attributes
                                & MethodAttributes.MemberAccessMask)
                                != MethodAttributes.Public
                            || AttributeReader.HasHiddenAttribute(
                                reader,
                                method.GetCustomAttributes())));
                if (methodExcluded)
                    excluded++;
                else
                    included.Add(MetadataTokens.GetToken(methodHandle));
            }

            foreach (TypeDefinitionHandle groupingHandle
                in type.GetNestedTypes())
            {
                TypeDefinition grouping =
                    reader.GetTypeDefinition(groupingHandle);
                if (!AttributeReader.HasExtensionAttribute(
                        reader,
                        grouping.GetCustomAttributes()))
                {
                    continue;
                }

                foreach (PropertyDefinitionHandle propertyHandle
                    in grouping.GetProperties())
                {
                    PropertyDefinition property =
                        reader.GetPropertyDefinition(propertyHandle);
                    PropertyAccessors accessors =
                        property.GetAccessors();
                    bool hasAccessor =
                        !accessors.Getter.IsNil
                        || !accessors.Setter.IsNil;
                    if (!hasAccessor)
                        continue;
                    bool propertyExcluded =
                        typeExcluded
                        || (!request.IncludeNonPublic
                            && (AttributeReader.HasHiddenAttribute(
                                    reader,
                                    property.GetCustomAttributes())
                                || (!IsIncludedExtensionAccessor(
                                        reader,
                                        accessors.Getter,
                                        includeNonPublic: false)
                                    && !IsIncludedExtensionAccessor(
                                        reader,
                                        accessors.Setter,
                                        includeNonPublic: false))));
                    if (propertyExcluded)
                        excluded++;
                    else
                        included.Add(
                            MetadataTokens.GetToken(propertyHandle));
                }
            }
        }

        return new(included, excluded);
    }

    private static void AuditRejectedExtensionCandidates(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        HashSet<int> admittedDeclarations,
        ImmutableArray<MetadataRelationDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!request.IncludesType(reader, typeHandle))
                continue;

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            TypeAttributes attributes = type.Attributes;
            bool isStatic =
                (attributes & TypeAttributes.Sealed) != 0
                && (attributes & TypeAttributes.Abstract) != 0;
            if (!isStatic
                || !AttributeReader.HasExtensionAttribute(
                    reader,
                    type.GetCustomAttributes())
                || (!request.IncludeNonPublic
                    && AttributeReader.HasHiddenAttribute(
                        reader,
                        type.GetCustomAttributes())))
            {
                continue;
            }

            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Static) == 0
                    || (!request.IncludeNonPublic
                        && (method.Attributes
                            & MethodAttributes.MemberAccessMask)
                            != MethodAttributes.Public)
                    || !AttributeReader.HasExtensionAttribute(
                        reader,
                        method.GetCustomAttributes())
                    || (!request.IncludeNonPublic
                        && AttributeReader.HasHiddenAttribute(
                            reader,
                            method.GetCustomAttributes())))
                {
                    continue;
                }

                AddRejectedExtensionDiagnostic(
                    MetadataTokens.GetToken(methodHandle),
                    admittedDeclarations,
                    diagnostics);
            }

            foreach (TypeDefinitionHandle groupingHandle
                in type.GetNestedTypes())
            {
                TypeDefinition grouping =
                    reader.GetTypeDefinition(groupingHandle);
                if (!AttributeReader.HasExtensionAttribute(
                        reader,
                        grouping.GetCustomAttributes()))
                {
                    continue;
                }

                foreach (PropertyDefinitionHandle propertyHandle
                    in grouping.GetProperties())
                {
                    PropertyDefinition property =
                        reader.GetPropertyDefinition(propertyHandle);
                    PropertyAccessors accessors =
                        property.GetAccessors();
                    bool included =
                        IsIncludedExtensionAccessor(
                            reader,
                            accessors.Getter,
                            request.IncludeNonPublic)
                        || IsIncludedExtensionAccessor(
                            reader,
                            accessors.Setter,
                            request.IncludeNonPublic);
                    if (!included
                        || (!request.IncludeNonPublic
                            && AttributeReader.HasHiddenAttribute(
                                reader,
                                property.GetCustomAttributes())))
                    {
                        continue;
                    }

                    AddRejectedExtensionDiagnostic(
                        MetadataTokens.GetToken(propertyHandle),
                        admittedDeclarations,
                        diagnostics);
                }
            }
        }
    }

    private static bool IsIncludedExtensionAccessor(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        bool includeNonPublic) =>
        !handle.IsNil
        && (includeNonPublic
            || (reader.GetMethodDefinition(handle).Attributes
                & MethodAttributes.MemberAccessMask)
                == MethodAttributes.Public
                && !AttributeReader.HasHiddenAttribute(
                    reader,
                    reader.GetMethodDefinition(handle)
                        .GetCustomAttributes()));

    private static void AddRejectedExtensionDiagnostic(
        int metadataToken,
        HashSet<int> admittedDeclarations,
        ImmutableArray<MetadataRelationDiagnostic>.Builder diagnostics)
    {
        if (admittedDeclarations.Contains(metadataToken))
            return;

        diagnostics.Add(
            UnsupportedDiagnostic(
                MetadataRelationFamily.Extensions,
                metadataToken,
                "An extension declaration candidate could not be decoded exactly."));
    }
}
