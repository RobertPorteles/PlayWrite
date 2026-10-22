using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Features.Logistica.Api;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Mensagens de erro do faturamento de logística (v1/Logistica/Faturamento), conferidas no contrato da API: é o
/// <c>message</c> que a tela mostra quando a chave <c>modules.faturamento.errors.*</c> não está no front. Módulo novo,
/// então não há "mensagem de antes": sem estas regras, o banco recusaria com erro de FK/constraint (500 sem texto).
/// <para>
/// Ficam de fora <c>faturaAlteradaPorOutroUsuario</c> e <c>osFaturadaAoMesmoTempo</c>: só acontecem numa corrida entre
/// duas gravações, o que não dá para provocar de forma determinística pela API. Estão cobertas por
/// <c>FaturamentoServiceTests</c> e pela verificação no Postgres registrada na spec.
/// </para>
/// <para>
/// Fatura não tem DELETE (é histórico), e o item segura a OS por FK: os testes que geram fatura deixam no banco a
/// fatura, a programação e o cliente "E2E-" do teste. Como cada teste usa um cliente próprio, isso não interfere nos
/// próximos.
/// </para>
/// </summary>
[TestFixture]
[Category(Categoria)]
public class MensagensErroFaturamentoTests : LogisticaTestBase
{
    private const string Faturamento = "v1/Logistica/Faturamento";

    // A validação do ajuste roda antes de buscar a OS, então o id não precisa existir.
    private const long OrdemServicoQualquer = 1;

    private static DateOnly DiaDaViagem => LogisticaDataFactory.DataViagem;

    [Test]
    public async Task MedicaoSemPeriodoDevePedirAsDuasDatas()
    {
        var resposta = await Api.EnviarAsync("GET", $"{Faturamento}/Medicao?clienteId=1");

        AssertMensagem(resposta, 400, "Informe a data inicial e a data final do período.");
    }

    [Test]
    public async Task MedicaoComDataFinalAntesDaInicialDeveDizerAOrdemCerta()
    {
        var resposta = await Api.EnviarAsync("GET", $"{Faturamento}/Medicao?clienteId=1&dataInicio=2026-10-10&dataFim=2026-10-01");

        AssertMensagem(resposta, 400, "A data final deve ser maior ou igual à data inicial.");
    }

    [Test]
    public async Task MedicaoComMaisDe31DiasDeveDizerOLimite()
    {
        var resposta = await Api.EnviarAsync("GET", $"{Faturamento}/Medicao?clienteId=1&dataInicio=2026-10-01&dataFim=2026-11-01");

        AssertMensagem(resposta, 400, "O período pode ter no máximo 31 dias.");
    }

    [Test]
    public async Task MedicaoSemClienteDevePedirOCliente()
    {
        var resposta = await Api.EnviarAsync("GET", $"{Faturamento}/Medicao?dataInicio=2026-10-01&dataFim=2026-10-31");

        AssertMensagem(resposta, 400, "Informe o cliente da medição.");
    }

    [Test]
    public async Task GerarFaturaSemOsDevePedirAoMenosUma()
    {
        var resposta = await GerarAsync(Fatura(clienteId: 1));

        AssertMensagem(resposta, 400, "Selecione ao menos uma OS para gerar a fatura.");
    }

    [Test]
    public async Task GerarFaturaComCentroDeCustoDeOutroClienteDeveRecusar()
    {
        var cliente = await Logistica.CriarClienteAsync();
        var outroCliente = await Logistica.CriarClienteAsync();
        var centroCustoDoOutro = await Logistica.CriarCentroCustoAsync(outroCliente.Id());

        var resposta = await GerarAsync(Fatura(cliente.Id(), OrdemServicoQualquer, centroCustoId: centroCustoDoOutro.Id()));

        AssertMensagem(resposta, 400, "O centro de custo informado não pertence ao cliente da fatura.");
    }

