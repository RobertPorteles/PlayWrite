using PlaywrightTests.Features.ControleOperacao.Data;
using PlaywrightTests.Shared.Api;

namespace PlaywrightTests.Features.ControleOperacao.Tests;

/// <summary>
/// Contrato entre o que as telas oferecem e o que o save aceita: toda opção listada precisa ser salvável.
/// Quando os dois lados divergem, o usuário escolhe uma opção válida na tela e recebe um 400 técnico.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class ContratoOpcoesMotoristaTests : ControleOperacaoTestBase
{
    /// <summary>
    /// AutoCompleteMotorista filtra a função mas não o Ativo (FuncionarioRepository.AutoCompleteMotorista),
    /// enquanto Vincular recusa motorista inativo (VinculoVeiculoService). As telas de Alocação e de
    /// Deslocamento ainda usam esse autocomplete, então oferecem alguém que o save rejeita.
    /// </summary>
    [Test]
    public async Task AutoCompleteDeMotoristaNaoDeveOferecerMotoristaInativo()
    {
        var funcaoMotoristaId = await ObterFuncaoMotoristaIdAsync();
        var nome = Nome("MotoristaInativo");
        var inativo = await CriarFuncionarioAsync(nome, funcaoMotoristaId, ativo: false);

        var oferecidos = (await Api.GetAsync($"{ControleOperacaoRecursos.AutoCompleteMotorista}?q={Uri.EscapeDataString(nome)}"))?.AsArray();
        var idsOferecidos = (oferecidos ?? []).Select(x => x!["id"].ComoTexto()).ToList();

        Assert.That(idsOferecidos, Does.Not.Contain(inativo["id"].ComoTexto()),
            "O autocomplete ofereceu um motorista inativo. Quem escolher esse nome recebe 400 " +
            "(\"O motorista X está inativo.\") só depois de salvar.");
    }

    /// <summary>Contrato geral: o autocomplete não pode oferecer ninguém fora da lista estrita de motoristas ativos.</summary>
    [Test]
    public async Task TodaOpcaoDoAutoCompleteDeveEstarEntreOsMotoristasAtivos()
    {
        var ativos = (await Api.GetAsync(ControleOperacaoRecursos.MotoristasAtivos))?.AsArray() ?? [];
        if (ativos.Count == 0)
        {
            Assert.Ignore("A empresa não tem motorista ativo; nada a comparar.");
        }

        var idsAtivos = ativos.Select(x => x!["id"].ComoTexto()).ToHashSet();

        // O endpoint exige q preenchido, então buscamos pelo início do nome de um motorista que existe.
        var termo = new string((ativos[0]!["nome"].ComoTexto() ?? "a").Take(3).ToArray());
        var oferecidos = (await Api.GetAsync($"{ControleOperacaoRecursos.AutoCompleteMotorista}?q={Uri.EscapeDataString(termo)}"))?.AsArray() ?? [];
        if (oferecidos.Count == 0)
        {
            Assert.Ignore($"O autocomplete não devolveu opções para \"{termo}\"; nada a comparar.");
        }

        var forasteiros = oferecidos
            .Select(x => new { Id = x!["id"].ComoTexto(), Nome = x["nome"].ComoTexto() })
            .Where(x => !idsAtivos.Contains(x.Id))
            .ToList();

        Assert.That(forasteiros, Is.Empty,
            $"O autocomplete oferece quem não está em ObterMotoristasAtivos: {string.Join(", ", forasteiros.Select(f => f.Nome))}. " +
            "As duas listas precisam usar o mesmo critério, senão a tela oferece o que o save recusa.");
    }

    /// <summary>
    /// Contrato do backend: funcionário sem função de motorista tem que ser recusado com mensagem clara,
    /// e não com 500 ou com um vínculo criado indevidamente.
    /// </summary>
    [Test]
    public async Task VincularDeveRecusarFuncionarioSemFuncaoDeMotorista()
    {
        var semFuncao = await CriarFuncionarioAsync(Nome("SemFuncao"), funcaoId: null, ativo: true);

        var resposta = await Api.EnviarAsync("POST", ControleOperacaoRecursos.Vincular, new System.Text.Json.Nodes.JsonObject
        {
            ["funcionarioId"] = semFuncao["id"].ComoTexto(),
            ["veiculoId"] = 1,
            ["dataHoraVinculo"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(400), $"Esperado 400, veio {resposta.Status}: {resposta.Corpo}");
            Assert.That(resposta.Corpo, Does.Contain("motorista"),
                "A mensagem precisa dizer ao usuário o que fazer, não só que a requisição é inválida.");
        });
    }
}
