using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Atendimento.Tests;

/// <summary>
/// Login como perfil de atendimento: usuários não-master criados por <see cref="UsuariosAtendimento"/> (perfil com as
/// funcionalidades 8110xx, vínculo sem empresa) entram direto no modo atendimento, sem escolher empresa. O usuário
/// "misto" também tem a empresa de E2E_EMPRESA e passa para ela pelo botão da fábrica.
/// Pré-requisitos: front e CCO.Api locais, E2E_LOGIN/E2E_SENHA/E2E_EMPRESA de um master (para a fixture criar os dados).
/// </summary>
[TestFixture]
[Category(Categoria)]
public class PerfilAtendimentoTests : AtendimentoTestBase
{
    [Test]
    public async Task PerfilAtendimentoDeveEntrarDiretoNoModoAtendimento()
    {
        // A /home consulta o próprio usuário; um erro ali vira alerta logo na entrada.
        var consultaHome = Page.WaitForResponseAsync(r => r.Url.Contains("/usuario/UsuarioEhEmpresaDetro"));
        await EntrarComPerfilAtendimentoAsync(UsuariosAtendimento.SoAtendimento);

        var sessao = await new LoginPage(Page).LerSessaoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(sessao.Atendimento, Is.True);
            Assert.That(sessao.UsuarioMaster, Is.False);
            Assert.That(sessao.EmpresaNome, Is.EqualTo("Atendimento"));
        });
        Assert.That((await consultaHome).Status, Is.EqualTo(200));
        await Expect(new SweetAlert(Page).Popup).ToHaveCountAsync(0);

        await ConferirMenuDeAtendimentoAsync();
    }

    [Test]
    public async Task PerfilAtendimentoDeveAbrirModulosComOProprioToken()
    {
        await EntrarComPerfilAtendimentoAsync(UsuariosAtendimento.SoAtendimento);

        await AbrirModulosPeloMenuAsync();
    }

    [Test]
    public async Task PerfilAtendimentoComSenhaErradaNaoDeveEntrar()
    {
        var login = new LoginPage(Page);
        await login.AbrirAsync();

        await login.PreencherEEntrarAsync(UsuariosAtendimento.SoAtendimento, "senha-errada-e2e");

        await new SweetAlert(Page).EsperarAsync("Aviso");
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
        Assert.That(await login.LerSessaoAsync(), Has.Property(nameof(SessaoApp.Token)).Null);
    }

    /// <summary>
    /// Quem tem o perfil de atendimento e também uma empresa entra direto no atendimento e passa para a empresa pelo
    /// botão da fábrica (AlterarEmpresa.vue → TrocarEmpresa → /auth/home). O vínculo sem empresa não aparece na lista.
    /// </summary>
    [Test]
    public async Task UsuarioMistoDeveEntrarNoAtendimentoEPoderTrocarParaAEmpresa()
    {
        await EntrarComPerfilAtendimentoAsync(UsuariosAtendimento.Misto);
        Assert.That((await new LoginPage(Page).LerSessaoAsync()).Atendimento, Is.True);

        await Page.Locator(".header-company button").ClickAsync();
        var empresas = Page.Locator(".cardEmpresa .v-list-item");
        await Expect(empresas).ToHaveCountAsync(1);
        await Expect(empresas).ToContainTextAsync(UsuariosAtendimento.EmpresaDoMisto);

        await empresas.ClickAsync();
        await Page.WaitForURLAsync(new Regex("/home$"));
        await Expect(Page.Locator(".header-company")).ToContainTextAsync(UsuariosAtendimento.EmpresaDoMisto);

        var sessao = await new LoginPage(Page).LerSessaoAsync();
        Assert.Multiple(() =>
        {
            Assert.That(sessao.Atendimento, Is.False);
            Assert.That(sessao.EmpresaNome, Is.EqualTo(UsuariosAtendimento.EmpresaDoMisto));
        });

        // Fora do atendimento, o item volta ao menu do avatar para ele entrar de novo.
        await AbrirMenuUsuarioAsync();
        await Expect(ItemAtendimento).ToBeVisibleAsync();
    }

    /// <summary>Login pela tela: o perfil de atendimento não passa pela escolha de empresa.</summary>
    private async Task EntrarComPerfilAtendimentoAsync(string email)
    {
        var login = new LoginPage(Page);
        await login.AbrirAsync();
        await login.PreencherEEntrarAsync(email, UsuariosAtendimento.Senha);

        await Expect(Page).ToHaveURLAsync(new Regex("/home$"), new() { Timeout = 30_000 });
        await Expect(login.BotaoMenu).ToBeVisibleAsync();
        await Expect(login.CardsEmpresa).ToHaveCountAsync(0);
    }
}
