using System.Text.Json.Nodes;
using PlaywrightTests.Features.ControleOperacao.Data;
using PlaywrightTests.Shared.Api;

namespace PlaywrightTests.Features.ControleOperacao.Tests;

/// <summary>
/// Veículo ou motorista já ocupado por outro vínculo aberto. O backend recusa; o que estes testes
/// guardam é a mensagem dizer COM QUEM está ocupado — sem isso o usuário não sabe qual vínculo encerrar.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class VinculoOcupadoTests : ControleOperacaoTestBase
{
    [Test]
    public async Task VincularVeiculoJaEmUsoDeveRecusarEIdentificarOMotoristaAtual()
    {
        var aberto = await ObterVinculoAbertoAsync();
        var novoMotorista = await CriarFuncionarioAsync(Nome("Motorista"), await ObterFuncaoMotoristaIdAsync(), ativo: true);

        var resposta = await VincularAsync(novoMotorista["id"].ComoTexto()!, aberto.VeiculoId);

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(400), $"Esperado 400, veio {resposta.Status}: {resposta.Corpo}");
            Assert.That(resposta.Corpo, Does.Contain("vínculo").IgnoreCase.Or.Contains("vinculo").IgnoreCase,
                "A mensagem precisa falar do vínculo aberto do veículo.");
            Assert.That(resposta.Corpo, Does.Contain(aberto.FuncionarioNome),
                $"A mensagem deveria nomear o motorista que está com o veículo ({aberto.FuncionarioNome}), " +
                "senão o usuário não sabe qual vínculo encerrar.");
        });
    }

    [Test]
    public async Task VincularMotoristaJaVinculadoDeveRecusarEIdentificarOVeiculoAtual()
    {
        var aberto = await ObterVinculoAbertoAsync();
        var outroVeiculoId = await ObterVeiculoLivreAsync(aberto.VeiculoId);

        var resposta = await VincularAsync(aberto.FuncionarioId, outroVeiculoId);

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(400), $"Esperado 400, veio {resposta.Status}: {resposta.Corpo}");
            Assert.That(resposta.Corpo, Does.Contain(aberto.FuncionarioNome),
                "A mensagem precisa nomear o motorista que já está vinculado.");
        });
    }

    private Task<ApiResposta> VincularAsync(string funcionarioId, long veiculoId) =>
        Api.EnviarAsync("POST", ControleOperacaoRecursos.Vincular, new JsonObject
        {
            ["funcionarioId"] = funcionarioId,
            ["veiculoId"] = veiculoId,
            ["dataHoraVinculo"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        });

    private async Task<(string FuncionarioId, string FuncionarioNome, long VeiculoId)> ObterVinculoAbertoAsync()
    {
        var pagina = await Api.PostAsync(ControleOperacaoRecursos.ListarVinculos, new JsonObject
        {
            ["page"] = 1,
            ["itemsPerPage"] = 50,
            ["situacao"] = ControleOperacaoRecursos.SituacaoVinculado,
        });

        var abertos = pagina?["items"]?.AsArray() ?? [];
        if (abertos.Count == 0)
        {
            Assert.Ignore("A empresa não tem nenhum vínculo aberto; sem ele não há conflito a exercitar.");
        }

        var vinculo = abertos[0]!;
        return (
            vinculo["funcionarioId"].ComoTexto()!,
            vinculo["funcionarioNome"].ComoTexto() ?? string.Empty,
            vinculo["veiculoId"]!.GetValue<long>());
    }

    private async Task<long> ObterVeiculoLivreAsync(long veiculoOcupadoId)
    {
        var veiculos = (await Api.GetAsync(ControleOperacaoRecursos.VeiculosAtivos))?.AsArray() ?? [];
        var livre = veiculos
            .Select(x => x!["id"]!.GetValue<long>())
            .FirstOrDefault(id => id != veiculoOcupadoId);

        if (livre == 0)
        {
            Assert.Ignore("Só existe um veículo ativo; sem um segundo veículo não dá para isolar o conflito do motorista.");
        }

        return livre;
    }
}
