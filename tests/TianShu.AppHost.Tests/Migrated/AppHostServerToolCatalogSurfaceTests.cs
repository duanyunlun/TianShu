using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.AppHost.Tests;

public sealed class AppHostServerToolCatalogSurfaceTests
{
    [Fact]
    public async Task RunAsync_ShouldExposeResolvedToolCatalog()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize("""{"id":1,"method":"tools/catalog/read","params":{"includeHidden":true}}"""));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, new KernelThreadStore(storePath));

            await server.RunAsync(CancellationToken.None);

            var response = writer
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .Single(static doc => IsResponseId(doc.RootElement, 1));
            using (response)
            {
                var items = response.RootElement.GetProperty("result").GetProperty("items").EnumerateArray().ToArray();
                var grepFiles = items.Single(static item => item.GetProperty("name").GetString() == "grep_files");
                var execCommand = items.Single(static item => item.GetProperty("name").GetString() == "exec_command");

                Assert.True(grepFiles.GetProperty("available").GetBoolean());
                Assert.True(grepFiles.GetProperty("modelVisible").GetBoolean());
                Assert.Equal("tianshu.tools.filesystem", grepFiles.GetProperty("implementationId").GetString());
                Assert.True(execCommand.GetProperty("available").GetBoolean());
                Assert.False(execCommand.GetProperty("modelVisible").GetBoolean());
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-tool-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool IsResponseId(JsonElement json, long id)
        => json.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.TryGetInt64(out var numericId)
           && numericId == id;
}
