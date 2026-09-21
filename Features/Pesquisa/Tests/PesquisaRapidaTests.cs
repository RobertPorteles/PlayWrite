using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Features.Pesquisa.Data;
using PlaywrightTests.Features.Pesquisa.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Pesquisa.Tests;

/// <summary>
/// "Pesquisa rápida" de todas as telas do front (fora a logística, coberta em PesquisaRapidaLogisticaTests):
/// um termo existente deixa só as linhas que o contêm e um termo inexistente esvazia a tabela, sem erro da API ou de
/// JavaScript, sem alerta e sem chamada legada saindo da máquina.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class PesquisaRapidaTests : PesquisaTestBase
{
    private const string Inexistente = "e2e-pesq-inexistente-9f3k";

    public static IEnumerable<TestCaseData> Tabelas() =>
        TelasPesquisa.Tabelas().Select(t => new TestCaseData(t).SetArgDisplayNames(t.Rota));

    public static IEnumerable<TestCaseData> Cards() =>
        TelasPesquisa.Cards().Select(r => new TestCaseData(r).SetArgDisplayNames(r));

    [TestCaseSource(nameof(Tabelas))]
    public async Task PesquisaRapida_FiltraATabela(TelaPesquisa tela)
    {
        var termo = await TermoAsync(tela);
        tela.Simular?.Invoke(Stub);

        await EntrarAsync();
        var pagina = new PesquisaPage(Page);
        await pagina.AbrirAsync(tela.Rota, tela.Titulo, tela.Aba);
        await pagina.EsperarTabelaAsync();

        if (termo is not null)
        {
            await VerificarAsync(tela.Rota, tela.Defeito, $"pesquisando '{termo}', a tabela não ficou só com as linhas que o contêm", async () =>
            {
                await pagina.Campo.FillAsync(termo);
                await Expect(pagina.Linhas.Filter(new() { HasText = termo }).First).ToBeVisibleAsync(new() { Timeout = 20_000 });
                await Expect(pagina.Linhas.Filter(new() { HasNotText = termo })).ToHaveCountAsync(0);
            });
        }

        await VerificarAsync(tela.Rota, tela.Defeito, $"pesquisando '{Inexistente}', a tabela não ficou vazia", async () =>
        {
            await pagina.Campo.FillAsync(Inexistente);
            await Expect(pagina.SemDados).ToBeVisibleAsync(new() { Timeout = 20_000 });
        });

        await ConferirSemErrosAsync(tela.Rota, tela.Defeito);
        if (termo is null)
        {
            Assert.Ignore($"{tela.Rota}: {tela.Recurso} não tem registros e o teste não sabe criar um; só o termo inexistente foi conferido.");
        }
    }

    [TestCaseSource(nameof(Cards))]
    public async Task PesquisaRapida_FiltraOsCards(string rota)
    {
        await EntrarAsync();
        var pagina = new PesquisaPage(Page);
        await pagina.AbrirAsync(rota, null, null);
        await Expect(pagina.Cards.First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var titulos = (await pagina.Cards.Locator(".v-card-title").AllInnerTextsAsync()).Select(t => t.Trim()).ToList();
        if (titulos.Count < 2)
        {
            Assert.Ignore($"{rota}: o usuário vê {titulos.Count} card(s); são precisos dois para conferir o filtro.");
        }

        var alvo = titulos[^1];
        await pagina.Campo.FillAsync(alvo);
        await Expect(pagina.Cards.Filter(new() { HasText = alvo }).First).ToBeVisibleAsync();
        await Expect(pagina.Cards.Filter(new() { HasNotText = alvo })).ToHaveCountAsync(0);

        await pagina.Campo.FillAsync(Inexistente);
        await Expect(pagina.Cards).ToHaveCountAsync(0);

        await ConferirSemErrosAsync(rota, null);
    }

    /// <summary>
    /// /chipDiscrepancia filtra no navegador a lista da Algar (externa, simulada aqui) pelo TextSearch, que dispara
    /// no "change" (Enter/blur).
    /// </summary>
    [Test]
    public async Task PesquisaRapida_ChipDiscrepancia()
    {
        Stub.Responder("/v1/chip/DiscrepanciaAlgar", _ => new JsonArray(
            new[] { TelasPesquisa.TermoSimulado, "E2E-PESQ-B" }.Select(n => (JsonNode)new JsonObject
            {
                ["chipSerial"] = n,
                ["chip"] = new JsonObject
                {
                    ["iccid"] = n, ["imei"] = n, ["imsi"] = n, ["operadora"] = "E2E", ["ultimaAPNConectada"] = "e2e",
                    ["status"] = new JsonObject { ["nome"] = "Ativo" },
                },
            }).ToArray()));

        await EntrarAsync();
        var pagina = new PesquisaPage(Page);
        await pagina.AbrirAsync("/chipDiscrepancia", null, null);
        await Expect(pagina.Linhas).ToHaveCountAsync(2, new() { Timeout = 20_000 });

        await VerificarAsync("/chipDiscrepancia", null, $"pesquisando '{TelasPesquisa.TermoSimulado}', a outra linha continuou na tabela", async () =>
        {
            await pagina.Campo.FillAsync(TelasPesquisa.TermoSimulado);
            await pagina.Campo.PressAsync("Enter");
            await Expect(pagina.Linhas.Filter(new() { HasNotText = TelasPesquisa.TermoSimulado })).ToHaveCountAsync(0);
        });

        await ConferirSemErrosAsync("/chipDiscrepancia", null);
    }

    /// <summary>
    /// /moduloClienteChip manda a pesquisa no corpo do POST moduloClienteChip/obterdados. Tudo o que a tela lê do
    /// Concentrador (externo) é simulado, e o teste confere o que o front enviou.
    /// </summary>
    [Test]
    public async Task PesquisaRapida_ModuloClienteChip()
    {
        Stub.Responder("/v1/CadTipoModulo", _ => new JsonArray());
        Stub.Responder("/v1/moduloClienteChip/", _ => new JsonArray());
        Stub.Responder("/v1/conectaWeb/servidor/", _ => new JsonArray());
        Stub.Responder("/v1/moduloClienteChip/obterestatisticas", _ => new JsonObject(
            new[] { "modulos", "servidores", "operadoras", "empresas", "fabricantes", "quantidades", "operadorasSemAtraso", "fabricantesSemAtraso" }
                .Select(k => KeyValuePair.Create(k, (JsonNode?)new JsonArray()))));
        Stub.Responder("/v1/moduloClienteChip/obterdados", _ => new JsonObject { ["modulos"] = new JsonArray(), ["total"] = 0 });

        await EntrarAsync();
        var pagina = new PesquisaPage(Page);
        await pagina.AbrirAsync("/moduloClienteChip", null, null);

        await VerificarAsync("/moduloClienteChip", null, $"pesquisando '{TelasPesquisa.TermoSimulado}', obterdados não recebeu o termo", async () =>
        {
            await Page.RunAndWaitForRequestAsync(async () =>
                {
                    await pagina.Campo.FillAsync(TelasPesquisa.TermoSimulado);
                    await pagina.Campo.PressAsync("Enter");
                },
                r => r.Url.Contains("/obterdados", StringComparison.OrdinalIgnoreCase)
                     && JsonNode.Parse(r.PostData ?? "{}")?["search"].ComoTexto() == TelasPesquisa.TermoSimulado,
                new() { Timeout = 10_000 });
        });

        await ConferirSemErrosAsync("/moduloClienteChip", null);
    }

    /// <summary>Termo a pesquisar: simulado, lido do primeiro registro da API ou semeado. Null se não houver como obter.</summary>
    private async Task<string?> TermoAsync(TelaPesquisa tela)
    {
        if (tela.Simular is not null)
        {
            return TelasPesquisa.TermoSimulado;
        }

        JsonNode? primeiro = null;
        try
        {
            primeiro = await PrimeiroAsync(tela.Recurso);
        }
        catch (ApiException ex)
        {
            Assert.Fail($"{tela.Rota}: a CCO.Api não lista {tela.Recurso} (HTTP {ex.Resposta.Status}).{Defeito(tela.Defeito)}");
        }

        var valor = primeiro?[tela.Campo].ComoTexto();
        if (!string.IsNullOrWhiteSpace(valor))
        {
            return valor;
        }

        return tela.Semear is null ? null : await tela.Semear(this);
    }

    private async Task VerificarAsync(string rota, string? defeito, string falha, Func<Task> passo)
    {
        try
        {
            await passo();
        }
        catch (PlaywrightException ex)
        {
            var alerta = new SweetAlert(Page);
            var textoAlerta = await alerta.Popup.CountAsync() > 0 ? $" Alerta na tela: '{await alerta.Popup.InnerTextAsync()}'." : "";
            Assert.Fail($"{rota}: {falha}.{Defeito(defeito)}{textoAlerta}{Listar(" Erros:", Erros)}\n{ex.Message}");
        }
    }

    private async Task ConferirSemErrosAsync(string rota, string? defeito)
    {
        var alerta = new SweetAlert(Page);
        var textoAlerta = await alerta.Popup.CountAsync() > 0 ? await alerta.Popup.InnerTextAsync() : null;
        Assert.Multiple(() =>
        {
            Assert.That(textoAlerta, Is.Null, $"{rota}: a tela mostrou um alerta durante a pesquisa.{Defeito(defeito)}");
            Assert.That(Erros, Is.Empty, $"{rota}: houve erro da CCO.Api ou de JavaScript durante a pesquisa.{Defeito(defeito)}");
            Assert.That(Stub.Legadas, Is.Empty, $"{rota}: chamadas legadas (sairiam da máquina) não previstas.");
        });
    }

    private static string Defeito(string? defeito) => defeito is null ? "" : $" Defeito conhecido: {defeito}";

    private static string Listar(string titulo, IReadOnlyList<string> itens) =>
        itens.Count == 0 ? "" : $"{titulo} {string.Join("; ", itens)}";
}
