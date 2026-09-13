using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Shared.Components;

/// <summary>
/// Alertas SweetAlert2 usados pelo front (AlertService.ts).
/// </summary>
public sealed class SweetAlert(IPage page)
{
    public ILocator Popup => page.Locator(".swal2-popup");

    public ILocator Titulo => Popup.Locator(".swal2-title");

    public ILocator Texto => Popup.Locator(".swal2-html-container");

    public async Task EsperarAsync(string titulo, string? texto = null)
    {
        await Expect(Titulo).ToContainTextAsync(titulo);
        if (texto is not null)
        {
            await Expect(Texto).ToContainTextAsync(texto);
        }
    }

    /// <summary>Alerta de erro ou de validação (ícone error/warning).</summary>
    public async Task<bool> EhErroOuAvisoAsync()
    {
        var classes = await Popup.GetAttributeAsync("class") ?? "";
        return classes.Contains("swal2-icon-error") || classes.Contains("swal2-icon-warning");
    }

    /// <summary>Clica no botão de confirmação ("OK"/"Sim") e espera o alerta fechar.</summary>
    public async Task ConfirmarAsync()
    {
        await Popup.Locator(".swal2-confirm").ClickAsync();
        await Expect(page.Locator(".swal2-container")).ToHaveCountAsync(0);
    }

    /// <summary>Clica no botão de confirmação sem esperar fechar (quando outro alerta abre em seguida).</summary>
    public Task ConfirmarSemEsperarAsync() => Popup.Locator(".swal2-confirm").ClickAsync();

    public async Task NegarAsync()
    {
        await Popup.Locator(".swal2-deny").ClickAsync();
        await Expect(page.Locator(".swal2-container")).ToHaveCountAsync(0);
    }
}
