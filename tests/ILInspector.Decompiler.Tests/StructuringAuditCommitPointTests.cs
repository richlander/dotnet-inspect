using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Decompiler.Tests;

public class StructuringAuditCommitPointTests
{
    [Fact]
    public void StructuringAuditCommitsAfterEveryDeclineAndInstallation()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ILInspector.Decompiler",
            "Pipeline",
            "Passes",
            "StructuringPass.cs");
        var root = CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                path: path,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);

        var structure = Method(root, "Structure");
        var structureStep = AssertCommitOrder(
            structure,
            descriptionFragment: "structure container at",
            successMethods: ["RecordStructured"],
            declineMethods: ["RecordStop"]);
        Assert.All(
            ExecutableNodes(structure).OfType<ReturnStatementSyntax>(),
            decline => AssertDeclineBeforeStep(decline, structureStep));

        var retained = Method(root, "TryStructureRetainedRegions");
        var step = AssertCommitOrder(
            retained,
            descriptionFragment: "retained-merge region(s)",
            successMethods: ["RecordStructured", "RecordRetainedRegion"],
            declineMethods: ["RecordRetainedDecline"]);
        var returns = ExecutableNodes(retained).OfType<ReturnStatementSyntax>().ToArray();
        var success = Assert.Single(
            returns,
            statement => statement.Expression?.RawKind
                == (int)SyntaxKind.TrueLiteralExpression);
        Assert.All(
            returns.Where(statement => !ReferenceEquals(statement, success)),
            decline => AssertDeclineBeforeStep(decline, step));
    }

    static InvocationExpressionSyntax AssertCommitOrder(
        MethodDeclarationSyntax method,
        string descriptionFragment,
        IReadOnlyList<string> successMethods,
        IReadOnlyList<string> declineMethods)
    {
        var invocations = ExecutableNodes(method).OfType<InvocationExpressionSyntax>().ToArray();
        var auditSteps = invocations
            .Where(invocation => InvocationName(invocation) is "StepOver" or "StepInto")
            .ToArray();
        var step = Assert.Single(
            auditSteps);
        Assert.Equal("StepOver", InvocationName(step));
        Assert.Contains(descriptionFragment, step.ArgumentList.ToString());
        var install = Assert.Single(
            invocations,
            invocation => InvocationName(invocation) == "ReplaceWith");
        Assert.True(
            step.SpanStart < install.SpanStart,
            $"Audit step at line {Line(step)} must precede installation at line {Line(install)}.");

        var declineRecords = invocations
            .Where(invocation => declineMethods.Contains(InvocationName(invocation)))
            .ToArray();
        Assert.NotEmpty(declineRecords);
        Assert.All(
            declineRecords,
            record => Assert.True(
                record.SpanStart < step.SpanStart,
                $"{InvocationName(record)} at line {Line(record)} occurs after the audit step at line {Line(step)}."));

        var successRecords = invocations
            .Where(invocation => InvocationName(invocation) is "RecordStructured" or "RecordRetainedRegion")
            .ToArray();
        Assert.Equal(successMethods.Count, successRecords.Length);
        foreach (string successMethod in successMethods)
        {
            var record = Assert.Single(
                successRecords,
                invocation => InvocationName(invocation) == successMethod);
            Assert.True(
                install.SpanStart < record.SpanStart,
                $"{successMethod} at line {Line(record)} precedes installation at line {Line(install)}.");
        }
        return step;
    }

    static IEnumerable<SyntaxNode> ExecutableNodes(MethodDeclarationSyntax method)
    {
        var body = Assert.IsType<BlockSyntax>(method.Body);
        return body.DescendantNodes(
            static node => node is not LocalFunctionStatementSyntax
                and not AnonymousFunctionExpressionSyntax);
    }

    static void AssertDeclineBeforeStep(
        ReturnStatementSyntax decline,
        InvocationExpressionSyntax step)
        => Assert.True(
            decline.SpanStart < step.SpanStart,
            $"Decline at line {Line(decline)} occurs after the audit step at line {Line(step)}.");

    static MethodDeclarationSyntax Method(CompilationUnitSyntax root, string name)
        => Assert.Single(
            root.DescendantNodes().OfType<MethodDeclarationSyntax>(),
            method => method.Identifier.ValueText == name);

    static string InvocationName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => "",
        };

    static int Line(CSharpSyntaxNode node)
        => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            string path = Path.Combine(
                directory.FullName,
                "src",
                "ILInspector.Decompiler",
                "Pipeline",
                "Passes",
                "StructuringPass.cs");
            if (File.Exists(path))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            "Could not find repository root containing StructuringPass.cs.");
    }
}
