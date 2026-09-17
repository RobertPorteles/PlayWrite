namespace PlaywrightTests.Features.ControleOperacao.Data;

/// <summary>Caminhos de controle de operação na CCO.Api.</summary>
public static class ControleOperacaoRecursos
{
    public const string Funcionario = "v1/ControleOperacao/Funcionario";

    /// <summary>Lista estrita: Funcionario.Ativo + função de motorista.</summary>
    public const string MotoristasAtivos = $"{Funcionario}/ObterMotoristasAtivos";

    /// <summary>Autocomplete por termo: filtra função; Ativo só com somenteAtivos=true.</summary>
    public const string AutoCompleteMotorista = $"{Funcionario}/AutoCompleteMotorista";

    /// <summary>Autocomplete do cadastro de viagem: só motoristas ativos, marcando a disponibilidade no período.</summary>
    public const string AutoCompleteFuncionarioDisponivel = $"{Funcionario}/AutoCompleteFuncionarioDisponivel";

    public const string Vincular = "v1/ControleOperacao/VinculoVeiculo/Vincular";

    public const string ListarVinculos = "v1/ControleOperacao/VinculoVeiculo/Listar";

    public const string Alocar = "v1/ControleOperacao/AlocacaoVeiculo/Alocar";

    public const string Veiculo = "v1/ControleOperacao/Veiculo";

    public const string VeiculosAtivos = $"{Veiculo}/ObterVeiculosAtivos";

    /// <summary>
    /// Mesmas listas que o cadastro de veículo carrega nos selects (Service.ListarTudo do front).
    /// O q é obrigatório na API; o front manda "pre" junto com preCarregado=true.
    /// </summary>
    public const string TiposVeiculo = "v1/TipoVeiculo/Autocomplete?preCarregado=true&q=pre";

    public const string TiposOperacao = "v1/TipoOperacao/Autocomplete?preCarregado=true&q=pre";

    /// <summary>SituacaoVinculoVeiculo.Vinculado.</summary>
    public const int SituacaoVinculado = 1;
}
