using System.Text;

namespace Scribe;

public sealed partial class Quill
{
    // ── XML Documentation (standalone — for one-liner members) ─────────

    /// <summary>
    ///     Emit a <c>/// &lt;summary&gt;</c> XML doc block at the current indentation level.
    ///     Use this for standalone one-liner members (operators, fields, properties)
    ///     that don't use <see cref="Block(string)"/>. For members with blocks,
    ///     prefer <see cref="BlockScope.Summary"/> (post-decoration).
    /// </summary>
    public Quill Summary(string text)
    {
        _body.Append(BuildXmlDocString("summary", text, _indent));
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;remarks&gt;</c> XML doc block at the current indentation level.
    /// </summary>
    public Quill Remarks(string text)
    {
        _body.Append(BuildXmlDocString("remarks", text, _indent));
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;param name="..."&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill Param(string name, string text)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <param name=\"")
            .Append(name)
            .Append("\">")
            .Append(text)
            .Append("</param>").Append(NewLine);
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;inheritdoc /&gt;</c> or <c>/// &lt;inheritdoc cref="..." /&gt;</c>
    ///     at the current indentation level.
    /// </summary>
    public Quill InheritDoc(string? cref = null)
    {
        _body.Append(' ', _indent * 4);
        if (cref is null)
            _body.Append("/// <inheritdoc />").Append(NewLine);
        else
            _body.Append("/// <inheritdoc cref=\"").Append(cref).Append("\" />").Append(NewLine);
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;typeparam name="..."&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill TypeParam(string name, string description)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <typeparam name=\"")
            .Append(name)
            .Append("\">")
            .Append(description)
            .Append("</typeparam>").Append(NewLine);
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;returns&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill Returns(string description)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <returns>")
            .Append(description)
            .Append("</returns>").Append(NewLine);
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;example&gt;</c> XML doc block at the current indentation level.
    /// </summary>
    public Quill Example(string text)
    {
        _body.Append(BuildXmlDocString("example", text, _indent));
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;exception cref="..."&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill Exception(string cref, string description)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <exception cref=\"")
            .Append(cref)
            .Append("\">")
            .Append(description)
            .Append("</exception>").Append(NewLine);
        return this;
    }

    /// <summary>
    ///     Emit a <c>/// &lt;seealso cref="..." /&gt;</c> XML doc line at the current indentation level.
    /// </summary>
    public Quill SeeAlso(string cref)
    {
        _body
            .Append(' ', _indent * 4)
            .Append("/// <seealso cref=\"")
            .Append(cref)
            .Append("\" />").Append(NewLine);
        return this;
    }

    // ── Attributes ───────────────────────────────────────────────────────

    /// <summary>
    ///     Emit an attribute line: <c>[AttributeName]</c> or <c>[AttributeName(args)]</c>
    ///     at the current indentation level.
    /// </summary>
    public Quill Attribute(string name, string? args = null)
    {
        _body.Append(' ', _indent * 4).Append('[').Append(name);
        if (args is not null)
            _body.Append('(').Append(args).Append(')');
        _body.Append("]").Append(NewLine);
        return this;
    }
}
