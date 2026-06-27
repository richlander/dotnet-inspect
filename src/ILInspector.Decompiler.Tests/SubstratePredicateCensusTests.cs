using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Decompiler.Tests;

public class SubstratePredicateCensusTests
{
    static readonly Dictionary<PredicateKey, string> ApprovedLocalPredicates = new()
    {
        [new("BooleanFoldingPass.cs", "SameNullTestedLoad")] =
            "Pass-local composition of PlaceIdentity.SameVariable and SameStackSlot for the null-tested value accepted by boolean folding.",
        [new("ConstructorCallDiagnosticsPass.cs", "SameDefinition")] =
            "Constructor diagnostics compare the declaring type with the current/base type; this is diagnostic routing, not a reusable rewrite gate.",
        [new("EhStructuringPass.cs", "SameZone")] =
            "Control-flow legality check over EH constructs and branch offsets, not a place/member identity predicate.",
        [new("ExpressionInliningPass.cs", "IsInsideCatchFilter")] =
            "Inlining-specific EH guard that first finds the containing catch clause before using ReferenceOwnership.IsInside on its filter.",
        [new("ExpressionInliningPass.cs", "SameRegions")] =
            "Inlining-specific EH range equality for candidate movement; the predicate is over function region spans, not IR identity.",
        [new("LocalFunctionRaisingPass.cs", "SameLocalFunctionMethod")] =
            "Local-function mangled names are unique within the declaring type; this pass-local check distinguishes self-recursion from nested or mutual local-function calls.",
        [new("NullCoalescingAssignmentPass.cs", "SameField")] =
            "FieldRef equality paired with pass-owned receiver/index-shape checks for null-coalescing assignment.",
        [new("NullCoalescingAssignmentPass.cs", "SameReceiver")] =
            "Pass-local receiver admissibility: null static receiver or PlaceIdentity.SameVariable only.",
        [new("StructuringPass.cs", "IsInsideFinallyBody")] =
            "Structuring-specific leave-target guard for finally bodies, not a general ancestor/location helper.",
    };

    [Fact]
    public void PassLocalPredicateHelpers_AreClassified()
    {
        var actual = EnumeratePassLocalPredicates().ToArray();
        var actualKeys = actual.Select(p => p.Key).ToHashSet();
        var approvedKeys = ApprovedLocalPredicates.Keys.ToHashSet();

        var unclassified = actual
            .Where(p => !approvedKeys.Contains(p.Key))
            .OrderBy(p => p.Key.File, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Method, StringComparer.Ordinal)
            .Select(p => $"{p.Key.File}:{p.Line}: {p.Key.Method}")
            .ToArray();
        var staleApprovals = approvedKeys
            .Where(key => !actualKeys.Contains(key))
            .OrderBy(key => key.File, StringComparer.Ordinal)
            .ThenBy(key => key.Method, StringComparer.Ordinal)
            .Select(key => $"{key.File}: {key.Method}")
            .ToArray();
        var missingRationale = ApprovedLocalPredicates
            .Where(kvp => string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => $"{kvp.Key.File}: {kvp.Key.Method}")
            .ToArray();

        Assert.True(unclassified.Length == 0,
            "Pass-local predicate-shaped helpers must be migrated to substrate atoms or approved with a rationale: "
            + string.Join(", ", unclassified));
        Assert.True(staleApprovals.Length == 0,
            "Approved pass-local predicate helpers no longer found; remove stale approvals: "
            + string.Join(", ", staleApprovals));
        Assert.True(missingRationale.Length == 0,
            "Approved pass-local predicate helpers must carry a rationale: "
            + string.Join(", ", missingRationale));
    }

    static IEnumerable<PredicateHit> EnumeratePassLocalPredicates()
    {
        string passesDirectory = Path.Combine(FindRepositoryRoot(), "src", "ILInspector.Decompiler", "Pipeline", "Passes");
        foreach (string path in Directory.EnumerateFiles(passesDirectory, "*.cs").OrderBy(static p => p, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(path);
            var tree = CSharpSyntaxTree.ParseText(text, path: path, cancellationToken: TestContext.Current.CancellationToken);
            var root = tree.GetCompilationUnitRoot(TestContext.Current.CancellationToken);

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                string methodName = method.Identifier.ValueText;
                if (!IsStaticBoolCensusPredicate(method.Modifiers, method.ReturnType, methodName))
                {
                    continue;
                }

                int line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                yield return new PredicateHit(new PredicateKey(Path.GetFileName(path), methodName), line);
            }

            foreach (var localFunction in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                string methodName = localFunction.Identifier.ValueText;
                if (!IsStaticBoolCensusPredicate(localFunction.Modifiers, localFunction.ReturnType, methodName))
                {
                    continue;
                }

                int line = localFunction.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                yield return new PredicateHit(new PredicateKey(Path.GetFileName(path), methodName), line);
            }
        }
    }

    static bool IsStaticBoolCensusPredicate(SyntaxTokenList modifiers, TypeSyntax returnType, string name)
        => modifiers.Any(SyntaxKind.StaticKeyword)
            && ReturnsBool(returnType)
            && IsCensusPredicateName(name);

    static bool IsCensusPredicateName(string name)
        => name.StartsWith("Same", StringComparison.Ordinal)
            || name.StartsWith("IsInside", StringComparison.Ordinal);

    static bool ReturnsBool(TypeSyntax returnType)
        => returnType switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.IsKind(SyntaxKind.BoolKeyword),
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == nameof(Boolean),
            _ => false,
        };

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string passesDirectory = Path.Combine(directory.FullName, "src", "ILInspector.Decompiler", "Pipeline", "Passes");
            if (Directory.Exists(passesDirectory))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing src/ILInspector.Decompiler/Pipeline/Passes.");
    }

    readonly record struct PredicateKey(string File, string Method);
    readonly record struct PredicateHit(PredicateKey Key, int Line);
}
