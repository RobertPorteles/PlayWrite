using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Shared;

/// <summary>
/// Base de todos os testes E2E: contexto do navegador em pt-BR apontando para o front e trace salvo em falhas.
/// </summary>
public abstract class E2ETestBase : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = E2ESettings.FrontUrl,
        Locale = "pt-BR",
        TimezoneId = "America/Sao_Paulo",
        ViewportSize = new() { Width = 1600, Height = 900 },
        IgnoreHTTPSErrors = true,
    };

    /// <summary>
    /// HEADED=1 mostra o navegador (por padrão o Playwright.NUnit roda headless); SLOWMO_MS atrasa cada ação
    /// do Playwright em milissegundos, para acompanhar visualmente. Para rodar de verdade passo a passo com
    /// "next", use PWDEBUG=1 (abre o Playwright Inspector) em vez destas variáveis.
    /// </summary>
    public override Task<BrowserTypeLaunchOptions?> LaunchOptionsAsync()
    {
        var opcoes = new BrowserTypeLaunchOptions { Headless = Environment.GetEnvironmentVariable("HEADED") != "1" };

        if (float.TryParse(Environment.GetEnvironmentVariable("SLOWMO_MS"), out var slowMo))
        {
            opcoes.SlowMo = slowMo;
        }

        return Task.FromResult<BrowserTypeLaunchOptions?>(opcoes);
    }

    [SetUp]
    public async Task IniciarTraceAsync()
    {
        await Context.Tracing.StartAsync(new()
        {
            Title = TestContext.CurrentContext.Test.FullName,
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
    }

    [TearDown]
    public async Task SalvarTraceAsync()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed)
        {
            await Context.Tracing.StopAsync();
            return;
        }

        var pasta = Path.Combine(TestContext.CurrentContext.WorkDirectory, "playwright-traces");
        Directory.CreateDirectory(pasta);
        var nome = string.Concat(TestContext.CurrentContext.Test.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var caminho = Path.Combine(pasta, $"{nome}.zip");

        await Context.Tracing.StopAsync(new() { Path = caminho });
        TestContext.AddTestAttachment(caminho, "Playwright trace");
        TestContext.Progress.WriteLine($"Trace salvo em: {caminho}");
    }
}
