using PlaywrightTests.Features.Siera.Data;
using PlaywrightTests.Features.Siera.Pages;
using PlaywrightTests.Features.Siera.Stub;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Siera.Tests;

/// <summary>
/// A regra que justifica a fachada: o navegador conversa só com a CCO.Api. O serviço de integrações não tem
/// autenticação e usa a conta técnica compartilhada da Siera; se o front falasse com ele — ou com a Siera — direto,
/// qualquer um leria a telemetria da frota inteira.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class IsolamentoRedeSieraTests : SieraTestBase
{
    /// <summary>
    /// Percorre as três abas, inclusive o vídeo: é o único ponto em que a tela carrega algo de fora da API, o iframe
    /// com a playerUrl que a própria CCO.Api entregou.
    /// </summary>
    [Test]
    public async Task TelaSiera_ChamadasDeDadosVaoSomenteParaACcoApi()
    {
        await PrepararCenarioAsync();
        var tela = await AbrirTelaSieraAsync();

        await Expect(tela.Linha(SieraCenario.VeiculoEmpresa).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await tela.AbrirAbaAsync("Alarmes");
        await Expect(tela.Linha(SieraCenario.AlarmeEmpresa).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await tela.AbrirAbaAsync("Histórico de alarmes");
        await Expect(tela.Linha(SieraCenario.HistoricoEmpresa).First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await tela.SelecionarDispositivoAsync(SieraCenario.DispositivoEmpresa);
        var playerUrl = await tela.AbrirVideoAsync();
        Rede.PermitirDocumento(playerUrl);
        await EsperarPlayerCarregarAsync(tela, playerUrl);

        string[] recursosDaTela =
        [
            SieraRecursos.QueryUserVehicle, SieraRecursos.DeviceStatus, SieraRecursos.VehicleAlarm,
            SieraRecursos.AlarmDetail, SieraRecursos.VideoPlayer,
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Rede.Violacoes(), Is.Empty, "O navegador falou com algo além da CCO.Api.");
            foreach (var recurso in recursosDaTela)
            {
                Assert.That(Rede.ChamadasNaApi(SieraRecursos.NaFachada(recurso)), Is.GreaterThan(0),
                    $"A tela não chamou {recurso} na CCO.Api; sem essa chamada o teste passaria sem provar nada.");
            }
        });
    }

    /// <summary>
    /// O front não monta endereço de player: usa exatamente o que a CCO.Api devolveu. Em Local o proxy do player está
    /// desligado, então a CCO.Api repassa a playerUrl das integrações sem mudar.
    /// </summary>
    [Test]
    public async Task AbaVideo_IframeUsaExatamenteAUrlDevolvidaPelaCcoApi()
    {
        await PrepararCenarioAsync();
        var tela = await AbrirTelaSieraAsync();

        await tela.SelecionarDispositivoAsync(SieraCenario.DispositivoEmpresa);
        var playerUrl = await tela.AbrirVideoAsync();

        Assert.That(playerUrl, Is.EqualTo(SieraCenario.UrlPlayer(Stub.Url)),
            "A CCO.Api alterou a playerUrl das integrações com o proxy do player desligado.");
        await EsperarPlayerCarregarAsync(tela, playerUrl);
    }

    /// <summary>
    /// Autoteste do detector: se o MonitorRede deixasse de ver chamadas diretas, os outros testes passariam sempre.
    /// Não precisa de login e usa o stub, nunca as integrações reais.
    /// </summary>
    [Test]
    public async Task MonitorRede_ReprovaChamadaDiretaAsIntegracoes()
    {
        await Page.GotoAsync("/login");
        var direta = new Uri(Stub.Url, $"api/siera/{SieraRecursos.QueryUserVehicle}").AbsoluteUri;

        await Page.EvaluateAsync("url => fetch(url, { mode: 'no-cors' }).catch(() => null)", direta);
        await EsperarAteAsync(
            () => Stub.Recebidas(SieraRecursos.QueryUserVehicle).Any(r => r.VeioDoNavegador),
            "O fetch de teste não chegou ao stub.");

        Assert.That(Rede.Violacoes(), Has.Some.Contains(direta),
            "O MonitorRede não reprovou um fetch do navegador direto para as integrações.");
    }

    private async Task EsperarPlayerCarregarAsync(SieraPage tela, string playerUrl)
    {
        await Expect(tela.IframePlayer).ToHaveAttributeAsync("src", playerUrl);
        await EsperarAteAsync(
            () => Stub.Recebidas().Any(r => r.Caminho == StubIntegracoesSiera.CaminhoPlayer && r.VeioDoNavegador),
            "O iframe não chegou a pedir a playerUrl.");
    }
}
