using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;
using static TianShu.Tools.FileSystem.FileSystemToolHandlerImports;

namespace TianShu.Tools.FileSystem;

/// <summary>
/// 只读文件系统工具域 Provider。
/// Provider for the read-only filesystem tool domain.
/// </summary>
public sealed class FileSystemToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [FileSystemToolNames.ListDir] = ListDirToolHandler.DescriptorInstance,
            [FileSystemToolNames.ReadFile] = ReadFileToolHandler.DescriptorInstance,
            [FileSystemToolNames.GrepFiles] = GrepFilesToolHandler.DescriptorInstance,
            [FileSystemToolNames.Grep] = GrepToolHandler.DescriptorInstance,
            [FileSystemToolNames.Glob] = GlobToolHandler.DescriptorInstance,
        };

    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return Descriptors.Values.ToArray();
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        return toolKey switch
        {
            FileSystemToolNames.ListDir => new ListDirToolHandler(),
            FileSystemToolNames.ReadFile => new ReadFileToolHandler(),
            FileSystemToolNames.GrepFiles => new GrepFilesToolHandler(),
            FileSystemToolNames.Grep => new GrepToolHandler(),
            FileSystemToolNames.Glob => new GlobToolHandler(),
            _ => throw new InvalidOperationException($"Unknown filesystem tool: {toolKey}"),
        };
    }
}

internal static class FileSystemToolNames
{
    public const string ListDir = "list_dir";
    public const string ReadFile = "read_file";
    public const string GrepFiles = "grep_files";
    public const string Grep = "grep";
    public const string Glob = "glob";
    public const string ImplementationId = "tianshu.tools.filesystem";
}

internal sealed class ListDirToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "dir_path" },
        properties = new
        {
            dir_path = new { type = "string", description = "Absolute or working-directory-relative path to the directory to list." },
            offset = new { type = "integer", description = "The entry number to start listing from. Must be 1 or greater." },
            limit = new { type = "integer", description = "The maximum number of entries to return." },
            depth = new { type = "integer", description = "The maximum directory depth to traverse. Must be 1 or greater." },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = BuildDescriptor(
        FileSystemToolNames.ListDir,
        "List Directory",
        "Lists entries in a local directory with 1-indexed entry numbers and simple type labels.",
        InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var dirPath = Normalize(ReadString(request.Input, "dir_path")) ?? Normalize(ReadString(request.Input, "dirPath"));
        if (dirPath is null)
        {
            return Failure(request, "dir_path 不能为空。");
        }

        var resolvedDirPath = ResolvePath(context.WorkingDirectory, dirPath);
        if (!Directory.Exists(resolvedDirPath))
        {
            return Failure(request, $"failed to read directory: {resolvedDirPath}");
        }

        var offset = ReadInt(request.Input, "offset") ?? 1;
        var limit = ReadInt(request.Input, "limit") ?? 25;
        var depth = ReadInt(request.Input, "depth") ?? 2;
        if (offset <= 0)
        {
            return Failure(request, "offset must be a 1-indexed entry number");
        }

        if (limit <= 0)
        {
            return Failure(request, "limit must be greater than zero");
        }

        if (depth <= 0)
        {
            return Failure(request, "depth must be greater than zero");
        }

        try
        {
            var entries = await CollectEntriesAsync(resolvedDirPath, depth, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                return Success(request, $"Absolute path: {resolvedDirPath}");
            }

            entries.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.SortKey, b.SortKey));
            var startIndex = offset - 1;
            if (startIndex >= entries.Count)
            {
                return Failure(request, "offset exceeds directory entry count");
            }

            var cappedLimit = Math.Min(limit, entries.Count - startIndex);
            var endIndex = startIndex + cappedLimit;
            var lines = new List<string>(cappedLimit + 2) { $"Absolute path: {resolvedDirPath}" };
            for (var i = startIndex; i < endIndex; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lines.Add(entries[i].Format());
            }

            if (endIndex < entries.Count)
            {
                lines.Add($"More than {cappedLimit} entries found");
            }

            return Success(request, string.Join('\n', lines));
        }
        catch (Exception ex)
        {
            return Failure(request, $"failed to read directory: {ex.Message}");
        }
    }

    private static async Task<List<ListDirEntry>> CollectEntriesAsync(string rootPath, int depth, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var entries = new List<ListDirEntry>();
        var queue = new Queue<(string DirPath, string RelativePrefix, int RemainingDepth)>();
        queue.Enqueue((rootPath, string.Empty, depth));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dirPath, relativePrefix, remainingDepth) = queue.Dequeue();
            var children = Directory.GetFileSystemEntries(dirPath, "*", SearchOption.TopDirectoryOnly);
            var batch = new List<(string FullPath, string RelativePath, ListDirEntry Entry)>(children.Length);
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                var relativePath = string.IsNullOrWhiteSpace(relativePrefix) ? name : Path.Combine(relativePrefix, name);
                var displayDepth = string.IsNullOrWhiteSpace(relativePrefix)
                    ? 0
                    : relativePrefix.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Length;
                var entry = new ListDirEntry(NormalizeEntryName(relativePath), NormalizeEntryComponent(name), displayDepth, GetEntryKind(child));
                batch.Add((child, relativePath, entry));
            }

            batch.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Entry.SortKey, b.Entry.SortKey));
            foreach (var (fullPath, relativePath, entry) in batch)
            {
                if (entry.Kind == ListDirEntryKind.Directory && remainingDepth > 1)
                {
                    queue.Enqueue((fullPath, relativePath, remainingDepth - 1));
                }

                entries.Add(entry);
            }
        }

        return entries;
    }

    private static string NormalizeEntryName(string relativePath)
        => Truncate(relativePath.Replace('\\', '/'), 500);

    private static string NormalizeEntryComponent(string component)
        => Truncate(component, 500);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static ListDirEntryKind GetEntryKind(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
        {
            return ListDirEntryKind.Symlink;
        }

        return (attributes & FileAttributes.Directory) == FileAttributes.Directory
            ? ListDirEntryKind.Directory
            : ListDirEntryKind.File;
    }
}

