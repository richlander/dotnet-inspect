using System.Collections.Immutable;
using System.Reflection;

using ILInspector.Findings;

namespace ILInspector.ILDiff.Tests;

public class FindingInspectionTopologyTests
{
    static readonly FindingSubject Subject = new("test", "test");
    static readonly FindingDescriptor Descriptor = new("test.item", "item");

    [Fact]
    public void FindingInspectionAbsenceKind_DeclaresCompleteClosedSet()
    {
        Assert.Equal(
            [
                FindingInspectionAbsenceKind.SubjectAbsent,
                FindingInspectionAbsenceKind.NoApplicableInput,
            ],
            Enum.GetValues<FindingInspectionAbsenceKind>());
    }

    [Fact]
    public void FindingInspectionAbsent_RequiresExplicitTypedKind()
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(FindingInspection<string>.Absent).GetConstructors());
        ParameterInfo kind = constructor.GetParameters()[0];

        Assert.Equal(typeof(FindingInspectionAbsenceKind), kind.ParameterType);
        Assert.False(kind.IsOptional);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FindingInspection<string>.Absent(
                (FindingInspectionAbsenceKind)int.MaxValue));
    }

    [Fact]
    public void FindingInspectionTransition_DerivesTheNineCellNonFailedMatrix()
    {
        FindingInspectionState[] states = Enum.GetValues<FindingInspectionState>();
        Assert.Equal(3, states.Length);

        var transitions = new HashSet<FindingInspectionTransition>();
        foreach (FindingInspectionState oldState in states)
        {
            foreach (FindingInspectionState newState in states)
            {
                FindingComparison<string>.Complete comparison = Complete(
                    FindingComparison.Compare(
                        Inspection(oldState),
                        Inspection(newState)));

                Assert.Equal(oldState, comparison.Transition.Old);
                Assert.Equal(newState, comparison.Transition.New);
                Assert.Equal(oldState == newState, comparison.Transition.IsSameTopology);
                Assert.Equal(oldState == newState, comparison.IsExact);
                transitions.Add(comparison.Transition);
            }
        }

        Assert.Equal(9, transitions.Count);
    }

    [Fact]
    public void FindingComparison_AbsentKindsControlTopologyExactness()
    {
        FindingComparison<string>.Complete comparison = Complete(
            FindingComparison.Compare(
                Inspection(FindingInspectionState.SubjectAbsent),
                Inspection(FindingInspectionState.NoApplicableInput)));

        Assert.Empty(comparison.Pairs);
        Assert.False(comparison.Transition.IsSameTopology);
        Assert.False(comparison.IsExact);
    }

    [Fact]
    public void FindingComparison_CompleteEmptyAndAbsentRemainTopologyDifferent()
    {
        FindingComparison<string>.Complete comparison = Complete(
            FindingComparison.Compare(
                Inspection(FindingInspectionState.Complete),
                Inspection(FindingInspectionState.SubjectAbsent)));

        Assert.Empty(comparison.OldAtoms);
        Assert.Empty(comparison.NewAtoms);
        Assert.Empty(comparison.Pairs);
        Assert.False(comparison.Transition.IsSameTopology);
        Assert.False(comparison.IsExact);
    }

    [Fact]
    public void FindingComparison_EqualAbsenceKindsIgnoreDetailText()
    {
        FindingComparison<string>.Complete comparison = Complete(
            FindingComparison.Compare(
                Inspection(FindingInspectionState.SubjectAbsent, "old detail"),
                Inspection(FindingInspectionState.SubjectAbsent, "new detail")));

        Assert.True(comparison.Transition.IsSameTopology);
        Assert.True(comparison.IsExact);
    }

    [Fact]
    public void FindingComparison_FailedHasNoCompletedTopology()
    {
        FindingInspection<string> failed = new FindingInspection<string>.Failed(
            new InspectionError(Subject, Descriptor, "failed"));

        FindingComparison<string> comparison = FindingComparison.Compare(
            failed,
            Inspection(FindingInspectionState.Complete));

        Assert.IsType<FindingComparison<string>.Failed>(comparison.Value);
        Assert.Null(typeof(FindingComparison<string>.Failed).GetProperty(
            nameof(FindingComparison<string>.Complete.Transition)));
    }

    [Fact]
    public void FindingCorrelation_PreservesBothAbsenceKinds()
    {
        var finding = new Finding<string>(
            Subject,
            Descriptor,
            new FindingKey("target"),
            "target");
        FindingCorrelation<string> correlation = FindingCorrelation<string>.Create(
            FindingCorrelationKey.From(finding),
            [
                new(
                    new FindingVersion("absent", "absent", 0),
                    Inspection(FindingInspectionState.SubjectAbsent, "missing")),
                new(
                    new FindingVersion("inapplicable", "inapplicable", 1),
                    Inspection(FindingInspectionState.NoApplicableInput, "bodyless")),
            ]);

        var subjectAbsent = Assert.IsType<FindingCorrelationPoint<string>.SubjectAbsent>(
            correlation.Timeline[0].Value);
        var noApplicableInput = Assert.IsType<FindingCorrelationPoint<string>.NoApplicableInput>(
            correlation.Timeline[1].Value);
        Assert.Equal("missing", subjectAbsent.Detail);
        Assert.Equal("bodyless", noApplicableInput.Detail);
    }

    [Fact]
    public void FindingInspectionTopology_DoesNotMakeObservationsPairDependent()
    {
        Type findingType = typeof(Finding<string>);
        IEnumerable<Type> publicContractTypes = findingType
            .GetConstructors()
            .SelectMany(static constructor => constructor.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .Concat(findingType.GetProperties().Select(static property => property.PropertyType));

        Assert.DoesNotContain(publicContractTypes, ContainsInspectionTopology);

        var finding = new Finding<string>(
            Subject,
            Descriptor,
            new FindingKey("independent"),
            "independent");
        Assert.Equal("independent", finding.Payload);
    }

    static FindingInspection<string> Inspection(
        FindingInspectionState state,
        string? detail = null)
        => state switch
        {
            FindingInspectionState.Complete => new FindingInspection<string>.Complete([]),
            FindingInspectionState.SubjectAbsent => new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.SubjectAbsent,
                detail),
            FindingInspectionState.NoApplicableInput => new FindingInspection<string>.Absent(
                FindingInspectionAbsenceKind.NoApplicableInput,
                detail),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    static FindingComparison<string>.Complete Complete(
        FindingComparison<string> comparison)
        => comparison switch
        {
            FindingComparison<string>.Complete complete => complete,
            FindingComparison<string>.Failed failed => throw new Xunit.Sdk.XunitException(
                failed.Failure),
        };

    static bool ContainsInspectionTopology(Type type)
    {
        if (type == typeof(FindingInspectionTransition))
        {
            return true;
        }

        if (!type.IsGenericType)
            return false;

        Type definition = type.GetGenericTypeDefinition();
        return definition == typeof(FindingInspection<>)
            || definition == typeof(FindingComparison<>)
            || type.GetGenericArguments().Any(ContainsInspectionTopology);
    }
}
