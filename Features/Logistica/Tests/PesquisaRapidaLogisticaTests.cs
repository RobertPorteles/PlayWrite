using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// "Pesquisa rápida" de cada tela de logística: a consulta filtrada montada pelo front precisa ser aceita pela API e
/// encontrar o registro criado pelo teste (excluído no TearDown).
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

    /// <summary>
    /// Viagem logística fica de fora: as colunas pesquisáveis (motorista, placa) só existem com ordem de serviço, e a
    /// consulta já falha em <see cref="PesquisaRapida_ConsultaAceitaPelaApi"/>.
    /// </summary>
    public static IEnumerable<TestCaseData> TelasComRegistro() => Telas().Where(t => (string)t.Arguments[0]! != ViagemLogisticaPage.Url);

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

    [TestCaseSource(nameof(TelasComRegistro))]
    public async Task PesquisaRapida_EncontraRegistroCriado(string rota, string titulo)
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        if (rota == ProgramacoesPage.Url)
        {
            await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));
        }

        var termo = titulo switch
        {
            "Centro de Custo" => apoio.NomeCentroCusto,
            "Solicitante" => apoio.NomeSolicitante,
            "Locais de interesse" => apoio.NomeOrigem,
            _ => apoio.NomeCliente,
        };

        var pagina = new LogisticaListPage(Page, rota, titulo);
        await pagina.AbrirAsync();
        await pagina.PesquisarAsync(termo);
    }

    private static TestCaseData Tela(string rota, string titulo) =>
        new TestCaseData(rota, titulo).SetArgDisplayNames(titulo);
}
