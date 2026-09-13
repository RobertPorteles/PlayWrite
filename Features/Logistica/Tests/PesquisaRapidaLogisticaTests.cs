using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// "Pesquisa rápida" de cada tela de logística: a consulta filtrada montada pelo front precisa ser aceita pela API.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class PesquisaRapidaLogisticaTests : LogisticaTestBase
{
    public static IEnumerable<TestCaseData> Telas()
    {
        yield return Tela(ClientePage.Url, "Cliente");
        yield return Tela(CentroCustoPage.Url, "Centro de Custo");
        yield return Tela(SolicitantePage.Url, "Solicitante");
        yield return Tela(LocaisInteressePage.Url, "Locais de interesse");
        yield return Tela(ProgramacoesPage.Url, "Programações");
        yield return Tela(ViagemLogisticaPage.Url, "Viagem logística");
    }

    [SetUp]
    public Task EntrarAsync() => LoginNaUiAsync();

    [TestCaseSource(nameof(Telas))]
    public async Task PesquisaRapida_ConsultaAceitaPelaApi(string rota, string titulo)
    {
        var pagina = new LogisticaListPage(Page, rota, titulo);
        await pagina.AbrirAsync();

        var resposta = await pagina.PesquisarERetornarRespostaAsync("e2e-pesquisa");

        Assert.That(resposta.Status, Is.EqualTo(200),
            $"A pesquisa rápida de '{titulo}' retornou HTTP {resposta.Status} para {Uri.UnescapeDataString(resposta.Url)}");
        await Expect(new SweetAlert(Page).Popup).ToHaveCountAsync(0);
    }

    private static TestCaseData Tela(string rota, string titulo) =>
        new TestCaseData(rota, titulo).SetArgDisplayNames(titulo);
}
