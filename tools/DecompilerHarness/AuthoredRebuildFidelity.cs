using System.Net;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Text;

using DotnetInspector.Core;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

enum AuthoredRebuildOutcome
{
    Exact,
    IlDifferent,
    RecompileFailed,
    ContextFailed,
    SourceAbsent,
    SourceFailed,
}

enum AuthoredBuildContextStatus
{
    Recorded,
    Incomplete,
    Drift,
    Failed,
}

sealed record AuthoredBuildContextAssessment(
    AuthoredBuildContextStatus Status,
    bool IsDeterministic,
    string Detail,
    IReadOnlyDictionary<string, string>? RecordedOptions = null);

sealed record AuthoredRebuildFidelityResult(
    ReturnToSender.Result DecompilerLane,
    AuthoredRebuildOutcome Outcome,
    SourceChecksumVerification? ChecksumVerification,
    AuthoredBuildContextAssessment BuildContext,
    string? Detail,
    ImplementationMemberDiffResult? ImplementationDiff);

static class AuthoredRebuildFidelity
{
    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
        => RunAsync(assemblies, cap, maxExamples).GetAwaiter().GetResult();

    static async Task<int> RunAsync(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples)
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        using var httpClient = HttpClientFactory.CreateClient();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
        IReadOnlyList<AuthoredRebuildFidelityResult> results =
            await EvaluateAssembliesAsync(
                assemblies,
                cap,
                httpClient,
                fetcher);

