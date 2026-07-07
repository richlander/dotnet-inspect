using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

using DotnetInspector.Fixtures;
using ILInspector.Decompiler;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

enum ReturnToSenderSourceOutcome
{
    ValidMatch,
    ValidDifferent,
    Invalid,
    SourceUnavailable,
    UnsupportedTarget,
}

sealed record ReturnToSenderSourceProbeResult(
    ReturnToSender.RequestedTarget Target,
    ReturnToSenderSourceOutcome Outcome,
    FidelityCheck.CompileBackStatus? CompileBackStatus,
    string Reason,
    string? Detail,
    string? SourcePath,
    string? ExpectedBody,
    string? ActualBody)
{
    public bool Passed => Outcome == ReturnToSenderSourceOutcome.ValidMatch;
    public bool Different => Outcome == ReturnToSenderSourceOutcome.ValidDifferent;
    public bool Failed => Outcome == ReturnToSenderSourceOutcome.Invalid;
    public bool Skipped => Outcome is ReturnToSenderSourceOutcome.SourceUnavailable or ReturnToSenderSourceOutcome.UnsupportedTarget;
}

static class ReturnToSenderSourceProbe
{
    sealed record ProbeTarget(ReturnToSender.RequestedTarget Target, IReadOnlyList<string> ExpectedFragments);

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        var results = Evaluate(assemblies, cap);
        int passed = results.Count(result => result.Passed);
        int different = results.Count(result => result.Different);
        int failed = results.Count(result => result.Failed);
        int skipped = results.Count(result => result.Skipped);
        var buckets = results
            .GroupBy(result => result.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine($"RETURNTOSENDER SOURCE PROBE over {results.Count} target(s)");
        Console.WriteLine();
        Console.WriteLine($"  ValidMatch       : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch)}");
        Console.WriteLine($"  ValidDifferent   : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent)}");
        Console.WriteLine($"  Invalid          : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid)}");
        Console.WriteLine($"  SourceUnavailable: {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable)}");
        Console.WriteLine($"  UnsupportedTarget: {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget)}");
        Console.WriteLine();
        Console.WriteLine($"  Passed   : {passed}");
        Console.WriteLine($"  Different: {different}");
        Console.WriteLine($"  Failed   : {failed}");
        Console.WriteLine($"  Skipped  : {skipped}");
        if (buckets.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Buckets:");
            foreach (var bucket in buckets)
                Console.WriteLine($"  {bucket.Count()}: {bucket.Key}");
        }
        var examples = results
            .Where(result => result.Outcome != ReturnToSenderSourceOutcome.ValidMatch)
            .Take(maxExamples)
            .ToArray();
        if (examples.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Examples (first {examples.Length}):");
            foreach (var example in examples)
            {
                Console.WriteLine($"  {example.Target.Type}::{example.Target.Method}#{example.Target.Overload}  outcome={OutcomeId(example.Outcome)}  rts={example.CompileBackStatus?.ToString() ?? "missing"}  bucket={example.Reason}");
                if (!string.IsNullOrWhiteSpace(example.Detail))
                    Console.WriteLine($"      detail: {example.Detail}");
                if (!string.IsNullOrWhiteSpace(example.SourcePath))
                    Console.WriteLine($"      source: {example.SourcePath}");
            }
        }

        return failed == 0 ? 0 : 1;
    }

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> Evaluate(IReadOnlyList<string> assemblies, int cap)
    {
        var results = new List<ReturnToSenderSourceProbeResult>();
        foreach (var assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            var targets = DiscoverTargets(assemblyPath, cap - results.Count);
            results.AddRange(EvaluateTargets(assemblyPath, targets.Select(target => target.Target).ToArray()));
        }

        return results.Count > cap ? results.Take(cap).ToArray() : results;
    }

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateTargets(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets)
    {
        if (targets.Count == 0)
            return [];

        var sourceIndex = FixtureSourceIndex.TryCreate(assemblyPath);
        return EvaluateTargets(assemblyPath, targets, sourceIndex);
    }

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateTargets(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets,
        IReadOnlyList<string> sourcePaths)
        => EvaluateTargets(
            assemblyPath,
            targets,
            FixtureSourceIndex.TryCreate(sourcePaths),
            "source index could not be built from the supplied source paths");

