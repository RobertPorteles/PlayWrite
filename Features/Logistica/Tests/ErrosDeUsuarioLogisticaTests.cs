using System.Text.Json.Nodes;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Components;
using PlaywrightTests.Shared.Pages;

namespace PlaywrightTests.Features.Logistica.Tests;

/// <summary>
/// Enganos plausíveis de quem opera a tela. Cada caso pergunta a mesma coisa: o erro é evitado ou
/// explicado na hora, ou o usuário só descobre depois de salvar, com uma mensagem técnica?
/// </summary>
[TestFixture]
[Category(Categoria)]
public class ErrosDeUsuarioLogisticaTests : LogisticaTestBase
{
    private const string CancelarItem = $"{LogisticaRecursos.ViagemLogistica}/CancelarItem";
    private const string FinalizarItem = $"{LogisticaRecursos.ViagemLogistica}/FinalizarItem";

    /// <summary>
    /// Cancelar exige motivo (ViagemLogisticaService). A tela precisa impedir o envio enquanto o campo
    /// está vazio, em vez de deixar clicar e devolver 400.
    /// </summary>
    [Test]
    public async Task CancelarViagemSemMotivoDeveSerBloqueadoNaPropriaTela()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var solicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));
        var observacao = LogisticaDataFactory.Nome("Viagem");
        await Logistica.CriarViagemIniciadaAsync(solicitacao, observacao);

        await LoginNaUiAsync();
        await new AppMenu(Page).NavegarAsync("Operação em Tempo Real", "Viagem Logística", ViagemLogisticaPage.Url);
        var viagens = new ViagemLogisticaPage(Page);
        await viagens.EsperarCarregadaAsync();

        var linha = await viagens.LocalizarLinhaAsync(observacao);
        var dialogo = await viagens.AbrirCancelamentoAsync(linha, apoio.NomeItem);

        await Assert.ThatAsync(
            async () => await ViagemLogisticaPage.BotaoConfirmarCancelamento(dialogo).IsDisabledAsync(),
            Is.True,
            "Com o motivo em branco, o botão de confirmar cancelamento deveria estar desabilitado. " +
            "Habilitado, o usuário só descobre a obrigatoriedade pelo 400 da API.");
    }

    /// <summary>Mesmo enviado direto à API, cancelar sem motivo tem que ser recusado com mensagem para humano.</summary>
    [Test]
    public async Task CancelarSemMotivoDeveRetornar400ComMensagemUtil()
    {
        var (viagem, itemId) = await CriarViagemComItemAsync();

        var resposta = await Api.EnviarAsync("POST", CancelarItem, new JsonObject
        {
            ["viagemLogisticaId"] = viagem.Id(),
            ["viagemLogisticaItemId"] = itemId,
            ["observacao"] = "",
        });

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(400), $"Esperado 400, veio {resposta.Status}: {resposta.Corpo}");
            Assert.That(resposta.Corpo, Does.Contain("observação").IgnoreCase.Or.Contains("observacao").IgnoreCase,
                "A mensagem precisa dizer qual campo falta.");
        });
    }

    /// <summary>Clique duplo em finalizar: a segunda chamada precisa ser recusada sem efeito colateral.</summary>
    [Test]
    public async Task FinalizarDuasVezesDeveRecusarASegundaChamada()
    {
        var (viagem, itemId) = await CriarViagemComItemAsync();
        var corpo = new JsonObject
        {
            ["viagemLogisticaId"] = viagem.Id(),
            ["viagemLogisticaItemId"] = itemId,
            ["observacao"] = "Finalização E2E",
        };

        var primeira = await Api.EnviarAsync("POST", FinalizarItem, corpo);
        Assert.That(primeira.Sucesso, Is.True, $"A primeira finalização deveria funcionar: {primeira.Status} {primeira.Corpo}");

        var segunda = await Api.EnviarAsync("POST", FinalizarItem, corpo);

        Assert.Multiple(() =>
        {
            Assert.That(segunda.Status, Is.EqualTo(400), $"Esperado 400 na repetição, veio {segunda.Status}: {segunda.Corpo}");
            Assert.That(segunda.Corpo, Does.Contain("finalizado").IgnoreCase,
                "A mensagem precisa explicar que o item já está finalizado.");
        });
    }

    /// <summary>Clique duplo em "Enviar OS": a solicitação já tem ordem de serviço e a segunda tem que ser recusada.</summary>
    [Test]
    public async Task SalvarOrdemServicoDuasVezesNaMesmaSolicitacaoDeveSerRecusado()
    {
        var (veiculoId, motoristaId) = await ObterParDisponivelAsync();
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var solicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));

        var primeira = await SalvarOrdemServicoAsync(solicitacao.Id(), veiculoId, motoristaId);
        Assert.That(primeira.Sucesso, Is.True, $"A primeira OS deveria salvar: {primeira.Status} {primeira.Corpo}");

        var segunda = await SalvarOrdemServicoAsync(solicitacao.Id(), veiculoId, motoristaId);

        Assert.Multiple(() =>
        {
            Assert.That(segunda.Status, Is.EqualTo(400), $"Esperado 400 na repetição, veio {segunda.Status}: {segunda.Corpo}");
            Assert.That(segunda.Corpo, Does.Contain("ordem de servico").IgnoreCase.Or.Contains("ordem de serviço").IgnoreCase,
                "A mensagem precisa dizer que a solicitação já tem OS.");
        });
    }

    /// <summary>
    /// Mesmo par motorista/veículo em duas programações do mesmo período: o segundo save tem que ser
    /// recusado apontando a programação conflitante, já que a OS vincula o par automaticamente.
    /// </summary>
    [Test]
    public async Task SalvarOrdemServicoComParJaAgendadoNoPeriodoDeveApontarAProgramacaoConflitante()
    {
        var (veiculoId, motoristaId) = await ObterParDisponivelAsync();
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var primeiraSolicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));
        var segundaSolicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));

        var primeira = await SalvarOrdemServicoAsync(primeiraSolicitacao.Id(), veiculoId, motoristaId);
        Assert.That(primeira.Sucesso, Is.True, $"A primeira OS deveria salvar: {primeira.Status} {primeira.Corpo}");

        var segunda = await SalvarOrdemServicoAsync(segundaSolicitacao.Id(), veiculoId, motoristaId);

        Assert.Multiple(() =>
        {
            Assert.That(segunda.Status, Is.EqualTo(400), $"Esperado 400, veio {segunda.Status}: {segunda.Corpo}");
            Assert.That(segunda.Corpo, Does.Contain("agendad").IgnoreCase,
                "A mensagem precisa explicar que o par já está agendado em outra programação do mesmo período.");
            Assert.That(segunda.Corpo, Does.Contain($"#{primeiraSolicitacao.Id()}"),
                "A mensagem deveria identificar a programação conflitante, senão não há como o usuário resolver.");
        });
    }

    private Task<ApiResposta> SalvarOrdemServicoAsync(long solicitacaoId, long veiculoId, string motoristaId) =>
        Api.EnviarAsync("POST", LogisticaRecursos.OrdemServico, new JsonObject
        {
            ["solicitacaoLogisticaId"] = solicitacaoId,
            ["veiculoId"] = veiculoId,
            ["motoristaId"] = motoristaId,
        });

    private async Task<(long VeiculoId, string MotoristaId)> ObterParDisponivelAsync()
    {
        var veiculos = (await Api.GetAsync("v1/ControleOperacao/Veiculo/ObterVeiculosAtivos"))?.AsArray() ?? [];
        var motoristas = (await Api.GetAsync("v1/ControleOperacao/Funcionario/ObterMotoristasAtivos"))?.AsArray() ?? [];

        if (veiculos.Count == 0 || motoristas.Count == 0)
        {
            Assert.Ignore("A empresa precisa de ao menos um veículo ativo e um motorista ativo para os cenários de OS.");
        }

        return (veiculos[0]!["id"]!.GetValue<long>(), motoristas[0]!["id"].ComoTexto()!);
    }

    private async Task<(JsonNode Viagem, long ItemId)> CriarViagemComItemAsync()
    {
        var apoio = await Logistica.CriarDadosApoioAsync(EmpresaCarga);
        var solicitacao = await Logistica.CriarSolicitacaoAsync(apoio, LogisticaDataFactory.Nome("Programacao"));
        var viagem = await Logistica.CriarViagemIniciadaAsync(solicitacao, LogisticaDataFactory.Nome("Viagem"));

        var itens = viagem["itens"]?.AsArray();
        if (itens is null || itens.Count == 0)
        {
            Assert.Ignore("A viagem criada não trouxe itens; sem item não há o que finalizar ou cancelar.");
        }

        return (viagem, itens![0].Id());
    }
}