internal sealed class ReadFileToolHandler : ITianShuToolHandler
{
    private const int MaxLineLength = 500;
    private const int TabWidth = 4;
    private const int ReadBufferSize = 16 * 1024;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "file_path" },
        properties = new
        {
            file_path = new { type = "string", description = "Absolute path to the file." },
            offset = new { type = "integer", description = "The line number to start reading from. Must be 1 or greater." },
            limit = new { type = "integer", description = "The maximum number of lines to return." },
            mode = new { type = "string", description = "Optional mode selector: \"slice\" for simple ranges (default) or \"indentation\" to expand around an anchor line." },
            indentation = new
            {
                type = "object",
                properties = new
                {
                    anchor_line = new { type = "integer", description = "Anchor line to center the indentation lookup on (defaults to offset)." },
                    max_levels = new { type = "integer", description = "How many parent indentation levels (smaller indents) to include." },
                    include_siblings = new { type = "boolean", description = "When true, include additional blocks that share the anchor indentation." },
                    include_header = new { type = "boolean", description = "Include doc comments or attributes directly above the selected block." },
                    max_lines = new { type = "integer", description = "Hard cap on the number of lines returned when using indentation mode." },
                },
                additionalProperties = false,
            },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = BuildDescriptor(
        FileSystemToolNames.ReadFile,
        "Read File",
        "Reads a local file with 1-indexed line numbers.",
        InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        var filePath = Normalize(ReadString(request.Input, "file_path"));
        if (filePath is null)
        {
            return Failure(request, "file_path 不能为空。");
        }

        if (!Path.IsPathFullyQualified(filePath))
        {
            return Failure(request, "file_path must be an absolute path");
        }

        var offset = ReadInt(request.Input, "offset") ?? 1;
        var limit = ReadInt(request.Input, "limit") ?? 2000;
        if (offset <= 0)
        {
            return Failure(request, "offset must be a 1-indexed line number");
        }

        if (limit <= 0)
        {
            return Failure(request, "limit must be greater than zero");
        }

        var mode = Normalize(ReadString(request.Input, "mode"));
        if (mode is null || string.Equals(mode, "slice", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadSliceAsync(request, filePath, offset, limit, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(mode, "indentation", StringComparison.OrdinalIgnoreCase))
        {
            var indentation = ReadIndentationArgs(request.Input);
            return await ReadIndentationAsync(request, filePath, offset, limit, indentation, cancellationToken).ConfigureAwait(false);
        }

        return Failure(request, "mode must be either \"slice\" or \"indentation\"");
    }

    private static async ValueTask<ToolInvocationResult> ReadSliceAsync(
        ToolInvocationRequest request,
        string filePath,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var collected = new List<string>(Math.Min(limit, 64));
            var seen = await VisitLinesAsync(
                filePath,
                (lineNumber, lineBytes) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (lineNumber < offset)
                    {
                        return true;
                    }

                    var decoded = Utf8.GetString(lineBytes.Span);
                    collected.Add($"L{lineNumber}: {FormatDecodedLine(decoded)}");
                    return collected.Count < limit;
                },
                cancellationToken).ConfigureAwait(false);

            if (seen < offset)
            {
                return Failure(request, "offset exceeds file length");
            }

            return Success(request, string.Join('\n', collected));
        }
        catch (Exception ex)
        {
            return Failure(request, $"failed to read file: {ex.Message}");
        }
    }

    private static IndentationArgs ReadIndentationArgs(StructuredValue input)
    {
        if (!input.TryGetProperty("indentation", out var indentationElement)
            || indentationElement is null
            || indentationElement.Kind != StructuredValueKind.Object)
        {
            return new IndentationArgs(AnchorLine: null, MaxLevels: 0, IncludeSiblings: false, IncludeHeader: true, MaxLines: null);
        }

        var anchorLine = ReadInt(indentationElement, "anchor_line");
        var maxLevels = ReadInt(indentationElement, "max_levels") ?? 0;
        var includeSiblings = ReadBool(indentationElement, "include_siblings") ?? false;
        var includeHeader = ReadBool(indentationElement, "include_header") ?? true;
        var maxLines = ReadInt(indentationElement, "max_lines");

        return new IndentationArgs(
            anchorLine,
            Math.Max(0, maxLevels),
            includeSiblings,
            includeHeader,
            maxLines);
    }

    private static async ValueTask<ToolInvocationResult> ReadIndentationAsync(
        ToolInvocationRequest request,
        string filePath,
        int offset,
        int limit,
        IndentationArgs options,
        CancellationToken cancellationToken)
    {
        var anchorLine = options.AnchorLine ?? offset;
        if (anchorLine <= 0)
        {
            return Failure(request, "anchor_line must be a 1-indexed line number");
        }

        var guardLimit = options.MaxLines ?? limit;
        if (guardLimit <= 0)
        {
            return Failure(request, "max_lines must be greater than zero");
        }

        List<LineRecord> collected;
        try
        {
            collected = await CollectFileLinesAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Failure(request, $"failed to read file: {ex.Message}");
        }

        if (collected.Count == 0 || anchorLine > collected.Count)
        {
            return Failure(request, "anchor_line exceeds file length");
        }

        var anchorIndex = anchorLine - 1;
        var effectiveIndents = ComputeEffectiveIndents(collected);
        var anchorIndent = effectiveIndents[anchorIndex];

        var minIndent = options.MaxLevels == 0
            ? 0
            : Math.Max(0, anchorIndent - options.MaxLevels * TabWidth);

        var finalLimit = Math.Min(Math.Min(limit, guardLimit), collected.Count);
        if (finalLimit == 1)
        {
            var record = collected[anchorIndex];
            return Success(request, $"L{record.Number}: {record.Display}");
        }

        var i = anchorIndex - 1;
        var j = anchorIndex + 1;
        var iCounterMinIndent = 0;
        var jCounterMinIndent = 0;

        var output = new LinkedList<LineRecord>();
        output.AddLast(collected[anchorIndex]);

        while (output.Count < finalLimit)
        {
            var progressed = 0;

            if (i >= 0)
            {
                var iu = i;
                if (effectiveIndents[iu] >= minIndent)
                {
                    output.AddFirst(collected[iu]);
                    progressed++;
                    i--;

                    if (effectiveIndents[iu] == minIndent && !options.IncludeSiblings)
                    {
                        var allowHeaderComment = options.IncludeHeader && collected[iu].IsComment();
                        var canTakeLine = allowHeaderComment || iCounterMinIndent == 0;

                        if (canTakeLine)
                        {
                            iCounterMinIndent++;
                        }
                        else
                        {
                            output.RemoveFirst();
                            progressed--;
                            i = -1;
                        }
                    }

                    if (output.Count >= finalLimit)
                    {
                        break;
                    }
                }
                else
                {
                    i = -1;
                }
            }

            if (j < collected.Count)
            {
                var ju = j;
                if (effectiveIndents[ju] >= minIndent)
                {
                    output.AddLast(collected[ju]);
                    progressed++;
                    j++;

                    if (effectiveIndents[ju] == minIndent && !options.IncludeSiblings)
                    {
                        if (jCounterMinIndent > 0)
                        {
                            output.RemoveLast();
                            progressed--;
                            j = collected.Count;
                        }

                        jCounterMinIndent++;
                    }
                }
                else
                {
                    j = collected.Count;
                }
            }

            if (progressed == 0)
            {
                break;
            }
        }

        TrimEmptyLines(output);

        var result = output.Select(record => $"L{record.Number}: {record.Display}").ToArray();
        return Success(request, string.Join('\n', result));
    }

    private static async Task<List<LineRecord>> CollectFileLinesAsync(string filePath, CancellationToken cancellationToken)
    {
        var records = new List<LineRecord>();

        _ = await VisitLinesAsync(
            filePath,
            (lineNumber, lineBytes) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var raw = Utf8.GetString(lineBytes.Span);
                records.Add(new LineRecord(lineNumber, raw, FormatDecodedLine(raw), MeasureIndent(raw)));
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        return records;
    }

    private static int[] ComputeEffectiveIndents(IReadOnlyList<LineRecord> records)
    {
        var effective = new int[records.Count];
        var previousIndent = 0;
        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index];
            if (record.IsBlank())
            {
                effective[index] = previousIndent;
            }
            else
            {
                previousIndent = record.Indent;
                effective[index] = previousIndent;
            }
        }

        return effective;
    }

    private static int MeasureIndent(string line)
    {
        var indent = 0;
        foreach (var ch in line)
        {
            if (ch == ' ')
            {
                indent++;
                continue;
            }

            if (ch == '\t')
            {
                indent += TabWidth;
                continue;
            }

            break;
        }

        return indent;
    }

    private static string FormatDecodedLine(string decoded)
    {
        if (Utf8.GetByteCount(decoded) <= MaxLineLength)
        {
            return decoded;
        }

        var encoded = Utf8.GetBytes(decoded);
        var prefixLength = GetUtf8PrefixLength(encoded, MaxLineLength);
        return Utf8.GetString(encoded, 0, prefixLength);
    }

    private static int GetUtf8PrefixLength(byte[] bytes, int maxBytes)
    {
        if (bytes.Length <= maxBytes)
        {
            return bytes.Length;
        }

        var start = maxBytes - 1;
        while (start >= 0 && (bytes[start] & 0b1100_0000) == 0b1000_0000)
        {
            start--;
        }

        if (start < 0)
        {
            return maxBytes;
        }

        var leading = bytes[start];
        var sequenceLength = leading switch
        {
            < 0b1000_0000 => 1,
            < 0b1110_0000 => 2,
            < 0b1111_0000 => 3,
            < 0b1111_1000 => 4,
            _ => 1,
        };

        if (start + sequenceLength <= maxBytes)
        {
            return maxBytes;
        }

        return start;
    }

    private delegate bool LineVisitor(int lineNumber, ReadOnlyMemory<byte> lineBytes);

    private static async Task<int> VisitLinesAsync(string filePath, LineVisitor visitor, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        var pending = new ArrayBufferWriter<byte>(ReadBufferSize);
        try
        {
            var lineNumber = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = await stream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                var start = 0;
                for (var index = 0; index < bytesRead; index++)
                {
                    if (buffer[index] != (byte)'\n')
                    {
                        continue;
                    }

                    AppendBytes(pending, buffer, start, index - start);
                    lineNumber++;
                    var lineBytes = pending.WrittenMemory;
                    if (EndsWithCarriageReturn(lineBytes))
                    {
                        lineBytes = lineBytes[..^1];
                    }

                    var shouldContinue = visitor(lineNumber, lineBytes);
                    pending.Clear();
                    start = index + 1;
                    if (!shouldContinue)
                    {
                        return lineNumber;
                    }
                }

                if (start < bytesRead)
                {
                    AppendBytes(pending, buffer, start, bytesRead - start);
                }
            }

            if (pending.WrittenCount > 0)
            {
                lineNumber++;
                _ = visitor(lineNumber, pending.WrittenMemory);
            }

            return lineNumber;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool EndsWithCarriageReturn(ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
        {
            return false;
        }

        if (MemoryMarshal.TryGetArray(data, out var segment) && segment.Array is not null)
        {
            return segment.Array[segment.Offset + segment.Count - 1] == (byte)'\r';
        }

        return data.Span[^1] == (byte)'\r';
    }

    private static void AppendBytes(ArrayBufferWriter<byte> writer, byte[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var target = writer.GetSpan(count);
        buffer.AsSpan(offset, count).CopyTo(target);
        writer.Advance(count);
    }

    private static void TrimEmptyLines(LinkedList<LineRecord> records)
    {
        while (records.First is not null && string.IsNullOrWhiteSpace(records.First.Value.Raw.Trim()))
        {
            records.RemoveFirst();
        }

        while (records.Last is not null && string.IsNullOrWhiteSpace(records.Last.Value.Raw.Trim()))
        {
            records.RemoveLast();
        }
    }

    private sealed record IndentationArgs(
        int? AnchorLine,
        int MaxLevels,
        bool IncludeSiblings,
        bool IncludeHeader,
        int? MaxLines);

    private sealed record LineRecord(int Number, string Raw, string Display, int Indent)
    {
        public bool IsBlank()
        {
            return string.IsNullOrWhiteSpace(Raw.TrimStart());
        }

        public bool IsComment()
        {
            var trimmed = Raw.Trim();
            return trimmed.StartsWith("#", StringComparison.Ordinal)
                   || trimmed.StartsWith("//", StringComparison.Ordinal)
                   || trimmed.StartsWith("--", StringComparison.Ordinal);
        }
    }
}

internal sealed class GrepFilesToolHandler : ITianShuToolHandler
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 2000;

    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "pattern" },
        properties = new
        {
            pattern = new { type = "string" },
            include = new { type = "string" },
            path = new { type = "string" },
            limit = new { type = "integer" },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        FileSystemToolNames.GrepFiles,
        "Grep Files",
        "Finds files that contain a regex match using the managed .NET search baseline.",
        capabilities: [new ToolCapability("file-read", "Search readable local files.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            FileSystemToolNames.GrepFiles,
            ToolImplementationKind.Managed,
            implementationId: FileSystemToolNames.ImplementationId,
            requirements: [new ToolRuntimeRequirement("file_system", "File system")],
            probe: new ToolCapabilityProbe(available: true),
            fallbackPolicy: new ToolFallbackPolicy("managed_default", [ToolImplementationKind.Managed])),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var pattern = Normalize(ReadString(request.Input, "pattern"));
        if (pattern is null)
        {
            return Failure(request, "pattern must not be empty");
        }

        var requestedLimit = ReadInt(request.Input, "limit") ?? DefaultLimit;
        if (requestedLimit <= 0)
        {
            return Failure(request, "limit must be greater than zero");
        }

        var path = Normalize(ReadString(request.Input, "path"));
        var searchPath = path is null ? context.WorkingDirectory : ResolvePath(context.WorkingDirectory, path);
        if (!Directory.Exists(searchPath) && !File.Exists(searchPath))
        {
            return Failure(request, $"unable to access `{searchPath}`");
        }

        var include = Normalize(ReadString(request.Input, "include"));
        try
        {
            var results = await FileSystemSearch.RunManagedFileSearchAsync(
                    pattern,
                    include,
                    searchPath,
                    Math.Min(requestedLimit, MaxLimit),
                    context.WorkingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            return results.Count == 0
                ? Failure(request, "No matches found.")
                : Success(request, string.Join('\n', results));
        }
        catch (Exception ex)
        {
            return Failure(request, ex.Message);
        }
    }
}

internal sealed class GrepToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "pattern" },
        properties = new
        {
            pattern = new { type = "string" },
            path = new { type = "string" },
            recursive = new { type = "boolean" },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = BuildDescriptor(
        FileSystemToolNames.Grep,
        "Grep",
        "按正则搜索文本。",
        InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var pattern = Normalize(ReadString(request.Input, "pattern"));
        if (pattern is null)
        {
            return Failure(request, "pattern 不能为空。");
        }

        var path = Normalize(ReadString(request.Input, "path")) ?? context.WorkingDirectory;
        var fullPath = ResolvePath(context.WorkingDirectory, path);
        if (!Directory.Exists(fullPath))
        {
            return Failure(request, $"目录不存在：{fullPath}");
        }

        var recursive = ReadBool(request.Input, "recursive") ?? true;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            return Failure(request, $"invalid regex pattern: {ex.Message}");
        }

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(fullPath, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (!regex.IsMatch(lines[i]))
                {
                    continue;
                }

                hits.Add($"{Path.GetRelativePath(fullPath, file)}:{i + 1}:{lines[i].TrimEnd('\r')}");
                if (hits.Count >= 200)
                {
                    return Success(request, string.Join(Environment.NewLine, hits));
                }
            }
        }

        return Success(request, hits.Count == 0 ? "(no matches)" : string.Join(Environment.NewLine, hits));
    }
}

