using Microsoft.Playwright;
using NUnit.Framework.Interfaces;
using PlaywrightTests.Features.Siera.Data;
using PlaywrightTests.Features.Siera.Pages;
using PlaywrightTests.Features.Siera.Stub;
using PlaywrightTests.Shared;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using PlaywrightTests.Shared.Rede;

namespace PlaywrightTests.Features.Siera;

/// <summary>
/// Base dos testes da Siera: navegador no front do <see cref="SieraAmbiente"/>, stub zerado a cada teste e rede do
/// navegador monitorada desde o início. O login é feito só por quem precisa, para que os testes que não dependem
/// de usuário rodem sem E2E_LOGIN.
/// </summary>
public abstract class SieraTestBase : E2ETestBase
{
    public const string Categoria = "Siera";

    private ApiClient? _api;

    protected static StubIntegracoesSiera Stub => SieraAmbiente.Stub;

    protected MonitorRede Rede { get; private set; } = null!;

    public override BrowserNewContextOptions ContextOptions()
    {
        var opcoes = base.ContextOptions();
        opcoes.BaseURL = SieraAmbiente.FrontUrl.AbsoluteUri;
        return opcoes;
    }

    [SetUp]
    public void PrepararSiera()
    {
        Stub.Resetar();

        Rede = new MonitorRede(Context, SieraAmbiente.ApiUrl, SieraAmbiente.FrontUrl);
        foreach (var porta in SieraAmbiente.PortasIntegracoes)
        {
            Rede.ProibirOrigem(new Uri($"http://localhost:{porta}/"));
        }

        Rede.ProibirOrigem(Stub.Url);
        foreach (var host in SieraAmbiente.HostsSiera)
        {
            Rede.ProibirHost(host);
        }
    }

    [TearDown]
    public async Task LimparSieraAsync()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            TestContext.Progress.WriteLine($"Rede — hosts externos: {string.Join(", ", Rede.HostsExternos())}");
            foreach (var violacao in Rede.Violacoes())
            {
                TestContext.Progress.WriteLine($"Rede — violação: {violacao}");
            }

            TestContext.Progress.WriteLine($"Stub — recebido: {string.Join(", ", Stub.Recebidas().Select(r => r.Caminho + r.Query))}");
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
            _api = null;
        }
    }

    /// <summary>API autenticada como o usuário dos testes (master), na CCO.Api do ambiente da Siera.</summary>
    protected async Task<ApiClient> ApiMasterAsync() =>
        _api ??= await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa,
            SieraAmbiente.ApiUrl.AbsoluteUri);

    /// <summary>
    /// Programa o stub com a frota da empresa logada. A CCO.Api recorta a frota pelo <c>Empresa.IntegracaoCodigo</c>
    /// e recusa a consulta sem ele, então nesse caso o teste é ignorado com a instrução do setup.
    /// </summary>
    protected async Task<int> PrepararCenarioAsync()
    {
        var api = await ApiMasterAsync();
        var empresaId = api.EmpresaId ?? throw new InvalidOperationException("O RetrieveSessionApp não devolveu o id da empresa.");
        var empresa = await api.GetAsync($"v1/empresa/{empresaId}");
        var codigo = empresa?["integracaoCodigo"].ComoInt();

        if (codigo is null)
        {
            Assert.Ignore(
                $"A empresa logada ({empresaId}) não tem IntegracaoCodigo, e a fachada da Siera recusa a consulta sem ele. " +
                "Setup único local (veja Features/Siera/README.md): " +
                $"update \"Base\".\"Empresa\" set \"IntegracaoCodigo\" = 990001 where \"Id\" = '{empresaId}';");
        }

        Stub.Resetar(SieraCenario.Padrao(codigo!.Value, Stub.Url));
        return codigo.Value;
    }

    /// <summary>Login pela interface e abertura de /siera pela URL (a tela não tem item de menu).</summary>
    protected async Task<SieraPage> AbrirTelaSieraAsync()
    {
        await new LoginPage(Page).LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

        // O que está em teste é o tráfego da tela da Siera, não o do login e da /home.
        Rede.Limpar();

        var tela = new SieraPage(Page);
        await tela.AbrirAsync();
        return tela;
    }

    /// <summary>Contexto HTTP cru na CCO.Api do ambiente, para conferir status e headers sem o tratamento do ApiClient.</summary>
    protected Task<IAPIRequestContext> NovoContextoApiAsync(string? token = null, IDictionary<string, string>? headers = null)
    {
        var todos = new Dictionary<string, string>(headers ?? new Dictionary<string, string>()) { ["Accept-Language"] = "pt-BR" };
        if (token is not null)
        {
            todos["Authorization"] = $"Bearer {token}";
        }

        return Playwright.APIRequest.NewContextAsync(new() { BaseURL = SieraAmbiente.ApiUrl.AbsoluteUri, ExtraHTTPHeaders = todos });
    }

    protected static async Task EsperarAteAsync(Func<bool> condicao, string mensagem, int segundos = 10)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
            {
                Assert.Fail(mensagem);
            }

            await Task.Delay(100);
        }
    }
}
