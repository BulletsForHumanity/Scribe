using System.Text;

namespace Scribe;

public sealed partial class Quill
{
    // ── Shortcuts ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Emit a collection initializer: <c>target = new Type { items... };</c>.
    ///     Handles braces, trailing commas, and indentation automatically.
    ///     Chain <see cref="ContentResult.Padded"/> to wrap in blank lines.
    /// </summary>
    public ContentResult ListInit<T>(
        string target,
        string type,
        IEnumerable<T> items,
        Func<T, string> selector
    )
    {
        var startPos = _body.Length;
        Line($"{target} = {type}");
        Line("{");
        using (Indent())
        {
            foreach (var item in items)
                Line($"{selector(item)},");
        }

        Line("};");
        return new ContentResult(this, startPos);
    }

    // ── Scoping ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Opens a block: writes the header and <c>{</c>, increases indent.
    ///     The header can be a single line or a multi-line raw string literal
    ///     (dedented and indented like <see cref="Lines(string)"/>).
    ///     Returns a <see cref="BlockScope"/> that supports post-decoration with XML docs
    ///     (inserted before the header) and closes the block on dispose.
    ///     Trailing blank lines inside the block are automatically trimmed before <c>}</c>.
    /// </summary>
    public BlockScope Block(string header)
    {
        var headerPos = _body.Length;
        var headerIndent = _indent;

        if (header.Contains("\n"))
            EmitDedentedLines(header);
        else
            _body.Append(' ', _indent * 4).Append(header).Append(NewLine);

        _body.Append(' ', _indent * 4).Append("{").Append(NewLine);
        _indent++;
        return new BlockScope(this, headerPos, headerIndent);
    }

    /// <summary>
    ///     Opens a bare <c>{</c> scope block for variable scoping or grouping.
    ///     Unlike <see cref="Block(string)"/>, has no header line and does not support XML docs.
    /// </summary>
    public BlockScope Scope()
    {
        var pos = _body.Length;
        _body.Append(' ', _indent * 4).Append("{").Append(NewLine);
        _indent++;
        return new BlockScope(this, pos, _indent - 1);
    }

    /// <summary>
    ///     Opens a switch expression block: writes the header and <c>{</c>, increases indent.
    ///     On dispose, writes <c>};</c> (with semicolon, as required by switch expressions).
    ///     <code>
    ///     using (q.SwitchExpr("var ok = segments switch"))
    ///     {
    ///         q.Line("0 => true,");
    ///         q.Line("_ => false,");
    ///     }
    ///     // produces: var ok = segments switch { 0 => true, _ => false, };
    ///     </code>
    /// </summary>
    public SwitchExprScope SwitchExpr(string header)
    {
        if (header.Contains("\n"))
            EmitDedentedLines(header);
        else
            _body.Append(' ', _indent * 4).Append(header).Append(NewLine);

        _body.Append(' ', _indent * 4).Append("{").Append(NewLine);
        _indent++;
        return new SwitchExprScope(this);
    }

    /// <summary>
    ///     Opens a <c>case</c> block: writes <c>case {expression}:</c>, increases indent.
    ///     On dispose, emits <c>break;</c>, restores indent, and adds a trailing blank line.
    /// </summary>
    public CaseScope Case(string expression)
    {
        _body.Append(' ', _indent * 4).Append("case ").Append(expression).Append(":").Append(NewLine);
        _body.Append(' ', _indent * 4).Append("{").Append(NewLine);
        _indent++;
        return new CaseScope(this);
    }

    /// <summary>
    ///     Opens a <c>#region</c> block with the given name.
    ///     On dispose, emits <c>#endregion</c>.
    /// </summary>
    public RegionScope Region(string name)
    {
        _body.Append(' ', _indent * 4).Append("#region ").Append(name).Append(NewLine);
        return new RegionScope(this);
    }

