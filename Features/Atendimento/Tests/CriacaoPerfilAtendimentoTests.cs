using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Atendimento.Tests;

/// <summary>
/// O caminho real do perfil de atendimento, todo pelas telas: o master cria o perfil em /perfil marcando o grupo
/// "Atendimento" (811000 e as filhas 811001–811015), cria o usuário em /usuario com esse perfil e sem empresa (aba
/// Acesso), sai, e o novo usuário entra direto no modo atendimento e abre Módulos. Com empresa no vínculo, o perfil de
/// atendimento também entra direto no atendimento (AutenticadorService.LoginInicial) e passa para a empresa pelo botão
/// da fábrica. Diferente de
/// <see cref="PerfilAtendimentoTests"/>, que usa os dados criados pela API em <see cref="UsuariosAtendimento"/>.
/// Perfil e usuário são apagados no TearDown; os prefixos também são limpos pela fixture em execuções abortadas.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class CriacaoPerfilAtendimentoTests : AtendimentoTestBase
{
    private static readonly int[] Funcionalidades = Enumerable.Range(811000, 16).ToArray();

    private string _perfil = "";

    private string _email = "";

    /// <summary>Empresa do master (E2E_EMPRESA), usada no vínculo do caso "com empresa".</summary>
    private string _empresa = "";

    [SetUp]
    public void GerarNomes()
    {
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        _perfil = $"E2E Atendimento UI {sufixo}";
        _email = $"e2e.atend.ui.{sufixo}@e2e.local";
    }

    [TearDown]
    public async Task ApagarAsync()
    {
        await using var api = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

        // Usuário antes do perfil: o vínculo Usuario_Empresa aponta para o perfil.
        if (await BuscarAsync(api, "v1/usuario", "email", _email) is { } usuario)
        {
            await ApagarAsync(api, $"v1/usuario/{usuario["id"]}");
        }

        if (await BuscarAsync(api, "v1/perfil", "nome", _perfil) is { } perfil)
        {
            await ApagarAsync(api, $"v1/perfil/{perfil["id"]}");
        }
    }

    [TestCase(false, TestName = "PerfilCriadoPelaTelaSemEmpresaDeveDarAcessoAoAtendimento")]
    [TestCase(true, TestName = "PerfilCriadoPelaTelaComEmpresaDeveDarAcessoAoAtendimentoETrocarParaAEmpresa")]
    public async Task PerfilCriadoPelaTelaDeveDarAcessoAoAtendimento(bool comEmpresa)
    {
        var master = new LoginPage(Page);
        await master.LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        _empresa = (await master.LerSessaoAsync()).EmpresaNome
            ?? throw new InvalidOperationException("A sessão do master não trouxe a empresa.");

        await CriarPerfilAsync();
        await CriarUsuarioAsync(comEmpresa);
        await ConferirGravacaoAsync(comEmpresa);

        // Sair: sem a sessão no localStorage o front volta para o login.
        await Page.EvaluateAsync("() => localStorage.clear()");
        var login = new LoginPage(Page);
        await login.AbrirAsync();
        await login.PreencherEEntrarAsync(_email, UsuariosAtendimento.Senha);

        await Expect(Page).ToHaveURLAsync(new Regex("/home$"), new() { Timeout = 30_000 });
        await Expect(login.CardsEmpresa).ToHaveCountAsync(0);
        var sessao = await login.LerSessaoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(sessao.Atendimento, Is.True, "O perfil criado pela tela deveria entrar direto no atendimento.");
            Assert.That(sessao.UsuarioMaster, Is.False);
            // Sem empresa a sessão é a "Atendimento"; com empresa o token de atendimento leva o EmpresaId do vínculo
            // (AutenticadorService.GerarTokenAtendimento) e a sessão mostra a empresa.
            Assert.That(sessao.EmpresaNome, Is.EqualTo(comEmpresa ? _empresa : "Atendimento"));
        });

        await ConferirMenuDeAtendimentoAsync();
        await AbrirModulosPeloMenuAsync();

        if (comEmpresa)
        {
            await TrocarParaAEmpresaAsync();
        }
    }

    /// <summary>Botão da fábrica (AlterarEmpresa.vue): a empresa do vínculo aparece e a sessão sai do atendimento.</summary>
    private async Task TrocarParaAEmpresaAsync()
    {
        // A troca volta para a rota atual (/auth/<rota>?token=...); a partir da /home ela termina em /home.
        await Page.GotoAsync("/home");
        await Page.Locator(".header-company button").ClickAsync();
        var empresas = Page.Locator(".cardEmpresa .v-list-item");
        await Expect(empresas).ToHaveCountAsync(1);
        await Expect(empresas).ToContainTextAsync(_empresa);

        await empresas.ClickAsync();
        await Page.WaitForURLAsync(new Regex("/home$"));
        await Expect(Page.Locator(".header-company")).ToContainTextAsync(_empresa);

        var sessao = await new LoginPage(Page).LerSessaoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(sessao.Atendimento, Is.False, "Depois da troca a sessão deveria ser a da empresa.");
            Assert.That(sessao.EmpresaNome, Is.EqualTo(_empresa));
        });
    }

    /// <summary>/perfil: nome e o checkbox do grupo "Atendimento", que marca as filhas (GerenciarItem → obterFilhos).</summary>
    private async Task CriarPerfilAsync()
    {
        var dialogo = await AbrirCadastroAsync("/perfil", "Criar Perfil");

        await Vuetify.Campo(dialogo, "Nome").FillAsync(_perfil);
        var grupo = await LocalizarFuncionalidadeAsync(dialogo, "Atendimento");
        await grupo.CheckAsync();
        await Expect(grupo).ToBeCheckedAsync();

        await SalvarCadastroAsync(dialogo);
    }

    /// <summary>
    /// Checkbox da funcionalidade nas tabelas do perfil. Cada módulo é um v-data-table paginado de 10 em 10
    /// (Atendimento fica na 2ª página da tabela Base), então percorre as páginas de cada tabela.
    /// </summary>
    private async Task<ILocator> LocalizarFuncionalidadeAsync(ILocator dialogo, string nome)
    {
        var tabelas = dialogo.Locator(".v-data-table");
        await Expect(tabelas.First.Locator("tbody tr").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        for (var t = 0; t < await tabelas.CountAsync(); t++)
        {
            var tabela = tabelas.Nth(t);
            while (true)
            {
                var checkbox = tabela.GetByRole(AriaRole.Checkbox, new() { Name = nome, Exact = true });
                if (await checkbox.CountAsync() > 0)
                {
                    return checkbox;
                }

                var proxima = tabela.Locator(".v-data-table-footer__pagination button").Nth(2);
                if (await proxima.CountAsync() == 0 || await proxima.IsDisabledAsync())
                {
                    break;
                }

                await proxima.ClickAsync();
            }
        }

        throw new AssertionException($"A funcionalidade '{nome}' não aparece em nenhuma tabela do cadastro de perfil.");
    }

    /// <summary>/usuario: login e nome na aba Geral; na aba Acesso, o perfil (e a empresa, se pedida) e "Adicionar".</summary>
    private async Task CriarUsuarioAsync(bool comEmpresa)
    {
        var dialogo = await AbrirCadastroAsync("/usuario", "Criar Usuario");

        await Vuetify.Campo(dialogo, "Login (E-mail)").FillAsync(_email);
        await Vuetify.Campo(dialogo, "Nome").FillAsync("E2E Atendimento UI");

        await dialogo.GetByRole(AriaRole.Tab, new() { Name = "Acesso", Exact = true }).ClickAsync();
        var vuetify = new Vuetify(Page);
        if (comEmpresa)
        {
            await vuetify.SelecionarAutocompleteAsync(dialogo, "Empresa", _empresa);
        }

        await vuetify.SelecionarAutocompleteAsync(dialogo, "Perfil", _perfil);
        if (!comEmpresa)
        {
            await Expect(Vuetify.Container(dialogo, "Empresa").Locator(".v-autocomplete__selection")).ToHaveCountAsync(0);
        }
        await dialogo.GetByRole(AriaRole.Button, new() { Name = "Adicionar", Exact = true }).ClickAsync();
        await Expect(dialogo.Locator("tbody tr", new() { HasText = _perfil })).ToBeVisibleAsync();

        await dialogo.GetByRole(AriaRole.Tab, new() { Name = "Geral", Exact = true }).ClickAsync();
        await Expect(Vuetify.Campo(dialogo, "Login (E-mail)")).ToHaveValueAsync(_email);
        await Expect(Vuetify.Campo(dialogo, "Nome")).ToHaveValueAsync("E2E Atendimento UI");

        await SalvarCadastroAsync(dialogo);
    }

    /// <summary>Na API: o perfil com todas as funcionalidades de atendimento e o vínculo do usuário sem empresa.</summary>
    private async Task ConferirGravacaoAsync(bool comEmpresa)
    {
        await using var api = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

        var perfil = await BuscarAsync(api, "v1/perfil", "nome", _perfil, "Itens")
            ?? throw new AssertionException($"O perfil '{_perfil}' não foi gravado.");
        var itens = perfil["itens"]!.AsArray().Select(i => i!["funcionalidade"]!.GetValue<int>()).Order().ToArray();
        Assert.That(itens, Is.SupersetOf(Funcionalidades), "O grupo Atendimento deveria gravar 811000 e todas as filhas.");

        var usuario = await BuscarAsync(api, "v1/usuario", "email", _email, "Empresas")
            ?? throw new AssertionException($"O usuário '{_email}' não foi gravado.");
        var vinculos = usuario["empresas"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(vinculos, Has.Count.EqualTo(1));
            Assert.That(vinculos[0]!["perfilId"]!.GetValue<long>(), Is.EqualTo(perfil.Id()));
            Assert.That(vinculos[0]!["empresaId"].ComoTexto(), comEmpresa ? Is.EqualTo(api.EmpresaId) : Is.Null,
                comEmpresa ? "O vínculo deveria ser com a empresa escolhida." : "O vínculo de atendimento é sem empresa.");
        });
    }

    /// <summary>Lista (ListHeader) → "Adicionar" → diálogo do CadastroBase ("Criar {entidade}").</summary>
    private async Task<ILocator> AbrirCadastroAsync(string rota, string titulo)
    {
        await Page.GotoAsync(rota);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Adicionar", Exact = true }).ClickAsync();
        var dialogo = Page.Locator(".v-overlay--active.v-dialog").Last;
        await Expect(dialogo.GetByText(titulo, new() { Exact = true })).ToBeVisibleAsync();
        // O que é digitado durante a transição de abertura se perde (visto em /usuario): espera ela terminar.
        await Expect(dialogo.Locator(".v-overlay__content")).Not.ToHaveClassAsync(new Regex("-(enter|leave)-"));
        return dialogo;
    }

    private async Task SalvarCadastroAsync(ILocator dialogo)
    {
        var salvar = dialogo.Locator(".save-button");
        await Expect(salvar).ToBeEnabledAsync();
        await salvar.ClickAsync();

        var alerta = new SweetAlert(Page);
        await alerta.EsperarAsync("Aviso", "Criado com sucesso!");
        await alerta.ConfirmarAsync();
    }

    private static async Task<JsonNode?> BuscarAsync(ApiClient api, string recurso, string campo, string valor, string? include = null) =>
        (await api.GetAsync($"{recurso}?$Top=1&$filter={campo} eq '{valor}'", include)).Registros().FirstOrDefault();

    private static async Task ApagarAsync(ApiClient api, string caminho)
    {
        var resposta = await api.DeleteAsync(caminho);
        if (!resposta.Sucesso && resposta.Status != 404)
        {
            TestContext.Progress.WriteLine($"Cleanup: DELETE {caminho} retornou {resposta.Status}: {resposta.Corpo}");
        }
    }
}
