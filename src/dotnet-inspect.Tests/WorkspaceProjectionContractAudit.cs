using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

using DotnetInspector.Artifacts;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.Research;
using InertText;

using M = DotnetInspector.Queries.WorkspaceTypeResolutionProjectionManifest;

namespace DotnetInspector.Tests;

internal sealed class WorkspaceProjectionContractAudit
{
    internal static readonly BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    internal static readonly BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // These are the design's union inventory, not a second projector/arm list.
    internal static readonly IReadOnlyDictionary<Type, int> RequiredUnions = new Dictionary<Type, int>
    {
        [typeof(TypeResolutionOutcome)] = 6,
        [typeof(TypeResolutionFailure)] = 16,
        [typeof(TypeResolutionAmbiguity)] = 2,
        [typeof(ResolutionPlanRequest)] = 2,
        [typeof(TypeResolutionStart)] = 4,
        [typeof(AssemblyBindingTarget)] = 2,
        [typeof(AssemblyBindingOrigin)] = 2,
        [typeof(TypeDeclarationCandidate)] = 3,
        [typeof(AssemblyResolutionProvenance)] = 6,
    };

    internal readonly Dictionary<Type, WorkspaceProjectionSchema> Sources =
        M.Materializers.SelectMany(Flatten).ToDictionary(schema => schema.Source);
    internal readonly Dictionary<string, WorkspaceProjectionSchema> Projectors =
        M.Materializers.ToDictionary(schema => schema.Name);
    internal readonly HashSet<Type> ObservedTypes = [];
    internal readonly Dictionary<(Type Type, string Property), List<object?>> ObservedProperties = [];
    internal readonly Dictionary<(Type Type, string Member), List<object?>> ObservedDestinations = [];
    internal readonly Dictionary<Type, List<TypeResolutionOutcome>> Outcomes = [];
    readonly Dictionary<(WorkspaceProjectionOperationId, Type), List<(object Source, object Destination)>> _identities = [];
    readonly HashSet<(Type, string)> _observedSites = [];

    internal static IEnumerable<WorkspaceProjectionSchema> Flatten(WorkspaceProjectionSchema schema)
    {
        yield return schema;
        foreach (WorkspaceProjectionSchema arm in schema.Arms)
            foreach (WorkspaceProjectionSchema child in Flatten(arm))
                yield return child;
    }

    internal static Type[] Arms(Type union) => union.Assembly.GetTypes()
        .Where(type => type != union && union.IsAssignableFrom(type) && !type.IsAbstract)
        .OrderBy(type => type.FullName, StringComparer.Ordinal).ToArray();

    internal static void EqualSet<T>(IEnumerable<T> expected, IEnumerable<T> actual, string subject)
        where T : notnull
    {
        T[] left = expected.ToArray();
        T[] right = actual.ToArray();
        Assert.True(left.Length == left.Distinct().Count(), $"Duplicate discovered {subject}.");
        Assert.True(right.Length == right.Distinct().Count(), $"Duplicate manifest {subject}.");
        Assert.True(left.ToHashSet().SetEquals(right),
            $"{subject}: missing [{string.Join(", ", left.Except(right))}]; extra [{string.Join(", ", right.Except(left))}].");
    }

