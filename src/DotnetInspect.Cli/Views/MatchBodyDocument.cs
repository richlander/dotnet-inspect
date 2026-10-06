using ILInspector.Analysis;
using ILInspector.CSharp;

namespace DotnetInspect.Cli.Views;

/// <summary>
/// JSON envelope for <c>match --body --json</c>. The structural result stays independent
/// of the native body comparisons; plain <c>match --json</c> keeps its flat document.
/// </summary>
public sealed record MatchBodyDocument(
    StructuralCloneComparisonDocument Match,
    MethodBodyDiffDocument Body);
