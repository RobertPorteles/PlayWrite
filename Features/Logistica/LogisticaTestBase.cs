using PlaywrightTests.Features.Logistica.Api;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Shared;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;

namespace PlaywrightTests.Features.Logistica;

/// <summary>
/// Base dos testes de logística: API autenticada para dados de apoio, cleanup no TearDown e login pela UI.
/// </summary>
public abstract class LogisticaTestBase : E2ETestBase
{
    public const string Categoria = "Logistica";

    protected ApiClient Api { get; private set; } = null!;

    protected LogisticaApi Logistica { get; private set; } = null!;

    protected CleanupTracker Cleanup { get; private set; } = null!;

    protected SessaoApp? Sessao { get; private set; }

    /// <summary>Empresa logada opera com Carga (tipoOperacaoId = 2); caso contrário, Passageiro (1).</summary>
    protected bool EmpresaCarga =>
        (Sessao?.TipoOperacaoId ?? throw new InvalidOperationException("Chame LoginNaUiAsync antes de usar EmpresaCarga.")) == 2;

    [SetUp]
    public async Task PrepararLogisticaAsync()
    {
        Cleanup = new CleanupTracker();
        Api = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        Logistica = new LogisticaApi(Api, Cleanup);
    }

    [TearDown]
    public async Task LimparLogisticaAsync()
    {
        if (Api is null)
        {
            return;
        }

        foreach (var falha in await Cleanup.LimparAsync(Api))
        {
            TestContext.Progress.WriteLine(falha);
        }

        await Api.DisposeAsync();
    }

    protected async Task<SessaoApp> LoginNaUiAsync()
    {
        var login = new LoginPage(Page);
        await login.LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

        Sessao = await login.LerSessaoAsync();
        Assert.That(Sessao.Token, Is.Not.Null.And.Not.Empty, "O front não gravou o token em localStorage.sessionApp.");
        Assert.That(Sessao.TipoOperacaoId, Is.EqualTo(1).Or.EqualTo(2),
            "A empresa logada não tem tipo de operação (Passageiro/Carga) definido; o wizard de Programação não funciona sem ele.");
        return Sessao;
    }
}
