using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using PlaywrightTests.Features.Equipamento.Stub;
using PlaywrightTests.Features.Logistica.Pages;
using PlaywrightTests.Shared;
using PlaywrightTests.Shared.Api;
using PlaywrightTests.Shared.Config;
using PlaywrightTests.Shared.Pages;
using static Microsoft.Playwright.Assertions;

namespace PlaywrightTests.Features.Equipamento.Tests;

/// <summary>
/// Lista geral de módulos em /equipamento (views/base/equipamento/Lista.vue): sem serial na busca, a tela carrega
/// sozinha uma tabela paginada do GET v1/associado e busca a coluna Veículo com uma chamada por linha visível.
/// Os dados vêm do <see cref="ApiEquipamentoStub"/> no navegador: a base local não tem associados, e o veículo e a
/// busca por serial sairiam da máquina para o concentrador. Só o teste de contrato vai à CCO.Api local.
/// Pré-requisitos: front e CCO.Api locais, E2E_LOGIN/E2E_SENHA/E2E_EMPRESA de um usuário master.
/// </summary>
[TestFixture]
[Category(Categoria)]
public class ListaEquipamentoTests : E2ETestBase
{
    public const string Categoria = "Equipamento";

    private const string Rota = "/equipamento";

    private static readonly IReadOnlyList<Modulo> Pagina1 = ApiEquipamentoStub.Modulos.Take(10).ToList();

    private static readonly IReadOnlyList<Modulo> Pagina2 = ApiEquipamentoStub.Modulos.Skip(10).ToList();

    private ApiEquipamentoStub _stub = null!;

    private LogisticaListPage _lista = null!;

    [SetUp]
    public async Task PrepararAsync()
    {
        _stub = await ApiEquipamentoStub.InstalarAsync(Page);
        _lista = new LogisticaListPage(Page, Rota, "Equipamentos");
    }

    [TearDown]
    public void ConferirChamadasLegadas()
    {
        if (_stub is null)
        {
            return;
        }

        Assert.That(_stub.Legadas, Is.Empty,
            "A tela fez chamadas legadas não previstas no stub (que iriam ao concentrador pela API).");
    }

    [Test]
    public async Task SemSerialDeveCarregarListaPaginadaAoEntrar()
    {
        await AbrirListaAsync();

        var chamada = _stub.Listas.Single();
        Assert.Multiple(() =>
        {
            Assert.That(new Uri(chamada.Url).Authority, Is.EqualTo(new Uri(E2ESettings.ApiUrl).Authority),
                "O front não está chamando a CCO.Api local.");
            Assert.That(chamada.Url, Does.Contain("$Top=10").And.Contain("$Skip=0"));
            Assert.That(chamada.Include, Is.EqualTo(ApiEquipamentoStub.Include));
            Assert.That(_stub.Buscas, Is.Empty, "Sem serial, a tela não deveria usar a busca por serial.");
        });

        var cabecalhos = (await _lista.Tabela.Locator("thead th").AllInnerTextsAsync()).Select(c => c.Trim());
        Assert.That(cabecalhos, Is.SupersetOf(new[] { "Serial Number", "Modelo", "Veículo", "Chip", "Operadora", "Cliente" }));

        var modulo = Pagina1[0];
        await Expect(await CelulaAsync(modulo, "Modelo")).ToHaveTextAsync(modulo.ModeloEquipamento);
        await Expect(await CelulaAsync(modulo, "Chip")).ToHaveTextAsync(modulo.Iccid);
        await Expect(await CelulaAsync(modulo, "Cliente")).ToHaveTextAsync(modulo.Cliente!);
        await Expect(_lista.Tabela.Locator(".v-data-table-footer__info")).ToHaveTextAsync(new Regex(@"1\D+10\D+12"));
        await Expect(Page.GetByText("Módulo: ")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ColunaVeiculoDeveMostrarIdentificadorPlacaOuHifen()
    {
        await AbrirListaAsync();

        // AbrirListaAsync já conferiu a célula Veículo de cada linha da página 1.
        Assert.That(_stub.ConsultasVeiculo, Is.EquivalentTo(Consultas(Pagina1)),
            "Deveria haver exatamente uma consulta por linha com cliente integrado, e nenhuma para as demais.");
    }

    [Test]
    public async Task TrocarDePaginaDeveBuscarNaApiEConsultarVeiculoSoDaPaginaVisivel()
    {
        await AbrirListaAsync();
        var consultasAntes = _stub.ConsultasVeiculo.Count;

        var proxima = _lista.Tabela.Locator(".v-data-table-footer__pagination button").Nth(2);
        var requisicao = await Page.RunAndWaitForRequestAsync(() => proxima.ClickAsync(), ApiEquipamentoStub.EhLista);
        Assert.That(Uri.UnescapeDataString(requisicao.Url), Does.Contain("$Top=10").And.Contain("$Skip=10"));

        await EsperarVeiculosAsync(Pagina2);
        await Expect(_lista.Linha(Pagina1[0].Serial)).ToHaveCountAsync(0);
        Assert.That(_stub.ConsultasVeiculo.Skip(consultasAntes), Is.EquivalentTo(Consultas(Pagina2)));
    }

    [Test]
    public async Task AtualizarSemSerialDeveRecarregarLista()
    {
        await AbrirListaAsync();

        await Page.RunAndWaitForRequestAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true }).ClickAsync(),
            ApiEquipamentoStub.EhLista);

