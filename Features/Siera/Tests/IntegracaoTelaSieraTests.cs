using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PlaywrightTests.Features.Siera.Data;
using PlaywrightTests.Features.Siera.Pages;
using PlaywrightTests.Features.Siera.Stub;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Siera.Tests;

/// <summary>
/// A tela integrada à CCO.Api real: o que o usuário vê vem das integrações passando pela fachada, recortado pela
/// empresa, e as falhas das integrações chegam traduzidas.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class IntegracaoTelaSieraTests : SieraTestBase
{
    [Test]
    public async Task TelaSiera_ExibeSomenteDadosDaEmpresaVindosDaCcoApi()
    {
        await PrepararCenarioAsync();
        var tela = await AbrirTelaSieraAsync();

        await Expect(tela.Linha(SieraCenario.VeiculoEmpresa).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(tela.Linha(SieraCenario.EnderecoEmpresa).First).ToBeVisibleAsync();
        await Expect(tela.Linha(SieraCenario.VeiculoOutraEmpresa)).ToHaveCountAsync(0);

        await tela.AbrirAbaAsync("Alarmes");
        await Expect(tela.Linha(SieraCenario.AlarmeEmpresa).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(tela.Linha(SieraCenario.AlarmeOutraEmpresa)).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A consulta do usuário chega às integrações pela CCO.Api — não pelo navegador — e sem o token do usuário, que
    /// carrega claims sensíveis e não tem nada a fazer num serviço interno.
    /// </summary>
    [Test]
    public async Task SelecionarRastreador_ConsultaChegaAsIntegracoesPelaCcoApi()
    {
        await PrepararCenarioAsync();
        var tela = await AbrirTelaSieraAsync();
        await Expect(tela.Linha(SieraCenario.VeiculoEmpresa).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await tela.SelecionarDispositivoAsync(SieraCenario.DispositivoEmpresa);
        await EsperarAteAsync(() => ConsultasDoRastreador().Count > 0,
            $"Selecionar o rastreador não fez device-status?devIdno={SieraCenario.DispositivoEmpresa} chegar às integrações.");

        var consulta = ConsultasDoRastreador()[^1];
        Assert.Multiple(() =>
        {
            Assert.That(consulta.VeioDoNavegador, Is.False, "A consulta chegou às integrações vinda do navegador, não da CCO.Api.");
            Assert.That(consulta.Header("Authorization"), Is.Null, "A CCO.Api repassou o token do usuário às integrações.");
            Assert.That(consulta.Header("X-Correlation-ID"), Is.Not.Null.And.Not.Empty,
                "Sem X-Correlation-ID um erro relatado na tela não é rastreável no serviço de integrações.");
        });
    }

    [Test]
    public async Task IntegracoesIndisponiveis_TelaExibeAvisoTraduzido()
    {
        await PrepararCenarioAsync();
        Stub.Programar(SieraRecursos.DeviceStatus, RespostaStub.Erro(502, "siera_unavailable"));

        await AbrirTelaSieraAsync();

        var alerta = new SweetAlert(Page);
        await Expect(alerta.Titulo).ToContainTextAsync("Aviso", new() { Timeout = 20_000 });
        await Expect(alerta.Texto).ToContainTextAsync("A Siera está indisponível no momento.");
    }

    [Test]
    public async Task IntegracoesLentas_TelaExibeAvisoDeTimeout()
    {
        await PrepararCenarioAsync();
        var semStatus = RespostaStub.Json(new JsonObject { ["result"] = 0, ["status"] = new JsonArray() });
        Stub.Programar(SieraRecursos.DeviceStatus, semStatus.ComAtraso(TimeSpan.FromSeconds(SieraAmbiente.TimeoutIntegracoesSegundos + 3)));

        await AbrirTelaSieraAsync();

        var alerta = new SweetAlert(Page);
        await Expect(alerta.Titulo).ToContainTextAsync("Aviso", new() { Timeout = 30_000 });
        await Expect(alerta.Texto).ToContainTextAsync("A Siera não respondeu a tempo.");
    }

    /// <summary>
    /// A guarda da rota não deixa uma sessão sem master nem montar a tela. Aqui a sessão de um master é marcada como
    /// não-master; o 403 da CCO.Api para um não-master de verdade está em <see cref="ContratoFachadaSieraTests"/>.
    /// </summary>
    [Test]
    public async Task SessaoSemMaster_GuardaDaRotaNaoChamaASiera()
    {
        var login = new LoginPage(Page);
        await login.LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        await Page.EvaluateAsync("""
            () => {
                const sessao = JSON.parse(localStorage.getItem('sessionApp'));
                sessao.dados.usuarioMaster = false;
                localStorage.setItem('sessionApp', JSON.stringify(sessao));
            }
            """);
        Rede.Limpar();

        await Page.GotoAsync(SieraPage.Url);

        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));
        await Expect(login.BotaoMenu).ToBeVisibleAsync(new() { Timeout = 30_000 });
        Assert.Multiple(() =>
        {
            Assert.That(Rede.ChamadasNaApi(SieraRecursos.NaFachada(SieraRecursos.QueryUserVehicle)), Is.Zero,
                "A tela da Siera chegou a montar para uma sessão sem master.");
            Assert.That(Stub.Recebidas(), Is.Empty, "Uma sessão sem master fez a CCO.Api chamar as integrações.");
        });
    }

    private List<RequisicaoStub> ConsultasDoRastreador() =>
        Stub.Recebidas(SieraRecursos.DeviceStatus)
            .Where(r => r.Parametro("devIdno") == SieraCenario.DispositivoEmpresa)
            .ToList();
}
