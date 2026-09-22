using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Diálogo "Cadastrar Cliente" aberto pelo "+" do campo Cliente no wizard de Programação
/// (AutoCompleteCreateField em DadosGeraisStep.vue): o cliente salvo tem que voltar já selecionado no wizard.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class CadastroClienteProgramacaoTests : LogisticaTestBase
{
    /// <summary>Valores fictícios: se aparecerem nos campos, a resposta veio do stub e não do ViaCEP real.</summary>
    private const string ViaCepStub =
        """{"cep":"01310-100","logradouro":"Rua do Stub E2E","bairro":"Bairro Stub E2E","localidade":"Cidade Stub E2E","uf":"SP"}""";

    private ProgramacaoWizard _wizard = null!;

    [SetUp]
    public async Task AbrirWizardAsync()
    {
        // A busca de CEP do diálogo sai do navegador direto para viacep.com.br; responde aqui para não sair da máquina.
        await Page.RouteAsync("https://viacep.com.br/**", rota =>
            rota.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = ViaCepStub }));

        await LoginNaUiAsync();
        var programacoes = new ProgramacoesPage(Page);
        await programacoes.AbrirAsync();
        _wizard = await programacoes.NovaProgramacaoAsync();
    }

    [Test]
    public async Task PessoaJuridica_CadastradaPeloWizard_FicaSelecionadaEGravaCelularEEndereco()
    {
        var nome = LogisticaDataFactory.Nome("Cliente");
        var cnpj = LogisticaDataFactory.GerarCnpj();
        var dialogo = await _wizard.AbrirCadastroClienteAsync();
        var cadastrar = dialogo.Locator(".save-button");

        await Expect(cadastrar).ToBeDisabledAsync();
        await Vuetify.Campo(dialogo, "Razão Social").FillAsync(nome);
        await Expect(cadastrar).ToBeDisabledAsync();
        await Vuetify.Campo(dialogo, "CNPJ").FillAsync(cnpj);
        await Expect(cadastrar).ToBeEnabledAsync();

        await Vuetify.Campo(dialogo, "Nome Fantasia").FillAsync(nome);
        await Vuetify.Campo(dialogo, "Celular").FillAsync("11961234567");
        await Vuetify.Campo(dialogo, "E-mail").FillAsync("e2e@teste.local");
        await Vuetify.Campo(dialogo, "CEP").FillAsync("01310100");
        await Expect(Vuetify.Campo(dialogo, "Rua")).ToHaveValueAsync("Rua do Stub E2E");
        await Vuetify.Campo(dialogo, "Nº").FillAsync("1578");

        var cliente = await CadastrarAsync(dialogo, nome);

        Assert.Multiple(() =>
        {
            Assert.That(cliente["tipoCliente"].ComoInt(), Is.EqualTo(2));
            Assert.That(cliente["nome"].ComoTexto(), Is.EqualTo(nome), "O nome do PJ deveria acompanhar a razão social.");
            Assert.That(SoDigitos(cliente["cnpj"].ComoTexto()), Is.EqualTo(cnpj));
            Assert.That(cliente["ddi"].ComoTexto(), Is.EqualTo("+55"));
            Assert.That(cliente["celular"].ComoTexto(), Is.EqualTo("11961234567"));
            Assert.That(cliente["logradouro"].ComoTexto(), Is.EqualTo("Rua do Stub E2E"));
            Assert.That(cliente["bairro"].ComoTexto(), Is.EqualTo("Bairro Stub E2E"));
            Assert.That(cliente["cidade"].ComoTexto(), Is.EqualTo("Cidade Stub E2E"));
            Assert.That(cliente["estado"].ComoTexto(), Is.EqualTo("SP"));
            Assert.That(cliente["numero"].ComoInt(), Is.EqualTo(1578));
            Assert.That(cliente["pais"].ComoTexto(), Is.EqualTo("Brasil"));
        });
    }

    [Test]
    public async Task PessoaFisica_CadastradaPeloWizard_FicaSelecionadaComRazaoSocialIgualAoNome()
    {
        var nome = LogisticaDataFactory.Nome("Cliente");
        var cpf = LogisticaDataFactory.GerarCpf();
        var dialogo = await _wizard.AbrirCadastroClienteAsync();

        await dialogo.GetByLabel("Pessoa Fisica", new() { Exact = true }).CheckAsync();
        await Vuetify.Campo(dialogo, "Nome").FillAsync(nome);
        await Vuetify.Campo(dialogo, "CPF").FillAsync(cpf);

        var cliente = await CadastrarAsync(dialogo, nome);

        Assert.Multiple(() =>
        {
            Assert.That(cliente["tipoCliente"].ComoInt(), Is.EqualTo(1));
            Assert.That(cliente["razaoSocial"].ComoTexto(), Is.EqualTo(nome), "A razão social do PF deveria acompanhar o nome.");
            Assert.That(SoDigitos(cliente["cpf"].ComoTexto()), Is.EqualTo(cpf));
        });
    }

    /// <summary>Cadastra, exige alerta de sucesso, o diálogo fechado e o cliente selecionado; devolve o registro gravado.</summary>
    private async Task<JsonNode> CadastrarAsync(ILocator dialogo, string nome)
    {
        var alerta = new SweetAlert(Page);
        await dialogo.Locator(".save-button").ClickAsync();
        await Expect(alerta.Popup).ToBeVisibleAsync(new() { Timeout = 20_000 });
        var mensagem = $"{await alerta.Titulo.InnerTextAsync()} {await alerta.Texto.InnerTextAsync()}";
        var id = await Logistica.RegistrarCriadoNaUiAsync(LogisticaRecursos.Cliente, "nome", nome);
        Assert.That(await alerta.EhErroOuAvisoAsync(), Is.False, $"O cliente não foi cadastrado: {mensagem}");
        await alerta.ConfirmarAsync();

        await Expect(dialogo).ToBeHiddenAsync();
        await Expect(Vuetify.Container(_wizard.PassoAtivo, "Cliente")).ToContainTextAsync(nome);

        Assert.That(id, Is.Not.Null, $"'{nome}' não foi encontrado em {LogisticaRecursos.Cliente} depois de cadastrar pelo wizard.");
        return await Api.GetAsync($"{LogisticaRecursos.Cliente}/{id}")
               ?? throw new InvalidOperationException($"Cliente {id} não encontrado.");
    }

    private static string SoDigitos(string? texto) => string.Concat((texto ?? "").Where(char.IsDigit));
}