    [TestCase(99, 10, "Conferido", "Selecione o tipo do ajuste.",
        TestName = "AjusteSemTipoValidoDevePedirOTipo")]
    [TestCase(1, 0, "Conferido", "Informe um valor diferente de zero para o ajuste.",
        TestName = "AjusteComValorZeroDevePedirOutroValor")]
    [TestCase(6, 10, "Conferido", "O valor do desconto deve ser negativo.",
        TestName = "DescontoPositivoDeveDizerQueODescontoENegativo")]
    [TestCase(2, -10, "Conferido", "O valor deste tipo de ajuste deve ser positivo. Para reduzir o valor da OS, use um desconto.",
        TestName = "PedagioNegativoDeveIndicarODesconto")]
    [TestCase(2, 10, "  ", "Informe a justificativa do ajuste.",
        TestName = "AjusteSemJustificativaDevePedirAJustificativa")]
    public async Task AjusteInvalidoDeveDizerOQueCorrigir(int tipo, decimal valor, string justificativa, string mensagem)
    {
        var resposta = await LancarAjusteAsync(OrdemServicoQualquer, tipo, valor, justificativa);

        AssertMensagem(resposta, 400, mensagem);
    }

    [Test]
    public async Task AjusteComJustificativaLongaDeveDizerOLimite()
    {
        var resposta = await LancarAjusteAsync(OrdemServicoQualquer, 2, 10, new string('a', 501));

        AssertMensagem(resposta, 400, "A justificativa do ajuste pode ter no máximo 500 caracteres.");
    }

    [Test]
    public async Task GerarFaturaComOsNaoFinalizadaDeveDizerQueAindaNaoPodeSerFaturada()
    {
        var (apoio, os) = await CriarOsAsync(realizada: false);

        var resposta = await GerarAsync(Fatura(apoio.Cliente.Id(), os.OrdemServicoId));

        AssertMensagem(resposta, 400,
            $"A OS #{os.OrdemServicoId} não pode ser faturada porque ainda não foi finalizada nem registrada como no-show.");
    }

    [Test]
    public async Task GerarFaturaComOsDeOutroClienteDeveRecusarAOs()
    {
        var (_, os) = await CriarOsAsync();
        var outroCliente = await Logistica.CriarClienteAsync();

        var resposta = await GerarAsync(Fatura(outroCliente.Id(), os.OrdemServicoId));

        AssertMensagem(resposta, 400, $"A OS #{os.OrdemServicoId} é de outro cliente e não pode entrar nesta fatura.");
    }

    [Test]
    public async Task GerarFaturaComOsDeOutroCentroDeCustoDeveRecusarAOs()
    {
        var (apoio, os) = await CriarOsAsync();
        var outroCentroCusto = await Logistica.CriarCentroCustoAsync(apoio.Cliente.Id());

        var resposta = await GerarAsync(Fatura(apoio.Cliente.Id(), os.OrdemServicoId, centroCustoId: outroCentroCusto.Id()));

        AssertMensagem(resposta, 400, $"A OS #{os.OrdemServicoId} é de outro centro de custo e não pode entrar nesta fatura.");
    }

    [Test]
    public async Task GerarFaturaComOsForaDoPeriodoDeveRecusarAOs()
    {
        var (apoio, os) = await CriarOsAsync();

        var resposta = await GerarAsync(Fatura(apoio.Cliente.Id(), os.OrdemServicoId,
            inicio: DiaDaViagem.AddDays(1), fim: DiaDaViagem.AddDays(2)));

        AssertMensagem(resposta, 400, $"A OS #{os.OrdemServicoId} está fora do período da fatura.");
    }

    [Test]
    public async Task GerarFaturaComOsSemValorDeRotaDevePedirOValor()
    {
        var (apoio, os) = await CriarOsAsync(valorRota: null);

        var resposta = await GerarAsync(Fatura(apoio.Cliente.Id(), os.OrdemServicoId));

        AssertMensagem(resposta, 400,
            $"A OS #{os.OrdemServicoId} não tem valor de rota. Informe o valor na programação antes de faturar.");
    }

