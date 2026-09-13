using PlaywrightTests.Shared.Api;

namespace PlaywrightTests.Features.Logistica.Data;

/// <summary>
/// Registra o que cada teste criou e exclui via API no TearDown, respeitando as FKs Restrict do backend.
/// </summary>
public sealed class CleanupTracker
{
    // Ordem de exclusão: dependentes primeiro. O DELETE de SolicitacaoLogistica já remove viagens, itens e OS.
    private static readonly string[] Ordem =
    [
        LogisticaRecursos.ViagemLogistica,
        LogisticaRecursos.OrdemServico,
        LogisticaRecursos.SolicitacaoLogistica,
        LogisticaRecursos.Passageiro,
        LogisticaRecursos.Carga,
        LogisticaRecursos.Solicitante,
        LogisticaRecursos.CentroCusto,
        LogisticaRecursos.EnderecoLogistica,
        LogisticaRecursos.Cliente,
    ];

    private readonly List<(string Recurso, long Id)> _criados = [];

    public void Registrar(string recurso, long id)
    {
        if (!_criados.Contains((recurso, id)))
        {
            _criados.Add((recurso, id));
        }
    }

    /// <summary>Exclui tudo o que foi registrado. Devolve as falhas (404 é ignorado) sem lançar exceção.</summary>
    public async Task<IReadOnlyList<string>> LimparAsync(ApiClient api)
    {
        var falhas = new List<string>();
        var itens = _criados
            .Select((c, indice) => (c.Recurso, c.Id, indice))
            .OrderBy(c => Array.IndexOf(Ordem, c.Recurso) is var p && p < 0 ? int.MaxValue : p)
            .ThenByDescending(c => c.indice);

        foreach (var (recurso, id, _) in itens)
        {
            try
            {
                var resposta = await api.DeleteAsync($"{recurso}/{id}");
                if (!resposta.Sucesso && resposta.Status != 404)
                {
                    falhas.Add($"Cleanup: DELETE {recurso}/{id} retornou {resposta.Status}: {resposta.Corpo}");
                }
            }
            catch (Exception ex)
            {
                falhas.Add($"Cleanup: DELETE {recurso}/{id} falhou: {ex.Message}");
            }
        }

        _criados.Clear();
        return falhas;
    }
}
