using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Shared.Components;

/// <summary>
/// Interações com componentes Vuetify 3 (v-select, v-autocomplete, menus).
/// </summary>
public sealed class Vuetify(IPage page)
{
    /// <summary>Itens do menu (v-select/v-autocomplete/v-menu) aberto no momento.</summary>
    public ILocator OpcoesMenuAberto => page.Locator(".v-overlay--active.v-menu .v-list-item");

    /// <summary>Somente itens selecionáveis; o item "sem dados" do autocomplete não tem a classe --link.</summary>
    private ILocator OpcoesSelecionaveis => page.Locator(".v-overlay--active.v-menu .v-list-item--link:not(.v-list-item--disabled)");

    private ILocator MenuAberto => page.Locator(".v-overlay--active.v-menu");

    public static ILocator Campo(ILocator escopo, string label) =>
        escopo.GetByLabel(label, new() { Exact = true }).First;

    /// <summary>
    /// Container clicável do campo (div.v-field, role=combobox). No Vuetify 3.12 o &lt;input&gt; do v-select tem
    /// tamanho mínimo e não recebe clique.
    /// </summary>
    public static ILocator Container(ILocator escopo, string label) =>
        Campo(escopo, label).Locator("xpath=ancestor::div[contains(concat(' ', normalize-space(@class), ' '), ' v-field ')][1]");

    /// <summary>Digita no v-autocomplete, escolhe a opção que contém o texto e confere a seleção no campo.</summary>
    public async Task SelecionarAutocompleteAsync(ILocator escopo, string label, string texto)
    {
        var campo = Campo(escopo, label);
        await campo.ClickAsync();
        await campo.FillAsync(texto);

        await SelecionarOpcaoAsync(campo, OpcoesSelecionaveis.Filter(new() { HasText = texto }).First, texto);
        await Expect(Container(escopo, label)).ToContainTextAsync(texto);
    }

    /// <summary>Abre o v-select e clica na opção com o texto exato.</summary>
    public async Task SelecionarAsync(ILocator escopo, string label, string texto)
    {
        await Container(escopo, label).ClickAsync();
        await ClicarOpcaoAsync(OpcoesMenuAberto.Filter(new() { HasTextRegex = TextoExato(texto) }).First);
    }

    /// <summary>Abre o campo e escolhe a primeira opção disponível. Devolve o texto escolhido, ou null se não houver opções.</summary>
    public async Task<string?> SelecionarPrimeiraOpcaoAsync(ILocator escopo, string label)
    {
        var campo = Campo(escopo, label);
        await campo.ClickAsync();

        var primeira = OpcoesSelecionaveis.First;
        try
        {
            await Expect(primeira).ToBeVisibleAsync(new() { Timeout = 5_000 });
        }
        catch (PlaywrightException)
        {
            await page.Keyboard.PressAsync("Escape");
            return null;
        }

        var texto = (await primeira.InnerTextAsync()).Trim();
        await SelecionarOpcaoAsync(campo, primeira, textoParaRedigitar: null);
        await Expect(Container(escopo, label)).ToContainTextAsync(texto);
        return texto;
    }

    /// <summary>Clica numa ação de menu (ex.: "Editar", "Excluir") já aberto.</summary>
    public Task ClicarItemMenuAsync(string texto) =>
        ClicarOpcaoAsync(OpcoesMenuAberto.Filter(new() { HasTextRegex = TextoExato(texto) }).First);

    public static Regex TextoExato(string texto) =>
        new($"^\\s*{Regex.Escape(texto)}\\s*$", RegexOptions.IgnoreCase);

    /// <summary>
    /// Clica na opção. Se o menu fica sendo re-renderizado e o item nunca estabiliza para o clique (ocorre no wizard de
    /// Programação depois de preencher datas e origem), seleciona pelo teclado (↓ + Enter), como um usuário também faria.
    /// </summary>
    private async Task SelecionarOpcaoAsync(ILocator campo, ILocator opcao, string? textoParaRedigitar)
    {
        try
        {
            await opcao.ClickAsync(new() { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            await campo.ClickAsync();
            if (textoParaRedigitar is not null)
            {
                await campo.FillAsync(textoParaRedigitar);
            }

            try
            {
                await Expect(opcao).ToBeVisibleAsync(new() { Timeout = 3_000 });
            }
            catch (PlaywrightException)
            {
                // O menu pode estar fechando/reabrindo; o teclado ainda seleciona o primeiro item filtrado.
            }

            await page.Keyboard.PressAsync("ArrowDown");
            await page.Keyboard.PressAsync("Enter");
        }

        await Expect(MenuAberto).ToHaveCountAsync(0);
    }

    private async Task ClicarOpcaoAsync(ILocator opcao)
    {
        await Expect(opcao).ToBeVisibleAsync();
        await opcao.ClickAsync();
        await Expect(MenuAberto).ToHaveCountAsync(0);
    }
}
