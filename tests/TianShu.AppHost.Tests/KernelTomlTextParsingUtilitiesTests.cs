using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class KernelTomlTextParsingUtilitiesTests
{
    [Fact]
    public void TryParseTopLevelTomlScalar_ShouldIgnoreSectionEntries()
    {
        const string text = """
        model = "gpt-5"

        [features]
        model = false
        """;

        var parsed = KernelTomlTextParsingUtilities.TryParseTopLevelTomlScalar(text, "model", out var value);

        Assert.True(parsed);
        Assert.Equal("gpt-5", value);
    }

    [Fact]
    public void TryParseTomlStringArray_ShouldNormalizeDistinctValues()
    {
        const string text = """
        allowed_web_search_modes = ["cached", "live", "cached"]
        """;

        var parsed = KernelTomlTextParsingUtilities.TryParseTomlStringArray(text, "allowed_web_search_modes", out var values);

        Assert.True(parsed);
        Assert.Equal(["cached", "live"], values);
    }

    [Fact]
    public void ParseTomlSectionRawValues_ShouldSupportTypedReaders()
    {
        const string text = """
        [experimental_network]
        enabled = true
        http_port = 8080
        allowed_domains = ["example.com"]
        """;

        var section = KernelTomlTextParsingUtilities.ParseTomlSectionRawValues(text, "experimental_network");

        Assert.True(KernelTomlTextParsingUtilities.TryReadTomlSectionBoolean(section, "enabled"));
        Assert.Equal((ushort)8080, KernelTomlTextParsingUtilities.TryReadTomlSectionUInt16(section, "http_port"));
        Assert.Equal(["example.com"], KernelTomlTextParsingUtilities.TryReadTomlSectionStringArray(section, "allowed_domains"));
    }
}
