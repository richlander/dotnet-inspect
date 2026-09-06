using System.Text.Json.Serialization;

namespace InspectWeb.Engine.SourceFacade;

[JsonConverter(typeof(JsonStringEnumConverter<BrowserMethodBodyResultKind>))]
public enum BrowserMethodBodyResultKind
{
    Succeeded,
    Failed,
    Canceled,
}

public sealed record BrowserMethodBodySelection(
    string TypeIdentity,
    string MemberName,
    string SelectorKey,
    int MetadataToken,
    string Label);

public sealed record BrowserMethodBodyTargets(
    string PackageId,
    string Version,
    string Framework,
    string Assembly,
    string ModuleVersionId,
    BrowserMethodBodySelection Before,
    BrowserMethodBodySelection[] Methods);

public sealed record BrowserMethodBodyComparisonRequest(
    string PackageId,
    string Version,
    string Framework,
    string Assembly,
    string ModuleVersionId,
    BrowserMethodBodySelection Before,
    BrowserMethodBodySelection After);

public sealed record BrowserMethodBodyTargetsResult(
    int Version,
    BrowserMethodBodyResultKind Kind,
    BrowserMethodBodyTargets? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason);

public sealed record BrowserMethodBodyComparisonResult(
    int Version,
    BrowserMethodBodyResultKind Kind,
    BrowserMethodBodyComparison? Value,
    BrowserTypeSourceFailureKind? FailureKind,
    string? Error,
    string? Diagnostic,
    string? Reason);

public sealed record BrowserMethodBodyComparison(
    BrowserMethodBodyComparisonRequest Request,
    string Stage,
    string Outcome,
    BrowserMethodBodyProducer[] Producers,
    BrowserMethodBodyDiagnostic[] Diagnostics);

public sealed record BrowserMethodBodyEndpoint(
    string State,
    string? ModuleVersionId,
    int? MetadataToken,
    string? TargetState,
    string? Detail);

public sealed record BrowserMethodBodyProducer(
    string Producer,
    string Outcome,
    string NativeVerdict,
    BrowserMethodBodyEndpoint Before,
    BrowserMethodBodyEndpoint After,
    BrowserCSharpBodyEvidence? CSharp,
    BrowserIlBodyEvidence? Il,
    BrowserMethodBodyDiagnostic[] Diagnostics);

public sealed record BrowserMethodBodyDiagnostic(
    string Kind,
    string? Side,
    string Message,
    string? Detail,
    int? HunkId = null,
    int? SubjectToken = null,
    string? Mechanism = null,
    string? Path = null);

public sealed record BrowserCSharpBodyEvidence(
    bool IsExact,
    BrowserCSharpBodyRow[] Rows);

public sealed record BrowserCSharpBodyOperation(
    string Kind,
    string Value);

public sealed record BrowserCSharpBodyRow(
    string AssemblyIdentity,
    string StableMemberKey,
    string Member,
    string ChangeId,
    string Message,
    int HunkId,
    string Kind,
    int? Line,
    string? SourceCoordinate,
    string Fidelity,
    string Text,
    string? OldValue,
    string? NewValue,
    BrowserCSharpBodyOperation? OldOperation,
    BrowserCSharpBodyOperation? NewOperation);

public sealed record BrowserIlBodyEvidence(
    string Outcome,
    bool IsExact,
    bool IsAvailable,
    string? Failure,
    BrowserIlBodyRow[] Rows);

public sealed record BrowserIlBodyRow(
    int HunkId,
    string Kind,
    BrowserIlBodyOperation Operation,
    string Message);

public sealed record BrowserIlBodyOperation(
    int Offset,
    string OpcodeFamily,
    BrowserIlBodyOperand? Operand);

public sealed record BrowserIlBodyOperand(
    string Kind,
    string Value);
