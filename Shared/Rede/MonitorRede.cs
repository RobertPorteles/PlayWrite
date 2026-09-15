using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace PlaywrightTests.Shared.Rede;

/// <summary>
/// Registra tudo o que o navegador pede e aponta o que foi para onde não devia: chamada de dados (XHR, fetch,
/// WebSocket, EventSource) para outra origem que não a API, ou qualquer requisição a um destino proibido — um
/// serviço interno que só o backend pode chamar, por exemplo.
/// </summary>
public sealed class MonitorRede
{
    private static readonly HashSet<string> TiposDeDados =
        new(StringComparer.OrdinalIgnoreCase) { "xhr", "fetch", "websocket", "eventsource" };

    private readonly ConcurrentQueue<RequisicaoObservada> _requisicoes = new();
    private readonly ConcurrentDictionary<string, byte> _documentosPermitidos = new();
    private readonly HashSet<string> _origensProibidas = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hostsProibidos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Uri _api;
    private readonly Uri _front;

    public MonitorRede(IBrowserContext contexto, Uri api, Uri front)
    {
        _api = api;
        _front = front;

        contexto.Request += (_, requisicao) =>
            _requisicoes.Enqueue(new(requisicao.Url, requisicao.ResourceType, requisicao.Method, UrlDoFrame(requisicao)));

        // WebSocket não dispara o evento Request do Playwright: cada página precisa ser observada à parte.
        foreach (var pagina in contexto.Pages)
        {
            Observar(pagina);
        }

        contexto.Page += (_, pagina) => Observar(pagina);
    }

    public IReadOnlyList<RequisicaoObservada> Requisicoes => _requisicoes.ToArray();

    /// <summary>Proíbe host + porta. Loopback vale em qualquer grafia (localhost, 127.0.0.1, ::1).</summary>
    public void ProibirOrigem(Uri origem) => _origensProibidas.Add(Chave(origem));

    /// <summary>Proíbe o host em qualquer porta.</summary>
    public void ProibirHost(string host) => _hostsProibidos.Add(NormalizarHost(host));

    /// <summary>
    /// Libera a navegação de um documento exatamente nesta URL, mesmo num destino proibido — o iframe de um
    /// player cuja URL o próprio backend entregou.
    /// </summary>
    public void PermitirDocumento(string url) => _documentosPermitidos.TryAdd(url, 0);

    /// <summary>Esquece o que foi registrado até agora (ex.: o tráfego do login, antes de abrir a tela testada).</summary>
    public void Limpar() => _requisicoes.Clear();

    /// <summary>Quantas chamadas de dados foram à API num caminho terminado em <paramref name="caminho"/>.</summary>
    public int ChamadasNaApi(string caminho) =>
        Requisicoes.Count(r =>
            TiposDeDados.Contains(r.Tipo)
            && TentarUri(r.Url) is { } uri
            && MesmaOrigem(uri, _api)
            && uri.AbsolutePath.TrimEnd('/').EndsWith("/" + caminho.Trim('/'), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> Violacoes()
    {
        var violacoes = new List<string>();
        foreach (var requisicao in Requisicoes)
        {
            if (TentarUri(requisicao.Url) is not { } uri)
            {
                continue;
            }

            if (TiposDeDados.Contains(requisicao.Tipo))
            {
                // A única conversa de dados fora da API é o HMR do Vite, na origem do próprio front.
                var hmrDoVite = requisicao.Tipo == "websocket" && MesmaOrigem(uri, _front);
                if (!MesmaOrigem(uri, _api) && !hmrDoVite)
                {
                    violacoes.Add($"{requisicao.Tipo} {requisicao.Metodo} {requisicao.Url} — chamada de dados fora da API");
                }

                continue;
            }

            var documentoPermitido = requisicao.Tipo == "document" && _documentosPermitidos.ContainsKey(requisicao.Url);
            if (Proibido(uri) && !documentoPermitido)
            {
                violacoes.Add($"{requisicao.Tipo} {requisicao.Metodo} {requisicao.Url} — destino proibido");
            }
        }

        return violacoes;
    }

    /// <summary>Hosts fora da API e do front que receberam alguma requisição (CDNs, tiles), para diagnóstico.</summary>
    public IReadOnlyList<string> HostsExternos() =>
        Requisicoes
            .Select(r => TentarUri(r.Url))
            .OfType<Uri>()
            .Where(uri => !MesmaOrigem(uri, _api) && !MesmaOrigem(uri, _front))
            .Select(uri => uri.Authority)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();

    private void Observar(IPage pagina) =>
        pagina.WebSocket += (_, socket) => _requisicoes.Enqueue(new(socket.Url, "websocket", "GET", pagina.Url));

    private bool Proibido(Uri uri) =>
        _origensProibidas.Contains(Chave(uri)) || _hostsProibidos.Contains(NormalizarHost(uri.Host));

    private static string? UrlDoFrame(IRequest requisicao)
    {
        try
        {
            return requisicao.Frame.Url;
        }
        catch (PlaywrightException)
        {
            // Requisição de service worker não tem frame.
            return null;
        }
    }

    private static Uri? TentarUri(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "ws" or "wss" ? uri : null;

    private static bool MesmaOrigem(Uri a, Uri b) => EsquemaHttp(a) == EsquemaHttp(b) && Chave(a) == Chave(b);

    private static string EsquemaHttp(Uri uri) => uri.Scheme switch
    {
        "ws" => "http",
        "wss" => "https",
        var esquema => esquema,
    };

    private static string Chave(Uri uri) => $"{NormalizarHost(uri.Host)}:{uri.Port}";

    private static string NormalizarHost(string host)
    {
        var normalizado = host.Trim('[', ']').ToLowerInvariant();
        return normalizado is "localhost" or "127.0.0.1" or "::1" ? "loopback" : normalizado;
    }
}

public sealed record RequisicaoObservada(string Url, string Tipo, string Metodo, string? Frame);
