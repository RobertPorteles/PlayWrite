using System.Text.Json.Nodes;
using System.Web;
using Microsoft.Playwright;
using PlaywrightTests.Features.Pesquisa.Stub;
using PlaywrightTests.Shared.Api;

namespace PlaywrightTests.Features.Pesquisa.Data;

/// <summary>
/// Uma tela com "Pesquisa rápida" sobre uma tabela.
/// Com dados reais, o termo é o <see cref="Campo"/> do primeiro registro de <see cref="Recurso"/> na CCO.Api local; sem
/// registro, <see cref="Semear"/> cria um pela API (excluído no TearDown). Com <see cref="Simular"/>, a lista vem do
/// navegador (a origem real é externa ou não tem dados locais) e o termo é <see cref="TelasPesquisa.TermoSimulado"/>.
/// </summary>
public sealed record TelaPesquisa(string Rota, string? Titulo, string Recurso, string Campo)
{
    public string? Aba { get; init; }

    public Func<PesquisaTestBase, Task<string>>? Semear { get; init; }

    /// <summary>Registra no stub as respostas da tela; a lista simulada tem as linhas E2E-PESQ-A e E2E-PESQ-B.</summary>
    public Action<StubNavegador>? Simular { get; init; }

    /// <summary>Defeito conhecido da tela, citado na mensagem quando o teste falha.</summary>
    public string? Defeito { get; init; }

    public override string ToString() => Rota;
}

public static class TelasPesquisa
{
    public const string TermoSimulado = "E2E-PESQ-A";

    private static readonly string[] NomesSimulados = [TermoSimulado, "E2E-PESQ-B"];

    private const string GrupoSemValor =
        "um cabeçalho de grupo sem value/path entra no filtro e Service.GetParamentroPaginacaoOrdenacao lança exceção (undefined.replaceAll) antes de chamar a API.";

    private const string NaoFiltra = "o campo é exibido, mas a tela não aplica a pesquisa (nem na consulta, nem na tabela).";

