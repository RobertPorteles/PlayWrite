using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Tutorial em vídeo da exportação para o cliente: em Viagem Logística, o botão Exportar baixa uma planilha Excel com
/// as viagens finalizadas do cliente no período. O cliente tem duas viagens finalizadas, criadas pela API; a planilha
/// baixada é mostrada na tela para o vídeo exibir o resultado.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class TutorialExportacaoTests : TutorialTestBase
{
    [Test]
    public async Task TutorialDaExportacaoDasViagensParaOCliente()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var primeira = await Logistica.CriarOrdemServicoFaturavelAsync(apoio);
        var segunda = await Logistica.CriarOrdemServicoFaturavelAsync(apoio);
        var menu = new AppMenu(Page);

        await LoginNaUiAsync();

        // 1. Onde fica.
        await menu.NavegarAsync("Operação em Tempo Real", "Viagem Logística", ViagemLogisticaPage.Url);
        await NarrarAsync("1. Viagem Logística",
            $"A exportação fica em Operação em Tempo Real → Viagem Logística. O cliente {apoio.NomeCliente} tem duas viagens finalizadas hoje: as das OS #{primeira.OrdemServicoId} e #{segunda.OrdemServicoId}.");

        // 2. O diálogo.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Exportar", Exact = true }).ClickAsync();
        var dialogo = Page.Locator(".v-overlay--active .v-card", new() { HasText = "Exportar viagens de logística" }).Last;
        await Expect(dialogo).ToBeVisibleAsync();
        await NarrarAsync("2. Período",
            "O botão Exportar abre o diálogo. O período é obrigatório e vem do dia 1º do mês até hoje. Ele conta pela data real de início da viagem, e só entram viagens finalizadas.");

        // 3. Filtros.
        var cliente = dialogo.GetByRole(AriaRole.Combobox, new() { Name = "Cliente", Exact = true });
        await cliente.ClickAsync();
        await cliente.FillAsync(apoio.NomeCliente);
        await Page.GetByRole(AriaRole.Option, new() { Name = apoio.NomeCliente }).ClickAsync();
        await NarrarAsync("3. Cliente",
            "Para exportar para um cliente, escolha o cliente. Sem cliente, a planilha traz as viagens de todos os clientes.");

        await dialogo.GetByRole(AriaRole.Combobox, new() { Name = "Solicitante", Exact = true }).ClickAsync(new() { Force = true });
        await Expect(Page.GetByRole(AriaRole.Option, new() { Name = apoio.NomeSolicitante })).ToBeVisibleAsync();
        await NarrarAsync("3. Solicitante e centro de custo",
            "Solicitante e centro de custo são opcionais e só listam os do cliente escolhido. Servem para mandar a cada área só as viagens dela. Aqui vamos deixar em branco.");
        await Page.Keyboard.PressAsync("Escape");

        // 4. Exportar e ver a planilha.
        var download = await Page.RunAndWaitForDownloadAsync(
            () => dialogo.GetByRole(AriaRole.Button, new() { Name = "Exportar", Exact = true }).ClickAsync());
        Assert.That(download.SuggestedFilename, Does.Match(@"^viagens-logistica-\d{8}-\d{8}\.xlsx$"));
        var planilha = Path.Combine(PastaArquivos, download.SuggestedFilename);
        await download.SaveAsAsync(planilha);
        TestContext.AddTestAttachment(planilha);

        var linhas = LinhasDaPlanilha(planilha);
        Assert.That(linhas, Has.Count.EqualTo(3), "Cabeçalho e as duas viagens do cliente.");
        Assert.That(linhas.Skip(1).Select(l => l[0]), Is.All.EqualTo(apoio.NomeCliente));
        await NarrarAsync("4. Download",
            $"Clicando em Exportar, o navegador baixa {download.SuggestedFilename}, com as datas do período no nome, para a pasta de downloads.");

        await MostrarPrevisaoPlanilhaAsync(download.SuggestedFilename, linhas,
            "Cliente", "Data de Início", "Hora de Início", "Data de Fim", "Hora de Fim", "ID do atendimento", "OS Cliente",
            "Centro de custo", "Solicitante", "Origem", "Destino", "Passageiros", "Status do serviço");
        await NarrarAsync("5. A planilha",
            "Uma linha por viagem: cliente, início e fim reais, atendimento, OS, centro de custo, solicitante, origem, destino, passageiros e status. Também traz tipo de veículo, placa, bilíngue, receptivo e observações.");
        await NarrarAsync("5. A planilha",
            "Ela não traz valores: é o detalhamento operacional que vai junto da fatura. Km inicial e final ainda saem vazios. Fim do tutorial!");
        await EsconderPrevisaoPlanilhaAsync();
        TestContext.Progress.WriteLine($"Planilha exportada: {planilha}");
    }
}
