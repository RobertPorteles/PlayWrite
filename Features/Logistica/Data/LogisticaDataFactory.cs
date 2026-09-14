using System.Globalization;
using System.Text.Json.Nodes;
using PlaywrightTests.Shared.Api;

namespace PlaywrightTests.Features.Logistica.Data;

public readonly record struct PontoGeografico(string Nome, string Logradouro, double Latitude, double Longitude);

/// <summary>
/// Monta payloads válidos para a CCO.Api com nomes únicos prefixados por "E2E-".
/// </summary>
public static class LogisticaDataFactory
{
    public const string Prefixo = "E2E-";

    public const int TipoOperacaoPassageiro = 1;
    public const int TipoOperacaoCarga = 2;

    public const int OrigemSolicitacaoInterno = 5;
    public const string OrigemSolicitacaoInternoNome = "Interno";

    public const int EnderecoOrigem = 1;
    public const int EnderecoDestino = 3;

    public const int StatusProgramacaoPendente = 1;
    public const int StatusProgramacaoProgramada = 2;

    public const int StatusViagemIniciada = 3;
    public const int StatusViagemFinalizada = 4;
    public const int StatusViagemCancelada = 5;

    /// <summary>Praça da Sé e MASP (São Paulo), bem distantes do raio de 70 m um do outro.</summary>
    public static readonly PontoGeografico PontoOrigem = new("Praça da Sé", "Praça da Sé", -23.550520, -46.633309);

    public static readonly PontoGeografico PontoDestino = new("MASP", "Avenida Paulista", -23.561414, -46.655881);

    /// <summary>Amanhã: fora do filtro "hoje" do CCO.Worker, que assim não interfere caso seja ligado.</summary>
    public static DateOnly DataViagem => DateOnly.FromDateTime(DateTime.Now.AddDays(1));

    public static readonly TimeOnly HoraInicio = new(8, 0);
    public static readonly TimeOnly HoraFim = new(10, 0);

    public static string Nome(string entidade) =>
        $"{Prefixo}{entidade}-{DateTime.Now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";

    public static string FormatarData(DateOnly data) => data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string FormatarHora(TimeOnly hora) => hora.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Formato das colunas "timestamp without time zone" (sem fuso).</summary>
    public static string FormatarDataHora(DateTime dataHora) =>
        dataHora.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    public static JsonObject ClientePessoaJuridica(string nome) => new()
    {
        ["tipoCliente"] = 2,
        ["tipoContrato"] = 1,
        ["nome"] = nome,
        ["razaoSocial"] = nome,
        ["nomeFantasia"] = nome,
        ["cnpj"] = GerarCnpj(),
        ["email"] = "e2e@teste.local",
        ["numero"] = 0,
        ["cidade"] = "São Paulo",
        ["estado"] = "São Paulo",
        ["pais"] = "Brasil",
    };

    public static JsonObject CentroCusto(string nome, long clienteId) => new()
    {
        ["nome"] = nome,
        ["clienteId"] = clienteId,
    };

    public static JsonObject Solicitante(string nome, long clienteId) => new()
    {
        ["nome"] = nome,
        ["email"] = "solicitante.e2e@teste.local",
        ["clienteId"] = clienteId,
    };

    public static JsonObject Passageiro(string nome, long clienteId) => new()
    {
        ["nome"] = nome,
        ["cpf"] = GerarCpf(),
        ["email"] = "passageiro.e2e@teste.local",
        ["genero"] = 1,
        ["clienteId"] = clienteId,
    };

    public static JsonObject Carga(string descricao) => new()
    {
        ["codigoProduto"] = Random.Shared.Next(1000, 9999),
        ["descricaoProduto"] = descricao,
        ["quantidadePacotes"] = "1",
    };

    public static JsonObject EnderecoLogistica(string nome, PontoGeografico ponto) => new()
    {
        ["nome"] = nome,
        ["logradouro"] = ponto.Logradouro,
        ["numero"] = "1",
        ["bairro"] = "Centro",
        ["cidade"] = "São Paulo",
        ["estado"] = "São Paulo",
        ["uf"] = "SP",
        ["pais"] = "Brasil",
        ["latitude"] = ponto.Latitude,
        ["longitude"] = ponto.Longitude,
        ["raioMetros"] = 70,
    };

