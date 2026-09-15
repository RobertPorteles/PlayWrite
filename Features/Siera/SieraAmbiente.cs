using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlaywrightTests.Features.Siera.Stub;
using PlaywrightTests.Shared.Ambiente;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Siera;

/// <summary>
/// Ambiente isolado dos testes da Siera: stub das integrações, uma CCO.Api apontada para ele e um Vite apontado
/// para essa CCO.Api. A tela e a fachada rodam de verdade sem chegar ao ccomobility-telematics-integrations nem à
/// Siera real, e as instâncias que o desenvolvedor já tem abertas (5173, 5225, 59528) não são tocadas.
/// </summary>
[SetUpFixture]
public sealed class SieraAmbiente
{
    /// <summary>Portas locais do ccomobility-telematics-integrations (launchSettings.json).</summary>
    public static readonly int[] PortasIntegracoes = [59527, 59528];

    /// <summary>Timeout da CCO.Api de teste para as integrações (o padrão de 35 s tornaria o teste de 504 lento).</summary>
    public const int TimeoutIntegracoesSegundos = 5;

    private static readonly TimeSpan LimiteBuild = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan LimiteSubida = TimeSpan.FromMinutes(2);

    private static StubIntegracoesSiera? _stub;
    private static ProcessoFilho? _api;
    private static ProcessoFilho? _vite;
    private static Uri? _apiUrl;
    private static Uri? _frontUrl;

    public static StubIntegracoesSiera Stub => _stub ?? throw NaoIniciado();

    public static Uri ApiUrl => _apiUrl ?? throw NaoIniciado();

    public static Uri FrontUrl => _frontUrl ?? throw NaoIniciado();

    /// <summary>Hosts da Siera configurados no serviço de integrações: o navegador nunca pode chegar neles.</summary>
    public static IReadOnlyList<string> HostsSiera { get; private set; } = [];

    [OneTimeSetUp]
    public async Task SubirAsync()
    {
        var pastaLogs = Directory.CreateDirectory(Path.Combine(TestContext.CurrentContext.WorkDirectory, "siera-logs")).FullName;
        HostsSiera = LerHostsSiera();

        _stub = StubIntegracoesSiera.Iniciar(PortaLivre());
        if (!_stub.Url.IsLoopback || PortasIntegracoes.Contains(_stub.Url.Port))
        {
            throw new InvalidOperationException($"O stub precisa ser local e fora das portas das integrações reais: {_stub.Url}");
        }

        var dll = await CompilarApiAsync(pastaLogs);

        _apiUrl = new Uri($"http://localhost:{PortaLivre()}/");
        TestContext.Progress.WriteLine($"Siera: CCO.Api em {_apiUrl} → stub {_stub.Url} (logs em {pastaLogs})");
        // A URL das integrações vai por argumento, que tem prioridade sobre qualquer appsettings: não há como
        // esta instância cair nas integrações reais.
        _api = ProcessoFilho.Iniciar("cco-api", "dotnet",
            [
                dll,
                "--urls", _apiUrl.GetLeftPart(UriPartial.Authority),
                $"--TelematicsSettings:BaseUrl={_stub.Url.AbsoluteUri}",
                $"--TelematicsSettings:TimeoutSeconds={TimeoutIntegracoesSegundos}",
            ],
            Path.Combine(E2ESettings.BackendDir, "CCO.Api"),
            new Dictionary<string, string?> { ["ASPNETCORE_ENVIRONMENT"] = "Local" },
            pastaLogs);
        // 401 anônimo prova que a API subiu com a autenticação ligada, sem que nada chegue ao stub. A saída da
        // API não vai para o resultado do teste: pode conter configuração do appsettings.
        await EsperarRespostaAsync(_api, new Uri(_apiUrl, "v1/Siera/queryUserVehicle"), HttpStatusCode.Unauthorized, mostrarSaida: false);

        var vite = Path.Combine(E2ESettings.FrontendDir, "node_modules", "vite", "bin", "vite.js");
        if (!File.Exists(vite))
        {
            throw new InvalidOperationException($"Vite não encontrado em {vite}. Rode npm install no front ou defina E2E_FRONTEND_DIR.");
        }

        _frontUrl = new Uri($"http://localhost:{PortaLivre()}/");
        TestContext.Progress.WriteLine($"Siera: front em {_frontUrl} → CCO.Api {_apiUrl}");
        _vite = ProcessoFilho.Iniciar("vite", "node",
            [vite, "--port", _frontUrl.Port.ToString(), "--strictPort"],
            E2ESettings.FrontendDir,
            new Dictionary<string, string?>
            {
                // Variável já presente no ambiente vence os arquivos .env do Vite.
                ["VITE_BASEAPI"] = _apiUrl.AbsoluteUri,
                ["BROWSER"] = "none",
                // Com "dev:https" o vite.config.ts troca a base da API pelo proxy HTTPS.
                ["npm_lifecycle_event"] = null,
            },
            pastaLogs);
        await EsperarRespostaAsync(_vite, _frontUrl, HttpStatusCode.OK, mostrarSaida: true);

        if (_stub.Recebidas().Count > 0)
        {
            throw new InvalidOperationException("O stub recebeu requisições durante a subida; nada deveria chamar as integrações antes dos testes.");
        }
    }

