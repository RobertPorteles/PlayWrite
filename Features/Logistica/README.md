# Feature Logística — testes E2E

Testes end-to-end (Playwright + NUnit) da logística do CCO Mobility, do login até a finalização da viagem.
Usam o `ccomobility-frontend` pela interface e a `CCO.Api` para preparar, conferir e limpar dados.

Tudo desta feature fica em `Features/Logistica/`. O que é genérico (configuração, login, cliente da API,
helpers de Vuetify e SweetAlert) fica em `Shared/` e serve para outras features.

## Pré-requisitos

- Front rodando (padrão `http://localhost:5173`, via `npm run dev`).
- API rodando (padrão `http://localhost:5225`, perfil `CCO.Api.Local`).
- Usuário com acesso à logística: master, ou com as permissões de logística, incluindo **Viagem Logística (10468000)**, necessária para finalizar/cancelar.
- A empresa precisa ter tipo de operação definido (`Empresa.TipoOperacaoId` = 1 Passageiro ou 2 Carga). Com o campo vazio,
  o wizard de Programação recusa salvar; os testes de logística são ignorados logo no login, com essa explicação no resultado.
- Para os cenários com ordem de serviço, a empresa precisa ter pelo menos um veículo e um motorista. Sem eles esses testes são ignorados, com a explicação no resultado.
- Chromium do Playwright instalado: `pwsh bin/Debug/net10.0/playwright.ps1 install chromium` ou `npx playwright install chromium`.

## Variáveis de ambiente

| Variável | Obrigatória | Padrão | Uso |
|---|---|---|---|
| `E2E_LOGIN` | sim | — | Login do usuário |
| `E2E_SENHA` | sim | — | Senha do usuário |
| `E2E_EMPRESA` | se o usuário tiver mais de uma empresa | — | Nome da empresa como aparece no card do login (ou o GUID) |
| `E2E_FRONT_URL` | não | `http://localhost:5173` | URL do front |
| `E2E_API_URL` | não | `http://localhost:5225` | URL da API |
| `E2E_VEICULO` | não | primeira opção | Texto do veículo usado na ordem de serviço |
| `E2E_MOTORISTA` | não | primeira opção | Texto do motorista usado na ordem de serviço |

## Como rodar

```bash
export E2E_LOGIN=master
export E2E_SENHA='***'
export E2E_EMPRESA='DESENVOLVIMENTO (IDEAL)'

# toda a feature (headless, silencioso)
dotnet test --filter "Category=Logistica"

# só o fluxo completo, com o navegador visível
HEADED=1 dotnet test --filter "FullyQualifiedName~FluxoCompletoLogisticaTests"

# navegador visível e mais lento, para acompanhar (500ms de atraso em cada ação)
HEADED=1 SLOWMO_MS=500 dotnet test --filter "FullyQualifiedName~FluxoCompletoLogisticaTests"
```

Quando um teste falha, o trace é salvo em `bin/Debug/net10.0/playwright-traces/<teste>.zip` e anexado ao resultado.
Para abrir: `npx playwright show-trace bin/Debug/net10.0/playwright-traces/<teste>.zip`.

## Rodar passo a passo (Playwright Inspector)

Para ir avançando ação por ação (equivalente a um "next"), use o Playwright Inspector, não `HEADED`:

```bash
PWDEBUG=1 dotnet test --filter "FullyQualifiedName~FluxoCompletoLogisticaTests"
```

Isso abre o navegador e uma janela separada do Inspector, já pausados antes da primeira ação. No Inspector:
- **Step over**: executa só a próxima ação do Playwright e pausa de novo.
- **Resume**: continua até o fim (ou até o próximo `Page.PauseAsync()`, se houver um no código).

`PWDEBUG=1` já ativa o modo visível por conta própria — não precisa combinar com `HEADED=1`. Se quiser pausar num ponto
específico do teste (por exemplo, só a partir da tela de Viagem Logística), adicione temporariamente uma linha
`await Page.PauseAsync();` ali no teste antes de rodar com `PWDEBUG=1`.

Como o teste é um processo .NET rodando o driver Node.js do Playwright por baixo, o Inspector funciona normalmente; não é
necessário nenhum ajuste adicional no projeto.

