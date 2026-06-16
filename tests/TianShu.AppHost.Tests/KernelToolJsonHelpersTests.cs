using System.Text.Json;

namespace TianShu.AppHost.Tests;

public sealed class KernelToolJsonHelpersTests
{
    [Fact]
    public void ReadStringArray_ShouldNormalizeAndSkipEmptyEntries()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "roots": [" src ", "", "tests", 42]
            }
            """);

        var roots = KernelToolJsonHelpers.ReadStringArray(document.RootElement, "roots");

        Assert.Equal(["src", "tests", "42"], roots);
    }

    [Fact]
    public void TryReadInputArray_ShouldCloneItems()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "input": [
                { "type": "text", "text": "hello" }
              ]
            }
            """);

        var parsed = KernelToolJsonHelpers.TryReadInputArray(document.RootElement, out var items);

        Assert.True(parsed);
        var item = Assert.Single(items);
        Assert.Equal("text", item.GetProperty("type").GetString());
        Assert.Equal("hello", item.GetProperty("text").GetString());
    }

    [Fact]
    public void TryReadExtraSkillRoots_ShouldNormalizeAbsoluteRootsPerKnownCwd()
    {
        var cwd = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tianshu-skill-roots-cwd"));
        var extraRootA = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tianshu-skill-roots-a"));
        var extraRootB = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tianshu-skill-roots-b"));

        using var document = JsonDocument.Parse(
            $$"""
            {
              "perCwdExtraUserRoots": [
                {
                  "cwd": "{{cwd.Replace("\\", "/")}}",
                  "extraUserRoots": ["{{extraRootA.Replace("\\", "/")}}", "{{extraRootB.Replace("\\", "/")}}"]
                }
              ]
            }
            """);

        var result = KernelToolJsonHelpers.TryReadExtraSkillRoots(document.RootElement, [cwd], out var error);

        Assert.Null(error);
        Assert.True(result.TryGetValue(cwd, out var roots));
        Assert.Equal([extraRootA, extraRootB], roots);
    }

    [Fact]
    public void TryReadExtraSkillRoots_ShouldRejectRelativeRoots()
    {
        var cwd = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tianshu-skill-roots-invalid"));

        using var document = JsonDocument.Parse(
            $$"""
            {
              "perCwdExtraUserRoots": [
                {
                  "cwd": "{{cwd.Replace("\\", "/")}}",
                  "extraUserRoots": ["relative/skills"]
                }
              ]
            }
            """);

        var result = KernelToolJsonHelpers.TryReadExtraSkillRoots(document.RootElement, [cwd], out var error);

        Assert.Empty(result);
        Assert.Equal("skills/list perCwdExtraUserRoots extraUserRoots paths must be absolute: relative/skills", error);
    }
}
