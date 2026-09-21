using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Atendimento.Tests;

/// <summary>
/// Contrato do acesso de atendimento na CCO.Api local, sem navegador:
/// - POST v1/autenticador/atendimento (anônimo, rail "Atendimento" da tela de login): exige login e senha;
/// - POST v1/autenticador/EntrarAtendimento (menu do avatar): exige token e usa o usuário dele;
/// - POST v1/Autenticador/login de um usuário com o perfil de atendimento: já devolve o token do atendimento;
/// - o que esse token libera no CCOMobilityProvider.
/// Nunca manda login inexistente ao v1/Autenticador/login: ele cairia no WS externo do permissionário.
/// Pré-requisitos: CCO.Api local, E2E_LOGIN/E2E_SENHA de um usuário master; usuários de <see cref="UsuariosAtendimento"/>.
/// </summary>
[TestFixture]
[Category(AtendimentoTestBase.Categoria)]
public class ContratoLoginAtendimentoTests : PlaywrightTest
{
    private const string LoginAtendimento = "v1/autenticador/atendimento";

    private const string EntrarAtendimento = "v1/autenticador/EntrarAtendimento";

    private IAPIRequestContext _api = null!;

    [SetUp]
    public async Task CriarContextoAsync() =>
        _api = await Playwright.APIRequest.NewContextAsync(new() { BaseURL = E2ESettings.ApiUrl });

    [TearDown]
    public async Task DescartarContextoAsync() => await _api.DisposeAsync();

    [Test]
    public async Task LoginAtendimentoSemSenhaNaoDeveEmitirToken()
    {
        var resposta = await EnviarAsync("POST", LoginAtendimento, new JsonObject { ["login"] = E2ESettings.Login });

        AssertSemToken(resposta, "Anônimo e sem senha, o POST de atendimento emitiu um JWT.");
    }

    [Test]
    public async Task LoginAtendimentoComSenhaErradaNaoDeveEmitirToken()
    {
        var resposta = await EnviarAsync("POST", LoginAtendimento, new JsonObject
        {
            ["login"] = E2ESettings.Login,
            ["senha"] = "senha-errada-e2e-" + Guid.NewGuid().ToString("N"),
        });

        AssertSemToken(resposta, "Com senha errada, o POST de atendimento emitiu um JWT.");
    }

    [Test]
    public async Task LoginAtendimentoComLoginInexistenteDeveDar4xx()
    {
        var resposta = await EnviarAsync("POST", LoginAtendimento,
            new JsonObject { ["login"] = $"nao-existe-{Guid.NewGuid():N}@e2e.local", ["senha"] = "x" });

        Assert.That(resposta.Status, Is.InRange(400, 404), $"Login inexistente deveria dar 400/404. Corpo: {resposta.Corpo}");
    }

    [Test]
    public async Task LoginAtendimentoComSenhaCertaDeveMarcarAcessoDeAtendimento()
    {
        var login = await EnviarAsync("POST", LoginAtendimento,
            new JsonObject { ["login"] = E2ESettings.Login, ["senha"] = E2ESettings.Senha });
        var sessao = await SessaoAtendimentoAsync(TokenDe(login));

        Assert.Multiple(() =>
        {
            Assert.That(sessao?["acesso"]?["atendimento"].ComoBool(), Is.True);
            Assert.That(sessao?["acesso"]?["ocorrencia"].ComoBool(), Is.False);
            Assert.That(sessao?["usuarioMaster"].ComoBool(), Is.True, "E2E_LOGIN deveria ser master.");
            // Master sem perfil de atendimento: o token sai sem empresa e o nome vira o tipo do login.
            Assert.That(sessao?["empresa"]?["id"].ComoTexto(), Is.EqualTo(Guid.Empty.ToString()));
            Assert.That(sessao?["empresa"]?["nome"].ComoTexto(), Is.EqualTo("Atendimento"));
        });
    }

    [Test]
    public async Task EntrarAtendimentoSemTokenDeveDar401()
    {
        var resposta = await EnviarAsync("POST", EntrarAtendimento);

        AssertSemToken(resposta, "EntrarAtendimento sem token emitiu um JWT.");
        Assert.That(resposta.Status, Is.EqualTo(401));
    }

