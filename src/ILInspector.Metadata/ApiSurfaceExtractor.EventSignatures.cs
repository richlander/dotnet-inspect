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
            byte typeNullableContext,
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
        // An event's own NullableAttribute is read against the Type's
        // context, as Roslyn reads it. Without one, the adder's value
        // parameter carries the annotations against the adder's context.
        byte[]? nullableBytes = NullabilityReader.GetNullableBytes(
            reader,
            eventDefinition.GetCustomAttributes(),
            beforeDecodeWork);
        byte defaultNullableByte = typeNullableContext;
        if (nullableBytes is null)
        {
            nullableBytes = NullabilityReader.GetParameterNullableBytes(
                reader,
                adder.GetParameters(),
                1,
                beforeDecodeWork);
            defaultNullableByte = NullabilityReader.GetNullableContext(
                reader,
                adder.GetCustomAttributes(),
                beforeDecodeWork)
                ?? typeNullableContext;
        }

        if (eventDefinition.Type.Kind != HandleKind.TypeSpecification)
        {
            byte outer = nullableBytes is { Length: > 0 }
                ? nullableBytes[0]
                : defaultNullableByte;
            if (outer == 2
                && !eventType.EndsWith("?", StringComparison.Ordinal))
            {
                eventType += "?";
            }
        }
        else
        {
            // A constructed delegate type carries nested annotations, so the
            // whole tree takes the nullable, dynamic, and tuple-name streams.
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
                    defaultNullableByte);
                position = 0;
                eventNode.ApplyDynamic(
                    DynamicReader.GetDynamicFlags(
                        reader,
                        eventDefinition.GetCustomAttributes(),
                        beforeDecodeWork),
                    ref position);
                eventNode.ApplyTupleNames(
                    TupleElementNamesReader.GetTupleElementNames(
                        reader,
                        eventDefinition.GetCustomAttributes(),
                        beforeDecodeWork));
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
