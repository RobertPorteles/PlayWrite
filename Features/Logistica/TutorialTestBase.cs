using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using PlaywrightTests.Shared.Config;

namespace PlaywrightTests.Features.Logistica;

/// <summary>
/// Base dos tutoriais em vídeo: legenda fixa no rodapé da tela, pausa para leitura e prévia de planilha baixada.
/// Para assistir: <c>HEADED=1 SLOWMO_MS=400</c> (navegador visível) ou <c>VIDEO=1</c> (grava em <c>videos/</c>). Nesses
/// modos cada legenda fica alguns segundos na tela; <c>TUTORIAL_PAUSA_MS</c> muda esse tempo.
/// </summary>
public abstract class TutorialTestBase : LogisticaTestBase
{
    protected static int PausaLeitura =>
        int.TryParse(E2ESettings.Ler("TUTORIAL_PAUSA_MS"), out var pausa) ? pausa
        : Environment.GetEnvironmentVariable("HEADED") == "1" || E2ESettings.Ler("VIDEO_DIR") is not null ? 3000
        : 0;

    /// <summary>Onde salvar arquivos baixados: junto dos vídeos quando gravando, senão na pasta do build.</summary>
    protected static string PastaArquivos => E2ESettings.Ler("VIDEO_DIR") ?? TestContext.CurrentContext.WorkDirectory;

    /// <summary>Legenda fixa no rodapé da tela (sem bloquear cliques), repetida no console do teste.</summary>
    protected async Task NarrarAsync(string passo, string texto)
    {
        await Page.EvaluateAsync(
            """
            ([passo, texto]) => {
                let legenda = document.getElementById('tutorial-legenda');
                if (!legenda) {
                    legenda = document.createElement('div');
                    legenda.id = 'tutorial-legenda';
                    legenda.style.cssText = 'position:fixed;left:50%;bottom:24px;transform:translateX(-50%);z-index:99999;'
                        + 'width:min(900px,90vw);padding:14px 20px;border-radius:10px;background:rgba(20,22,26,.92);color:#fff;'
                        + 'font:15px/1.45 sans-serif;box-shadow:0 6px 24px rgba(0,0,0,.35);pointer-events:none';
                    document.body.appendChild(legenda);
                }
                const titulo = document.createElement('strong');
                titulo.textContent = passo;
                const corpo = document.createElement('div');
                corpo.textContent = texto;
                legenda.replaceChildren(titulo, corpo);
            }
            """,
            new[] { passo, texto });
        TestContext.Progress.WriteLine($"{passo} — {texto}");
        await PausarAsync();
    }

    /// <summary>
    /// Mostra na tela uma prévia da planilha baixada (as colunas pedidas, como abririam no Excel), para o vídeo exibir o
    /// resultado da exportação, que o navegador baixa sem mostrar. Some ao navegar.
    /// </summary>
    protected async Task MostrarPrevisaoPlanilhaAsync(string arquivo, IReadOnlyList<IReadOnlyList<string>> linhas, params string[] colunas)
    {
        var indices = colunas.Select(c => linhas[0].ToList().IndexOf(c)).ToArray();
        var tabela = linhas.Select(l => indices.Select(i => i >= 0 && i < l.Count ? l[i] : "").ToArray()).ToArray();

        await Page.EvaluateAsync(
            """
            ([arquivo, tabela]) => {
                document.getElementById('tutorial-planilha')?.remove();
                const painel = document.createElement('div');
                painel.id = 'tutorial-planilha';
                painel.style.cssText = 'position:fixed;left:50%;top:90px;transform:translateX(-50%);z-index:99998;width:min(1500px,96vw);'
                    + 'max-height:62vh;overflow:auto;padding:16px;border-radius:10px;background:#fff;color:#1f2328;'
                    + 'font:13px/1.4 sans-serif;box-shadow:0 10px 40px rgba(0,0,0,.45);pointer-events:none';
                const titulo = document.createElement('div');
                titulo.style.cssText = 'font-weight:700;margin-bottom:10px;color:#1d6f42';
                titulo.textContent = '📄 ' + arquivo + ' — aba "Viagens"';
                const t = document.createElement('table');
                t.style.cssText = 'border-collapse:collapse;width:100%;table-layout:fixed;font-size:12px';
                tabela.forEach((linha, i) => {
                    const tr = document.createElement('tr');
                    linha.forEach(valor => {
                        const celula = document.createElement(i === 0 ? 'th' : 'td');
                        celula.textContent = valor;
                        // Quebra dentro da célula: nomes longos não podem empurrar colunas para fora do vídeo.
                        celula.style.cssText = 'border:1px solid #d0d7de;padding:5px 8px;text-align:left;vertical-align:top;'
                            + 'overflow-wrap:anywhere;'
                            + (i === 0 ? 'background:#428bca;color:#fff' : '');
                        tr.appendChild(celula);
                    });
                    t.appendChild(tr);
                });
                painel.replaceChildren(titulo, t);
                document.body.appendChild(painel);
            }
            """,
            new object[] { arquivo, tabela });
        await PausarAsync();
    }

