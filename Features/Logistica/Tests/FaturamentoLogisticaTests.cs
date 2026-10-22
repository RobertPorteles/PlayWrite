using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Fluxo do faturamento de logística pela tela, sobre uma OS realizada criada pela API. A fatura gerada fica no banco
/// (não tem DELETE); ela é cancelada no fim para a OS voltar a ficar livre.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class FaturamentoLogisticaTests : LogisticaTestBase
{
    private const string Faturamento = "v1/Logistica/Faturamento";

    [Test]
    public async Task MedicaoGeraEEmiteAFaturaDaOsRealizada()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var os = await Logistica.CriarOrdemServicoFaturavelAsync(apoio, valorRota: 782.12m);
        var dia = LogisticaDataFactory.DataViagem;

        await LoginNaUiAsync();
        await Page.GotoAsync("/logistica/faturamento/medicao");
        await Page.GetByLabel("Data inicial").FillAsync(LogisticaDataFactory.FormatarData(dia.AddDays(-1)));
        await Page.GetByLabel("Data final").FillAsync(LogisticaDataFactory.FormatarData(dia.AddDays(1)));
        var cliente = Page.GetByRole(AriaRole.Combobox, new() { Name = "Cliente", Exact = true });
        await cliente.ClickAsync();
        await cliente.FillAsync(apoio.NomeCliente);
        await Page.GetByRole(AriaRole.Option, new() { Name = apoio.NomeCliente }).ClickAsync();

        await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Buscar OS" }).ClickAsync(),
            r => r.Url.Contains("/Faturamento/Medicao") && r.Status == 200);
        var linha = Page.Locator("tbody tr", new() { HasText = $"#{os.OrdemServicoId}" });
        await Expect(linha).ToContainTextAsync("R$ 782,12");
        await linha.Locator("input[type=checkbox]").CheckAsync();
        await Expect(Page.GetByText("1 OS selecionada(s) · Total R$ 782,12")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Gerar fatura" }).ClickAsync();
        var alerta = new SweetAlert(Page);
        await alerta.EsperarAsync("Gerar fatura", apoio.NomeCliente);
        var geracao = await Page.RunAndWaitForResponseAsync(
            () => alerta.ConfirmarAsync(),
            r => r.Request.Method == "POST" && r.Url.EndsWith("/Faturamento/Fatura"));
        Assert.That(geracao.Status, Is.EqualTo(200), await geracao.TextAsync());
        var fatura = (await geracao.JsonAsync())!.Value.GetProperty("id").GetInt64();

        await Expect(Page).ToHaveURLAsync(new Regex($"/logistica/faturamento/faturas/{fatura}$"));
        await Expect(Page.GetByText("Aberta", new() { Exact = true }).First).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Emitir" }).ClickAsync();
        await alerta.EsperarAsync("Atenção", $"Emitir a fatura #{fatura}?");
        await Page.RunAndWaitForResponseAsync(
            () => alerta.ConfirmarAsync(),
            r => r.Request.Method == "PATCH" && r.Url.EndsWith($"/Fatura/{fatura}/Emitir") && r.Status == 200);

        await Expect(Page.GetByText("Emitida", new() { Exact = true }).First).ToBeVisibleAsync();
        await Expect(Page.GetByText(new Regex("^Emitida em "))).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Marcar como enviada" })).ToBeVisibleAsync();

        await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/Cancelar",
            new System.Text.Json.Nodes.JsonObject { ["motivo"] = "Limpeza do teste E2E" });
    }
}
