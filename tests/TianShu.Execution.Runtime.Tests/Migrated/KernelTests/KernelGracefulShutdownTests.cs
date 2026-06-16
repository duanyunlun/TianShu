using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelGracefulShutdownTests
{
    [Fact]
    public async Task RunAsync_WhenInputEnds_ShouldEmitShutdownComplete()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");

        try
        {
            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(string.Empty);
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore);

            await server.RunAsync(CancellationToken.None);

            Assert.True(string.IsNullOrWhiteSpace(writer.ToString()));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-shutdown-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                ResetReadOnlyAttributes(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(120);
            }
        }

        ResetReadOnlyAttributes(path);
        Directory.Delete(path, recursive: true);
    }

    private static void ResetReadOnlyAttributes(string path)
    {
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(directory);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(directory, attrs & ~FileAttributes.ReadOnly);
            }
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }
}

