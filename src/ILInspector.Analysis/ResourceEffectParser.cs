using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using InertText;

namespace ILInspector.Analysis;

public enum ResourceEffectWorkLimitKind
{
    StatementCharacters,
    StatementTokens,
    NestingDepth,
    StatementArguments,
    Models,
    ModelDeclarations,
    ModelStatements,
    ModelResourceKinds,
    CatalogStatements,
}

public sealed record ResourceEffectWorkLimits
{
    public ResourceEffectWorkLimits(
        int maxStatementCharacters = 4096,
        int maxStatementTokens = 256,
        int maxNestingDepth = 8,
        int maxStatementArguments = 16,
        int maxModels = 128,
        int maxDeclarationsPerModel = 512,
        int maxStatementsPerModel = 1024,
        int maxResourceKindsPerModel = 256,
        int maxCatalogStatements = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStatementCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStatementTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNestingDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStatementArguments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxModels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDeclarationsPerModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStatementsPerModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResourceKindsPerModel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCatalogStatements);
        MaxStatementCharacters = maxStatementCharacters;
        MaxStatementTokens = maxStatementTokens;
        MaxNestingDepth = maxNestingDepth;
        MaxStatementArguments = maxStatementArguments;
        MaxModels = maxModels;
        MaxDeclarationsPerModel = maxDeclarationsPerModel;
        MaxStatementsPerModel = maxStatementsPerModel;
        MaxResourceKindsPerModel = maxResourceKindsPerModel;
        MaxCatalogStatements = maxCatalogStatements;
    }

    public int MaxStatementCharacters { get; }
    public int MaxStatementTokens { get; }
    public int MaxNestingDepth { get; }
    public int MaxStatementArguments { get; }
    public int MaxModels { get; }
    public int MaxDeclarationsPerModel { get; }
    public int MaxStatementsPerModel { get; }
    public int MaxResourceKindsPerModel { get; }
    public int MaxCatalogStatements { get; }
}

public enum ResourceEffectDiagnosticKind
{
    EmptyStatement,
    UnknownVerb,
    UnknownArgument,
    DuplicateArgument,
    MissingArgument,
    ExpectedToken,
    InvalidIdentifier,
    InvalidTerm,
    InvalidLocation,
    InvalidCompletion,
    InvalidGuard,
    InvalidOutcomeTest,
    TrailingText,
    UnknownLanguage,
    InvalidTarget,
    UnboundGenericVariable,
    InconsistentGenericVariable,
    DuplicateLocalIdentity,
    UnresolvedField,
    UnresolvedOutcome,
    UnresolvedCallback,
    UnresolvedOperation,
    UnresolvedResourceKind,
    ResourceKindArityMismatch,
    ConflictingDeclaration,
}

public sealed record ResourceEffectDiagnostic
{
    public ResourceEffectDiagnostic(
        ResourceEffectDiagnosticKind kind,
        int offset,
        int length,
        InertString message)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Kind = kind;
        Offset = offset;
        Length = length;
        Message = message;
    }

    public ResourceEffectDiagnosticKind Kind { get; }
    public int Offset { get; }
    public int Length { get; }
    public InertString Message { get; }
}

public sealed record ResourceEffectStatementReceipt(
    int CharacterCount,
    int TokenCount,
    int NestingDepth,
    int ArgumentCount);

public abstract class ResourceEffectParseOutcome
{
    private protected ResourceEffectParseOutcome()
    {
    }

    public sealed class Parsed : ResourceEffectParseOutcome
    {
        internal Parsed(
            ResourceEffect effect,
            ResourceEffectStatementReceipt receipt)
        {
            Effect = effect;
            Receipt = receipt;
        }

        public ResourceEffect Effect { get; }
        public ResourceEffectStatementReceipt Receipt { get; }
    }

    public sealed class Rejected : ResourceEffectParseOutcome
    {
        internal Rejected(ResourceEffectDiagnostic diagnostic)
            => Diagnostic = diagnostic;

        public ResourceEffectDiagnostic Diagnostic { get; }
    }

    public sealed class WorkLimitExceeded : ResourceEffectParseOutcome
    {
        internal WorkLimitExceeded(
            ResourceEffectWorkLimitKind limitKind,
            int limit,
            int required,
            int offset)
        {
            LimitKind = limitKind;
            Limit = limit;
            Required = required;
            Offset = offset;
        }