    [Test]
    public async Task EntrarAtendimentoComTokenDeveEmitirTokenDeAtendimentoDoMesmoUsuario()
    {
        await using var master = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

        var resposta = await master.EnviarAsync("POST", EntrarAtendimento);
        var sessao = await SessaoAtendimentoAsync(TokenDe(resposta));

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Json?["atendimento"].ComoBool(), Is.True);
            Assert.That(sessao?["acesso"]?["atendimento"].ComoBool(), Is.True);
            Assert.That(sessao?["usuarioLogin"].ComoTexto(), Is.EqualTo(E2ESettings.Login).IgnoreCase);
        });
    }

    [TestCase(nameof(UsuariosAtendimento.SoAtendimento))]
    [TestCase(nameof(UsuariosAtendimento.Misto))]
    public async Task LoginDoPerfilAtendimentoDeveEntrarDiretoNoAtendimento(string usuario)
    {
        var login = usuario == nameof(UsuariosAtendimento.Misto) ? UsuariosAtendimento.Misto : UsuariosAtendimento.SoAtendimento;

        var resposta = await EnviarAsync("POST", "v1/Autenticador/login",
            new JsonObject { ["login"] = login, ["senha"] = UsuariosAtendimento.Senha });
        var sessao = await SessaoAtendimentoAsync(TokenDe(resposta));

        Assert.Multiple(() =>
        {
            Assert.That(resposta.Json?["atendimento"].ComoBool(), Is.True, "O login deveria já ser o do atendimento.");
            Assert.That(resposta.Json?["empresas"].Registros(), Is.Empty, "Não deveria pedir para escolher empresa.");
            Assert.That(sessao?["usuarioMaster"].ComoBool(), Is.False);
            Assert.That(sessao?["acesso"]?["atendimento"].ComoBool(), Is.True);
            // O vínculo sem empresa tem preferência, inclusive para o usuário misto.
            Assert.That(sessao?["empresa"]?["nome"].ComoTexto(), Is.EqualTo("Atendimento"));
            Assert.That(sessao?["funcionalidades"].Registros().Select(f => f?["funcionalidade"].ComoInt()),
                Does.Contain(811000).And.Contain(811015));
        });
    }

    [Test]
    public async Task TokenDoPerfilAtendimentoDeveAcessarModulosEAHome()
    {
        var token = TokenDe(await EnviarAsync("POST", "v1/Autenticador/login",
            new JsonObject { ["login"] = UsuariosAtendimento.SoAtendimento, ["senha"] = UsuariosAtendimento.Senha }));
        var usuarioId = (await SessaoAtendimentoAsync(token))?["usuarioId"].ComoTexto();

        var associados = await EnviarAsync("GET", "v1/associado?$Top=1&$Skip=0", token: token);
        var home = await EnviarAsync("GET", $"v1/usuario/UsuarioEhPermissionario?usuarioId={usuarioId}", token: token);

        Assert.Multiple(() =>
        {
            Assert.That(associados.Status, Is.EqualTo(200), $"Módulos (associado) é do atendimento: {associados.Corpo}");
            Assert.That(home.Status, Is.EqualTo(200), $"A /home consulta o próprio usuário: {home.Corpo}");
        });
    }

    private async Task<ApiResposta> EnviarAsync(string metodo, string caminho, JsonObject? corpo = null, string? token = null)
    {
        var headers = new Dictionary<string, string>();
        if (token is not null)
        {
            headers["Authorization"] = $"Bearer {token}";
        }
        if (corpo is not null)
        {
            headers["Content-Type"] = "application/json";
        }

        var resposta = await _api.FetchAsync(caminho, new()
        {
            Method = metodo,
            Headers = headers,
            Data = corpo?.ToJsonString(),
        });
        var texto = await resposta.TextAsync();
        JsonNode? json = null;
        try { json = JsonNode.Parse(texto); } catch (System.Text.Json.JsonException) { }
        return new ApiResposta(metodo, caminho, resposta.Status, texto, json);
    }

    private async Task<JsonNode?> SessaoAtendimentoAsync(string token)
    {
        var sessao = await EnviarAsync("GET", "v1/autenticador/RetrieveSessionApp/Atendimento", token: token);
        Assert.That(sessao.Status, Is.EqualTo(200), sessao.Corpo);
        return sessao.Json;
    }

    private static string TokenDe(ApiResposta resposta)
    {
        Assert.That(resposta.Status, Is.EqualTo(200), $"{resposta.Caminho}: {resposta.Corpo}");
        var token = (resposta.Json as JsonObject)?["token"].ComoTexto();
        Assert.That(string.IsNullOrEmpty(token), Is.False, $"{resposta.Caminho} não devolveu token.");
        return token!;
    }

    private static void AssertSemToken(ApiResposta resposta, string bug) =>
        Assert.Multiple(() =>
        {
            Assert.That(resposta.Status, Is.AnyOf(400, 401, 403), $"{bug} Status {resposta.Status}.");
            // Só o booleano: o JWT não pode aparecer no log do teste.
            Assert.That(string.IsNullOrEmpty((resposta.Json as JsonObject)?["token"].ComoTexto()), Is.True, bug);
        });
}
