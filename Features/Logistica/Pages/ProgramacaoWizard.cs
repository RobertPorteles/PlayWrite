using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Api;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Pages;

/// <summary>
/// Wizard de Programação (components/logistica/programacao/Cadastro.vue):
/// 1 Programação → 2 Passageiro/Carga → 3 Trajetos → 4 Execução.
/// </summary>
public sealed class ProgramacaoWizard(IPage page)
{
    private static readonly Regex PassoSelecionado = new("v-stepper-item--selected");

    private readonly Vuetify _vuetify = new(page);
    private readonly SweetAlert _alerta = new(page);

    public ILocator Card => page.Locator(".programacao-wizard-card");

    public ILocator PassoAtivo => Card.Locator(".v-window-item--active");

    public ILocator BotaoProximo => Acoes.GetByRole(AriaRole.Button, new() { Name = "Próximo", Exact = true });

    public ILocator BotaoSalvar => Acoes.GetByRole(AriaRole.Button, new() { Name = "Salvar programação", Exact = true });

    private ILocator Acoes => Card.Locator(".programacao-wizard-actions");

    public ILocator Passo(int numero) => Card.Locator(".v-stepper-item").Nth(numero - 1);

    public async Task PreencherDadosGeraisAsync(DadosApoio apoio)
    {
        var passo = PassoAtivo;
        var data = LogisticaDataFactory.FormatarData(LogisticaDataFactory.DataViagem);

        await Vuetify.Campo(passo, "Data da Viagem").FillAsync(data);
        await Vuetify.Campo(passo, "Hora da Viagem").FillAsync(HoraCampo(LogisticaDataFactory.HoraInicio));
        await Vuetify.Campo(passo, "Data Prevista Término").FillAsync(data);
        await Vuetify.Campo(passo, "Hora Prevista Término").FillAsync(HoraCampo(LogisticaDataFactory.HoraFim));
        await _vuetify.SelecionarAsync(passo, "Origem da solicitação", LogisticaDataFactory.OrigemSolicitacaoInternoNome);
        await _vuetify.SelecionarAutocompleteAsync(passo, "Cliente", apoio.NomeCliente);

        if (!apoio.Carga)
        {
            await _vuetify.SelecionarAutocompleteAsync(passo, "Centro de custo", apoio.NomeCentroCusto);
            await _vuetify.SelecionarAutocompleteAsync(passo, "Solicitante", apoio.NomeSolicitante);
        }
    }

    /// <summary>Seleciona o passageiro/carga, confirma e define os locais de origem e destino.</summary>
    public async Task AdicionarItemAsync(DadosApoio apoio)
    {
        var passo = PassoAtivo;
        await _vuetify.SelecionarAutocompleteAsync(passo, apoio.Carga ? "Carga" : "Passageiro", apoio.NomeItem);

        // CONFIRMAR inclui o item e o torna ativo, exibindo Origem/Destino. Se o item não ficar ativo, um novo clique
        // cai no caminho "já existe" do front, que reativa o mesmo item sem duplicá-lo.
        var confirmar = passo.Locator(".confirmar-passageiro-btn").First;
        var campoOrigem = Vuetify.Campo(passo, "Origem");
        for (var tentativa = 1; ; tentativa++)
        {
            await Expect(confirmar).ToBeEnabledAsync();
            await confirmar.ClickAsync();
            try
            {
                await Expect(campoOrigem).ToBeVisibleAsync(new() { Timeout = 5_000 });
                break;
            }
            catch (PlaywrightException) when (tentativa < 3)
            {
            }
        }

        await Expect(Card.Locator(".v-chip", new() { HasText = apoio.NomeItem })).ToHaveCountAsync(1);

        await _vuetify.SelecionarAutocompleteAsync(passo, "Origem", apoio.NomeOrigem);
        await _vuetify.SelecionarAutocompleteAsync(passo, "Destino", apoio.NomeDestino);
    }

    public async Task ProximoAsync(int passoEsperado)
    {
        await BotaoProximo.ClickAsync();
        try
        {
            await Expect(Passo(passoEsperado)).ToHaveClassAsync(PassoSelecionado);
        }
        catch (Exception)
        {
            if (await _alerta.Popup.IsVisibleAsync())
            {
                Assert.Fail($"O wizard não avançou para o passo {passoEsperado}. Aviso exibido: {await _alerta.Texto.InnerTextAsync()}");
            }

            throw;
        }
    }

    /// <summary>
    /// Liga "Definir ordem de serviço agora" e escolhe veículo e motorista (o texto informado ou a primeira opção).
    /// Devolve nulos se a empresa não tiver opções cadastradas.
    /// </summary>
    public async Task<(string? Veiculo, string? Motorista)> DefinirOrdemServicoAsync(string? veiculo, string? motorista)
    {
        var passo = PassoAtivo;
        var chave = passo.GetByLabel("Definir ordem de serviço agora", new() { Exact = true });
        if (!await chave.IsCheckedAsync())
        {
            await chave.CheckAsync();
        }

        await Expect(Vuetify.Campo(passo, "Veículo")).ToBeVisibleAsync();
        var veiculoEscolhido = await EscolherAsync(passo, "Veículo", veiculo);
        if (veiculoEscolhido is null)
        {
            return (null, null);
        }

        return (veiculoEscolhido, await EscolherAsync(passo, "Motorista", motorista));
    }

    /// <summary>Salva, confere que o alerta não é de erro e espera o wizard fechar. Devolve a mensagem exibida.</summary>
    public async Task<string> SalvarAsync()
    {
        await Expect(BotaoSalvar).ToBeEnabledAsync();
        await BotaoSalvar.ClickAsync();

        await Expect(_alerta.Popup).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var mensagem = $"{await _alerta.Titulo.InnerTextAsync()} {await _alerta.Texto.InnerTextAsync()}".Trim();
        if (await _alerta.EhErroOuAvisoAsync())
        {
            Assert.Fail($"A programação não foi salva: {mensagem}");
        }

        await _alerta.ConfirmarAsync();
        await Expect(Card).ToBeHiddenAsync(new() { Timeout = 20_000 });
        return mensagem;
    }

    private async Task<string?> EscolherAsync(ILocator escopo, string label, string? texto)
    {
        if (texto is null)
        {
            return await _vuetify.SelecionarPrimeiraOpcaoAsync(escopo, label);
        }

        await _vuetify.SelecionarAutocompleteAsync(escopo, label, texto);
        return texto;
    }

    private static string HoraCampo(TimeOnly hora) => hora.ToString("HH:mm", CultureInfo.InvariantCulture);
}
