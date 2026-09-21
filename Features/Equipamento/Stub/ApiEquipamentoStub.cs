using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Equipamento.Stub;

/// <summary>
/// Responde no próprio navegador (Page.RouteAsync) as chamadas de /equipamento que não podem chegar à CCO.Api:
/// localmente não há associados, e BuscarPorAssociado e a busca por serial saem da API para o
/// concentrador31.gpsconecta.com.br (connection strings CCOMobilityAntigo e Concentrador31, inclusive no perfil Local).
/// Qualquer outra chamada legada (clienteAntigo, Concentrador, ConectaWEB) também é respondida aqui e registrada em
/// <see cref="Legadas"/>, para o teste falhar em vez de deixar a requisição sair.
/// </summary>
public sealed class ApiEquipamentoStub
{
    public const string Include = "Equipamento.ModeloEquipamento, Cliente, Chip";

    /// <summary>Página 1 (10 itens) pensada para a coluna Veículo; página 2 com os 2 restantes.</summary>
    public static readonly IReadOnlyList<Modulo> Modulos =
    [
        new(1, "E2E Cliente 01", 9101, 200, "E2E-VEIC-01", "AAA0A01", "E2E-VEIC-01"),
        new(2, "E2E Cliente 02", 9102, 200, null, "BBB1B11", "BBB1B11"),
        new(3, "E2E Cliente 03", 9103, 204, null, null, "-"),
        new(4, "E2E Cliente 04", 9104, 500, null, null, "-"),
        new(5, "E2E Cliente sem integração", null, 200, "NAO-CONSULTAR", "NAO-CONSULTAR", "-"),
        new(6, null, null, 200, "NAO-CONSULTAR", "NAO-CONSULTAR", "-"),
        new(7, "E2E Cliente 07", 9107, 200, "E2E-VEIC-07", "GGG7G77", "E2E-VEIC-07"),
        new(8, "E2E Cliente 08", 9108, 200, "E2E-VEIC-08", "HHH8H88", "E2E-VEIC-08"),
        new(9, "E2E Cliente 09", 9109, 200, "E2E-VEIC-09", "III9I99", "E2E-VEIC-09"),
        new(10, "E2E Cliente 10", 9110, 200, "E2E-VEIC-10", "JJJ0J00", "E2E-VEIC-10"),
        new(11, "E2E Cliente 11", 9111, 200, "E2E-VEIC-11", "KKK1K11", "E2E-VEIC-11"),
        new(12, "E2E Cliente 12", 9112, 200, "E2E-VEIC-12", "LLL2L22", "E2E-VEIC-12"),
    ];

    private static readonly string[] PrefixosLegados = ["/v1/clienteAntigo/", "/v1/Concentrador/", "/v1/ConectaWEB/"];

    private readonly ConcurrentQueue<ChamadaLista> _listas = new();
    private readonly ConcurrentQueue<ConsultaVeiculo> _consultas = new();
    private readonly ConcurrentQueue<string[]> _buscas = new();
    private readonly ConcurrentQueue<string> _legadas = new();

    private ApiEquipamentoStub()
    {
    }

    /// <summary>GET v1/associado paginado da lista geral.</summary>
    public IReadOnlyList<ChamadaLista> Listas => _listas.ToArray();

    /// <summary>BuscarPorAssociado, na ordem em que o navegador pediu.</summary>
    public IReadOnlyList<ConsultaVeiculo> ConsultasVeiculo => _consultas.ToArray();

    /// <summary>Seriais enviados a obterListaPorSerialNumber.</summary>
    public IReadOnlyList<string[]> Buscas => _buscas.ToArray();

    /// <summary>Chamadas legadas que a tela fez sem estarem previstas aqui.</summary>
    public IReadOnlyList<string> Legadas => _legadas.ToArray();

    public static async Task<ApiEquipamentoStub> InstalarAsync(IPage page)
    {
        var stub = new ApiEquipamentoStub();
        await page.RouteAsync(url => Atende(new Uri(url).AbsolutePath), stub.ResponderAsync);
        return stub;
    }

    public static bool EhLista(IRequest requisicao) =>
        requisicao.Method == "GET" && Termina(new Uri(requisicao.Url).AbsolutePath, "/v1/associado");

    public static bool EhBusca(IRequest requisicao) =>
        requisicao.Method == "POST" && Termina(new Uri(requisicao.Url).AbsolutePath, "/v1/associado/obterListaPorSerialNumber");

    private static bool Atende(string caminho) =>
        Termina(caminho, "/v1/associado")
        || Termina(caminho, "/v1/associado/obterListaPorSerialNumber")
        || Termina(caminho, "/v1/chip/AtualizarAssociadosPorSeriais")
        || PrefixosLegados.Any(p => caminho.Contains(p, StringComparison.OrdinalIgnoreCase));