## O que é coberto

| Arquivo | Cenários |
|---|---|
| `Tests/LoginLogisticaTests.cs` | Login válido com os menus de logística; senha inválida; navegação pelo menu até cada uma das 6 telas |
| `Tests/CadastrosBaseLogisticaTests.cs` | Cliente, Centro de custo, Solicitante e Local de interesse: criar pela tela, conferir na API, pesquisar e excluir |
| `Tests/ProgramacaoLogisticaTests.cs` | Wizard completo sem OS (fica **Pendente**); "Enviar OS" com veículo e motorista (fica **Programada**); validação de campo obrigatório |
| `Tests/ViagemLogisticaTests.cs` | Visualizar viagem; finalizar item (fica **Finalizada**); cancelar exige motivo (fica **Cancelada**) |
| `Tests/PesquisaRapidaLogisticaTests.cs` | A consulta da "Pesquisa rápida" de cada uma das 6 telas é aceita pela API (HTTP 200) |
| `Tests/FluxoCompletoLogisticaTests.cs` | Login → Programações pelo menu → wizard com OS → viagem iniciada → Viagem Logística pelo menu → visualizar → finalizar → conferência na tela e na API |

## Dados de teste e limpeza

- Todo registro criado tem nome com o prefixo `E2E-` e um timestamp.
- A programação usa a data de **amanhã**, para ficar fora do filtro "hoje" do CCO.Worker.
- Tudo o que o teste cria (via API ou pela tela) é registrado e excluído via API no `TearDown`, na ordem que respeita as FKs. Falhas de limpeza aparecem no log do teste, sem mascarar o resultado.

## Problemas da aplicação encontrados pelos testes

- **A pesquisa rápida retorna HTTP 500** nas telas **Cliente**, **Programações** e **Viagem logística**. O front monta
  `Contains(tolower(<coluna>), '...')` também para colunas enum (`tipoCliente`, `status`), e a API falha ao avaliar o filtro.
  Os casos correspondentes de `PesquisaRapidaLogisticaTests` ficam vermelhos até a correção, de propósito.
  Os fluxos localizam linhas sem a pesquisa rápida (paginação com 50 itens), para não serem bloqueados por esse defeito.
- **O menu do autocomplete "Cliente" fica instável no wizard de Programação** depois que datas e "Origem da solicitação" são
  preenchidas: o item é recriado continuamente e o clique não estabiliza. O helper `Vuetify.SelecionarAutocompleteAsync`
  tenta o clique e, se não conseguir, seleciona pelo teclado (↓ + Enter).
- Em Viagem logística, a pesquisa não considera a coluna Observação (`noSearch`), e o backend reescreve a observação da
  viagem ao finalizar ou cancelar. Por isso, depois dessas ações, a linha é reencontrada pela coluna "Início Viagem".

## Limitação: viagem simulada

Em produção, a `ViagemLogistica` é criada e avançada pelo **CCO.Worker** (`ViagemLogisticaWorker`, a cada 15 s), a partir das
posições GPS do veículo gravadas em `Geral.UltimoRecebido`. Nenhuma API grava essas posições. Por isso os testes criam a
viagem via `POST v1/Logistica/ViagemLogistica` já com status **Iniciada** (3), no mesmo formato que o worker usaria. A partir
daí, visualizar, finalizar e cancelar são feitos pela tela e conferidos na API.

Cobrir o worker de ponta a ponta exigiria o CCO.Worker ligado, com os workers 50/51 ativos para a empresa, e inserções SQL de
posição no banco da empresa. Isso fica como evolução futura.

## Estrutura

```
Features/Logistica/
├─ LogisticaTestBase.cs      login pela UI, API autenticada e cleanup
├─ Api/LogisticaApi.cs       criação e consulta de dados de apoio, programação e viagem via API
├─ Data/                     payloads (LogisticaDataFactory), rotas da API e cleanup
├─ Pages/                    page objects das telas e do wizard de Programação
└─ Tests/                    cenários
```

Para outra feature, crie `Features/<Feature>/` com a mesma organização, reutilize `Shared/` e marque os fixtures com
`[Category("<Feature>")]`.
