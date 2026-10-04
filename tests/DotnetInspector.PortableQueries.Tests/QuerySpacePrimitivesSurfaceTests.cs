using System.Reflection;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.PortableQueries.Tests;

/// <summary>
/// Gates the assembly line that
/// <c>docs/design/query-space-library.md#two-assemblies-and-two-participation-tiers</c>
/// draws: <c>QuerySpace.Primitives</c> carries Declaration and Portable-request
/// types only. The content rule is judged over every exported type's members,
/// and the design's explicit disposition table is pinned by name so a type
/// cannot drift across the line silently.
/// </summary>
public sealed class QuerySpacePrimitivesSurfaceTests
{
    private static readonly Assembly Primitives =
        typeof(QuerySpaceDescriptor).Assembly;

    private static readonly Assembly Machinery =
        typeof(PortableQueryPayloadCodec).Assembly;

    [Fact]
    public void PrimitivesIsItsOwnAssembly()
    {
        Assert.Equal("QuerySpace.Primitives", Primitives.GetName().Name);
        Assert.Equal("QuerySpace", Machinery.GetName().Name);
        Assert.NotSame(Primitives, Machinery);
    }

    [Fact]
    public void PrimitivesCarriesOnlyDeclarationAndRequestTypes()
    {
        var violations = new List<string>();

        foreach (Type type in Primitives.GetExportedTypes())
        {
            if (typeof(Exception).IsAssignableFrom(type))
                violations.Add($"{type.FullName}: exception types are machinery");
            if (typeof(IDisposable).IsAssignableFrom(type))
                violations.Add($"{type.FullName}: holds a disposable resource");
            if (typeof(System.Collections.IEnumerator).IsAssignableFrom(type))
                violations.Add($"{type.FullName}: is an enumerator");
            if (type.GetInterfaces().Any(IsComparerContract))
                violations.Add($"{type.FullName}: implements a comparer");

            foreach (MemberInfo member in type.GetMembers(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            {
                foreach ((string role, Type carried) in CarriedTypes(member))
                {
                    string? reason = Forbidden(member, role, carried);
                    if (reason is not null)
                        violations.Add($"{type.FullName}.{member.Name} {role}: {reason}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(typeof(QuerySpaceDescriptor))]
    [InlineData(typeof(QuerySpaceRowFacetDescriptor))]
    [InlineData(typeof(QuerySpaceRequest))]
    [InlineData(typeof(QuerySpaceRowIntentAssociation))]
    [InlineData(typeof(QuerySpaceResourceIdentity))]
    [InlineData(typeof(QuerySpaceSourceBindingIdentity))]
    [InlineData(typeof(QuerySpaceRequestAssociationIdentity))]
    [InlineData(typeof(ProducerCapabilityIdentity))]
    [InlineData(typeof(ProducerCapabilityRequirement))]
    [InlineData(typeof(ProducerCapabilityProvisionDeclaration))]
    [InlineData(typeof(ProducerCapabilityCoverageDeclaration))]
    [InlineData(typeof(QueryOperationTermBinding))]
    [InlineData(typeof(QueryOperationOrderBinding))]
    [InlineData(typeof(QueryOperationTermCapability))]
    [InlineData(typeof(QueryOperationOrderCapability))]
    [InlineData(typeof(QueryOperationRouteCapabilities))]
    [InlineData(typeof(IQueryOperationRoute))]
    [InlineData(typeof(QueryOperationRegistry))]
    [InlineData(typeof(PortableQueryIntent))]
    [InlineData(typeof(PortableQueryModel))]
    [InlineData(typeof(PortableQueryRowSelection))]
    [InlineData(typeof(RowQueryIntent))]
    [InlineData(typeof(RowQueryVocabularyIdentity))]
    [InlineData(typeof(ResolvedRowQueryOrderIdentity))]
    [InlineData(typeof(RowSelectionIntent<>))]
    [InlineData(typeof(RowSelectionStageKind))]
    [InlineData(typeof(RowSequenceKey))]
    public void DispositionTablePlacesDeclarationsInPrimitives(Type type) =>
        Assert.Same(Primitives, type.Assembly);

    [Theory]
    [InlineData(typeof(PortableQueryIdentity))]
    [InlineData(typeof(PortableQueryPayloadCodec))]
    [InlineData(typeof(PortableQueryResolver))]
    [InlineData(typeof(PortableQueryVocabulary<,>))]
    [InlineData(typeof(PortableQueryKeyDeclaration<>))]
    [InlineData(typeof(PortableQueryBinding<>))]
    [InlineData(typeof(QueryOperationDefinition<,>))]
    [InlineData(typeof(QueryOperationRoute<,>))]
    [InlineData(typeof(QuerySpaceBinding))]
    [InlineData(typeof(QuerySpaceRequestSetPlanner))]
    [InlineData(typeof(ProducerCapabilitySatisfactionCandidate))]
    [InlineData(typeof(ProducerCapabilityPlanCandidate))]
    [InlineData(typeof(ProducerCapabilityPlanValidator))]
    [InlineData(typeof(RowQueryVocabulary<>))]
    [InlineData(typeof(RowQueryKey<>))]
    [InlineData(typeof(RowQueryNamedOrder<>))]
    [InlineData(typeof(RowQueryText))]
    [InlineData(typeof(RowQueryResolver))]
    [InlineData(typeof(ResolvedRowQueryPlan<>))]
    [InlineData(typeof(RowSelectionStage<>))]
    [InlineData(typeof(RowSelectionPlan<>))]
    [InlineData(typeof(RowSelectionExecutor))]
    [InlineData(typeof(RowSelectionResult<>))]
    [InlineData(typeof(NamedRowSequence<>))]
    public void DispositionTableKeepsBindersPlansAndMachineryInQuerySpace(Type type) =>
        Assert.Same(Machinery, type.Assembly);

    [Fact]
    public void PrimitivesReferencesOnlyThePlatform()
    {
        string[] references = Primitives
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.All(
            references,
            name => Assert.True(
                name == "netstandard"
                || name == "mscorlib"
                || name.StartsWith("System", StringComparison.Ordinal),
                $"QuerySpace.Primitives references '{name}'."));
    }

    private static IEnumerable<(string Role, Type Type)> CarriedTypes(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo field:
                yield return ("field", field.FieldType);
                break;
            case PropertyInfo property:
                yield return ("property", property.PropertyType);
                break;
            case ConstructorInfo constructor:
                foreach (ParameterInfo parameter in constructor.GetParameters())
                    yield return ($"parameter '{parameter.Name}'", parameter.ParameterType);
                break;
            case MethodInfo method when !method.IsSpecialName:
                yield return ("return", method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters())
                    yield return ($"parameter '{parameter.Name}'", parameter.ParameterType);
                break;
        }
    }

    private static string? Forbidden(MemberInfo member, string role, Type carried)
    {
        foreach (Type type in Flatten(carried))
        {
            if (typeof(Delegate).IsAssignableFrom(type))
                return $"carries delegate {Describe(type)}";

            if (IsComparerContract(type))
            {
                // A fixed comparer over text held as a datum is admitted
                // (PortableQueryModel.ScalarOrder); a comparer that is
                // produced, accepted, or parameterized over rows is a factory.
                bool datum = member is FieldInfo or PropertyInfo
                    && type.GetGenericArguments()[0] == typeof(string);
                if (!datum)
                    return $"carries comparer {Describe(type)}";
            }

            if (type == typeof(CancellationToken)
                || type == typeof(CancellationTokenSource)
                || typeof(System.Collections.IEnumerator).IsAssignableFrom(type)
                || typeof(Stream).IsAssignableFrom(type))
            {
                return $"carries execution resource {Describe(type)}";
            }
        }

        return null;
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (Type nested in Flatten(element))
                yield return nested;
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                foreach (Type nested in Flatten(argument))
                    yield return nested;
            }
        }
    }

    private static bool IsComparerContract(Type type)
    {
        if (!type.IsGenericType)
            return false;
        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(IComparer<>)
            || definition == typeof(IEqualityComparer<>)
            || definition == typeof(Comparer<>)
            || definition == typeof(EqualityComparer<>);
    }

    private static string Describe(Type type) =>
        type.IsGenericParameter ? type.Name : type.ToString();
}
