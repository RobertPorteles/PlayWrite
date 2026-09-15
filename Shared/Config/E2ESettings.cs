namespace PlaywrightTests.Shared.Config;

/// <summary>
/// Configuração dos testes E2E lida de variáveis de ambiente (nada sensível fica no código).
/// </summary>
public static class E2ESettings
{
    public static string FrontUrl => Ler("E2E_FRONT_URL") ?? "http://localhost:5173";

    public static string ApiUrl => (Ler("E2E_API_URL") ?? "http://localhost:5225").TrimEnd('/') + "/";

    public static string Login => Obrigatoria("E2E_LOGIN");

    public static string Senha => Obrigatoria("E2E_SENHA");

    /// <summary>Nome da empresa (como aparece no card do login) ou seu GUID. Opcional se o usuário tiver uma só empresa.</summary>
    public static string? Empresa => Ler("E2E_EMPRESA");

    /// <summary>Texto do veículo a escolher na ordem de serviço. Se vazio, usa a primeira opção disponível.</summary>
    public static string? Veiculo => Ler("E2E_VEICULO");

    /// <summary>Texto do motorista a escolher na ordem de serviço. Se vazio, usa a primeira opção disponível.</summary>
    public static string? Motorista => Ler("E2E_MOTORISTA");

    /// <summary>Usuário local sem perfil master, para provar o 403 da fachada da Siera. Opcional.</summary>
    public static string? LoginNaoMaster => Ler("E2E_LOGIN_NAO_MASTER");

    public static string? SenhaNaoMaster => Ler("E2E_SENHA_NAO_MASTER");

    /// <summary>Raiz do ccomobility-backend. Padrão: pasta irmã do PlayWrite.</summary>
    public static string BackendDir => Ler("E2E_BACKEND_DIR") ?? Path.Combine(Workspace, "ccomobility-backend");

    /// <summary>Raiz do ccomobility-frontend (a pasta com package.json). Padrão: pasta irmã do PlayWrite.</summary>
    public static string FrontendDir =>
        Ler("E2E_FRONTEND_DIR") ?? Path.Combine(Workspace, "ccomobility-frontend", "ccomobility-frontend");

    /// <summary>Raiz do ccomobility-telematics-integrations. Padrão: pasta irmã do PlayWrite.</summary>
    public static string IntegracoesDir =>
        Ler("E2E_INTEGRACOES_DIR") ?? Path.Combine(Workspace, "ccomobility-telematics-integrations");

    public static string? Ler(string nome)
    {
        var valor = Environment.GetEnvironmentVariable(nome);
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private static string Obrigatoria(string nome) =>
        Ler(nome) ?? throw new InvalidOperationException(
            $"Variável de ambiente {nome} não definida. Exporte E2E_LOGIN, E2E_SENHA e E2E_EMPRESA " +
            "antes de rodar os testes (veja Features/Logistica/README.md).");

    /// <summary>Pasta em que o PlayWrite e os repositórios do CCO Mobility ficam lado a lado.</summary>
    private static string Workspace
    {
        get
        {
            for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
            {
                if (File.Exists(Path.Combine(pasta.FullName, "PlaywrightTests.csproj")))
                {
                    return pasta.Parent?.FullName ?? pasta.FullName;
                }
            }

            throw new InvalidOperationException(
                $"PlaywrightTests.csproj não encontrado acima de {AppContext.BaseDirectory}; " +
                "defina E2E_BACKEND_DIR, E2E_FRONTEND_DIR e E2E_INTEGRACOES_DIR.");
        }
    }
}