internal sealed class GlobToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "pattern" },
        properties = new
        {
            pattern = new { type = "string" },
            path = new { type = "string" },
            recursive = new { type = "boolean" },
        },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = BuildDescriptor(
        FileSystemToolNames.Glob,
        "Glob",
        "按通配符匹配文件。",
        InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var pattern = Normalize(ReadString(request.Input, "pattern"));
        if (pattern is null)
        {
            return ValueTask.FromResult(Failure(request, "pattern 不能为空。"));
        }

        var path = Normalize(ReadString(request.Input, "path")) ?? context.WorkingDirectory;
        var fullPath = ResolvePath(context.WorkingDirectory, path);
        if (!Directory.Exists(fullPath))
        {
            return ValueTask.FromResult(Failure(request, $"目录不存在：{fullPath}"));
        }

        var recursive = ReadBool(request.Input, "recursive") ?? true;
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(fullPath, pattern, option)
            .Take(500)
            .Select(file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Path.GetRelativePath(fullPath, file);
            })
            .ToArray();
        var output = files.Length == 0 ? "(no matches)" : string.Join(Environment.NewLine, files);
        return ValueTask.FromResult(Success(request, output));
    }
}

internal static class FileSystemSearch
{
    private static readonly Encoding Utf8Lossy = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static async Task<List<string>> RunManagedFileSearchAsync(
        string pattern,
        string? include,
        string searchPath,
        int limit,
        string cwd,
        CancellationToken cancellationToken)
    {
        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException($"invalid regex pattern: {ex.Message}");
        }

