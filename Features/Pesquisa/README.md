# Feature Pesquisa — "Pesquisa rápida" de todas as telas

Testes E2E do campo **Pesquisa rápida** em todas as telas do front que o exibem. A logística fica em
`Features/Logistica/Tests/PesquisaRapidaLogisticaTests.cs`.

Pré-requisitos e variáveis de ambiente: os mesmos da logística (`Features/Logistica/README.md`), ou seja, front, CCO.Api
e Postgres **locais** e usuário master.

```bash
dotnet test --filter "Category=Pesquisa"
HEADED=1 dotnet test --filter "FullyQualifiedName~PesquisaRapidaTests&Name~estoque"
```

## O que cada teste confere

| Teste | Telas | Verificação |
|---|---|---|
| `PesquisaRapida_FiltraATabela` | 30 telas com tabela (`Data/TelasPesquisa.cs`) | Com um termo existente, ficam só as linhas que o contêm. Com um termo inexistente, a tabela fica vazia. |
| `PesquisaRapida_FiltraOsCards` | `/subsidio`, `/relatorioEmpresaCliente`, `/relatorioProduto`, `/orgaoGestor` | O título de um card deixa só os cards que o contêm. Um termo inexistente esconde todos. |
| `PesquisaRapida_ChipDiscrepancia` | `/chipDiscrepancia` | Com Enter, a lista da Algar (simulada) fica filtrada. |
| `PesquisaRapida_ModuloClienteChip` | `/moduloClienteChip` | O termo chega no corpo do `POST moduloClienteChip/obterdados`. |

Em todos os testes também se exige que não haja:
- resposta 4xx/5xx da CCO.Api;
- exceção JavaScript;
- alerta na tela;
- chamada legada (`clienteAntigo`, `Concentrador`, `ConectaWEB`) sem resposta prevista pelo teste.

## De onde vem o termo pesquisado

1. **Dado real:** o campo pesquisável do primeiro registro de `GET {recurso}?$Top=1`. Nesse caso nada é gravado.
2. **Sem registro:** o teste cria um pela API, com nome `E2E-PESQ-{entidade}-{timestamp}`, e cria também as
   dependências (área, tipo de manutenção, manutenção etc.). Tudo o que ele cria é excluído no `TearDown`, em ordem inversa.
   Os DELETEs do backend são físicos. Se o processo for interrompido, podem sobrar registros com `E2E-PESQ-` no nome.
3. **Lista simulada no navegador** (`Stub/StubNavegador.cs`) em dois casos. Nesses casos o termo é `E2E-PESQ-A`.
   - A origem é externa: GTFS (`/stop`), Órgão Gestor (`/permissionario`) e Concentrador/ConectaWeb (`/manutencaoGerenciada`, telas de chip).
   - A tela nem chega a mandar a pesquisa para a API: `/linha`, `/veiculo`, `/bom` e `/registroRequisicaoMapa`.

Telas sem registro e sem forma de semear (`/funcionario`, `/rota`, `/usuario` e `/empresa`, que dependem do ambiente)
conferem só o termo inexistente e ficam como **ignoradas**, com a explicação.

## Problemas da aplicação encontrados pelos testes

Os casos abaixo ficam vermelhos de propósito, e a mensagem de falha cita o defeito.

| Tela | Defeito |
|---|---|
| `/rota` | O filtro inclui `ativa`, `distanciaMetros`, `tempoPrevisto` e `valor` em `Contains(tolower(...))`, e a API retorna **HTTP 500**. |
| `/tempoDeslocamento` | O mesmo problema com `tempoDeslocamentoMinutos`, e a API retorna **HTTP 500**. |
| `/tipoManutencao`, `/usuarioLinhaEmpresa` | Um cabeçalho de grupo sem `value`/`path` quebra `Service.GetParamentroPaginacaoOrdenacao` (`undefined.replaceAll`). O front mostra "Não foi possível acessar a API." e não faz a consulta. |
| `/agency` | A tela lista por `GET v1/agency` (OData), mas o `AgencyController` só tem `ListarTudo` e `Autocomplete`, e a API retorna **HTTP 404**. |
| `/linha`, `/veiculo`, `/bom`, `/registroRequisicaoMapa` | O campo aparece, mas digitar nele não filtra nada. |
| Logística: Cliente, Programações, Viagem logística | `Contains(tolower(...))` em colunas enum, e a API retorna **HTTP 500** (já descrito no README da logística). |

## Estrutura

```
Features/Pesquisa/
├─ PesquisaTestBase.cs     login, criação de dados com cleanup, erros da API/JS e stub no navegador
├─ Data/TelasPesquisa.cs   catálogo das telas: rota, recurso, campo, como semear ou simular, defeito conhecido
├─ Pages/PesquisaPage.cs   campo visível e a tabela que vem depois dele
├─ Stub/StubNavegador.cs   respostas simuladas e bloqueio de chamadas legadas
└─ Tests/PesquisaRapidaTests.cs
```

Para cobrir uma tela nova com tabela, acrescente uma linha em `TelasPesquisa.Tabelas()`.
