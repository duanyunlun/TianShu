using System.Globalization;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.FileSystemMutating;

/// <summary>
/// 写入文件系统工具域 Provider。
/// Provider for the mutating filesystem tool domain.
/// </summary>
public sealed class MutatingFileSystemToolProvider : ITianShuToolProvider
{
    private static readonly IReadOnlyDictionary<string, ToolDescriptor> Descriptors =
        new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            [MutatingFileSystemToolNames.Write] = WriteToolHandler.DescriptorInstance,
            [MutatingFileSystemToolNames.ApplyPatch] = ApplyPatchToolHandler.DescriptorInstance,
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
            MutatingFileSystemToolNames.Write => new WriteToolHandler(),
            MutatingFileSystemToolNames.ApplyPatch => new ApplyPatchToolHandler(),
            _ => throw new InvalidOperationException($"Unknown mutating filesystem tool: {toolKey}"),
        };
    }
}

internal static class MutatingFileSystemToolNames
{
    public const string Write = "write";
    public const string ApplyPatch = "apply_patch";
    public const string ImplementationId = "tianshu.tools.filesystem-mutating";
}

internal sealed class WriteToolHandler : ITianShuToolHandler
{
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "path", "content" },
        properties = new
        {
            path = new { type = "string", description = "Workspace-relative file path." },
            content = new { type = "string" },
            append = new { type = "boolean" },
        },
    });

    public static ToolDescriptor DescriptorInstance { get; } = MutatingFileSystemToolDescriptors.BuildDescriptor(
        MutatingFileSystemToolNames.Write,
        "Write File",
        "写入文件内容。",
        InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var path = MutatingFileSystemToolInput.Normalize(MutatingFileSystemToolInput.ReadString(request.Input, "path"));
        var content = MutatingFileSystemToolInput.ReadString(request.Input, "content");
        if (string.IsNullOrWhiteSpace(path))
        {
            return MutatingFileSystemToolResult.Failure(request, "path 不能为空。");
        }

        if (Path.IsPathRooted(path))
        {
            return MutatingFileSystemToolResult.Failure(request, "path must be workspace-relative.");
        }

        if (content is null)
        {
            return MutatingFileSystemToolResult.Failure(request, "content 不能为空。");
        }

        var fullPath = MutatingFileSystemPaths.ResolvePath(context.WorkingDirectory, path);
        if (!IsWritePathAllowed(context, fullPath))
        {
            return MutatingFileSystemToolResult.Failure(request, $"沙箱策略禁止写入路径：{fullPath}");
        }

        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var append = MutatingFileSystemToolInput.ReadBool(request.Input, "append") ?? false;
        if (append)
        {
            await File.AppendAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
        }

        return MutatingFileSystemToolResult.Success(request, $"写入成功：{fullPath}");
    }

    private static bool IsWritePathAllowed(TianShuToolInvocationContext context, string fullPath)
        => context.FileMutationServices?.IsWritePathAllowed(fullPath) == true;
}

internal sealed class ApplyPatchToolHandler : ITianShuToolHandler
{
    private const string FreeformGrammar = """
start: begin_patch hunk+ end_patch
begin_patch: "*** Begin Patch" LF
end_patch: "*** End Patch" LF?

hunk: add_hunk | delete_hunk | update_hunk
add_hunk: "*** Add File: " filename LF add_line+
delete_hunk: "*** Delete File: " filename LF
update_hunk: "*** Update File: " filename LF change_move? change?

filename: /(.+)/
add_line: "+" /(.*)/ LF -> line

change_move: "*** Move to: " filename LF
change: (change_context | change_line)+ eof_line?
change_context: ("@@" | "@@ " /(.+)/) LF
change_line: ("+" | "-" | " ") /(.*)/ LF
eof_line: "*** End of File" LF

%import common.LF
""";

