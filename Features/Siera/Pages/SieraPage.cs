using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Siera.Pages;

/// <summary>
/// Tela /siera (src/views/siera/Lista.vue): seletor de rastreador e abas Telemetria, Alarmes e Vídeo.
/// Não há data-testid nos componentes da Siera, então os seletores usam textos e as classes .siera-*.
/// </summary>
public sealed class SieraPage(IPage page)
{
    public const string Url = "/siera";

    public const string Titulo = "Vídeo e Monitoramento";

    private const string CampoDispositivo = "Veículo / rastreador";

    public ILocator IframePlayer => page.Locator("iframe.siera-player__iframe");

    public async Task AbrirAsync()
    {
        await page.GotoAsync(Url);
        await Expect(page).ToHaveURLAsync(new Regex(Regex.Escape(Url) + "$"));
        await Expect(page.GetByText(Titulo, new() { Exact = true }).First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>Aba ou subaba pelo texto: Telemetria, Alarmes, Vídeo, Alarmes ativos, Histórico de alarmes.</summary>
    public Task AbrirAbaAsync(string nome) =>
        page.GetByRole(AriaRole.Tab, new() { Name = nome, Exact = true }).ClickAsync();

    /// <summary>Linha visível — ou seja, da aba aberta — que contém o texto.</summary>
    public ILocator Linha(string texto) => page.Locator("tbody tr:visible", new() { HasText = texto });

    public Task SelecionarDispositivoAsync(string texto) =>
        new Vuetify(page).SelecionarAutocompleteAsync(page.Locator("body"), CampoDispositivo, texto);

    /// <summary>Abre a aba Vídeo (com um rastreador selecionado) e devolve a playerUrl que a CCO.Api entregou.</summary>
    public async Task<string> AbrirVideoAsync()
    {
        var resposta = await page.RunAndWaitForResponseAsync(
            () => AbrirAbaAsync("Vídeo"),
            r => r.Url.Contains("/v1/Siera/video/player", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 20_000 });

        Assert.That(resposta.Status, Is.EqualTo(200), $"v1/Siera/video/player retornou HTTP {resposta.Status}.");
        var corpo = await resposta.JsonAsync();
        return corpo?.GetProperty("playerUrl").GetString()
            ?? throw new InvalidOperationException("v1/Siera/video/player não devolveu playerUrl.");
    }
}