    internal void ValidateInventory()
    {
        EqualSet(
            typeof(M).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(field => typeof(WorkspaceProjectionSchema).IsAssignableFrom(field.FieldType))
                .Select(field => (WorkspaceProjectionSchema)field.GetValue(null)!),
            M.Materializers, "registered materializers");
        var discovered = new HashSet<Type>();
        DiscoverSource(typeof(TypeResolutionOutcome), discovered);
        DiscoverSource(typeof(AssemblyContextTypeResolutionResult), discovered);
        EqualSet(discovered, Sources.Keys, "source containers/unions/arms");
        EqualSet(RequiredUnions.Keys,
            discovered.Where(type => type.Namespace == "ILInspector.Metadata" && type.IsAbstract
                && type != typeof(AssemblyBindingLineage)), "Metadata unions");
        Assert.Empty(Sources[typeof(AssemblyBindingLineage)].Arms);

        foreach (WorkspaceProjectionSchema schema in Sources.Values)
        {
            if (schema.Source == typeof(ArtifactAcquisitionRegistration)
                || schema.Source == typeof(ImmutableArray<byte>))
            {
                Assert.All(schema.Properties, property => Assert.Null(property.Source));
            }
            else
            {
                EqualSet(schema.Source.GetProperties(PublicInstance).Select(property => property.Name),
                    schema.Properties.Where(property => property.Source is not null).Select(property => property.Source!),
                    $"{schema.Source.FullName} source properties");
            }
            ValidateDestinationClaims(schema);
            if (!schema.Arms.IsEmpty)
            {
                Type[] arms = Arms(schema.Source);
                Assert.All(arms, arm => Assert.True(arm.IsSealed));
                EqualSet(arms, schema.Arms.Select(arm => arm.Source), $"{schema.Source.Name} source arms");
                EqualSet(Arms(schema.Destination), schema.Arms.Select(arm => arm.Destination),
                    $"{schema.Destination.Name} destination arms");
                if (RequiredUnions.TryGetValue(schema.Source, out int count))
                {
                    Assert.Equal(count, arms.Length);
                    Assert.All(schema.Arms, arm => Assert.Equal(arm.Source.Name, arm.Destination.Name));
                }
            }

            foreach (WorkspaceProjectionProperty property in schema.Properties)
            {
                Assert.False(string.IsNullOrWhiteSpace(property.Rule));
                if (property.Disposition is WorkspaceProjectionDisposition.Project or WorkspaceProjectionDisposition.Contain)
                {
                    Assert.True(Projectors.TryGetValue(property.Rule, out var projector),
                        $"{schema.Source.Name}.{property.Source} has no named projector.");
                    Type type = schema.Source.GetProperty(property.Source!, PublicInstance)!.PropertyType;
                    Assert.Contains(Decompose(type, includeContainer: true), projector.Source.IsAssignableFrom);
                }
            }
        }
        foreach (WorkspaceProjectionIdentity identity in M.IdentityDerivations)
        {
            WorkspaceProjectionSchema schema = Sources[identity.Source];
            WorkspaceProjectionProperty derivation = Assert.Single(schema.Properties, property =>
                property.Disposition == WorkspaceProjectionDisposition.Derive
                && schema.Destination.GetProperty(property.Destination!)?.PropertyType == identity.Destination);
            Assert.Equal(identity.EqualityRule, derivation.Rule);
            EqualSet(PublishedMembers(identity.Destination).Select(member => member.Name),
                [identity.ParentProperty], $"{identity.Destination.Name} identity derivation");
            Assert.Equal(typeof(WorkspaceProjectionOperationId),
                identity.Destination.GetProperty(identity.ParentProperty)!.PropertyType);
            ValidateDestinationStorage(identity.Destination, new HashSet<string> { identity.ParentProperty });
        }

        var excluded = Sources.Values.SelectMany(schema => schema.Properties
            .Where(property => property.Disposition == WorkspaceProjectionDisposition.Exclude)
            .Select(property => (schema.Source, property.Source, property.Destination, property.Rule))).ToArray();
        Assert.Equal(
            [(typeof(ResolvedAssemblyReference), "OpenRead", (string?)null, M.OpenReadDenyRule)], excluded);
        Assert.True(typeof(Delegate).IsAssignableFrom(
            typeof(ResolvedAssemblyReference).GetProperty("OpenRead")!.PropertyType));
        Assert.Equal(
            [(typeof(ModuleFileReference), "Hash", "Hash", nameof(M.ModuleHash))],
            Sources.Values.SelectMany(schema => schema.Properties
                .Where(property => property.Disposition == WorkspaceProjectionDisposition.Contain)
                .Select(property => (schema.Source, property.Source, property.Destination, property.Rule))));

        var destinationTypes = new HashSet<Type>();
        DiscoverDestination(typeof(WorkspaceTypeResolutionEvidence), destinationTypes);
        var claimedTypes = Sources.Values.Select(schema => schema.Destination)
            .Concat(M.IdentityDerivations.Select(identity => identity.Destination))
            .Append(typeof(WorkspaceProjectionOperationId));
        EqualSet(destinationTypes, claimedTypes, "destination schema closure");
    }

