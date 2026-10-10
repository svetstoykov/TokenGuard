using System.Text;
using System.Text.Json;
using WorkspaceModel = Codexplorer.Workspace.Workspace;

namespace Codexplorer.Tools;

/// <summary>
///     Replaces one exact occurrence of a text in an existing file of the cloned repository.
/// </summary>
/// <remarks>
///     <para>
///         The text to replace must occur exactly once, so an edit lands where the model intends or fails without
///         changing the file. The result names the path and the changed line range and leaves the contents out.
///     </para>
///     <para>
///         The tool edits existing text files only. Paths under <c>.git</c> are refused, so the clone can always be
///         restored to its commit.
///     </para>
/// </remarks>
public sealed class EditFileTool : IWorkspaceTool
{
    /// <summary>
    ///     The tool name exposed to the model.
    /// </summary>
    public const string ToolName = "edit_file";

    private const string ErrorPrefix = "Error:";

    private static readonly byte[] Utf8Preamble = Encoding.UTF8.GetPreamble();

    private static readonly ToolSchema CachedSchema = ToolSchema.CreateFunction(
        ToolName,
        "Replace one exact occurrence of oldText with newText in one existing workspace-relative text file. Fails without changing the file when oldText is absent or occurs more than once; include enough surrounding text to make it unique. Cannot create, delete, or rename files.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": {
              "type": "string",
              "description": "Workspace-relative path of the existing file to edit."
            },
            "oldText": {
              "type": "string",
              "description": "Exact text to replace, including whitespace and indentation. Must occur exactly once in the file."
            },
            "newText": {
              "type": "string",
              "description": "Text that replaces oldText. Use an empty string to delete oldText."
            }
          },
          "required": ["path", "oldText", "newText"]
        }
        """);

    /// <summary>
    ///     Gets the tool name exposed to the model.
    /// </summary>
    public string Name => ToolName;

    /// <summary>
    ///     Gets the cached OpenAI-compatible schema for this tool.
    /// </summary>
    public ToolSchema Schema => CachedSchema;

    /// <summary>
    ///     Represents the arguments of <see cref="EditFileTool" />.
    /// </summary>
    /// <param name="Path">The workspace-relative path of the file to edit.</param>
    /// <param name="OldText">The exact text to replace. It must occur exactly once in the file.</param>
    /// <param name="NewText">The replacement text. An empty string deletes <paramref name="OldText" />.</param>
    public sealed record Parameters(string Path, string OldText, string NewText);

    /// <summary>
    ///     Determines whether a result returned by this tool reports an applied edit.
    /// </summary>
    /// <param name="toolResult">The text returned by <see cref="HandleAsync" />. Cannot be <see langword="null" />.</param>
    /// <returns><see langword="true" /> if the file was changed; otherwise, <see langword="false" />.</returns>
    public static bool IsSuccess(string toolResult)
    {
        ArgumentNullException.ThrowIfNull(toolResult);

        return !toolResult.StartsWith(ErrorPrefix, StringComparison.Ordinal);
    }

    Task<string> IWorkspaceTool.ExecuteAsync(JsonElement arguments, WorkspaceModel workspace, CancellationToken ct)
    {
        return this.HandleAsync(ToolRegistry.DeserializeArguments<Parameters>(arguments), workspace, ct);
    }

    /// <summary>
    ///     Asynchronously replaces the single occurrence of the old text in one file.
    /// </summary>
    /// <param name="parameters">The typed tool arguments. Cannot be <see langword="null" />.</param>
    /// <param name="workspace">The workspace that constrains file access. Cannot be <see langword="null" />.</param>
    /// <param name="ct">A <see cref="CancellationToken" /> to observe while waiting for the task to complete.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the edited path with its changed line
    ///     range, or a recoverable error text when the file is left unchanged.
    /// </returns>
    /// <exception cref="PathEscapeException">The path leaves the workspace.</exception>
    public async Task<string> HandleAsync(Parameters parameters, WorkspaceModel workspace, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(workspace);

        var requestedPath = string.IsNullOrWhiteSpace(parameters.Path) ? "." : parameters.Path;
        var resolvedPath = PathGuard.ResolvePath(workspace.LocalPath, requestedPath);
        var displayPath = ToolResultFormatting.ToWorkspaceRelativePath(workspace, resolvedPath);

        if (displayPath == ".git" || displayPath.StartsWith(".git/", StringComparison.Ordinal))
        {
            return $"{ErrorPrefix} path is inside the .git directory and cannot be edited: {displayPath}";
        }

        if (Directory.Exists(resolvedPath))
        {
            return $"{ErrorPrefix} path is a directory: {displayPath}";
        }

        if (!File.Exists(resolvedPath))
        {
            return $"{ErrorPrefix} file not found: {displayPath}. edit_file changes existing files only.";
        }

        if (string.IsNullOrEmpty(parameters.OldText))
        {
            return $"{ErrorPrefix} oldText is empty";
        }

        if (await ToolFileHelpers.IsBinaryFileAsync(resolvedPath, ct).ConfigureAwait(false))
        {
            return $"{ErrorPrefix} binary file, cannot edit as text";
        }

        var bytes = await File.ReadAllBytesAsync(resolvedPath, ct).ConfigureAwait(false);
        var hasPreamble = bytes.AsSpan().StartsWith(Utf8Preamble);
        var content = Encoding.UTF8.GetString(bytes.AsSpan(hasPreamble ? Utf8Preamble.Length : 0));

        // The read tools return lines joined by the platform newline, so text copied from a CRLF file arrives with LF.
        var usesCrlf = content.Contains("\r\n", StringComparison.Ordinal);
        var oldText = usesCrlf ? ToCrlf(parameters.OldText) : parameters.OldText;
        var newText = usesCrlf ? ToCrlf(parameters.NewText ?? string.Empty) : parameters.NewText ?? string.Empty;

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            return $"{ErrorPrefix} oldText and newText are identical";
        }

        var occurrences = CountOccurrences(content, oldText);

        if (occurrences == 0)
        {
            return $"{ErrorPrefix} oldText not found in {displayPath}. Read the file again and copy the text exactly.";
        }

        if (occurrences > 1)
        {
            return $"{ErrorPrefix} oldText occurs {occurrences} times in {displayPath}. Include more surrounding text so it occurs once.";
        }

        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        var edited = string.Concat(content.AsSpan(0, index), newText, content.AsSpan(index + oldText.Length));
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasPreamble);

        await File.WriteAllTextAsync(resolvedPath, edited, encoding, ct).ConfigureAwait(false);

        var startLine = CountLineBreaks(content.AsSpan(0, index)) + 1;
        var oldEndLine = startLine + CountLineBreaks(oldText);
        var newEndLine = startLine + CountLineBreaks(newText);

        return $"Edited {displayPath}: replaced lines {startLine}-{oldEndLine}; the new text is at lines {startLine}-{newEndLine}.";
    }

    private static int CountOccurrences(string content, string text)
    {
        var count = 0;
        var index = content.IndexOf(text, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = content.IndexOf(text, index + text.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static int CountLineBreaks(ReadOnlySpan<char> text) => text.Count('\n');

    private static string ToCrlf(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
    }
}
