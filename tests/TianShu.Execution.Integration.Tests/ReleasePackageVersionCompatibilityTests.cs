using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TianShu.Execution.Integration.Tests;

public sealed class ReleasePackageVersionCompatibilityTests
{
    [Fact]
    public void P31_2_OldReleasePackageManifest_ShouldValidateAndDiagnoseUnsupportedSchema()
    {
        var repoRoot = FindRepoRoot();
        var testRoot = Path.Combine(repoRoot, "artifacts", $"p31-version-compat-{Guid.NewGuid():N}");
        var packagesRoot = Path.Combine(testRoot, "packages");
        Directory.CreateDirectory(packagesRoot);

        try
        {
            var archiveName = "tianshu-0.5.0-win-x64.zip";
            var archivePath = Path.Combine(packagesRoot, archiveName);
            CreateMinimalReleaseArchive(archivePath, "tianshu-0.5.0-win-x64");
            var size = new FileInfo(archivePath).Length;
            var sha256 = ComputeSha256(archivePath);
            var manifestPath = Path.Combine(packagesRoot, "release-manifest.json");

            WriteReleaseManifest(manifestPath, schemaVersion: 1, archiveName, sha256, size);

            var relativePackagesRoot = Path.GetRelativePath(repoRoot, packagesRoot);
            var success = RunReleaseManifestScript(repoRoot, relativePackagesRoot, expectedExitCode: 0);
            Assert.Contains("release-manifest validation passed", success.StdOut, StringComparison.Ordinal);

            WriteReleaseManifest(manifestPath, schemaVersion: 99, archiveName, sha256, size);

            var failed = RunReleaseManifestScript(repoRoot, relativePackagesRoot, expectedExitCode: 1);
            Assert.Contains("Unsupported release-manifest schemaVersion", failed.StdErr + failed.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static void CreateMinimalReleaseArchive(string archivePath, string packageRootName)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        AddEntry(archive, $"{packageRootName}/README.md", "# TianShu compatibility fixture");
        AddEntry(archive, $"{packageRootName}/LICENSE", "Compatibility test fixture.");
        AddEntry(archive, $"{packageRootName}/VERSION.txt", "version=0.5.0");
        AddEntry(archive, $"{packageRootName}/tianshu.toml", "profile = \"default\"");
        AddEntry(archive, $"{packageRootName}/modules/model/provider-instances/default.toml", "# fixture provider module");
        AddEntry(archive, $"{packageRootName}/modules/model/route-sets/default.toml", "# fixture route set");
        AddEntry(archive, $"{packageRootName}/modules/model/protocol-rules/default.toml", "# fixture protocol rules");
        AddEntry(archive, $"{packageRootName}/bin/tianshu.exe", "fixture executable placeholder");
        AddEntry(archive, $"{packageRootName}/runtime/apphost/TianShu.AppHost.exe", "fixture apphost placeholder");
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void WriteReleaseManifest(
        string manifestPath,
        int schemaVersion,
        string archiveName,
        string sha256,
        long size)
    {
        var manifest = new
        {
            schemaVersion,
            version = "0.5.0",
            generatedAtUtc = "2026-01-01T00:00:00.0000000Z",
            configuration = "Release",
            runtimeIdentifiers = new[] { "win-x64" },
            layout = "portable-tianshu-home",
            publishSingleFile = false,
            publishTrimmed = false,
            selfContained = true,
            archives = new[]
            {
                new
                {
                    runtimeIdentifier = "win-x64",
                    assetName = archiveName,
                    relativePath = archiveName,
                    sha256,
                    sizeBytes = size,
                    layout = "portable-tianshu-home",
                    entryPath = "bin/tianshu.exe",
                    configPath = "tianshu.toml",
                    modulesPath = "modules",
                    appHostPath = "runtime/apphost/TianShu.AppHost.exe",
                    selfContained = true,
                    entryName = "tianshu.exe",
                },
            },
        };

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static ProcessResult RunReleaseManifestScript(string repoRoot, string packagesRoot, int expectedExitCode)
    {
        var scriptPath = Path.Combine(repoRoot, "tools", "Test-TianShuReleaseManifest.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-PackagesRoot");
        startInfo.ArgumentList.Add(packagesRoot);
        startInfo.ArgumentList.Add("-Version");
        startInfo.ArgumentList.Add("0.5.0");
        startInfo.ArgumentList.Add("-RuntimeIdentifiers");
        startInfo.ArgumentList.Add("win-x64");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 PowerShell。");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == expectedExitCode,
            $"Expected exit code {expectedExitCode}, actual {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        return new ProcessResult(stdout, stderr);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法从测试目录定位仓库根目录。");
    }

    private sealed record ProcessResult(string StdOut, string StdErr);
}
