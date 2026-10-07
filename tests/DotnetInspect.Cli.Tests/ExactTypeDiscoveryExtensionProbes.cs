namespace DotnetInspect.Cli.Tests.ExactTypeDiscoveryExtensions;

// Receivers whose discovery depends on same-image extension admission.
public class ObsoleteExtensionTarget
{
}

public static class ObsoleteExtensionTargetExtensions
{
    [System.Obsolete]
    public static void Touch(this ObsoleteExtensionTarget target)
    {
    }
}

public class PropertyBlockTarget
{
}

public static class PropertyBlockTargetExtensions
{
    extension(PropertyBlockTarget target)
    {
        public int Value => 1;
    }
}

public interface IExtensionOnlyTarget
{
}

public static class ExtensionOnlyTargetExtensions
{
    public static int Measure(this IExtensionOnlyTarget target) => 1;
}
