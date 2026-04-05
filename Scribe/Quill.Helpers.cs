using System.Text;

namespace Scribe;

public sealed partial class Quill
{
    // ── Internal helpers ─────────────────────────────────────────────────

    /// <summary>
    ///     Ensure there is a blank line before the current position in the body,
    ///     unless we are at the start of the body or right after an opening <c>{</c>.
    /// </summary>
    internal void EnsureBlankLineSeparation()
    {
        if (_body.Length is 0)
            return;

        var pos = _body.Length - 1;
        if (pos >= 0 && _body[pos] == '\n')
        {
            var scanFrom = pos - 1;
            if (scanFrom >= 0 && _body[scanFrom] == '\r')
                scanFrom--;

            var lineStart = scanFrom;
            while (lineStart >= 0 && _body[lineStart] != '\n')
                lineStart--;

            lineStart++;

            var lineContent = _body.ToString(lineStart, scanFrom - lineStart + 1).Trim();
            if (lineContent.Length is 0 || lineContent == "{" || lineContent.StartsWith("//"))
                return;
        }

        _body.Append(NewLine);
    }

    /// <summary>
    ///     Check whether a blank-line separator is needed at a specific position in the body.
    /// </summary>
    internal bool NeedsBlankLineSeparationAt(int pos)
    {
        if (pos is 0)
            return false;

        var idx = pos - 1;
        if (idx < 0 || _body[idx] != '\n')
            return true;

        idx--;
        if (idx >= 0 && _body[idx] == '\r')
            idx--;

        var lineStart = idx;
        while (lineStart >= 0 && _body[lineStart] != '\n')
            lineStart--;

        lineStart++;

        if (lineStart > idx)
            return false;

        var lineContent = _body.ToString(lineStart, idx - lineStart + 1).Trim();
        return lineContent.Length > 0 && lineContent != "{" && !lineContent.StartsWith("//");
    }

    /// <summary>Build an XML doc tag block and return it as a string for insertion.</summary>
    internal string BuildXmlDocString(string tag, string text, int indent)
    {
        var sb = new StringBuilder();
        var lines = DedentRawString(text);
        var prefix = new string(' ', indent * 4) + "/// ";

        if (lines.Length is 1)
        {
            sb.Append(prefix)
                .Append('<')
                .Append(tag)
                .Append('>')
                .Append(lines[0])
                .Append("</")
                .Append(tag)
                .Append(">").Append(NewLine);
        }
        else
        {
            sb.Append(prefix).Append('<').Append(tag).Append(">").Append(NewLine);
            foreach (var line in lines)
            {
                if (line.Length is 0)
                    sb.Append(prefix).Append(NewLine);
                else
                    sb.Append(prefix).Append("    ").Append(line).Append(NewLine);
            }

            sb.Append(prefix).Append("</").Append(tag).Append(">").Append(NewLine);
        }

        return sb.ToString();
    }

    /// <summary>Dedent a raw string literal into individual lines, trimming surrounding blanks.</summary>
    internal static string[] DedentRawString(string text)
    {
        var rawLines = text.Split('\n');

        var start = 0;
        while (start < rawLines.Length && rawLines[start].TrimEnd('\r').Trim().Length is 0)
            start++;

        var end = rawLines.Length - 1;
        while (end > start && rawLines[end].TrimEnd('\r').Trim().Length is 0)
            end--;

        if (start > end)
            return [text.Trim()];

        var minIndent = int.MaxValue;
        for (var i = start; i <= end; i++)
        {
            var line = rawLines[i].TrimEnd('\r');
            if (line.Trim().Length is 0)
                continue;

            var leading = 0;
            while (leading < line.Length && line[leading] == ' ')
                leading++;

            if (leading < minIndent)
                minIndent = leading;
        }

        if (minIndent == int.MaxValue)
            minIndent = 0;

        var result = new string[end - start + 1];
        for (var i = start; i <= end; i++)
        {
            var line = rawLines[i].TrimEnd('\r');
            result[i - start] = line.Trim().Length is 0
                ? ""
                : (line.Length > minIndent ? line.Substring(minIndent) : "");
        }

        return result;
    }

    /// <summary>Dedent a raw string literal and emit each line at current indentation.</summary>
    private void EmitDedentedLines(string text)
    {
        foreach (var line in DedentRawString(text))
        {
            if (line.Length is 0)
                _body.Append(NewLine);
            else
                _body.Append(' ', _indent * 4).Append(line).Append(NewLine);
        }
    }

    /// <summary>Remove trailing blank lines from the body (used before closing a block).</summary>
    internal void TrimTrailingBlankLines()
    {
        while (_body.Length >= 2)
        {
            if (_body[_body.Length - 1] == '\n' && _body[_body.Length - 2] == '\r')
            {
                if (
                    _body.Length >= 4
                    && _body[_body.Length - 3] == '\n'
                    && _body[_body.Length - 4] == '\r'
                )
                {
                    _body.Length -= 2;
                    continue;
                }

                if (_body.Length >= 3 && _body[_body.Length - 3] == '\n')
                {
                    _body.Length -= 2;
                    continue;
                }

                break;
            }

            if (_body[_body.Length - 1] == '\n')
            {
                if (_body.Length >= 2 && _body[_body.Length - 2] == '\n')
                {
                    _body.Length -= 1;
                    continue;
                }

                break;
            }

            break;
        }
    }
}
