using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Features.Equipamento.Stub;
using PlaywrightTests.Shared;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using PlaywrightTests.Shared.Rede;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Atendimento;

/// <summary>
/// Base dos testes de tela do modo atendimento: menu lateral só com o que não é foraAtendimento(), essencialmente o
/// grupo "Atendimento" (Módulos, Rotas, Chip...). /equipamento é servido pelo <see cref="ApiEquipamentoStub"/>: a base
/// local não tem associados e a busca de veículo sairia para o concentrador. A rede do navegador é conferida no fim.
/// </summary>
public abstract class AtendimentoTestBase : E2ETestBase
{
    public const string Categoria = "Atendimento";

    protected const string GrupoAtendimento = "Atendimento";

    /// <summary>Grupo cujos itens são todos foraAtendimento*(): some no modo atendimento. ("Operação em Tempo Real"
    /// não serve: Monitoramento Conecta só esconde no modo ocorrência.)</summary>
    private const string GrupoForaDoAtendimento = "Planejamento";

    private ApiEquipamentoStub _stub = null!;

    protected MonitorRede Rede { get; private set; } = null!;

    protected AppMenu Menu { get; private set; } = null!;

    [SetUp]
    public async Task PrepararAtendimentoAsync()
    {
        Rede = new MonitorRede(Context, new Uri(E2ESettings.ApiUrl), new Uri(E2ESettings.FrontUrl));
        _stub = await ApiEquipamentoStub.InstalarAsync(Page);
        Menu = new AppMenu(Page);
    }

    [TearDown]
    public void ConferirRede()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Rede.Violacoes(), Is.Empty, "O navegador chamou dados fora da CCO.Api local.");
            Assert.That(_stub.Legadas, Is.Empty, "Chamadas legadas não previstas (iriam ao concentrador pela API).");
        });
    }

    /// <summary>Item "Atendimento" (mdi-phone) do menu do avatar.</summary>
    protected ILocator ItemAtendimento =>
        Page.Locator(".menuList .v-list-item", new() { HasTextRegex = new Regex(@"^\s*Atendimento\s*$") });

    protected async Task AbrirMenuUsuarioAsync()
    {
        await Page.Locator("button:has(.v-avatar.avatar)").First.ClickAsync();
        await Expect(Page.Locator(".menuList")).ToBeVisibleAsync();
    }

    /// <summary>Menu lateral restrito ao atendimento e item "Atendimento" fora do menu do avatar.</summary>
    protected async Task ConferirMenuDeAtendimentoAsync()
    {
        await Menu.ExpandirGrupoAsync(GrupoAtendimento);
        await Expect(Menu.Item(GrupoAtendimento, "Módulos")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Expect(Menu.Grupo(GrupoForaDoAtendimento))
            .ToHaveCountAsync(0, new() { Timeout = 5_000 });

        // O drawer é temporário: o scrim cobre a barra superior até ser fechado.
        await Page.Locator(".v-navigation-drawer__scrim").ClickAsync();
        await AbrirMenuUsuarioAsync();
        await Expect(ItemAtendimento).ToHaveCountAsync(0, new() { Timeout = 5_000 });
        await Page.Keyboard.PressAsync("Escape");
    }

    /// <summary>
    /// Botão da fábrica (AlterarEmpresa.vue) → a única empresa da lista. A troca abre /auth/home?token=... numa nova
    /// navegação e o Auth.vue pede o RetrieveSessionApp antes de voltar para a /home. Esperar só a URL /home não basta:
    /// ela já é a URL antes da troca, e ler a sessão no meio da navegação dá "Execution context was destroyed".
    /// </summary>
    protected async Task TrocarDeEmpresaAsync(string empresa)
    {
        await Page.Locator(".header-company button").ClickAsync();
        var empresas = Page.Locator(".cardEmpresa .v-list-item");
        await Expect(empresas).ToHaveCountAsync(1);
        await Expect(empresas).ToContainTextAsync(empresa);

        await Page.RunAndWaitForResponseAsync(() => empresas.ClickAsync(), r => r.Url.Contains("/RetrieveSessionApp/"));
        await Page.WaitForURLAsync(new Regex("/home$"));
        await Expect(Page.Locator(".header-company")).ToContainTextAsync(empresa);
    }

    /// <summary>Atendimento → Módulos: a lista de /equipamento vai com o token da sessão atual.</summary>
    protected async Task AbrirModulosPeloMenuAsync()
    {
        var token = (await new LoginPage(Page).LerSessaoAsync()).Token;

        var lista = await Page.RunAndWaitForRequestAsync(
            () => Menu.NavegarAsync(GrupoAtendimento, "Módulos", "/equipamento"),
            ApiEquipamentoStub.EhLista);

        Assert.That(await lista.HeaderValueAsync("authorization"), Is.EqualTo($"Bearer {token}"));
        await Expect(Page.Locator("tr", new() { HasText = ApiEquipamentoStub.Modulos[0].Serial })).ToBeVisibleAsync();
    }
}