    void DiscoverSource(Type type, HashSet<Type> discovered)
    {
        if (type.IsArray || type.IsGenericType)
        {
            foreach (Type element in Decompose(type))
                DiscoverSource(element, discovered);
            if (type.IsArray || type.Namespace is not ("ILInspector.Metadata" or "DotnetInspector.Queries"))
            {
                if (Sources.ContainsKey(type))
                    discovered.Add(type);
                return;
            }
        }
        if (type.IsPrimitive || type.IsEnum || M.PermittedValueLeaves.Contains(type))
            return;
        if (type == typeof(ArtifactAcquisitionRegistration))
        {
            discovered.Add(type);
            return;
        }
        Assert.True(type.Namespace is "ILInspector.Metadata" or "DotnetInspector.Queries",
            $"Unaccounted source leaf {type.FullName}.");
        if (!discovered.Add(type))
            return;
        foreach (PropertyInfo property in type.GetProperties(PublicInstance))
        {
            if (type == typeof(ResolvedAssemblyReference) && property.Name == "OpenRead")
                continue;
            DiscoverSource(property.PropertyType, discovered);
        }
        if (type.IsAbstract && type != typeof(AssemblyBindingLineage))
            foreach (Type arm in Arms(type))
                DiscoverSource(arm, discovered);
        if (type.BaseType is { IsAbstract: true } parent
            && parent != typeof(object) && parent.Namespace == "ILInspector.Metadata"
            && parent != typeof(AssemblyBindingLineage))
            DiscoverSource(parent, discovered);
    }

    void DiscoverDestination(Type type, HashSet<Type> discovered)
    {
        if (type.IsGenericType || type.IsArray)
        {
            foreach (Type element in Decompose(type))
                DiscoverDestination(element, discovered);
            if (type.IsArray || type.Namespace is not ("ILInspector.Metadata" or "DotnetInspector.Queries"))
                return;
        }
        if (type.IsEnum || M.PermittedValueLeaves.Contains(type))
            return;
        // A retained identity's own properties describe that exact owner-issued
        // value. Its private closure is still walked by the structural gate.
        if (M.RetainedOwnerCurrency.Contains(type) && type != typeof(AssemblyReferenceIdentity))
            return;
        if (!discovered.Add(type))
            return;
        foreach (MemberInfo member in PublishedMembers(type))
            DiscoverDestination(MemberType(member), discovered);
        if (type.IsAbstract)
            foreach (Type arm in Arms(type))
                DiscoverDestination(arm, discovered);
    }

    internal static IEnumerable<Type> Decompose(Type type, bool includeContainer = false)
    {
        if (includeContainer)
            yield return type;
        if (type.HasElementType)
        {
            Type element = type.GetElementType()!;
            yield return element;
            foreach (Type nested in Decompose(element))
                yield return nested;
        }
        foreach (Type argument in type.GenericTypeArguments)
        {
            yield return argument;
            foreach (Type nested in Decompose(argument))
                yield return nested;
        }
    }

    internal static MemberInfo[] PublishedMembers(Type type) =>
        [.. type.GetProperties(PublicInstance), .. type.GetFields(PublicInstance)];

