using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace PlaywrightTests.Features.Siera.Stub;

/// <summary>
/// Faz o papel do ccomobility-telematics-integrations para a CCO.Api de teste: responde o que cada teste
/// programar e registra o que chegou. Nenhum teste da Siera depende — nem chega perto — da Siera real.
/// </summary>
public sealed class StubIntegracoesSiera : IAsyncDisposable
{
    public const string PrefixoApi = "/api/siera/";

    /// <summary>Caminho da página do player da Siera; o stub serve um HTML mínimo para o iframe carregar.</summary>
    public const string CaminhoPlayer = "/808gps/open/player/video.html";

    private const string PaginaPlayer =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Player E2E</title></head>" +
        "<body><p>Player falso do stub E2E da Siera</p></body></html>";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _parar = new();
    private readonly ConcurrentDictionary<string, RespostaStub> _respostas = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<RequisicaoStub> _recebidas = new();
    private Task _atendimento = Task.CompletedTask;

    private StubIntegracoesSiera(int porta)
    {
        Url = new Uri($"http://127.0.0.1:{porta}/");
        _listener.Prefixes.Add(Url.AbsoluteUri);
    }

    public Uri Url { get; }

    public static StubIntegracoesSiera Iniciar(int porta)
    {
        var stub = new StubIntegracoesSiera(porta);
        stub._listener.Start();
        stub._atendimento = stub.AtenderAsync();
        return stub;
    }

    /// <summary>Troca todas as respostas pelas do cenário (nenhuma, se omitido) e esquece o que foi recebido.</summary>
    public void Resetar(IReadOnlyDictionary<string, RespostaStub>? cenario = null)
    {
        _respostas.Clear();
        foreach (var (recurso, resposta) in cenario ?? new Dictionary<string, RespostaStub>())
        {
            _respostas[recurso] = resposta;
        }

        LimparRecebidas();
    }

    public void Programar(string recurso, RespostaStub resposta) => _respostas[recurso] = resposta;

    public void LimparRecebidas() => _recebidas.Clear();

    /// <summary>Requisições recebidas; com <paramref name="recurso"/>, só as de <c>/api/siera/{recurso}</c>.</summary>
    public IReadOnlyList<RequisicaoStub> Recebidas(string? recurso = null) =>
        _recebidas
            .Where(r => recurso is null || string.Equals(r.Recurso, recurso, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public async ValueTask DisposeAsync()
    {
        await _parar.CancelAsync();
        _listener.Stop();
        try
        {
            await _atendimento;
        }
        catch (Exception)
        {
            // Encerrando.
        }

        _listener.Close();
        _parar.Dispose();
    }

    private async Task AtenderAsync()
    {
        while (!_parar.IsCancellationRequested)
        {
            HttpListenerContext contexto;
            try
            {
                contexto = await _listener.GetContextAsync();
            }
            catch (Exception) when (_parar.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }

            _ = Task.Run(() => ResponderAsync(contexto));
        }
    }

    private async Task ResponderAsync(HttpListenerContext contexto)
    {
        var requisicao = contexto.Request;
        var caminho = requisicao.Url?.AbsolutePath ?? "/";
        var recurso = caminho.StartsWith(PrefixoApi, StringComparison.OrdinalIgnoreCase) ? caminho[PrefixoApi.Length..] : null;
        var headers = requisicao.Headers.AllKeys
            .OfType<string>()
            .ToDictionary(chave => chave, chave => requisicao.Headers[chave] ?? "", StringComparer.OrdinalIgnoreCase);

        _recebidas.Enqueue(new RequisicaoStub(requisicao.HttpMethod, caminho, recurso, requisicao.Url?.Query ?? "", headers));

        var resposta = caminho.Equals(CaminhoPlayer, StringComparison.OrdinalIgnoreCase)
            ? RespostaStub.Html(PaginaPlayer)
            : recurso is not null && _respostas.TryGetValue(recurso, out var programada)
                ? programada
                // Sem `code` no corpo a CCO.Api trata como indisponibilidade: quem esqueceu de programar um
                // recurso vê o erro na tela em vez de receber dado inventado.
                : new RespostaStub(404, "", "text/plain");

        try
        {
            if (resposta.Atraso is { } atraso)
            {
                await Task.Delay(atraso, _parar.Token);
            }

            if (resposta.DerrubarConexao)
            {
                contexto.Response.Abort();
                return;
            }

            var corpo = Encoding.UTF8.GetBytes(resposta.Corpo);
            contexto.Response.StatusCode = resposta.Status;
            contexto.Response.ContentType = resposta.ContentType;
            contexto.Response.ContentLength64 = corpo.Length;
            await contexto.Response.OutputStream.WriteAsync(corpo, _parar.Token);
            contexto.Response.Close();
        }
        catch (Exception)
        {
            // Quem chamou já desistiu (o timeout da CCO.Api, no teste de 504) ou o stub está parando.
            try
            {
                contexto.Response.Abort();
            }
            catch (Exception)
            {
                // Conexão já fechada.
            }
        }
    }
}

public sealed record RequisicaoStub(
    string Metodo,
    string Caminho,
    string? Recurso,
    string Query,
    IReadOnlyDictionary<string, string> Headers)
{
    public string? Parametro(string nome) => HttpUtility.ParseQueryString(Query)[nome];

    public string? Header(string nome) => Headers.TryGetValue(nome, out var valor) ? valor : null;

    /// <summary>Veio de um navegador (fetch, XHR, iframe), e não do HttpClient da CCO.Api.</summary>
    public bool VeioDoNavegador =>
        Header("Origin") is not null
        || Header("Sec-Fetch-Mode") is not null
        || (Header("User-Agent")?.Contains("Mozilla", StringComparison.OrdinalIgnoreCase) ?? false);
}

public sealed record RespostaStub(
    int Status,
    string Corpo,
    string ContentType = "application/json; charset=utf-8",
    TimeSpan? Atraso = null,
    bool DerrubarConexao = false)
{
    public static RespostaStub Json(JsonNode corpo, int status = 200) => new(status, corpo.ToJsonString());

    public static RespostaStub Html(string html) => new(200, html, "text/html; charset=utf-8");

    /// <summary>Erro no contrato do serviço de integrações (<c>ExceptionHandlingMiddleware</c>).</summary>
    public static RespostaStub Erro(int status, string code) =>
        Json(new JsonObject { ["code"] = code, ["title"] = code, ["status"] = status, ["correlationId"] = "stub-e2e" }, status);

    /// <summary>Fecha a conexão sem responder, como um serviço que caiu no meio da requisição.</summary>
    public static RespostaStub Queda() => new(0, "", DerrubarConexao: true);

    public RespostaStub ComAtraso(TimeSpan atraso) => this with { Atraso = atraso };
}
