using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TianShu.AppHost.Tools;

internal sealed record KernelArtifactsRuntimeOptions(
    string TianShuHome,
    string RuntimeVersion,
    string? CacheRoot = null,
    string? PreferredNodePath = null);

internal sealed record KernelArtifactCommandOutput(int? ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

internal sealed class KernelArtifactsRuntimeManager
{
    private const string DefaultCacheRootRelative = "packages/artifacts";
    internal const string PinnedRuntimeVersion = "2.4.0";
    private static readonly string[] TianShuAppProductNames =
    [
        "TianShu",
        "TianShu (Dev)",
        "TianShu (Agent)",
        "TianShu (Nightly)",
        "TianShu (Alpha)",
        "TianShu (Beta)",
    ];

    public static bool CanManageArtifactRuntime() => TryGetPlatformMoniker() is not null;

    public static string? ValidateSource(string source)
    {
        var trimmed = source?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "artifacts expects raw JavaScript source text (non-empty) authored against the preloaded TianShu artifact surface. Provide JS only, optionally with first-line `// tianshu-artifacts: timeout_ms=15000` or `// tianshu-artifact-tool: timeout_ms=15000`.";
        }

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return "artifacts expects raw JavaScript source, not markdown code fences. Resend plain JS only (optional first line `// tianshu-artifacts: ...` or `// tianshu-artifact-tool: ...`).";
        }

        try
        {
            using var json = JsonDocument.Parse(trimmed);
            if (json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.String)
            {
                return "artifacts is a freeform tool and expects raw JavaScript source authored against the preloaded TianShu artifact surface. Resend plain JS only (optional first line `// tianshu-artifacts: ...` or `// tianshu-artifact-tool: ...`); do not send JSON (`{\"code\":...}`), quoted code, or markdown fences.";
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static string FormatOutput(KernelArtifactCommandOutput output)
    {
        var stdout = (output.Stdout ?? string.Empty).Trim();
        var stderr = (output.Stderr ?? string.Empty).Trim();
        var sections = new List<string>
        {
            $"exit_code: {(output.ExitCode?.ToString() ?? "null")}",
        };
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            sections.Add($"stdout:{Environment.NewLine}{stdout}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            sections.Add($"stderr:{Environment.NewLine}{stderr}");
        }

        if (string.IsNullOrWhiteSpace(stdout)
            && string.IsNullOrWhiteSpace(stderr)
            && output.Success)
        {
            sections.Add("artifact JS completed successfully.");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    public async Task<KernelArtifactCommandOutput> ExecuteBuildAsync(
        KernelArtifactsExecutionRequest request,
        KernelArtifactsRuntimeOptions options,
        string cwd,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateSource(request.Source);
        if (validationError is not null)
        {
            return ErrorOutput(validationError);
        }

        try
        {
            var runtime = LoadInstalledRuntime(options);
            var jsRuntime = ResolveJsRuntime(runtime, options);
            var stagingDir = Path.Combine(Path.GetTempPath(), "tianshu-artifacts", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);
            try
            {
                var scriptPath = Path.Combine(stagingDir, "artifact-build.mjs");
                File.WriteAllText(scriptPath, BuildWrappedScript(request.Source), new UTF8Encoding(false));
                return await RunCommandAsync(jsRuntime, runtime, scriptPath, cwd, request.TimeoutMs, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDeleteDirectory(stagingDir);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ErrorOutput(ex.Message);
        }
    }

    internal static string ResolveDefaultCacheRoot(string tianShuHome)
        => Path.Combine(tianShuHome, DefaultCacheRootRelative);

    private static KernelInstalledArtifactRuntime LoadInstalledRuntime(KernelArtifactsRuntimeOptions options)
    {
        var platform = TryGetPlatformMoniker() ?? throw new InvalidOperationException("artifact runtime is unsupported on this platform");
        var cacheRoot = string.IsNullOrWhiteSpace(options.CacheRoot)
            ? ResolveDefaultCacheRoot(options.TianShuHome)
            : Path.GetFullPath(options.CacheRoot!);
        var installDir = Path.Combine(cacheRoot, options.RuntimeVersion, platform);
        if (!Directory.Exists(installDir))
        {
            throw new IOException($"artifact runtime {options.RuntimeVersion} is not installed at {installDir}");
        }

        var manifestPath = Path.Combine(installDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new IOException($"failed to read {manifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<KernelArtifactRuntimeManifest>(File.ReadAllText(manifestPath))
                       ?? throw new InvalidOperationException($"invalid artifact runtime manifest: {manifestPath}");
        var buildJsPath = ResolveRelativeRuntimePath(installDir, manifest.Entrypoints?.BuildJs?.RelativePath, "build_js");
        var renderCliPath = ResolveRelativeRuntimePath(installDir, manifest.Entrypoints?.RenderCli?.RelativePath, "render_cli");
        var nodePath = ResolveRelativeRuntimePathOrNull(installDir, manifest.Node?.RelativePath);
        if (!File.Exists(buildJsPath))
        {
            throw new IOException($"artifact runtime entrypoint is missing: {buildJsPath}");
        }

        if (!File.Exists(renderCliPath))
        {
            throw new IOException($"artifact runtime entrypoint is missing: {renderCliPath}");
        }

        return new KernelInstalledArtifactRuntime(
            installDir,
            manifest.RuntimeVersion ?? options.RuntimeVersion,
            nodePath,
            buildJsPath,
            renderCliPath);
    }

    private static KernelJsRuntime ResolveJsRuntime(KernelInstalledArtifactRuntime runtime, KernelArtifactsRuntimeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PreferredNodePath))
        {
            var preferred = Path.GetFullPath(options.PreferredNodePath!);
            if (File.Exists(preferred))
            {
                return new KernelJsRuntime(preferred, RequiresElectronRunAsNode: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(runtime.NodePath) && File.Exists(runtime.NodePath))
        {
            return new KernelJsRuntime(runtime.NodePath!, RequiresElectronRunAsNode: false);
        }

        var machineNode = FindExecutableOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node");
        if (machineNode is not null)
        {
            return new KernelJsRuntime(machineNode, RequiresElectronRunAsNode: false);
        }

        var machineElectron = FindExecutableOnPath(OperatingSystem.IsWindows() ? "electron.exe" : "electron");
        if (machineElectron is not null)
        {
            return new KernelJsRuntime(machineElectron, RequiresElectronRunAsNode: true);
        }

        foreach (var candidate in EnumerateTianShuAppRuntimeCandidates())
        {
            if (File.Exists(candidate))
            {
                return new KernelJsRuntime(candidate, RequiresElectronRunAsNode: true);
            }
        }

        throw new IOException("no Node-compatible runtime is available for artifacts");
    }

    private static async Task<KernelArtifactCommandOutput> RunCommandAsync(
        KernelJsRuntime jsRuntime,
        KernelInstalledArtifactRuntime runtime,
        string scriptPath,
        string cwd,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = jsRuntime.ExecutablePath,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.Environment["TIANSHU_ARTIFACT_BUILD_ENTRYPOINT"] = runtime.BuildJsPath;
        startInfo.Environment["TIANSHU_ARTIFACT_RENDER_ENTRYPOINT"] = runtime.RenderCliPath;
        if (jsRuntime.RequiresElectronRunAsNode)
        {
            startInfo.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return ErrorOutput("failed to spawn artifact command");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var effectiveTimeout = TimeSpan.FromMilliseconds(timeoutMs.GetValueOrDefault(30_000));
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            effectiveTimeout = TimeSpan.FromMilliseconds(30_000);
        }

        var completionTask = Task.WhenAll(waitTask, stdoutTask, stderrTask);
        var completed = await Task.WhenAny(
            completionTask,
            Task.Delay(effectiveTimeout, cancellationToken)).ConfigureAwait(false);

        if (completed != completionTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryKillProcess(process);
            var stdout = await AwaitQuietlyAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await AwaitQuietlyAsync(stderrTask).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(stderr))
            {
                stderr = $"artifact command timed out after {effectiveTimeout}";
            }
            else
            {
                stderr = $"{stderr}{Environment.NewLine}artifact command timed out after {effectiveTimeout}";
            }

            return new KernelArtifactCommandOutput(process.HasExited ? process.ExitCode : 1, stdout, stderr);
        }

        await completionTask.ConfigureAwait(false);
        return new KernelArtifactCommandOutput(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private static async Task<string> AwaitQuietlyAsync(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static KernelArtifactCommandOutput ErrorOutput(string stderr)
        => new(1, string.Empty, stderr);

    private static string ResolveRelativeRuntimePath(string rootDir, string? relativePath, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException($"artifact runtime manifest missing {fieldName} entrypoint");
        }

        return ResolveRelativeRuntimePathCore(rootDir, relativePath!);
    }

    private static string? ResolveRelativeRuntimePathOrNull(string rootDir, string? relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? null
            : ResolveRelativeRuntimePathCore(rootDir, relativePath!);
    }

    private static string ResolveRelativeRuntimePathCore(string rootDir, string relativePath)
    {
        var candidate = Path.GetFullPath(Path.Combine(rootDir, relativePath));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDir));
        if (!candidate.StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"artifact runtime path escapes install root: {relativePath}");
        }

        return candidate;
    }

    private static string BuildWrappedScript(string source)
    {
        var builder = new StringBuilder();
        builder.AppendLine("import { pathToFileURL } from \"node:url\";");
        builder.AppendLine("const artifactTool = await import(pathToFileURL(process.env.TIANSHU_ARTIFACT_BUILD_ENTRYPOINT).href);");
        builder.AppendLine("globalThis.artifactTool = artifactTool;");
        builder.AppendLine("globalThis.artifacts = artifactTool;");
        builder.AppendLine("for (const [name, value] of Object.entries(artifactTool)) {");
        builder.AppendLine("  if (name === \"default\" || Object.prototype.hasOwnProperty.call(globalThis, name)) {");
        builder.AppendLine("    continue;");
        builder.AppendLine("  }");
        builder.AppendLine("  globalThis[name] = value;");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine(source);
        return builder.ToString();
    }

    private static string? FindExecutableOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(segment, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTianShuAppRuntimeCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var roots = new List<string>();
            AddIfNotNull(roots, Environment.GetEnvironmentVariable("LOCALAPPDATA"), "Programs");
            AddIfNotNull(roots, Environment.GetEnvironmentVariable("ProgramFiles"));
            AddIfNotNull(roots, Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
            foreach (var root in roots)
            {
                foreach (var productName in TianShuAppProductNames)
                {
                    yield return Path.Combine(root, productName, $"{productName}.exe");
                }
            }

            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            var roots = new List<string> { "/Applications" };
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOME")))
            {
                roots.Add(Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "Applications"));
            }

            foreach (var root in roots)
            {
                foreach (var productName in TianShuAppProductNames)
                {
                    yield return Path.Combine(root, $"{productName}.app", "Contents", "MacOS", productName);
                }
            }

            yield break;
        }

        if (OperatingSystem.IsLinux())
        {
            foreach (var root in new[] { "/opt", "/usr/lib" })
            {
                foreach (var productName in TianShuAppProductNames)
                {
                    yield return Path.Combine(root, productName, productName);
                }
            }
        }
    }

    private static void AddIfNotNull(List<string> roots, string? root, params string[] suffixes)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        var path = root!;
        foreach (var suffix in suffixes)
        {
            path = Path.Combine(path, suffix);
        }

        roots.Add(path);
    }

    internal static string? TryGetPlatformMoniker()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "windows-x64",
                Architecture.Arm64 => "windows-arm64",
                _ => null,
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "darwin-x64",
                Architecture.Arm64 => "darwin-arm64",
                _ => null,
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => null,
            };
        }

        return null;
    }

    private sealed record KernelInstalledArtifactRuntime(
        string RootDir,
        string RuntimeVersion,
        string? NodePath,
        string BuildJsPath,
        string RenderCliPath);

    private sealed record KernelJsRuntime(string ExecutablePath, bool RequiresElectronRunAsNode);

    private sealed class KernelArtifactRuntimeManifest
    {
        [JsonPropertyName("runtime_version")]
        public string? RuntimeVersion { get; set; }

        [JsonPropertyName("node")]
        public KernelArtifactRuntimeNodeManifest? Node { get; set; }

        [JsonPropertyName("entrypoints")]
        public KernelArtifactRuntimeEntrypointsManifest? Entrypoints { get; set; }
    }

    private sealed class KernelArtifactRuntimeNodeManifest
    {
        [JsonPropertyName("relative_path")]
        public string? RelativePath { get; set; }
    }

    private sealed class KernelArtifactRuntimeEntrypointsManifest
    {
        [JsonPropertyName("build_js")]
        public KernelArtifactRuntimePathEntry? BuildJs { get; set; }

        [JsonPropertyName("render_cli")]
        public KernelArtifactRuntimePathEntry? RenderCli { get; set; }
    }

    private sealed class KernelArtifactRuntimePathEntry
    {
        [JsonPropertyName("relative_path")]
        public string? RelativePath { get; set; }
    }
}