    internal static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new InvalidOperationException(),
    };

    internal static object? ReadMember(MemberInfo member, object value) => member switch
    {
        PropertyInfo property => property.GetValue(value),
        FieldInfo field => field.GetValue(value),
        _ => throw new InvalidOperationException(),
    };

    internal static void ValidateDestinationClaims(WorkspaceProjectionSchema schema)
    {
        EqualSet(PublishedMembers(schema.Destination).Select(member => member.Name),
            schema.Properties.Where(property => property.Destination is not null)
                .Select(property => property.Destination!),
            $"{schema.Destination.FullName} destination members");
        ValidateDestinationStorage(schema.Destination,
            schema.Properties.Where(property => property.Destination is not null)
                .Select(property => property.Destination!).ToHashSet());
    }

    static void ValidateDestinationStorage(Type type, IReadOnlySet<string> claimed)
    {
        for (Type? current = type; current is not null && current != typeof(object)
            && current != typeof(ValueType); current = current.BaseType)
        foreach (FieldInfo field in current.GetFields(DeclaredInstance))
        {
            if (field.IsPublic)
            {
                Assert.Contains(field.Name, claimed);
                continue;
            }
            PropertyInfo? property = current.GetProperties(PublicInstance | BindingFlags.DeclaredOnly)
                .SingleOrDefault(property => field.Name == $"<{property.Name}>k__BackingField");
            Assert.True(property is not null
                && field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))
                && claimed.Contains(property.Name) && property.PropertyType == field.FieldType,
                $"Unclaimed destination storage {type.FullName}.{field.Name}.");
        }
    }

    internal void Compare(
        WorkspaceProjectionSchema schema, object? source, object? destination,
        WorkspaceProjectionContext context,
        IReadOnlyDictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> inputs)
    {
        if (source is null)
        {
            Assert.Null(destination);
            return;
        }
        Assert.NotNull(destination);
        if (source is IEnumerable sequence && source is not string
            && schema.Source != typeof(ImmutableArray<byte>))
        {
            object?[] left = sequence.Cast<object?>().ToArray();
            object?[] right = Assert.IsAssignableFrom<IEnumerable>(destination).Cast<object?>().ToArray();
            Assert.Equal(left.Length, right.Length);
            for (int index = 0; index < left.Length; index++)
                Compare(schema, left[index], right[index], context, inputs);
            return;
        }
        ObservedTypes.Add(schema.Source);
        if (!schema.Arms.IsEmpty)
        {
            WorkspaceProjectionSchema arm = Assert.Single(schema.Arms, arm => arm.Source == source.GetType());
            Assert.Equal(arm.Destination, destination.GetType());
            Compare(arm, source, destination, context, inputs);
            return;
        }
        Assert.True(schema.Source.IsInstanceOfType(source));
        Assert.Equal(schema.Destination, destination.GetType());
        foreach (WorkspaceProjectionProperty disposition in schema.Properties)
        {
            // Do not even obtain the excluded property's value.
            if (disposition.Disposition == WorkspaceProjectionDisposition.Exclude)
                continue;
            PropertyInfo? sourceProperty = disposition.Source is { } sourceName
                ? schema.Source.GetProperty(sourceName, PublicInstance) : null;
            object? value = sourceProperty?.GetValue(source);
            if (sourceProperty is not null)
            {
                var key = (schema.Source, sourceProperty.Name);
                if (!ObservedProperties.TryGetValue(key, out var values))
                    ObservedProperties.Add(key, values = []);
                values.Add(value);
            }
            MemberInfo member = Assert.Single(PublishedMembers(schema.Destination),
                member => member.Name == disposition.Destination);
            object? projected = ReadMember(member, destination);
            var destinationKey = (schema.Destination, member.Name);
            if (!ObservedDestinations.TryGetValue(destinationKey, out var destinations))
                ObservedDestinations.Add(destinationKey, destinations = []);
            destinations.Add(projected);
            switch (disposition.Disposition)
            {
                case WorkspaceProjectionDisposition.Copy:
                    Assert.Equal(value, projected);
                    if (value is DateTime timestamp)
                        Assert.Equal(timestamp.Kind, Assert.IsType<DateTime>(projected).Kind);
                    break;
                case WorkspaceProjectionDisposition.InertText:
                    CompareText(value, projected);
                    break;
                case WorkspaceProjectionDisposition.Project:
                case WorkspaceProjectionDisposition.Contain:
                    _observedSites.Add((schema.Source, disposition.Source!));
                    Compare(Projectors[disposition.Rule], value, projected, context, inputs);
                    break;
                case WorkspaceProjectionDisposition.OpaqueIdentity:
                    Assert.Equal(M.CatalogIdentityRule, disposition.Rule);
                    Assert.Same(context.Operation, projected);
                    CompareIdentity(schema.Source, source, destination, disposition.Rule, context);
                    break;
                case WorkspaceProjectionDisposition.Derive:
                    CompareDerivation(schema, disposition, source, destination, projected, context, inputs);
                    break;
                default:
                    Assert.Fail($"Unhandled disposition {disposition.Disposition}.");
                    break;
            }
        }
    }

    static void CompareText(object? source, object? destination)
    {
        if (source is null)
        {
            Assert.Null(destination);
            return;
        }
        if (source is IEnumerable sequence && source is not string)
        {
            object?[] left = sequence.Cast<object?>().ToArray();
            object?[] right = Assert.IsAssignableFrom<IEnumerable>(destination).Cast<object?>().ToArray();
            Assert.Equal(left.Length, right.Length);
            for (int index = 0; index < left.Length; index++)
                CompareText(left[index], right[index]);
            return;
        }
        string text = Assert.IsType<string>(source);
        InertString inert = Assert.IsType<InertString>(destination);
        var expected = new InertString(TextPolicy.Field, text);
        Assert.Equal(expected, inert);
        Assert.Equal(expected.Forms, inert.Forms);
        Assert.Equal(expected.Concerns, inert.Concerns);
        Assert.False(inert.IsTruncated);
    }

    void CompareDerivation(
        WorkspaceProjectionSchema schema, WorkspaceProjectionProperty disposition,
        object source, object destination, object? projected, WorkspaceProjectionContext context,
        IReadOnlyDictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> inputs)
    {
        if (disposition.Rule == M.ModuleHashRule)
        {
            var bytes = Assert.IsType<ImmutableArray<byte>>(source);
            switch (disposition.Destination)
            {
                case "ByteLength":
                    Assert.Equal(bytes.IsDefault ? 0 : bytes.Length, Assert.IsType<int>(projected));
                    break;
                case "Sha256":
                    CompareText(Convert.ToHexString(SHA256.HashData(bytes.AsSpan())), projected);
                    break;
                default:
                    Assert.Fail("An undeclared hash-summary field was added.");
                    break;
            }
        }
        else if (disposition.Destination == "Input")
        {
            var registration = source switch
            {
                AssemblyAcquisitionRegistration acquisition => acquisition,
                AssemblyContextTypeResolutionResult.Rejected rejected => rejected.Assembly.Registration,
                AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy unsupported =>
                    unsupported.Assembly.Registration,
                _ => throw new InvalidOperationException("Unknown sealed-input derivation."),
            };
            Assert.Same(inputs.GetValueOrDefault(registration), projected);
        }
        else
        {
            Assert.True(disposition.Rule is M.ReferenceIdentityRule or M.LineageIdentityRule);
            object identity = disposition.Destination == "Operation" ? destination : projected!;
            CompareIdentity(schema.Source, source, identity, disposition.Rule, context);
            PropertyInfo operation = identity.GetType().GetProperty("Operation")!;
            Assert.Same(context.Operation, operation.GetValue(identity));
        }
    }

    void CompareIdentity(Type sourceType, object source, object destination,
        string rule, WorkspaceProjectionContext context)
    {
        var key = (context.Operation, destination.GetType());
        if (!_identities.TryGetValue(key, out var values))
            _identities.Add(key, values = []);
        foreach (var previous in values)
        {
            bool equal = rule == M.ReferenceIdentityRule
                ? ReferenceEquals(previous.Source, source)
                : previous.Source.Equals(source);
            Assert.Equal(equal, ReferenceEquals(previous.Destination, destination));
        }
        values.Add((source, destination));
        Assert.True(sourceType.IsInstanceOfType(source));
    }

    internal WorkspaceMetadataEvidence.Outcome Observe(
        TypeResolutionContext resolution, TypeResolutionRequest request, WorkspaceProjectionContext context,
        IReadOnlyDictionary<AssemblyAcquisitionRegistration, QueryComparisonInputId> inputs)
    {
        TypeResolutionOutcome source = resolution.Resolve(request);
        if (!Outcomes.TryGetValue(source.GetType(), out var outcomes))
            Outcomes.Add(source.GetType(), outcomes = []);
        outcomes.Add(source);
        WorkspaceMetadataEvidence.Outcome projected = M.Outcome.Project(context, source);
        Compare(M.Outcome, source, projected, context, inputs);
        // Kind failures are produced by Resolve but intentionally internal to
        // its definition. Exercise the same Failure projector without forging
        // a public Rejected outcome.
        if (source is TypeResolutionOutcome.Resolved { Definition.KindResolutionFailure: { } failure })
            Compare(M.Failure, failure, M.Failure.Project(context, failure), context, inputs);
        return projected;
    }

    internal void ValidateOccurrenceCoverage(IReadOnlySet<Type> dormant)
    {
        var schemas = Sources.Values.Where(schema => schema.Arms.IsEmpty && !dormant.Contains(schema.Source));
        var expected = schemas
            .SelectMany(schema => schema.Properties
                .Where(property => property.Disposition is WorkspaceProjectionDisposition.Project
                    or WorkspaceProjectionDisposition.Contain)
                .Select(property => (schema.Source, property.Source!)));
        EqualSet(expected, _observedSites, "observed projection occurrence sites");
        EqualSet(schemas.SelectMany(schema => schema.Properties
            .Where(property => property.Source is not null && property.Disposition != WorkspaceProjectionDisposition.Exclude)
            .Select(property => (schema.Source, property.Source!))),
            ObservedProperties.Keys, "compared source properties");
        EqualSet(schemas.SelectMany(schema => schema.Properties
            .Where(property => property.Destination is not null)
            .Select(property => (schema.Destination, property.Destination!))),
            ObservedDestinations.Keys, "compared destination members");
    }
}

