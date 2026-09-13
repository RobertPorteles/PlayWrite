using Microsoft.Playwright;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>Tela Centro de custo (views/logistica/centroCusto).</summary>
public sealed class CentroCustoPage(IPage page) : LogisticaListPage(page, Url, "Centro de Custo")
{
    public const string Url = "/logistica/centroCusto";

    public async Task CriarAsync(string nome, string cliente)
    {
        var dialogo = await AdicionarAsync("Criar Centro de Custo");
        await Shared.Components.Vuetify.Campo(dialogo, "Nome").FillAsync(nome);
        await Vuetify.SelecionarAutocompleteAsync(dialogo, "Cliente", cliente);
        await SalvarCadastroAsync();
    }
}
