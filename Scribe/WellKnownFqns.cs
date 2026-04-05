namespace Scribe;

/// <summary>
///     Fully-qualified <c>global::</c> type names for well-known BCL and framework types
///     used in generated source output. Reduces <c>global::System...</c> noise in generators
///     and keeps the strings in one auditable location.
/// </summary>
public static class WellKnownFqns
{
    // ── System ───────────────────────────────────────────────────────────

    public const string Type = "global::System.Type";
    public const string Guid = "global::System.Guid";

    // ── System.Threading.Tasks ───────────────────────────────────────────

    public const string Task = "global::System.Threading.Tasks.Task";
    public const string CancellationToken = "global::System.Threading.CancellationToken";

    // ── System.Collections.Generic ──────────────────────────────────────

    public const string List = "global::System.Collections.Generic.List";
    public const string Dictionary = "global::System.Collections.Generic.Dictionary";

    // ── System.Text.Json ────────────────────────────────────────────────

    public const string JsonSerializer = "global::System.Text.Json.JsonSerializer";
    public const string JsonSerializerOptions = "global::System.Text.Json.JsonSerializerOptions";
    public const string Utf8JsonReader = "global::System.Text.Json.Utf8JsonReader";
    public const string Utf8JsonWriter = "global::System.Text.Json.Utf8JsonWriter";
    public const string JsonConverter = "global::System.Text.Json.Serialization.JsonConverter";

    // ── System.Text.Json.Nodes ──────────────────────────────────────────

    public const string JsonNode = "global::System.Text.Json.Nodes.JsonNode";
    public const string JsonValue = "global::System.Text.Json.Nodes.JsonValue";

    // ── Microsoft.OpenApi ───────────────────────────────────────────────

    public const string OpenApiSchema = "global::Microsoft.OpenApi.OpenApiSchema";
    public const string JsonSchemaType = "global::Microsoft.OpenApi.JsonSchemaType";
    public const string IOpenApiSchemaTransformer =
        "global::Microsoft.AspNetCore.OpenApi.IOpenApiSchemaTransformer";
    public const string OpenApiSchemaTransformerCtx =
        "global::Microsoft.AspNetCore.OpenApi.OpenApiSchemaTransformerContext";

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Build a generic <c>global::</c> type reference, e.g.
    ///     <c>WellKnownFqns.Generic(WellKnownFqns.List, WellKnownFqns.JsonNode)</c>
    ///     produces <c>"global::System.Collections.Generic.List&lt;global::System.Text.Json.Nodes.JsonNode&gt;"</c>.
    /// </summary>
    public static string Generic(this string openType, params string[] typeArgs) =>
        openType + "<" + string.Join(", ", typeArgs) + ">";
}
