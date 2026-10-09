using System.Reflection.Metadata;

namespace ILInspector.Metadata;

public static partial class ApiSurfaceExtractor
{
    internal static (
        string Name,
        string Type,
        string Signature,
        TypeNode? StructuralType)
        GetEventSignatureForIdentity(
            MetadataReader reader,
            GenericContext context,
            EventDefinition eventDefinition,
            MethodDefinition adder,
            Action<string>? beforeRetainText = null,
            Action<int>? beforeDecodeWork = null)
    {
        TypeNode? structuralType = null;
        string eventType = ResolveRequiredTypeName(
            reader,
            eventDefinition.Type,
            context,
            beforeRetainText,
            beforeDecodeWork,
            captureTypeNode: node => structuralType = node);
        byte[]? nullableBytes = NullabilityReader.GetNullableBytes(
            reader,
            eventDefinition.GetCustomAttributes(),
            beforeDecodeWork);
        nullableBytes ??= NullabilityReader.GetParameterNullableBytes(
            reader,
            adder.GetParameters(),
            1,
            beforeDecodeWork);
        if (nullableBytes is { Length: > 0 }
            && nullableBytes[0] == 2
            && !eventType.EndsWith("?", StringComparison.Ordinal))
        {
            eventType += "?";
        }

        string?[]? tupleNames =
            TupleElementNamesReader.GetTupleElementNames(
                reader,
                eventDefinition.GetCustomAttributes(),
                beforeDecodeWork);
        byte[]? dynamicFlags =
            eventDefinition.Type.Kind == HandleKind.TypeSpecification
                ? DynamicReader.GetDynamicFlags(
                    reader,
                    eventDefinition.GetCustomAttributes(),
                    beforeDecodeWork)
                : null;
        if (eventDefinition.Type.Kind == HandleKind.TypeSpecification
            && (dynamicFlags is not null || tupleNames is not null))
        {
            TypeNode eventNode = GuardedProviderDecode.TypeSpec(
                reader,
                (TypeSpecificationHandle)eventDefinition.Type,
                beforeRetainText is null
                    ? TypeNodeProvider.Instance
                    : new TypeNodeProvider(
                        beforeRetainText,
                        beforeDecodeWork),
                context,
                new DegradedTypeNode());
            if (!eventNode.IsDegraded)
            {
                int position = 0;
                eventNode.ApplyNullability(
                    nullableBytes,
                    ref position,
                    defaultByte: 0);
                position = 0;
                eventNode.ApplyDynamic(
                    dynamicFlags,
                    ref position);
                eventNode.ApplyTupleNames(tupleNames);
                eventType = eventNode.Render();
            }
        }

        string eventName = DecodeString(
            reader,
            eventDefinition.Name,
            beforeDecodeWork);
        return (
            eventName,
            eventType,
            $"{eventType} {SanitizeIdentifier(eventName)}",
            structuralType);
    }
}