    private static readonly JsonElement FreeformFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = FreeformGrammar,
    });

    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "input" },
        properties = new
        {
            input = new { type = "string" },
        },
    });

    public static ToolDescriptor DescriptorInstance { get; } = MutatingFileSystemToolDescriptors.BuildDescriptor(
        MutatingFileSystemToolNames.ApplyPatch,
        "Apply Patch",
        "将结构化补丁应用到本地文件（使用结构化 apply_patch 格式）。",
        InputSchemaElement,
        customInputDefinition: new ToolCustomInputDefinition(
            "使用 `apply_patch` 工具编辑文件。这是 FREEFORM 工具，调用时不要把补丁包装成 JSON。",
            FreeformFormat));

    public ToolDescriptor Descriptor => DescriptorInstance;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var patch = MutatingFileSystemToolInput.Normalize(MutatingFileSystemToolInput.ReadString(request.Input, "input"));
        try
        {
            var output = MutatingApplyPatch.ApplyVerified(
                patch ?? string.Empty,
                context.WorkingDirectory,
                fullPath => context.FileMutationServices?.IsWritePathAllowed(fullPath) == true
                            || context.FileMutationServices?.IsFileChangeApproved(fullPath) == true);
            return ValueTask.FromResult(MutatingFileSystemToolResult.Success(request, output));
        }
        catch (MutatingApplyPatchException ex)
        {
            return ValueTask.FromResult(MutatingFileSystemToolResult.Failure(request, $"apply_patch verification failed: {ex.Message}"));
        }
    }
}

internal static class MutatingFileSystemToolDescriptors
{
    public static ToolDescriptor BuildDescriptor(
        string name,
        string displayName,
        string description,
        JsonElement inputSchema,
        ToolCustomInputDefinition? customInputDefinition = null)
        => new(
            name,
            displayName,
            description,
            capabilities: [new ToolCapability("file-write", "Write local filesystem data.")],
            approvalRequirement: ToolApprovalRequirement.Required,
            concurrencyClass: ToolConcurrencyClass.Exclusive,
            implementationBinding: new ToolImplementationBinding(
                name,
                ToolImplementationKind.Managed,
                implementationId: MutatingFileSystemToolNames.ImplementationId,
                requirements: [new ToolRuntimeRequirement("file_system", "File system")],
                probe: new ToolCapabilityProbe(available: true),
                fallbackPolicy: new ToolFallbackPolicy("managed_default", [ToolImplementationKind.Managed])),
            inputSchema: inputSchema,
            customInputDefinition: customInputDefinition);
}

internal static class MutatingFileSystemToolResult
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

internal static class MutatingFileSystemToolInput
{
    public static string? ReadString(StructuredValue input, string propertyName)
        => input.TryGetProperty(propertyName, out var value) ? value?.GetString() : null;

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

internal static class MutatingFileSystemPaths
{
    public static string ResolvePath(string cwd, string path)
        => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(cwd, path));

    public static string NormalizeApprovalKey(string fullPath)
    {
        var normalized = Path.GetFullPath(fullPath);
        return Path.TrimEndingDirectorySeparator(normalized);
    }
}

internal static class MutatingApplyPatch
{
    private const string BeginPatchMarker = "*** Begin Patch";
    private const string EndPatchMarker = "*** End Patch";
    private const string AddFileMarker = "*** Add File: ";
    private const string DeleteFileMarker = "*** Delete File: ";
    private const string UpdateFileMarker = "*** Update File: ";
    private const string MoveToMarker = "*** Move to: ";
    private const string EofMarker = "*** End of File";
    private const string ChangeContextMarker = "@@ ";
    private const string EmptyChangeContextMarker = "@@";

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string ApplyVerified(string patchText, string cwd, Func<string, bool>? canWrite = null)
    {
        var cwdFullPath = Path.GetFullPath(cwd);
        var hunks = ParsePatch(patchText);
        if (hunks.Count == 0)
        {
            throw new MutatingApplyPatchException("No files were modified.");
        }

        var plan = VerifyPlan(hunks, cwdFullPath, canWrite);
        ApplyPlan(plan);
        return BuildSummary(plan);
    }