    public static IEnumerable<TelaPesquisa> Tabelas()
    {
        // Pesquisa no servidor: o front monta $filter=Contains(tolower(coluna), '...') (Service.GetParamentroPaginacaoOrdenacao).
        yield return new("/empresa", "Empresas", "v1/empresa", "nome");
        yield return new("/estoque", "Estoque", "v1/Estoque", "nome") { Semear = t => SemearNomeAsync(t, "v1/Estoque") };
        yield return new("/perfil", "Perfil de Usuário", "v1/perfil", "nome") { Semear = t => SemearNomeAsync(t, "v1/perfil") };
        yield return new("/servidor", "Servidor", "v1/servidor", "host");
        yield return new("/usuario", "Usuários", "v1/usuario", "nome");
        yield return new("/funcao", "Funções", "v1/funcao", "nome")
        {
            Semear = t => SemearNomeAsync(t, "v1/funcao", new() { ["ativo"] = true }),
        };
        yield return new("/funcionario", "Funcionários", "v1/controleoperacao/funcionario", "nome");
        yield return new("/quadroHorarioFuncionario", "Quadros de Horário", "v1/controleoperacao/quadrohorariofuncionario", "descricao")
        {
            Semear = t => SemearNomeAsync(t, "v1/controleoperacao/quadrohorariofuncionario",
                new() { ["ativo"] = true, ["segundaTurno1"] = true }, campo: "descricao"),
        };
        yield return new("/rota", "Rotas", "v1/controleOperacao/rota", "nome")
        {
            Defeito = "a pesquisa inclui colunas não textuais (ativa, distanciaMetros, tempoPrevisto, valor) em Contains(tolower(...)).",
        };
        yield return new("/tempoDeslocamento", "Tempo Deslocamento", "v1/controleoperacao/tempodeslocamento", "nomeTerminal")
        {
            Semear = t => SemearNomeAsync(t, "v1/controleoperacao/tempodeslocamento",
                new() { ["tempoDeslocamentoMinutos"] = 10, ["pontoInteresseId"] = 1, ["garagemDestinoNome"] = "E2E" },
                campo: "nomeTerminal"),
            Defeito = "a pesquisa inclui a coluna numérica tempoDeslocamentoMinutos em Contains(tolower(...)).",
        };
        yield return new("/agrupamentoLinha", "Agrupamento de Linha", "v1/agrupamentoLinha", "nome")
        {
            Semear = t => SemearNomeAsync(t, "v1/agrupamentoLinha"),
        };
        yield return new("/agrupamentoVeiculo", "Agrupamento de Veículo", "v1/agrupamentoVeiculo", "nome")
        {
            Semear = t => SemearNomeAsync(t, "v1/agrupamentoVeiculo"),
        };
        yield return new("/area", "Áreas", "v1/controleOperacao/area", "nome") { Semear = async t => (await SemearAreaAsync(t)).Nome };
        yield return new("/regiaoExploracao", "Regioẽs de Explorações", "v1/regiaoExploracao", "nome")
        {
            Semear = t => SemearNomeAsync(t, "v1/regiaoExploracao"),
        };
        yield return new("/tipoEvento", "Tipos de Evento", "v1/tipoEvento", "nome")
        {
            Semear = t => SemearNomeAsync(t, "v1/tipoEvento", new() { ["ativo"] = true }),
        };
        yield return new("/tipoManutencao", "Tipos de Manutencao", "v1/tipoManutencao", "nome")
        {
            Defeito = GrupoSemValor,
            Semear = async t => (await SemearTipoManutencaoAsync(t))["nome"].ComoTexto()!,
        };
        yield return new("/agency", "Agências", "v1/agency", "name")
        {
            Defeito = "a tela lista por GET v1/agency (OData), mas o AgencyController só expõe ListarTudo e Autocomplete.",
        };
        yield return new("/parada", "Paradas (Mobilidade)", "v1/parada", "nome")
        {
            Semear = t => SemearNomeAsync(t, "v1/parada",
                new() { ["ativa"] = true, ["geom"] = new JsonObject { ["type"] = "Point", ["coordinates"] = new JsonArray(-43.2, -22.9) } }),
        };
        yield return new("/quilometragemEntreAreas", null, "v1/ccomobility/ViagemEntreArea", "nome")
        {
            Aba = "Viagens Entre Areas",
            Semear = SemearViagemEntreAreaAsync,
        };
        yield return new("/odometroVeiculoEquipamento", null, "v1/cliente", "nome")
        {
            Aba = "Configurações",
            Semear = SemearClienteAsync,
        };
        yield return new("/usuarioLinhaEmpresa", "Usuários Linhas Empresas", "v1/usuarioLinhaEmpresa", "id")
        {
            Defeito = GrupoSemValor,
        };

        // Pesquisa no cliente: a lista é carregada uma vez ($Top=500) e filtrada em JavaScript.
        // A descrição não aparece na tabela; o nome do tipo de manutenção aparece e entra no filtro.
        yield return new("/manutencaoVeiculo", "Manutencoes de Veiculos", "v1/ControleOperacao/ManutencaoVeiculo", "tipoManutencaoNome")
        {
            Semear = async t => (await SemearManutencaoAsync(t)).TipoNome,
        };
        yield return new("/manutencaoVeiculoOS", "Manutencao Veiculo OS", "v1/ControleOperacao/ManutencaoVeiculoOS", "numero")
        {
            Semear = SemearOrdemServicoAsync,
        };

        // Listas simuladas no navegador: a origem é um serviço externo (GTFS, Órgão Gestor, Concentrador) ou a base local
        // não tem dados e a tela nem chega a mandar a pesquisa para a API.
        yield return new("/stop", "Paradas (GTFS)", "/v1/stop/ListarStop/", "stopName")
        {
            Simular = s => s.Responder("/v1/stop/ListarStop/", r => Pagina("items", "count", FiltradosPorSearch(r).Select(n => new JsonObject { ["stopId"] = n, ["stopName"] = n }))),
        };
        yield return new("/permissionario", "Permissionarios", "/v1/permissionario/ListarPermissionarios", "nome")
        {
            Simular = s =>
            {
                s.Responder("/v1/permissionario/ListarPermissionarios",
                    _ => Pagina("items", "count", NomesSimulados.Select((n, i) => new JsonObject { ["id"] = i + 1, ["nome"] = n })));
                // Os permissionários "prioritários" fixos no front são buscados um a um; null = não encontrado.
                s.Responder("/v1/permissionario/ObterPermissionarioPorId/", _ => null);
            },
        };
        yield return new("/manutencaoGerenciada", "Manutenção Gerenciada", "/v1/Concentrador/Rastreado/ListarModulosReservados", "codigoModulo")
        {
            Simular = s =>
            {
                // A tabela da esquerda (veículos do cliente) também é legada; fica vazia.
                s.Responder("/v1/clienteAntigo/veiculo", _ => Pagina("items", "count", []));
                s.Responder("/v1/Concentrador/Rastreado/ListarModulosReservados", r => new JsonArray(FiltradosPorSearch(r)
                    .Select(n => (JsonNode)new JsonObject { ["codigoModulo"] = n, ["dataHoraRastreio"] = "2026-09-21T10:00:00" }).ToArray()));
            },
        };
        yield return new("/linha", "Linhas", "/ObterLinhasPorUnidadeOrganizacional", "nome")
        {
            Simular = s => s.Responder("/ObterLinhasPorUnidadeOrganizacional", _ => Lista(n => new JsonObject { ["id"] = 1, ["codigoLinha"] = n, ["nome"] = n, ["ativo"] = true })),
            Defeito = NaoFiltra,
        };
        yield return new("/veiculo", "Veículos", "/ObterVeiculosPorUnidadeOrganizacional", "identificador")
        {
            Simular = s => s.Responder("/ObterVeiculosPorUnidadeOrganizacional", _ => Lista(n => new JsonObject
            {
                ["id"] = 1, ["identificador"] = n, ["descricao"] = n, ["placa"] = n, ["ativo"] = true,
                ["operacaoTransportePublico"] = new JsonObject { ["alocacaoAutomatica"] = false },
            })),
            Defeito = NaoFiltra,
        };
        yield return new("/bom", "Boletim de Operação Mensal", "/ObterBomPorUnidadeOrganizacional", "nomeUnidadeOrganizacional")
        {
            Simular = s => s.Responder("/ObterBomPorUnidadeOrganizacional", _ => Lista(n => new JsonObject { ["id"] = 1, ["nomeUnidadeOrganizacional"] = n })),
            Defeito = NaoFiltra,
        };
        yield return new("/registroRequisicaoMapa", "Requisições de Mapa", "/GetRegistroRequisicaoMapa", "NomeUsuario")
        {
            Simular = s => s.Responder("/GetRegistroRequisicaoMapa", _ => Pagina("Items", "Count", NomesSimulados.Select(n => new JsonObject { ["IdCliente"] = 1, ["NomeUsuario"] = n }))),
            Defeito = NaoFiltra,
        };
    }