        var esperadas = Consultas(Pagina1).Count * 2;
        await EsperarAsync(() => _stub.ConsultasVeiculo.Count == esperadas,
            () => $"Esperava {esperadas} consultas de veículo após Atualizar; houve {_stub.ConsultasVeiculo.Count}.");
        Assert.That(_stub.Listas, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AtualizacaoAutomaticaSemSerialDeveRecarregarLista()
    {
        await Page.Clock.InstallAsync();
        await AbrirListaAsync();

        await Page.Locator(".auto-update-checkbox input").CheckAsync();

        // Intervalo padrão de 30s: antes dele nada; logo depois, a lista é pedida de novo.
        await Page.Clock.RunForAsync(29_000);
        Assert.That(_stub.Listas, Has.Count.EqualTo(1), "A lista recarregou antes do intervalo.");

        await Page.Clock.RunForAsync(2_000);
        await EsperarAsync(() => _stub.Listas.Count == 2,
            () => $"A atualização automática não recarregou a lista (chamadas: {_stub.Listas.Count}).");
    }

    [Test]
    public async Task BuscaPorSerialDeveMostrarResultadoEVoltarParaListaAoRemoverSeriais()
    {
        await AbrirListaAsync();
        var (primeiro, segundo) = (Pagina1[0], Pagina1[1]);

        await AdicionarSerialAsync(primeiro.Serial);
        await AdicionarSerialAsync(segundo.Serial);
        await BuscarAsync();

        Assert.That(_stub.Buscas.Last(), Is.EquivalentTo(new[] { primeiro.Serial, segundo.Serial }));
        await Expect(_lista.Linha(primeiro.Serial)).ToBeVisibleAsync();
        await Expect(_lista.Linha(segundo.Serial)).ToBeVisibleAsync();
        await Expect(_lista.Linha(Pagina1[2].Serial)).ToHaveCountAsync(0);
        Assert.That(_stub.Listas, Has.Count.EqualTo(1), "Com serial na busca, a lista geral não deveria ser pedida.");

        var fecharChip = Page.Locator(".v-combobox .v-chip__close");
        await Page.RunAndWaitForRequestAsync(() => fecharChip.First.ClickAsync(), ApiEquipamentoStub.EhBusca);
        await Expect(fecharChip).ToHaveCountAsync(1);
        Assert.That(_stub.Listas, Has.Count.EqualTo(1), "Ainda há serial na busca; a lista geral não deveria ser pedida.");

        await Page.RunAndWaitForRequestAsync(() => fecharChip.First.ClickAsync(), ApiEquipamentoStub.EhLista);
        await EsperarVeiculosAsync(Pagina1);
    }

    [Test]
    public async Task BuscaPorUmSerialDeveMostrarDetalhe()
    {
        await AbrirListaAsync();
        var modulo = Pagina1[0];

        await AdicionarSerialAsync(modulo.Serial);
        await BuscarAsync();

        await Expect(Page.GetByText($"Módulo: {modulo.Serial}")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Columnheader, new() { Name = "Serial Number", Exact = true })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ComSeriaisSalvosNaoDeveCarregarListaGeralAoEntrar()
    {
        var seriais = new[] { Pagina1[0].Serial, Pagina1[1].Serial };
        await Page.AddInitScriptAsync(
            $"localStorage.setItem('monitoredSerialNumbers', JSON.stringify({JsonSerializer.Serialize(seriais)}))");
        await EntrarAsync();

        await Page.RunAndWaitForResponseAsync(() => Page.GotoAsync(Rota), r => ApiEquipamentoStub.EhBusca(r.Request));
        await Expect(_lista.Linha(seriais[0])).ToBeVisibleAsync();
        await Expect(_lista.Linha(seriais[1])).ToBeVisibleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_stub.Listas, Is.Empty,
                "Voltando com seriais salvos, a tela pediu a lista geral antes de aplicar a busca.");
            Assert.That(_stub.ConsultasVeiculo, Is.Empty,
                "Voltando com seriais salvos, a tela consultou o veículo de cada linha da lista geral.");
        });
    }

    /// <summary>
    /// Sem navegador, na CCO.Api local (só Postgres): o include e cada ordenação que a tabela paginada manda precisam
    /// ser aceitos pelo OData. A base local vazia basta, porque o caminho do $OrderBy é validado mesmo sem linhas.
    /// </summary>
    [Test]
    public async Task ListaDeAssociadosDeveAceitarIncludeEOrdenacoesDaTela()
    {
        await using var api = await ApiClient.LoginAsync(Playwright, E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);
        string[] ordenacoes =
        [
            "equipamento/serialNumber", "equipamento/modeloEquipamento/modelo", "chip/iccid", "chip/operadora",
            "cliente/nome", "dataHoraRastreio", "latitude", "longitude",
        ];

        var respostas = new List<ApiResposta>();
        foreach (var ordenacao in ordenacoes)
        {
            respostas.Add(await api.EnviarAsync("GET", $"v1/associado?$Top=10&$Skip=0&$OrderBy={ordenacao}",
                include: ApiEquipamentoStub.Include));
        }

        Assert.Multiple(() =>
        {
            foreach (var resposta in respostas)
            {
                Assert.That(resposta.Status, Is.EqualTo(200), $"{resposta.Caminho}: {resposta.Corpo}");
                Assert.That(resposta.Json?["items"], Is.InstanceOf<JsonArray>(), resposta.Caminho);
                Assert.That(resposta.Json?["count"]?.GetValueKind(), Is.EqualTo(JsonValueKind.Number), resposta.Caminho);
            }
        });
    }

    private Task EntrarAsync() =>
        new LoginPage(Page).LoginAsync(E2ESettings.Login, E2ESettings.Senha, E2ESettings.Empresa);

    /// <summary>Entra na tela sem serial e espera a página 1 com a coluna Veículo resolvida.</summary>
    private async Task AbrirListaAsync()
    {
        await EntrarAsync();
        await Page.RunAndWaitForRequestAsync(() => Page.GotoAsync(Rota), ApiEquipamentoStub.EhLista);
        await EsperarVeiculosAsync(Pagina1);
    }

    private async Task EsperarVeiculosAsync(IEnumerable<Modulo> modulos)
    {
        foreach (var modulo in modulos)
        {
            await Expect(await CelulaAsync(modulo, "Veículo")).ToHaveTextAsync(modulo.VeiculoNaTela);
        }
    }

    private async Task<ILocator> CelulaAsync(Modulo modulo, string coluna)
    {
        var linha = _lista.Linha(modulo.Serial).First;
        await Expect(linha).ToBeVisibleAsync();
        return await _lista.CelulaAsync(linha, coluna);
    }

    private async Task AdicionarSerialAsync(string serial)
    {
        var campo = Page.GetByLabel("Buscar módulo", new() { Exact = true });
        await campo.FillAsync(serial);
        await campo.PressAsync("Enter");
        await Expect(Page.Locator(".v-combobox .v-chip", new() { HasText = serial })).ToBeVisibleAsync();
    }

    /// <summary>Adicionar o serial não dispara a busca; como antes da mudança, ela sai pelo Atualizar.</summary>
    private Task BuscarAsync() =>
        Page.RunAndWaitForResponseAsync(
            () => Page.GetByRole(AriaRole.Button, new() { Name = "Atualizar", Exact = true }).ClickAsync(),
            r => ApiEquipamentoStub.EhBusca(r.Request));

    private static List<ConsultaVeiculo> Consultas(IEnumerable<Modulo> modulos) =>
        modulos.Where(m => m.IntegracaoId is not null).Select(m => new ConsultaVeiculo(m.IntegracaoId!.Value, m.Serial)).ToList();

    private static async Task EsperarAsync(Func<bool> condicao, Func<string> mensagem)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
            {
                Assert.Fail(mensagem());
            }

            await Task.Delay(100);
        }
    }
}