        var files = File.Exists(searchPath)
            ? [searchPath]
            : Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories);
        var results = new List<string>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesInclude(file, searchPath, include))
            {
                continue;
            }

            if (!await FileContainsMatchAsync(file, regex, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            results.Add(Path.GetRelativePath(cwd, file));
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private static bool MatchesInclude(string file, string searchPath, string? include)
    {
        if (string.IsNullOrWhiteSpace(include))
        {
            return true;
        }

        var root = Directory.Exists(searchPath) ? searchPath : Path.GetDirectoryName(searchPath) ?? Environment.CurrentDirectory;
        var fileName = Path.GetFileName(file);
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return FileSystemName.MatchesSimpleExpression(include, fileName, ignoreCase: true)
               || FileSystemName.MatchesSimpleExpression(include, relative, ignoreCase: true);
    }

    private static async Task<bool> FileContainsMatchAsync(string file, Regex regex, CancellationToken cancellationToken)
    {
        try
        {
            if (await LooksBinaryAsync(file, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, useAsync: true);
            using var reader = new StreamReader(stream, Utf8Lossy, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (regex.IsMatch(line))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static async Task<bool> LooksBinaryAsync(string file, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, useAsync: true);
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
        return buffer.AsSpan(0, read).Contains((byte)0);
    }
}

internal static class FileSystemToolResult
{
    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => new(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", StructuredValue.FromPlainObject(payload), isTerminal: true)]);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure($"{request.ToolKey}.invalid_request", message));
}

internal static class FileSystemToolInput
{
    public static string? ReadString(StructuredValue input, string propertyName)
        => input.TryGetProperty(propertyName, out var value) ? value?.GetString() : null;

    public static int? ReadInt(StructuredValue input, string propertyName)
    {
        if (!input.TryGetProperty(propertyName, out var value) || value is null)
        {
            return null;
        }

        return int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static bool? ReadBool(StructuredValue input, string propertyName)
    {
        if (!input.TryGetProperty(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value.Kind == StructuredValueKind.Boolean
            ? value.GetBoolean()
            : bool.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    public static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class FileSystemToolHandlerImports
{
    public static ToolDescriptor BuildDescriptor(string name, string displayName, string description, JsonElement inputSchema)
        => new(
            name,
            displayName,
            description,
            capabilities: [new ToolCapability("file-read", "Read local filesystem data.")],
            approvalRequirement: ToolApprovalRequirement.None,
            concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
            implementationBinding: new ToolImplementationBinding(
                name,
                ToolImplementationKind.Managed,
                implementationId: FileSystemToolNames.ImplementationId),
            inputSchema: inputSchema);

    public static ToolInvocationResult Success(ToolInvocationRequest request, object payload)
        => FileSystemToolResult.Success(request, payload);

    public static ToolInvocationResult Failure(ToolInvocationRequest request, string message)
        => FileSystemToolResult.Failure(request, message);

    public static string? ReadString(StructuredValue input, string propertyName)
        => FileSystemToolInput.ReadString(input, propertyName);

    public static int? ReadInt(StructuredValue input, string propertyName)
        => FileSystemToolInput.ReadInt(input, propertyName);

    public static bool? ReadBool(StructuredValue input, string propertyName)
        => FileSystemToolInput.ReadBool(input, propertyName);

    public static string? Normalize(string? value)
        => FileSystemToolInput.Normalize(value);

    public static string ResolvePath(string cwd, string path)
        => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(cwd, path));
}

internal enum ListDirEntryKind
{
    Directory,
    File,
    Symlink,
}

internal sealed record ListDirEntry(string SortKey, string DisplayName, int Depth, ListDirEntryKind Kind)
{
    public string Format()
    {
        var indent = new string(' ', Depth * 2);
        var suffix = Kind == ListDirEntryKind.Directory ? "/" : string.Empty;
        var label = Kind switch
        {
            ListDirEntryKind.Directory => "dir",
            ListDirEntryKind.File => "file",
            ListDirEntryKind.Symlink => "symlink",
            _ => "other",
        };

        return $"{indent}- [{label}] {DisplayName}{suffix}";
    }
}
