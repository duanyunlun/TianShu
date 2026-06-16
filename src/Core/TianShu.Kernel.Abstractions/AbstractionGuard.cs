using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Abstractions;

internal static class AbstractionGuard
{
    public static string RequiredText(string value, string paramName)
        => IdentifierGuard.AgainstNullOrWhiteSpace(value, paramName);
}
