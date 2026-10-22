using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Tutorial do faturamento: percorre o módulo inteiro pela tela, como um usuário faria, com uma legenda em cada passo.
/// Para assistir: <c>HEADED=1 SLOWMO_MS=400</c> (navegador visível) ou <c>VIDEO=1</c> (grava em <c>videos/</c>). Nesses
/// modos cada legenda fica alguns segundos na tela; <c>TUTORIAL_PAUSA_MS</c> muda esse tempo.
/// <para>
/// A programação e a viagem finalizada nascem pela API (o wizard e a finalização da viagem têm testes próprios); o valor
/// da rota é informado pela tela. A fatura aprovada fica no banco: fatura não tem DELETE, é histórico.
/// </para>
/// </summary>
[TestFixture]
[Category(Categoria)]
public class TutorialFaturamentoTests : LogisticaTestBase
{
    private static int PausaLeitura =>
        int.TryParse(E2ESettings.Ler("TUTORIAL_PAUSA_MS"), out var pausa) ? pausa
        : Environment.GetEnvironmentVariable("HEADED") == "1" || E2ESettings.Ler("VIDEO_DIR") is not null ? 3000
        : 0;

    [Test]
    public async Task TutorialDoFaturamentoDaOsAteAFaturaAprovada()
    {
        // Preparação: cliente, centro de custo, solicitante, passageiro, locais e uma OS já realizada (viagem
        // finalizada), ainda sem valor de rota.
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var os = await Logistica.CriarOrdemServicoFaturavelAsync(apoio, valorRota: null);
        var dia = LogisticaDataFactory.DataViagem;
        var menu = new AppMenu(Page);
        var alerta = new SweetAlert(Page);

        await LoginNaUiAsync();

        // 1. Achar a programação e informar o valor da rota.
        await menu.NavegarAsync("Planejamento", "Programações", ProgramacoesPage.Url);
        await NarrarAsync("1. Programações",
            $"A OS #{os.OrdemServicoId} é do cliente {apoio.NomeCliente} e já foi realizada: a viagem terminou. Mas ela está sem valor de rota, e sem ele não pode ser faturada. Vamos informar o valor pelo menu Ações → Enviar OS.");
        var programacoes = new ProgramacoesPage(Page);
        var linha = await programacoes.LocalizarLinhaAsync(apoio.NomeCliente);
        var wizard = await programacoes.EnviarOrdemServicoAsync(linha);
        await wizard.InformarValorDaRotaAsync(782.12m);
        await NarrarAsync("1. Valor da rota",
            "No passo Execução fica o Valor da rota: a cotação do serviço para o cliente, R$ 782,12. Ele é a base do faturamento e vai ao Drake na confirmação. Dá para informar ao criar a OS ou depois, enquanto ela não estiver numa fatura emitida.");
        await wizard.SalvarAsync();
        linha = await programacoes.LocalizarLinhaAsync(apoio.NomeCliente);
        await programacoes.AbrirAcaoAsync(linha, "Ver detalhes");
        await Expect(Page).ToHaveURLAsync(new Regex($"/logistica/programacoes/{os.SolicitacaoId}$"));

        // 2. Bloco Valores da OS: valor da rota + ajustes.
        var valores = Page.Locator(".v-card", new() { HasText = "Valores da OS" }).Last;
        await Expect(valores).ToContainTextAsync("R$ 782,12");
        await NarrarAsync("2. Valores da OS",
            "Valor da OS = valor da rota + ajustes. A rota vale R$ 782,12. Tempo de espera, pedágio e estacionamento só se sabem depois da viagem, e entram como ajuste.");

        await valores.GetByRole(AriaRole.Button, new() { Name = "Adicionar ajuste" }).ClickAsync();
        var dialogo = Page.Locator(".v-overlay--active .v-card", new() { HasText = "Adicionar ajuste" }).Last;
        // O v-select do Vuetify nunca fica "estável" para o Playwright dentro do diálogo animado: o clique é forçado.
        await dialogo.GetByRole(AriaRole.Combobox, new() { Name = "Tipo" }).ClickAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Option, new() { Name = "Tempo de espera" }).ClickAsync();
        await dialogo.GetByLabel("Valor").FillAsync("58.31");
        await dialogo.GetByLabel("Justificativa").FillAsync("35 minutos de espera no hotel");
        await NarrarAsync("2. Valores da OS", "Todo ajuste pede tipo, valor e justificativa. O sistema guarda quem lançou e quando.");
        await dialogo.GetByRole(AriaRole.Button, new() { Name = "Salvar" }).ClickAsync();
        await Expect(valores).ToContainTextAsync("35 minutos de espera no hotel");
        await Expect(valores).ToContainTextAsync("R$ 840,43");
        await NarrarAsync("2. Valores da OS", "Total da OS: R$ 782,12 + R$ 58,31 = R$ 840,43. Enquanto a OS não estiver numa fatura emitida, dá para lançar e remover ajustes.");

