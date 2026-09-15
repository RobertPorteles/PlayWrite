using System.Collections.Concurrent;
using System.Diagnostics;

namespace PlaywrightTests.Shared.Ambiente;

/// <summary>
/// Processo auxiliar dos testes (build, API, dev server): saída gravada em arquivo e encerramento da árvore
/// inteira, inclusive se o runner morrer sem passar pelo TearDown.
/// </summary>
public sealed class ProcessoFilho : IDisposable
{
    private const int LinhasGuardadas = 60;

    private readonly Process _processo;
    private readonly StreamWriter _log;
    private readonly ConcurrentQueue<string> _ultimasLinhas = new();
    private readonly object _travaLog = new();

    private ProcessoFilho(string nome, string arquivoLog, Process processo, StreamWriter log)
    {
        Nome = nome;
        ArquivoLog = arquivoLog;
        _processo = processo;
        _log = log;
    }

    public string Nome { get; }

    public string ArquivoLog { get; }

    public bool Encerrou => _processo.HasExited;

    /// <param name="ambiente">Variáveis a definir; valor nulo remove a variável herdada.</param>
    public static ProcessoFilho Iniciar(string nome, string executavel, IEnumerable<string> argumentos, string pasta,
        IReadOnlyDictionary<string, string?> ambiente, string pastaLogs)
    {
        var inicio = new ProcessStartInfo(executavel)
        {
            WorkingDirectory = pasta,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argumento in argumentos)
        {
            inicio.ArgumentList.Add(argumento);
        }

        foreach (var (chave, valor) in ambiente)
        {
            if (valor is null)
            {
                inicio.Environment.Remove(chave);
            }
            else
            {
                inicio.Environment[chave] = valor;
            }
        }

        var arquivoLog = Path.Combine(pastaLogs, $"{nome}.log");
        var log = new StreamWriter(arquivoLog, append: false) { AutoFlush = true };
        var processo = new Process { StartInfo = inicio };
        var filho = new ProcessoFilho(nome, arquivoLog, processo, log);

        processo.OutputDataReceived += (_, e) => filho.Registrar(e.Data);
        processo.ErrorDataReceived += (_, e) => filho.Registrar(e.Data);

        try
        {
            processo.Start();
        }
        catch (Exception e)
        {
            log.Dispose();
            throw new InvalidOperationException($"Não foi possível iniciar {nome} ({executavel}): {e.Message}", e);
        }

        processo.BeginOutputReadLine();
        processo.BeginErrorReadLine();
        AppDomain.CurrentDomain.ProcessExit += filho.AoSairDoRunner;

        return filho;
    }

    public async Task<int> AguardarSaidaAsync(TimeSpan limite)
    {
        using var cancelamento = new CancellationTokenSource(limite);
        try
        {
            await _processo.WaitForExitAsync(cancelamento.Token);
        }
        catch (OperationCanceledException)
        {
            Encerrar();
            throw new TimeoutException($"{Nome} não terminou em {limite}. Log: {ArquivoLog}");
        }

        return _processo.ExitCode;
    }

    /// <summary>Últimas linhas da saída. Não usar para processos que possam escrever configuração sensível.</summary>
    public string UltimasLinhas() => string.Join(Environment.NewLine, _ultimasLinhas);

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= AoSairDoRunner;
        Encerrar();
        _processo.Dispose();
        lock (_travaLog)
        {
            _log.Dispose();
        }
    }

    private void Registrar(string? linha)
    {
        if (linha is null)
        {
            return;
        }

        lock (_travaLog)
        {
            try
            {
                _log.WriteLine(linha);
            }
            catch (ObjectDisposedException)
            {
                // Saída que chegou depois do Dispose.
            }
        }

        _ultimasLinhas.Enqueue(linha);
        while (_ultimasLinhas.Count > LinhasGuardadas)
        {
            _ultimasLinhas.TryDequeue(out _);
        }
    }

    private void AoSairDoRunner(object? remetente, EventArgs argumentos) => Encerrar();

    private void Encerrar()
    {
        try
        {
            if (!_processo.HasExited)
            {
                _processo.Kill(entireProcessTree: true);
                _processo.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Já encerrado.
        }
    }
}