        public ResourceEffectWorkLimitKind LimitKind { get; }
        public int Limit { get; }
        public int Required { get; }
        public int Offset { get; }
    }
}

public static class ResourceEffectStatementParser
{
    public static ResourceEffectParseOutcome Parse(
        string statement,
        ResourceEffectWorkLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(statement);
        limits ??= new ResourceEffectWorkLimits();
        if (statement.Length > limits.MaxStatementCharacters)
        {
            return new ResourceEffectParseOutcome.WorkLimitExceeded(
                ResourceEffectWorkLimitKind.StatementCharacters,
                limits.MaxStatementCharacters,
                statement.Length,
                limits.MaxStatementCharacters);
        }

        try
        {
            var parser = new Parser(statement, limits);
            ResourceEffect effect = parser.ParseStatement();
            return new ResourceEffectParseOutcome.Parsed(
                effect,
                new ResourceEffectStatementReceipt(
                    statement.Length,
                    parser.TokenCount,
                    parser.MaximumDepth,
                    parser.ArgumentCount));
        }
        catch (ParseFailure failure)
        {
            return new ResourceEffectParseOutcome.Rejected(
                new ResourceEffectDiagnostic(
                    failure.Kind,
                    failure.Offset,
                    failure.Length,
                    new InertString(TextPolicy.Field, failure.Message)));
        }
        catch (WorkLimitFailure failure)
        {
            return new ResourceEffectParseOutcome.WorkLimitExceeded(
                failure.Kind,
                failure.Limit,
                failure.Required,
                failure.Offset);
        }
    }

    sealed class Parser
    {
        static readonly ImmutableDictionary<string, VerbSchema> Schemas =
            new Dictionary<string, VerbSchema>(StringComparer.Ordinal)
            {
                ["resource"] = Schema(["kind"], ["value", "selector"]),
                ["authority"] = Schema(["kind", "target", "key"], []),
                ["acquire"] = Schema(
                    ["kind", "target", "when"],
                    ["correspondence", "lender"]),
                ["move"] = Schema(["source", "target", "when"], ["kind"]),
                ["consume"] = Schema(["source", "target"], ["kind"]),
                ["release"] = Schema(
                    ["source", "when"],
                    ["kind", "correspondence", "observation"]),
                ["borrow"] = Schema(
                    ["source", "target", "access", "scope"],
                    ["kind", "lender", "materialization"]),
                ["derive"] = Schema(
                    ["source", "target", "relation"],
                    ["guard"]),
                ["pass"] = Schema(["source", "target"], ["identity"]),
                ["independent"] = Schema(["source", "target"], []),
                ["callback"] = Schema(
                    ["delegate", "scope", "execution", "cardinality"],
                    []),
                ["accept"] = Schema(
                    ["source", "target", "when"],
                    ["kind", "order"]),
                ["operation"] = Schema(["boundary", "throws"], ["guard"]),
                ["outcome"] = Schema(["id", "source", "test"], []),
            }.ToImmutableDictionary(StringComparer.Ordinal);

        readonly string _text;
        readonly ResourceEffectWorkLimits _limits;
        int _position;
        int _depth;

        public Parser(string text, ResourceEffectWorkLimits limits)
        {
            _text = text;
            _limits = limits;
        }

        public int TokenCount { get; private set; }
        public int MaximumDepth { get; private set; }
        public int ArgumentCount { get; private set; }

