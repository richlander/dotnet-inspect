using System.Security.Cryptography.Pkcs;

namespace DotnetInspector.RestoredRidFixtures;

public static class SignedMessage
{
    public static SignedCms Create(byte[] content) => new(new ContentInfo(content));
}
