using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Fluxo principal da logística, do login à finalização da viagem, navegando pelo menu como um usuário.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class FluxoCompletoLogisticaTests : LogisticaTestBase
{
    [Test]
    public async Task DoLoginAteAFinalizacaoDaViagem()
    {
        // 1. Login e dados de apoio (cliente, centro de custo, solicitante, passageiro/carga, locais).
        await LoginNaUiAsync();
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var menu = new AppMenu(Page);

        // 2. Programação completa pelo wizard, com ordem de serviço.
        await menu.NavegarAsync("Planejamento", "Programações", ProgramacoesPage.Url);
        var programacoes = new ProgramacoesPage(Page);
        await programacoes.EsperarCarregadaAsync();

        var wizard = await programacoes.NovaProgramacaoAsync();
        await wizard.PreencherDadosGeraisAsync(apoio);
        await wizard.ProximoAsync(2);
        await wizard.AdicionarItemAsync(apoio);
        await wizard.ProximoAsync(3);
        await wizard.ProximoAsync(4);

        var (veiculo, motorista) = await wizard.DefinirOrdemServicoAsync(E2ESettings.Veiculo, E2ESettings.Motorista);
        if (veiculo is null || motorista is null)
        {
            Assert.Ignore("A empresa não tem veículo e/ou motorista para a ordem de serviço. Cadastre ou defina E2E_VEICULO/E2E_MOTORISTA.");
        }

        await wizard.SalvarAsync();

        var solicitacao = await Logistica.BuscarSolicitacaoPorClienteAsync(apoio.Cliente.Id());
        Assert.That(solicitacao, Is.Not.Null, "A programação salva pela tela não foi encontrada na API.");
        Assert.That(solicitacao!["status"].ComoInt(), Is.EqualTo(LogisticaDataFactory.StatusProgramacaoProgramada),
            "Com veículo e motorista na OS a programação deveria ficar Programada.");

        var linhaProgramacao = await programacoes.LocalizarLinhaAsync(apoio.NomeCliente);
        await Expect(await programacoes.CelulaAsync(linhaProgramacao, "Status")).ToContainTextAsync("Programada");

        // 3. O CCO.Worker criaria e iniciaria a viagem pela posição do veículo; aqui isso é simulado via API.
        var observacao = LogisticaDataFactory.Nome("Viagem");
        var viagem = await Logistica.CriarViagemIniciadaAsync(solicitacao, observacao);

        // 4. Acompanhamento e finalização na tela Viagem Logística.
        await menu.NavegarAsync("Operação em Tempo Real", "Viagem Logística", ViagemLogisticaPage.Url);
        var viagens = new ViagemLogisticaPage(Page);
        await viagens.EsperarCarregadaAsync();

        var linhaViagem = await viagens.LocalizarLinhaAsync(observacao);
        await Expect(await viagens.CelulaAsync(linhaViagem, "Status")).ToContainTextAsync("Iniciada");

        var chaveViagem = await viagens.ChaveDaLinhaAsync(linhaViagem);
        await viagens.VisualizarAsync(linhaViagem, apoio.NomeItem);
        await viagens.FinalizarAsync(linhaViagem, apoio.NomeItem, "Entrega concluída (teste E2E)");

        await Expect(await viagens.StatusDaViagemAsync(chaveViagem)).ToContainTextAsync("Finalizada");

        // 5. Estado final conferido na API.
        var final = await Logistica.ObterViagemAsync(viagem.Id());
        Assert.Multiple(() =>
        {
            Assert.That(final["status"].ComoInt(), Is.EqualTo(LogisticaDataFactory.StatusViagemFinalizada));
            Assert.That(final["itens"]!.AsArray().Select(i => i!["status"].ComoInt()),
                Is.All.EqualTo(LogisticaDataFactory.StatusViagemFinalizada));
            Assert.That(final["dataHoraRealFim"].ComoTexto(), Is.Not.Null.And.Not.Empty);
        });
    }
}
