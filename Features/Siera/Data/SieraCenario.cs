using System.Globalization;
using System.Text.Json.Nodes;
using PlaywrightTests.Features.Siera.Stub;

namespace PlaywrightTests.Features.Siera.Data;

/// <summary>
/// Frota falsa no formato da Siera com duas empresas: a da sessão, cujo nome na Siera (<c>companys[].nm</c>) é o
/// <c>Empresa.IntegracaoCodigo</c>, e outra, que a CCO.Api precisa recortar de toda resposta
/// (<c>SieraEscopoEmpresa</c>). Formatos conferidos com os mocks de ccomobility-frontend/tests/siera.
/// </summary>
public static class SieraCenario
{
    public const string VeiculoEmpresa = "E2E-SIERA-01";

    /// <summary>Numérico como na Siera: o <c>vehicle-alarm</c> devolve <c>DevIDNO</c> como número.</summary>
    public const string DispositivoEmpresa = "990101";

    public const string EnderecoEmpresa = "Av. E2E da Siera, 100";

    public const string AlarmeEmpresa = "E2E alarme ativo da empresa";

    public const string HistoricoEmpresa = "E2E histórico da empresa";

    public const string VeiculoOutraEmpresa = "E2E-OUTRA-01";

    public const string DispositivoOutraEmpresa = "990199";

    public const string AlarmeOutraEmpresa = "E2E alarme ativo de outra empresa";

    public const string HistoricoOutraEmpresa = "E2E histórico de outra empresa";

    /// <summary>Ids das empresas dentro da Siera (<c>companys[].id</c>), referenciados pelos rastreadores (<c>dl[].pid</c>).</summary>
    private const long EmpresaNaSiera = 990001;

    private const long OutraEmpresaNaSiera = 990002;

    public static string UrlPlayer(Uri stub) =>
        new Uri(stub, $"{StubIntegracoesSiera.CaminhoPlayer.TrimStart('/')}?devIdno={DispositivoEmpresa}").AbsoluteUri;

