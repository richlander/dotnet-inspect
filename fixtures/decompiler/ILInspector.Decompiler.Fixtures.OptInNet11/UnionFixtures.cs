using System.Runtime.CompilerServices;

namespace ILInspector.Decompiler.Fixtures.OptInNet11;

public sealed class UnionCat
{
    public string Name { get; } = "cat";
}

public sealed class UnionDog
{
    public string Name { get; } = "dog";
}

public union PetUnion(UnionCat, UnionDog);

public union ResultUnion<T>(T, string);

[Union]
public sealed class ClassPetUnion : IUnion
{
    public ClassPetUnion(UnionCat value) => Value = value;
    public ClassPetUnion(UnionDog value) => Value = value;

    public object? Value { get; }
}

public static class UnionSwitchFixtures
{
    public static string Describe(PetUnion pet) => pet switch
    {
        UnionCat cat => cat.Name,
        UnionDog dog => dog.Name,
    };

    public static string DescribeValue(ResultUnion<int> result) => result switch
    {
        int value => value.ToString(),
        string message => message,
        null => "null",
    };

    public static string DescribeClass(ClassPetUnion pet) => pet switch
    {
        UnionCat cat => cat.Name,
        UnionDog dog => dog.Name,
    };
}
