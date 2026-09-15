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
    /// Contrato do endpoint estrito em si: precisa refletir Funcionario.Ativo, não só a função de motorista.
    /// Os outros testes desta classe comparam o autocomplete contra esta lista assumindo que ela já está
    /// correta — sem este teste, um bug aqui passaria despercebido.
    /// </summary>
    [Test]
    public async Task ObterMotoristasAtivosDeveFiltrarPorFuncionarioAtivo()
    {
        var funcaoMotoristaId = await ObterFuncaoMotoristaIdAsync();
        var ativo = await CriarFuncionarioAsync(Nome("MotoristaAtivo"), funcaoMotoristaId, ativo: true);
        var inativo = await CriarFuncionarioAsync(Nome("MotoristaInativo"), funcaoMotoristaId, ativo: false);

        var motoristas = (await Api.GetAsync(ControleOperacaoRecursos.MotoristasAtivos))?.AsArray() ?? [];
        var ids = motoristas.Select(x => x!["id"].ComoTexto()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(ativo["id"].ComoTexto()),
                "O motorista ativo recém-criado deveria aparecer em ObterMotoristasAtivos.");
            Assert.That(ids, Does.Not.Contain(inativo["id"].ComoTexto()),
                "ObterMotoristasAtivos ofereceu um motorista inativo — o endpoint não está filtrando por Funcionario.Ativo.");
        });
    }

    /// <summary>
    /// Com somenteAtivos=true (diálogo de alocar veículo) o autocomplete não pode oferecer motorista inativo:
    /// o Alocar não valida Ativo, então a alocação seria gravada com alguém desligado.
    /// </summary>
    [Test]
    public async Task AutoCompleteSomenteAtivosNaoDeveOferecerMotoristaInativo()
    {
        var nome = Nome("MotoristaInativo");
        var inativo = await CriarFuncionarioAsync(nome, await ObterFuncaoMotoristaIdAsync(), ativo: false);

        var ids = await BuscarIdsNoAutoCompleteAsync(nome, somenteAtivos: true);

        Assert.That(ids, Does.Not.Contain(inativo["id"].ComoTexto()),
            "O autocomplete com somenteAtivos=true ofereceu um motorista inativo.");
    }

    /// <summary>
    /// Sem o parâmetro vale o contrato antigo: os filtros das listagens de alocação e de deslocamento
    /// precisam achar o histórico de motoristas que hoje estão inativos.
    /// </summary>
    [Test]
    public async Task AutoCompleteSemSomenteAtivosDeveContinuarOferecendoMotoristaInativo()
    {
        var nome = Nome("MotoristaInativo");
        var inativo = await CriarFuncionarioAsync(nome, await ObterFuncaoMotoristaIdAsync(), ativo: false);

        var ids = await BuscarIdsNoAutoCompleteAsync(nome, somenteAtivos: false);

        Assert.That(ids, Does.Contain(inativo["id"].ComoTexto()),
            "Sem somenteAtivos o autocomplete deixou de oferecer motorista inativo, e os filtros de listagem perdem o histórico.");
    }

    /// <summary>Com somenteAtivos=true, o autocomplete não pode oferecer ninguém fora da lista estrita de motoristas ativos.</summary>
    [Test]
    public async Task TodaOpcaoDoAutoCompleteSomenteAtivosDeveEstarEntreOsMotoristasAtivos()
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

    /// <summary>Contrato do backend: o Alocar precisa recusar motorista inativo, como o Vincular já faz.</summary>
    [Test]
    public async Task AlocarDeveRecusarMotoristaInativo()
    {
        var inativo = await CriarFuncionarioAsync(Nome("MotoristaInativo"), await ObterFuncaoMotoristaIdAsync(), ativo: false);

        // Veículo inexistente de propósito: se a validação sumir, vem "Veiculo nao encontrado" em vez de gravar uma alocação.
        var resposta = await Api.EnviarAsync("POST", ControleOperacaoRecursos.Alocar, new System.Text.Json.Nodes.JsonObject
        {
            ["funcionarioId"] = inativo["id"].ComoTexto(),
            ["veiculoId"] = long.MaxValue,
            ["dataHoraAlocacao"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
        });

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.EqualTo(400), $"Esperado 400, veio {resposta.Status}: {resposta.Corpo}");
            Assert.That(resposta.Corpo, Does.Contain("está inativo"),
                "O Alocar precisa recusar o motorista por estar inativo, antes de olhar veículo e vínculos.");
        });
    }

    /// <summary>
    /// O cadastro de viagem escolhe por este autocomplete o motorista que vai gravar, então inativo não pode aparecer.
    /// O ativo criado junto garante que o teste não passa só porque a busca voltou vazia.
    /// </summary>
    [Test]
    public async Task AutoCompleteFuncionarioDisponivelNaoDeveOferecerMotoristaInativo()
    {
        var funcaoMotoristaId = await ObterFuncaoMotoristaIdAsync();
        var termo = Nome("Disponivel");
        var ativo = await CriarFuncionarioAsync($"{termo}-Ativo", funcaoMotoristaId, ativo: true);
        var inativo = await CriarFuncionarioAsync($"{termo}-Inativo", funcaoMotoristaId, ativo: false);

        var inicio = DateTime.Today.AddDays(1).AddHours(8);
        var caminho = $"{ControleOperacaoRecursos.AutoCompleteFuncionarioDisponivel}?q={Uri.EscapeDataString(termo)}" +
            $"&inicio={inicio:yyyy-MM-ddTHH:mm:ss}&fim={inicio.AddHours(1):yyyy-MM-ddTHH:mm:ss}";
        var oferecidos = (await Api.GetAsync(caminho))?.AsArray() ?? [];
        var ids = oferecidos.Select(x => x!["id"].ComoTexto()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Does.Contain(ativo["id"].ComoTexto()), "O motorista ativo deveria ser oferecido no cadastro de viagem.");
            Assert.That(ids, Does.Not.Contain(inativo["id"].ComoTexto()), "O cadastro de viagem ofereceu um motorista inativo.");
        });
    }

    private async Task<List<string?>> BuscarIdsNoAutoCompleteAsync(string termo, bool somenteAtivos)
    {
        var caminho = $"{ControleOperacaoRecursos.AutoCompleteMotorista}?q={Uri.EscapeDataString(termo)}";
        if (somenteAtivos)
        {
            caminho += "&somenteAtivos=true";
        }

        var oferecidos = (await Api.GetAsync(caminho))?.AsArray() ?? [];
        return oferecidos.Select(x => x!["id"].ComoTexto()).ToList();
    }
}
