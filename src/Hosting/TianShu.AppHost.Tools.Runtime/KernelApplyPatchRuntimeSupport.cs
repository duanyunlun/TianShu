using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal static class KernelApplyPatchRuntimeSupport
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

    public static readonly JsonElement FreeformFormat = JsonSerializer.SerializeToElement(new
    {
        type = "grammar",
        syntax = "lark",
        definition = FreeformGrammar,
    });

    public static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        required = new[] { "input" },
        properties = new
        {
            input = new { type = "string" },
        },
    });

    public static ProviderResponsesToolDefinition BuildModelToolDefinition(bool freeform)
    {
        if (!freeform)
        {
            return new ProviderResponsesFunctionToolDefinition(
                "apply_patch",
                "将结构化补丁应用到本地文件（使用结构化 apply_patch 格式）。",
                InputSchema,
                strict: false);
        }

        return new ProviderResponsesCustomToolDefinition(
            "apply_patch",
            "使用 `apply_patch` 工具编辑文件。这是 FREEFORM 工具，调用时不要把补丁包装成 JSON。",
            FreeformFormat);
    }

    public static Task<KernelToolResult> ExecuteAsync(
        JsonElement arguments,
        KernelToolCallContext context,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(ExecuteCore(ExtractPatch(arguments), context));
    }

    public static Task<KernelToolResult> ExecuteCustomAsync(string input, KernelToolCallContext context, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(ExecuteCore(KernelToolJsonHelpers.Normalize(input), context));
    }

    private static string? ExtractPatch(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.String)
        {
            return KernelToolJsonHelpers.Normalize(arguments.GetString());
        }

        return KernelToolJsonHelpers.ReadString(arguments, "input");
    }

    private static KernelToolResult ExecuteCore(string? patch, KernelToolCallContext context)
    {
        try
        {
            var output = KernelApplyPatch.ApplyVerified(
                patch ?? string.Empty,
                context.Cwd,
                fullPath => KernelSandboxEnforcer.EnsureWritePathAllowed(fullPath, context.Cwd, context.SandboxPolicy, context.SandboxMode).Allowed
                    || KernelFileChangeApprovalHelpers.IsApproved(fullPath, context.ApprovedFileChangePaths));
            return Success(output);
        }
        catch (KernelApplyPatchException ex)
        {
            return Failure($"apply_patch verification failed: {ex.Message}");
        }
    }

    private static KernelToolResult Success(string message) => new(true, message);

    private static KernelToolResult Failure(string message) => new(false, message);
}

internal sealed record KernelFileChangeDescription(string Path, string Kind, string Diff);

