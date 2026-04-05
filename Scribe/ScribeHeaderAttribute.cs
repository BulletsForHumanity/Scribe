namespace Scribe;

/// <summary>
///     Assembly-level attribute that provides a header for Scribe-generated source files.
///     When present, <see cref="Quill.Inscribe"/> renders a decorative page header containing
///     the specified text, the Scribe attribution, and the repository URL.
/// </summary>
/// <example>
///     <code>
///     // In the generator project's Scribe.cs or AssemblyInfo.cs:
///     [assembly: ScribeHeader("My Generator")]
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ScribeHeaderAttribute : Attribute
{
    /// <summary>The header text to display on the generated page.</summary>
    public string Header { get; }

    /// <summary>Creates a new <see cref="ScribeHeaderAttribute"/> with the specified header text.</summary>
    /// <param name="header">The header text to display in generated source file headers.</param>
    public ScribeHeaderAttribute(string header) => Header = header;
}