    /// <summary>Telas cuja "Pesquisa rápida" filtra cards de atalho pelo título (sem dados).</summary>
    public static IEnumerable<string> Cards() => ["/subsidio", "/relatorioEmpresaCliente", "/relatorioProduto", "/orgaoGestor"];

    private static async Task<string> SemearNomeAsync(PesquisaTestBase teste, string recurso, JsonObject? extras = null,
        string campo = "nome")
    {
        var corpo = extras ?? new JsonObject();
        corpo[campo] = PesquisaTestBase.NomeE2E(recurso.Split('/')[^1]);
        return (await teste.CriarAsync(recurso, corpo))[campo].ComoTexto()!;
    }

    private static async Task<(long Id, string Nome)> SemearAreaAsync(PesquisaTestBase teste)
    {
        var poligono = new JsonArray(new JsonArray(
            new JsonArray(-43.2, -22.9), new JsonArray(-43.19, -22.9), new JsonArray(-43.19, -22.89), new JsonArray(-43.2, -22.9)));
        var area = await teste.CriarAsync("v1/controleOperacao/area", new JsonObject
        {
            ["nome"] = PesquisaTestBase.NomeE2E("Area"),
            ["tipoArea"] = 1,
            ["coordenadas"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = poligono },
        });
        return (area.Id(), area["nome"].ComoTexto()!);
    }

