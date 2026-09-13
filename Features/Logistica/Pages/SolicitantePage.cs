using Microsoft.Playwright;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>Tela Solicitante (views/logistica/solicitante).</summary>
public sealed class SolicitantePage(IPage page) : LogisticaListPage(page, Url, "Solicitante")
{
    public const string Url = "/logistica/solicitante";

    public async Task CriarAsync(string nome, string email, string cliente)
    {
        var dialogo = await AdicionarAsync("Criar Solicitante");
        await Shared.Components.Vuetify.Campo(dialogo, "Nome").FillAsync(nome);
        await Shared.Components.Vuetify.Campo(dialogo, "E-mail").FillAsync(email);
        await Vuetify.SelecionarAutocompleteAsync(dialogo, "Cliente", cliente);
        await SalvarCadastroAsync();
    }
}
