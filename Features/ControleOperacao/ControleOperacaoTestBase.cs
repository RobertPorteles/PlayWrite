using System.Text.Json.Nodes;
using PlaywrightTests.Features.ControleOperacao.Data;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Shared;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.ControleOperacao;

/// <summary>
/// Base dos testes de controle de operação. Tem limpeza própria porque o id de Funcionario é Guid,
/// e o CleanupTracker da logística trabalha com ids long: aqui guarda-se o caminho completo do DELETE.
/// </summary>
public abstract class ControleOperacaoTestBase : E2ETestBase
{
    public const string Categoria = "ControleOperacao";

    private readonly List<string> _criados = [];

    protected ApiClient Api { get; private set; } = null!;

    [SetUp]
    public async Task PrepararControleOperacaoAsync()
    {
        Api = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
    }

    [TearDown]
    public async Task LimparControleOperacaoAsync()
    {
        if (Api is null)
        {
            return;
        }

        foreach (var caminho in _criados)
        {
            var resposta = await Api.DeleteAsync(caminho);
            if (!resposta.Sucesso && resposta.Status != 404)
            {
                TestContext.Progress.WriteLine($"Cleanup: DELETE {caminho} retornou {resposta.Status}: {resposta.Corpo}");
            }
        }

        _criados.Clear();
        await Api.DisposeAsync();
    }

    /// <summary>Registra o caminho (recurso/id) a excluir no TearDown.</summary>
    protected void RegistrarCriado(string recurso, string id) => _criados.Add($"{recurso}/{id}");

    /// <summary>Id da função considerada motorista, pelo mesmo critério do backend: cargo do parâmetro, senão a função ativa "MOTORISTA".</summary>
    protected async Task<long> ObterFuncaoMotoristaIdAsync()
    {
        var motoristas = (await Api.GetAsync(ControleOperacaoRecursos.MotoristasAtivos))?.AsArray();
        if (motoristas is null || motoristas.Count == 0)
        {
            Assert.Ignore("A empresa não tem nenhum motorista ativo; sem ele não dá para descobrir a função de motorista.");
        }

        var funcionario = await Api.GetAsync($"{ControleOperacaoRecursos.Funcionario}/{motoristas![0]!["id"].ComoTexto()}");
        var funcaoId = funcionario?["funcaoId"]?.GetValue<long>();

        return funcaoId ?? throw new InvalidOperationException("O motorista ativo não tem funcaoId preenchido.");
    }

    protected async Task<JsonNode> CriarFuncionarioAsync(string nome, long? funcaoId, bool ativo)
    {
        var corpo = new JsonObject
        {
            ["nome"] = nome,
            ["ativo"] = ativo,
            ["funcaoId"] = funcaoId,
            // FuncionarioService.Post recusa CPF/login repetidos comparando com ==, então null colide com
            // qualquer funcionário já cadastrado sem esses campos.
            ["cpf"] = LogisticaDataFactory.GerarCpf(),
            ["login"] = nome,
            // Sem senha o POST estoura 500 ("Value cannot be null. (Parameter 'password')") em vez de 400.
            ["senha"] = "E2E@Teste1",
        };

        var criado = (await Api.PostAsync(ControleOperacaoRecursos.Funcionario, corpo))
            ?? throw new InvalidOperationException($"POST {ControleOperacaoRecursos.Funcionario} não devolveu corpo.");

        RegistrarCriado(ControleOperacaoRecursos.Funcionario, criado["id"].ComoTexto()!);
        return criado;
    }

    protected static string Nome(string prefixo) =>
        $"E2E-{prefixo}-{DateTime.Now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";
}
