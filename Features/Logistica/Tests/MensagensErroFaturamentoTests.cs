using System.Text.Json.Nodes;
using PlaywrightTests.Shared.Api;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Mensagens de erro do faturamento de logística (v1/Logistica/Faturamento), conferidas no contrato da API: é o
/// <c>message</c> que a tela mostra quando a chave <c>modules.faturamento.errors.*</c> não está no front. Módulo novo,
/// então não há "mensagem de antes": sem estas regras, o banco recusaria com erro de FK/constraint (500 sem texto).
/// Aqui ficam as regras que o backend valida antes de qualquer OS; as que dependem de uma OS faturável (realizada,
/// com valor de rota) estão cobertas por <c>FaturamentoServiceTests</c> no backend e pedem uma fixture de OS no E2E.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class MensagensErroFaturamentoTests : LogisticaTestBase
{
    private const string Faturamento = "v1/Logistica/Faturamento";

    // A validação do ajuste roda antes de buscar a OS, então o id não precisa existir.
    private const long OrdemServicoQualquer = 1;

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
        var resposta = await Api.EnviarAsync("POST", $"{Faturamento}/Fatura", Fatura(clienteId: 1, centroCustoId: null));

        AssertMensagem(resposta, 400, "Selecione ao menos uma OS para gerar a fatura.");
    }

    [Test]
    public async Task GerarFaturaComCentroDeCustoDeOutroClienteDeveRecusar()
    {
        var cliente = await Logistica.CriarClienteAsync();
        var outroCliente = await Logistica.CriarClienteAsync();
        var centroCustoDoOutro = await Logistica.CriarCentroCustoAsync(outroCliente.Id());

        var resposta = await Api.EnviarAsync("POST", $"{Faturamento}/Fatura",
            Fatura(cliente.Id(), centroCustoDoOutro.Id(), OrdemServicoQualquer));

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
        var resposta = await LancarAjusteAsync(tipo, valor, justificativa);

        AssertMensagem(resposta, 400, mensagem);
    }

    [Test]
    public async Task AjusteComJustificativaLongaDeveDizerOLimite()
    {
        var resposta = await LancarAjusteAsync(2, 10, new string('a', 501));

        AssertMensagem(resposta, 400, "A justificativa do ajuste pode ter no máximo 500 caracteres.");
    }

    private Task<ApiResposta> LancarAjusteAsync(int tipo, decimal valor, string justificativa) =>
        Api.EnviarAsync("POST", $"{Faturamento}/OrdemServico/{OrdemServicoQualquer}/Ajuste", new JsonObject
        {
            ["tipo"] = tipo,
            ["valor"] = valor,
            ["justificativa"] = justificativa,
        });

    private static JsonObject Fatura(long clienteId, long? centroCustoId, params long[] ordemServicoIds) => new()
    {
        ["clienteId"] = clienteId,
        ["centroCustoId"] = centroCustoId,
        ["periodoInicio"] = "2026-10-01",
        ["periodoFim"] = "2026-10-31",
        ["ordemServicoIds"] = new JsonArray(ordemServicoIds.Select(id => (JsonNode)id).ToArray()),
    };

    private static void AssertMensagem(ApiResposta resposta, int status, string mensagem) =>
        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(status), resposta.Corpo);
            Assert.That(resposta.Json?["message"].ComoTexto(), Is.EqualTo(mensagem), resposta.Corpo);
        });
}
