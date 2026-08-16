using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class BoxedReferenceEqualityTests
{
    [Fact]
    public void StringReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.StringReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void DynamicReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DynamicReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void UserOperatorReferenceInequality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.UserReferenceNotEquals));

        Assert.Contains("return (object)left != (object)right;", output);
    }

    [Fact]
    public void InheritedUserOperatorReferenceEquality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DerivedReferenceEquals));

        Assert.Contains("return (object)left == (object)right;", output);
    }

    [Fact]
    public void CrossAssemblyInheritedOperatorReferenceInequality_PreservesObjectComparison()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.DelegateReferenceNotEquals));

        Assert.Contains("return (object)left != (object)right;", output);
    }

    [Fact]
    public void UserOperatorCall_RemainsAnOperatorCall()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.UserOperatorEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void OperatorFreeClassReferenceEquality_RemainsUncast()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.PlainReferenceEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void ObjectReferenceEquality_RemainsUncast()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.ObjectReferenceEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void CrossAssemblyOperatorFreeClassReferenceEquality_RemainsUncast()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.StringBuilderReferenceEquals));

        Assert.Contains("return left == right;", output);
        Assert.DoesNotContain("(object)left", output);
    }

    [Fact]
    public void NullComparison_RemainsAnIsPattern()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.StringIsNull));

        Assert.Contains("return value is null;", output);
        Assert.DoesNotContain("(object)value", output);
    }

    [Fact]
    public void BoxedGenericEquality_PreservesObjectCasts()
    {
        string output = Print(nameof(BoxedReferenceEqualitySpecimens.IndexOfReference));

        Assert.Contains("if ((object)item == (object)list[i])", output);
        Assert.DoesNotContain("if ((item) == (list[i]))", output);
    }

    [Fact]
    public void BoxedGenericInequality_PreservesObjectCasts()
    {
        var generic = TypeRef.MethodGenericParameter(0, "T");
        var comparison = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new Box(generic, new LoadArgument(0, "left", generic)),
            new Box(generic, new LoadArgument(1, "right", generic)));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [new Parameter("left", generic), new Parameter("right", generic)],
                HasThis: false,
                GenericParameterCount: 1),
            [],
            body);

        var output = CSharpPrinter.Print(function).Output!;

        Assert.Contains("return (object)left != (object)right;", output);
        Assert.DoesNotContain("return left != right;", output);
    }

    static string Print(string methodName)
    {
        using var context = new MetadataContext(TestAssemblyReferenceResolvers.TrustedPlatformAssemblies());
        using var source = MetadataSource.Open(
            typeof(BoxedReferenceEqualitySpecimens).Assembly.Location,
            context: context);
        var function = IrImporter.Import(
            source,
            typeof(BoxedReferenceEqualitySpecimens).FullName!,
            methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }
}

public static class BoxedReferenceEqualitySpecimens
{
    public static bool StringReferenceEquals(string left, string right)
        => (object)left == right;

    public static bool DynamicReferenceEquals(dynamic left, dynamic right)
        => (object)left == (object)right;

    public static bool UserReferenceNotEquals(UserEquality left, UserEquality right)
        => (object)left != right;

    public static bool DerivedReferenceEquals(DerivedEquality left, DerivedEquality right)
        => (object)left == right;

    public static bool DelegateReferenceNotEquals(EventHandler left, EventHandler right)
        => (object)left != right;

    public static bool UserOperatorEquals(UserEquality left, UserEquality right)
        => left == right;

    public static bool PlainReferenceEquals(PlainReference left, PlainReference right)
        => left == right;

    public static bool ObjectReferenceEquals(object left, object right)
        => left == right;

    public static bool StringBuilderReferenceEquals(
        System.Text.StringBuilder left,
        System.Text.StringBuilder right)
        => (object)left == right;

    public static bool StringIsNull(string value)
        => value is null;

    public static int IndexOfReference<T>(System.Collections.Generic.List<T> list, T item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if ((object?)item == (object?)list[i])
                return i;
        }

        return -1;
    }
}

public sealed class UserEquality
{
    public int Value { get; init; }

    public static bool operator ==(UserEquality? left, UserEquality? right)
        => left?.Value == right?.Value;

    public static bool operator !=(UserEquality? left, UserEquality? right)
        => !(left == right);

    public override bool Equals(object? obj)
        => obj is UserEquality other && this == other;

    public override int GetHashCode()
        => Value;
}

public sealed class PlainReference;

public class BaseEquality
{
    public static bool operator ==(BaseEquality? left, BaseEquality? right)
        => ReferenceEquals(left, right);

    public static bool operator !=(BaseEquality? left, BaseEquality? right)
        => !ReferenceEquals(left, right);

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj);

    public override int GetHashCode()
        => 0;
}

public sealed class DerivedEquality : BaseEquality;
