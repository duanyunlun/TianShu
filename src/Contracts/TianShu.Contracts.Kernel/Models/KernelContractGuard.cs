using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Kernel;

internal static class KernelContractGuard
{
    public static IReadOnlyList<T> ListOrEmpty<T>(IReadOnlyList<T>? values) => values ?? Array.Empty<T>();

    public static MetadataBag MetadataOrEmpty(MetadataBag? metadata) => metadata ?? MetadataBag.Empty;

    public static T NotNull<T>(T? value, string paramName)
        where T : class
        => value ?? throw new ArgumentNullException(paramName);

    public static int NonNegative(int value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "值不能为负。");
        }

        return value;
    }

    public static long NonNegative(long value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "值不能为负。");
        }

        return value;
    }

    public static decimal NonNegative(decimal value, string paramName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "值不能为负。");
        }

        return value;
    }

    public static string RequiredText(string value, string paramName)
        => IdentifierGuard.AgainstNullOrWhiteSpace(value, paramName);
}