    /// <summary>
    ///     Increase indentation and return a disposable that restores it.
    ///     Prefer <see cref="Scope"/> when braces are appropriate.
    /// </summary>
    public IndentScope Indent()
    {
        _indent++;
        return new IndentScope(this);
    }

    // ── Result & Scope types ─────────────────────────────────────────────

    /// <summary>
    ///     Returned by content-emitting methods (<see cref="Lines(string)"/>,
    ///     <see cref="LinesFor{T}"/>, <see cref="ListInit{T}"/>).
    ///     Supports <see cref="Padded"/> to wrap emitted content with blank lines.
    ///     Implicitly converts back to <see cref="Quill"/> for fluent chaining.
    /// </summary>
    public readonly struct ContentResult
    {
        private readonly Quill _owner;
        private readonly int _startPos;

        internal ContentResult(Quill owner, int startPos)
        {
            _owner = owner;
            _startPos = startPos;
        }

        /// <summary>
        ///     Wrap the just-emitted content with empty lines before and after.
        ///     Inserts a blank line before the content (unless at start of block or already separated),
        ///     and appends a blank line after. Blocks already handle their own spacing —
        ///     use this for <c>Lines</c>, <c>LinesFor</c>, and <c>ListInit</c> content.
        /// </summary>
        public Quill Padded()
        {
            _owner._body.Append(NewLine);

            if (_owner.NeedsBlankLineSeparationAt(_startPos))
                _owner._body.Insert(_startPos, NewLine);

            return _owner;
        }

        public static implicit operator Quill(ContentResult result) => result._owner;
    }

    /// <summary>
    ///     Disposable that closes a <c>{</c> block with <c>}</c> and decreases indent.
    ///     Supports post-decoration with XML documentation via <see cref="Summary"/>,
    ///     <see cref="Remarks"/>, <see cref="Param"/>, <see cref="TypeParam"/>,
    ///     <see cref="Returns"/>, <see cref="InheritDoc"/>, <see cref="Example"/>,
    ///     <see cref="Exception"/>, <see cref="SeeAlso"/>, and <see cref="Attribute"/> — these are inserted before the block header.
    ///     Trailing blank lines inside the block are automatically trimmed before <c>}</c>.
    /// </summary>
    public struct BlockScope : IDisposable
    {
        private readonly Quill _owner;
        private readonly int _headerPos;
        private readonly int _headerIndent;
        private int _insertLen;

        internal BlockScope(Quill owner, int headerPos, int headerIndent)
        {
            _owner = owner;
            _headerPos = headerPos;
            _headerIndent = headerIndent;
            _insertLen = 0;
        }

        /// <summary>Insert a <c>&lt;summary&gt;</c> XML doc before the block header.</summary>
        public BlockScope Summary(string text)
        {
            InsertXmlTag("summary", text);
            return this;
        }

        /// <summary>Insert a <c>&lt;remarks&gt;</c> XML doc before the block header.</summary>
        public BlockScope Remarks(string text)
        {
            InsertXmlTag("remarks", text);
            return this;
        }

        /// <summary>Insert a <c>&lt;param name="..."&gt;</c> XML doc line before the block header.</summary>
        public BlockScope Param(string name, string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(prefix + "<param name=\"" + name + "\">" + description + "</param>" + NewLine);
            return this;
        }

        /// <summary>Insert a <c>&lt;typeparam name="..."&gt;</c> XML doc line before the block header.</summary>
        public BlockScope TypeParam(string name, string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(
                prefix + "<typeparam name=\"" + name + "\">" + description + "</typeparam>" + NewLine
            );
            return this;
        }

        /// <summary>Insert a <c>&lt;returns&gt;</c> XML doc line before the block header.</summary>
        public BlockScope Returns(string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(prefix + "<returns>" + description + "</returns>" + NewLine);
            return this;
        }

