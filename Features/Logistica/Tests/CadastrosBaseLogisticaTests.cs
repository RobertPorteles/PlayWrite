using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Cadastros de apoio pela tela: criar, conferir na API, pesquisar e excluir.
/// Pré-requisitos (ex.: o cliente do centro de custo) vêm da API.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class CadastrosBaseLogisticaTests : LogisticaTestBase
{
    [SetUp]
    public Task EntrarAsync() => LoginNaUiAsync();

    [Test]
    public async Task Cliente_CriarPesquisarEExcluirPelaTela()
    {
        var nome = LogisticaDataFactory.Nome("Cliente");
        var pagina = new ClientePage(Page);
        await pagina.AbrirAsync();

        await pagina.CriarPessoaJuridicaAsync(nome, LogisticaDataFactory.GerarCnpj());
        await ConferirGravadoAsync(LogisticaRecursos.Cliente, "razaoSocial", nome);

        await ExcluirPelaTelaAsync(pagina, nome);
    }

    [Test]
    public async Task CentroCusto_CriarPesquisarEExcluirPelaTela()
    {
        var cliente = await Logistica.CriarClienteAsync();
        var nome = LogisticaDataFactory.Nome("CentroCusto");
        var pagina = new CentroCustoPage(Page);
        await pagina.AbrirAsync();

        await pagina.CriarAsync(nome, cliente["nome"].ComoTexto()!);
        await ConferirGravadoAsync(LogisticaRecursos.CentroCusto, "nome", nome);

        await ExcluirPelaTelaAsync(pagina, nome);
    }

    [Test]
    public async Task Solicitante_CriarPesquisarEExcluirPelaTela()
    {
        var cliente = await Logistica.CriarClienteAsync();
        var nome = LogisticaDataFactory.Nome("Solicitante");
        var pagina = new SolicitantePage(Page);
        await pagina.AbrirAsync();

        await pagina.CriarAsync(nome, "solicitante.e2e@teste.local", cliente["nome"].ComoTexto()!);
        await ConferirGravadoAsync(LogisticaRecursos.Solicitante, "nome", nome);

        await ExcluirPelaTelaAsync(pagina, nome);
    }

    [Test]
    public async Task LocalDeInteresse_CriarPesquisarEExcluirPelaTela()
    {
        var nome = LogisticaDataFactory.Nome("Local");
        var pagina = new LocaisInteressePage(Page);
        await pagina.AbrirAsync();

        await pagina.CriarAsync(nome, LogisticaDataFactory.PontoOrigem);
        await ConferirGravadoAsync(LogisticaRecursos.EnderecoLogistica, "nome", nome);

        await ExcluirPelaTelaAsync(pagina, nome);
    }

    private async Task ConferirGravadoAsync(string recurso, string campo, string valor)
    {
        var id = await Logistica.RegistrarCriadoNaUiAsync(recurso, campo, valor);
        Assert.That(id, Is.Not.Null, $"'{valor}' não foi encontrado em {recurso} depois de salvar pela tela.");
    }

    private async Task ExcluirPelaTelaAsync(LogisticaListPage pagina, string nome)
    {
        var linha = await pagina.LocalizarLinhaAsync(nome);
        await pagina.ExcluirAsync(linha);
        await Expect(pagina.Linha(nome)).ToHaveCountAsync(0);
    }
}
