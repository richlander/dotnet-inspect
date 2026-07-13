using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;

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
        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
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

            using var source = SourceLinkService.Open(assemblyPath);
            await AcquirePdbAsync(source, httpClient);
            var buildContext = AssessBuildContext(
                AssemblyInspector.InspectDll(assemblyPath).IsDeterministic,
                MetadataFindings.InspectCompilationOptions(
                    source,
                    new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath))),
                MetadataFindings.InspectCompilationReferences(
                    source,
                    new FindingSubject(assemblyPath, Path.GetFileName(assemblyPath))),
                ReturnToSender.CompilationReferences(assemblyPath));

            foreach (var decompilerResult in decompilerResults)
            {
                if (results.Count >= cap)
                    break;

                results.Add(await EvaluateAsync(
                    source,
                    fetcher,
                    decompilerResult,
                    buildContext));
            }
        }

        WriteReport(results, maxExamples);
        return results.Any(result =>
            result.Outcome is AuthoredRebuildOutcome.RecompileFailed
                or AuthoredRebuildOutcome.ContextFailed
                or AuthoredRebuildOutcome.SourceFailed)
            ? 1
            : 0;
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
        var authored = await AuthoredSourceAcquisition.AcquireMemberAsync(
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

        if (!TryExtractTargetBody(authoredBody, request.MethodName, out string targetBody))
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
    {
        ArgumentNullException.ThrowIfNull(memberSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataMethodName);

        var root = CSharpSyntaxTree.ParseText(
            $"class __AuthoredSourceHost {{{Environment.NewLine}{memberSource}{Environment.NewLine}}}")
            .GetCompilationUnitRoot();
        var identity = MetadataMethodIdentity.Parse(metadataMethodName);
        foreach (var member in root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(candidate => candidate.Parent is ClassDeclarationSyntax))
        {
            body = MemberBodyText(member, identity);
            if (body.Length > 0)
                return true;
        }

        body = "";
        return false;
    }

    static string MemberBodyText(
        MemberDeclarationSyntax member,
        MetadataMethodIdentity identity)
        => member switch
        {
            MethodDeclarationSyntax method
                when string.Equals(
                    method.Identifier.ValueText,
                    identity.SimpleName,
                    StringComparison.Ordinal)
                    && ExplicitInterfaceMatches(
                        method.ExplicitInterfaceSpecifier,
                        identity.ExplicitInterface)
                => BodyText(method.Body, method.ExpressionBody, ReturnsVoid(method.ReturnType)),
            ConstructorDeclarationSyntax constructor
                when identity.SimpleName is ".ctor" or ".cctor"
                    && identity.ExplicitInterface is null
                => BodyText(constructor.Body, constructor.ExpressionBody, returnsVoid: true),
            PropertyDeclarationSyntax property
                when AccessorMatches(
                    identity,
                    "get_",
                    property.Identifier.ValueText,
                    property.ExplicitInterfaceSpecifier)
                => AccessorBodyText(property, SyntaxKind.GetAccessorDeclaration),
            PropertyDeclarationSyntax property
                when AccessorMatches(
                    identity,
                    "set_",
                    property.Identifier.ValueText,
                    property.ExplicitInterfaceSpecifier)
                => AccessorBodyText(property, SyntaxKind.SetAccessorDeclaration),
            EventDeclarationSyntax eventDeclaration
                when AccessorMatches(
                    identity,
                    "add_",
                    eventDeclaration.Identifier.ValueText,
                    eventDeclaration.ExplicitInterfaceSpecifier)
                => AccessorBodyText(eventDeclaration, SyntaxKind.AddAccessorDeclaration),
            EventDeclarationSyntax eventDeclaration
                when AccessorMatches(
                    identity,
                    "remove_",
                    eventDeclaration.Identifier.ValueText,
                    eventDeclaration.ExplicitInterfaceSpecifier)
                => AccessorBodyText(eventDeclaration, SyntaxKind.RemoveAccessorDeclaration),
            _ => "",
        };

    static bool AccessorMatches(
        MetadataMethodIdentity identity,
        string prefix,
        string memberName,
        ExplicitInterfaceSpecifierSyntax? explicitInterface)
        => identity.SimpleName.StartsWith(prefix, StringComparison.Ordinal)
           && string.Equals(
               identity.SimpleName[prefix.Length..],
               memberName,
               StringComparison.Ordinal)
           && ExplicitInterfaceMatches(
               explicitInterface,
               identity.ExplicitInterface);

    static bool ExplicitInterfaceMatches(
        ExplicitInterfaceSpecifierSyntax? syntax,
        string? metadataInterface)
    {
        if (syntax is null || metadataInterface is null)
            return syntax is null && metadataInterface is null;

        string sourceInterface = MetadataInterfaceName(syntax.Name);
        return string.Equals(
                   metadataInterface,
                   sourceInterface,
                   StringComparison.Ordinal)
               || metadataInterface.EndsWith(
                   $".{sourceInterface}",
                   StringComparison.Ordinal);
    }

    static string MetadataInterfaceName(NameSyntax name)
        => name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified =>
                $"{MetadataInterfaceName(qualified.Left)}.{MetadataInterfaceName(qualified.Right)}",
            AliasQualifiedNameSyntax alias
                when alias.Alias.Identifier.ValueText == "global"
                => MetadataInterfaceName(alias.Name),
            AliasQualifiedNameSyntax alias =>
                $"{alias.Alias.Identifier.ValueText}.{MetadataInterfaceName(alias.Name)}",
            _ => name.ToString(),
        };

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

    static string AccessorBodyText(
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

        if (accessorKind == SyntaxKind.GetAccessorDeclaration
            && declaration is PropertyDeclarationSyntax
                { ExpressionBody: { } expressionBody })
        {
            return $"return {expressionBody.Expression};";
        }

        return "";
    }

    static string BodyText(
        BlockSyntax? block,
        ArrowExpressionClauseSyntax? expressionBody,
        bool returnsVoid)
    {
        if (block is not null)
        {
            return string.Join(
                Environment.NewLine,
                block.Statements.Select(statement => statement.ToFullString()))
                .Trim();
        }
        if (expressionBody is null)
            return "";

        return returnsVoid
            ? $"{expressionBody.Expression};"
            : $"return {expressionBody.Expression};";
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
            var references = ReturnToSender.CompilationReferences(
                request.AssemblyPath).ToArray();
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

    static async Task AcquirePdbAsync(
        SourceLinkService source,
        HttpClient httpClient)
    {
        if (!source.Context.NeedsPdb || source.Context.PdbId is not { } pdb)
            return;

        var result = await new SymbolPackageDownloader(httpClient).DownloadPdbAsync(
            pdb.Guid,
            pdb.Age,
            pdb.PdbFileName,
            pdb.IsPortable,
            source.Context.AssemblyPath);
        if (result.PdbFilePath is not null)
            source.LoadPdb(result.PdbFilePath, "Symbol Package", result.SymbolServer);
    }

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