    static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateTargets(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets,
        FixtureSourceIndex? sourceIndex,
        string sourceUnavailableDetail = "assembly is not registered in FixtureCatalog")
    {
        if (targets.Count == 0)
            return [];

        var rtsResults = ReturnToSender.CompileBackTargets(assemblyPath, targets.Distinct().ToArray())
            .ToDictionary(
                result => Key(
                    result.Plan.TargetMethod.Type,
                    result.Plan.TargetMethod.Method,
                    result.Plan.TargetMethod.Overload),
                StringComparer.Ordinal);

        var results = new List<ReturnToSenderSourceProbeResult>();
        foreach (var target in targets)
        {
            if (!rtsResults.TryGetValue(Key(target.Type, target.Method, target.Overload), out var result))
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.UnsupportedTarget,
                    CompileBackStatus: null,
                    "unsupported-rts-target",
                    Detail: null,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: null));
                continue;
            }

            SourceMember? sourceMember = null;
            bool sourceFound = sourceIndex?.TryFind(target, out sourceMember) == true;
            if (result.Status is FidelityCheck.CompileBackStatus.RecompileFail
                or FidelityCheck.CompileBackStatus.ContextFail)
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.Invalid,
                    result.Status,
                    FailureReason(result),
                    result.Detail,
                    SourcePath: sourceMember?.SourcePath,
                    ExpectedBody: sourceMember?.Body,
                    ActualBody: result.TargetBody));
                continue;
            }

            if (result.Status is not (FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff))
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    FailureReason(result),
                    result.Detail,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody));
                continue;
            }

            if (sourceIndex is null)
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    "fixture-source-unavailable",
                    sourceUnavailableDetail,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody));
                continue;
            }

            if (!sourceFound || sourceMember is null)
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    "source-slice-unavailable",
                    $"no source member matched {target.Type}::{target.Method}#{target.Overload}",
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody));
                continue;
            }

            string expected = sourceMember.Body;
            string actual = result.TargetBody;
            if (NormalizeBody(expected) == NormalizeBody(actual))
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.ValidMatch,
                    result.Status,
                    "valid_match",
                    Detail: null,
                    sourceMember.SourcePath,
                    expected,
                    actual));
                continue;
            }

            var decisions = result.Decisions ?? [];
            string reason = TryKnownTasteDifference(expected, actual, decisions, out var tasteDetail)
                ? "valid_different.known_taste"
                : "valid_different.unclassified";
            results.Add(new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.ValidDifferent,
                result.Status,
                reason,
                Detail: tasteDetail ?? "decompiled body is Roslyn-valid but differs from the fixture source slice",
                sourceMember.SourcePath,
                expected,
                actual));
        }

        return results;
    }

    sealed record SourceMember(string Type, string Method, int Overload, string SourcePath, string Body);

    sealed class FixtureSourceIndex
    {
        readonly Dictionary<string, SourceMember> _members;

        FixtureSourceIndex(Dictionary<string, SourceMember> members)
        {
            _members = members;
        }

        public static FixtureSourceIndex? TryCreate(IReadOnlyList<string> sourcePaths)
        {
            var members = new Dictionary<string, SourceMember>(StringComparer.Ordinal);
            var overloads = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var sourceFiles = new List<(string Path, CompilationUnitSyntax Root)>();
            foreach (var sourcePath in sourcePaths)
            {
                if (!TryReadSourceFile(sourcePath, out var root))
                    return null;
                sourceFiles.Add((sourcePath, root));
            }

            var sourceIdentity = CSharpSourceIdentityContext.Create(sourceFiles.Select(file => file.Root));
            foreach (var sourceFile in sourceFiles)
                AddSourceFile(members, overloads, sourceFile.Path, sourceFile.Root, sourceIdentity);

            return new FixtureSourceIndex(members);
        }

        public static FixtureSourceIndex? TryCreate(string assemblyPath)
        {
            foreach (var fixture in FixtureCatalog.All)
            {
                if (!TryGetFixtureAssemblyPath(fixture, out var fixtureAssemblyPath))
                    continue;

                if (!string.Equals(
                    Path.GetFullPath(fixtureAssemblyPath),
                    Path.GetFullPath(assemblyPath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetSourcePaths(fixture, out var sourcePaths))
                    return null;

                return TryCreate(sourcePaths);
            }

            return null;
        }

        static bool TryGetFixtureAssemblyPath(FixtureDefinition fixture, out string path)
        {
            try
            {
                path = fixture.AssemblyPath();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                path = "";
                return false;
            }
        }

        static bool TryGetSourcePaths(FixtureDefinition fixture, out IReadOnlyList<string> paths)
        {
            try
            {
                paths = fixture.SourcePaths();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                paths = [];
                return false;
            }
        }

        public bool TryFind(ReturnToSender.RequestedTarget target, out SourceMember member)
            => _members.TryGetValue(Key(target.Type, target.Method, target.Overload), out member!);

        static bool TryReadSourceFile(string sourcePath, out CompilationUnitSyntax root)
        {
            string source;
            try
            {
                source = File.ReadAllText(sourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                root = null!;
                return false;
            }

            var tree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
            root = tree.GetCompilationUnitRoot();
            return true;
        }

        static void AddSourceFile(
            Dictionary<string, SourceMember> members,
            Dictionary<string, Dictionary<string, int>> overloads,
            string sourcePath,
            CompilationUnitSyntax root,
            CSharpSourceIdentityContext sourceIdentity)
        {
            foreach (var member in SourceMembers(root, sourcePath, overloads, sourceIdentity))
                members.TryAdd(Key(member.Type, member.Method, member.Overload), member);
        }

        static IEnumerable<SourceMember> SourceMembers(
            CompilationUnitSyntax root,
            string sourcePath,
            Dictionary<string, Dictionary<string, int>> overloads,
            CSharpSourceIdentityContext sourceIdentity)
        {
            foreach (var member in SourceMembers(root.Members, namespaceName: "", containingTypes: [], sourcePath, overloads, sourceIdentity))
                yield return member;
        }

        static IEnumerable<SourceMember> SourceMembers(
            SyntaxList<MemberDeclarationSyntax> declarations,
            string namespaceName,
            IReadOnlyList<string> containingTypes,
            string sourcePath,
            Dictionary<string, Dictionary<string, int>> overloads,
            CSharpSourceIdentityContext sourceIdentity)
        {
            foreach (var declaration in declarations)
            {
                switch (declaration)
                {
                    case BaseNamespaceDeclarationSyntax ns:
                    {
                        string nextNamespace = namespaceName.Length == 0
                            ? ns.Name.ToString()
                            : $"{namespaceName}.{ns.Name}";
                        foreach (var member in SourceMembers(ns.Members, nextNamespace, containingTypes, sourcePath, overloads, sourceIdentity))
                            yield return member;
                        break;
                    }
                    case TypeDeclarationSyntax type:
                    {
                        string typeName = type.Identifier.ValueText;
                        var typeStack = containingTypes.Concat([typeName]).ToArray();
                        string fullType = namespaceName.Length == 0
                            ? string.Join(".", typeStack)
                            : $"{namespaceName}.{string.Join(".", typeStack)}";

                        foreach (var member in TypeMembers(type, fullType, sourcePath, overloads, sourceIdentity))
                            yield return member;
                        foreach (var member in SourceMembers(type.Members, namespaceName, typeStack, sourcePath, overloads, sourceIdentity))
                            yield return member;
                        break;
                    }
                }
            }

            static IEnumerable<SourceMember> TypeMembers(
                TypeDeclarationSyntax type,
                string fullType,
                string path,
                Dictionary<string, Dictionary<string, int>> overloadsByType,
                CSharpSourceIdentityContext sourceIdentity)
            {
                if (!overloadsByType.TryGetValue(fullType, out var overloads))
                    overloadsByType[fullType] = overloads = new Dictionary<string, int>(StringComparer.Ordinal);

                if (HasPrimaryConstructor(type))
                    NextOverload(overloads, ".ctor");

                foreach (var member in type.Members)
                {
                    switch (member)
                    {
                        case MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null }:
                            break;
                        case MethodDeclarationSyntax method:
                        {
                            if (IsBodylessPartial(method))
                                break;
                            string methodName = method.Identifier.ValueText;
                            int overload = NextOverload(overloads, methodName);
                            if (BodyText(method) is { } body)
                                yield return new SourceMember(fullType, methodName, overload, path, body);
                            break;
                        }
                        case ConstructorDeclarationSyntax constructor:
                        {
                            string methodName = constructor.Modifiers.Any(SyntaxKind.StaticKeyword) ? ".cctor" : ".ctor";
                            int overload = NextOverload(overloads, methodName);
                            if (BodyText(constructor) is { } body)
                                yield return new SourceMember(fullType, methodName, overload, path, body);
                            break;
                        }
                        case PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: not null }:
                            break;
                        case PropertyDeclarationSyntax property:
                        {
                            foreach (var propertyMember in sourceIdentity.PropertyMembers(property))
                            {
                                string methodName = propertyMember.MetadataName;
                                int overload = NextOverload(overloads, methodName);
                                if (propertyMember.Body is { } body)
                                    yield return new SourceMember(fullType, methodName, overload, path, body);
                            }

                            break;
                        }
                        case IndexerDeclarationSyntax { ExplicitInterfaceSpecifier: not null }:
                            break;
                        case IndexerDeclarationSyntax indexer:
                        {
                            foreach (var indexerMember in sourceIdentity.IndexerMembers(indexer, fullType))
                            {
                                string methodName = indexerMember.MetadataName;
                                int overload = NextOverload(overloads, methodName);
                                if (indexerMember.Body is { } body)
                                    yield return new SourceMember(fullType, methodName, overload, path, body);
                            }

                            break;
                        }
                        case OperatorDeclarationSyntax op:
                        {
                            string methodName = OperatorMetadataName(op);
                            int overload = NextOverload(overloads, methodName);
                            if (BodyText(op) is { } body)
                                yield return new SourceMember(fullType, methodName, overload, path, body);
                            break;
                        }
                        case ConversionOperatorDeclarationSyntax conversion:
                        {
                            string methodName = conversion.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword)
                                ? "op_Implicit"
                                : "op_Explicit";
                            int overload = NextOverload(overloads, methodName);
                            if (BodyText(conversion) is { } body)
                                yield return new SourceMember(fullType, methodName, overload, path, body);
                            break;
                        }
                    }
                }
            }

            static int NextOverload(Dictionary<string, int> overloads, string methodName)
            {
                int overload = overloads.GetValueOrDefault(methodName);
                overloads[methodName] = overload + 1;
                return overload;
            }
        }

    }

    static IReadOnlyList<ProbeTarget> DiscoverTargets(string assemblyPath, int cap)
    {
        var targets = new List<ProbeTarget>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return targets;

        var reader = pe.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (targets.Count >= cap)
                break;

            var type = reader.GetTypeDefinition(typeHandle);
            var typeNamespace = reader.GetString(type.Namespace);
            if (!type.GetDeclaringType().IsNil
                || !type.IsPublic
                || typeNamespace == "System"
                || typeNamespace.StartsWith("System.", StringComparison.Ordinal)
                || AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), "System.CodeDom.Compiler.GeneratedCodeAttribute")
                || AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                || reader.GetString(type.Name) == "<Module>"
                || reader.GetString(type.Name).Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            var fullType = reader.GetFullTypeName(type);
            var typeFragments = AttributeReader.RenderAttributes(reader, type.GetCustomAttributes(), qualifyNames: true)
                .Select(attribute => $"[{attribute}]")
                .ToArray();

            var methodOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var methodHandle in type.GetMethods())
            {
                if (targets.Count >= cap)
                    break;

                var method = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(method.Name);
                var overload = methodOverloads.GetValueOrDefault(methodName);
                methodOverloads[methodName] = overload + 1;

                if (method.RelativeVirtualAddress == 0
                    || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                    || methodName is ".ctor" or ".cctor"
                    || methodName.StartsWith("get_", StringComparison.Ordinal)
                    || methodName.StartsWith("set_", StringComparison.Ordinal)
                    || methodName.StartsWith("add_", StringComparison.Ordinal)
                    || methodName.StartsWith("remove_", StringComparison.Ordinal)
                    || methodName.Contains('<', StringComparison.Ordinal))
                {
                    continue;
                }

                var fragments = new List<string>();
                fragments.AddRange(typeFragments);
                fragments.AddRange(AttributeReader.RenderAttributes(reader, method.GetCustomAttributes(), qualifyNames: true)
                    .Select(attribute => $"[{attribute}]"));
                AddReturnAndParameterFragments(reader, method.GetParameters(), fragments);
                targets.Add(new ProbeTarget(new ReturnToSender.RequestedTarget(fullType, methodName, overload), fragments.Distinct(StringComparer.Ordinal).ToArray()));
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                if (targets.Count >= cap)
                    break;

                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;

                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (getter.RelativeVirtualAddress == 0
                    || (getter.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }

                var methodName = reader.GetString(getter.Name);
                var overload = OverloadIndex(reader, type, accessors.Getter, methodName);
                var fragments = new List<string>();
                fragments.AddRange(typeFragments);
                fragments.AddRange(AttributeReader.RenderAttributes(reader, property.GetCustomAttributes(), qualifyNames: true)
                    .Select(attribute => $"[{attribute}]"));
                AddReturnAndParameterFragments(reader, getter.GetParameters(), fragments);
                targets.Add(new ProbeTarget(new ReturnToSender.RequestedTarget(fullType, methodName, overload), fragments.Distinct(StringComparer.Ordinal).ToArray()));
            }
        }

        return targets;
    }

    static void AddReturnAndParameterFragments(MetadataReader reader, ParameterHandleCollection parameters, List<string> fragments)
    {
        foreach (var parameterHandle in parameters)
        {
            var parameter = reader.GetParameter(parameterHandle);
            var attributes = AttributeReader.RenderParameterAttributes(reader, parameterHandle)
                .Select(attribute => parameter.SequenceNumber == 0 ? $"[return: {attribute}]" : $"[{attribute}]");
            fragments.AddRange(attributes);
        }
    }

    static int OverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string methodName)
    {
        int overload = 0;
        foreach (var handle in typeDef.GetMethods())
        {
            if (handle == target)
                return overload;
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName)
                overload++;
        }
        return overload;
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string OutcomeId(ReturnToSenderSourceOutcome outcome)
        => outcome switch
        {
            ReturnToSenderSourceOutcome.ValidMatch => "valid_match",
            ReturnToSenderSourceOutcome.ValidDifferent => "valid_different",
            ReturnToSenderSourceOutcome.Invalid => "invalid",
            ReturnToSenderSourceOutcome.SourceUnavailable => "source_unavailable",
            ReturnToSenderSourceOutcome.UnsupportedTarget => "unsupported_target",
            _ => outcome.ToString(),
        };

    static string? BodyText(MethodDeclarationSyntax method)
    {
        if (method.Body is { } body)
            return StatementsText(body);
        if (method.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(method.ReturnType, expressionBody.Expression);
        return null;
    }

    static bool HasPrimaryConstructor(TypeDeclarationSyntax type)
        => type switch
        {
            ClassDeclarationSyntax { ParameterList: not null } => true,
            StructDeclarationSyntax { ParameterList: not null } => true,
            RecordDeclarationSyntax { ParameterList: not null } => true,
            _ => false,
        };

    static bool IsBodylessPartial(MethodDeclarationSyntax method)
        => method.Modifiers.Any(SyntaxKind.PartialKeyword)
            && method.Body is null
            && method.ExpressionBody is null;

    static string? BodyText(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Body is { } body)
            return StatementsText(body);
        if (constructor.ExpressionBody is { } expressionBody)
            return $"{expressionBody.Expression};";
        return null;
    }

    static string? BodyText(OperatorDeclarationSyntax op)
    {
        if (op.Body is { } body)
            return StatementsText(body);
        if (op.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(op.ReturnType, expressionBody.Expression);
        return null;
    }

    static string? BodyText(ConversionOperatorDeclarationSyntax conversion)
    {
        if (conversion.Body is { } body)
            return StatementsText(body);
        if (conversion.ExpressionBody is { } expressionBody)
            return ExpressionBodyText(conversion.Type, expressionBody.Expression);
        return null;
    }

    static string ExpressionBodyText(TypeSyntax returnType, ExpressionSyntax expression)
        => returnType is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)
            ? $"{expression};"
            : $"return {expression};";

    static string StatementsText(BlockSyntax body)
        => string.Join(Environment.NewLine, body.Statements.Select(statement => statement.ToString()));

    static string NormalizeBody(string text)
        => Regex.Replace(text, @"\s+", "");

    static bool TryKnownTasteDifference(
        string expected,
        string actual,
        IReadOnlyList<DecompilerDecision> decisions,
        out string? detail)
    {
        var rewrites = decisions
            .Where(decision => decision is { Category: "taste", RuleId: "type-name.framework-imported" })
            .Select(decision =>
            {
                string oldValue = decision.OldValue ?? decision.Subject.Replace('+', '.');
                int lastDot = oldValue.LastIndexOf('.');
                return lastDot < 0 || lastDot == oldValue.Length - 1
                    ? null
                    : new FrameworkTypeRewrite(
                        oldValue,
                        decision.NewValue ?? oldValue[(lastDot + 1)..],
                        decision);
            })
            .Where(rewrite => rewrite is not null)
            .Select(rewrite => rewrite!)
            .ToArray();
        if (rewrites.Length == 0)
        {
            detail = null;
            return false;
        }

        string wrapped = "{" + Environment.NewLine + expected + Environment.NewLine + "}";
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        var root = tree.GetCompilationUnitRoot();
        var rewriter = new FrameworkTypeNameRewriter(rewrites);
        var rewrittenRoot = rewriter.Visit(root);
        if (rewrittenRoot is null || rewriter.Applied.Count == 0)
        {
            detail = null;
            return false;
        }

        string rewrittenExpected = rewrittenRoot.ToFullString();
        int openBrace = rewrittenExpected.IndexOf('{');
        int closeBrace = rewrittenExpected.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
            rewrittenExpected = rewrittenExpected[(openBrace + 1)..closeBrace];

        if (NormalizeBody(rewrittenExpected) == NormalizeBody(actual))
        {
            detail = string.Join("; ", rewriter.Applied.Distinct().Select(decision => decision.Detail));
            return true;
        }

        detail = null;
        return false;
    }

    sealed record FrameworkTypeRewrite(string FullName, string SimpleName, DecompilerDecision Decision);

    sealed class FrameworkTypeNameRewriter(IReadOnlyList<FrameworkTypeRewrite> rewrites) : CSharpSyntaxRewriter
    {
        public List<DecompilerDecision> Applied { get; } = [];

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var rewrite = FindRewrite(node);
            var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;
            return rewrite is not null
                ? ApplyRewrite(visited, rewrite)
                : RewriteName(visited) ?? visited;
        }

        public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
        {
            var rewrite = FindRewrite(node);
            var visited = (AliasQualifiedNameSyntax)base.VisitAliasQualifiedName(node)!;
            return rewrite is not null
                ? ApplyRewrite(visited, rewrite)
                : RewriteName(visited) ?? visited;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var rewrite = FindRewrite(node);
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            return rewrite is not null
                ? ApplyRewrite(visited, rewrite)
                : RewriteName(visited) ?? visited;
        }

        SyntaxNode? RewriteName(SyntaxNode node)
            => FindRewrite(node) is { } rewrite ? ApplyRewrite(node, rewrite) : null;

        FrameworkTypeRewrite? FindRewrite(SyntaxNode node)
        {
            string canonical = CanonicalNameText(node);
            return rewrites
                .Where(rewrite => canonical == rewrite.FullName)
                .OrderByDescending(rewrite => rewrite.FullName.Length)
                .FirstOrDefault();
        }

        SyntaxNode ApplyRewrite(SyntaxNode node, FrameworkTypeRewrite rewrite)
        {
            Applied.Add(rewrite.Decision);
            return ReplacementName(node, rewrite.SimpleName).WithTriviaFrom(node);
        }

        static string CanonicalNameText(SyntaxNode node)
            => node switch
            {
                GenericNameSyntax generic => generic.Identifier.ValueText,
                QualifiedNameSyntax qualified => $"{CanonicalNameText(qualified.Left)}.{CanonicalNameText(qualified.Right)}",
                AliasQualifiedNameSyntax aliasQualified => $"{aliasQualified.Alias.Identifier.ValueText}::{CanonicalNameText(aliasQualified.Name)}",
                MemberAccessExpressionSyntax memberAccess => $"{CanonicalNameText(memberAccess.Expression)}.{CanonicalNameText(memberAccess.Name)}",
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
                NameSyntax name => name.ToString(),
                _ => node.ToString(),
            };

        static SimpleNameSyntax ReplacementName(SyntaxNode original, string simpleName)
            => original switch
            {
                GenericNameSyntax generic => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(simpleName),
                    generic.TypeArgumentList),
                QualifiedNameSyntax { Right: GenericNameSyntax generic } => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(simpleName),
                    generic.TypeArgumentList),
                MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(simpleName),
                    generic.TypeArgumentList),
                _ => SyntaxFactory.IdentifierName(simpleName),
            };
    }

    static string OperatorMetadataName(OperatorDeclarationSyntax op)
        => op.OperatorToken.Kind() switch
        {
            SyntaxKind.PlusToken => op.ParameterList.Parameters.Count == 1 ? "op_UnaryPlus" : "op_Addition",
            SyntaxKind.MinusToken => op.ParameterList.Parameters.Count == 1 ? "op_UnaryNegation" : "op_Subtraction",
            SyntaxKind.ExclamationToken => "op_LogicalNot",
            SyntaxKind.TildeToken => "op_OnesComplement",
            SyntaxKind.PlusPlusToken => "op_Increment",
            SyntaxKind.MinusMinusToken => "op_Decrement",
            SyntaxKind.TrueKeyword => "op_True",
            SyntaxKind.FalseKeyword => "op_False",
            SyntaxKind.AsteriskToken => "op_Multiply",
            SyntaxKind.SlashToken => "op_Division",
            SyntaxKind.PercentToken => "op_Modulus",
            SyntaxKind.AmpersandToken => "op_BitwiseAnd",
            SyntaxKind.BarToken => "op_BitwiseOr",
            SyntaxKind.CaretToken => "op_ExclusiveOr",
            SyntaxKind.LessThanLessThanToken => "op_LeftShift",
            SyntaxKind.GreaterThanGreaterThanToken => "op_RightShift",
            SyntaxKind.GreaterThanGreaterThanGreaterThanToken => "op_UnsignedRightShift",
            SyntaxKind.EqualsEqualsToken => "op_Equality",
            SyntaxKind.ExclamationEqualsToken => "op_Inequality",
            SyntaxKind.LessThanToken => "op_LessThan",
            SyntaxKind.GreaterThanToken => "op_GreaterThan",
            SyntaxKind.LessThanEqualsToken => "op_LessThanOrEqual",
            SyntaxKind.GreaterThanEqualsToken => "op_GreaterThanOrEqual",
            _ => op.OperatorToken.ValueText,
        };

    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "recompile-fail";
        var match = Regex.Match(detail, @"CS\d{4}");
        return match.Success ? match.Value : "recompile-fail";
    }

    static string FailureReason(ReturnToSender.Result result)
        => result.Status switch
        {
            FidelityCheck.CompileBackStatus.RecompileFail => DiagnosticCode(result.Detail),
            FidelityCheck.CompileBackStatus.ContextFail => string.IsNullOrWhiteSpace(result.Detail) ? "context-fail" : result.Detail,
            FidelityCheck.CompileBackStatus.OpcodeDiff => "opcode-diff",
            _ => result.Status.ToString(),
        };
}
