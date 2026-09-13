using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Shared.Api;

/// <summary>
/// Cliente HTTP autenticado na CCO.Api, usado para preparar, verificar e limpar dados dos testes.
/// </summary>
public sealed class ApiClient : IAsyncDisposable
{
    private readonly IAPIRequestContext _request;

    private ApiClient(IAPIRequestContext request, string token)
    {
        _request = request;
        Token = token;
    }

    public string Token { get; }

    /// <summary>
    /// Autentica como o front: <c>login</c> e, se o usuário tiver várias empresas, <c>login/empresa</c>.
    /// </summary>
    public static async Task<ApiClient> LoginAsync(IPlaywright playwright, string login, string senha, string? empresa)
    {
        var anonimo = await NovoContextoAsync(playwright, token: null);
        string token;
        try
        {
            var retorno = (await EnviarAsync(anonimo, "POST", "v1/Autenticador/login",
                new JsonObject { ["login"] = login, ["senha"] = senha })).GarantirSucesso().Json;

            token = retorno?["token"]?.GetValue<string>() ?? "";
            if (token.Length == 0)
            {
                var empresaId = EscolherEmpresa(retorno?["empresas"] as JsonArray, empresa);
                var retornoEmpresa = (await EnviarAsync(anonimo, "POST", "v1/Autenticador/login/empresa",
                    new JsonObject { ["login"] = login, ["senha"] = senha, ["empresaId"] = empresaId })).GarantirSucesso().Json;

                token = retornoEmpresa?["token"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("O login por empresa não retornou token.");
            }
        }
        finally
        {
            await anonimo.DisposeAsync();
        }

        return new ApiClient(await NovoContextoAsync(playwright, token), token);
    }

    public async Task<JsonNode?> GetAsync(string caminho, string? include = null) =>
        (await EnviarAsync(_request, "GET", caminho, include: include)).GarantirSucesso().Json;

    public async Task<JsonNode?> PostAsync(string caminho, JsonNode corpo) =>
        (await EnviarAsync(_request, "POST", caminho, corpo)).GarantirSucesso().Json;

    /// <summary>Não lança exceção: quem chama decide o que fazer com 404/409.</summary>
    public Task<ApiResposta> DeleteAsync(string caminho) => EnviarAsync(_request, "DELETE", caminho);

    public Task<ApiResposta> EnviarAsync(string metodo, string caminho, JsonNode? corpo = null, string? include = null) =>
        EnviarAsync(_request, metodo, caminho, corpo, include);

    public ValueTask DisposeAsync() => _request.DisposeAsync();

    private static Task<IAPIRequestContext> NovoContextoAsync(IPlaywright playwright, string? token)
    {
        var headers = new Dictionary<string, string> { ["Accept-Language"] = "pt-BR" };
        if (token is not null)
        {
            headers["Authorization"] = $"Bearer {token}";
        }

        return playwright.APIRequest.NewContextAsync(new() { BaseURL = E2ESettings.ApiUrl, ExtraHTTPHeaders = headers });
    }

    private static async Task<ApiResposta> EnviarAsync(IAPIRequestContext contexto, string metodo, string caminho,
        JsonNode? corpo = null, string? include = null)
    {
        var headers = new Dictionary<string, string>();
        var opcoes = new APIRequestContextOptions { Method = metodo, Headers = headers, FailOnStatusCode = false };
        if (include is not null)
        {
            headers["include"] = include;
        }
        if (corpo is not null)
        {
            headers["Content-Type"] = "application/json";
            opcoes.Data = corpo.ToJsonString();
        }

        var resposta = await contexto.FetchAsync(caminho.TrimStart('/'), opcoes);
        var texto = await resposta.TextAsync();
        return new ApiResposta(metodo, caminho, resposta.Status, texto, LerJson(texto));
    }

    private static JsonNode? LerJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(texto);
        }
        catch (JsonException)
        {
            return JsonValue.Create(texto);
        }
    }

    private static string EscolherEmpresa(JsonArray? empresas, string? empresa)
    {
        var lista = (empresas ?? [])
            .Select(e => (Id: e?["id"]?.ToString() ?? "", Nome: e?["descricao"]?.ToString() ?? ""))
            .ToList();

        if (lista.Count == 0)
        {
            throw new InvalidOperationException("O login não retornou token nem empresas; verifique E2E_LOGIN e E2E_SENHA.");
        }

        if (empresa is null)
        {
            return lista.Count == 1
                ? lista[0].Id
                : throw new InvalidOperationException(
                    $"O usuário tem {lista.Count} empresas; defina E2E_EMPRESA com uma delas: {string.Join(" | ", lista.Select(e => e.Nome))}");
        }

        var escolhida = lista.FirstOrDefault(e =>
            string.Equals(e.Nome.Trim(), empresa, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Id, empresa, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrEmpty(escolhida.Id)
            ? throw new InvalidOperationException(
                $"Empresa '{empresa}' não encontrada para o usuário. Disponíveis: {string.Join(" | ", lista.Select(e => e.Nome))}")
            : escolhida.Id;
    }
}

public sealed record ApiResposta(string Metodo, string Caminho, int Status, string Corpo, JsonNode? Json)
{
    public bool Sucesso => Status is >= 200 and < 300;

    public ApiResposta GarantirSucesso() => Sucesso ? this : throw new ApiException(this);
}

public sealed class ApiException(ApiResposta resposta)
    : Exception($"{resposta.Metodo} {resposta.Caminho} retornou {resposta.Status}: {Resumir(resposta.Corpo)}")
{
    public ApiResposta Resposta { get; } = resposta;

    private static string Resumir(string corpo) => corpo.Length <= 800 ? corpo : corpo[..800] + "…";
}
