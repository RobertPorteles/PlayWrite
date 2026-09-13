using System.Text.Json.Nodes;
using PlaywrightTests.Features.Logistica.Api;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Viagem logística: a viagem "Iniciada" é criada via API (simulando o CCO.Worker) e tratada pela tela.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class ViagemLogisticaTests : LogisticaTestBase
{
    private DadosApoio _apoio = null!;
    private JsonNode _viagem = null!;
    private string _observacao = "";
    private ViagemLogisticaPage _viagens = null!;

    [SetUp]
    public async Task PrepararViagemIniciadaAsync()
    {
        await LoginNaUiAsync();

        _apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var solicitacao = await Logistica.CriarSolicitacaoAsync(_apoio, LogisticaDataFactory.Nome("Programacao"));
        _observacao = LogisticaDataFactory.Nome("Viagem");
        _viagem = await Logistica.CriarViagemIniciadaAsync(solicitacao, _observacao);

        _viagens = new ViagemLogisticaPage(Page);
        await _viagens.AbrirAsync();
    }

    [Test]
    public async Task VisualizarViagem_ExibeResumoEItem()
    {
        var linha = await _viagens.LocalizarLinhaAsync(_observacao);
        await Expect(await _viagens.CelulaAsync(linha, "Status")).ToContainTextAsync("Iniciada");

        await _viagens.VisualizarAsync(linha, _apoio.NomeItem);
    }

    [Test]
    public async Task FinalizarViagem_PelaTela_FicaFinalizada()
    {
        var linha = await _viagens.LocalizarLinhaAsync(_observacao);
        var chave = await _viagens.ChaveDaLinhaAsync(linha);
        await _viagens.FinalizarAsync(linha, _apoio.NomeItem, "Finalizada pelo teste E2E");

        await Expect(await _viagens.StatusDaViagemAsync(chave)).ToContainTextAsync("Finalizada");
        await AssertStatusNaApiAsync(LogisticaDataFactory.StatusViagemFinalizada);
    }

    [Test]
    public async Task CancelarViagem_ExigeMotivo_EFicaCancelada()
    {
        var linha = await _viagens.LocalizarLinhaAsync(_observacao);
        var chave = await _viagens.ChaveDaLinhaAsync(linha);
        var dialogo = await _viagens.AbrirCancelamentoAsync(linha, _apoio.NomeItem);
        await Expect(ViagemLogisticaPage.BotaoConfirmarCancelamento(dialogo)).ToBeDisabledAsync();

        await _viagens.InformarMotivoEConfirmarCancelamentoAsync(dialogo, "Cancelada pelo teste E2E");

        await Expect(await _viagens.StatusDaViagemAsync(chave)).ToContainTextAsync("Cancelada");
        await AssertStatusNaApiAsync(LogisticaDataFactory.StatusViagemCancelada);
    }

    private async Task AssertStatusNaApiAsync(int status)
    {
        var viagem = await Logistica.ObterViagemAsync(_viagem.Id());
        Assert.Multiple(() =>
        {
            Assert.That(viagem["status"].ComoInt(), Is.EqualTo(status));
            Assert.That(viagem["itens"]!.AsArray().Select(i => i!["status"].ComoInt()), Is.All.EqualTo(status));
        });
    }
}
