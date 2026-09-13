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

    public static string? Ler(string nome)
    {
        var valor = Environment.GetEnvironmentVariable(nome);
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private static string Obrigatoria(string nome) =>
        Ler(nome) ?? throw new InvalidOperationException(
            $"Variável de ambiente {nome} não definida. Exporte E2E_LOGIN, E2E_SENHA e E2E_EMPRESA " +
            "antes de rodar os testes (veja Features/Logistica/README.md).");
}
