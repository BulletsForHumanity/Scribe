namespace Scribe;

/// <summary>
///     Minimal template substitution: replaces <c>{{key}}</c> markers with values.
///     No control flow, no escaping, no nesting — for structural shells only.
///     Use variant templates + C# branching for conditional sections.
/// </summary>
public static class Template
{
    /// <summary>
    ///     Replace all <c>{{key}}</c> markers in <paramref name="template"/> with the corresponding values.
    /// </summary>
    public static string Apply(string template, params (string Key, string Value)[] parameters)
    {
        var result = template;
        foreach (var (key, value) in parameters)
        {
            result = result.Replace("{{" + key + "}}", value);
        }

        return result;
    }

    /// <summary>
    ///     Replace all <c>{{key}}</c> markers using a dictionary.
    /// </summary>
    public static string Apply(string template, Dictionary<string, string> parameters)
    {
        var result = template;
        foreach (var kvp in parameters)
        {
            result = result.Replace("{{" + kvp.Key + "}}", kvp.Value);
        }

        return result;
    }
}
