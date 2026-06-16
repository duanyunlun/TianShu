using System.Runtime.InteropServices;
using System.Text.Json;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 文件系统 surface 共用的路径校验、平台标识与复制辅助原语。
/// Shared filesystem primitives for path validation, platform identification, and recursive copy flows.
/// </summary>
internal static class KernelFileSystemUtilities
{
    public static string ResolveInitializePlatformFamily()
        => OperatingSystem.IsWindows() ? "windows" : "unix";

    public static string ResolveInitializePlatformOs()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        return RuntimeInformation.OSDescription
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault()
            ?.ToLowerInvariant()
            ?? "unknown";
    }

    public static bool TryReadRequiredAbsolutePath(
        JsonElement @params,
        string propertyName,
        out string? path,
        out string? errorMessage)
    {
        path = null;
        errorMessage = null;

        var rawPath = KernelToolJsonHelpers.Normalize(KernelToolJsonHelpers.ReadString(@params, propertyName));
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            errorMessage = $"Invalid request: missing required {propertyName}";
            return false;
        }

        if (!Path.IsPathRooted(rawPath))
        {
            errorMessage = "Invalid request: AbsolutePathBuf deserialized without a base path";
            return false;
        }

        path = Path.GetFullPath(rawPath!);
        return true;
    }

    public static long GetFileSystemTimeUtc(string path, bool creationTime)
    {
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return new DateTimeOffset(creationTime ? info.CreationTimeUtc : info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        }

        var fileInfo = new FileInfo(path);
        return new DateTimeOffset(creationTime ? fileInfo.CreationTimeUtc : fileInfo.LastWriteTimeUtc).ToUnixTimeMilliseconds();
    }

    public static async Task CopyFileSystemEntryAsync(
        string sourcePath,
        string destinationPath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sourceDirectoryInfo = new DirectoryInfo(sourcePath);
        var sourceFileInfo = new FileInfo(sourcePath);
        sourceDirectoryInfo.Refresh();
        sourceFileInfo.Refresh();

        var sourceDirectoryExists = sourceDirectoryInfo.Exists;
        var sourceFileExists = sourceFileInfo.Exists;
        var directoryLinkTarget = sourceDirectoryInfo.LinkTarget;
        var fileLinkTarget = sourceFileInfo.LinkTarget;

        if (!sourceDirectoryExists
            && !sourceFileExists
            && string.IsNullOrWhiteSpace(directoryLinkTarget)
            && string.IsNullOrWhiteSpace(fileLinkTarget))
        {
            throw new IOException($"Could not find file or directory '{sourcePath}'.");
        }

        if (sourceDirectoryExists)
        {
            if (!recursive)
            {
                throw new InvalidOperationException("fs/copy requires recursive: true when sourcePath is a directory");
            }

            if (IsSameOrDescendantPath(destinationPath, sourcePath))
            {
                throw new InvalidOperationException("fs/copy cannot copy a directory to itself or one of its descendants");
            }

            await CopyDirectoryRecursiveAsync(sourceDirectoryInfo, destinationPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(fileLinkTarget))
        {
            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Delete(destinationPath);
            File.CreateSymbolicLink(destinationPath, fileLinkTarget);
            return;
        }

        if (!sourceFileExists)
        {
            throw new InvalidOperationException("fs/copy only supports regular files, directories, and symlinks");
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static async Task CopyDirectoryRecursiveAsync(
        DirectoryInfo sourceDirectory,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(sourceDirectory.LinkTarget))
        {
            var parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            Directory.Delete(destinationPath, recursive: true);
            Directory.CreateSymbolicLink(destinationPath, sourceDirectory.LinkTarget);
            return;
        }

        Directory.CreateDirectory(destinationPath);

        foreach (var entry in sourceDirectory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationChild = Path.Combine(destinationPath, entry.Name);
            switch (entry)
            {
                case DirectoryInfo childDirectory when childDirectory.Exists || !string.IsNullOrWhiteSpace(childDirectory.LinkTarget):
                    await CopyDirectoryRecursiveAsync(childDirectory, destinationChild, cancellationToken).ConfigureAwait(false);
                    break;

                case FileInfo childFile when childFile.Exists:
                    File.Copy(childFile.FullName, destinationChild, overwrite: true);
                    break;

                case FileInfo childLink when !string.IsNullOrWhiteSpace(childLink.LinkTarget):
                    File.Delete(destinationChild);
                    File.CreateSymbolicLink(destinationChild, childLink.LinkTarget);
                    break;

                default:
                    break;
            }
        }
    }

    private static bool IsSameOrDescendantPath(string candidatePath, string rootPath)
    {
        var normalizedCandidate = KernelPathUtilities.TryNormalizeForComparison(candidatePath)
            ?? Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        var normalizedRoot = KernelPathUtilities.TryNormalizeForComparison(rootPath)
            ?? Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(normalizedCandidate, normalizedRoot, comparison))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison);
    }
}
