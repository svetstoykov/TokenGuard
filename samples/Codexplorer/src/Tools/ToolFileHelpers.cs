using System.Text;

namespace Codexplorer.Tools;

internal static class ToolFileHelpers
{
    public static async Task<bool> IsBinaryFileAsync(string path, CancellationToken ct)
    {
        const int probeLength = 4096;
        var buffer = new byte[probeLength];

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: probeLength,
            useAsync: true);

        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, probeLength), ct).ConfigureAwait(false);

        for (var index = 0; index < bytesRead; index++)
        {
            if (buffer[index] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildTextResult(IReadOnlyList<string> lines, int totalLineCount, int cap)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        if (totalLineCount <= cap)
        {
            return string.Join(Environment.NewLine, lines);
        }

        var builder = new StringBuilder();
        builder.AppendJoin(Environment.NewLine, lines);
        builder.AppendLine();
        builder.Append(ToolResultFormatting.TruncationMarker(totalLineCount - lines.Count, cap, "lines"));
        return builder.ToString();
    }
}
