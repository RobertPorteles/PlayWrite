using Microsoft.Playwright;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>Tela Cliente (views/logistica/cliente).</summary>
public sealed class ClientePage(IPage page) : LogisticaListPage(page, Url, "Cliente")
{
    public const string Url = "/logistica/cliente";

    public async Task CriarPessoaJuridicaAsync(string razaoSocial, string cnpj)
    {
        var dialogo = await AdicionarAsync("Criar Cliente");
        await dialogo.GetByLabel("Pessoa Juridica", new() { Exact = true }).CheckAsync();
        await Shared.Components.Vuetify.Campo(dialogo, "CNPJ").FillAsync(cnpj);
        await Shared.Components.Vuetify.Campo(dialogo, "Razão Social").FillAsync(razaoSocial);
        await Shared.Components.Vuetify.Campo(dialogo, "Nome Fantasia").FillAsync(razaoSocial);
        await SalvarCadastroAsync();
    }
}
