using System.Text;

namespace WindowSill.ClipboardHistory.Core;

internal sealed record TextComparisonResult(string Diff, int Added, int Removed, bool IsTruncated);

internal static class TextComparison
{
    /// <summary>Uses bounded work and output even when a selected clipboard entry is very large.</summary>
    internal static TextComparisonResult Compare(string before, string after, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const int maxCharacters = 200_000, maxLines = 1500, maxOutput = 300_000;
        bool truncated = before.Length > maxCharacters || after.Length > maxCharacters;
        string[] left = before.Length == 0 ? [] : before[..Math.Min(before.Length, maxCharacters)].Replace("\r\n", "\n").Split('\n');
        string[] right = after.Length == 0 ? [] : after[..Math.Min(after.Length, maxCharacters)].Replace("\r\n", "\n").Split('\n');
        truncated |= left.Length > maxLines || right.Length > maxLines;
        left = left.Take(maxLines).ToArray(); right = right.Take(maxLines).ToArray();
        var lengths = new ushort[left.Length + 1, right.Length + 1];
        for (int i = left.Length - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int j = right.Length - 1; j >= 0; j--)
                lengths[i, j] = left[i] == right[j] ? (ushort)(1 + lengths[i + 1, j + 1]) : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        }
        var output = new StringBuilder();
        int x = 0, y = 0, added = 0, removed = 0;
        while ((x < left.Length || y < right.Length) && output.Length < maxOutput)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (x < left.Length && y < right.Length && left[x] == right[y]) { output.Append("  ").AppendLine(left[x++]); y++; }
            else if (y < right.Length && (x == left.Length || lengths[x, y + 1] > lengths[x + 1, y]))
            { output.Append("+ ").AppendLine(right[y++]); added++; }
            else { output.Append("- ").AppendLine(left[x++]); removed++; }
        }
        truncated |= x < left.Length || y < right.Length;
        if (output.Length > maxOutput) { output.Length = maxOutput; truncated = true; }
        return new(output.ToString(), added, removed, truncated);
    }
}
