using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// A deliberately bounded source audit for this decoder, not a general analyzer.
/// Source calls/accessors/initializers are followed; framework effects require an
/// import, and materialization sites require an explicit inventory entry.
/// </summary>
internal sealed class SignatureOccurrenceSourceAudit(
    CSharpCompilation compilation,
    Func<IOperation, string?> classify)
{
    // These nodes have no independent materialization/call effect; their child
    // operations are still visited. A new Roslyn operation is not safe by default.
    static readonly HashSet<string> PureOperations = new(StringComparer.Ordinal)
    {
        "MethodBodyOperation", "ConstructorBodyOperation", "Block", "Return", "ExpressionStatement",
        "Conditional", "SwitchExpression", "SwitchExpressionArm", "Throw",
        "Literal", "DefaultValue", "NameOf", "TypeOf",
        "ParameterReference", "LocalReference", "InstanceReference",
        "VariableDeclarationGroup", "VariableDeclaration", "VariableDeclarator",
        "VariableInitializer", "FieldInitializer", "PropertyInitializer",
        "DeclarationExpression", "CollectionExpressionElementsPlaceholder",
        "ParameterInitializer", "ArrayInitializer", "ObjectOrCollectionInitializer",
        "ArrayElementReference", "Conversion", "Unary", "Binary",
        "SimpleAssignment", "CompoundAssignment", "Increment", "Decrement",
        "Argument", "ConditionalAccess", "ConditionalAccessInstance",
        "Coalesce", "IsPattern", "IsType", "ConstantPattern", "DeclarationPattern",
        "NegatedPattern", "BinaryPattern", "RelationalPattern", "DiscardPattern",
        "TypePattern", "RecursivePattern", "PropertySubpattern", "Discard",
        "Tuple", "TupleBinary", "DeconstructionAssignment", "Range",
        "InterpolatedStringText", "Interpolation", "Try", "CatchClause",
        "Using", "UsingDeclaration", "Empty",
    };
    readonly HashSet<ISymbol> _visited = new(SymbolEqualityComparer.Default);
    readonly HashSet<INamedTypeSymbol> _initialized = new(SymbolEqualityComparer.Default);
    readonly HashSet<(SyntaxNode Syntax, OperationKind Kind)> _operations = [];
    readonly List<string> _violations = [];
    readonly List<IOperation> _effects = [];
    readonly Dictionary<IMethodSymbol, IOperation> _bodies = new(SymbolEqualityComparer.Default);
    readonly List<(IMethodSymbol Target, IOperation Site)> _calls = [];

    internal IReadOnlyList<IOperation> Effects => _effects;
    internal IReadOnlyDictionary<IMethodSymbol, IOperation> Bodies => _bodies;
    internal IReadOnlyList<(IMethodSymbol Target, IOperation Site)> Calls => _calls;

    internal IReadOnlyList<string> Run()
    {
        foreach (string name in new[] { "SignatureOccurrenceDecoder", "SignatureOccurrenceProvider" })
        {
            var type = compilation.GetTypeByMetadataName("ILInspector.Metadata." + name)!;
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                .Where(method => method.DeclaredAccessibility == Accessibility.Public
                    && method.MethodKind == MethodKind.Ordinary))
                Follow(method);
        }
        return _violations;
    }

    void Follow(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        if (!_visited.Add(method))
            return;
        Initialize(method.ContainingType);
        if (method.MethodKind == MethodKind.Constructor
            && method.ContainingType.BaseType is { } baseType && HasSource(baseType))
        {
            foreach (var constructor in baseType.InstanceConstructors)
                Follow(constructor);
        }
        if (method.IsVirtual && !method.IsSealed && !method.ContainingType.IsSealed
            && method.ContainingType.TypeKind != TypeKind.Interface)
        {
            _violations.Add($"Unclassified source virtual dispatch: {method}");
            return;
        }
        if (method.IsAbstract && method.ContainingType.TypeKind == TypeKind.Interface)
        {
            // Only this closed, source-defined static dispatch is part of the
            // decoder. A new interface boundary needs its own declared targets.
            if (method.ContainingType.Name != "IMetadataTypeNameRow")
            {
                _violations.Add($"Unclassified source dispatch: {method}");
                return;
            }
            var owner = method.ContainingType.ContainingType!;
            foreach (var implementation in owner.GetTypeMembers().Where(type =>
                type.AllInterfaces.Any(iface => SymbolEqualityComparer.Default.Equals(
                    iface.OriginalDefinition, method.ContainingType))))
            {
                foreach (var target in implementation.GetMembers(method.Name).OfType<IMethodSymbol>())
                    Follow(target);
            }
            return;
        }

        foreach (var declaration in method.DeclaringSyntaxReferences)
        {
            var syntax = declaration.GetSyntax();
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var operation = model.GetOperation(syntax);
            if (operation is null && syntax is AccessorDeclarationSyntax accessor
                && ((SyntaxNode?)accessor.Body ?? accessor.ExpressionBody) is { } accessorBody)
                operation = model.GetOperation(accessorBody);
            if (operation is null && syntax is PropertyDeclarationSyntax { ExpressionBody: { } propertyBody })
                operation = model.GetOperation(propertyBody);
            if (operation is null && syntax is IndexerDeclarationSyntax { ExpressionBody: { } indexerBody })
                operation = model.GetOperation(indexerBody);
            if (operation is null)
            {
                // Auto-properties, positional records, and implicit constructors
                // have no user body; their initializers are audited separately.
                if (syntax is ParameterSyntax or TypeDeclarationSyntax
                    || syntax is PropertyDeclarationSyntax { ExpressionBody: null }
                    || syntax is AccessorDeclarationSyntax { Body: null, ExpressionBody: null })
                    continue;
                _violations.Add($"No auditable source body: {method}: {syntax.Kind()}");
                continue;
            }
            _bodies[method] = operation;
            Visit(operation);
        }
    }

    void Initialize(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (!_initialized.Add(type) || !HasSource(type))
            return;
        if (type.ContainingType is not null)
            Initialize(type.ContainingType);
        if (type.BaseType is { } baseType && HasSource(baseType))
            Initialize(baseType);
        foreach (var constructor in type.StaticConstructors)
            Follow(constructor);
        // Conservatively include both instance and static initializers. This
        // avoids relying on lazy/static initialization timing for the bound.
        foreach (var declaration in type.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax() is not TypeDeclarationSyntax syntax)
                continue;
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            foreach (var member in syntax.Members)
            {
                foreach (var initializer in Initializers(member))
                {
                    if (model.GetOperation(initializer) is { } operation)
                        Visit(operation);
                    else if (model.GetOperation(initializer.Value) is { } value)
                        Visit(value);
                    else
                        _violations.Add($"Unbound initializer: {initializer}");
                }
            }
        }
    }

    static IEnumerable<EqualsValueClauseSyntax> Initializers(MemberDeclarationSyntax member) =>
        member switch
        {
            FieldDeclarationSyntax field => field.Declaration.Variables
                .Where(variable => variable.Initializer is not null)
                .Select(variable => variable.Initializer!),
            PropertyDeclarationSyntax { Initializer: { } initializer } => [initializer],
            EventFieldDeclarationSyntax field => field.Declaration.Variables
                .Where(variable => variable.Initializer is not null)
                .Select(variable => variable.Initializer!),
            _ => [],
        };

    void Visit(IOperation operation)
    {
        if (operation is INameOfOperation)
            return;
        foreach (var child in operation.ChildOperations)
            Visit(child);
        if (operation is IInvalidOperation)
        {
            _violations.Add($"Unbound operation: {operation.Syntax}");
            return;
        }
        switch (operation)
        {
            case IInvocationOperation call:
                MethodEffect(call.TargetMethod, operation);
                break;
            case IObjectCreationOperation creation:
                Effect(operation);
                if (creation.Constructor is { } constructor)
                    MethodEffect(constructor, operation, alreadyClassified: true);
                break;
            case IPropertyReferenceOperation property:
                if (HasSource(property.Property))
                {
                    Initialize(property.Property.ContainingType);
                    if (property.Property.GetMethod is { } getter)
                    {
                        _calls.Add((getter.OriginalDefinition, operation));
                        Follow(getter);
                    }
                    if (property.Property.SetMethod is { } setter)
                    {
                        _calls.Add((setter.OriginalDefinition, operation));
                        Follow(setter);
                    }
                }
                else
                    Effect(operation);
                break;
            case IFieldReferenceOperation field:
                if (HasSource(field.Field))
                    Initialize(field.Field.ContainingType);
                else if (!field.Field.IsConst)
                    Effect(operation);
                break;
            case IMethodReferenceOperation reference:
                // Source delegates (including the name-reader charge callbacks)
                // are not opaque sanctioned regions.
                MethodEffect(reference.Method, operation);
                break;
            case IConversionOperation { OperatorMethod: { } conversion }:
                MethodEffect(conversion, operation);
                break;
            case IConversionOperation conversion when conversion.Operand.Type?.IsValueType == true
                && conversion.Type?.IsReferenceType == true:
                Effect(operation);
                break;
            case IImplicitIndexerReferenceOperation indexer:
                Effect(operation);
                if (indexer.IndexerSymbol is IMethodSymbol slice && HasSource(slice))
                    MethodEffect(slice, operation);
                else if (indexer.IndexerSymbol is IPropertySymbol { GetMethod: { } getter } && HasSource(getter))
                    MethodEffect(getter, operation);
                break;
            case IArrayElementReferenceOperation element when element.Indices.Any(index =>
                index.Type?.ToDisplayString() == "System.Range"):
                Effect(operation);
                break;
            case IBinaryOperation { OperatorMethod: { } binary }:
                MethodEffect(binary, operation);
                break;
            case IUnaryOperation { OperatorMethod: { } unary }:
                MethodEffect(unary, operation);
                break;
            case ICollectionExpressionOperation collection:
                Effect(operation);
                if (collection.ConstructMethod is { } factory)
                    MethodEffect(factory, operation, alreadyClassified: true);
                break;
            case IWithOperation with:
                Effect(operation);
                if (with.CloneMethod is { } clone)
                    MethodEffect(clone, operation, alreadyClassified: true);
                break;
            case IForEachLoopOperation each:
                Effect(operation);
                if (each.IsAsynchronous)
                    _violations.Add($"Unclassified asynchronous enumeration: {each.Syntax}");
                if (each.Syntax is ForEachStatementSyntax syntax)
                {
                    var info = compilation.GetSemanticModel(syntax.SyntaxTree).GetForEachStatementInfo(syntax);
                    foreach (var method in new[]
                    {
                        info.GetEnumeratorMethod, info.MoveNextMethod,
                        info.CurrentProperty?.GetMethod, info.DisposeMethod,
                    }.OfType<IMethodSymbol>())
                    {
                        if (HasSource(method))
                            MethodEffect(method, operation);
                        else if (!method.ContainingType.OriginalDefinition.ToDisplayString()
                            .StartsWith("System.Collections.Immutable.ImmutableArray<", StringComparison.Ordinal))
                            _violations.Add($"Unclassified enumeration import: {method}");
                    }
                }
                break;
            case IBinaryOperation binary when binary.Type?.SpecialType == SpecialType.System_String:
            case IArrayCreationOperation:
            case IInterpolatedStringOperation:
            case IDelegateCreationOperation:
            case ILoopOperation:
            case IAnonymousFunctionOperation:
            case ILocalFunctionOperation:
            case IAwaitOperation:
                Effect(operation);
                break;
            case IBranchOperation branch when !branch.IsImplicit:
            case ILabeledOperation:
                Effect(operation);
                break;
            default:
                if (operation.Syntax is StackAllocArrayCreationExpressionSyntax
                    or ImplicitStackAllocArrayCreationExpressionSyntax)
                    Effect(operation);
                else if (!PureOperations.Contains(operation.Kind.ToString()))
                    _violations.Add($"Unclassified operation kind: {operation.Kind}: {operation.Syntax}");
                break;
        }
    }

    void MethodEffect(IMethodSymbol method, IOperation operation, bool alreadyClassified = false)
    {
        if (HasSource(method))
        {
            _calls.Add((method.OriginalDefinition, operation));
            Follow(method);
        }
        else if (HasSource(method.ContainingType) && method.IsImplicitlyDeclared
            && method.MethodKind == MethodKind.Constructor)
            Initialize(method.ContainingType);
        else if (!alreadyClassified)
            Effect(operation);
    }

    void Effect(IOperation operation)
    {
        if (!_operations.Add((operation.Syntax, operation.Kind)))
            return;
        _effects.Add(operation);
        if (classify(operation) is null)
            _violations.Add($"Unclassified effect: {Site(operation)}");
    }

    internal static string Site(IOperation operation) =>
        $"{Owner(operation.Syntax)} | {operation.Kind} | {EffectName(operation)}";

    static string EffectName(IOperation operation) => operation switch
    {
        IInvocationOperation call => Key(call.TargetMethod),
        IObjectCreationOperation creation => creation.Constructor?.ToDisplayString()
            ?? creation.Type!.ToDisplayString(),
        IPropertyReferenceOperation property =>
            $"{property.Property.OriginalDefinition.ContainingType.ToDisplayString()}.{property.Property.Name}",
        IFieldReferenceOperation field =>
            $"{field.Field.OriginalDefinition.ContainingType.ToDisplayString()}.{field.Field.Name}",
        ILoopOperation loop when loop.Syntax is ForEachStatementSyntax each =>
            $"foreach({Compact(each.Type)}{each.Identifier.ValueText}in{Compact(each.Expression)})",
        ILoopOperation loop when loop.Syntax is ForStatementSyntax @for =>
            $"for({Compact(@for.Declaration!)};{Compact(@for.Condition!)};{string.Join(",", @for.Incrementors.Select(Compact))})",
        _ => Compact(operation.Syntax),
    };

    internal static string Owner(SyntaxNode syntax)
    {
        var type = syntax.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
        string member = syntax.AncestorsAndSelf().FirstOrDefault(node =>
            node is BaseMethodDeclarationSyntax or PropertyDeclarationSyntax or VariableDeclaratorSyntax) switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax => ".ctor",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
            _ => ".initializer",
        };
        // A local variable belongs to its containing method, not to a new scope.
        var enclosing = syntax.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
        if (enclosing is MethodDeclarationSyntax enclosingMethod)
            member = enclosingMethod.Identifier.ValueText;
        else if (enclosing is ConstructorDeclarationSyntax)
            member = ".ctor";
        return $"{type}.{member}";
    }

    internal static string Compact(SyntaxNode syntax) =>
        string.Concat(syntax.DescendantTokens().Select(token => token.Text));

    internal static bool HasSource(ISymbol symbol) =>
        symbol.Locations.Any(location => location.IsInSource);

    internal static string Key(IMethodSymbol method) =>
        $"{method.OriginalDefinition.ContainingType.ToDisplayString()}.{method.Name}";
}