    private static List<PatchHunk> ParsePatch(string patchText)
    {
        var lines = ReadLines(patchText.Trim());
        if (lines.Count == 0)
        {
            throw new MutatingApplyPatchException("invalid patch: The last line of the patch must be '*** End Patch'");
        }

        var normalized = SliceToPatchLines(lines);
        if (!IsMarkerLine(normalized[0], BeginPatchMarker))
        {
            throw new MutatingApplyPatchException("invalid patch: The first line of the patch must be '*** Begin Patch'");
        }

        if (!IsMarkerLine(normalized[^1], EndPatchMarker))
        {
            throw new MutatingApplyPatchException("invalid patch: The last line of the patch must be '*** End Patch'");
        }

        var remaining = normalized.Skip(1).Take(normalized.Count - 2).ToArray();
        var hunks = new List<PatchHunk>();
        var lineNumber = 2;
        var index = 0;
        while (index < remaining.Length)
        {
            var (hunk, parsedLines) = ParseOneHunk(remaining.AsSpan(index), lineNumber);
            hunks.Add(hunk);
            index += parsedLines;
            lineNumber += parsedLines;
        }

        return hunks;
    }

    private static List<string> SliceToPatchLines(List<string> originalLines)
    {
        if (CheckPatchBoundariesStrict(originalLines))
        {
            return originalLines;
        }

        if (originalLines.Count >= 4)
        {
            var first = originalLines[0];
            var last = originalLines[^1];
            if ((first == "<<EOF" || first == "<<'EOF'" || first == "<<\"EOF\"")
                && last.EndsWith("EOF", StringComparison.Ordinal))
            {
                var inner = originalLines.Skip(1).Take(originalLines.Count - 2).ToList();
                if (CheckPatchBoundariesStrict(inner))
                {
                    return inner;
                }
            }
        }

        return originalLines;
    }

    private static bool CheckPatchBoundariesStrict(IReadOnlyList<string> lines)
        => lines.Count >= 2
           && IsMarkerLine(lines[0], BeginPatchMarker)
           && IsMarkerLine(lines[^1], EndPatchMarker);

    private static bool IsMarkerLine(string line, string marker)
        => string.Equals(line.Trim(), marker, StringComparison.Ordinal);

    private static (PatchHunk Hunk, int ParsedLines) ParseOneHunk(ReadOnlySpan<string> lines, int lineNumber)
    {
        if (lines.Length == 0)
        {
            throw new MutatingApplyPatchException($"invalid hunk at line {lineNumber}, Update hunk does not contain any lines");
        }

        var firstLine = lines[0].Trim();
        if (firstLine.StartsWith(AddFileMarker, StringComparison.Ordinal))
        {
            var path = firstLine[AddFileMarker.Length..];
            var contents = new StringBuilder();
            var parsed = 1;
            for (var i = 1; i < lines.Length; i++)
            {
                var addLine = lines[i];
                if (!addLine.StartsWith('+'))
                {
                    break;
                }

                contents.Append(addLine[1..]);
                contents.Append('\n');
                parsed++;
            }

            return (new AddFileHunk(path, contents.ToString()), parsed);
        }

        if (firstLine.StartsWith(DeleteFileMarker, StringComparison.Ordinal))
        {
            var path = firstLine[DeleteFileMarker.Length..];
            return (new DeleteFileHunk(path), 1);
        }

        if (firstLine.StartsWith(UpdateFileMarker, StringComparison.Ordinal))
        {
            return ParseUpdateHunk(firstLine[UpdateFileMarker.Length..], lines[1..], lineNumber);
        }

        throw new MutatingApplyPatchException(
            $"invalid hunk at line {lineNumber}, '{firstLine}' is not a valid hunk header. Valid hunk headers: '*** Add File: {{path}}', '*** Delete File: {{path}}', '*** Update File: {{path}}'");
    }

