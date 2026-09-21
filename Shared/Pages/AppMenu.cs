using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Shared.Pages;

/// <summary>
/// Menu lateral da aplicação (src/components/shared/PageBase.vue).
/// </summary>
public sealed class AppMenu(IPage page)
{
    private ILocator DrawerAberto => page.Locator(".v-navigation-drawer--active .navDrawerMenu");

    public async Task AbrirAsync()
    {
        if (await DrawerAberto.CountAsync() > 0)
        {
            return;
        }

        await page.Locator(".v-app-bar-nav-icon").ClickAsync();
        await Expect(DrawerAberto).ToBeVisibleAsync();
    }

    public ILocator Grupo(string titulo) =>
        DrawerAberto.Locator(".v-list-group__header", new() { HasTextRegex = TextoExato(titulo) }).First;

    /// <summary>Item de menu dentro do grupo informado (os itens são links &lt;a href&gt;).</summary>
    public ILocator Item(string grupo, string item) =>
        Conteiner(grupo)
            .Locator("a.v-list-item", new() { HasTextRegex = TextoExato(item) })
            .First;

    public async Task ExpandirGrupoAsync(string grupo)
    {
        await AbrirAsync();
        var cabecalho = Grupo(grupo);
        await Expect(cabecalho).ToBeVisibleAsync();

        // O cabeçalho do Vuetify não tem aria-expanded: aberto é a classe v-list-group--open do grupo. Clicar num grupo
        // que já está aberto (o drawer lembra o estado ao fechar e reabrir) o fecharia.
        var conteiner = Conteiner(grupo);
        if (!(await conteiner.GetAttributeAsync("class") ?? "").Split(' ').Contains("v-list-group--open"))
        {
            await cabecalho.ClickAsync();
        }

        await Expect(conteiner).ToHaveClassAsync(GrupoAberto());
    }

    /// <summary>Abre o menu, expande o grupo e navega para o item (o link recarrega a página).</summary>
    public async Task NavegarAsync(string grupo, string item, string urlEsperada)
    {
        await ExpandirGrupoAsync(grupo);
        var link = Item(grupo, item);
        await Expect(link).ToBeVisibleAsync();
        await link.ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(Regex.Escape(urlEsperada) + "$", RegexOptions.IgnoreCase));
    }

    private ILocator Conteiner(string grupo) =>
        Grupo(grupo).Locator("xpath=ancestor::div[contains(concat(' ', normalize-space(@class), ' '), ' v-list-group ')][1]");

    private static Regex GrupoAberto() => new(@"(^|\s)v-list-group--open(\s|$)");

    private static Regex TextoExato(string texto) =>
        new($"^\\s*{Regex.Escape(texto)}\\s*$", RegexOptions.IgnoreCase);
}
