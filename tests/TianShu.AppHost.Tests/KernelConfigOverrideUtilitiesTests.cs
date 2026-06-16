using System.Text.Json;
using TianShu.AppHost.Configuration;

namespace TianShu.AppHost.Tests;

public sealed class KernelConfigOverrideUtilitiesTests
{
    [Fact]
    public void ConvertRawOverrideToJson_ShouldNormalizeScalarPayloads()
    {
        Assert.Equal("true", KernelConfigOverrideUtilities.ConvertRawOverrideToJson("true"));
        Assert.Equal("42", KernelConfigOverrideUtilities.ConvertRawOverrideToJson("42"));
        Assert.Equal("""{"enabled":true}""", KernelConfigOverrideUtilities.ConvertRawOverrideToJson("""json:{"enabled":true}"""));
        Assert.Equal(JsonSerializer.Serialize("plain-text"), KernelConfigOverrideUtilities.ConvertRawOverrideToJson("plain-text"));
    }

    [Fact]
    public void RebaseCliConfigOverrideRawValue_ShouldRebaseJsonPathPayloads()
    {
        var rebased = KernelConfigOverrideUtilities.RebaseCliConfigOverrideRawValue(
            "profiles.demo.js_repl_node_module_dirs",
            """json:["tools/node.exe","C:/node/global"]""",
            @"D:\Work\Project");

        var json = rebased["json:".Length..];
        var element = JsonDocument.Parse(json).RootElement;

        Assert.Equal(Path.GetFullPath(@"D:\Work\Project\tools\node.exe"), element[0].GetString());
        Assert.Equal(Path.GetFullPath(@"C:\node\global"), element[1].GetString());
    }
}