        WriteReport(results, maxExamples);
        return results.Any(result =>
            result.Outcome is AuthoredRebuildOutcome.RecompileFailed
                or AuthoredRebuildOutcome.ContextFailed
                or AuthoredRebuildOutcome.SourceFailed)
            ? 1
            : 0;
    }

    internal static async Task<IReadOnlyList<AuthoredRebuildFidelityResult>>
        EvaluateAssembliesAsync(
            IReadOnlyList<string> assemblies,
            int cap,
            HttpClient httpClient,
            SourceFetcher fetcher,
            IPdbStore? pdbStore = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(fetcher);

        List<AuthoredRebuildFidelityResult> results = [];

        foreach (string assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            IReadOnlyList<ReturnToSender.Result> decompilerResults;
            try
            {
                decompilerResults = ReturnToSender.CompileBackPropertyGetters(
                    assemblyPath,
                    cap - results.Count);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
            {
                Console.Error.WriteLine(
                    $"Warning: authored rebuild skipped '{assemblyPath}' "
                    + $"({ex.GetType().Name}: {ex.Message}).");
                continue;
            }

            SourceLinkService? source = null;
            Exception? pdbAcquisitionFailure = null;
            try
            {
                source = SourceLinkService.Open(assemblyPath);
                await AcquirePdbAsync(
                    source,
                    httpClient,
                    pdbStore: pdbStore);
            }
            catch (Exception ex) when (IsPdbAcquisitionFailure(ex))
            {
                pdbAcquisitionFailure = ex;
            }
            IReadOnlyList<MetadataReference> compilationReferences =
                decompilerResults.FirstOrDefault()?.FinalRequest?
                    .CompilationClosure?.References
                ?? ReturnToSender.CompilationReferences(
                    assemblyPath).ToArray();
            AuthoredBuildContextAssessment buildContext =
                source is null
                    ? new AuthoredBuildContextAssessment(
                        AuthoredBuildContextStatus.Failed,
                        IsDeterministic: false,
                        "Portable PDB acquisition failed before build-context inspection.")
                    : AssessBuildContext(
                        SourceLinkInspector.InspectDll(assemblyPath).IsDeterministic,
                        MetadataFindings.InspectCompilationOptions(
                            source.Context,
                            new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath))),
                        MetadataFindings.InspectCompilationReferences(
                            source.Context,
                            new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath))),
                        compilationReferences);

            using (source)
            {
                foreach (var decompilerResult in decompilerResults)
                {
                    if (results.Count >= cap)
                        break;

                    AuthoredRebuildFidelityResult evaluated;
                    if (pdbAcquisitionFailure is not null)
                    {
                        evaluated = new AuthoredRebuildFidelityResult(
                            decompilerResult,
                            AuthoredRebuildOutcome.SourceFailed,
                            ChecksumVerification: null,
                            buildContext,
                            "Portable PDB acquisition failed: "
                                + pdbAcquisitionFailure.Message,
                            ImplementationDiff: null);
                    }
                    else if (source is { Context.NeedsPdb: true })
                    {
                        evaluated = new AuthoredRebuildFidelityResult(
                            decompilerResult,
                            AuthoredRebuildOutcome.SourceAbsent,
                            ChecksumVerification: null,
                            buildContext,
                            source.Context.WindowsPdbDetected
                                ? "A Windows PDB was found, but portable-PDB source mapping is unavailable."
                                : "No matching portable PDB is available.",
                            ImplementationDiff: null);
                    }
                    else
                    {
                        if (source is null)
                        {
                            throw new InvalidOperationException(
                                "PDB acquisition completed without a source context or failure.");
                        }

                        evaluated = await EvaluateAsync(
                            source,
                            fetcher,
                            decompilerResult,
                            buildContext);
                    }

                    results.Add(
                        evaluated with
                        {
                            DecompilerLane =
                                evaluated.DecompilerLane with
                                {
                                    FinalRequest = null,
                                },
                        });
                }
            }
        }

        return results;
    }

    internal static async Task<AuthoredRebuildFidelityResult> EvaluateAsync(
        SourceLinkService source,
        SourceFetcher fetcher,
        ReturnToSender.Result decompilerResult,
        AuthoredBuildContextAssessment buildContext)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(decompilerResult);
        ArgumentNullException.ThrowIfNull(buildContext);

        if (decompilerResult.FinalRequest is not { } request)
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.ContextFailed,
                ChecksumVerification: null,
                buildContext,
                "RTS did not produce a final artifact request.",
                ImplementationDiff: null);
        }

        var subject = new FindingSubject(
            decompilerResult.MemberAnchor?.StableSelector
                ?? $"{request.FullType}.{request.MethodName}",
            $"{request.FullType}.{request.MethodName}");
        var authored = await PdbSourceAcquisition.AcquireMemberAsync(
            source,
            MetadataTokens.GetToken(request.TargetMethod),
            request.MethodName,
            subject,
            fetcher);
        if (authored.Lines.Value is FindingInspection<string>.Absent absent)
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.SourceAbsent,
                authored.ChecksumVerification,
                buildContext,
                absent.Detail,
                ImplementationDiff: null);
        }
        if (authored.Lines.Value is FindingInspection<string>.Failed failed)
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.SourceFailed,
                authored.ChecksumVerification,
                buildContext,
                failed.Error.Reason,
                ImplementationDiff: null);
        }
        if (authored.Text is not { } authoredBody)
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.SourceFailed,
                authored.ChecksumVerification,
                buildContext,
                "Authored-source acquisition completed without body text.",
                ImplementationDiff: null);
        }

        if (!TryExtractTargetBody(
            authoredBody,
            request.MethodName,
            request.Function.Signature.Parameters.Length,
            out string targetBody))
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.SourceFailed,
                authored.ChecksumVerification,
                buildContext,
                "Checksum-verified authored member source did not contain the target body.",
                ImplementationDiff: null);
        }

        return CompileAuthoredBody(
            decompilerResult,
            targetBody,
            authored.ChecksumVerification,
            buildContext);
    }

    internal static bool TryExtractTargetBody(
        string memberSource,
        string metadataMethodName,
        out string body)
        => TryExtractTargetBodies(
            memberSource,
            metadataMethodName,
            expectedParameterCount: null,
            out body,
            out _);

    internal static bool TryExtractTargetBody(
        string memberSource,
        string metadataMethodName,
        int expectedParameterCount,
        out string body)
        => TryExtractTargetBodies(
            memberSource,
            metadataMethodName,
            (int?)expectedParameterCount,
            out body,
            out _);

    internal static bool TryExtractTargetBodies(
        string memberSource,
        string metadataMethodName,
        int expectedParameterCount,
        out string body,
        out string? printerBody)
        => TryExtractTargetBodies(
            memberSource,
            metadataMethodName,
            (int?)expectedParameterCount,
            out body,
            out printerBody);

    static bool TryExtractTargetBodies(
        string memberSource,
        string metadataMethodName,
        int? expectedParameterCount,
        out string body,
        out string? printerBody)
        => TryMatchTargetBodies(
            memberSource,
            metadataMethodName,
            expectedParameterCount,
            out body,
            out printerBody,
            out bool hasBody)
            && hasBody;

    internal static bool IsBodylessTarget(
        string memberSource,
        string metadataMethodName,
        int expectedParameterCount)
        => TryMatchTargetBodies(
            memberSource,
            metadataMethodName,
            expectedParameterCount,
            out string body,
            out _,
            out bool hasBody)
            && !hasBody;

    static bool TryMatchTargetBodies(
        string memberSource,
        string metadataMethodName,
        int? expectedParameterCount,
        out string body,
        out string? printerBody,
        out bool hasBody)
    {
        ArgumentNullException.ThrowIfNull(memberSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataMethodName);

        var root = CSharpSyntaxTree.ParseText(
            $"class __AuthoredSourceHost {{{Environment.NewLine}{memberSource}{Environment.NewLine}}}")
            .GetCompilationUnitRoot();
        var identity = MetadataMethodIdentity.Parse(metadataMethodName);
        int bestScore = -1;
        bool ambiguous = false;
        body = "";
        printerBody = null;
        hasBody = false;
        foreach (var member in root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(candidate => candidate.Parent is ClassDeclarationSyntax))
        {
            var candidate = MemberBody(member, identity, expectedParameterCount);
            if (candidate.Score < bestScore)
                continue;
            if (candidate.Score == bestScore)
            {
                if (candidate.Score >= 0)
                    ambiguous = true;
                continue;
            }

            bestScore = candidate.Score;
            body = candidate.Body.Legacy;
            printerBody = candidate.Body.Printer;
            hasBody = candidate.Body.HasBody;
            ambiguous = false;
        }

        return bestScore >= 0 && !ambiguous;
    }

    readonly record struct ExtractedBody(
        string Legacy,
        string? Printer,
        bool HasBody = true)
    {
        public static ExtractedBody None => new(
            "",
            Printer: null,
            HasBody: false);
    }

    static (int Score, ExtractedBody Body) MemberBody(
        MemberDeclarationSyntax member,
        MetadataMethodIdentity identity,
        int? expectedParameterCount)
        => member switch
        {
            MethodDeclarationSyntax method
                when string.Equals(
                    method.Identifier.ValueText,
                    identity.SimpleName,
                    StringComparison.Ordinal)
                    && ParameterCountMatches(
                        method.ParameterList.Parameters.Count,
                        expectedParameterCount)
                => ScoredBody(
                    method.ExplicitInterfaceSpecifier,
                    identity,
                    BodyText(method.Body, method.ExpressionBody, ReturnsVoid(method.ReturnType))),
            ConstructorDeclarationSyntax constructor
                when identity.SimpleName is ".ctor" or ".cctor"
                    && identity.ExplicitInterface is null
                    && ConstructorKindMatches(constructor, identity.SimpleName)
                    && ParameterCountMatches(
                        constructor.ParameterList.Parameters.Count,
                        expectedParameterCount)
                => (2, BodyText(
                    constructor.Body,
                    constructor.ExpressionBody,
                    returnsVoid: true)),
            PropertyDeclarationSyntax property
                when AccessorNameMatches(
                    identity,
                    "get_",
                    property.Identifier.ValueText)
                => ScoredBody(
                    property.ExplicitInterfaceSpecifier,
                    identity,
                    AccessorBodyText(property, SyntaxKind.GetAccessorDeclaration)),
            PropertyDeclarationSyntax property
                when AccessorNameMatches(
                    identity,
                    "set_",
                    property.Identifier.ValueText)
                => ScoredBody(
                    property.ExplicitInterfaceSpecifier,
                    identity,
                    AccessorBodyText(property, SyntaxKind.SetAccessorDeclaration)),
            IndexerDeclarationSyntax indexer
                when IndexerAccessorMatches(
                    identity,
                    "get_",
                    indexer,
                    expectedParameterCount)
                => ScoredBody(
                    indexer.ExplicitInterfaceSpecifier,
                    identity,
                    AccessorBodyText(indexer, SyntaxKind.GetAccessorDeclaration)),
            IndexerDeclarationSyntax indexer
                when IndexerAccessorMatches(
                    identity,
                    "set_",
                    indexer,
                    expectedParameterCount)
                => ScoredBody(
                    indexer.ExplicitInterfaceSpecifier,
                    identity,
                    AccessorBodyText(indexer, SyntaxKind.SetAccessorDeclaration)),
            EventDeclarationSyntax eventDeclaration
                when AccessorNameMatches(
                    identity,
                    "add_",
                    eventDeclaration.Identifier.ValueText)
                => ScoredBody(
                    eventDeclaration.ExplicitInterfaceSpecifier,
                    identity,
                    AccessorBodyText(eventDeclaration, SyntaxKind.AddAccessorDeclaration)),
            EventDeclarationSyntax eventDeclaration
                when AccessorNameMatches(
                    identity,
                    "remove_",
                    eventDeclaration.Identifier.ValueText)
                => ScoredBody(
                    eventDeclaration.ExplicitInterfaceSpecifier,
                    identity,
                    AccessorBodyText(eventDeclaration, SyntaxKind.RemoveAccessorDeclaration)),
            OperatorDeclarationSyntax op
                when string.Equals(
                    CSharpSourceIdentityContext.OperatorMetadataName(op),
                    identity.SimpleName,
                    StringComparison.Ordinal)
                    && ParameterCountMatches(
                        op.ParameterList.Parameters.Count,
                        expectedParameterCount)
                => (2, BodyText(
                    op.Body,
                    op.ExpressionBody,
                    ReturnsVoid(op.ReturnType))),
            ConversionOperatorDeclarationSyntax conversion
                when string.Equals(
                    CSharpSourceIdentityContext.ConversionOperatorMetadataName(
                        conversion),
                    identity.SimpleName,
                    StringComparison.Ordinal)
                    && ParameterCountMatches(
                        conversion.ParameterList.Parameters.Count,
                        expectedParameterCount)
                => (2, BodyText(
                    conversion.Body,
                    conversion.ExpressionBody,
                    returnsVoid: false)),
            _ => (-1, ExtractedBody.None),
        };

    static bool IndexerAccessorMatches(
        MetadataMethodIdentity identity,
        string prefix,
        IndexerDeclarationSyntax indexer,
        int? expectedParameterCount)
    {
        int parameterCount = indexer.ParameterList.Parameters.Count
            + (prefix == "set_" ? 1 : 0);
        if (!ParameterCountMatches(parameterCount, expectedParameterCount))
            return false;

        string? declaredName =
            CSharpSourceIdentityContext.IndexerMetadataName(indexer);
        if (declaredName is not null)
            return AccessorNameMatches(identity, prefix, declaredName);

        // The PDB body slicer can omit an IndexerName attribute outside its
        // vouched declaration range. The exact mapped MethodDef supplies the
        // accessor name; equal-rank neighboring indexers remain ambiguous.
        return identity.SimpleName.StartsWith(prefix, StringComparison.Ordinal)
            && identity.SimpleName.Length > prefix.Length;
    }

    static bool ParameterCountMatches(int actual, int? expected)
        => expected is null || actual == expected.Value;

    static bool ConstructorKindMatches(
        ConstructorDeclarationSyntax constructor,
        string metadataMethodName)
        => constructor.Modifiers.Any(SyntaxKind.StaticKeyword)
            ? metadataMethodName == ".cctor"
            : metadataMethodName == ".ctor";

    static (int Score, ExtractedBody Body) ScoredBody(
        ExplicitInterfaceSpecifierSyntax? syntax,
        MetadataMethodIdentity identity,
        ExtractedBody body)
        => (ExplicitInterfaceMatchScore(syntax, identity.ExplicitInterface), body);

    static bool AccessorNameMatches(
        MetadataMethodIdentity identity,
        string prefix,
        string memberName)
        => identity.SimpleName.StartsWith(prefix, StringComparison.Ordinal)
           && string.Equals(
               identity.SimpleName[prefix.Length..],
               memberName,
               StringComparison.Ordinal);

    static int ExplicitInterfaceMatchScore(
        ExplicitInterfaceSpecifierSyntax? syntax,
        string? metadataInterface)
    {
        if (syntax is null || metadataInterface is null)
            return syntax is null && metadataInterface is null ? 4 : -1;

        string sourceInterface = MetadataInterfaceName(syntax.Name);
        if (string.Equals(
                   metadataInterface,
                   sourceInterface,
                   StringComparison.Ordinal))
        {
            return 4;
        }
        if (metadataInterface.EndsWith(
            $".{sourceInterface}",
            StringComparison.Ordinal))
        {
            return 3;
        }

        string erasedSource = EraseGenericArguments(sourceInterface);
        string erasedMetadata = EraseGenericArguments(metadataInterface);
        if (string.Equals(erasedMetadata, erasedSource, StringComparison.Ordinal))
            return 2;

        return erasedMetadata.EndsWith(
            $".{erasedSource}",
            StringComparison.Ordinal)
            ? 1
            : -1;
    }

    static string MetadataInterfaceName(NameSyntax name)
        => name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic =>
                $"{generic.Identifier.ValueText}<"
                + $"{string.Join(",", generic.TypeArgumentList.Arguments.Select(MetadataTypeName))}>",
            QualifiedNameSyntax qualified =>
                $"{MetadataInterfaceName(qualified.Left)}.{MetadataInterfaceName(qualified.Right)}",
            AliasQualifiedNameSyntax alias
                when alias.Alias.Identifier.ValueText == "global"
                => MetadataInterfaceName(alias.Name),
            AliasQualifiedNameSyntax alias => MetadataInterfaceName(alias.Name),
            _ => name.ToString(),
        };

    static string MetadataTypeName(TypeSyntax type)
        => type switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.Kind() switch
            {
                SyntaxKind.BoolKeyword => "System.Boolean",
                SyntaxKind.ByteKeyword => "System.Byte",
                SyntaxKind.SByteKeyword => "System.SByte",
                SyntaxKind.ShortKeyword => "System.Int16",
                SyntaxKind.UShortKeyword => "System.UInt16",
                SyntaxKind.IntKeyword => "System.Int32",
                SyntaxKind.UIntKeyword => "System.UInt32",
                SyntaxKind.LongKeyword => "System.Int64",
                SyntaxKind.ULongKeyword => "System.UInt64",
                SyntaxKind.CharKeyword => "System.Char",
                SyntaxKind.FloatKeyword => "System.Single",
                SyntaxKind.DoubleKeyword => "System.Double",
                SyntaxKind.DecimalKeyword => "System.Decimal",
                SyntaxKind.StringKeyword => "System.String",
                SyntaxKind.ObjectKeyword => "System.Object",
                _ => predefined.Keyword.ValueText,
            },
            NullableTypeSyntax nullable =>
                $"System.Nullable<{MetadataTypeName(nullable.ElementType)}>",
            ArrayTypeSyntax array =>
                MetadataTypeName(array.ElementType)
                + string.Concat(array.RankSpecifiers.Select(rank =>
                    rank.Rank == 1 ? "[]" : $"[{new string(',', rank.Rank - 1)}]")),
            NameSyntax name => MetadataInterfaceName(name),
            _ => type.WithoutTrivia().ToString(),
        };

    static string EraseGenericArguments(string name)
    {
        StringBuilder normalized = new(name.Length);
        int genericDepth = 0;
        foreach (char value in name)
        {
            switch (value)
            {
                case '<':
                    genericDepth++;
                    break;
                case '>':
                    genericDepth--;
                    break;
                default:
                    if (genericDepth == 0)
                        normalized.Append(value);
                    break;
            }
        }

        return normalized.ToString();
    }

    readonly record struct MetadataMethodIdentity(
        string SimpleName,
        string? ExplicitInterface)
    {
        public static MetadataMethodIdentity Parse(string metadataMethodName)
        {
            if (metadataMethodName is ".ctor" or ".cctor")
                return new(metadataMethodName, ExplicitInterface: null);

            int separator = metadataMethodName.LastIndexOf('.');
            return separator >= 0
                ? new(
                    metadataMethodName[(separator + 1)..],
                    metadataMethodName[..separator])
                : new(metadataMethodName, ExplicitInterface: null);
        }
    }

    static ExtractedBody AccessorBodyText(
        BasePropertyDeclarationSyntax declaration,
        SyntaxKind accessorKind)
    {
        var accessor = declaration.AccessorList?.Accessors
            .FirstOrDefault(candidate => candidate.IsKind(accessorKind));
        if (accessor is not null)
        {
            return BodyText(
                accessor.Body,
                accessor.ExpressionBody,
                returnsVoid: accessorKind is SyntaxKind.SetAccessorDeclaration
                    or SyntaxKind.AddAccessorDeclaration
                    or SyntaxKind.RemoveAccessorDeclaration);
        }

        ArrowExpressionClauseSyntax? expressionBody = declaration switch
        {
            PropertyDeclarationSyntax property => property.ExpressionBody,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody,
            _ => null,
        };
        if (accessorKind == SyntaxKind.GetAccessorDeclaration
            && expressionBody is not null)
        {
            return new($"return {expressionBody.Expression};", Printer: null);
        }

        return ExtractedBody.None;
    }

    static ExtractedBody BodyText(
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? expressionBody,
        bool returnsVoid)
    {
        if (block is not null)
        {
            string legacy = string.Join(
                    Environment.NewLine,
                    block.Statements.Select(statement => statement.ToFullString()))
                .Trim();
            return new(
                legacy,
                PrinterBodyIsMechanicallyComparable(block)
                    ? PrinterBodyText(block)
                    : null);
        }
        if (expressionBody is null)
            return ExtractedBody.None;

        string projected = returnsVoid
            ? $"{expressionBody.Expression};"
            : $"return {expressionBody.Expression};";
        // Expression-bodied declarations have a different source envelope than
        // the block body emitted by the decompiler. They remain valid Correct
        // evidence, but are deliberately ineligible for byte-for-byte printer
        // comparison until that projection has its own versioned contract.
        return new(projected, Printer: null);
    }

    static bool PrinterBodyIsMechanicallyComparable(BlockSyntax block)
    {
        var lines = block.SyntaxTree.GetText().Lines;
        return !block.DescendantTokens().Any(token =>
                lines.GetLinePositionSpan(token.Span) is var span
                && span.Start.Line != span.End.Line)
            && !block.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                trivia.GetStructure() is DirectiveTriviaSyntax);
    }

    static string PrinterBodyText(BlockSyntax block)
    {
        string text = block.SyntaxTree.GetText()
            .ToString(Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(
                block.OpenBraceToken.Span.End,
                block.CloseBraceToken.SpanStart))
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = text.Split('\n');
        int start = 0;
        int end = lines.Length;
        while (start < end && string.IsNullOrWhiteSpace(lines[start]))
            start++;
        while (end > start && string.IsNullOrWhiteSpace(lines[end - 1]))
            end--;
        if (start == end)
            return "";

        bool closeBraceSharesContentLine =
            end == lines.Length && !string.IsNullOrWhiteSpace(lines[end - 1]);
        if (closeBraceSharesContentLine)
            lines[end - 1] = lines[end - 1].TrimEnd(' ', '\t');

        int indentation = int.MaxValue;
        for (int i = start; i < end; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            int current = 0;
            while (current < lines[i].Length
                && lines[i][current] is ' ' or '\t')
            {
                current++;
            }
            indentation = Math.Min(indentation, current);
        }

        if (indentation is int.MaxValue)
            indentation = 0;

        return string.Join(
            "\n",
            lines[start..end].Select(line =>
                string.IsNullOrWhiteSpace(line)
                    ? ""
                    : line[Math.Min(indentation, line.Length)..]));
    }

    static bool ReturnsVoid(TypeSyntax type)
        => type is PredefinedTypeSyntax predefined
           && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);

    internal static AuthoredRebuildFidelityResult CompileAuthoredBody(
        ReturnToSender.Result decompilerResult,
        string authoredBody,
        SourceChecksumVerification? checksumVerification,
        AuthoredBuildContextAssessment buildContext)
    {
        ArgumentNullException.ThrowIfNull(decompilerResult);
        ArgumentNullException.ThrowIfNull(authoredBody);
        ArgumentNullException.ThrowIfNull(buildContext);
        if (decompilerResult.FinalRequest is not { } request)
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.ContextFailed,
                checksumVerification,
                buildContext,
                "RTS did not produce a final artifact request.",
                ImplementationDiff: null);
        }

        try
        {
            using var originalPe = new PEReader(File.OpenRead(request.AssemblyPath));
            var originalReader = originalPe.GetMetadataReader();
            request = ReturnToSender.WithReader(request, originalReader);
            var authoredRequest = ReturnToSender.WithTargetBody(
                request,
                new ProductTargetBody(authoredBody, []));
            var artifact = CompileBackSourceComposer.Compose(authoredRequest);
            var parseOptions = ParseOptions(buildContext.RecordedOptions);
            var compileOptions = CompilationOptions(buildContext.RecordedOptions);
            if (request.CompilationClosure is not { } compilationClosure)
            {
                return new AuthoredRebuildFidelityResult(
                    decompilerResult,
                    AuthoredRebuildOutcome.ContextFailed,
                    checksumVerification,
                    buildContext,
                    "RTS did not retain its frozen compilation closure.",
                    ImplementationDiff: null);
            }
            MetadataReference[] references =
                compilationClosure.References;
            var compilation = CSharpCompilation.Create(
                "return-to-sender",
                [CSharpSyntaxTree.ParseText(artifact.Source, parseOptions)],
                references,
                compileOptions);
            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                var error = emit.Diagnostics.FirstOrDefault(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error);
                return new AuthoredRebuildFidelityResult(
                    decompilerResult,
                    AuthoredRebuildOutcome.RecompileFailed,
                    checksumVerification,
                    buildContext,
                    error is null
                        ? "The authored body did not compile in the RTS shell."
                        : $"{error.Id}: {error.GetMessage()}",
                    ImplementationDiff: null);
            }

            var originalMethod = MetadataTokens.MethodDefinitionHandle(
                MetadataTokens.GetRowNumber(request.TargetMethod));
            var implementationDiff = ReturnToSender.BuildImplementationDiff(
                request.AssemblyPath,
                originalReader,
                originalMethod,
                stream.ToArray(),
                request.FullType,
                request.MethodName,
                overload: 0,
                ImplementationDiffMechanism.IlBody);
            if (implementationDiff is null)
            {
                return new AuthoredRebuildFidelityResult(
                    decompilerResult,
                    AuthoredRebuildOutcome.ContextFailed,
                    checksumVerification,
                    buildContext,
                    "The authored rebuild target could not be compared to shipped IL.",
                    ImplementationDiff: null);
            }

            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                implementationDiff.IsExact
                    ? AuthoredRebuildOutcome.Exact
                    : AuthoredRebuildOutcome.IlDifferent,
                checksumVerification,
                buildContext,
                Detail: null,
                implementationDiff);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or IOException)
        {
            return new AuthoredRebuildFidelityResult(
                decompilerResult,
                AuthoredRebuildOutcome.ContextFailed,
                checksumVerification,
                buildContext,
                $"{ex.GetType().Name}: {ex.Message}",
                ImplementationDiff: null);
        }
    }

    internal static AuthoredBuildContextAssessment AssessBuildContext(
        bool isDeterministic,
        FindingInspection<CompilationOptionInfo> options,
        FindingInspection<CompilationReferenceInfo> references,
        IEnumerable<MetadataReference> actualReferences)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(actualReferences);

        if (options.Value is FindingInspection<CompilationOptionInfo>.Failed optionFailure)
        {
            return new AuthoredBuildContextAssessment(
                AuthoredBuildContextStatus.Failed,
                isDeterministic,
                $"Compilation options: {optionFailure.Error.Reason}");
        }
        if (references.Value is FindingInspection<CompilationReferenceInfo>.Failed referenceFailure)
        {
            return new AuthoredBuildContextAssessment(
                AuthoredBuildContextStatus.Failed,
                isDeterministic,
                $"Compilation references: {referenceFailure.Error.Reason}");
        }
        if (options.Value is not FindingInspection<CompilationOptionInfo>.Complete optionComplete
            || references.Value is not FindingInspection<CompilationReferenceInfo>.Complete referenceComplete
            || optionComplete.Findings.IsEmpty
            || referenceComplete.Findings.IsEmpty)
        {
            return new AuthoredBuildContextAssessment(
                AuthoredBuildContextStatus.Incomplete,
                isDeterministic,
                "Portable-PDB compilation options or references are incomplete.");
        }

        var drift = new List<string>();
        var optionValues = optionComplete.Findings
            .Select(finding => finding.Payload)
            .ToDictionary(option => option.Name, option => option.Value, StringComparer.OrdinalIgnoreCase);
        CheckOption(optionValues, "optimization", "release", drift);
        CheckOption(optionValues, "unsafe", "true", drift, missingIsDrift: false);
        CheckOption(optionValues, "language-version", "preview", drift, missingIsDrift: false);
        CheckOption(
            optionValues,
            "compiler-version",
            typeof(CSharpCompilation).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
                ?? typeof(CSharpCompilation).Assembly.GetName().Version?.ToString()
                ?? "unknown",
            drift,
            missingIsDrift: false);

        var actualNames = actualReferences
            .Select(reference => Path.GetFileName(reference.Display))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingReferences = referenceComplete.Findings
            .Select(finding => finding.Payload.Name)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !actualNames.Contains(name!))
            .Take(3)
            .ToArray();
        if (missingReferences.Length > 0)
            drift.Add($"references missing from RTS context: {string.Join(", ", missingReferences)}");

        return drift.Count == 0
            ? new AuthoredBuildContextAssessment(
                AuthoredBuildContextStatus.Recorded,
                isDeterministic,
                "Portable-PDB options and reference names agree with the RTS context.",
                optionValues)
            : new AuthoredBuildContextAssessment(
                AuthoredBuildContextStatus.Drift,
                isDeterministic,
                string.Join("; ", drift),
                optionValues);
    }

    static CSharpParseOptions ParseOptions(
        IReadOnlyDictionary<string, string>? options)
    {
        var languageVersion = LanguageVersion.Preview;
        if (options?.TryGetValue("language-version", out string? language) == true
            && LanguageVersionFacts.TryParse(language, out var parsedLanguage))
        {
            languageVersion = parsedLanguage;
        }

        var symbols = options?.TryGetValue("define", out string? define) == true
            ? define.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        return new CSharpParseOptions(
            languageVersion,
            preprocessorSymbols: symbols);
    }

    static CSharpCompilationOptions CompilationOptions(
        IReadOnlyDictionary<string, string>? options)
    {
        bool release = options?.TryGetValue("optimization", out string? optimization) != true
            || string.Equals(optimization, "release", StringComparison.OrdinalIgnoreCase);
        bool allowUnsafe = options?.TryGetValue("unsafe", out string? unsafeValue) != true
            || bool.TryParse(unsafeValue, out bool parsedUnsafe) && parsedUnsafe;
        bool checkOverflow = options?.TryGetValue("checked", out string? checkedValue) == true
            && bool.TryParse(checkedValue, out bool parsedChecked) && parsedChecked;
        return new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: release ? OptimizationLevel.Release : OptimizationLevel.Debug,
            nullableContextOptions: NullableContextOptions.Disable,
            allowUnsafe: allowUnsafe,
            checkOverflow: checkOverflow);
    }

    static void CheckOption(
        IReadOnlyDictionary<string, string> options,
        string name,
        string expected,
        ICollection<string> drift,
        bool missingIsDrift = true)
    {
        if (!options.TryGetValue(name, out string? actual))
        {
            if (missingIsDrift)
                drift.Add($"{name} is not recorded");
            return;
        }

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            drift.Add($"{name}={actual} (RTS uses {expected})");
    }

    internal static async Task AcquirePdbAsync(
        SourceLinkService source,
        HttpClient httpClient,
        string? packageName = null,
        string? packageVersion = null,
        IPdbStore? pdbStore = null)
    {
        if (source.Context.HasPdb
            && source.Context.PdbId is null
            && string.Equals(
                source.Context.PdbLocation,
                "Standalone",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The standalone Portable PDB identity cannot be verified "
                + "because the assembly has no Portable CodeView entry.");
        }

        if (!source.Context.NeedsPdb || source.Context.PdbId is not { } pdb)
            return;

        using var failureScope =
            FeedFailureTelemetry.Scope(mergeIntoParent: false);
        FeedFailureCollector failures = FeedFailureTelemetry.Current!;
        var downloader = pdbStore is null
            ? new SymbolPackageDownloader(httpClient)
            : new SymbolPackageDownloader(httpClient, pdbStore);
        var result = await downloader.DownloadPdbAsync(
            pdb.Guid,
            pdb.Age,
            pdb.PdbFileName,
            pdb.IsPortable,
            source.Context.AssemblyPath,
            packageName,
            packageVersion,
            portablePdbStamp: pdb.Stamp);
        if (result.PdbFilePath is not null)
        {
            source.LoadPdb(result.PdbFilePath, "Symbol Package", result.SymbolServer);
            return;
        }

        if (result.StoreFailure is { } storeFailure)
        {
            throw new IOException(
                storeFailure switch
                {
                    PortablePdbStoreFailureKind.ReadFailed =>
                        "The PDB store could not read cached Portable PDB content.",
                    PortablePdbStoreFailureKind.InvalidCachedContent =>
                        "The PDB store returned malformed or mismatched cached content.",
                    PortablePdbStoreFailureKind.PublicationNotRetained =>
                        "The PDB store did not retain verified Portable PDB content.",
                    _ => "The PDB store could not provide verified Portable PDB content.",
                });
        }

        if (failures.HasFailures)
        {
            throw new HttpRequestException(
                "Portable PDB sources did not answer: "
                + string.Join(
                    "; ",
                    failures.Failures.Select(static failure =>
                        failure.Status == HttpStatusCode.OK
                            ? "a source returned invalid or mismatched Portable PDB content"
                            : $"{failure.StatusText} while {failure.PhaseText}")));
        }
    }

    internal static bool IsPdbAcquisitionFailure(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or BadImageFormatException
            or InvalidDataException
            or InvalidOperationException
            or HttpRequestException
            or TaskCanceledException;

    static void WriteReport(
        IReadOnlyList<AuthoredRebuildFidelityResult> results,
        int maxExamples)
    {
        Console.WriteLine($"AUTHORED-SOURCE REBUILD FIDELITY over {results.Count} target(s)");
        Console.WriteLine();
        foreach (AuthoredRebuildOutcome outcome in Enum.GetValues<AuthoredRebuildOutcome>())
            Console.WriteLine($"  {outcome,-16}: {results.Count(result => result.Outcome == outcome)}");
        Console.WriteLine();
        Console.WriteLine("Build context:");
        foreach (AuthoredBuildContextStatus status in Enum.GetValues<AuthoredBuildContextStatus>())
            Console.WriteLine($"  {status,-16}: {results.Count(result => result.BuildContext.Status == status)}");
        Console.WriteLine($"  {"Deterministic",-16}: {results.Count(result => result.BuildContext.IsDeterministic)}");

        var examples = results
            .Where(result => result.Outcome != AuthoredRebuildOutcome.Exact
                || result.BuildContext.Status != AuthoredBuildContextStatus.Recorded
                || !result.BuildContext.IsDeterministic)
            .Take(maxExamples)
            .ToArray();
        if (examples.Length == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Examples:");
        foreach (var result in examples)
        {
            var target = result.DecompilerLane.Plan.TargetMethod;
            Console.WriteLine($"  {target.Type}::{target.Method}");
            Console.WriteLine($"    decompiled : {result.DecompilerLane.Status}");
            Console.WriteLine($"    authored   : {result.Outcome}");
            Console.WriteLine($"    checksum   : {result.ChecksumVerification?.ToString() ?? "unavailable"}");
            Console.WriteLine($"    deterministic: {result.BuildContext.IsDeterministic}");
            Console.WriteLine($"    context    : {result.BuildContext.Status} — {result.BuildContext.Detail}");
            if (!string.IsNullOrWhiteSpace(result.Detail))
                Console.WriteLine($"    detail     : {result.Detail}");
        }
    }
}
