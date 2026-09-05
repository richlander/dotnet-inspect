using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Research;

static class ResearchFactFinding
{
    public static Finding<IAnnotation> Project<T>(
        Finding<T> source,
        IAnnotation annotation)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(annotation);
        return new Finding<IAnnotation>(
            source.Subject,
            Descriptor(annotation.Descriptor),
            source.Key,
            annotation,
            source.Ordinal,
            annotation.Detail);
    }

    public static Finding<IAnnotation> Create(
        FindingSubject subject,
        IAnnotation annotation,
        FindingKey key,
        int? ordinal = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(annotation);
        return new Finding<IAnnotation>(
            subject,
            Descriptor(annotation.Descriptor),
            key,
            annotation,
            ordinal,
            annotation.Detail);
    }

    public static FindingSubject Subject(IrFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new FindingSubject(
            $"{function.AssemblyPath}|{function.MetadataToken:X8}",
            function.Name);
    }

    static FindingDescriptor Descriptor(AnnotationDescriptor descriptor)
        => new(descriptor.Id, descriptor.Title);
}
