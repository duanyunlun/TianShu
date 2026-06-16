using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

[Collection("EnvironmentVariables")]
public sealed class KernelConfigRequirementsTests
{
    private const string SystemRootOverrideEnvironmentVariable = "TIANSHU_SYSTEM_CONFIG_ROOT";
    private const string CloudRequirementsTomlEnvironmentVariable = "TIANSHU_CLOUD_REQUIREMENTS_TOML";
    private const string AdminRequirementsTomlEnvironmentVariable = "TIANSHU_ADMIN_REQUIREMENTS_TOML";

    [Fact]
    public async Task RunAsync_ShouldReturnNullRequirementsWhenRequirementsTomlMissing()
    {
        using var scope = new TestDirectoryScope();
        using var response = await RunConfigRequirementsReadAsync(scope, requirementsToml: null);

        var requirements = response.RootElement
            .GetProperty("result")
            .GetProperty("requirements");

        Assert.Equal(JsonValueKind.Null, requirements.ValueKind);
    }

    [Fact]
    public async Task RunAsync_ShouldMapFeatureResidencyNetworkAndDisabledWebSearchMode()
    {
        using var scope = new TestDirectoryScope();
        const string requirementsToml = """
allowed_approval_policies = ["on-request", "never"]
allowed_sandbox_modes = ["workspace-write"]
allowed_web_search_modes = ["cached"]
enforce_residency = "us"

[features]
mcp_elicitations = true
experimental_ui = false

[experimental_network]
enabled = true
http_port = 8080
socks_port = 9090
allow_upstream_proxy = true
dangerously_allow_non_loopback_proxy = false
dangerously_allow_non_loopback_admin = false
dangerously_allow_all_unix_sockets = true
allowed_domains = ["example.com", "api.openai.com"]
denied_domains = ["blocked.example.com"]
allow_unix_sockets = ["/tmp/tianshu.sock"]
allow_local_binding = true
""";

        using var response = await RunConfigRequirementsReadAsync(scope, requirementsToml);
        var requirements = response.RootElement
            .GetProperty("result")
            .GetProperty("requirements");

        Assert.Equal("us", requirements.GetProperty("enforceResidency").GetString());

        var allowedWebSearchModes = requirements.GetProperty("allowedWebSearchModes").EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        Assert.Equal(new[] { "cached", "disabled" }, allowedWebSearchModes);

        var featureRequirements = requirements.GetProperty("featureRequirements");
        Assert.True(featureRequirements.GetProperty("mcp_elicitations").GetBoolean());
        Assert.False(featureRequirements.GetProperty("experimental_ui").GetBoolean());

        var network = requirements.GetProperty("network");
        Assert.True(network.GetProperty("enabled").GetBoolean());
        Assert.Equal((ushort)8080, network.GetProperty("httpPort").GetUInt16());
        Assert.Equal((ushort)9090, network.GetProperty("socksPort").GetUInt16());
        Assert.True(network.GetProperty("allowUpstreamProxy").GetBoolean());
        Assert.False(network.GetProperty("dangerouslyAllowNonLoopbackProxy").GetBoolean());
        Assert.False(network.GetProperty("dangerouslyAllowNonLoopbackAdmin").GetBoolean());
        Assert.True(network.GetProperty("dangerouslyAllowAllUnixSockets").GetBoolean());
        Assert.Equal(new[] { "example.com", "api.openai.com" }, network.GetProperty("allowedDomains").EnumerateArray().Select(static x => x.GetString()).Cast<string>());
        Assert.Equal(new[] { "blocked.example.com" }, network.GetProperty("deniedDomains").EnumerateArray().Select(static x => x.GetString()).Cast<string>());
        Assert.Equal(new[] { "/tmp/tianshu.sock" }, network.GetProperty("allowUnixSockets").EnumerateArray().Select(static x => x.GetString()).Cast<string>());
        Assert.True(network.GetProperty("allowLocalBinding").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ShouldSupportFeatureRequirementsAliasTable()
    {
        using var scope = new TestDirectoryScope();
        const string requirementsToml = """
[feature_requirements]
legacy_flag = true
""";

        using var response = await RunConfigRequirementsReadAsync(scope, requirementsToml);
        var featureRequirements = response.RootElement
            .GetProperty("result")
            .GetProperty("requirements")
            .GetProperty("featureRequirements");

        Assert.True(featureRequirements.GetProperty("legacy_flag").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ShouldMergeCloudAdminAndSystemRequirementsByPriority()
    {
        using var scope = new TestDirectoryScope();
        var originalSystemRoot = Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable);
        var originalCloudRequirements = Environment.GetEnvironmentVariable(CloudRequirementsTomlEnvironmentVariable);
        var originalAdminRequirements = Environment.GetEnvironmentVariable(AdminRequirementsTomlEnvironmentVariable);
        var systemRoot = Path.Combine(scope.Root, "system-tianshu");

        try
        {
            Directory.CreateDirectory(systemRoot);
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, systemRoot);
            Environment.SetEnvironmentVariable(
                CloudRequirementsTomlEnvironmentVariable,
                """
allowed_approval_policies = ["never"]
""");
            Environment.SetEnvironmentVariable(
                AdminRequirementsTomlEnvironmentVariable,
                """
allowed_approval_policies = ["on-request"]
allowed_sandbox_modes = ["workspace-write"]
enforce_residency = "us"
""");
            await File.WriteAllTextAsync(
                Path.Combine(systemRoot, "requirements.toml"),
                """
allowed_sandbox_modes = ["read-only"]
allowed_web_search_modes = ["cached"]

[feature_requirements]
personality = true
""");

            using var response = await RunConfigRequirementsReadAsync(scope, requirementsToml: null);
            var requirements = response.RootElement
                .GetProperty("result")
                .GetProperty("requirements");

            Assert.Equal(["never"], requirements.GetProperty("allowedApprovalPolicies").EnumerateArray().Select(static x => x.GetString()).Cast<string>());
            Assert.Equal(["workspace-write"], requirements.GetProperty("allowedSandboxModes").EnumerateArray().Select(static x => x.GetString()).Cast<string>());
            Assert.Equal(["cached", "disabled"], requirements.GetProperty("allowedWebSearchModes").EnumerateArray().Select(static x => x.GetString()).Cast<string>());
            Assert.Equal("us", requirements.GetProperty("enforceResidency").GetString());
            Assert.True(requirements.GetProperty("featureRequirements").GetProperty("personality").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, originalSystemRoot);
            Environment.SetEnvironmentVariable(CloudRequirementsTomlEnvironmentVariable, originalCloudRequirements);
            Environment.SetEnvironmentVariable(AdminRequirementsTomlEnvironmentVariable, originalAdminRequirements);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldIgnoreLegacyManagedConfigWhenBuildingRequirements()
    {
        using var scope = new TestDirectoryScope();
        var originalSystemRoot = Environment.GetEnvironmentVariable(SystemRootOverrideEnvironmentVariable);
        var systemRoot = Path.Combine(scope.Root, "system-tianshu");

        try
        {
            Directory.CreateDirectory(systemRoot);
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, systemRoot);
            await File.WriteAllTextAsync(
                Path.Combine(systemRoot, "managed_config.toml"),
                """
approval_policy = "never"
sandbox_mode = "workspace-write"
""");

            using var response = await RunConfigRequirementsReadAsync(scope, requirementsToml: null);
            var requirements = response.RootElement
                .GetProperty("result")
                .GetProperty("requirements");

            Assert.Equal(JsonValueKind.Null, requirements.ValueKind);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SystemRootOverrideEnvironmentVariable, originalSystemRoot);
        }
    }

    private static async Task<JsonDocument> RunConfigRequirementsReadAsync(TestDirectoryScope scope, string? requirementsToml)
    {
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", scope.TianShuHome);
            if (requirementsToml is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(scope.TianShuHome, "requirements.toml"), requirementsToml);
            }

            var input = """{"jsonrpc":"2.0","id":1,"method":"configRequirements/read","params":{}}""";
            var storePath = Path.Combine(scope.Root, "threads.json");
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            var responseLine = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(IsResponseIdOne);

            return JsonDocument.Parse(responseLine);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
        }
    }

    private static bool IsResponseIdOne(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.TryGetProperty("id", out var id)
               && id.ValueKind == JsonValueKind.Number
               && id.GetInt32() == 1;
    }

    private sealed class TestDirectoryScope : IDisposable
    {
        public TestDirectoryScope()
        {
            Root = Path.Combine(Path.GetTempPath(), "tianshu-kernel-config-requirements", Guid.NewGuid().ToString("N"));
            TianShuHome = Path.Combine(Root, ".tianshu-home");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(TianShuHome);
        }

        public string Root { get; }

        public string TianShuHome { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}