internal sealed class WorkspaceCapabilitySurfaceWalker
{
    readonly HashSet<Type> _visited = [];
    internal IReadOnlySet<Type> Visited => _visited;

    static readonly HashSet<Type> ImmutableContainers =
    [
        typeof(ImmutableArray<>), typeof(ImmutableList<>), typeof(ImmutableHashSet<>),
        typeof(ImmutableSortedSet<>), typeof(ImmutableQueue<>), typeof(ImmutableStack<>),
        typeof(ImmutableDictionary<,>), typeof(ImmutableSortedDictionary<,>),
    ];

    internal void Visit(Type type, string path)
    {
        Assert.False(type == typeof(object), $"Erased object at {path}.");
        Assert.False(type.IsInterface, $"Interface carrier {type} at {path}.");
        Assert.False(type.ContainsGenericParameters, $"Open generic {type} at {path}.");
        Assert.False(type.IsPointer || type.IsByRef || type.IsFunctionPointer, $"Pointer at {path}.");
        Assert.False(typeof(Delegate).IsAssignableFrom(type)
            || typeof(Stream).IsAssignableFrom(type)
            || typeof(Exception).IsAssignableFrom(type)
            || typeof(IDisposable).IsAssignableFrom(type)
            || typeof(IAsyncDisposable).IsAssignableFrom(type), $"Capability {type} at {path}.");
        Assert.False(type == typeof(System.Reflection.Metadata.MetadataReader)
            || type == typeof(System.Reflection.PortableExecutable.PEReader), $"Reader {type} at {path}.");
        Assert.False(type == typeof(AssemblyContextTypeResolutionResult)
            || typeof(AssemblyContextTypeResolutionResult).IsAssignableFrom(type), $"Query result {type} at {path}.");
        Assert.False(type.Namespace == "ILInspector.Metadata" && !type.IsValueType
            && type != typeof(AssemblyReferenceIdentity), $"Metadata reference {type} at {path}.");
        Assert.False((type.HasElementType || type.IsGenericType)
            && WorkspaceProjectionContractAudit.Decompose(type).Contains(typeof(byte)),
            $"Byte-bearing carrier {type} at {path}.");
        Assert.False(type.IsArray, $"Mutable array {type} at {path}.");
        if (!_visited.Add(type))
            return;
        if (type.IsPrimitive || type.IsEnum || M.PermittedValueLeaves.Contains(type))
            return;
        if (type.IsGenericType)
        {
            foreach (Type argument in type.GenericTypeArguments)
                Visit(argument, $"{path}<{argument.Name}>");
            if (type.GetGenericTypeDefinition() == typeof(Nullable<>)
                || ImmutableContainers.Contains(type.GetGenericTypeDefinition()))
                return;
        }

        bool queries = type.Namespace == "DotnetInspector.Queries"
            && (type.Assembly == typeof(WorkspaceTypeResolutionEvidence).Assembly
                || type.Assembly == typeof(QueryComparisonInputId).Assembly);
        bool ownerCurrency = M.RetainedOwnerCurrency.Contains(type);
        Assert.True(queries || ownerCurrency, $"Unlisted value {type} at {path}.");
        if (!type.IsValueType && !type.IsSealed)
        {
            Assert.True(type.IsAbstract, $"Non-sealed value {type} at {path}.");
            Assert.All(type.GetConstructors(WorkspaceProjectionContractAudit.DeclaredInstance),
                constructor => Assert.True(constructor.IsPrivate || constructor.IsFamilyAndAssembly,
                    $"Open union constructor {constructor} at {path}."));
            Type[] arms = WorkspaceProjectionContractAudit.Arms(type);
            Assert.NotEmpty(arms);
            foreach (Type arm in arms)
            {
                Assert.True(arm.IsSealed && !arm.ContainsGenericParameters, $"Open union arm {arm}.");
                Visit(arm, $"{path}/{arm.Name}");
            }
        }
        VisitMembers(type, path);
        if (type.BaseType is { } parent && parent != typeof(object) && parent != typeof(ValueType))
            Visit(parent, $"{path}/base");
    }

