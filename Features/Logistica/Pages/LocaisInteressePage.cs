using System.Globalization;
using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Data;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>Tela Locais de interesse (views/logistica/enderecoLogistica).</summary>
public sealed class LocaisInteressePage(IPage page) : LogisticaListPage(page, Url, "Locais de interesse")
{
    public const string Url = "/logistica/enderecoLogistica";

    /// <summary>Preenche coordenadas manualmente, sem busca de CEP/CNPJ em serviço externo.</summary>
    public async Task CriarAsync(string nome, PontoGeografico ponto)
    {
        var dialogo = await AdicionarAsync("Criar Endereço");
        await Shared.Components.Vuetify.Campo(dialogo, "Nome do endereço").FillAsync(nome);
        await Shared.Components.Vuetify.Campo(dialogo, "Logradouro").FillAsync(ponto.Logradouro);
        await Shared.Components.Vuetify.Campo(dialogo, "Latitude").FillAsync(ponto.Latitude.ToString(CultureInfo.InvariantCulture));
        await Shared.Components.Vuetify.Campo(dialogo, "Longitude").FillAsync(ponto.Longitude.ToString(CultureInfo.InvariantCulture));
        await SalvarCadastroAsync();
    }
}