    private static (PatchHunk Hunk, int ParsedLines) ParseUpdateHunk(string path, ReadOnlySpan<string> lines, int lineNumber)
    {
        var parsed = 1;
        string? movePath = null;
        if (lines.Length > 0 && lines[0].StartsWith(MoveToMarker, StringComparison.Ordinal))
        {
            movePath = lines[0][MoveToMarker.Length..];
            lines = lines[1..];
            parsed++;
        }

        var chunks = new List<UpdateFileChunk>();
        while (lines.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(lines[0].Trim()))
            {
                lines = lines[1..];
                parsed++;
                continue;
            }

            if (lines[0].StartsWith("***", StringComparison.Ordinal))
            {
                break;
            }

            var (chunk, chunkLines) = ParseUpdateFileChunk(lines, lineNumber + parsed, chunks.Count == 0);
            chunks.Add(chunk);
            lines = lines[chunkLines..];
            parsed += chunkLines;
        }

        if (chunks.Count == 0)
        {
            throw new MutatingApplyPatchException($"invalid hunk at line {lineNumber}, Update file hunk for path '{path}' is empty");
        }

        return (new UpdateFileHunk(path, movePath, chunks), parsed);
    }

    private static (UpdateFileChunk Chunk, int ParsedLines) ParseUpdateFileChunk(
        ReadOnlySpan<string> lines,
        int lineNumber,
        bool allowMissingContext)
    {
        if (lines.Length == 0)
        {
            throw new MutatingApplyPatchException($"invalid hunk at line {lineNumber}, Update hunk does not contain any lines");
        }

        string? changeContext = null;
        var startIndex = 0;
        if (string.Equals(lines[0], EmptyChangeContextMarker, StringComparison.Ordinal))
        {
            startIndex = 1;
        }
        else if (lines[0].StartsWith(ChangeContextMarker, StringComparison.Ordinal))
        {
            changeContext = lines[0][ChangeContextMarker.Length..];
            startIndex = 1;
        }
        else if (!allowMissingContext)
        {
            throw new MutatingApplyPatchException(
                $"invalid hunk at line {lineNumber}, Expected update hunk to start with a @@ context marker, got: '{lines[0]}'");
        }

        if (startIndex >= lines.Length)
        {
            throw new MutatingApplyPatchException($"invalid hunk at line {lineNumber + 1}, Update hunk does not contain any lines");
        }

        var chunk = new UpdateFileChunk(changeContext);
        var parsed = 0;
        for (var i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.Equals(line, EofMarker, StringComparison.Ordinal))
            {
                if (parsed == 0)
                {
                    throw new MutatingApplyPatchException($"invalid hunk at line {lineNumber + 1}, Update hunk does not contain any lines");
                }

                chunk.IsEndOfFile = true;
                parsed++;
                break;
            }

            if (line.Length == 0)
            {
                chunk.OldLines.Add(string.Empty);
                chunk.NewLines.Add(string.Empty);
                parsed++;
                continue;
            }

            var marker = line[0];
            switch (marker)
            {
                case ' ':
                    chunk.OldLines.Add(line[1..]);
                    chunk.NewLines.Add(line[1..]);
                    parsed++;
                    break;
                case '+':
                    chunk.NewLines.Add(line[1..]);
                    parsed++;
                    break;
                case '-':
                    chunk.OldLines.Add(line[1..]);
                    parsed++;
                    break;
                default:
                    if (parsed == 0)
                    {
                        throw new MutatingApplyPatchException(
                            $"invalid hunk at line {lineNumber + 1}, Unexpected line found in update hunk: '{line}'. Every line should start with ' ' (context line), '+' (added line), or '-' (removed line)");
                    }

                    return (chunk, parsed + startIndex);
            }
        }

        return (chunk, parsed + startIndex);
    }

    private static List<string> ReadLines(string text)
    {
        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static PatchPlan VerifyPlan(IReadOnlyList<PatchHunk> hunks, string cwdFullPath, Func<string, bool>? canWrite)
    {
        var plan = new PatchPlan();
        foreach (var hunk in hunks)
        {
            switch (hunk)
            {
                case AddFileHunk add:
                    plan.Operations.Add(VerifyAdd(add, cwdFullPath, canWrite));
                    break;
                case DeleteFileHunk delete:
                    plan.Operations.Add(VerifyDelete(delete, cwdFullPath, canWrite));
                    break;
                case UpdateFileHunk update:
                    plan.Operations.Add(VerifyUpdate(update, cwdFullPath, canWrite));
                    break;
            }
        }

        return plan;
    }

    private static AddOperation VerifyAdd(AddFileHunk hunk, string cwdFullPath, Func<string, bool>? canWrite)
    {
        EnsureRelativePath(hunk.Path);
        var fullPath = ResolvePathWithinCwd(cwdFullPath, hunk.Path);
        EnsureWritable(fullPath, canWrite);
        return new AddOperation(hunk.Path, fullPath, hunk.Contents);
    }

    private static DeleteOperation VerifyDelete(DeleteFileHunk hunk, string cwdFullPath, Func<string, bool>? canWrite)
    {
        EnsureRelativePath(hunk.Path);
        var fullPath = ResolvePathWithinCwd(cwdFullPath, hunk.Path);
        EnsureWritable(fullPath, canWrite);
        try
        {
            _ = File.ReadAllText(fullPath, Utf8NoBom);
        }
        catch (Exception ex)
        {
            throw new MutatingApplyPatchException($"Failed to read {fullPath}: {ex.Message}");
        }

        return new DeleteOperation(hunk.Path, fullPath);
    }

    private static UpdateOperation VerifyUpdate(UpdateFileHunk hunk, string cwdFullPath, Func<string, bool>? canWrite)
    {
        EnsureRelativePath(hunk.Path);
        var sourceFullPath = ResolvePathWithinCwd(cwdFullPath, hunk.Path);
        EnsureWritable(sourceFullPath, canWrite);
        var updatedContent = DeriveNewContentsFromChunks(sourceFullPath, hunk.Chunks);

        if (hunk.MovePath is not null)
        {
            EnsureRelativePath(hunk.MovePath);
            var destFullPath = ResolvePathWithinCwd(cwdFullPath, hunk.MovePath);
            EnsureWritable(destFullPath, canWrite);
            return new UpdateOperation(hunk.Path, sourceFullPath, hunk.MovePath, destFullPath, updatedContent);
        }

        return new UpdateOperation(hunk.Path, sourceFullPath, null, null, updatedContent);
    }

    private static void EnsureRelativePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            throw new MutatingApplyPatchException("absolute paths are not allowed");
        }
    }

    private static string ResolvePathWithinCwd(string cwdFullPath, string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(cwdFullPath, relativePath));
        var prefix = cwdFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.StartsWith(prefix, comparison) && !string.Equals(combined, cwdFullPath, comparison))
        {
            throw new MutatingApplyPatchException("path escapes cwd");
        }

        return combined;
    }

    private static void ApplyPlan(PatchPlan plan)
    {
        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case AddOperation add:
                    CreateParentDirectory(add.FullPath);
                    File.WriteAllText(add.FullPath, add.Contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    break;
                case DeleteOperation delete:
                    try
                    {
                        File.Delete(delete.FullPath);
                    }
                    catch (Exception ex)
                    {
                        throw new MutatingApplyPatchException($"Failed to delete file {delete.FullPath}: {ex.Message}");
                    }

                    break;
                case UpdateOperation update:
                    var destPath = update.DestFullPath ?? update.SourceFullPath;
                    CreateParentDirectory(destPath);
                    File.WriteAllText(destPath, update.NewContents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    if (update.DestFullPath is not null)
                    {
                        try
                        {
                            File.Delete(update.SourceFullPath);
                        }
                        catch (Exception ex)
                        {
                            throw new MutatingApplyPatchException($"Failed to remove original {update.SourceFullPath}: {ex.Message}");
                        }
                    }

                    break;
            }
        }
    }

    private static void CreateParentDirectory(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void EnsureWritable(string fullPath, Func<string, bool>? canWrite)
    {
        if (canWrite is not null && !canWrite(fullPath))
        {
            throw new MutatingApplyPatchException($"Patch target is outside sandbox writable roots: {fullPath}");
        }
    }

    private static string BuildSummary(PatchPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("Success. Updated the following files:\n");
        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case AddOperation add:
                    builder.Append("A ");
                    builder.Append(add.Path);
                    builder.Append('\n');
                    break;
                case UpdateOperation update:
                    builder.Append("M ");
                    builder.Append(update.DestPath ?? update.SourcePath);
                    builder.Append('\n');
                    break;
                case DeleteOperation delete:
                    builder.Append("D ");
                    builder.Append(delete.Path);
                    builder.Append('\n');
                    break;
            }
        }

        return builder.ToString();
    }

    private static string DeriveNewContentsFromChunks(string fullPath, IReadOnlyList<UpdateFileChunk> chunks)
    {
        string originalContents;
        try
        {
            originalContents = File.ReadAllText(fullPath, Utf8NoBom);
        }
        catch (Exception ex)
        {
            throw new MutatingApplyPatchException($"Failed to read file to update {fullPath}: {ex.Message}");
        }

        var originalLines = originalContents.Split('\n').Select(static x => x.ToString()).ToList();
        if (originalLines.Count > 0 && string.Equals(originalLines[^1], string.Empty, StringComparison.Ordinal))
        {
            originalLines.RemoveAt(originalLines.Count - 1);
        }

        var replacements = ComputeReplacements(originalLines, fullPath, chunks);
        var newLines = ApplyReplacements(originalLines, replacements);
        if (newLines.Count == 0 || !string.Equals(newLines[^1], string.Empty, StringComparison.Ordinal))
        {
            newLines.Add(string.Empty);
        }

        return string.Join("\n", newLines);
    }

    private static List<Replacement> ComputeReplacements(
        IReadOnlyList<string> originalLines,
        string fullPath,
        IReadOnlyList<UpdateFileChunk> chunks)
    {
        var replacements = new List<Replacement>();
        var lineIndex = 0;
        foreach (var chunk in chunks)
        {
            if (chunk.ChangeContext is not null)
            {
                var idx = SeekSequence(originalLines, new[] { chunk.ChangeContext }, lineIndex, eof: false);
                if (idx is null)
                {
                    throw new MutatingApplyPatchException($"Failed to find context '{chunk.ChangeContext}' in {fullPath}");
                }

                lineIndex = idx.Value + 1;
            }

            if (chunk.OldLines.Count == 0)
            {
                var insertionIndex = originalLines.Count;
                if (originalLines.Count > 0 && string.Equals(originalLines[^1], string.Empty, StringComparison.Ordinal))
                {
                    insertionIndex = originalLines.Count - 1;
                }

                replacements.Add(new Replacement(insertionIndex, 0, chunk.NewLines.ToList()));
                continue;
            }

            var pattern = chunk.OldLines;
            var found = SeekSequence(originalLines, pattern, lineIndex, chunk.IsEndOfFile);
            var newSlice = chunk.NewLines;
            if (found is null && pattern.Count > 0 && string.Equals(pattern[^1], string.Empty, StringComparison.Ordinal))
            {
                pattern = pattern.Take(pattern.Count - 1).ToList();
                if (newSlice.Count > 0 && string.Equals(newSlice[^1], string.Empty, StringComparison.Ordinal))
                {
                    newSlice = newSlice.Take(newSlice.Count - 1).ToList();
                }

                found = SeekSequence(originalLines, pattern, lineIndex, chunk.IsEndOfFile);
            }

            if (found is null)
            {
                throw new MutatingApplyPatchException($"Failed to find expected lines in {fullPath}:\n{string.Join("\n", chunk.OldLines)}");
            }

            replacements.Add(new Replacement(found.Value, pattern.Count, newSlice.ToList()));
            lineIndex = found.Value + pattern.Count;
        }

        replacements.Sort(static (a, b) => a.StartIndex.CompareTo(b.StartIndex));
        return replacements;
    }

    private static List<string> ApplyReplacements(List<string> lines, IReadOnlyList<Replacement> replacements)
    {
        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            var replacement = replacements[i];
            for (var k = 0; k < replacement.OldLength; k++)
            {
                if (replacement.StartIndex < lines.Count)
                {
                    lines.RemoveAt(replacement.StartIndex);
                }
            }

            for (var offset = 0; offset < replacement.NewLines.Count; offset++)
            {
                lines.Insert(replacement.StartIndex + offset, replacement.NewLines[offset]);
            }
        }

        return lines;
    }

    private static int? SeekSequence(IReadOnlyList<string> lines, IReadOnlyList<string> pattern, int start, bool eof)
    {
        if (pattern.Count == 0)
        {
            return start;
        }

        if (pattern.Count > lines.Count)
        {
            return null;
        }

        var searchStart = eof && lines.Count >= pattern.Count ? lines.Count - pattern.Count : start;
        var maxStart = lines.Count - pattern.Count;
        if (searchStart > maxStart)
        {
            return null;
        }

        foreach (var comparer in MatchComparers)
        {
            for (var i = searchStart; i <= maxStart; i++)
            {
                if (MatchAt(lines, pattern, i, comparer))
                {
                    return i;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<Func<string, string, bool>> MatchComparers { get; } =
    [
        static (a, b) => string.Equals(a, b, StringComparison.Ordinal),
        static (a, b) => string.Equals(a.TrimEnd(), b.TrimEnd(), StringComparison.Ordinal),
        static (a, b) => string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal),
        static (a, b) => string.Equals(NormalizeForMatch(a), NormalizeForMatch(b), StringComparison.Ordinal),
    ];

    private static bool MatchAt(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> pattern,
        int startIndex,
        Func<string, string, bool> comparer)
    {
        for (var i = 0; i < pattern.Count; i++)
        {
            if (!comparer(lines[startIndex + i], pattern[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeForMatch(string input)
    {
        var builder = new StringBuilder();
        foreach (var ch in input.Trim())
        {
            builder.Append(ch switch
            {
                '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' => '-',
                '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
                '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
                '\u00A0' or '\u2002' or '\u2003' or '\u2004' or '\u2005' or '\u2006'
                    or '\u2007' or '\u2008' or '\u2009' or '\u200A' or '\u202F' or '\u205F'
                    or '\u3000' => ' ',
                _ => ch,
            });
        }

        return builder.ToString();
    }

    private abstract record PatchHunk;

    private sealed record AddFileHunk(string Path, string Contents) : PatchHunk;

    private sealed record DeleteFileHunk(string Path) : PatchHunk;

    private sealed record UpdateFileHunk(string Path, string? MovePath, List<UpdateFileChunk> Chunks) : PatchHunk;

    private sealed class UpdateFileChunk
    {
        public UpdateFileChunk(string? changeContext)
        {
            ChangeContext = changeContext;
        }

        public string? ChangeContext { get; }

        public List<string> OldLines { get; } = new();

        public List<string> NewLines { get; } = new();

        public bool IsEndOfFile { get; set; }
    }

    private sealed class PatchPlan
    {
        public List<PatchOperation> Operations { get; } = new();
    }

    private abstract record PatchOperation;

    private sealed record AddOperation(string Path, string FullPath, string Contents) : PatchOperation;

    private sealed record DeleteOperation(string Path, string FullPath) : PatchOperation;

    private sealed record UpdateOperation(
        string SourcePath,
        string SourceFullPath,
        string? DestPath,
        string? DestFullPath,
        string NewContents) : PatchOperation;

    private sealed record Replacement(int StartIndex, int OldLength, List<string> NewLines);
}

internal sealed class MutatingApplyPatchException : Exception
{
    public MutatingApplyPatchException(string message)
        : base(message)
    {
    }
}