    internal void VisitMembers(Type type, string path)
    {
        foreach (FieldInfo field in type.GetFields(WorkspaceProjectionContractAudit.DeclaredInstance))
            Visit(field.FieldType, $"{path}.{field.Name}");
        foreach (PropertyInfo property in type.GetProperties(WorkspaceProjectionContractAudit.PublicInstance))
        {
            Assert.Empty(property.GetIndexParameters());
            Visit(property.PropertyType, $"{path}.{property.Name}");
        }
    }
}

internal static class WorkspaceProjectionIl
{
    static readonly IReadOnlyDictionary<ushort, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => unchecked((ushort)opcode.Value));

    internal static IEnumerable<MemberInfo> References(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            yield break;
        for (int offset = 0; offset < il.Length;)
        {
            ushort code = il[offset++];
            if (code == 0xfe)
                code = (ushort)(0xfe00 | il[offset++]);
            OpCode opcode = Opcodes[code];
            if (opcode.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)
            {
                MemberInfo? member = method.Module.ResolveMember(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (member is not null)
                    yield return member;
            }
            offset += opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
        }
    }

    internal static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(WorkspaceProjectionContractAudit.DeclaredInstance | BindingFlags.Static)
            .Cast<MethodBase>().Concat(type.GetConstructors(
                WorkspaceProjectionContractAudit.DeclaredInstance | BindingFlags.Static));

    internal static bool BelongsTo(Type? type, Type owner) =>
        type is not null && (type == owner || BelongsTo(type.DeclaringType, owner));

    internal static HashSet<MemberInfo> MaterializerReferences(WorkspaceProjectionSchema schema)
    {
        Delegate materialize = Assert.Single(schema.GetType()
            .GetFields(WorkspaceProjectionContractAudit.DeclaredInstance)
            .Select(field => field.GetValue(schema)).OfType<Delegate>());
        var references = new HashSet<MemberInfo>();
        var visited = new HashSet<MethodBase>();
        var pending = new Stack<MethodBase>();
        pending.Push(materialize.Method);
        while (pending.TryPop(out MethodBase? method))
        {
            if (!visited.Add(method))
                continue;
            foreach (MemberInfo member in References(method))
            {
                references.Add(member);
                if (member is MethodBase called && BelongsTo(called.DeclaringType, typeof(M)))
                    pending.Push(called);
            }
        }
        return references;
    }
}