        // 3. Medição: escolher cliente e período e gerar a fatura.
        await menu.NavegarAsync("Financeiro e Órgão Gestor", "Medição", "/logistica/faturamento/medicao");
        await NarrarAsync("3. Medição",
            "A Medição lista as OS que podem ser faturadas: realizadas ou em no-show e fora de qualquer fatura ativa. Escolha o período (até 31 dias) e o cliente.");
        await Page.GetByLabel("Data inicial").FillAsync(LogisticaDataFactory.FormatarData(dia.AddDays(-1)));
        await Page.GetByLabel("Data final").FillAsync(LogisticaDataFactory.FormatarData(dia.AddDays(1)));
        var cliente = Page.GetByRole(AriaRole.Combobox, new() { Name = "Cliente", Exact = true });
        await cliente.ClickAsync();
        await cliente.FillAsync(apoio.NomeCliente);
        await Page.GetByRole(AriaRole.Option, new() { Name = apoio.NomeCliente }).ClickAsync();
        await Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Buscar OS" }).ClickAsync(),
            r => r.Url.Contains("/Faturamento/Medicao") && r.Status == 200);

        var linhaMedicao = Page.Locator("tbody tr", new() { HasText = $"#{os.OrdemServicoId}" });
        await Expect(linhaMedicao).ToContainTextAsync("R$ 840,43");
        await linhaMedicao.Locator("input[type=checkbox]").CheckAsync();
        await Expect(Page.GetByText("1 OS selecionada(s) · Total R$ 840,43")).ToBeVisibleAsync();
        await NarrarAsync("3. Medição",
            "A OS aparece com o valor já somado aos ajustes. Marque as OS que vão na fatura e confira o total no rodapé. OS sem valor de rota aparecem com alerta e não podem ser marcadas.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Gerar fatura" }).ClickAsync();
        await alerta.EsperarAsync("Gerar fatura", apoio.NomeCliente);
        var geracao = await Page.RunAndWaitForResponseAsync(
            () => alerta.ConfirmarAsync(),
            r => r.Request.Method == "POST" && r.Url.EndsWith("/Faturamento/Fatura"));
        var fatura = (await geracao.JsonAsync())!.Value.GetProperty("id").GetInt64();

        // 4. Fatura Aberta → Emitida.
        await Expect(Page).ToHaveURLAsync(new Regex($"/logistica/faturamento/faturas/{fatura}$"));
        await Expect(Page.GetByText("Aberta", new() { Exact = true }).First).ToBeVisibleAsync();
        await NarrarAsync("4. Fatura aberta",
            $"A fatura #{fatura} nasceu Aberta, como um rascunho. Ainda dá para remover OS (botão na linha) ou cancelar. Os valores acompanham os ajustes.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Emitir" }).ClickAsync();
        await alerta.EsperarAsync("Atenção", $"Emitir a fatura #{fatura}?");
        await alerta.ConfirmarAsync();
        await Expect(Page.GetByText("Emitida", new() { Exact = true }).First).ToBeVisibleAsync();
        await NarrarAsync("4. Fatura emitida",
            "Emitir congela os valores: o sistema grava o valor de cada OS e os ajustes do momento. A partir daqui a OS não aceita ajuste nem troca do valor da rota.");

        // 5. Envio ao cliente e retorno.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Marcar como enviada" }).ClickAsync();
        await alerta.EsperarAsync("Atenção", $"Marcar a fatura #{fatura} como enviada");
        await alerta.ConfirmarAsync();
        await Expect(Page.GetByText(new Regex("^Marcada como enviada em "))).ToBeVisibleAsync();
        await NarrarAsync("5. Enviada",
            "O envio ao cliente é feito fora do sistema (e-mail, portal). Aqui só registramos que foi enviada, e o histórico guarda a data.");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Registrar aprovação" }).ClickAsync();
        await alerta.EsperarAsync("Atenção", $"Registrar que o cliente aprovou a fatura #{fatura}?");
        await alerta.ConfirmarAsync();
        await Expect(Page.GetByText("Aprovada", new() { Exact = true }).First).ToBeVisibleAsync();
        await NarrarAsync("5. Aprovada",
            "O cliente aprovou e a fatura chegou ao estado final: não pode mais ser cancelada. Se ele tivesse reprovado (com motivo), o caminho seria cancelar, corrigir e gerar outra.");

        // 6. De volta à OS: agora ela está travada.
        await Page.GetByRole(AriaRole.Link, new() { Name = $"#{os.OrdemServicoId}" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($"/logistica/programacoes/{os.SolicitacaoId}$"));
        var valoresTravados = Page.Locator(".v-card", new() { HasText = "Valores da OS" }).Last;
        await Expect(valoresTravados).ToContainTextAsync($"Faturada na fatura #{fatura} (Aprovada)");
        await Expect(valoresTravados.GetByRole(AriaRole.Button, new() { Name = "Adicionar ajuste" })).ToHaveCountAsync(0);
        await NarrarAsync("6. OS faturada",
            "Na programação, a OS mostra em qual fatura está e não deixa mais lançar ajuste. Também não pode ser excluída: o que foi faturado fica no histórico.");

        // 7. Lista de faturas.
        await menu.NavegarAsync("Financeiro e Órgão Gestor", "Faturas", "/logistica/faturamento/faturas");
        await Expect(Page.Locator("tbody tr", new() { HasText = $"#{fatura}" })).ToContainTextAsync("Aprovada");
        await NarrarAsync("7. Faturas",
            "A lista mostra todas as faturas, com filtros de status, cliente e período. Clique numa linha para abrir o detalhe. Fim do tutorial!");
    }

    /// <summary>Legenda fixa no rodapé da tela (sem bloquear cliques), repetida no console do teste.</summary>
    private async Task NarrarAsync(string passo, string texto)
    {
        await Page.EvaluateAsync(
            """
            ([passo, texto]) => {
                let legenda = document.getElementById('tutorial-legenda');
                if (!legenda) {
                    legenda = document.createElement('div');
                    legenda.id = 'tutorial-legenda';
                    legenda.style.cssText = 'position:fixed;left:50%;bottom:24px;transform:translateX(-50%);z-index:99999;'
                        + 'width:min(900px,90vw);padding:14px 20px;border-radius:10px;background:rgba(20,22,26,.92);color:#fff;'
                        + 'font:15px/1.45 sans-serif;box-shadow:0 6px 24px rgba(0,0,0,.35);pointer-events:none';
                    document.body.appendChild(legenda);
                }
                const titulo = document.createElement('strong');
                titulo.textContent = passo;
                const corpo = document.createElement('div');
                corpo.textContent = texto;
                legenda.replaceChildren(titulo, corpo);
            }
            """,
            new[] { passo, texto });
        TestContext.Progress.WriteLine($"{passo} — {texto}");

        if (PausaLeitura > 0)
        {
            await Page.WaitForTimeoutAsync(PausaLeitura);
        }
    }
}
