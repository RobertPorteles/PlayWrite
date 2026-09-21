using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Features.Equipamento.Stub;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Atendimento.Tests;

/// <summary>
/// Master entrando no modo atendimento: pelo item "Atendimento" do menu do avatar (PageBase.vue, LoginAtendimento:
/// POST v1/autenticador/EntrarAtendimento → token novo → RetrieveSessionApp/Atendimento → /home) ou pelo rail
/// "Atendimento" da escolha de empresa na tela de login. O perfil de atendimento fica em <see cref="PerfilAtendimentoTests"/>.
/// Pré-requisitos: front e CCO.Api locais, E2E_LOGIN/E2E_SENHA/E2E_EMPRESA de um usuário master.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class ModoAtendimentoTests : AtendimentoTestBase
{
    [Test]
    public async Task MenuDoUsuarioDeveMostrarAtendimentoParaMaster()
    {
        await EntrarComoMasterAsync();

        await AbrirMenuUsuarioAsync();
        await Expect(ItemAtendimento).ToBeVisibleAsync();
        await Expect(ItemAtendimento.Locator(".mdi-phone")).ToBeVisibleAsync();
    }

    [Test]
    public async Task EntrarEmAtendimentoDeveTrocarSessaoEIrParaHome()
    {
        await EntrarComoMasterAsync();
        var antes = await new LoginPage(Page).LerSessaoAsync();
        Assert.That(antes.Atendimento, Is.False);

        await IrParaEquipamentoAsync();
        var (entrar, sessao) = await EntrarEmAtendimentoAsync();

        Assert.That(entrar.Status, Is.EqualTo(200));
        Assert.That(sessao.Status, Is.EqualTo(200));
        Assert.That(await entrar.Request.HeaderValueAsync("authorization"), Is.EqualTo($"Bearer {antes.Token}"),
            "O backend deveria identificar o usuário pelo token da sessão, não por um login no corpo.");

        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));
        var depois = await new LoginPage(Page).LerSessaoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(depois.Atendimento, Is.True, "sessionApp.dados.acesso.atendimento deveria ser true.");
            Assert.That(depois.Token, Is.Not.Empty.And.Not.EqualTo(antes.Token), "O token deveria ser o do atendimento.");
            Assert.That(depois.EmpresaNome, Is.EqualTo("Atendimento"), "Master entra no atendimento sem empresa.");
        });
    }

    [Test]
    public async Task ModoAtendimentoDeveMostrarSoGrupoAtendimento()
    {
        await EntrarComoMasterAsync();
        await IrParaEquipamentoAsync();
        await EntrarEmAtendimentoAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));

        await ConferirMenuDeAtendimentoAsync();

        await Page.ReloadAsync();
        await ConferirMenuDeAtendimentoAsync();
    }

    /// <summary>Na /home o router.push("home") cai na mesma rota e não remonta a página: o PageBase recarrega o `app`.</summary>
    [Test]
    public async Task ClicarAtendimentoEstandoNaHomeDeveAtualizarMenu()
    {
        await EntrarComoMasterAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));

        await EntrarEmAtendimentoAsync();
        Assert.That((await new LoginPage(Page).LerSessaoAsync()).Atendimento, Is.True);

        await ConferirMenuDeAtendimentoAsync();
    }

    [Test]
    public async Task NoModoAtendimentoDeveAbrirModulosComTokenDeAtendimento()
    {
        await EntrarComoMasterAsync();
        await IrParaEquipamentoAsync();
        await EntrarEmAtendimentoAsync();

        await AbrirModulosPeloMenuAsync();
    }

    [Test]
    public async Task ErroAoEntrarEmAtendimentoDeveAvisarENaoTrocarSessao()
    {
        const string mensagem = "Você não tem acesso ao sistema de atendimento!";
        await Page.RouteAsync(EhEntrarAtendimentoUrl, rota =>
        {
            var cors = new Dictionary<string, string>
            {
                ["Access-Control-Allow-Origin"] = rota.Request.Headers.GetValueOrDefault("origin") ?? E2ESettings.FrontUrl,
                ["Access-Control-Allow-Headers"] = "*",
                ["Access-Control-Allow-Methods"] = "POST",
            };
            return rota.Request.Method == "OPTIONS"
                ? rota.FulfillAsync(new() { Status = 204, Headers = cors })
                : rota.FulfillAsync(new() { Status = 400, ContentType = "text/plain; charset=utf-8", Body = mensagem, Headers = cors });
        });
        await EntrarComoMasterAsync();
        await IrParaEquipamentoAsync();
        var antes = await new LoginPage(Page).LerSessaoAsync();

        await AbrirMenuUsuarioAsync();
        await ItemAtendimento.ClickAsync();

        await new SweetAlert(Page).EsperarAsync("Aviso", mensagem);
        Assert.That(Rede.ChamadasNaApi("RetrieveSessionApp/Atendimento"), Is.Zero);
        Assert.That(await new LoginPage(Page).LerSessaoAsync(), Is.EqualTo(antes), "A sessão não deveria mudar.");
        await Expect(Page).ToHaveURLAsync(new Regex("/equipamento$"));
    }

    /// <summary>Rail "Atendimento" da escolha de empresa (Login.vue): rota anônima, agora conferindo a senha digitada.</summary>
    [Test]
    public async Task MasterPeloRailDoLoginDeveEntrarNoAtendimento()
    {
        var login = new LoginPage(Page);
        await login.AbrirAsync();
        await login.PreencherEEntrarAsync(E2ESettings.Login, E2ESettings.Senha);
        await Expect(login.CardsEmpresa.First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        var rail = Page.Locator(".v-navigation-drawer .v-list-item", new() { HasText = "Atendimento" });
        var resposta = await Page.RunAndWaitForResponseAsync(() => rail.ClickAsync(),
            r => r.Request.Method == "POST" && new Uri(r.Url).AbsolutePath.EndsWith("/v1/autenticador/atendimento", StringComparison.OrdinalIgnoreCase));

        // Só o booleano: a senha não pode aparecer no log do teste.
        var corpo = resposta.Request.PostDataJSON();
        Assert.That(string.IsNullOrEmpty(corpo?.GetProperty("senha").GetString()), Is.False, "O rail deveria mandar a senha digitada.");
        Assert.That(resposta.Status, Is.EqualTo(200));
        await Expect(Page).ToHaveURLAsync(new Regex("/home$"));
        Assert.That((await login.LerSessaoAsync()).Atendimento, Is.True);
    }

    private Task EntrarComoMasterAsync() =>
        new LoginPage(Page).LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

    private async Task IrParaEquipamentoAsync()
    {
        await Page.RunAndWaitForRequestAsync(() => Page.GotoAsync("/equipamento"), ApiEquipamentoStub.EhLista);
        await Expect(Page.Locator("tr", new() { HasText = ApiEquipamentoStub.Modulos[0].Serial })).ToBeVisibleAsync();
    }

    private async Task<(IResponse Entrar, IResponse Sessao)> EntrarEmAtendimentoAsync()
    {
        await AbrirMenuUsuarioAsync();
        var sessao = Page.WaitForResponseAsync(r => r.Url.Contains("/RetrieveSessionApp/Atendimento"));
        var entrar = await Page.RunAndWaitForResponseAsync(
            () => ItemAtendimento.ClickAsync(),
            r => r.Request.Method == "POST" && EhEntrarAtendimentoUrl(r.Url));
        return (entrar, await sessao);
    }

    private static bool EhEntrarAtendimentoUrl(string url) =>
        new Uri(url).AbsolutePath.TrimEnd('/').EndsWith("/v1/autenticador/EntrarAtendimento", StringComparison.OrdinalIgnoreCase);
}
