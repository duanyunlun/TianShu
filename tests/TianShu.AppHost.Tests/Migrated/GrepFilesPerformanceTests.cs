using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using Xunit.Abstractions;

namespace TianShu.AppHost.Tests;

public sealed class GrepFilesPerformanceTests
{
    private const string RunPerfTestsVariable = "TIANSHU_RUN_GREP_PERF";
    private const string IterationsVariable = "TIANSHU_GREP_PERF_ITERATIONS";
    private const string SyntheticFileCountVariable = "TIANSHU_GREP_PERF_SYNTHETIC_FILES";
    private const string ArtifactDirectoryVariable = "TIANSHU_GREP_PERF_ARTIFACT_DIR";
    private const string MaxRatioVariable = "TIANSHU_GREP_PERF_ASSERT_MAX_RATIO";
    private const int DefaultIterations = 5;
    private const int DefaultSyntheticFileCount = 5000;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly ITestOutputHelper output;

    public GrepFilesPerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task MeasureManagedAgainstOptionalRg_WhenExplicitlyEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunPerfTestsVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        var iterations = ReadPositiveInt(IterationsVariable, DefaultIterations);
        var syntheticFileCount = ReadPositiveInt(SyntheticFileCountVariable, DefaultSyntheticFileCount);
        var maxRatio = ReadPositiveDouble(MaxRatioVariable, 100d);
        var artifactDirectory = ResolveArtifactDirectory();
        Directory.CreateDirectory(artifactDirectory);

