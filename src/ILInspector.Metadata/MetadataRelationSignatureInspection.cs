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
        MetadataSignatureRelationEvidence> ScanSignatures(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        MetadataVisibilityClassification visibility,
        MetadataOperationContext operation,
        CancellationToken cancellationToken)
    {
        var evidence =
            ImmutableArray.CreateBuilder<
                MetadataSignatureRelationEvidence>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        int considered = 0;
        int excluded = 0;
        int examined = 0;
        int unavailable = 0;
        bool limited = false;
        try
        {
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                if (!request.IncludesType(reader, typeHandle))
                    continue;
                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                bool typeExcluded =
                    !request.IncludeNonPublic
                    && !visibility.IsExternallyVisible(typeHandle);
                foreach (MethodDefinitionHandle methodHandle
                    in type.GetMethods())
                {
                    considered++;
                    if (typeExcluded
                        || (!request.IncludeNonPublic
                            && !MetadataDeclarationQuery
                                .IsPublicOrProtected(
                                    reader.GetMethodDefinition(
                                        methodHandle))))
                    {
                        excluded++;
                    }
                }
            }

            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!request.IncludesType(reader, typeHandle))
                    continue;
                if (!request.IncludeNonPublic
                    && !visibility.IsExternallyVisible(typeHandle))
                {
                    continue;
                }

                TypeDefinition type =
                    reader.GetTypeDefinition(typeHandle);
                MetadataTypeDefinitionName? sourceName =
                    ReadTypeName(
                        reader,
                        typeHandle,
                        MetadataRelationFamily.Signatures,
                        diagnostics);
                if (sourceName is null)
                {
                    unavailable += type.GetMethods().Count(
                        methodHandle =>
                            request.IncludeNonPublic
                            || MetadataDeclarationQuery
                                .IsPublicOrProtected(
                                    reader.GetMethodDefinition(
                                        methodHandle)));
                    continue;
                }
                MetadataTypeDefinitionAddress source =
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        typeHandle);

                foreach (MethodDefinitionHandle methodHandle
                    in type.GetMethods())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    MethodDefinition method =
                        reader.GetMethodDefinition(methodHandle);
                    operation.Charge(
                        MetadataOperationDimension
                            .DeclarationCandidates);
                    if (!request.IncludeNonPublic
                        && !MetadataDeclarationQuery
                            .IsPublicOrProtected(method))
                    {
                        continue;
                    }

                    MetadataMethodSignatureDecodeResult signature =
                        MetadataTypeIdentityDecoder.DecodeMethod(
                            reader,
                            type,
                            method,
                            operation);
                    if (signature
                        is MetadataMethodSignatureDecodeResult.Rejected
                            rejected)
                    {
                        diagnostics.Add(
                            UnsupportedDiagnostic(
                                MetadataRelationFamily.Signatures,
                                MetadataTokens.GetToken(methodHandle),
                                rejected.Detail));
                        unavailable++;
                        continue;
                    }

                    MemberAnchor anchor;
                    try
                    {
                        anchor = ApiMemberIdentity.CreateMethodAnchor(
                            reader,
                            typeHandle,
                            method);
                    }
                    catch (Exception exception)
                        when (exception is BadImageFormatException
                            or ArgumentException
                            or InvalidOperationException)
                    {
                        diagnostics.Add(
                            MalformedDiagnostic(
                                MetadataRelationFamily.Signatures,
                                MetadataTokens.GetToken(methodHandle),
                                exception.Message));
                        unavailable++;
                        continue;
                    }

                    MetadataMethodAddress methodAddress =
                        MetadataMethodAddress.Create(
                            reader,
                            methodHandle);
                    MetadataMethodSignatureIdentity identity =
                        ((MetadataMethodSignatureDecodeResult.Decoded)
                            signature).Signature;
                    operation.Charge(
                        MetadataOperationDimension.RelationshipEdges);
                    evidence.Add(
                        new(
                            source,
                            sourceName,
                            methodAddress,
                            anchor,
                            MetadataSignatureRelationKind.Returns,
                            null,
                            identity.ReturnType));
                    for (int index = 0;
                        index < identity.ParameterTypes.Length;
                        index++)
                    {
                        operation.Charge(
                            MetadataOperationDimension.RelationshipEdges);
                        evidence.Add(
                            new(
                                source,
                                sourceName,
                                methodAddress,
                                anchor,
                                MetadataSignatureRelationKind.Accepts,
                                index,
                                identity.ParameterTypes[index]));
                    }
                    examined++;
                }
            }
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            limited = true;
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.Signatures,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.Signatures,
                    null,
                    exception.Message));
        }

        int remaining =
            considered - examined - excluded - unavailable;
        if (considered == 0 && diagnostics.Count != 0)
        {
            return CompleteOrPartial(
                evidence,
                diagnostics,
                new(1, 0, 0, 1, 0));
        }
        if (!limited)
            unavailable += remaining;
        return CompleteOrPartial(
            evidence,
            diagnostics,
            new(
                considered,
                examined,
                excluded,
                unavailable,
                limited ? remaining : 0));
    }
}
