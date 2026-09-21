using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Shared;

/// <summary>
/// Base de todos os testes E2E: contexto do navegador em pt-BR apontando para o front, trace salvo em falhas e, com
/// VIDEO_DIR definido (VIDEO=1 no testes.sh), um vídeo por teste.
/// </summary>
public abstract class E2ETestBase : PageTest
{
    /// <summary>Pasta dos vídeos; vazia = não grava.</summary>
    private static string? PastaVideo => E2ESettings.Ler("VIDEO_DIR");

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = E2ESettings.FrontUrl,
        Locale = "pt-BR",
        TimezoneId = "America/Sao_Paulo",
        ViewportSize = new() { Width = 1600, Height = 900 },
        IgnoreHTTPSErrors = true,
        RecordVideoDir = PastaVideo,
        RecordVideoSize = PastaVideo is null ? null : new() { Width = 1600, Height = 900 },
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
        var nome = string.Concat(TestContext.CurrentContext.Test.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed)
        {
            await Context.Tracing.StopAsync();
        }
        else
        {
            var pasta = Path.Combine(TestContext.CurrentContext.WorkDirectory, "playwright-traces");
            Directory.CreateDirectory(pasta);
            var caminho = Path.Combine(pasta, $"{nome}.zip");

            await Context.Tracing.StopAsync(new() { Path = caminho });
            TestContext.AddTestAttachment(caminho, "Playwright trace");
            TestContext.Progress.WriteLine($"Trace salvo em: {caminho}");
        }

        await SalvarVideoAsync(nome);
    }

    /// <summary>O Playwright só fecha o arquivo do vídeo quando o contexto fecha: fecha aqui para salvar com o nome do teste.</summary>
    private async Task SalvarVideoAsync(string nome)
    {
        if (PastaVideo is null || Page.Video is null)
        {
            return;
        }

        var gravado = await Page.Video.PathAsync();
        await Context.CloseAsync();

        var destino = Path.Combine(PastaVideo, $"{nome}.webm");
        File.Move(gravado, destino, overwrite: true);
        TestContext.AddTestAttachment(destino, "Vídeo");
        TestContext.Progress.WriteLine($"Vídeo salvo em: {destino}");
    }
}
