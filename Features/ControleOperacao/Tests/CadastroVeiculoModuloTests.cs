using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Features.ControleOperacao.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.ControleOperacao.Tests;

/// <summary>
/// O campo Módulo do cadastro de veículo (components/geral/veiculo/Cadastro.vue) fica na tela, mas desabilitado.
/// Na edição o save manda só o diff (PATCH), então o módulo já gravado precisa continuar exibido e
/// sobreviver a um save pela tela.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class CadastroVeiculoModuloTests : ControleOperacaoTestBase
{
    private const string Modulo = "Módulo";

    private LogisticaListPage _lista = null!;

    [SetUp]
    public async Task EntrarAsync()
    {
        await new LoginPage(Page).LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        _lista = new LogisticaListPage(Page, "/veiculo", "Veículos");
    }

    [Test]
    public async Task NovoVeiculoNaoDevePermitirInformarModulo()
    {
        await _lista.AbrirAsync();
        var dialogo = await _lista.AdicionarAsync("Criar Veículo");

        var campo = Vuetify.Campo(dialogo, Modulo);
        await Expect(campo).ToBeVisibleAsync();
        await Expect(campo).ToBeDisabledAsync();
        await Expect(campo).ToHaveValueAsync("");
    }

    [Test]
    public async Task EditarVeiculoDeveExibirModuloSemPermitirAlterarEPreservarAoSalvar()
    {
        var veiculo = await CriarVeiculoAsync();
        var id = veiculo.Id();
        var moduloGravado = veiculo["modulo"].ComoTexto()!;

        await _lista.AbrirAsync();
        await _lista.AbrirAcaoAsync(await LocalizarLinhaAsync(veiculo["identificador"].ComoTexto()!), "Editar");
        var dialogo = _lista.Dialogo;
        await Expect(dialogo.GetByText("Editar Veículo", new() { Exact = true })).ToBeVisibleAsync();

        var campo = Vuetify.Campo(dialogo, Modulo);
        await Expect(campo).ToBeDisabledAsync();
        await Expect(campo).ToHaveValueAsync(moduloGravado);

        var novaDescricao = Nome("Descricao");
        await Vuetify.Campo(dialogo, "Descrição").FillAsync(novaDescricao);

        var salvar = dialogo.Locator(".save-button");
        await Expect(salvar).ToBeEnabledAsync();
        var resposta = await Page.RunAndWaitForResponseAsync(
            () => salvar.ClickAsync(),
            r => r.Request.Method == "PATCH" && r.Url.EndsWith($"/{id}", StringComparison.OrdinalIgnoreCase));
        Assert.That(resposta.Status, Is.EqualTo(200), $"O PATCH do veículo retornou {resposta.Status}: {await resposta.TextAsync()}");

        var alerta = new SweetAlert(Page);
        await alerta.EsperarAsync("Aviso");
        await alerta.ConfirmarAsync();

        var gravado = await Api.GetAsync($"{ControleOperacaoRecursos.Veiculo}/{id}");
        Assert.Multiple(() =>
        {
            Assert.That(gravado?["descricao"].ComoTexto(), Is.EqualTo(novaDescricao),
                "O save pela tela não gravou a descrição alterada.");
            Assert.That(gravado?["modulo"].ComoTexto(), Is.EqualTo(moduloGravado),
                "Salvar pela tela apagou ou alterou o Módulo, que está desabilitado.");
        });
    }

    private async Task<JsonNode> CriarVeiculoAsync()
    {
        var sufixo = Random.Shared.Next(1000, 9999);
        var corpo = new JsonObject
        {
            ["identificador"] = Nome("Veiculo"),
            ["placa"] = $"E2E{sufixo}",
            ["modulo"] = $"E2E-MOD-{sufixo}",
            ["ativo"] = true,
            // Obrigatórios na tela: sem eles o Salvar fica desabilitado.
            ["caracteristica"] = new JsonObject
            {
                ["tipoVeiculoId"] = await PrimeiroIdAsync(ControleOperacaoRecursos.TiposVeiculo, "tipo de veículo"),
                ["tipoOperacaoId"] = await PrimeiroIdAsync(ControleOperacaoRecursos.TiposOperacao, "tipo de operação"),
            },
        };

        var criado = (await Api.PostAsync(ControleOperacaoRecursos.Veiculo, corpo))
            ?? throw new InvalidOperationException($"POST {ControleOperacaoRecursos.Veiculo} não devolveu corpo.");

        RegistrarCriado(ControleOperacaoRecursos.Veiculo, criado.Id().ToString());
        return criado;
    }

    private async Task<long> PrimeiroIdAsync(string caminho, string descricao)
    {
        var primeiro = (await Api.GetAsync(caminho)).Registros().FirstOrDefault();
        if (primeiro is null)
        {
            Assert.Ignore($"A empresa não tem nenhum {descricao}; sem ele a tela não deixa salvar o veículo.");
        }

        return primeiro.Id();
    }

    /// <summary>
    /// A lista de veículos carrega tudo de uma vez e pagina no navegador (sem requisição por página),
    /// então avança o rodapé até a linha aparecer.
    /// </summary>
    private async Task<ILocator> LocalizarLinhaAsync(string identificador)
    {
        var linha = _lista.Linha(identificador).First;
        var info = _lista.Tabela.Locator(".v-data-table-footer__info");
        var proxima = _lista.Tabela.Locator(".v-data-table-footer__pagination button").Nth(2);

        while (await linha.CountAsync() == 0 && await proxima.IsEnabledAsync())
        {
            var paginaAtual = await info.InnerTextAsync();
            await proxima.ClickAsync();
            await Expect(info).Not.ToHaveTextAsync(paginaAtual);
        }

        await Expect(linha).ToBeVisibleAsync();
        return linha;
    }
}
