namespace IntrinsicCoreLibraryCallFixtures;

public static class RepeatedCall
{
    public static void CallTargetTwice()
    {
        Target.Api.Forward();
        Target.Api.Forward();
    }
}