        var cases = new List<GrepFilesPerformanceCase>();
        var syntheticRoot = CreateSyntheticCorpus(syntheticFileCount);
        try
        {
            cases.Add(await MeasureCaseAsync(
                "synthetic-text-corpus",
                syntheticRoot,
                "TIANSHU_PERF_NEEDLE_[0-9]+",
                "*.txt",
                iterations,
                CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(syntheticRoot);
        }

        var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);
        if (repositoryRoot is not null)
        {
            cases.Add(await MeasureCaseAsync(
                "tianshu-repository-csharp",
                repositoryRoot,
                "TianShu",
                "*.cs",
                iterations,
                CancellationToken.None));
        }

        var summary = new GrepFilesPerformanceSummary(
            DateTimeOffset.UtcNow,
            Environment.MachineName,
            Environment.ProcessorCount,
            Environment.OSVersion.ToString(),
            iterations,
            syntheticFileCount,
            cases);
        var summaryPath = Path.Combine(artifactDirectory, "grep-files-performance-summary.json");
        await File.WriteAllTextAsync(
            summaryPath,
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }),
            Utf8NoBom);

        output.WriteLine($"grep_files performance summary: {summaryPath}");
        foreach (var measuredCase in cases)
        {
            output.WriteLine(
                $"{measuredCase.Name}: managed={measuredCase.Managed.MedianMilliseconds:n2} ms, " +
                $"rg={(measuredCase.Rg is null ? "unavailable" : measuredCase.Rg.MedianMilliseconds.ToString("n2") + " ms")}, " +
                $"ratio={(measuredCase.ManagedToRgRatio is null ? "n/a" : measuredCase.ManagedToRgRatio.Value.ToString("n2") + "x")}, " +
                $"matches={measuredCase.Managed.MatchCount}");
        }

        var excessiveRatios = cases
            .Where(static x => x.ManagedToRgRatio is not null)
            .Where(x => x.ManagedToRgRatio!.Value >= maxRatio)
            .Select(x => $"{x.Name}: {x.ManagedToRgRatio!.Value:n2}x")
            .ToArray();
        Assert.Empty(excessiveRatios);
    }

    private static async Task<GrepFilesPerformanceCase> MeasureCaseAsync(
        string name,
        string root,
        string pattern,
        string include,
        int iterations,
        CancellationToken cancellationToken)
    {
        var managed = await MeasureHandlerAsync(
            preferExternalRg: false,
            root,
            pattern,
            include,
            iterations,
            cancellationToken).ConfigureAwait(false);

        GrepFilesPerformanceMeasurement? rg = null;
        if (RgAvailable())
        {
            rg = await MeasureHandlerAsync(
                preferExternalRg: true,
                root,
                pattern,
                include,
                iterations,
                cancellationToken).ConfigureAwait(false);
        }

        double? ratio = rg is null || rg.MedianMilliseconds <= 0
            ? null
            : managed.MedianMilliseconds / rg.MedianMilliseconds;
        return new GrepFilesPerformanceCase(name, root, pattern, include, managed, rg, ratio);
    }

    private static async Task<GrepFilesPerformanceMeasurement> MeasureHandlerAsync(
        bool preferExternalRg,
        string root,
        string pattern,
        string include,
        int iterations,
        CancellationToken cancellationToken)
    {
        _ = preferExternalRg;
        var handler = ToolProviderTestAdapters.CreateFileSystemRuntimeHandler("grep_files");
        using var args = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            pattern,
            include,
            path = root.Replace('\\', '/'),
            limit = 100,
        }));

        var elapsed = new List<double>(iterations);
        string[] outputLines = [];
        for (var i = 0; i < iterations + 1; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await handler.ExecuteAsync(
                args.RootElement,
                new KernelToolCallContext("thread", "turn", root),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            Assert.True(result.Success, result.OutputText);
            outputLines = result.OutputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (i > 0)
            {
                elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        elapsed.Sort();
        return new GrepFilesPerformanceMeasurement(
            MedianMilliseconds: Median(elapsed),
            MinMilliseconds: elapsed[0],
            MaxMilliseconds: elapsed[^1],
            MatchCount: outputLines.Length);
    }

    private static string CreateSyntheticCorpus(int fileCount)
    {
        var root = Path.Combine(Path.GetTempPath(), "tianshu-grep-perf", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        for (var i = 0; i < fileCount; i++)
        {
            var directory = Path.Combine(root, "bucket-" + (i % 50).ToString("00"));
            Directory.CreateDirectory(directory);
            var needle = i % 31 == 0 ? $"TIANSHU_PERF_NEEDLE_{i}" : "ordinary content";
            var content = string.Join('\n',
                $"file={i}",
                "The quick brown fox jumps over the lazy dog.",
                "TianShu grep_files managed performance corpus.",
                needle,
                new string('x', 512));
            File.WriteAllText(Path.Combine(directory, $"file-{i:00000}.txt"), content, Utf8NoBom);
        }

        return root;
    }

    private static string ResolveArtifactDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory) ?? Environment.CurrentDirectory;
        return Path.Combine(
            repositoryRoot,
            "Test",
            "GrepFilesPerformance",
            "artifacts",
            DateTimeOffset.Now.ToString("yyyyMMdd-HHmmssfff"));
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool RgAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "rg",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");
            using var process = Process.Start(startInfo);
            return process is not null && process.WaitForExit(2000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static int ReadPositiveInt(string name, int defaultValue)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : defaultValue;
    }

    private static double ReadPositiveDouble(string name, double defaultValue)
    {
        return double.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : defaultValue;
    }

    private static double Median(IReadOnlyList<double> sortedValues)
    {
        var middle = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 0
            ? (sortedValues[middle - 1] + sortedValues[middle]) / 2d
            : sortedValues[middle];
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

        Directory.Delete(path, recursive: true);
    }

    private sealed record GrepFilesPerformanceSummary(
        DateTimeOffset MeasuredAt,
        string MachineName,
        int ProcessorCount,
        string OperatingSystem,
        int Iterations,
        int SyntheticFileCount,
        IReadOnlyList<GrepFilesPerformanceCase> Cases);

    private sealed record GrepFilesPerformanceCase(
        string Name,
        string Root,
        string Pattern,
        string Include,
        GrepFilesPerformanceMeasurement Managed,
        GrepFilesPerformanceMeasurement? Rg,
        double? ManagedToRgRatio);

    private sealed record GrepFilesPerformanceMeasurement(
        double MedianMilliseconds,
        double MinMilliseconds,
        double MaxMilliseconds,
        int MatchCount);
}