/// <summary>
/// Checks the actual Class B reads using call-site-specific argument provenance.
/// Only immutable local aliases, row properties, and the existing simple
/// projection helper chains are admitted. Unrecognized value flow fails closed.
/// </summary>
internal sealed class SignatureOccurrenceChargeAudit(
    CSharpCompilation compilation,
    IReadOnlyDictionary<IMethodSymbol, IOperation> bodies,
    IReadOnlyList<(IMethodSymbol Target, IOperation Site)> calls)
{
    readonly List<string> _violations = [];
    readonly Dictionary<string, string> _spent = [];
    readonly HashSet<SyntaxNode> _checkedCalls = [];
    readonly HashSet<SyntaxNode> _checkedReads = [];

    sealed record Frame(
        IMethodSymbol Method,
        Dictionary<IParameterSymbol, string> Arguments,
        Frame? Caller,
        IInvocationOperation? Call);

    internal IReadOnlyList<string> Run()
    {
        var provider = compilation.GetTypeByMetadataName("ILInspector.Metadata.SignatureOccurrenceProvider")!;
        foreach (string name in new[] { "ReadAssemblyScope", "ReadModuleScope" })
        {
            var method = provider.GetMembers(name).OfType<IMethodSymbol>().Single();
            var arguments = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
            foreach (var parameter in method.Parameters)
                arguments[parameter] = $"{name}:{parameter.Name}";
            Walk(new Frame(method, arguments, null, null), []);
        }
        CheckKernel();
        CheckCoverage();
        return _violations;
    }

    void Walk(Frame frame, HashSet<IMethodSymbol> active)
    {
        if (active.Count >= 32 || !active.Add(frame.Method))
        {
            _violations.Add($"Unclassified recursive projection boundary: {frame.Method}");
            return;
        }
        if (!bodies.TryGetValue(frame.Method.OriginalDefinition, out var body))
        {
            _violations.Add($"Missing projection body: {frame.Method}");
            active.Remove(frame.Method);
            return;
        }
        foreach (var call in All(body).OfType<IInvocationOperation>())
        {
            string key = SignatureOccurrenceSourceAudit.Key(call.TargetMethod);
            if (key is "System.Reflection.Metadata.MetadataReader.GetString"
                or "System.Reflection.Metadata.MetadataReader.GetBlobBytes")
            {
                CheckRead(frame, call);
            }
            else if (SignatureOccurrenceSourceAudit.HasSource(call.TargetMethod)
                && call.TargetMethod.ContainingType.Name != "SignatureOccurrenceWorkBudget")
            {
                _checkedCalls.Add(call.Syntax);
                var arguments = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
                foreach (var argument in call.Arguments)
                {
                    if (argument.Parameter is null || argument.Parameter.RefKind != RefKind.None)
                    {
                        _violations.Add($"Unclassified projection argument: {argument.Syntax}");
                        continue;
                    }
                    arguments[argument.Parameter.OriginalDefinition] = Value(argument.Value, frame);
                }
                Walk(new Frame(call.TargetMethod.OriginalDefinition, arguments, frame, call),
                    new HashSet<IMethodSymbol>(active, SymbolEqualityComparer.Default));
            }
        }
        active.Remove(frame.Method);
    }

    void CheckRead(Frame frame, IInvocationOperation read)
    {
        _checkedReads.Add(read.Syntax);
        if (read.Syntax.Ancestors().Any(node => node is AnonymousFunctionExpressionSyntax
            or LocalFunctionStatementSyntax))
        {
            _violations.Add($"Unclassified deferred projection read: {read.Syntax}");
            return;
        }
        string reader = Value(read.Instance!, frame);
        string handle = Value(read.Arguments[0].Value, frame);
        string handleType = read.Arguments[0].Value.Type!.ToDisplayString();
        if (reader.Contains('?', StringComparison.Ordinal) || handle.Contains('?', StringComparison.Ordinal)
            || handleType != (read.TargetMethod.Name == "GetString"
                ? "System.Reflection.Metadata.StringHandle" : "System.Reflection.Metadata.BlobHandle"))
        {
            _violations.Add($"Unclassified raw-storage provenance: {read.Syntax}");
            return;
        }
        string price = $"property(Length,call(System.Reflection.Metadata.MetadataReader.GetBlobReader,{reader},{handle}))";
        string metric = frame.Method.Name switch
        {
            "ReadModuleScope" => Metric("ModuleReferenceNameBytes"),
            "Create" => Metric("AssemblyReferenceNameBytes"),
            "StringOrNull" => Metric("AssemblyReferenceCultureBytes"),
            "TokenOrNull" => KeyMetric(frame, handle),
            _ => "?unclassified-read",
        };
        var current = frame;
        IInvocationOperation target = read;
        string path = $"{frame.Method}:{read.Syntax.SpanStart}";
        while (current is not null)
        {
            if (!bodies.TryGetValue(current.Method.OriginalDefinition, out var body))
                break;
            if (HasRepeat(body, target))
            {
                _violations.Add($"Materialization can repeat without a fresh charge: {read.Syntax}");
                return;
            }
            foreach (var charge in All(body).OfType<IInvocationOperation>().Where(call =>
                SignatureOccurrenceSourceAudit.Key(call.TargetMethod)
                    == "ILInspector.Metadata.SignatureOccurrenceWorkBudget.Work"))
            {
                if (Value(charge.Arguments[0].Value, current) != metric
                    || Value(charge.Arguments[1].Value, current) != price
                    || !Dominates(body, charge, target))
                    continue;
                string chargeId = $"{FramePath(current)}:{charge.Syntax.SpanStart}";
                if (_spent.TryGetValue(chargeId, out string? previous) && previous != path)
                    _violations.Add($"Charge reused for separate materializations: {charge.Syntax}");
                else
                    _spent[chargeId] = path;
                return;
            }
            if (current.Caller is null || current.Call is null)
                break;
            target = current.Call;
            path += $":{target.Syntax.SpanStart}";
            current = current.Caller;
        }
        _violations.Add($"No dominating exact-storage charge reaches {read.Syntax} ({metric}; {price}).");
    }

    string KeyMetric(Frame frame, string handle)
    {
        var flag = frame.Method.Parameters.Single(parameter => parameter.Name == "isPublicKey");
        string actual = ValueParameter(flag, frame);
        // The key flag must derive from the same AssemblyRef row as the blob.
        string prefix = "property(PublicKeyOrToken,";
        if (!handle.StartsWith(prefix, StringComparison.Ordinal))
            return "?unclassified-key-origin";
        string row = handle[prefix.Length..^1];
        string expected = $"binary(NotEquals,binary(And,property(Flags,{row}),constant(System.Reflection.AssemblyFlags,1)),constant(System.Reflection.AssemblyFlags,0))";
        if (actual != expected)
            return "?key-class-not-flag-derived:" + actual;
        return $"conditional({actual},{Metric("AssemblyReferenceFullKeyBytes")},{Metric("AssemblyReferenceTokenBytes")})";
    }

    string Metric(string name)
    {
        var member = compilation.GetTypeByMetadataName("ILInspector.Metadata.SignatureOccurrenceMetric")!
            .GetMembers(name).OfType<IFieldSymbol>().Single();
        return $"constant(ILInspector.Metadata.SignatureOccurrenceMetric,{member.ConstantValue})";
    }

    string Value(IOperation operation, Frame frame)
    {
        if (operation.ConstantValue.HasValue)
            return $"constant({operation.Type?.ToDisplayString()},{operation.ConstantValue.Value})";
        return operation switch
        {
            IConversionOperation conversion => Value(conversion.Operand, frame),
            IParameterReferenceOperation parameter => ValueParameter(parameter.Parameter, frame),
            ILocalReferenceOperation local => Local(local, frame),
            IPropertyReferenceOperation property when !SignatureOccurrenceSourceAudit.HasSource(property.Property) =>
                $"property({property.Property.Name},{Value(property.Instance!, frame)})",
            IInvocationOperation call when !SignatureOccurrenceSourceAudit.HasSource(call.TargetMethod) =>
                $"call({SignatureOccurrenceSourceAudit.Key(call.TargetMethod)},{(call.Instance is null ? "static" : Value(call.Instance, frame))},{string.Join(",", call.Arguments.Select(argument => Value(argument.Value, frame)))})",
            IBinaryOperation binary =>
                $"binary({binary.OperatorKind},{Value(binary.LeftOperand, frame)},{Value(binary.RightOperand, frame)})",
            IConditionalOperation conditional =>
                $"conditional({Value(conditional.Condition, frame)},{Value(conditional.WhenTrue, frame)},{Value(conditional.WhenFalse!, frame)})",
            _ => "?unclassified-value:" + operation.Syntax,
        };
    }

    string ValueParameter(IParameterSymbol parameter, Frame frame)
    {
        if (bodies.TryGetValue(frame.Method.OriginalDefinition, out var body)
            && All(body).Any(operation =>
                operation is IAssignmentOperation assignment && ParameterReferenced(assignment.Target, parameter)
                || operation is IArgumentOperation { Parameter.RefKind: not RefKind.None } argument
                    && ParameterReferenced(argument.Value, parameter)))
            return $"?mutable-parameter:{parameter}";
        return frame.Arguments.TryGetValue(parameter.OriginalDefinition, out string? value)
            ? value : $"?unbound-parameter:{parameter}";
    }

    static bool ParameterReferenced(IOperation operation, IParameterSymbol parameter) =>
        All(operation).OfType<IParameterReferenceOperation>().Any(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter));

    string Local(ILocalReferenceOperation local, Frame frame)
    {
        if (!bodies.TryGetValue(frame.Method.OriginalDefinition, out var body))
            return "?missing-local-body";
        var declarations = All(body).OfType<IVariableDeclaratorOperation>()
            .Where(variable => SymbolEqualityComparer.Default.Equals(variable.Symbol, local.Local)).ToArray();
        if (declarations is not [{ Initializer: { } initializer }]
            || All(body).Any(operation =>
                operation is IAssignmentOperation assignment && References(assignment.Target, local.Local)
                || operation is IIncrementOrDecrementOperation increment && References(increment.Target, local.Local)
                || operation is IArgumentOperation { Parameter.RefKind: not RefKind.None } argument
                    && References(argument.Value, local.Local)))
            return "?mutable-or-unbound-local:" + local.Local.Name;
        return Value(initializer.Value, frame);
    }

    static bool References(IOperation operation, ILocalSymbol local) =>
        All(operation).OfType<ILocalReferenceOperation>().Any(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Local, local));

    static string FramePath(Frame frame) =>
        frame.Caller is null ? frame.Method.ToDisplayString()
            : $"{FramePath(frame.Caller)}:{frame.Call!.Syntax.SpanStart}";

    static ControlFlowGraph Graph(IOperation body) => body switch
    {
        IMethodBodyOperation method => ControlFlowGraph.Create(method),
        IConstructorBodyOperation constructor => ControlFlowGraph.Create(constructor),
        _ => throw new InvalidOperationException($"Unclassified projection CFG body: {body.Kind}"),
    };

    static BasicBlock Block(ControlFlowGraph graph, IOperation target) =>
        graph.Blocks.Single(block => block.Operations
            .Concat(block.BranchValue is null ? [] : new[] { block.BranchValue })
            .SelectMany(All).Any(operation => operation.Syntax == target.Syntax && operation.Kind == target.Kind));

    static bool Dominates(IOperation body, IOperation charge, IOperation target)
    {
        var graph = Graph(body);
        var start = Block(graph, charge);
        var end = Block(graph, target);
        if (start == end)
            return charge.Syntax.Span.End <= target.Syntax.SpanStart;
        return !Reachable(graph.Blocks[0], end, start);
    }

    static bool HasRepeat(IOperation body, IOperation target)
    {
        var graph = Graph(body);
        var block = Block(graph, target);
        return Successors(block).Any(next => Reachable(next, block, null));
    }

    static bool Reachable(BasicBlock start, BasicBlock target, BasicBlock? excluded)
    {
        var pending = new Stack<BasicBlock>();
        var visited = new HashSet<int>();
        pending.Push(start);
        while (pending.TryPop(out var block))
        {
            if (block == excluded || !visited.Add(block.Ordinal))
                continue;
            if (block == target)
                return true;
            foreach (var successor in Successors(block))
                pending.Push(successor);
        }
        return false;
    }

    static IEnumerable<BasicBlock> Successors(BasicBlock block)
    {
        if (block.FallThroughSuccessor?.Destination is { } next)
            yield return next;
        if (block.ConditionalSuccessor?.Destination is { } conditional)
            yield return conditional;
    }

    void CheckKernel()
    {
        var budget = compilation.GetTypeByMetadataName("ILInspector.Metadata.SignatureOccurrenceWorkBudget")!;
        var expected = new Dictionary<string, string>
        {
            ["Node"] = "Charge(SignatureOccurrenceMetric.SignatureNodes,1,ref_nodes,limits.Nodes,SignatureOccurrenceRejectionReason.NodeBudget)",
            ["Copies"] = "Charge(SignatureOccurrenceMetric.OccurrenceCopies,amount,ref_copies,limits.Copies,SignatureOccurrenceRejectionReason.OccurrenceCopyBudget)",
            ["Work"] = "Charge(metric,amount,ref_work,limits.Work,SignatureOccurrenceRejectionReason.WorkBudget)",
            ["Charge"] =
                "{ArgumentOutOfRangeException.ThrowIfNegative(amount);metrics?.Observe(metric,amount);" +
                "if(amount>ceiling-used)thrownewSignatureOccurrenceRejectedException(rejection);" +
                "used+=amount;metrics?.SetUsage(_nodes,_copies,_work);}",
        };
        foreach (var (name, approved) in expected)
        {
            var method = budget.GetMembers(name).OfType<IMethodSymbol>().Single();
            var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax();
            if (SignatureOccurrenceSourceAudit.Compact((SyntaxNode?)syntax.Body ?? syntax.ExpressionBody!.Expression)
                != approved)
                _violations.Add($"The ledger kernel changed and needs an updated enforcement proof: {name}");
        }
        var token = compilation.GetTypeByMetadataName("ILInspector.Metadata.AssemblyReferenceIdentity")!
            .GetMembers("TokenOrNull").OfType<IMethodSymbol>().Single();
        var tokenBody = ((MethodDeclarationSyntax)token.DeclaringSyntaxReferences.Single().GetSyntax()).Body!;
        if (tokenBody.Statements.Count != 4
            || SignatureOccurrenceSourceAudit.Compact(tokenBody.Statements[0]) != "if(handle.IsNil)returnnull;"
            || SignatureOccurrenceSourceAudit.Compact(tokenBody.Statements[1]) !=
                "if(!isPublicKey&&reader.GetBlobReader(handle).Length!=8){thrownewBadImageFormatException(" +
                "\"An assembly-reference public-key token must contain exactly 8 bytes.\");}"
            || SignatureOccurrenceSourceAudit.Compact(tokenBody.Statements[3]) !=
                "returnisPublicKey?ComputePublicKeyToken(bytes):Convert.ToHexString(bytes).ToLowerInvariant();")
            _violations.Add("The imported token/full-key branch no longer enforces nil or exactly eight token bytes.");
    }

    void CheckCoverage()
    {
        var requiresPayment = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var (method, body) in bodies)
        {
            foreach (var read in All(body).OfType<IInvocationOperation>().Where(call =>
                SignatureOccurrenceSourceAudit.Key(call.TargetMethod) is
                    "System.Reflection.Metadata.MetadataReader.GetString"
                    or "System.Reflection.Metadata.MetadataReader.GetBlobBytes"))
            {
                requiresPayment.Add(method);
                if (!_checkedReads.Contains(read.Syntax))
                    _violations.Add($"Raw materialization is outside the proved projection boundary: {read.Syntax}");
            }
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var (target, site) in calls.Where(call => requiresPayment.Contains(call.Target)))
            {
                var owner = compilation.GetSemanticModel(site.Syntax.SyntaxTree).GetEnclosingSymbol(site.Syntax.SpanStart)
                    as IMethodSymbol;
                if (owner is not null && owner.Name is not ("ReadAssemblyScope" or "ReadModuleScope"))
                    changed |= requiresPayment.Add(owner.OriginalDefinition);
            }
        } while (changed);
        foreach (var (target, site) in calls.Where(call => requiresPayment.Contains(call.Target)
            && call.Target.Name is not ("ReadAssemblyScope" or "ReadModuleScope")))
        {
            if (!_checkedCalls.Contains(site.Syntax))
                _violations.Add($"A new helper entry has no inherited charge proof: {target}: {site.Syntax}");
        }
    }

    internal static IEnumerable<IOperation> All(IOperation operation)
    {
        foreach (var child in operation.ChildOperations)
            foreach (var nested in All(child))
                yield return nested;
        yield return operation;
    }
}