    [Test]
    public async Task GerarFaturaComOsJaFaturadaDeveDizerEmQualFaturaEla()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);

        var resposta = await GerarAsync(Fatura(apoio.Cliente.Id(), os.OrdemServicoId));

        AssertMensagem(resposta, 409,
            $"A OS #{os.OrdemServicoId} já está na fatura #{fatura}. Cancele essa fatura ou remova a OS dela antes de faturá-la de novo.");
    }

    [Test]
    public async Task AjusteEmOsDeFaturaEmitidaDeveMandarCancelarAFatura()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);
        await AcaoComSucessoAsync(fatura, "Emitir");

        var resposta = await LancarAjusteAsync(os.OrdemServicoId, 2, 10, "Pedágio esquecido");

        AssertMensagem(resposta, 400,
            $"A OS #{os.OrdemServicoId} está na fatura #{fatura}, já emitida. Para alterar o valor, cancele a fatura.");
    }

    [Test]
    public async Task EmitirFaturaSemOsDeveMandarAdicionarOuCancelar()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);
        (await Api.EnviarAsync("DELETE", $"{Faturamento}/Fatura/{fatura}/Item/{os.OrdemServicoId}")).GarantirSucesso();

        var resposta = await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/Emitir");

        AssertMensagem(resposta, 400, $"A fatura #{fatura} não tem OS. Adicione OS pela Medição ou cancele a fatura.");
    }

    [Test]
    public async Task MarcarComoEnviadaDuasVezesDeveDizerQuandoFoiEnviada()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);
        await AcaoComSucessoAsync(fatura, "Emitir");
        var enviada = await AcaoComSucessoAsync(fatura, "MarcarEnviada");
        var dataEnvio = DateTime.Parse(enviada["dataEnvio"].ComoTexto()!, CultureInfo.InvariantCulture)
            .ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var resposta = await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/MarcarEnviada");

        AssertMensagem(resposta, 400, $"A fatura #{fatura} já foi marcada como enviada em {dataEnvio}.");
    }

    [Test]
    public async Task ReprovarSemMotivoDevePedirOMotivo()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);
        await AcaoComSucessoAsync(fatura, "Emitir");

        var resposta = await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/RegistrarRetorno",
            new JsonObject { ["aprovada"] = false });

        AssertMensagem(resposta, 400, "Informe o motivo da reprovação.");
    }

    [Test]
    public async Task CancelarSemMotivoDevePedirOMotivo()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);

        var resposta = await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/Cancelar",
            new JsonObject { ["motivo"] = "  " });

        AssertMensagem(resposta, 400, "Informe o motivo do cancelamento.");
    }

    [Test]
    public async Task EmitirFaturaJaEmitidaDeveDizerOStatusAtual()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);
        await AcaoComSucessoAsync(fatura, "Emitir");

        var resposta = await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/Emitir");

        AssertMensagem(resposta, 400, $"A fatura #{fatura} está emitida e não pode ser emitida.");
    }

    /// <summary>Pela API: na tela o wizard já barra o valor negativo com o mesmo texto antes de salvar.</summary>
    [Test]
    public async Task CriarOsComValorDeRotaNegativoDevePedirValorMaiorOuIgualAZero()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var solicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Faturamento"));

        var resposta = await Api.EnviarAsync("POST", LogisticaRecursos.OrdemServico, new JsonObject
        {
            ["solicitacaoLogisticaId"] = solicitacao.Id(),
            ["rota"] = new JsonObject { ["nome"] = LogisticaDataFactory.Nome("Rota"), ["valor"] = -1 },
        });

        AssertMensagem(resposta, 400, "Informe um valor de rota maior ou igual a zero.");
    }

    /// <summary>Sem tela (a OS sai junto com a programação); antes, o DELETE da OS devolvia 500 sem texto.</summary>
    [Test]
    public async Task ExcluirOsFaturadaPelaApiDeveDizerEmQualFaturaEla()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);

        var resposta = await Api.DeleteAsync($"{LogisticaRecursos.OrdemServico}/{os.OrdemServicoId}");

        AssertMensagem(resposta, 400,
            $"A OS #{os.OrdemServicoId} não pode ser excluída porque entrou na fatura #{fatura}, " +
            "e o que foi faturado fica no histórico do faturamento. " +
            "Para não cobrar a OS, remova-a da fatura enquanto ela estiver aberta ou cancele a fatura.");
    }

    /// <summary>
    /// Pela tela: antes desta regra, excluir a programação estourava a FK do item de fatura e a tela mostrava
    /// "Ocorreu um erro inesperado." (500 sem texto).
    /// </summary>
    [Test]
    public async Task ExcluirProgramacaoFaturadaPelaTelaDeveDizerEmQualFaturaAOsEsta()
    {
        var (apoio, os) = await CriarOsAsync();
        var fatura = await GerarComSucessoAsync(apoio, os);

        await LoginNaUiAsync();
        var programacoes = new ProgramacoesPage(Page);
        await programacoes.AbrirAsync();
        var linha = await programacoes.LocalizarLinhaAsync(apoio.NomeCliente);
        await programacoes.AbrirAcaoAsync(linha, "Excluir");

        var alerta = new SweetAlert(Page);
        await alerta.EsperarAsync("Atenção", "Tem certeza que deseja excluir");
        var resposta = await Page.RunAndWaitForResponseAsync(
            () => alerta.ConfirmarSemEsperarAsync(),
            r => r.Request.Method == "DELETE");

        Assert.That(resposta.Status, Is.EqualTo(400), await resposta.TextAsync());
        await Expect(alerta.Texto).ToHaveTextAsync(
            $"A programação #{os.SolicitacaoId} não pode ser excluída porque a OS #{os.OrdemServicoId} entrou na fatura #{fatura}, " +
            "e o que foi faturado fica no histórico do faturamento. " +
            "Para não cobrar a OS, remova-a da fatura enquanto ela estiver aberta ou cancele a fatura.");
    }

    private async Task<(DadosApoio Apoio, OrdemServicoFaturavel Os)> CriarOsAsync(decimal? valorRota = 100m, bool realizada = true)
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        return (apoio, await Logistica.CriarOrdemServicoFaturavelAsync(apoio, valorRota, realizada));
    }

    private async Task<long> GerarComSucessoAsync(DadosApoio apoio, OrdemServicoFaturavel os) =>
        (await Api.PostAsync($"{Faturamento}/Fatura", Fatura(apoio.Cliente.Id(), os.OrdemServicoId)))!.Id();

    private async Task<JsonNode> AcaoComSucessoAsync(long fatura, string acao) =>
        (await Api.EnviarAsync("PATCH", $"{Faturamento}/Fatura/{fatura}/{acao}")).GarantirSucesso().Json!;

    private Task<ApiResposta> GerarAsync(JsonObject fatura) => Api.EnviarAsync("POST", $"{Faturamento}/Fatura", fatura);

    private Task<ApiResposta> LancarAjusteAsync(long ordemServicoId, int tipo, decimal valor, string justificativa) =>
        Api.EnviarAsync("POST", $"{Faturamento}/OrdemServico/{ordemServicoId}/Ajuste", new JsonObject
        {
            ["tipo"] = tipo,
            ["valor"] = valor,
            ["justificativa"] = justificativa,
        });

    private static JsonObject Fatura(long clienteId, long? ordemServicoId = null, long? centroCustoId = null,
        DateOnly? inicio = null, DateOnly? fim = null) => new()
    {
        ["clienteId"] = clienteId,
        ["centroCustoId"] = centroCustoId,
        ["periodoInicio"] = LogisticaDataFactory.FormatarData(inicio ?? DiaDaViagem.AddDays(-1)),
        ["periodoFim"] = LogisticaDataFactory.FormatarData(fim ?? DiaDaViagem.AddDays(1)),
        ["ordemServicoIds"] = ordemServicoId is { } id ? new JsonArray(id) : new JsonArray(),
    };

    private static void AssertMensagem(ApiResposta resposta, int status, string mensagem) =>
        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(status), resposta.Corpo);
            Assert.That(resposta.Json?["message"].ComoTexto(), Is.EqualTo(mensagem), resposta.Corpo);
        });
}