    public static Dictionary<string, RespostaStub> Padrao(int codigoCliente, Uri stub)
    {
        var agora = DateTime.Now;
        var dataHora = agora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var inicioAlarme = new DateTimeOffset(agora.AddHours(-2)).ToUnixTimeSeconds();
        var fimAlarme = new DateTimeOffset(agora.AddHours(-1)).ToUnixTimeSeconds();

        return new(StringComparer.OrdinalIgnoreCase)
        {
            [SieraRecursos.QueryUserVehicle] = RespostaStub.Json(new JsonObject
            {
                ["result"] = 0,
                ["companys"] = new JsonArray(
                    new JsonObject { ["id"] = EmpresaNaSiera, ["nm"] = codigoCliente.ToString(CultureInfo.InvariantCulture) },
                    new JsonObject { ["id"] = OutraEmpresaNaSiera, ["nm"] = (codigoCliente + 1).ToString(CultureInfo.InvariantCulture) }),
                ["vehicles"] = new JsonArray(
                    Veiculo(1, VeiculoEmpresa, EmpresaNaSiera, DispositivoEmpresa, 2, "Frente,Cabine"),
                    Veiculo(2, VeiculoOutraEmpresa, OutraEmpresaNaSiera, DispositivoOutraEmpresa, 1, "Frente")),
            }),

            [SieraRecursos.DeviceStatus] = RespostaStub.Json(new JsonObject
            {
                ["result"] = 0,
                ["status"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = DispositivoEmpresa, ["vid"] = VeiculoEmpresa, ["lat"] = -22.9068, ["lng"] = -43.1729,
                        ["sp"] = 42.5, ["ol"] = 1, ["gt"] = dataHora, ["lc"] = 1234.5, ["ps"] = EnderecoEmpresa,
                    },
                    new JsonObject
                    {
                        ["id"] = DispositivoOutraEmpresa, ["vid"] = VeiculoOutraEmpresa, ["lat"] = -23.5505, ["lng"] = -46.6333,
                        ["sp"] = 10.0, ["ol"] = 0, ["gt"] = dataHora, ["lc"] = 99.0, ["ps"] = "Rua de outra empresa, 1",
                    }),
            }),

            [SieraRecursos.DeviceOlStatus] = RespostaStub.Json(new JsonObject
            {
                ["result"] = 0,
                ["onlines"] = new JsonArray(
                    new JsonObject { ["did"] = DispositivoEmpresa, ["vid"] = VeiculoEmpresa, ["online"] = 1 },
                    new JsonObject { ["did"] = DispositivoOutraEmpresa, ["vid"] = VeiculoOutraEmpresa, ["online"] = 0 }),
            }),

            [SieraRecursos.VehicleStatus] = RespostaStub.Json(new JsonObject
            {
                ["result"] = 0,
                ["infos"] = new JsonArray(
                    new JsonObject { ["vi"] = VeiculoEmpresa, ["tm"] = fimAlarme, ["jd"] = -43.1729, ["wd"] = -22.9068 },
                    new JsonObject { ["vi"] = VeiculoOutraEmpresa, ["tm"] = fimAlarme, ["jd"] = -46.6333, ["wd"] = -23.5505 }),
            }),

            [SieraRecursos.VehicleAlarm] = RespostaStub.Json(new JsonObject
            {
                ["result"] = 0,
                ["alarmlist"] = new JsonArray(
                    Alarme(DispositivoEmpresa, AlarmeEmpresa, "e2e-alarme-empresa", dataHora),
                    Alarme(DispositivoOutraEmpresa, AlarmeOutraEmpresa, "e2e-alarme-outra", dataHora)),
            }),

            [SieraRecursos.AlarmDetail] = RespostaStub.Json(new JsonObject
            {
                ["result"] = 0,
                ["alarmlist"] = new JsonArray(
                    Historico(DispositivoEmpresa, VeiculoEmpresa, HistoricoEmpresa, inicioAlarme, fimAlarme),
                    Historico(DispositivoOutraEmpresa, VeiculoOutraEmpresa, HistoricoOutraEmpresa, inicioAlarme, fimAlarme)),
                ["totalPages"] = 1,
                ["currentPage"] = 1,
                ["pageRecords"] = 10,
                ["totalRecords"] = 2,
            }),

            [SieraRecursos.VideoPlayer] = RespostaStub.Json(new JsonObject
            {
                ["deviceId"] = DispositivoEmpresa,
                ["playerUrl"] = UrlPlayer(stub),
                ["streamType"] = "sub",
                ["expiresAtUtc"] = DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["channels"] = new JsonArray(
                    new JsonObject { ["channel"] = 1, ["hlsUrl"] = new Uri(stub, $"hls/{DispositivoEmpresa}/1/playlist.m3u8").AbsoluteUri }),
            }),
        };
    }

    private static JsonObject Veiculo(long id, string placa, long empresa, string dispositivo, int canais, string nomesCanais) => new()
    {
        ["id"] = id,
        ["nm"] = placa,
        ["pid"] = empresa,
        ["dl"] = new JsonArray(new JsonObject { ["id"] = dispositivo, ["pid"] = empresa, ["cc"] = canais, ["cn"] = nomesCanais }),
    };

    /// <summary>Sem <c>img</c>: a tela não oferece o botão de evidência, que exigiria um ZIP no stub.</summary>
    private static JsonObject Alarme(string dispositivo, string descricao, string guid, string dataHora) => new()
    {
        ["DevIDNO"] = long.Parse(dispositivo, CultureInfo.InvariantCulture),
        ["desc"] = descricao,
        ["guid"] = guid,
        ["hd"] = 0,
        ["type"] = 2,
        ["time"] = dataHora,
        ["Gps"] = new JsonObject { ["sp"] = 30.0, ["lat"] = -22.9068, ["lng"] = -43.1729, ["gt"] = dataHora },
    };

    private static JsonObject Historico(string dispositivo, string veiculo, string descricao, long inicio, long fim) => new()
    {
        ["did"] = dispositivo,
        ["vid"] = veiculo,
        ["desc"] = descricao,
        ["atp"] = 2,
        ["stm"] = inicio,
        ["etm"] = fim,
        ["hd"] = 1,
        ["guid"] = $"e2e-historico-{dispositivo}",
    };
}
