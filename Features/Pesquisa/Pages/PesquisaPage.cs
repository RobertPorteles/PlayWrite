using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Pesquisa.Pages;

/// <summary>
/// Qualquer tela com o campo "Pesquisa rápida" (ListHeader, TextSearch ou campo próprio). Nem toda tabela tem
/// id="tabela" e telas com abas mantêm campos ocultos no DOM: vale o campo visível e a tabela que vem depois dele.
/// </summary>
public sealed partial class PesquisaPage(IPage page)
{
    public ILocator Campo => page.GetByLabel(RotuloPesquisa()).Filter(new() { Visible = true }).First;

    /// <summary>A primeira tabela depois do campo (há telas com mais de uma tabela, como /manutencaoGerenciada).</summary>
    public ILocator Tabela =>
        Campo.Locator("xpath=following::div[contains(concat(' ', normalize-space(@class), ' '), ' v-data-table ')][1]");

    /// <summary>Linhas de dados (sem a linha de "carregando" nem a de "nenhum dado").</summary>
    public ILocator Linhas =>
        Tabela.Locator("tbody tr:not(.v-data-table-rows-no-data):not(.v-data-table-rows-loading)");

    public ILocator SemDados => Tabela.Locator(".v-data-table-rows-no-data");

    public ILocator Cards => page.Locator(".v-main .card-wrapper").Filter(new() { Visible = true });

    public async Task AbrirAsync(string rota, string? titulo, string? aba)
    {
        await page.GotoAsync(rota);
        if (titulo is not null)
        {
            await Expect(page.GetByText(titulo, new() { Exact = true }).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        }

        if (aba is not null)
        {
            await page.GetByRole(AriaRole.Tab, new() { Name = aba, Exact = true }).ClickAsync();
        }

        await Expect(Campo).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    public async Task EsperarTabelaAsync()
    {
        await Expect(Tabela).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Expect(Tabela.Locator(".v-data-table-progress")).ToHaveCountAsync(0, new() { Timeout = 20_000 });
    }

    [GeneratedRegex("^Pesquisa rápida$", RegexOptions.IgnoreCase)]
    private static partial Regex RotuloPesquisa();
}
