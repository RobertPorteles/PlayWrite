using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>Tela Viagem logística (views/logistica/viagemLogistica): visualizar, finalizar e cancelar itens.</summary>
public sealed class ViagemLogisticaPage(IPage page) : LogisticaListPage(page, Url, "Viagem logística")
{
    public const string Url = "/logistica/viagemLogistica";

    /// <summary>
    /// Texto da coluna "Início Viagem". Serve para reencontrar a viagem depois de finalizar ou cancelar, porque o
    /// backend reescreve a observação da viagem ao recalcular o status.
    /// </summary>
    public async Task<string> ChaveDaLinhaAsync(ILocator linha)
    {
        var chave = (await (await CelulaAsync(linha, "Início Viagem")).InnerTextAsync()).Trim();
        return chave.Length > 0 && char.IsDigit(chave[0])
            ? chave
            : throw new InvalidOperationException($"A viagem não tem 'Início Viagem' preenchido para servir de chave (valor: '{chave}').");
    }

    public async Task<ILocator> StatusDaViagemAsync(string chave) =>
        await CelulaAsync(await LocalizarLinhaAsync(chave), "Status");

    public ILocator DialogoComTitulo(string titulo) =>
        Page.Locator(".v-overlay--active.v-dialog").Filter(new() { HasText = titulo }).Last;

    public async Task VisualizarAsync(ILocator linha, string nomeItem)
    {
        await AbrirAcaoAsync(linha, "Visualizar");
        // O título do diálogo é "Detalhar" (common.details); o conteúdo é mais específico.
        var dialogo = DialogoComTitulo("Resumo da operação");
        var fechar = dialogo.GetByRole(AriaRole.Button, new() { Name = "Fechar", Exact = true });
        await Expect(fechar).ToBeVisibleAsync();
        await Expect(dialogo).ToContainTextAsync("Início previsto");
        await Expect(dialogo).ToContainTextAsync(nomeItem);

        await fechar.ClickAsync();
        await Expect(dialogo).ToBeHiddenAsync();
    }

    public async Task FinalizarAsync(ILocator linha, string nomeItem, string? observacao)
    {
        await AbrirAcaoAsync(linha, "Finalizar viagem");
        var dialogo = DialogoComTitulo("Finalizar item da viagem");
        await Expect(dialogo).ToBeVisibleAsync();

        await dialogo.GetByRole(AriaRole.Radio, new() { Name = nomeItem }).CheckAsync();
        if (observacao is not null)
        {
            await Shared.Components.Vuetify.Campo(dialogo, "Observação (opcional)").FillAsync(observacao);
        }

        await ConfirmarAsync(dialogo, "Confirmar finalização");
    }

    /// <summary>Abre o diálogo de cancelamento e seleciona o item (sem informar o motivo).</summary>
    public async Task<ILocator> AbrirCancelamentoAsync(ILocator linha, string nomeItem)
    {
        await AbrirAcaoAsync(linha, "Cancelar viagem");
        var dialogo = DialogoComTitulo("Cancelar item da viagem");
        await Expect(dialogo).ToBeVisibleAsync();
        await dialogo.GetByRole(AriaRole.Radio, new() { Name = nomeItem }).CheckAsync();
        return dialogo;
    }

    public static ILocator BotaoConfirmarCancelamento(ILocator dialogo) =>
        dialogo.GetByRole(AriaRole.Button, new() { Name = "Confirmar cancelamento", Exact = true });

    public async Task InformarMotivoEConfirmarCancelamentoAsync(ILocator dialogo, string motivo)
    {
        await Shared.Components.Vuetify.Campo(dialogo, "Motivo do cancelamento").FillAsync(motivo);
        await ConfirmarAsync(dialogo, "Confirmar cancelamento");
    }

    private async Task ConfirmarAsync(ILocator dialogo, string botao)
    {
        var confirmar = dialogo.GetByRole(AriaRole.Button, new() { Name = botao, Exact = true });
        await Expect(confirmar).ToBeEnabledAsync();
        await confirmar.ClickAsync();
        await Expect(dialogo).ToBeHiddenAsync(new() { Timeout = 20_000 });
        await EsperarTabelaCarregarAsync();
    }
}
