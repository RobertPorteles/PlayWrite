using System.Text.Json.Nodes;

namespace PlaywrightTests.Shared.Api;

/// <summary>
/// Leituras seguras de JSON da API (JsonValue.GetValue não aceita tipos anuláveis).
/// </summary>
public static class JsonExtensions
{
    public static long Id(this JsonNode? registro) =>
        registro?["id"]?.GetValue<long>()
        ?? throw new InvalidOperationException($"Registro sem 'id': {registro?.ToJsonString()}");

    public static long? ComoLong(this JsonNode? valor) => valor?.GetValue<long>();

    public static int? ComoInt(this JsonNode? valor) => valor?.GetValue<int>();

    public static bool? ComoBool(this JsonNode? valor) => valor?.GetValue<bool>();

    public static string? ComoTexto(this JsonNode? valor) => valor?.ToString();

    /// <summary>Registros de uma resposta paginada (PageResultModel) ou de uma lista simples.</summary>
    public static JsonArray Registros(this JsonNode? pagina) =>
        pagina as JsonArray
        ?? pagina?["items"] as JsonArray
        ?? pagina?["value"] as JsonArray
        ?? new JsonArray();
}
