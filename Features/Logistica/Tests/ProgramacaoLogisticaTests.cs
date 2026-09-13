using System.Text.RegularExpressions;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>Programações pelo wizard, com dados de apoio criados via API.</summary>
[TestFixture]
[Category(Categoria)]
public class ProgramacaoLogisticaTests : LogisticaTestBase
{
    private ProgramacoesPage _programacoes = null!;

    [SetUp]
    public async Task EntrarAsync()
    {
        await LoginNaUiAsync();
        _programacoes = new ProgramacoesPage(Page);
    }

    [Test]
    public async Task CriarProgramacaoPeloWizard_SemOrdemServico_FicaPendente()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        await _programacoes.AbrirAsync();

        var wizard = await _programacoes.NovaProgramacaoAsync();
        await wizard.PreencherDadosGeraisAsync(apoio);
        await wizard.ProximoAsync(2);
        await wizard.AdicionarItemAsync(apoio);
        await wizard.ProximoAsync(3);
        await wizard.ProximoAsync(4);
        await wizard.SalvarAsync();

        var solicitacao = await Logistica.BuscarSolicitacaoPorClienteAsync(apoio.Cliente.Id());
        Assert.That(solicitacao, Is.Not.Null, "A programação salva pela tela não foi encontrada na API.");

        var itens = solicitacao!["itens"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(solicitacao["status"].ComoInt(), Is.EqualTo(LogisticaDataFactory.StatusProgramacaoPendente));
            Assert.That(solicitacao["origemSolicitacaoId"].ComoInt(), Is.EqualTo(LogisticaDataFactory.OrigemSolicitacaoInterno));
            Assert.That(solicitacao["ordemServico"], Is.Null);
            Assert.That(itens, Has.Count.EqualTo(1));
            Assert.That(itens[0]!["enderecos"]!.AsArray().Select(e => e!["tipoEndereco"].ComoInt()),
                Is.SupersetOf(new int?[] { LogisticaDataFactory.EnderecoOrigem, LogisticaDataFactory.EnderecoDestino }));
        });

        var linha = await _programacoes.LocalizarLinhaAsync(apoio.NomeCliente);
        await Expect(await _programacoes.CelulaAsync(linha, "Status")).ToContainTextAsync("Pendente");
    }

    [Test]
    public async Task EnviarOrdemServico_ComVeiculoEMotorista_FicaProgramada()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var solicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));
        await _programacoes.AbrirAsync();

        var linha = await _programacoes.LocalizarLinhaAsync(apoio.NomeCliente);
        await Expect(await _programacoes.CelulaAsync(linha, "Status")).ToContainTextAsync("Pendente");

        var wizard = await _programacoes.EnviarOrdemServicoAsync(linha);
        var (veiculo, motorista) = await wizard.DefinirOrdemServicoAsync(E2ESettings.Veiculo, E2ESettings.Motorista);
        if (veiculo is null || motorista is null)
        {
            Assert.Ignore("A empresa não tem veículo e/ou motorista para a ordem de serviço. Cadastre ou defina E2E_VEICULO/E2E_MOTORISTA.");
        }

        await wizard.SalvarAsync();

        var atualizada = await Logistica.ObterSolicitacaoAsync(solicitacao.Id());
        Assert.Multiple(() =>
        {
            Assert.That(atualizada["status"].ComoInt(), Is.EqualTo(LogisticaDataFactory.StatusProgramacaoProgramada));
            Assert.That(atualizada["ordemServico"]?["veiculoId"].ComoLong(), Is.Not.Null);
            Assert.That(atualizada["ordemServico"]?["motoristaId"].ComoTexto(), Is.Not.Null.And.Not.Empty);
        });

        await Expect(await _programacoes.CelulaAsync(_programacoes.Linha(apoio.NomeCliente).First, "Status"))
            .ToContainTextAsync("Programada");
    }

    [Test]
    public async Task AvancarWizardSemOrigemDaSolicitacao_ExibeAviso()
    {
        await _programacoes.AbrirAsync();
        var wizard = await _programacoes.NovaProgramacaoAsync();

        await wizard.BotaoProximo.ClickAsync();

        var alerta = new SweetAlert(Page);
        await alerta.EsperarAsync("Aviso", "Informe a origem da solicitação.");
        await alerta.ConfirmarAsync();
        await Expect(wizard.Passo(1)).ToHaveClassAsync(new Regex("v-stepper-item--selected"));
    }
}