    /// <summary>Endereço de item da solicitação copiado de um EnderecoLogistica já criado.</summary>
    public static JsonObject EnderecoItem(int tipoEndereco, JsonNode endereco) => new()
    {
        ["tipoEndereco"] = tipoEndereco,
        ["enderecoLogisticaId"] = endereco.Id(),
        ["nome"] = endereco["nome"].ComoTexto(),
        ["logradouro"] = endereco["logradouro"].ComoTexto(),
        ["numero"] = endereco["numero"].ComoTexto(),
        ["bairro"] = endereco["bairro"].ComoTexto(),
        ["cidade"] = endereco["cidade"].ComoTexto(),
        ["estado"] = endereco["estado"].ComoTexto(),
        ["uf"] = endereco["uf"].ComoTexto(),
        ["pais"] = endereco["pais"].ComoTexto(),
        ["latitude"] = endereco["latitude"]!.GetValue<double>(),
        ["longitude"] = endereco["longitude"]!.GetValue<double>(),
        ["raioMetros"] = 70,
    };

    public static JsonObject SolicitacaoLogistica(int tipoOperacao, long clienteId, long? centroCustoId, long? solicitanteId,
        string observacao, JsonArray itens) => new()
    {
        ["status"] = StatusProgramacaoPendente,
        ["tipoOperacao"] = tipoOperacao,
        ["origemSolicitacaoId"] = OrigemSolicitacaoInterno,
        ["dataInicioSolicitada"] = FormatarData(DataViagem),
        ["horaInicioSolicitada"] = FormatarHora(HoraInicio),
        ["dataFimSolicitada"] = FormatarData(DataViagem),
        ["horaFimSolicitada"] = FormatarHora(HoraFim),
        ["bilingue"] = false,
        ["receptivo"] = false,
        ["clienteId"] = clienteId,
        ["centroCustoId"] = centroCustoId,
        ["solicitanteId"] = solicitanteId,
        ["observacao"] = observacao,
        ["itens"] = itens,
    };

    /// <summary>
    /// Viagem já "Iniciada", como o CCO.Worker a deixaria depois que o veículo sai do raio da origem.
    /// </summary>
    public static JsonObject ViagemIniciada(JsonNode solicitacao, string observacao)
    {
        var agora = DateTime.Now;
        var itens = new JsonArray();
        foreach (var item in solicitacao["itens"]!.AsArray())
        {
            itens.Add(new JsonObject
            {
                ["solicitacaoLogisticaItemId"] = item.Id(),
                ["passageiroId"] = item!["passageiroId"].ComoLong(),
                ["cargaId"] = item["cargaId"].ComoLong(),
                ["status"] = StatusViagemIniciada,
                ["dataHoraChegadaVeiculoOrigem"] = FormatarDataHora(agora.AddMinutes(-10)),
                ["dataHoraRealInicio"] = FormatarDataHora(agora.AddMinutes(-5)),
            });
        }

        var ordemServico = solicitacao["ordemServico"];
        return new JsonObject
        {
            ["solicitacaoLogisticaId"] = solicitacao.Id(),
            ["ordemServicoId"] = ordemServico?["id"].ComoLong(),
            ["veiculoId"] = ordemServico?["veiculoId"].ComoLong(),
            ["motoristaId"] = ordemServico?["motoristaId"].ComoTexto(),
            ["dataHoraPrevistaInicio"] = FormatarDataHora(DataViagem.ToDateTime(HoraInicio)),
            ["dataHoraPrevistaFim"] = FormatarDataHora(DataViagem.ToDateTime(HoraFim)),
            ["dataHoraRealInicio"] = FormatarDataHora(agora.AddMinutes(-5)),
            ["status"] = StatusViagemIniciada,
            ["observacao"] = observacao,
            ["itens"] = itens,
        };
    }

    public static string GerarCpf()
    {
        var digitos = Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(0, 10)).ToList();
        digitos.Add(DigitoVerificador(digitos, [10, 9, 8, 7, 6, 5, 4, 3, 2]));
        digitos.Add(DigitoVerificador(digitos, [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digitos);
    }

    public static string GerarCnpj()
    {
        var digitos = Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(0, 10)).Concat([0, 0, 0, 1]).ToList();
        digitos.Add(DigitoVerificador(digitos, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digitos.Add(DigitoVerificador(digitos, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digitos);
    }

    private static int DigitoVerificador(IReadOnlyList<int> digitos, int[] pesos)
    {
        var resto = pesos.Select((peso, i) => peso * digitos[i]).Sum() % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
