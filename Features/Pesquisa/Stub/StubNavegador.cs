using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Pesquisa.Stub;

/// <summary>
/// Respostas da CCO.Api simuladas no próprio navegador (Page.RouteAsync), no padrão do ApiEquipamentoStub.
/// Toda chamada legada (clienteAntigo, Concentrador, ConectaWEB) sem resposta prevista é respondida com [] e registrada em
/// <see cref="Legadas"/>: localmente essas rotas saem da máquina para o concentrador31/ConectaWeb.
/// As respostas de <see cref="Responder"/> valem só para o teste corrente (cada teste tem sua página).
/// </summary>
public sealed class StubNavegador
{
    private static readonly string[] PrefixosLegados = ["/v1/clienteAntigo/", "/v1/Concentrador/", "/v1/ConectaWEB/"];

    private readonly ConcurrentQueue<string> _legadas = new();

    private readonly List<(string Trecho, Func<IRequest, JsonNode?> Resposta)> _respostas = [];

    private StubNavegador()
    {
    }

    /// <summary>Chamadas legadas que a tela fez sem uma resposta prevista pelo teste.</summary>
    public IReadOnlyList<string> Legadas => _legadas.ToArray();

    public static async Task<StubNavegador> InstalarAsync(IPage page)
    {
        var stub = new StubNavegador();
        await page.RouteAsync(url => stub.Atende(new Uri(url).AbsolutePath), stub.ResponderRotaAsync);
        return stub;
    }

    /// <summary>Responde com JSON toda requisição cujo caminho contém <paramref name="trecho"/> (sem diferenciar maiúsculas).</summary>
    public void Responder(string trecho, Func<IRequest, JsonNode?> resposta) => _respostas.Add((trecho, resposta));

    private bool Atende(string caminho) =>
        Resposta(caminho) is not null || PrefixosLegados.Any(p => caminho.Contains(p, StringComparison.OrdinalIgnoreCase));

    private Func<IRequest, JsonNode?>? Resposta(string caminho) =>
        _respostas.LastOrDefault(r => caminho.Contains(r.Trecho, StringComparison.OrdinalIgnoreCase)).Resposta;

    private async Task ResponderRotaAsync(IRoute rota)
    {
        var requisicao = rota.Request;
        var caminho = new Uri(requisicao.Url).AbsolutePath;
        var resposta = Resposta(caminho);
        if (resposta is null)
        {
            _legadas.Enqueue($"{requisicao.Method} {caminho}");
        }

        await rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = resposta is null ? "[]" : resposta(requisicao)?.ToJsonString() ?? "null",
            // O front (5173) chama a API (5225) em outra origem: sem estes headers o navegador descarta a resposta.
            Headers = new Dictionary<string, string>
            {
                ["Access-Control-Allow-Origin"] = requisicao.Headers.GetValueOrDefault("origin") ?? E2ESettings.FrontUrl,
                ["Access-Control-Allow-Credentials"] = "true",
            },
        });
    }
}