    private async Task ResponderAsync(IRoute rota)
    {
        var requisicao = rota.Request;
        var uri = new Uri(requisicao.Url);
        var caminho = uri.AbsolutePath;

        if (EhLista(requisicao))
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            var top = int.TryParse(query["$Top"], out var t) ? t : Modulos.Count;
            var skip = int.TryParse(query["$Skip"], out var s) ? s : 0;
            _listas.Enqueue(new(Uri.UnescapeDataString(requisicao.Url), requisicao.Headers.GetValueOrDefault("include")));

            await ResponderJsonAsync(rota, new JsonObject
            {
                ["items"] = new JsonArray(Modulos.Skip(skip).Take(top).Select(m => (JsonNode)m.Json()).ToArray()),
                ["count"] = Modulos.Count,
            });
        }
        else if (EhBusca(requisicao))
        {
            var seriais = Seriais(requisicao);
            _buscas.Enqueue(seriais);
            var encontrados = Modulos.Where(m => seriais.Contains(m.Serial)).ToList();

            await ResponderJsonAsync(rota, new JsonObject
            {
                ["associados"] = new JsonArray(encontrados.Select(m => (JsonNode)m.Json()).ToArray()),
                ["serialNumbersNaoEncontrados"] = new JsonArray(
                    seriais.Except(encontrados.Select(m => m.Serial)).Select(x => (JsonNode)x).ToArray()),
            });
        }
        else if (Termina(caminho, "/v1/chip/AtualizarAssociadosPorSeriais"))
        {
            await rota.FulfillAsync(new() { Status = 200, Headers = Cors(requisicao) });
        }
        else if (Termina(caminho, "/v1/clienteAntigo/veiculo/BuscarPorAssociado"))
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            var serial = query["serialNumber"] ?? "";
            _consultas.Enqueue(new(int.Parse(query["integracaoId"] ?? "0"), serial));

            var modulo = Modulos.FirstOrDefault(m => m.Serial == serial);
            if (modulo is null || modulo.StatusVeiculo != 200)
            {
                await rota.FulfillAsync(new() { Status = modulo?.StatusVeiculo ?? 204, Headers = Cors(requisicao) });
                return;
            }

            await ResponderJsonAsync(rota, new JsonObject
            {
                ["identificadorVeiculo"] = modulo.Identificador,
                ["placa"] = modulo.Placa,
                ["modulo"] = modulo.Serial,
            });
        }
        else if (Termina(caminho, "/v1/Concentrador/LogControleWeb/GetByCodigoModulo"))
        {
            await ResponderJsonAsync(rota, new JsonArray());
        }
        else
        {
            _legadas.Enqueue($"{requisicao.Method} {caminho}");
            await ResponderJsonAsync(rota, new JsonArray());
        }
    }

    private static string[] Seriais(IRequest requisicao) =>
        (JsonNode.Parse(requisicao.PostData ?? "[]") as JsonArray ?? [])
        .Select(s => s?.ToString() ?? "")
        .ToArray();

    private static Task ResponderJsonAsync(IRoute rota, JsonNode corpo) =>
        rota.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = corpo.ToJsonString(),
            Headers = Cors(rota.Request),
        });

    /// <summary>O front (5173) chama a API (5225) em outra origem: sem estes headers o navegador descarta a resposta.</summary>
    private static Dictionary<string, string> Cors(IRequest requisicao) => new()
    {
        ["Access-Control-Allow-Origin"] = requisicao.Headers.GetValueOrDefault("origin") ?? E2ESettings.FrontUrl,
        ["Access-Control-Allow-Credentials"] = "true",
    };

    private static bool Termina(string caminho, string sufixo) =>
        caminho.TrimEnd('/').EndsWith(sufixo, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Um associado da lista. <paramref name="StatusVeiculo"/> é o que BuscarPorAssociado responde para ele e
/// <paramref name="VeiculoNaTela"/>, o que a coluna Veículo deve mostrar.
/// </summary>
public sealed record Modulo(
    int Id,
    string? Cliente,
    int? IntegracaoId,
    int StatusVeiculo,
    string? Identificador,
    string? Placa,
    string VeiculoNaTela)
{
    public string Serial => $"E2E-MOD-{Id:00}";

    public string ModeloEquipamento => $"E2E Modelo {Id:00}";

    public string Iccid => $"E2E-ICCID-{Id:00}";

    /// <summary>Associado como a CCO.Api serializa, com o include da tela.</summary>
    public JsonObject Json() => new()
    {
        ["id"] = Id,
        ["equipamentoId"] = Id,
        ["equipamentoSerial"] = Serial,
        ["equipamento"] = new JsonObject
        {
            ["id"] = Id,
            ["serialNumber"] = Serial,
            ["modeloEquipamento"] = new JsonObject { ["modelo"] = ModeloEquipamento, ["fabricante"] = "E2E Fabricante" },
        },
        ["chipSerial"] = Iccid,
        ["chip"] = new JsonObject { ["iccid"] = Iccid, ["operadora"] = "E2E Operadora" },
        ["cliente"] = Cliente is null ? null : new JsonObject { ["nome"] = Cliente, ["integracaoId"] = IntegracaoId },
        ["dataHoraRastreio"] = DateTime.Now.AddMinutes(-Id).ToString("yyyy-MM-ddTHH:mm:ss"),
        ["latitude"] = -22.9068,
        ["longitude"] = -43.1729,
        ["codigoClienteReservado"] = null,
        ["sucata"] = 0,
    };
}

public sealed record ChamadaLista(string Url, string? Include);

public sealed record ConsultaVeiculo(int IntegracaoId, string Serial);
