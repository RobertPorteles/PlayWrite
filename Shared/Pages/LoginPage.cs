using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Api;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Shared.Pages;

/// <summary>
/// Tela de login (src/views/shared/login/Login.vue), incluindo a seleção de empresa.
/// </summary>
public sealed partial class LoginPage(IPage page)
{
    public ILocator CampoLogin => page.GetByLabel("Login", new() { Exact = true });

    public ILocator CampoSenha => page.GetByLabel("Senha", new() { Exact = true });

    public ILocator BotaoEntrar => page.GetByRole(AriaRole.Button, new() { Name = "Entrar", Exact = true });

    public ILocator CardsEmpresa => page.Locator(".card-container");

    public ILocator BotaoMenu => page.Locator(".v-app-bar-nav-icon");

    public async Task AbrirAsync()
    {
        await page.GotoAsync("/login");
        await Expect(CampoLogin).ToBeVisibleAsync();
    }

    public async Task PreencherEEntrarAsync(string login, string senha)
    {
        await CampoLogin.FillAsync(login);
        await CampoSenha.FillAsync(senha);
        await BotaoEntrar.ClickAsync();
    }

    /// <summary>Faz o login completo e espera a aplicação carregar (/home ou /despachante).</summary>
    public async Task LoginAsync(string login, string senha, string? empresa)
    {
        await AbrirAsync();
        await PreencherEEntrarAsync(login, senha);

        await Expect(CardsEmpresa.First.Or(BotaoMenu)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await CardsEmpresa.CountAsync() > 0)
        {
            await SelecionarEmpresaAsync(empresa);
        }

        await Expect(page).ToHaveURLAsync(AppCarregada(), new() { Timeout = 30_000 });
        await Expect(BotaoMenu).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    public async Task SelecionarEmpresaAsync(string? empresa)
    {
        if (empresa is null)
        {
            if (await CardsEmpresa.CountAsync() != 1)
            {
                var nomes = await CardsEmpresa.Locator(".text-description").AllInnerTextsAsync();
                throw new InvalidOperationException(
                    $"Defina E2E_EMPRESA com uma das empresas: {string.Join(" | ", nomes)}");
            }

            await CardsEmpresa.First.ClickAsync();
            return;
        }

        await page.GetByLabel("Empresa", new() { Exact = true }).FillAsync(empresa);
        var card = CardsEmpresa.Filter(new()
        {
            Has = page.Locator(".text-description", new() { HasTextRegex = TextoExato(empresa) }),
        });
        await card.First.ClickAsync();
    }

    /// <summary>Lê a sessão gravada pelo front em localStorage.sessionApp.</summary>
    public async Task<SessaoApp> LerSessaoAsync()
    {
        var bruto = await page.EvaluateAsync<string?>("() => localStorage.getItem('sessionApp')");
        var sessao = string.IsNullOrWhiteSpace(bruto) ? null : JsonNode.Parse(bruto);
        var dados = sessao?["dados"];

        return new SessaoApp(
            Token: sessao?["token"].ComoTexto(),
            TipoOperacaoId: dados?["empresa"]?["tipoOperacaoId"].ComoInt(),
            UsuarioMaster: dados?["usuarioMaster"].ComoBool() ?? false,
            EmpresaNome: dados?["empresa"]?["nome"].ComoTexto());
    }

    public static Regex AppCarregada() => AppCarregadaRegex();

    private static Regex TextoExato(string texto) =>
        new($"^\\s*{Regex.Escape(texto)}\\s*$", RegexOptions.IgnoreCase);

    [GeneratedRegex("/(home|despachante)")]
    private static partial Regex AppCarregadaRegex();
}

public sealed record SessaoApp(string? Token, int? TipoOperacaoId, bool UsuarioMaster, string? EmpresaNome);
