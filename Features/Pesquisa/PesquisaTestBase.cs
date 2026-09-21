using System.Text.Json.Nodes;
using PlaywrightTests.Features.Pesquisa.Stub;
using PlaywrightTests.Shared;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;

namespace PlaywrightTests.Features.Pesquisa;

/// <summary>
/// Base dos testes de "Pesquisa rápida": API autenticada para semear dados que faltam, exclusão de tudo o que foi
/// criado no TearDown (em ordem inversa, ids long/int/Guid como texto), registro de respostas de erro da CCO.Api e de
/// exceções JavaScript da página, e
/// bloqueio das chamadas legadas que sairiam da máquina.
/// </summary>
public abstract class PesquisaTestBase : E2ETestBase
{
    public const string Categoria = "Pesquisa";

    private readonly List<string> _criados = [];

    private readonly List<string> _erros = [];

    public ApiClient Api { get; private set; } = null!;

    protected StubNavegador Stub { get; private set; } = null!;

    /// <summary>Respostas 4xx/5xx da CCO.Api e exceções JavaScript não tratadas vistas durante o teste.</summary>
    protected IReadOnlyList<string> Erros => _erros;

    [SetUp]
    public async Task PrepararPesquisaAsync()
    {
        _erros.Clear();
        Api = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        Stub = await StubNavegador.InstalarAsync(Page);
        Page.Response += (_, resposta) =>
        {
            if (resposta.Status >= 400 && resposta.Url.StartsWith(E2ESettings.ApiUrl, StringComparison.OrdinalIgnoreCase))
            {
                _erros.Add($"{resposta.Request.Method} {Uri.UnescapeDataString(resposta.Url)} → HTTP {resposta.Status}");
            }
        };
        Page.PageError += (_, erro) => _erros.Add($"JavaScript: {erro}");
    }

    [TearDown]
    public async Task LimparPesquisaAsync()
    {
        if (Api is null)
        {
            return;
        }

        foreach (var caminho in Enumerable.Reverse(_criados))
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

    public static string NomeE2E(string entidade) => $"E2E-PESQ-{entidade}-{DateTime.Now:yyyyMMddHHmmss}";

    /// <summary>POST no recurso e registro do DELETE para o TearDown.</summary>
    public async Task<JsonNode> CriarAsync(string recurso, JsonNode corpo)
    {
        var criado = await Api.PostAsync(recurso, corpo)
            ?? throw new InvalidOperationException($"POST {recurso} não devolveu o registro criado.");
        _criados.Add($"{recurso}/{criado["id"].ComoTexto()}");
        return criado;
    }

    /// <summary>Primeiro registro do recurso (GET paginado), ou null se a lista estiver vazia.</summary>
    public async Task<JsonNode?> PrimeiroAsync(string recurso) =>
        (await Api.GetAsync($"{recurso}?$Top=1&$Skip=0")).Registros().FirstOrDefault();

    protected async Task EntrarAsync()
    {
        await new LoginPage(Page).LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
    }
}
