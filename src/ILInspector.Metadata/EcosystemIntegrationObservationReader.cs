using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

internal static class EcosystemIntegrationObservationReader
{
    internal static EcosystemIntegrationObservationContext Read(MetadataReader reader)
    {
        var types = ImmutableArray.CreateBuilder<EcosystemIntegrationTypeObservation>();
        var methods = ImmutableArray.CreateBuilder<EcosystemIntegrationMethodObservation>();
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            if (!definition.IsPublic)
                continue;

            string typeName = reader.GetFullTypeName(definition);
            MetadataTypeDefinitionName? definitionName =
                MetadataTypeDefinitionNameReader.Read(reader, handle)
                    is MetadataTypeDefinitionNameReadResult.Read read
                        ? read.Name
                        : null;
            var type = new EcosystemIntegrationTypeObservation(
                typeName,
                definitionName,
                AttributeReader.ReadTypeDiscoveryAttributes(
                    reader,
                    definition.GetCustomAttributes()));
            types.Add(type);
            AddStarterMethods(methods, reader, handle, definition, type);
        }

        return new EcosystemIntegrationObservationContext(
            types.ToImmutable(),
            methods.ToImmutable());
    }

    static void AddStarterMethods(
        ImmutableArray<EcosystemIntegrationMethodObservation>.Builder methods,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDefinition,
        EcosystemIntegrationTypeObservation declaringType)
    {
        TypeAttributes attributes = typeDefinition.Attributes;
        bool isStatic = (attributes & TypeAttributes.Sealed) != 0
                        && (attributes & TypeAttributes.Abstract) != 0;
        if (!isStatic
            || !AttributeReader.HasExtensionAttribute(
                reader,
                typeDefinition.GetCustomAttributes()))
        {
            return;
        }

        foreach (MethodDefinitionHandle handle in typeDefinition.GetMethods())
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (method.Attributes & MethodAttributes.Static) == 0
                || !AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes()))
            {
                continue;
            }

            string name = reader.GetString(method.Name);
            GenericContext context = GenericContext.ForMethod(reader, typeDefinition, method);
            MethodSignature<string> signature;
            try
            {
                signature = GuardedSignatureText.MethodText(reader, method, context)
                    .GetValueOrThrow();
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            if (signature.ParameterTypes.Length == 0)
                continue;

            EcosystemIntegrationApiEvidence? evidence = null;
            if (declaringType.Definition is { } definitionName)
            {
                try
                {
                    ExtensionMemberAnchorInfo anchor =
                        ApiMemberIdentity.CreateExtensionMethodAnchorInfo(
                            reader,
                            typeHandle,
                            method);
                    var displaySignature =
                        ApiSurfaceExtractor.GetMethodSignatureForIdentity(
                            reader,
                            GenericContext.ForType(
                                reader,
                                typeDefinition),
                            handle,
                            method,
                            NullabilityReader.GetTypeNullableContext(
                                reader,
                                typeHandle),
                            captureExtensionReceiver: true);
                    if (displaySignature.IsDegraded)
                        throw new BadImageFormatException(
                            "The Integration API display signature is degraded.");
                    evidence = new EcosystemIntegrationApiEvidence(
                        anchor.Anchor,
                        definitionName,
                        anchor.ExtendedTypeReference,
                        anchor.ReturnTypeReference,
                        ApiSurfaceExtractor.ClassifyMethodKind(
                            name,
                            isFinalizer: false,
                            isExplicitInterfaceImplementation: false),
                        displaySignature.Text,
                        declaringType.DiscoveryAttributes,
                        AttributeReader.HasEditorBrowsableNeverAttribute(
                            reader,
                            method.GetCustomAttributes()));
                }
                catch (BadImageFormatException)
                {
                    evidence = null;
                }
            }

            methods.Add(new EcosystemIntegrationMethodObservation(
                declaringType,
                name,
                signature,
                evidence));
        }
    }
}
