using System.Text.Json.Nodes;
using Microsoft.Playwright;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Atendimento;

/// <summary>
/// Cria uma vez por execução, via CCO.Api local e como master, o perfil de atendimento (todas as funcionalidades
/// 8110xx) e três usuários não-master com ele: <see cref="SoAtendimento"/>, com o vínculo sem empresa (como a tela
/// /usuario permite); <see cref="Misto"/>, que também tem a empresa de E2E_EMPRESA com o mesmo perfil; e
/// <see cref="ComEmpresaLiberada"/>, que tem essa empresa liberada para o atendimento (vínculo com empresa e sem perfil,
/// o campo "Empresas do atendimento"). Para essa empresa ter o que mostrar, cria também um usuário comum dela com um
/// perfil próprio (<see cref="FuncionalidadesDaEmpresa"/>). Apaga tudo no fim, e antes de criar remove sobras de
/// execuções abortadas. A base local não tem perfis e todos os usuários são master.
/// </summary>
[SetUpFixture]
public sealed class UsuariosAtendimento
{
    private const string PrefixoPerfil = "E2E Atendimento";

    private const string PrefixoEmail = "e2e.atend.";

    /// <summary>UsuarioService.Post ignora a senha enviada e grava sempre esta.</summary>
    public const string Senha = "123@mudar";

    /// <summary>Atendimento (811000) e as filhas, até Estoque (811015).</summary>
    private static readonly int[] Funcionalidades = Enumerable.Range(811000, 16).ToArray();

    /// <summary>
    /// Perfil do usuário comum da empresa: Mapa (1035000) e Mapa_Listar, que aparecem no menu, e Administrador
    /// (Cliente) (813004), de ControleSistema, que o atendimento NÃO deve herdar.
    /// </summary>
    public static readonly int[] FuncionalidadesDaEmpresa = [1035000, 1035001, 813004];

    private IPlaywright? _playwright;

    private ApiClient? _api;

    public static string SoAtendimento { get; private set; } = "";

    public static string Misto { get; private set; } = "";

    /// <summary>Atendimento com a empresa de E2E_EMPRESA liberada (vínculo com empresa e sem perfil).</summary>
    public static string ComEmpresaLiberada { get; private set; } = "";

    /// <summary>Nome da empresa (E2E_EMPRESA) que o usuário misto também tem.</summary>
    public static string EmpresaDoMisto { get; private set; } = "";

    /// <summary>Id (GUID) da empresa de E2E_EMPRESA.</summary>
    public static string EmpresaId { get; private set; } = "";

    [OneTimeSetUp]
    public async Task CriarAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _api = await ApiClient.LoginAsync(_playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        await LimparAsync(_api);

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var perfil = await _api.PostAsync("v1/perfil", new JsonObject
        {
            ["nome"] = $"{PrefixoPerfil} {sufixo}",
            ["ativo"] = true,
            ["itens"] = new JsonArray(Funcionalidades.Select(f => (JsonNode)new JsonObject { ["funcionalidade"] = f }).ToArray()),
        });
        var perfilId = perfil.Id();

        var sessao = await _api.GetAsync("v1/Autenticador/RetrieveSessionApp/Sistema");
        EmpresaDoMisto = sessao?["empresa"]?["nome"].ComoTexto()
            ?? throw new InvalidOperationException("A sessão do master não trouxe a empresa.");

        EmpresaId = _api.EmpresaId ?? throw new InvalidOperationException("A sessão do master não trouxe o id da empresa.");

        SoAtendimento = await CriarUsuarioAsync(_api, $"{PrefixoEmail}so.{sufixo}@e2e.local", new JsonObject { ["perfilId"] = perfilId });
        Misto = await CriarUsuarioAsync(_api, $"{PrefixoEmail}misto.{sufixo}@e2e.local",
            new JsonObject { ["perfilId"] = perfilId },
            new JsonObject { ["perfilId"] = perfilId, ["empresaId"] = EmpresaId });
        ComEmpresaLiberada = await CriarUsuarioAsync(_api, $"{PrefixoEmail}liberada.{sufixo}@e2e.local",
            new JsonObject { ["perfilId"] = perfilId },
            new JsonObject { ["empresaId"] = EmpresaId });

        var perfilEmpresa = await _api.PostAsync("v1/perfil", new JsonObject
        {
            ["nome"] = $"{PrefixoPerfil} Empresa {sufixo}",
            ["ativo"] = true,
            ["itens"] = new JsonArray(FuncionalidadesDaEmpresa.Select(f => (JsonNode)new JsonObject { ["funcionalidade"] = f }).ToArray()),
        });
        await CriarUsuarioAsync(_api, $"{PrefixoEmail}empresa.{sufixo}@e2e.local",
            new JsonObject { ["perfilId"] = perfilEmpresa.Id(), ["empresaId"] = EmpresaId });
    }

    [OneTimeTearDown]
    public async Task ApagarAsync()
    {
        if (_api is not null)
        {
            await LimparAsync(_api);
            await _api.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    private static async Task<string> CriarUsuarioAsync(ApiClient api, string email, params JsonObject[] vinculos)
    {
        await api.PostAsync("v1/usuario", new JsonObject
        {
            ["email"] = email,
            ["nome"] = "E2E Atendimento",
            ["empresas"] = new JsonArray(vinculos.Select(v => (JsonNode)v).ToArray()),
        });
        return email;
    }

    /// <summary>Usuários antes do perfil: o vínculo Usuario_Empresa aponta para o perfil.</summary>
    private static async Task LimparAsync(ApiClient api)
    {
        var falhas = new List<string>();

        foreach (var usuario in (await api.GetAsync("v1/usuario")).Registros())
        {
            if (usuario?["email"].ComoTexto()?.StartsWith(PrefixoEmail, StringComparison.OrdinalIgnoreCase) == true)
            {
                falhas.AddRange(await ApagarAsync(api, $"v1/usuario/{usuario["id"]}"));
            }
        }

        foreach (var perfil in (await api.GetAsync("v1/perfil")).Registros())
        {
            if (perfil?["nome"].ComoTexto()?.StartsWith(PrefixoPerfil, StringComparison.Ordinal) == true)
            {
                falhas.AddRange(await ApagarAsync(api, $"v1/perfil/{perfil["id"]}"));
            }
        }

        if (falhas.Count > 0)
        {
            TestContext.Progress.WriteLine("Limpeza do atendimento incompleta: " + string.Join(" | ", falhas));
        }
    }

    private static async Task<IEnumerable<string>> ApagarAsync(ApiClient api, string caminho)
    {
        var resposta = await api.DeleteAsync(caminho);
        return resposta.Sucesso || resposta.Status == 404 ? [] : [$"{caminho}: {resposta.Status} {resposta.Corpo}"];
    }
}
