using ILInspector.Analysis;
using ILInspector.Findings;

namespace DotnetInspector.Output;

internal static class FindingTargetFormatter
{
    public static string Format(Finding<AllocationOccurrence> finding)
        => Format(finding.Subject.Display, finding);

    public static string Format(
        string subjectDisplay,
        Finding<AllocationOccurrence> finding)
    {
        var occurrence = finding.Payload;
        var allocatedType = occurrence.AllocatedType?.ToQualifiedDisplayString()
            ?? occurrence.RuntimeAllocationType
            ?? occurrence.Detail
            ?? "?";
        return $"{subjectDisplay} :: {occurrence.Source}/{occurrence.Kind} {allocatedType}";
    }

    public static string Format(Finding<DirectCall> finding)
        => Format(finding.Subject.Display, finding);

    public static string Format(
        string subjectDisplay,
        Finding<DirectCall> finding)
    {
        var callee = finding.Payload.Callee;
        if (callee.Kind == MemberKind.Unsupported)
            return $"{subjectDisplay} :: {callee.DeclaringType.ToDisplayString()}";

        var typeArguments = callee.TypeArguments.IsDefaultOrEmpty
            ? ""
            : $"<{string.Join(", ", callee.TypeArguments.Select(type => type.ToQualifiedDisplayString()))}>";
        var parameters = string.Join(
            ", ",
            callee.ParameterTypes.Select(type => type.ToQualifiedDisplayString()));
        var declaringType = callee.DeclaringType.ToQualifiedDisplayString();
        var calleeDisplay = callee.Kind == MemberKind.Constructor
            ? $"{declaringType}{typeArguments}({parameters})"
            : $"{declaringType}.{callee.Name}{typeArguments}({parameters})";
        return $"{subjectDisplay} :: {calleeDisplay}";
    }

    public static string Format(Finding<UnsafetyOccurrence> finding)
        => Format(finding.Subject.Display, finding);

    public static string Format(
        string subjectDisplay,
        Finding<UnsafetyOccurrence> finding)
    {
        var occurrence = finding.Payload;
        string detail = string.IsNullOrWhiteSpace(occurrence.Detail)
            ? ""
            : $" {occurrence.Detail}";
        return $"{subjectDisplay} :: {occurrence.Kind}{detail}";
    }
}