    protected Task EsconderPrevisaoPlanilhaAsync() =>
        Page.EvaluateAsync("() => document.getElementById('tutorial-planilha')?.remove()");

    /// <summary>
    /// Linhas da primeira aba de um .xlsx (zip de XMLs), com as datas e horas já formatadas. A primeira linha é o
    /// cabeçalho. Colunas "Data ..." e "Hora ..." vêm como número serial do Excel e são convertidas.
    /// </summary>
    protected static List<IReadOnlyList<string>> LinhasDaPlanilha(string caminho)
    {
        XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var zip = ZipFile.OpenRead(caminho);
        var compartilhadas = zip.GetEntry("xl/sharedStrings.xml") is { } entrada
            ? Carregar(entrada).Descendants(m + "si").Select(si => string.Concat(si.Descendants(m + "t").Select(t => t.Value))).ToList()
            : [];
        var aba = Carregar(zip.GetEntry("xl/worksheets/sheet1.xml")!);

        var linhas = new List<IReadOnlyList<string>>();
        foreach (var linha in aba.Descendants(m + "row"))
        {
            var valores = new List<string>();
            foreach (var celula in linha.Elements(m + "c"))
            {
                // A célula diz a própria coluna ("C2"): as vazias não aparecem no XML.
                var coluna = IndiceColuna(celula.Attribute("r")!.Value);
                while (valores.Count < coluna)
                {
                    valores.Add("");
                }

                var valor = celula.Element(m + "v")?.Value;
                valores.Add(celula.Attribute("t")?.Value switch
                {
                    "s" => compartilhadas[int.Parse(valor!, CultureInfo.InvariantCulture)],
                    "inlineStr" => string.Concat(celula.Descendants(m + "t").Select(t => t.Value)),
                    _ => valor ?? "",
                });
            }

            linhas.Add(valores);
        }

        var cabecalho = linhas[0];
        foreach (var linha in linhas.Skip(1).Cast<List<string>>())
        {
            for (var i = 0; i < linha.Count && i < cabecalho.Count; i++)
            {
                if (!double.TryParse(linha[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
                {
                    continue;
                }

                if (cabecalho[i].StartsWith("Data", StringComparison.Ordinal))
                {
                    linha[i] = DateTime.FromOADate(serial).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                }
                else if (cabecalho[i].StartsWith("Hora", StringComparison.Ordinal))
                {
                    linha[i] = DateTime.FromOADate(serial).ToString("HH:mm", CultureInfo.InvariantCulture);
                }
            }
        }

        return linhas;

        static XDocument Carregar(ZipArchiveEntry entrada)
        {
            using var stream = entrada.Open();
            return XDocument.Load(stream);
        }

        static int IndiceColuna(string referencia) =>
            referencia.TakeWhile(char.IsLetter).Aggregate(0, (soma, letra) => soma * 26 + (letra - 'A' + 1)) - 1;
    }

    private async Task PausarAsync()
    {
        if (PausaLeitura > 0)
        {
            await Page.WaitForTimeoutAsync(PausaLeitura);
        }
    }
}