    private static async Task<string> SemearViagemEntreAreaAsync(PesquisaTestBase teste)
    {
        var (areaId, _) = await SemearAreaAsync(teste);
        var viagem = await teste.CriarAsync("v1/ccomobility/ViagemEntreArea", new JsonObject
        {
            ["nome"] = PesquisaTestBase.NomeE2E("ViagemEntreArea"),
            ["areaSaidaId"] = areaId,
            ["areaEntradaId"] = areaId,
            ["tipoViagem"] = 1,
            ["duracaoMaximaMinutos"] = 60,
        });
        return viagem["nome"].ComoTexto()!;
    }

    private static async Task<string> SemearClienteAsync(PesquisaTestBase teste)
    {
        var servidor = await teste.PrimeiroAsync("v1/servidor");
        if (servidor is null)
        {
            Assert.Ignore("Não há servidor cadastrado para criar o cliente (Base.Cliente.ServidorId).");
        }

        return await SemearNomeAsync(teste, "v1/cliente", new() { ["servidorId"] = servidor!.Id(), ["ativo"] = true });
    }

    private static Task<JsonNode> SemearTipoManutencaoAsync(PesquisaTestBase teste) =>
        teste.CriarAsync("v1/tipoManutencao", new JsonObject { ["nome"] = PesquisaTestBase.NomeE2E("TipoManutencao"), ["categoria"] = 1 });

    private static async Task<(JsonNode Manutencao, string TipoNome)> SemearManutencaoAsync(PesquisaTestBase teste)
    {
        var veiculo = await teste.PrimeiroAsync("v1/controleOperacao/veiculo");
        if (veiculo is null)
        {
            Assert.Ignore("A empresa não tem veículo; sem ele não dá para criar a manutenção pesquisada.");
        }

        var tipo = await SemearTipoManutencaoAsync(teste);
        var manutencao = await teste.CriarAsync("v1/ControleOperacao/ManutencaoVeiculo", new JsonObject
        {
            ["veiculoId"] = veiculo!.Id(),
            ["tipoManutencaoId"] = tipo.Id(),
            ["descricao"] = PesquisaTestBase.NomeE2E("Manutencao"),
        });
        return (manutencao, tipo["nome"].ComoTexto()!);
    }

    private static async Task<string> SemearOrdemServicoAsync(PesquisaTestBase teste)
    {
        // A OS precisa vincular ao menos uma manutenção já existente; excluir a OS desvincula a manutenção.
        var (manutencao, _) = await SemearManutencaoAsync(teste);
        var os = await teste.CriarAsync("v1/ControleOperacao/ManutencaoVeiculoOS", new JsonObject
        {
            ["veiculoId"] = manutencao["veiculoId"]!.GetValue<long>(),
            ["numero"] = PesquisaTestBase.NomeE2E("OS"),
            ["operacaoBase"] = new JsonObject { ["dataHora"] = DateTime.Now.ToString("s") },
            ["manutencoes"] = new JsonArray(new JsonObject
            {
                ["id"] = manutencao.Id(),
                ["veiculoId"] = manutencao["veiculoId"]!.GetValue<long>(),
                ["tipoManutencaoId"] = manutencao["tipoManutencaoId"]!.GetValue<long>(),
            }),
        });
        return os["numero"].ComoTexto()!;
    }

    private static IEnumerable<string> FiltradosPorSearch(IRequest requisicao)
    {
        var search = HttpUtility.ParseQueryString(new Uri(requisicao.Url).Query)["search"] ?? "";
        return NomesSimulados.Where(n => n.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonArray Lista(Func<string, JsonObject> item) =>
        new(NomesSimulados.Select(n => (JsonNode)item(n)).ToArray());

    private static JsonObject Pagina(string itens, string total, IEnumerable<JsonObject> registros)
    {
        var lista = new JsonArray(registros.Select(r => (JsonNode)r).ToArray());
        return new JsonObject { [itens] = lista, [total] = lista.Count };
    }
}
