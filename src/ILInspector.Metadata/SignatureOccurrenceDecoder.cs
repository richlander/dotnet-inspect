using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata;

/// <summary>Bounded named-occurrence evidence for one member signature; does not resolve names.</summary>
public static class SignatureOccurrenceDecoder
{
    public static SignatureOccurrenceDecodeResult Decode(PEReader image, EntityHandle member) =>
        Decode(image, member, null, SignatureOccurrenceLimits.Default);

    internal static SignatureOccurrenceDecodeResult Decode(
        PEReader image,
        EntityHandle member,
        SignatureOccurrenceMetrics? metrics,
        SignatureOccurrenceLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (member.IsNil || member.Kind is not (
            HandleKind.MethodDefinition or HandleKind.FieldDefinition or HandleKind.PropertyDefinition))
        {
            throw new ArgumentException(
                "A non-nil MethodDef, FieldDef, or PropertyDef handle is required.", nameof(member));
        }
        var effectiveLimits = limits ?? SignatureOccurrenceLimits.Default;
        effectiveLimits.Validate();

        var format = MetadataImageFormatClassifier.Classify(image);
        if (format is not MetadataImageFormatResult.SupportedEcma335)
        {
            return new SignatureOccurrenceDecodeResult.Rejected(format switch
            {
                MetadataImageFormatResult.NoMetadata => SignatureOccurrenceRejectionReason.NoMetadata,
                MetadataImageFormatResult.UnsupportedWindowsMetadata =>
                    SignatureOccurrenceRejectionReason.UnsupportedWindowsMetadata,
                MetadataImageFormatResult.MalformedRoot => SignatureOccurrenceRejectionReason.MalformedMetadata,
                _ => throw new InvalidOperationException("Unknown metadata image classification."),
            });
        }

        try
        {
            var reader = ReadMetadata(image);
            var budget = new SignatureOccurrenceWorkBudget(effectiveLimits, metrics);
            var provider = new SignatureOccurrenceProvider(image, budget);
            var occurrences = member.Kind switch
            {
                HandleKind.MethodDefinition => DecodeMethod(
                    reader, reader.GetMethodDefinition((MethodDefinitionHandle)member), provider),
                HandleKind.FieldDefinition => DecodeField(
                    reader, reader.GetFieldDefinition((FieldDefinitionHandle)member), provider),
                HandleKind.PropertyDefinition => DecodeProperty(
                    reader, reader.GetPropertyDefinition((PropertyDefinitionHandle)member), provider),
                _ => throw new InvalidOperationException("Unexpected validated member kind."),
            };
            return new SignatureOccurrenceDecodeResult.Decoded(occurrences);
        }
        catch (SignatureOccurrenceRejectedException rejection)
        {
            return new SignatureOccurrenceDecodeResult.Rejected(rejection.Reason);
        }
        catch (BadImageFormatException)
        {
            return new SignatureOccurrenceDecodeResult.Rejected(
                SignatureOccurrenceRejectionReason.MalformedMetadata);
        }
    }

    static MetadataReader ReadMetadata(PEReader image)
    {
        try
        {
            return image.GetMetadataReader(MetadataReaderOptions.None);
        }
        catch (OverflowException)
        {
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.MalformedMetadata);
        }
    }

    static ImmutableArray<SignatureNamedTypeOccurrence> DecodeMethod(
        MetadataReader reader, MethodDefinition method, SignatureOccurrenceProvider provider)
    {
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
            reader, method.Signature, SignatureBlobGuard.Kind.Method, out var measurements))
        {
            provider.ObserveGuard(measurements);
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.UnsafeSignature);
        }
        try
        {
            var signature = method.DecodeSignature(provider, genericContext: (object?)null);
            return provider.Combine(signature.ReturnType, signature.ParameterTypes);
        }
        finally
        {
            provider.ObserveGuard(measurements);
        }
    }

    static ImmutableArray<SignatureNamedTypeOccurrence> DecodeField(
        MetadataReader reader, FieldDefinition field, SignatureOccurrenceProvider provider)
    {
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
            reader, field.Signature, SignatureBlobGuard.Kind.Field, out var measurements))
        {
            provider.ObserveGuard(measurements);
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.UnsafeSignature);
        }
        try
        {
            return field.DecodeSignature(provider, genericContext: (object?)null);
        }
        finally
        {
            provider.ObserveGuard(measurements);
        }
    }

    static ImmutableArray<SignatureNamedTypeOccurrence> DecodeProperty(
        MetadataReader reader, PropertyDefinition property, SignatureOccurrenceProvider provider)
    {
        if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
            reader, property.Signature, SignatureBlobGuard.Kind.Method, out var measurements))
        {
            provider.ObserveGuard(measurements);
            throw new SignatureOccurrenceRejectedException(SignatureOccurrenceRejectionReason.UnsafeSignature);
        }
        try
        {
            var signature = property.DecodeSignature(provider, genericContext: (object?)null);
            return provider.Combine(signature.ReturnType, signature.ParameterTypes);
        }
        finally
        {
            provider.ObserveGuard(measurements);
        }
    }
}