internal static class KernelApplyPatch
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
            throw new KernelApplyPatchException("No files were modified.");
        }

        var plan = VerifyPlan(hunks, cwdFullPath, canWrite);
        ApplyPlan(plan);
        return BuildSummary(plan);
    }

    public static IReadOnlyList<string> CollectAffectedFullPaths(string patchText, string cwd)
    {
        var cwdFullPath = Path.GetFullPath(cwd);
        var hunks = ParsePatch(patchText);
        if (hunks.Count == 0)
        {
            throw new KernelApplyPatchException("No files were modified.");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hunk in hunks)
        {
            switch (hunk)
            {
                case AddFileHunk add:
                    paths.Add(KernelFileChangeApprovalHelpers.NormalizeApprovalKey(ResolvePatchFullPath(cwdFullPath, add.Path)));
                    break;
                case DeleteFileHunk delete:
                    paths.Add(KernelFileChangeApprovalHelpers.NormalizeApprovalKey(ResolvePatchFullPath(cwdFullPath, delete.Path)));
                    break;
                case UpdateFileHunk update:
                    paths.Add(KernelFileChangeApprovalHelpers.NormalizeApprovalKey(ResolvePatchFullPath(cwdFullPath, update.Path)));
                    if (!string.IsNullOrWhiteSpace(update.MovePath))
                    {
                        paths.Add(KernelFileChangeApprovalHelpers.NormalizeApprovalKey(ResolvePatchFullPath(cwdFullPath, update.MovePath!)));
                    }
                    break;
            }
        }

        return paths.ToArray();
    }

    public static IReadOnlyList<KernelFileChangeDescription> DescribeChanges(string patchText, string cwd)
    {
        var cwdFullPath = Path.GetFullPath(cwd);
        var hunks = ParsePatch(patchText);
        if (hunks.Count == 0)
        {
            throw new KernelApplyPatchException("No files were modified.");
        }

        var changes = new List<KernelFileChangeDescription>(hunks.Count);
        foreach (var hunk in hunks)
        {
            switch (hunk)
            {
                case AddFileHunk add:
                    changes.Add(new KernelFileChangeDescription(
                        ResolvePatchFullPath(cwdFullPath, add.Path),
                        "add",
                        add.Contents));
                    break;
                case DeleteFileHunk delete:
                    changes.Add(new KernelFileChangeDescription(
                        ResolvePatchFullPath(cwdFullPath, delete.Path),
                        "delete",
                        string.Empty));
                    break;
                case UpdateFileHunk update:
                    changes.Add(new KernelFileChangeDescription(
                        ResolvePatchFullPath(cwdFullPath, update.MovePath ?? update.Path),
                        "update",
                        BuildChunkDiff(update.Chunks)));
                    break;
            }
        }

        return changes;
    }

    private static string ResolvePatchFullPath(string cwdFullPath, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(cwdFullPath, relativePath));
    }

    private static List<PatchHunk> ParsePatch(string patchText)
    {
        var lines = ReadLines(patchText.Trim());
        if (lines.Count == 0)
        {
            throw new KernelApplyPatchException("invalid patch: The last line of the patch must be '*** End Patch'");
        }

        var normalized = SliceToPatchLines(lines);
        if (!IsMarkerLine(normalized[0], BeginPatchMarker))
        {
            throw new KernelApplyPatchException("invalid patch: The first line of the patch must be '*** Begin Patch'");
        }

        if (!IsMarkerLine(normalized[^1], EndPatchMarker))
        {
            throw new KernelApplyPatchException("invalid patch: The last line of the patch must be '*** End Patch'");
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
    {
        if (lines.Count < 2)
        {
            return false;
        }

        return IsMarkerLine(lines[0], BeginPatchMarker) && IsMarkerLine(lines[^1], EndPatchMarker);
    }

    private static bool IsMarkerLine(string line, string marker)
        => string.Equals(line.Trim(), marker, StringComparison.Ordinal);

    private static (PatchHunk hunk, int parsedLines) ParseOneHunk(ReadOnlySpan<string> lines, int lineNumber)
    {
        if (lines.Length == 0)
        {
            throw new KernelApplyPatchException($"invalid hunk at line {lineNumber}, Update hunk does not contain any lines");
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
            var path = firstLine[UpdateFileMarker.Length..];
            var parsed = 1;
            var span = lines[1..];

            string? movePath = null;
            if (span.Length > 0 && span[0].StartsWith(MoveToMarker, StringComparison.Ordinal))
            {
                movePath = span[0][MoveToMarker.Length..];
                span = span[1..];
                parsed++;
            }

            var chunks = new List<UpdateFileChunk>();
            while (span.Length > 0)
            {
                if (string.IsNullOrWhiteSpace(span[0].Trim()))
                {
                    span = span[1..];
                    parsed++;
                    continue;
                }

                if (span[0].StartsWith("***", StringComparison.Ordinal))
                {
                    break;
                }

                var (chunk, chunkLines) = ParseUpdateFileChunk(span, lineNumber + parsed, chunks.Count == 0);
                chunks.Add(chunk);
                span = span[chunkLines..];
                parsed += chunkLines;
            }

            if (chunks.Count == 0)
            {
                throw new KernelApplyPatchException($"invalid hunk at line {lineNumber}, Update file hunk for path '{path}' is empty");
            }

            return (new UpdateFileHunk(path, movePath, chunks), parsed);
        }

        throw new KernelApplyPatchException(
            $"invalid hunk at line {lineNumber}, '{firstLine}' is not a valid hunk header. Valid hunk headers: '*** Add File: {{path}}', '*** Delete File: {{path}}', '*** Update File: {{path}}'");
    }

    private static (UpdateFileChunk chunk, int parsedLines) ParseUpdateFileChunk(
        ReadOnlySpan<string> lines,
        int lineNumber,
        bool allowMissingContext)
    {
        if (lines.Length == 0)
        {
            throw new KernelApplyPatchException($"invalid hunk at line {lineNumber}, Update hunk does not contain any lines");
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
            throw new KernelApplyPatchException(
                $"invalid hunk at line {lineNumber}, Expected update hunk to start with a @@ context marker, got: '{lines[0]}'");
        }

        if (startIndex >= lines.Length)
        {
            throw new KernelApplyPatchException($"invalid hunk at line {lineNumber + 1}, Update hunk does not contain any lines");
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
                    throw new KernelApplyPatchException($"invalid hunk at line {lineNumber + 1}, Update hunk does not contain any lines");
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
                        throw new KernelApplyPatchException(
                            $"invalid hunk at line {lineNumber + 1}, Unexpected line found in update hunk: '{line}'. Every line should start with ' ' (context line), '+' (added line), or '-' (removed line)");
                    }

                    // Assume this is the start of the next hunk.
                    return (chunk, parsed + startIndex);
            }
        }

        return (chunk, parsed + startIndex);
    }


    private static string BuildChunkDiff(IReadOnlyList<UpdateFileChunk> chunks)
    {
        var builder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (!string.IsNullOrWhiteSpace(chunk.ChangeContext))
            {
                builder.Append("@@ ");
                builder.AppendLine(chunk.ChangeContext);
            }

            foreach (var line in chunk.OldLines)
            {
                builder.Append('-');
                builder.AppendLine(line);
            }

            foreach (var line in chunk.NewLines)
            {
                builder.Append('+');
                builder.AppendLine(line);
            }
        }

        return builder.ToString().TrimEnd('\r', '\n');
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
        if (Path.IsPathRooted(hunk.Path))
        {
            throw new KernelApplyPatchException("absolute paths are not allowed");
        }

        var fullPath = ResolvePathWithinCwd(cwdFullPath, hunk.Path);
        EnsureWritable(fullPath, canWrite);
        return new AddOperation(hunk.Path, fullPath, hunk.Contents);
    }

    private static DeleteOperation VerifyDelete(DeleteFileHunk hunk, string cwdFullPath, Func<string, bool>? canWrite)
    {
        if (Path.IsPathRooted(hunk.Path))
        {
            throw new KernelApplyPatchException("absolute paths are not allowed");
        }

        var fullPath = ResolvePathWithinCwd(cwdFullPath, hunk.Path);
        EnsureWritable(fullPath, canWrite);
        try
        {
            _ = File.ReadAllText(fullPath, Utf8NoBom);
        }
        catch (Exception ex)
        {
            throw new KernelApplyPatchException($"Failed to read {fullPath}: {ex.Message}");
        }

        return new DeleteOperation(hunk.Path, fullPath);
    }

    private static UpdateOperation VerifyUpdate(UpdateFileHunk hunk, string cwdFullPath, Func<string, bool>? canWrite)
    {
        if (Path.IsPathRooted(hunk.Path))
        {
            throw new KernelApplyPatchException("absolute paths are not allowed");
        }

        var sourceFullPath = ResolvePathWithinCwd(cwdFullPath, hunk.Path);
        EnsureWritable(sourceFullPath, canWrite);
        var updatedContent = DeriveNewContentsFromChunks(sourceFullPath, hunk.Chunks);

        if (hunk.MovePath is not null)
        {
            if (Path.IsPathRooted(hunk.MovePath))
            {
                throw new KernelApplyPatchException("absolute paths are not allowed");
            }

            var destFullPath = ResolvePathWithinCwd(cwdFullPath, hunk.MovePath);
            EnsureWritable(destFullPath, canWrite);
            return new UpdateOperation(
                SourcePath: hunk.Path,
                SourceFullPath: sourceFullPath,
                DestPath: hunk.MovePath,
                DestFullPath: destFullPath,
                NewContents: updatedContent);
        }

        return new UpdateOperation(
            SourcePath: hunk.Path,
            SourceFullPath: sourceFullPath,
            DestPath: null,
            DestFullPath: null,
            NewContents: updatedContent);
    }

    private static string ResolvePathWithinCwd(string cwdFullPath, string relativePath)
    {
        var combined = Path.GetFullPath(Path.Combine(cwdFullPath, relativePath));
        var prefix = cwdFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, cwdFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new KernelApplyPatchException("path escapes cwd");
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
                    if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(add.FullPath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(add.FullPath)!);
                    }

                    File.WriteAllText(add.FullPath, add.Contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    break;

                case DeleteOperation delete:
                    try
                    {
                        File.Delete(delete.FullPath);
                    }
                    catch (Exception ex)
                    {
                        throw new KernelApplyPatchException($"Failed to delete file {delete.FullPath}: {ex.Message}");
                    }

                    break;

                case UpdateOperation update:
                    var destPath = update.DestFullPath ?? update.SourceFullPath;
                    if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(destPath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    }

                    File.WriteAllText(destPath, update.NewContents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                    if (update.DestFullPath is not null)
                    {
                        try
                        {
                            File.Delete(update.SourceFullPath);
                        }
                        catch (Exception ex)
                        {
                            throw new KernelApplyPatchException($"Failed to remove original {update.SourceFullPath}: {ex.Message}");
                        }
                    }

                    break;
            }
        }
    }

    private static void EnsureWritable(string fullPath, Func<string, bool>? canWrite)
    {
        if (canWrite is not null && !canWrite(fullPath))
        {
            throw new KernelApplyPatchException($"Patch target is outside sandbox writable roots: {fullPath}");
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
            throw new KernelApplyPatchException($"Failed to read file to update {fullPath}: {ex.Message}");
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
                    throw new KernelApplyPatchException($"Failed to find context '{chunk.ChangeContext}' in {fullPath}");
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
                throw new KernelApplyPatchException($"Failed to find expected lines in {fullPath}:\n{string.Join("\n", chunk.OldLines)}");
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

    private static int? SeekSequence(
        IReadOnlyList<string> lines,
        IReadOnlyList<string> pattern,
        int start,
        bool eof)
    {
        if (pattern.Count == 0)
        {
            return start;
        }

        if (pattern.Count > lines.Count)
        {
            return null;
        }

        var searchStart = eof && lines.Count >= pattern.Count
            ? lines.Count - pattern.Count
            : start;

        var maxStart = lines.Count - pattern.Count;
        if (searchStart > maxStart)
        {
            return null;
        }

        // Exact match
        for (var i = searchStart; i <= maxStart; i++)
        {
            if (MatchAt(lines, pattern, i, static (a, b) => string.Equals(a, b, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        // Trim end match
        for (var i = searchStart; i <= maxStart; i++)
        {
            if (MatchAt(lines, pattern, i, static (a, b) => string.Equals(a.TrimEnd(), b.TrimEnd(), StringComparison.Ordinal)))
            {
                return i;
            }
        }

        // Trim match
        for (var i = searchStart; i <= maxStart; i++)
        {
            if (MatchAt(lines, pattern, i, static (a, b) => string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal)))
            {
                return i;
            }
        }

        // Normalised match
        for (var i = searchStart; i <= maxStart; i++)
        {
            if (MatchAt(lines, pattern, i, static (a, b) => string.Equals(NormalizeForMatch(a), NormalizeForMatch(b), StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return null;
    }

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

internal sealed class KernelApplyPatchException : Exception
{
    public KernelApplyPatchException(string message)
        : base(message)
    {
    }
}