        public ResourceEffect ParseStatement()
        {
            SkipWhiteSpace();
            if (AtEnd)
                Fail(ResourceEffectDiagnosticKind.EmptyStatement, 0, 0, "The statement is empty.");

            int verbOffset = _position;
            string verb = ReadSimpleIdentifier();
            if (!Schemas.TryGetValue(verb, out VerbSchema? schema)
                || schema is null)
            {
                Fail(
                    ResourceEffectDiagnosticKind.UnknownVerb,
                    verbOffset,
                    verb.Length,
                    "The effect verb is not defined by resource-effects/1.");
            }

            ExpectOpen('(');
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
            SkipWhiteSpace();
            if (!TryClose(')'))
            {
                while (true)
                {
                    int nameOffset = _position;
                    string name = ReadSimpleIdentifier();
                    if (!schema.All.Contains(name))
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.UnknownArgument,
                            nameOffset,
                            name.Length,
                            "The argument is not defined for this effect verb.");
                    }
                    if (values.ContainsKey(name))
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.DuplicateArgument,
                            nameOffset,
                            name.Length,
                            "An effect argument may occur only once.");
                    }
                    ArgumentCount++;
                    if (ArgumentCount > _limits.MaxStatementArguments)
                    {
                        Limit(
                            ResourceEffectWorkLimitKind.StatementArguments,
                            _limits.MaxStatementArguments,
                            ArgumentCount,
                            nameOffset);
                    }
                    Expect('=');
                    offsets.Add(name, nameOffset);
                    values.Add(name, ParseValue(verb, name));
                    SkipWhiteSpace();
                    if (TryClose(')'))
                        break;
                    Expect(',');
                }
            }

            foreach (string required in schema.Required)
            {
                if (!values.ContainsKey(required))
                {
                    Fail(
                        ResourceEffectDiagnosticKind.MissingArgument,
                        Math.Min(_position, _text.Length),
                        0,
                        "A required effect argument is missing.");
                }
            }

            SkipWhiteSpace();
            if (!AtEnd)
            {
                Fail(
                    ResourceEffectDiagnosticKind.TrailingText,
                    _position,
                    _text.Length - _position,
                    "Text follows the complete effect statement.");
            }

            return CreateEffect(verb, values, offsets);
        }

        object ParseValue(string verb, string name)
            => (verb, name) switch
            {
                ("resource", "kind")
                    or ("authority", "kind")
                    or ("acquire", "kind")
                    or ("move", "kind")
                    or ("consume", "kind")
                    or ("release", "kind")
                    or ("borrow", "kind")
                    or ("accept", "kind") => ParseResourceKind(),
                ("resource", "value") => ParseEnum(
                    new Dictionary<string, ResourceDeclaredValueKind>
                    {
                        ["declared-type"] = ResourceDeclaredValueKind.DeclaredType,
                        ["declared-field"] = ResourceDeclaredValueKind.DeclaredField,
                    }),
                ("resource", "selector")
                    or ("accept", "order")
                    or ("outcome", "id") => ParseLocalIdentity(),
                ("authority", "target")
                    or ("acquire", "target")
                    or ("acquire", "correspondence")
                    or ("acquire", "lender")
                    or ("move", "source")
                    or ("move", "target")
                    or ("consume", "source")
                    or ("consume", "target")
                    or ("release", "source")
                    or ("release", "correspondence")
                    or ("release", "observation")
                    or ("borrow", "source")
                    or ("borrow", "target")
                    or ("borrow", "lender")
                    or ("derive", "source")
                    or ("derive", "target")
                    or ("pass", "source")
                    or ("pass", "target")
                    or ("independent", "source")
                    or ("independent", "target")
                    or ("callback", "delegate")
                    or ("accept", "source")
                    or ("accept", "target")
                    or ("outcome", "source") => ParseLocation(),
                ("authority", "key") => ParseAuthorityKey(),
                ("acquire", "when")
                    or ("move", "when")
                    or ("release", "when")
                    or ("accept", "when") => ParseCompletion(),
                ("borrow", "access") => ParseEnum(
                    new Dictionary<string, ResourceBorrowAccess>
                    {
                        ["read"] = ResourceBorrowAccess.Read,
                        ["write"] = ResourceBorrowAccess.Write,
                    }),
                ("borrow", "scope") => ParseBorrowScope(),
                ("borrow", "materialization") => ParseEnum(
                    new Dictionary<string, ResourceBorrowMaterialization>
                    {
                        ["none"] = ResourceBorrowMaterialization.None,
                    }),
                ("derive", "relation") => ParseEnum(
                    new Dictionary<string, ResourceDerivationRelation>
                    {
                        ["same-value"] = ResourceDerivationRelation.SameValue,
                        ["alias"] = ResourceDerivationRelation.Alias,
                        ["borrow"] = ResourceDerivationRelation.Borrow,
                    }),
                ("derive", "guard")
                    or ("operation", "guard") => ParseGuard(),
                ("pass", "identity") => ParseEnum(
                    new Dictionary<string, ResourcePassIdentity>
                    {
                        ["preserve"] = ResourcePassIdentity.Preserve,
                    }),
                ("callback", "scope") => ParseCallbackScope(),
                ("callback", "execution") => ParseEnum(
                    new Dictionary<string, ResourceCallbackExecution>
                    {
                        ["synchronous"] = ResourceCallbackExecution.Synchronous,
                    }),
                ("callback", "cardinality") => ParseEnum(
                    new Dictionary<string, ResourceCallbackCardinality>
                    {
                        ["exactly-once"] = ResourceCallbackCardinality.ExactlyOnce,
                    }),
                ("operation", "boundary") => ParseEnum(
                    new Dictionary<string, ResourceOperationBoundary>
                    {
                        ["transparent"] = ResourceOperationBoundary.Transparent,
                        ["ordinary"] = ResourceOperationBoundary.Ordinary,
                    }),
                ("operation", "throws") => ParseEnum(
                    new Dictionary<string, ResourceOperationThrows>
                    {
                        ["never"] = ResourceOperationThrows.Never,
                        ["possible"] = ResourceOperationThrows.Possible,
                    }),
                ("outcome", "test") => ParseOutcomeTest(),
                _ => throw new InvalidOperationException("The finite schema is incomplete."),
            };

        ResourceEffect CreateEffect(
            string verb,
            Dictionary<string, object?> values,
            Dictionary<string, int> offsets)
        {
            T Get<T>(string name) where T : notnull => (T)values[name]!;
            T? Optional<T>(string name) where T : class
                => values.TryGetValue(name, out object? value) ? (T)value! : null;
            T? OptionalValue<T>(string name) where T : struct
                => values.TryGetValue(name, out object? value) ? (T)value! : null;

            switch (verb)
            {
                case "resource":
                    return new ResourceEffect.Resource(
                        Get<ResourceKindReference>("kind"),
                        OptionalValue<ResourceDeclaredValueKind>("value"),
                        OptionalValue<ResourceEffectLocalIdentity>("selector"));
                case "authority":
                    return new ResourceEffect.Authority(
                        Get<ResourceKindReference>("kind"),
                        Get<ResourceEffectLocation>("target"),
                        Get<ResourceAuthorityKey>("key"));
                case "acquire":
                    return new ResourceEffect.Acquire(
                        Get<ResourceKindReference>("kind"),
                        Get<ResourceEffectLocation>("target"),
                        Get<ResourceEffectCompletion>("when"),
                        Optional<ResourceEffectLocation>("correspondence"),
                        Optional<ResourceEffectLocation>("lender"));
                case "move":
                    return new ResourceEffect.Move(
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectLocation>("target"),
                        Get<ResourceEffectCompletion>("when"),
                        Optional<ResourceKindReference>("kind"));
                case "consume":
                {
                    ResourceEffectLocation consumeTarget =
                        Get<ResourceEffectLocation>("target");
                    if (consumeTarget is not ResourceEffectLocation.Operation)
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidLocation,
                            offsets["target"],
                            0,
                            "A consume target must be operation[N].");
                    }
                    return new ResourceEffect.Consume(
                        Get<ResourceEffectLocation>("source"),
                        (ResourceEffectLocation.Operation)consumeTarget,
                        Optional<ResourceKindReference>("kind"));
                }
                case "release":
                {
                    var when = Get<ResourceEffectCompletion>("when");
                    var observation = Optional<ResourceEffectLocation>("observation");
                    if ((when is ResourceEffectCompletion.SuccessfulAwait)
                        != (observation is not null))
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidTerm,
                            offsets.TryGetValue("observation", out int offset)
                                ? offset
                                : offsets["when"],
                            0,
                            "Release observation is required exactly for successful-await.");
                    }
                    if (observation is not null
                        && observation is not ResourceEffectLocation.Return)
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidLocation,
                            offsets["observation"],
                            0,
                            "A successful-await observation must identify the operation return.");
                    }
                    return new ResourceEffect.Release(
                        Get<ResourceEffectLocation>("source"),
                        when,
                        Optional<ResourceKindReference>("kind"),
                        Optional<ResourceEffectLocation>("correspondence"),
                        observation);
                }
                case "borrow":
                    return new ResourceEffect.Borrow(
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectLocation>("target"),
                        Get<ResourceBorrowAccess>("access"),
                        Get<ResourceBorrowScope>("scope"),
                        Optional<ResourceKindReference>("kind"),
                        Optional<ResourceEffectLocation>("lender"),
                        OptionalValue<ResourceBorrowMaterialization>("materialization"));
                case "derive":
                    return new ResourceEffect.Derive(
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectLocation>("target"),
                        Get<ResourceDerivationRelation>("relation"),
                        Optional<ResourceEffectGuard>("guard"));
                case "pass":
                    return new ResourceEffect.Pass(
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectLocation>("target"),
                        OptionalValue<ResourcePassIdentity>("identity"));
                case "independent":
                    return new ResourceEffect.Independent(
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectLocation>("target"));
                case "callback":
                {
                    ResourceEffectLocation callbackDelegate =
                        Get<ResourceEffectLocation>("delegate");
                    if (callbackDelegate is not ResourceEffectLocation.Parameter)
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidLocation,
                            offsets["delegate"],
                            0,
                            "A callback delegate must be parameter[N].");
                    }
                    return new ResourceEffect.Callback(
                        (ResourceEffectLocation.Parameter)callbackDelegate,
                        Get<ResourceBorrowScope.Callback>("scope"),
                        Get<ResourceCallbackExecution>("execution"),
                        Get<ResourceCallbackCardinality>("cardinality"));
                }
                case "accept":
                    return new ResourceEffect.Accept(
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectLocation>("target"),
                        Get<ResourceEffectCompletion>("when"),
                        Optional<ResourceKindReference>("kind"),
                        OptionalValue<ResourceEffectLocalIdentity>("order"));
                case "operation":
                    return new ResourceEffect.Operation(
                        Get<ResourceOperationBoundary>("boundary"),
                        Get<ResourceOperationThrows>("throws"),
                        Optional<ResourceEffectGuard>("guard"));
                case "outcome":
                    return new ResourceEffect.Outcome(
                        Get<ResourceEffectLocalIdentity>("id"),
                        Get<ResourceEffectLocation>("source"),
                        Get<ResourceEffectOutcomeTest>("test"));
                default:
                    throw new InvalidOperationException("The finite verb set is incomplete.");
            }
        }

        ResourceKindReference ParseResourceKind()
        {
            int offset = _position;
            string name = ReadQualifiedIdentifier();
            ResourceKindIdentity identity;
            try
            {
                identity = new ResourceKindIdentity(name);
            }
            catch (ArgumentException)
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidIdentifier,
                    offset,
                    name.Length,
                    "A resource kind must be a qualified ASCII identity.");
                throw;
            }

            SkipWhiteSpace();
            if (!TryOpen('<'))
                return new ResourceKindReference(identity);

            var variables = ImmutableArray.CreateBuilder<ResourceEffectGenericVariable>();
            while (true)
            {
                variables.Add(ParseGenericVariable());
                SkipWhiteSpace();
                if (TryClose('>'))
                    break;
                Expect(',');
            }
            return new ResourceKindReference(identity, variables.ToImmutable());
        }

        ResourceAuthorityKey ParseAuthorityKey()
        {
            int offset = _position;
            string kind = ReadSimpleIdentifier();
            if (kind == "value")
                return new ResourceAuthorityKey.Value();
            if (kind != "singleton")
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidTerm,
                    offset,
                    kind.Length,
                    "Authority key must be value or singleton[type-variable-list].");
            }
            ExpectOpen('[');
            var variables = ImmutableArray.CreateBuilder<ResourceEffectGenericVariable>();
            SkipWhiteSpace();
            if (TryClose(']'))
                return new ResourceAuthorityKey.Singleton([]);
            while (true)
            {
                variables.Add(ParseGenericVariable());
                SkipWhiteSpace();
                if (TryClose(']'))
                    break;
                Expect(',');
            }
            return new ResourceAuthorityKey.Singleton(variables.ToImmutable());
        }

        ResourceEffectGenericVariable ParseGenericVariable()
        {
            int offset = _position;
            string kind = ReadSimpleIdentifier();
            ResourceEffectGenericVariableKind variableKind = kind switch
            {
                "type" => ResourceEffectGenericVariableKind.Type,
                "method" => ResourceEffectGenericVariableKind.Method,
                _ => InvalidVariable(),
            };
            ExpectOpen('[');
            int index = ReadIndex();
            ExpectClose(']');
            return new ResourceEffectGenericVariable(variableKind, index);

            ResourceEffectGenericVariableKind InvalidVariable()
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidTerm,
                    offset,
                    kind.Length,
                    "A generic variable must be type[N] or method[N].");
                return default;
            }
        }

        ResourceEffectLocation ParseLocation()
        {
            int offset = _position;
            string root = ReadSimpleIdentifier();
            ResourceEffectLocation location;
            switch (root)
            {
                case "receiver":
                    location = new ResourceEffectLocation.Receiver();
                    break;
                case "return":
                    location = new ResourceEffectLocation.Return();
                    break;
                case "constructed":
                    return new ResourceEffectLocation.Constructed();
                case "parameter":
                    location = new ResourceEffectLocation.Parameter(ParseBracketedIndex());
                    break;
                case "operation":
                    return new ResourceEffectLocation.Operation(ParseBracketedIndex());
                case "callback":
                {
                    int callback = ParseBracketedIndex();
                    Expect('.');
                    string callbackPart = ReadSimpleIdentifier();
                    if (callbackPart == "return")
                        return new ResourceEffectLocation.CallbackReturn(callback);
                    if (callbackPart == "parameter")
                    {
                        return new ResourceEffectLocation.CallbackParameter(
                            callback,
                            ParseBracketedIndex());
                    }
                    Fail(
                        ResourceEffectDiagnosticKind.InvalidLocation,
                        offset,
                        _position - offset,
                        "A callback location must name parameter[N] or return.");
                    throw new InvalidOperationException();
                }
                default:
                    Fail(
                        ResourceEffectDiagnosticKind.InvalidLocation,
                        offset,
                        root.Length,
                        "The value location is not defined by resource-effects/1.");
                    throw new InvalidOperationException();
            }

            SkipWhiteSpace();
            if (!TryConsume('.'))
                return location;
            string field = ReadSimpleIdentifier();
            if (field != "field")
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidLocation,
                    offset,
                    _position - offset,
                    "Only a rooted field selector may follow a location.");
            }
            ExpectOpen('[');
            ResourceEffectLocalIdentity selector = ParseLocalIdentity();
            ExpectClose(']');
            return new ResourceEffectLocation.Field(location, selector);
        }

        ResourceEffectCompletion ParseCompletion()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            return value switch
            {
                "entry" => new ResourceEffectCompletion.Entry(),
                "normal-return" => new ResourceEffectCompletion.NormalReturn(),
                "exceptional-exit" => new ResourceEffectCompletion.ExceptionalExit(),
                "successful-await" => new ResourceEffectCompletion.SuccessfulAwait(),
                "outcome" => ParseOutcomeCompletion(),
                _ => InvalidCompletion(),
            };

            ResourceEffectCompletion ParseOutcomeCompletion()
            {
                ExpectOpen('[');
                ResourceEffectLocalIdentity identity = ParseLocalIdentity();
                ExpectClose(']');
                return new ResourceEffectCompletion.Outcome(identity);
            }

            ResourceEffectCompletion InvalidCompletion()
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidCompletion,
                    offset,
                    value.Length,
                    "The completion point is not defined by resource-effects/1.");
                throw new InvalidOperationException();
            }
        }

        ResourceBorrowScope ParseBorrowScope()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            if (value == "call")
                return new ResourceBorrowScope.Call();
            if (value == "callback")
                return ParseCallbackScopeAfterName();
            Fail(
                ResourceEffectDiagnosticKind.InvalidTerm,
                offset,
                value.Length,
                "Borrow scope must be call or callback[N].");
            throw new InvalidOperationException();
        }

        ResourceBorrowScope.Callback ParseCallbackScope()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            if (value != "callback")
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidTerm,
                    offset,
                    value.Length,
                    "Callback scope must be callback[N].");
            }
            return ParseCallbackScopeAfterName();
        }

        ResourceBorrowScope.Callback ParseCallbackScopeAfterName()
            => new(ParseBracketedIndex());

        ResourceEffectGuard ParseGuard()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            if (value != "exact-type")
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidGuard,
                    offset,
                    value.Length,
                    "The guard is not defined by resource-effects/1.");
            }
            ExpectOpen('[');
            ResourceEffectLocation subject = ParseLocation();
            Expect(';');
            ResourceEffectSignatureLocation expected = ParseSignatureLocation();
            ExpectClose(']');
            return new ResourceEffectGuard.ExactRuntimeType(subject, expected);
        }

        ResourceEffectSignatureLocation ParseSignatureLocation()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            return value switch
            {
                "signature-receiver" => new ResourceEffectSignatureLocation.Receiver(),
                "signature-return" => new ResourceEffectSignatureLocation.Return(),
                "signature-parameter" =>
                    new ResourceEffectSignatureLocation.Parameter(ParseBracketedIndex()),
                _ => InvalidSignatureLocation(),
            };

            ResourceEffectSignatureLocation InvalidSignatureLocation()
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidGuard,
                    offset,
                    value.Length,
                    "The signature location is not defined by resource-effects/1.");
                throw new InvalidOperationException();
            }
        }

        ResourceEffectOutcomeTest ParseOutcomeTest()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            switch (value)
            {
                case "null":
                    return new ResourceEffectOutcomeTest.Null();
                case "non-null":
                    return new ResourceEffectOutcomeTest.NonNull();
                case "bool":
                {
                    ExpectOpen('[');
                    string literal = ReadSimpleIdentifier();
                    bool parsed = literal switch
                    {
                        "true" => true,
                        "false" => false,
                        _ => InvalidBoolean(),
                    };
                    ExpectClose(']');
                    return new ResourceEffectOutcomeTest.Boolean(parsed);

                    bool InvalidBoolean()
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidOutcomeTest,
                            offset,
                            _position - offset,
                            "A boolean outcome test accepts only true or false.");
                        return default;
                    }
                }
                case "enum":
                    ExpectOpen('[');
                    string enumValue = ReadQualifiedIdentifier();
                    ExpectClose(']');
                    try
                    {
                        return new ResourceEffectOutcomeTest.Enum(enumValue);
                    }
                    catch (ArgumentException)
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidOutcomeTest,
                            offset,
                            _position - offset,
                            "An enum outcome test requires a valid ASCII identifier.");
                        throw;
                    }
                case "type":
                    ExpectOpen('[');
                    string selector = ReadQualifiedIdentifier();
                    ExpectClose(']');
                    try
                    {
                        return new ResourceEffectOutcomeTest.ExactType(selector);
                    }
                    catch (ArgumentException)
                    {
                        Fail(
                            ResourceEffectDiagnosticKind.InvalidOutcomeTest,
                            offset,
                            _position - offset,
                            "An exact constructed type test requires a qualified selector.");
                        throw;
                    }
                default:
                    Fail(
                        ResourceEffectDiagnosticKind.InvalidOutcomeTest,
                        offset,
                        value.Length,
                        "The outcome test is not defined by resource-effects/1.");
                    throw new InvalidOperationException();
            }
        }

        ResourceEffectLocalIdentity ParseLocalIdentity()
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            try
            {
                return new ResourceEffectLocalIdentity(value);
            }
            catch (ArgumentException)
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidIdentifier,
                    offset,
                    value.Length,
                    "A model-local identity must use bounded ASCII identifier spelling.");
                throw;
            }
        }

        T ParseEnum<T>(IReadOnlyDictionary<string, T> values)
            where T : struct, Enum
        {
            int offset = _position;
            string value = ReadSimpleIdentifier();
            if (values.TryGetValue(value, out T result))
                return result;
            Fail(
                ResourceEffectDiagnosticKind.InvalidTerm,
                offset,
                value.Length,
                "The argument value is outside the finite resource-effects/1 domain.");
            return default;
        }

        string ReadSimpleIdentifier()
            => ReadIdentifier(allowDot: false);

        string ReadQualifiedIdentifier()
            => ReadIdentifier(allowDot: true);

        string ReadIdentifier(bool allowDot)
        {
            SkipWhiteSpace();
            int start = _position;
            if (AtEnd
                || !IsIdentifierStart(_text[_position]))
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidIdentifier,
                    _position,
                    AtEnd ? 0 : 1,
                    "An ASCII identifier is required.");
            }
            _position++;
            while (!AtEnd && IsIdentifierPart(_text[_position], allowDot))
                _position++;
            Token(start);
            return _text[start.._position];
        }

        int ReadIndex()
        {
            SkipWhiteSpace();
            int start = _position;
            if (AtEnd || _text[_position] is < '0' or > '9')
            {
                Fail(
                    ResourceEffectDiagnosticKind.InvalidTerm,
                    _position,
                    AtEnd ? 0 : 1,
                    "A non-negative decimal index is required.");
            }
            int value = 0;
            while (!AtEnd && _text[_position] is >= '0' and <= '9')
            {
                int digit = _text[_position] - '0';
                if (value > (int.MaxValue - digit) / 10)
                {
                    Fail(
                        ResourceEffectDiagnosticKind.InvalidTerm,
                        start,
                        _position - start + 1,
                        "The decimal index exceeds Int32.");
                }
                value = value * 10 + digit;
                _position++;
            }
            Token(start);
            return value;
        }

        int ParseBracketedIndex()
        {
            ExpectOpen('[');
            int value = ReadIndex();
            ExpectClose(']');
            return value;
        }

        void Expect(char expected)
        {
            SkipWhiteSpace();
            int offset = _position;
            if (AtEnd || _text[_position] != expected)
            {
                Fail(
                    ResourceEffectDiagnosticKind.ExpectedToken,
                    offset,
                    AtEnd ? 0 : 1,
                    $"Expected '{expected}'.");
            }
            _position++;
            Token(offset);
        }

        void ExpectOpen(char expected)
        {
            Expect(expected);
            _depth++;
            MaximumDepth = Math.Max(MaximumDepth, _depth);
            if (_depth > _limits.MaxNestingDepth)
            {
                Limit(
                    ResourceEffectWorkLimitKind.NestingDepth,
                    _limits.MaxNestingDepth,
                    _depth,
                    _position - 1);
            }
        }

        bool TryOpen(char expected)
        {
            SkipWhiteSpace();
            if (AtEnd || _text[_position] != expected)
                return false;
            ExpectOpen(expected);
            return true;
        }

        void ExpectClose(char expected)
        {
            Expect(expected);
            _depth--;
        }

        bool TryClose(char expected)
        {
            SkipWhiteSpace();
            if (AtEnd || _text[_position] != expected)
                return false;
            ExpectClose(expected);
            return true;
        }

        bool TryConsume(char expected)
        {
            SkipWhiteSpace();
            if (AtEnd || _text[_position] != expected)
                return false;
            Expect(expected);
            return true;
        }

        void Token(int offset)
        {
            TokenCount++;
            if (TokenCount > _limits.MaxStatementTokens)
            {
                Limit(
                    ResourceEffectWorkLimitKind.StatementTokens,
                    _limits.MaxStatementTokens,
                    TokenCount,
                    offset);
            }
        }

        void SkipWhiteSpace()
        {
            while (!AtEnd && char.IsWhiteSpace(_text[_position]))
                _position++;
        }

        bool AtEnd => _position == _text.Length;

        static bool IsIdentifierStart(char value)
            => value is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or '_';

        static bool IsIdentifierPart(char value, bool allowDot)
            => IsIdentifierStart(value)
                || value is >= '0' and <= '9'
                || value == '-'
                || (allowDot && value == '.');

        static VerbSchema Schema(string[] required, string[] optional)
            => new(
                required.ToImmutableHashSet(StringComparer.Ordinal),
                required.Concat(optional).ToImmutableHashSet(StringComparer.Ordinal));

        [DoesNotReturn]
        static void Fail(
            ResourceEffectDiagnosticKind kind,
            int offset,
            int length,
            string message)
            => throw new ParseFailure(kind, offset, length, message);

        [DoesNotReturn]
        static void Limit(
            ResourceEffectWorkLimitKind kind,
            int limit,
            int required,
            int offset)
            => throw new WorkLimitFailure(kind, limit, required, offset);
    }

    sealed record VerbSchema(
        ImmutableHashSet<string> Required,
        ImmutableHashSet<string> All);

    sealed class ParseFailure(
        ResourceEffectDiagnosticKind kind,
        int offset,
        int length,
        string message)
        : Exception(message)
    {
        public ResourceEffectDiagnosticKind Kind { get; } = kind;
        public int Offset { get; } = offset;
        public int Length { get; } = length;
    }

    sealed class WorkLimitFailure(
        ResourceEffectWorkLimitKind kind,
        int limit,
        int required,
        int offset)
        : Exception
    {
        public ResourceEffectWorkLimitKind Kind { get; } = kind;
        public int Limit { get; } = limit;
        public int Required { get; } = required;
        public int Offset { get; } = offset;
    }
}
