using System.Text.Json.Nodes;
using PlaywrightTests.Features.Logistica.Data;
using PlaywrightTests.Shared.Api;
using Fabrica = PlaywrightTests.Features.Logistica.Data.LogisticaDataFactory;

namespace PlaywrightTests.Features.Logistica.Api;

/// <summary>
/// Operações de logística na CCO.Api para preparar e verificar cenários. Tudo o que é criado vai para o cleanup.
/// </summary>
public sealed class LogisticaApi(ApiClient api, CleanupTracker cleanup)
{
    public const string IncludeSolicitacao =
        "OrigemSolicitacao,Cliente,Solicitante,CentroCusto,Itens,Itens.Enderecos,Itens.Passageiro,Itens.Carga,OrdemServico";

    public const string IncludeViagem = "Itens,Itens.Passageiro,Itens.Carga";

    public Task<JsonNode> CriarClienteAsync() =>
        CriarAsync(LogisticaRecursos.Cliente, Fabrica.ClientePessoaJuridica(Fabrica.Nome("Cliente")));

    public Task<JsonNode> CriarCentroCustoAsync(long clienteId) =>
        CriarAsync(LogisticaRecursos.CentroCusto, Fabrica.CentroCusto(Fabrica.Nome("CentroCusto"), clienteId));

    public Task<JsonNode> CriarSolicitanteAsync(long clienteId) =>
        CriarAsync(LogisticaRecursos.Solicitante, Fabrica.Solicitante(Fabrica.Nome("Solicitante"), clienteId));

    public Task<JsonNode> CriarPassageiroAsync(long clienteId) =>
        CriarAsync(LogisticaRecursos.Passageiro, Fabrica.Passageiro(Fabrica.Nome("Passageiro"), clienteId));

    public Task<JsonNode> CriarCargaAsync() =>
        CriarAsync(LogisticaRecursos.Carga, Fabrica.Carga(Fabrica.Nome("Carga")));

    public Task<JsonNode> CriarEnderecoAsync(PontoGeografico ponto) =>
        CriarAsync(LogisticaRecursos.EnderecoLogistica, Fabrica.EnderecoLogistica(Fabrica.Nome("Local"), ponto));

    /// <summary>Cliente, centro de custo, solicitante, passageiro ou carga, e os locais de origem e destino.</summary>
    public async Task<DadosApoio> CriarDadosApoioAsync(bool carga)
    {
        var cliente = await CriarClienteAsync();
        var centroCusto = await CriarCentroCustoAsync(cliente.Id());
        var solicitante = await CriarSolicitanteAsync(cliente.Id());
        var item = carga ? await CriarCargaAsync() : await CriarPassageiroAsync(cliente.Id());
        var origem = await CriarEnderecoAsync(Fabrica.PontoOrigem);
        var destino = await CriarEnderecoAsync(Fabrica.PontoDestino);

        return new DadosApoio(carga, cliente, centroCusto, solicitante, item, origem, destino);
    }

    /// <summary>Programação pendente (sem ordem de serviço) com um item de origem → destino.</summary>
    public async Task<JsonNode> CriarSolicitacaoAsync(DadosApoio apoio, string observacao)
    {
        var item = new JsonObject
        {
            [apoio.Carga ? "cargaId" : "passageiroId"] = apoio.Item.Id(),
            ["enderecos"] = new JsonArray(
                Fabrica.EnderecoItem(Fabrica.EnderecoOrigem, apoio.Origem),
                Fabrica.EnderecoItem(Fabrica.EnderecoDestino, apoio.Destino)),
        };

        var corpo = Fabrica.SolicitacaoLogistica(
            apoio.Carga ? Fabrica.TipoOperacaoCarga : Fabrica.TipoOperacaoPassageiro,
            apoio.Cliente.Id(), apoio.CentroCusto.Id(), apoio.Solicitante.Id(), observacao, new JsonArray(item));

        var criada = await CriarAsync(LogisticaRecursos.SolicitacaoLogistica, corpo);
        return await ObterSolicitacaoAsync(criada.Id());
    }

    public async Task<JsonNode> ObterSolicitacaoAsync(long id) =>
        await api.GetAsync($"{LogisticaRecursos.SolicitacaoLogistica}/{id}", IncludeSolicitacao)
        ?? throw new InvalidOperationException($"Solicitação {id} não encontrada.");

    /// <summary>Localiza a programação criada pela tela (o cliente de apoio é exclusivo do teste) e a registra no cleanup.</summary>
    public async Task<JsonNode?> BuscarSolicitacaoPorClienteAsync(long clienteId)
    {
        var ids = await BuscarIdsAsync(LogisticaRecursos.SolicitacaoLogistica, $"clienteId eq {clienteId}");
        foreach (var id in ids)
        {
            cleanup.Registrar(LogisticaRecursos.SolicitacaoLogistica, id);
        }

        return ids.Count == 0 ? null : await ObterSolicitacaoAsync(ids[0]);
    }

    /// <summary>Simula o CCO.Worker: cria a viagem da programação já com status Iniciada.</summary>
    public async Task<JsonNode> CriarViagemIniciadaAsync(JsonNode solicitacao, string observacao)
    {
        var criada = await CriarAsync(LogisticaRecursos.ViagemLogistica, Fabrica.ViagemIniciada(solicitacao, observacao));
        return await ObterViagemAsync(criada.Id());
    }

    public async Task<JsonNode> ObterViagemAsync(long id) =>
        await api.GetAsync($"{LogisticaRecursos.ViagemLogistica}/{id}", IncludeViagem)
        ?? throw new InvalidOperationException($"Viagem {id} não encontrada.");

    /// <summary>Encontra pela API um registro criado pela tela e o registra no cleanup (rede de segurança se o teste falhar).</summary>
    public async Task<long?> RegistrarCriadoNaUiAsync(string recurso, string campo, string valor)
    {
        var termo = valor.ToLowerInvariant().Replace("'", "''");
        var ids = await BuscarIdsAsync(recurso, $"Contains(tolower({campo}),'{termo}')");
        foreach (var id in ids)
        {
            cleanup.Registrar(recurso, id);
        }

        return ids.Count == 0 ? null : ids[0];
    }

    private async Task<List<long>> BuscarIdsAsync(string recurso, string filtro)
    {
        var pagina = await api.GetAsync($"{recurso}?$Top=10&$filter={Uri.EscapeDataString(filtro)}");
        return pagina.Registros().Select(r => r.Id()).ToList();
    }

    private async Task<JsonNode> CriarAsync(string recurso, JsonObject corpo)
    {
        var criado = await api.PostAsync(recurso, corpo)
            ?? throw new InvalidOperationException($"POST {recurso} não devolveu o registro criado.");
        cleanup.Registrar(recurso, criado.Id());
        return criado;
    }
}

/// <summary>Dados de apoio criados via API para um cenário de programação.</summary>
public sealed record DadosApoio(
    bool Carga, JsonNode Cliente, JsonNode CentroCusto, JsonNode Solicitante, JsonNode Item, JsonNode Origem, JsonNode Destino)
{
    public string NomeCliente => Cliente["nome"].ComoTexto()!;

    public string NomeCentroCusto => CentroCusto["nome"].ComoTexto()!;

    public string NomeSolicitante => Solicitante["nome"].ComoTexto()!;

    /// <summary>Nome do passageiro ou descrição da carga.</summary>
    public string NomeItem => (Carga ? Item["descricaoProduto"] : Item["nome"]).ComoTexto()!;

    public string NomeOrigem => Origem["nome"].ComoTexto()!;

    public string NomeDestino => Destino["nome"].ComoTexto()!;
}
