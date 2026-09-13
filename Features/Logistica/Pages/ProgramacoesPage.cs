using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>Tela Programações (views/logistica/programacao).</summary>
public sealed class ProgramacoesPage(IPage page) : LogisticaListPage(page, Url, "Programações")
{
    public const string Url = "/logistica/programacoes";

    public ProgramacaoWizard Wizard { get; } = new(page);

    public async Task<ProgramacaoWizard> NovaProgramacaoAsync()
    {
        await BotaoAdicionar.ClickAsync();
        await Expect(Wizard.Card.GetByText("Criar Programação", new() { Exact = true })).ToBeVisibleAsync();
        return Wizard;
    }

    /// <summary>Ação "Enviar OS": abre o wizard da programação existente direto no passo Execução.</summary>
    public async Task<ProgramacaoWizard> EnviarOrdemServicoAsync(ILocator linha)
    {
        await AbrirAcaoAsync(linha, "Enviar OS");
        await Expect(Wizard.Card).ToBeVisibleAsync();
        await Expect(Wizard.Passo(4)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("v-stepper-item--selected"));
        return Wizard;
    }
}