        /// <summary>Insert a <c>&lt;inheritdoc /&gt;</c> or <c>&lt;inheritdoc cref="..." /&gt;</c> before the block header.</summary>
        public BlockScope InheritDoc(string? cref = null)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            if (cref is null)
                InsertDoc(prefix + "<inheritdoc />" + NewLine);
            else
                InsertDoc(prefix + "<inheritdoc cref=\"" + cref + "\" />" + NewLine);
            return this;
        }

        /// <summary>Insert an <c>&lt;example&gt;</c> XML doc block before the block header.</summary>
        public BlockScope Example(string text)
        {
            InsertXmlTag("example", text);
            return this;
        }

        /// <summary>Insert an <c>&lt;exception cref="..."&gt;</c> XML doc line before the block header.</summary>
        public BlockScope Exception(string cref, string description)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(
                prefix + "<exception cref=\"" + cref + "\">" + description + "</exception>" + NewLine
            );
            return this;
        }

        /// <summary>Insert a <c>&lt;seealso cref="..." /&gt;</c> XML doc line before the block header.</summary>
        public BlockScope SeeAlso(string cref)
        {
            var prefix = new string(' ', _headerIndent * 4) + "/// ";
            InsertDoc(prefix + "<seealso cref=\"" + cref + "\" />" + NewLine);
            return this;
        }

        /// <summary>Insert an attribute (<c>[Name]</c> or <c>[Name(args)]</c>) before the block header, after any XML docs.</summary>
        public BlockScope Attribute(string name, string? args = null)
        {
            var sb = new StringBuilder();
            sb.Append(' ', _headerIndent * 4).Append('[').Append(name);
            if (args is not null)
                sb.Append('(').Append(args).Append(')');
            sb.Append("]").Append(NewLine);
            InsertDoc(sb.ToString());
            return this;
        }

        private void InsertXmlTag(string tag, string text) =>
            InsertDoc(_owner.BuildXmlDocString(tag, text, _headerIndent));

        private void InsertDoc(string doc)
        {
            if (_insertLen is 0 && _owner.NeedsBlankLineSeparationAt(_headerPos))
            {
                _owner._body.Insert(_headerPos, NewLine);
                _insertLen += 1;
            }

            _owner._body.Insert(_headerPos + _insertLen, doc);
            _insertLen += doc.Length;
        }

        public void Dispose()
        {
            _owner.TrimTrailingBlankLines();
            _owner._indent--;
            _owner.Line("}");
        }
    }

    /// <summary>
    ///     Disposable that closes a <c>case</c> block: emits <c>break;</c>,
    ///     decreases indent, and adds a trailing blank line.
    /// </summary>
    public readonly struct CaseScope(Quill owner) : IDisposable
    {
        public void Dispose()
        {
            owner.Line("break;");
            owner._indent--;
            owner._body.Append(' ', owner._indent * 4).Append("}").Append(NewLine);
            owner._body.Append(NewLine);
        }
    }

    /// <summary>
    ///     Disposable that closes a switch expression block with <c>};</c> (semicolon).
    ///     Returned by <see cref="SwitchExpr"/>.
    /// </summary>
    public readonly struct SwitchExprScope(Quill owner) : IDisposable
    {
        public void Dispose()
        {
            owner.TrimTrailingBlankLines();
            owner._indent--;
            owner._body.Append(' ', owner._indent * 4).Append("};").Append(NewLine);
        }
    }

    /// <summary>
    ///     Disposable that decreases indent when disposed.
    ///     Returned by <see cref="Indent()"/>.
    /// </summary>
    public readonly struct IndentScope(Quill owner) : IDisposable
    {
        public void Dispose() => owner._indent = Math.Max(0, owner._indent - 1);
    }

    /// <summary>
    ///     Disposable that closes a <c>#region</c> block with <c>#endregion</c>.
    ///     Returned by <see cref="Region"/>.
    /// </summary>
    public readonly struct RegionScope(Quill owner) : IDisposable
    {
        public void Dispose()
        {
            owner._body.Append(' ', owner._indent * 4).Append("#endregion").Append(NewLine);
        }
    }
}
