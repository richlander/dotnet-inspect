using ILInspector.Findings;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// A notable method classification bound to Metadata's stable member identity.
/// Rendered declaration text remains outside the Finding payload.
/// </summary>
public sealed record ClassifiedMethodObservation(
    MemberAnchor Anchor,
    MethodClassification Classification,
    string ReturnType,
    string? ModuleName = null)
{
    public MemberAnchor Anchor { get; }
        = Anchor ?? throw new ArgumentNullException(nameof(Anchor));

    public string ReturnType { get; }
        = ReturnType ?? throw new ArgumentNullException(nameof(ReturnType));
}

public static partial class MetadataFindings
{
    public static readonly FindingDescriptor ClassifiedMethodDescriptor =
        new("metadata.classified-method", "Classified method");

    public static FindingInspection<ClassifiedMethodObservation> InspectClassifiedMethods(
        IEnumerable<ClassifiedMethodInfo> methods,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectClassifiedMethodsCore(methods, subject, nameof(methods));
    }

    static FindingInspection<ClassifiedMethodObservation> InspectClassifiedMethodsCore(
        IEnumerable<ClassifiedMethodInfo> methods,
        FindingSubject subject,
        string parameterName)
    {
        var materialized = methods
            .Select(method => method is null
                ? throw new ArgumentException(
                    "Classified method inventory cannot contain null observations.",
                    parameterName)
                : method)
            .ToArray();

        if (materialized.Any(static method => method.Anchor is null || method.ReturnType is null))
        {
            return new FindingInspection<ClassifiedMethodObservation>.Failed(
                new InspectionError(
                    subject,
                    ClassifiedMethodDescriptor,
                    "One or more classified methods do not have structured method shape."));
        }

        return InspectInventory(
            materialized.Select(static method => new ClassifiedMethodObservation(
                method.Anchor!,
                method.Classification,
                method.ReturnType!,
                method.ModuleName)),
            subject,
            ClassifiedMethodDescriptor,
            static method => JoinSortKey(
                method.Anchor.CanonicalSignature,
                method.Classification.ToString()),
            static method => JoinSortKey(
                method.Anchor.CanonicalSignature,
                method.Classification.ToString(),
                method.ReturnType,
                method.ModuleName),
            parameterName);
    }

    public static FindingComparison<ClassifiedMethodObservation> CompareClassifiedMethods(
        IEnumerable<ClassifiedMethodInfo> oldMethods,
        IEnumerable<ClassifiedMethodInfo> newMethods,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldMethods);
        ArgumentNullException.ThrowIfNull(newMethods);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectClassifiedMethodsCore(oldMethods, subject, nameof(oldMethods)),
            InspectClassifiedMethodsCore(newMethods, subject, nameof(newMethods)));
    }
}
