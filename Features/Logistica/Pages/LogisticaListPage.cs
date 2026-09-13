using Microsoft.Playwright;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>
/// Padrão das telas de lista da logística: ListHeader ("Pesquisa rápida", "Adicionar"), tabela #tabela,
/// menu "Ações" por linha e diálogo CadastroBase ("Criar {entidade}", botão "Salvar").
/// </summary>
public class LogisticaListPage(IPage page, string rota, string titulo)
{
    protected IPage Page { get; } = page;

    protected Vuetify Vuetify { get; } = new(page);

    protected SweetAlert Alerta { get; } = new(page);

    public string Rota { get; } = rota;

    public string Titulo { get; } = titulo;

    public ILocator Tabela => Page.Locator("#tabela");

    public ILocator CampoPesquisa => Page.GetByLabel("Pesquisa rápida", new() { Exact = true });

    public ILocator BotaoAdicionar => Page.GetByRole(AriaRole.Button, new() { Name = "Adicionar", Exact = true });

    /// <summary>Diálogo de cadastro aberto (fullscreen do CadastroBase ou wizard).</summary>
    public ILocator Dialogo => Page.Locator(".v-overlay--active.v-dialog").Last;

    public async Task AbrirAsync()
    {
        await Page.GotoAsync(Rota);
        await EsperarCarregadaAsync();
    }

    public async Task EsperarCarregadaAsync()
    {
        await Expect(Page.GetByText(Titulo, new() { Exact = true }).First).ToBeVisibleAsync();
        await Expect(Tabela).ToBeVisibleAsync();
        await EsperarTabelaCarregarAsync();
    }

    public Task EsperarTabelaCarregarAsync() =>
        Expect(Tabela.Locator(".v-data-table-progress")).ToHaveCountAsync(0, new() { Timeout = 20_000 });

    public ILocator Linha(string texto) => Tabela.Locator("tbody tr", new() { HasText = texto });

    /// <summary>Usa a "Pesquisa rápida" e exige que a API responda com sucesso à consulta filtrada.</summary>
    public async Task<ILocator> PesquisarAsync(string texto)
    {
        var resposta = await PesquisarERetornarRespostaAsync(texto);
        Assert.That(resposta.Status, Is.EqualTo(200),
            $"A pesquisa rápida de '{Titulo}' retornou HTTP {resposta.Status} para a consulta {Uri.UnescapeDataString(resposta.Url)}");

        var linha = Linha(texto).First;
        await Expect(linha).ToBeVisibleAsync(new() { Timeout = 20_000 });
        return linha;
    }

    public Task<IResponse> PesquisarERetornarRespostaAsync(string texto) =>
        Page.RunAndWaitForResponseAsync(
            () => CampoPesquisa.FillAsync(texto),
            r => r.Request.Method == "GET" && Uri.UnescapeDataString(r.Url).Contains("$filter", StringComparison.OrdinalIgnoreCase),
            new() { Timeout = 20_000 });

    /// <summary>
    /// Localiza a linha sem a pesquisa rápida: mostra 50 itens por página e percorre as páginas até achar o texto.
    /// </summary>
    public async Task<ILocator> LocalizarLinhaAsync(string texto, int maximoPaginas = 20)
    {
        await EsperarTabelaCarregarAsync();
        await MostrarItensPorPaginaAsync(50);

        for (var pagina = 1; pagina <= maximoPaginas; pagina++)
        {
            var linha = Linha(texto).First;
            if (await linha.CountAsync() > 0)
            {
                await Expect(linha).ToBeVisibleAsync();
                return linha;
            }

            var proxima = BotaoProximaPagina;
            if (await proxima.CountAsync() == 0 || await proxima.IsDisabledAsync())
            {
                break;
            }

            await Page.RunAndWaitForResponseAsync(() => proxima.ClickAsync(), EhConsultaDaLista, new() { Timeout = 20_000 });
            await EsperarTabelaCarregarAsync();
        }

        throw new InvalidOperationException($"Nenhuma linha com '{texto}' foi encontrada na tela {Titulo}.");
    }

    private ILocator RodapeTabela => Tabela.Locator(".v-data-table-footer");

    private ILocator BotaoProximaPagina => RodapeTabela.Locator(".v-data-table-footer__pagination button").Nth(2);

    private async Task MostrarItensPorPaginaAsync(int quantidade)
    {
        var seletor = RodapeTabela.Locator(".v-data-table-footer__items-per-page .v-field");
        if ((await seletor.InnerTextAsync()).Trim() == quantidade.ToString())
        {
            return;
        }

        await seletor.ClickAsync();
        await Page.RunAndWaitForResponseAsync(
            () => Vuetify.ClicarItemMenuAsync(quantidade.ToString()),
            EhConsultaDaLista,
            new() { Timeout = 20_000 });
        await EsperarTabelaCarregarAsync();
    }

    private static bool EhConsultaDaLista(IResponse resposta) =>
        resposta.Request.Method == "GET" && Uri.UnescapeDataString(resposta.Url).Contains("$Top=", StringComparison.OrdinalIgnoreCase);

    /// <summary>Célula da linha na coluna com o cabeçalho informado.</summary>
    public async Task<ILocator> CelulaAsync(ILocator linha, string coluna)
    {
        var cabecalhos = (await Tabela.Locator("thead th").AllInnerTextsAsync()).Select(c => c.Trim()).ToList();
        var indice = cabecalhos.FindIndex(c => string.Equals(c, coluna, StringComparison.OrdinalIgnoreCase));
        return indice >= 0
            ? linha.Locator("td").Nth(indice)
            : throw new InvalidOperationException($"Coluna '{coluna}' não encontrada. Colunas: {string.Join(" | ", cabecalhos)}");
    }

    public async Task AbrirAcaoAsync(ILocator linha, string acao)
    {
        await linha.GetByRole(AriaRole.Button, new() { Name = "Ações" }).ClickAsync();
        await Vuetify.ClicarItemMenuAsync(acao);
    }

    public async Task<ILocator> AdicionarAsync(string tituloDialogo)
    {
        await BotaoAdicionar.ClickAsync();
        await Expect(Dialogo.GetByText(tituloDialogo, new() { Exact = true })).ToBeVisibleAsync();
        return Dialogo;
    }

    /// <summary>Clica em "Salvar" do CadastroBase e confirma o alerta "Criado com sucesso!".</summary>
    public async Task SalvarCadastroAsync()
    {
        var salvar = Dialogo.Locator(".save-button");
        await Expect(salvar).ToBeEnabledAsync();
        await salvar.ClickAsync();
        await Alerta.EsperarAsync("Aviso", "Criado com sucesso!");
        await Alerta.ConfirmarAsync();
    }

    /// <summary>Exclui a linha pelo menu "Ações" › "Excluir" e confirma os dois alertas.</summary>
    public async Task ExcluirAsync(ILocator linha)
    {
        await AbrirAcaoAsync(linha, "Excluir");
        await Alerta.EsperarAsync("Atenção", "Tem certeza que deseja excluir");
        await Alerta.ConfirmarSemEsperarAsync();
        await Alerta.EsperarAsync("Aviso", "sucesso");
        await Alerta.ConfirmarAsync();
    }
}
