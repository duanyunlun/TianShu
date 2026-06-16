using Tomlyn.Model;

namespace TianShu.AppHost.Tests;

public sealed class KernelPersistedSkillConfigUtilitiesTests
{
    [Fact]
    public void ReadPersistedConfigValues_ShouldFlattenSkillsConfigEntries()
    {
        var root = Directory.CreateTempSubdirectory("tianshu-skill-persist-read-");
        try
        {
            var skillDoc = Path.Combine(root.FullName, "skills", "demo", "SKILL.md");
            Directory.CreateDirectory(Path.GetDirectoryName(skillDoc)!);
            File.WriteAllText(skillDoc, "# demo");

            var configPath = Path.Combine(root.FullName, "config.toml");
            File.WriteAllText(
                configPath,
                $$"""
                model = "gpt-5"

                [[skills.config]]
                path = "{{skillDoc.Replace("\\", "/")}}"
                enabled = false
                """);

            var values = KernelPersistedSkillConfigUtilities.ReadPersistedConfigValues(configPath);

            Assert.Equal("\"gpt-5\"", values["model"]);
            Assert.Equal("false", values[KernelPersistedSkillConfigUtilities.ToSkillEnabledConfigKey(skillDoc)]);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ApplyPersistedConfigValues_ShouldWriteDisabledSkillEntry()
    {
        var skillDoc = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tianshu-skill-persist-write", "demo", "SKILL.md"));
        var root = new TomlTable();

        KernelPersistedSkillConfigUtilities.ApplyPersistedConfigValues(
            root,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KernelPersistedSkillConfigUtilities.ToSkillEnabledConfigKey(skillDoc)] = "false",
            });

        var skills = Assert.IsType<TomlTable>(root["skills"]);
        var config = Assert.IsType<TomlTableArray>(skills["config"]);
        var entry = Assert.Single(config);
        Assert.Equal(skillDoc, entry["path"]);
        Assert.Equal(false, entry["enabled"]);
    }

    [Fact]
    public void ApplyPersistedConfigValues_ShouldRemoveDisabledSkillEntry_WhenEnabledAgain()
    {
        var skillDoc = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tianshu-skill-persist-remove", "demo", "SKILL.md"));
        var root = new TomlTable
        {
            ["skills"] = new TomlTable
            {
                ["config"] = new TomlTableArray
                {
                    new TomlTable
                    {
                        ["path"] = skillDoc,
                        ["enabled"] = false,
                    },
                },
            },
        };

        KernelPersistedSkillConfigUtilities.ApplyPersistedConfigValues(
            root,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KernelPersistedSkillConfigUtilities.ToSkillEnabledConfigKey(skillDoc)] = "true",
            });

        Assert.False(root.ContainsKey("skills"));
    }
}
