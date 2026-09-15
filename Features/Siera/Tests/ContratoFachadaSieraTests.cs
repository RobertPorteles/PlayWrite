using System.Globalization;
using System.Text.Json.Nodes;
using PlaywrightTests.Features.Siera.Data;
using PlaywrightTests.Features.Siera.Stub;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Siera.Tests;

/// <summary>
/// Contrato da fachada v1/Siera na CCO.Api, só por HTTP: quem pode chamar, o que é repassado às integrações e como
/// as falhas delas voltam. Os testes unitários da CCO.Api não passam pelo pipeline HTTP (autenticação, provider,
/// filtros), então nada disso estava coberto.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class ContratoFachadaSieraTests : SieraTestBase
{
    public static IEnumerable<TestCaseData> RecursosDaTela() =>
        SieraRecursos.DaTela.Select(recurso => new TestCaseData(recurso).SetArgDisplayNames(recurso));

    public static IEnumerable<TestCaseData> FalhasDasIntegracoes()
    {
        yield return new TestCaseData(RespostaStub.Erro(404, "device_not_found"), 404, "device_not_found", "modules.siera.errorDeviceNotFound")
            .SetArgDisplayNames("erro com code preserva status e code");
        yield return new TestCaseData(new RespostaStub(500, "falha fora do contrato", "text/plain"), 502, "siera_unavailable", "modules.siera.errorUnavailable")
            .SetArgDisplayNames("erro sem code vira 502");
        yield return new TestCaseData(RespostaStub.Queda(), 502, "siera_unavailable", "modules.siera.errorUnavailable")
            .SetArgDisplayNames("queda de conexão vira 502");
        yield return new TestCaseData(
                RespostaStub.Json(new JsonObject { ["result"] = 0, ["status"] = new JsonArray() })
                    .ComAtraso(TimeSpan.FromSeconds(SieraAmbiente.TimeoutIntegracoesSegundos + 3)),
                504, "siera_timeout", "modules.siera.errorTimeout")
            .SetArgDisplayNames("lentidão vira 504");
    }

    [TestCaseSource(nameof(RecursosDaTela))]
    public async Task SemToken_Retorna401SemChamarIntegracoes(string recurso)
    {
        await using var anonimo = await NovoContextoApiAsync();

        var resposta = await anonimo.GetAsync(SieraRecursos.NaFachada(recurso));

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(401), $"GET {recurso} sem token deveria dar 401.");
            Assert.That(Stub.Recebidas(), Is.Empty, "Uma requisição anônima chegou às integrações.");
        });
    }

    [Test]
    public async Task NaoMaster_Retorna403SemChamarIntegracoes()
    {
        if (E2ESettings.LoginNaoMaster is not { } login || E2ESettings.SenhaNaoMaster is not { } senha)
        {
            Assert.Ignore("Defina E2E_LOGIN_NAO_MASTER e E2E_SENHA_NAO_MASTER com um usuário local sem perfil master " +
                          "(veja Features/Siera/README.md).");
            return;
        }

        await using var naoMaster = await ApiClient.LoginAsync(Playwright, login, senha, E2ESettings.Empresa, SieraAmbiente.ApiUrl.AbsoluteUri);

        var respostas = new List<ApiResposta>();
        foreach (var recurso in SieraRecursos.DaTela)
        {
            respostas.Add(await naoMaster.EnviarAsync("GET", SieraRecursos.NaFachada(recurso)));
        }

        Assert.Multiple(() =>
        {
            foreach (var resposta in respostas)
            {
                Assert.That(resposta.Status, Is.EqualTo(403), $"{resposta.Caminho}: esperado 403, veio {resposta.Status}: {resposta.Corpo}");
                Assert.That(resposta.Json?["messageKey"].ComoTexto(), Is.EqualTo("errors.auth.forbidden"), resposta.Caminho);
            }

            Assert.That(Stub.Recebidas(), Is.Empty, "Um usuário sem master fez a CCO.Api chamar as integrações.");
        });
    }

    [Test]
    public async Task QueryUserVehicle_RecortaAFrotaPelaEmpresaDaSessao()
    {
        var codigo = await PrepararCenarioAsync();
        var api = await ApiMasterAsync();

        var frota = await api.GetAsync(SieraRecursos.NaFachada(SieraRecursos.QueryUserVehicle));

        var veiculos = (frota?["vehicles"] as JsonArray ?? []).Select(v => v?["nm"].ComoTexto()).ToList();
        var empresas = (frota?["companys"] as JsonArray ?? []).Select(c => c?["nm"].ComoTexto()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(veiculos, Is.EqualTo(new[] { SieraCenario.VeiculoEmpresa }), "A frota não foi recortada pela empresa da sessão.");
            Assert.That(empresas, Is.EqualTo(new[] { codigo.ToString(CultureInfo.InvariantCulture) }),
                "A lista de empresas da Siera vazou outra empresa.");
        });
    }

    [Test]
    public async Task RastreadorDeOutraEmpresa_Retorna403SemRepassar()
    {
        await PrepararCenarioAsync();
        var api = await ApiMasterAsync();

        var resposta = await api.EnviarAsync("GET",
            $"{SieraRecursos.NaFachada(SieraRecursos.DeviceStatus)}?devIdno={SieraCenario.DispositivoOutraEmpresa}");

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(403), $"Esperado 403, veio {resposta.Status}: {resposta.Corpo}");
            Assert.That(Stub.Recebidas(SieraRecursos.DeviceStatus), Is.Empty,
                "A consulta pelo rastreador de outra empresa chegou às integrações.");
        });
    }

    /// <summary>Sem rastreador na consulta, a Siera devolveria a frota inteira: a CCO.Api precisa restringir o devIdno.</summary>
    [Test]
    public async Task HistoricoSemRastreador_ConsultaSoOsRastreadoresDaEmpresa()
    {
        await PrepararCenarioAsync();
        var api = await ApiMasterAsync();
        var agora = DateTime.Now;
        var query = $"?begintime={Uri.EscapeDataString(agora.AddDays(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}" +
                    $"&endtime={Uri.EscapeDataString(agora.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}&armType=2";

        var pagina = await api.GetAsync(SieraRecursos.NaFachada(SieraRecursos.AlarmDetail) + query);

        var consulta = Stub.Recebidas(SieraRecursos.AlarmDetail).LastOrDefault();
        var dispositivos = (pagina?["alarmlist"] as JsonArray ?? []).Select(a => a?["did"].ComoTexto()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(consulta?.Parametro("devIdno"), Is.EqualTo(SieraCenario.DispositivoEmpresa),
                "A consulta livre não foi restrita aos rastreadores da empresa.");
            Assert.That(dispositivos, Is.EqualTo(new[] { SieraCenario.DispositivoEmpresa }),
                "O histórico devolveu alarme de rastreador de outra empresa.");
        });
    }

    [Test]
    public async Task Sucesso_RepassaOCorpoDasIntegracoesSemVazarOToken()
    {
        await PrepararCenarioAsync();
        var api = await ApiMasterAsync();
        var correlacao = $"e2e-siera-{Guid.NewGuid():N}";
        await using var contexto = await NovoContextoApiAsync(api.Token, new Dictionary<string, string> { ["X-Correlation-ID"] = correlacao });

        var resposta = await contexto.GetAsync(SieraRecursos.NaFachada(SieraRecursos.VehicleAlarm));

        var corpo = await resposta.TextAsync();
        var consulta = Stub.Recebidas(SieraRecursos.VehicleAlarm).LastOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(200), corpo);
            Assert.That(resposta.Headers.GetValueOrDefault("content-type"), Does.StartWith("application/json"));
            // Casing original da Siera: o corpo não passa pelo camelCase da CCO.Api.
            Assert.That(corpo, Does.Contain($"\"DevIDNO\":{SieraCenario.DispositivoEmpresa}"));
            Assert.That(corpo, Does.Not.Contain(SieraCenario.DispositivoOutraEmpresa), "O alarme de outra empresa não foi recortado.");
            Assert.That(consulta?.Header("X-Correlation-ID"), Is.EqualTo(correlacao), "A correlação não foi propagada às integrações.");
            Assert.That(consulta?.Header("Authorization"), Is.Null, "A CCO.Api repassou o token do usuário às integrações.");
        });
    }

    [TestCaseSource(nameof(FalhasDasIntegracoes))]
    public async Task FalhaDasIntegracoes_VoltaNoContratoDeErroDaApi(RespostaStub falha, int status, string code, string messageKey)
    {
        await PrepararCenarioAsync();
        Stub.Programar(SieraRecursos.DeviceStatus, falha);
        var api = await ApiMasterAsync();

        var resposta = await api.EnviarAsync("GET", SieraRecursos.NaFachada(SieraRecursos.DeviceStatus));

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(status), resposta.Corpo);
            Assert.That(resposta.Json?["code"].ComoTexto(), Is.EqualTo(code), resposta.Corpo);
            Assert.That(resposta.Json?["messageKey"].ComoTexto(), Is.EqualTo(messageKey), "A messageKey é o que a tela traduz para o usuário.");
        });
    }
}
