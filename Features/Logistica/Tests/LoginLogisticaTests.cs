using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

[TestFixture]
[Category(Categoria)]
public class LoginLogisticaTests : LogisticaTestBase
{
    /// <summary>Grupo do menu, item, rota e título de cada tela de logística.</summary>
    public static IEnumerable<TestCaseData> TelasLogistica()
    {
        yield return Tela("Pessoas e Escalas", "Cliente", ClientePage.Url, "Cliente");
        yield return Tela("Frota e Ativos", "Centro de custo", CentroCustoPage.Url, "Centro de Custo");
        yield return Tela("Pessoas e Escalas", "Solicitante", SolicitantePage.Url, "Solicitante");
        yield return Tela("Planejamento", "Locais de interesse", LocaisInteressePage.Url, "Locais de interesse");
        yield return Tela("Planejamento", "Programações", ProgramacoesPage.Url, "Programações");
        yield return Tela("Operação em Tempo Real", "Viagem Logística", ViagemLogisticaPage.Url, "Viagem logística");
    }

    [Test]
    public async Task LoginValido_AbreAplicacaoComMenusDeLogistica()
    {
        var sessao = await LoginNaUiAsync();

        await Expect(Page).ToHaveURLAsync(LoginPage.AppCarregada());
        Assert.That(sessao.Token, Is.Not.Empty);

        var menu = new AppMenu(Page);
        foreach (var tela in TelasLogistica())
        {
            var grupo = (string)tela.Arguments[0]!;
            var item = (string)tela.Arguments[1]!;
            await menu.ExpandirGrupoAsync(grupo);
            await Expect(menu.Item(grupo, item)).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task LoginComSenhaInvalida_ExibeAvisoEPermaneceNoLogin()
    {
        var login = new LoginPage(Page);
        await login.AbrirAsync();
        await login.PreencherEEntrarAsync(E2ESettings.Login, "senha-invalida-e2e");

        await new SweetAlert(Page).EsperarAsync("Aviso");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login$"));
        await Expect(login.CardsEmpresa).ToHaveCountAsync(0);
    }

    [TestCaseSource(nameof(TelasLogistica))]
    public async Task NavegacaoPeloMenu_AbreTelaDeLogistica(string grupo, string item, string rota, string titulo)
    {
        await LoginNaUiAsync();

        await new AppMenu(Page).NavegarAsync(grupo, item, rota);
        await new LogisticaListPage(Page, rota, titulo).EsperarCarregadaAsync();
        await Expect(new SweetAlert(Page).Popup).ToHaveCountAsync(0);
    }

    private static TestCaseData Tela(string grupo, string item, string rota, string titulo) =>
        new TestCaseData(grupo, item, rota, titulo).SetArgDisplayNames(item);
}