    [OneTimeTearDown]
    public async Task DerrubarAsync()
    {
        _vite?.Dispose();
        _api?.Dispose();
        if (_stub is not null)
        {
            await _stub.DisposeAsync();
        }

        _vite = null;
        _api = null;
        _stub = null;
    }

    private static async Task<string> CompilarApiAsync(string pastaLogs)
    {
        var projeto = Path.Combine(E2ESettings.BackendDir, "CCO.Api", "CCO.Api.csproj");
        // Pasta própria, ignorada pelo git do backend ([Bb]in/): não disputa arquivos com uma CCO.Api aberta no
        // debugger e não copia os appsettings para fora do repositório.
        var saida = Path.Combine(E2ESettings.BackendDir, "CCO.Api", "bin", "E2E");
        var dll = Path.Combine(saida, "CCO.Api.dll");

        if (!File.Exists(projeto))
        {
            throw new InvalidOperationException($"CCO.Api não encontrada em {projeto}. Defina E2E_BACKEND_DIR.");
        }

        if (E2ESettings.Ler("E2E_SIERA_SEM_BUILD") == "1")
        {
            return File.Exists(dll)
                ? dll
                : throw new InvalidOperationException($"E2E_SIERA_SEM_BUILD=1, mas {dll} não existe. Rode os testes uma vez sem a variável.");
        }

        TestContext.Progress.WriteLine($"Siera: compilando a CCO.Api em {saida} (E2E_SIERA_SEM_BUILD=1 pula esta etapa)");
        using var build = ProcessoFilho.Iniciar("build-cco-api", "dotnet",
            ["build", projeto, "-c", "Debug", "-o", saida, "--nologo", "-v", "q", "--disable-build-servers"],
            E2ESettings.BackendDir,
            AmbienteSemMsBuild(),
            pastaLogs);

        var codigo = await build.AguardarSaidaAsync(LimiteBuild);
        return codigo == 0 && File.Exists(dll)
            ? dll
            : throw new InvalidOperationException(
                $"O build da CCO.Api falhou (código {codigo}). Log: {build.ArquivoLog}{Environment.NewLine}{build.UltimasLinhas()}");
    }

    /// <summary>
    /// O <c>dotnet test</c> exporta variáveis do MSBuild para os processos filhos; herdadas, elas fazem o build
    /// aninhado tentar reaproveitar a infraestrutura do build dos testes.
    /// </summary>
    private static Dictionary<string, string?> AmbienteSemMsBuild() =>
        Environment.GetEnvironmentVariables().Keys
            .OfType<string>()
            .Where(chave => chave.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(chave => chave, _ => (string?)null);

    private static async Task EsperarRespostaAsync(ProcessoFilho processo, Uri url, HttpStatusCode esperado, bool mostrarSaida)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
        var limite = DateTime.UtcNow + LimiteSubida;
        HttpStatusCode? ultimo = null;

        while (DateTime.UtcNow < limite)
        {
            if (processo.Encerrou)
            {
                throw new InvalidOperationException($"{processo.Nome} encerrou durante a subida. {Diagnostico(processo, mostrarSaida)}");
            }

            try
            {
                using var resposta = await http.GetAsync(url);
                if (resposta.StatusCode == esperado)
                {
                    return;
                }

                ultimo = resposta.StatusCode;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                // Ainda subindo.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"{processo.Nome} não respondeu {(int)esperado} em {url} dentro de {LimiteSubida} " +
            $"(último status: {ultimo?.ToString() ?? "sem resposta"}). {Diagnostico(processo, mostrarSaida)}");
    }

    private static string Diagnostico(ProcessoFilho processo, bool mostrarSaida) =>
        mostrarSaida ? $"Log: {processo.ArquivoLog}{Environment.NewLine}{processo.UltimasLinhas()}" : $"Log: {processo.ArquivoLog}";

    /// <summary>
    /// Hosts da Siera no appsettings do serviço de integrações. Só os hosts são lidos: o arquivo também tem
    /// credenciais, que não saem daqui.
    /// </summary>
    private static IReadOnlyList<string> LerHostsSiera()
    {
        var arquivo = Path.Combine(E2ESettings.IntegracoesDir, "src", "CCO.Integrations.Api", "appsettings.json");
        if (!File.Exists(arquivo))
        {
            return [];
        }

        var raiz = JsonNode.Parse(File.ReadAllText(arquivo),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var siera = raiz?["Siera"];

        var hosts = new List<string>();
        if (Uri.TryCreate(siera?["BaseUrl"]?.GetValue<string>(), UriKind.Absolute, out var baseUrl))
        {
            hosts.Add(baseUrl.Host);
        }

        hosts.AddRange((siera?["Streaming"]?["AllowedHosts"] as JsonArray ?? []).Select(host => host?.GetValue<string>()).OfType<string>());

        // Um host local proibiria a própria API e o front.
        return hosts
            .Where(host => host is not ("localhost" or "127.0.0.1" or "::1"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int PortaLivre()
    {
        while (true)
        {
            var ouvinte = new TcpListener(IPAddress.Loopback, 0);
            ouvinte.Start();
            var porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
            ouvinte.Stop();

            if (!PortasIntegracoes.Contains(porta))
            {
                return porta;
            }
        }
    }

    private static InvalidOperationException NaoIniciado() =>
        new("O ambiente da Siera não foi iniciado: os testes precisam estar no namespace PlaywrightTests.Features.Siera.");
}
