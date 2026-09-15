# Feature Siera — testes E2E de integração

Testes end-to-end (Playwright + NUnit) da tela `/siera` ("Vídeo e Monitoramento") integrada à `CCO.Api`.
Eles provam que:

1. a tela funciona com a CCO.Api real (fachada `v1/Siera/*`);
2. **o navegador só conversa com a CCO.Api** — nunca com o `ccomobility-telematics-integrations` nem com a Siera;
3. a fachada exige token e perfil master e recorta os dados pela empresa da sessão;
4. sucesso e falhas das integrações voltam coerentes, sem o token do usuário ser repassado a elas.

O serviço de integrações não tem autenticação e usa a conta técnica compartilhada da Siera; se o front falasse
com ele direto, qualquer um leria a telemetria da frota inteira. É isso que estes testes guardam.

## Nada toca a Siera real

O serviço de integrações local está configurado com a Siera de verdade. Por isso os testes **não usam** as
instâncias que você já tem abertas: um `SetUpFixture` (`SieraAmbiente.cs`) sobe um ambiente próprio e o derruba
no fim.

```
Chromium (Playwright)
  └─ Vite próprio         localhost:<porta livre>   VITE_BASEAPI → CCO.Api abaixo
       └─ CCO.Api própria  localhost:<porta livre>   Local, --TelematicsSettings:BaseUrl → stub
             └─ Stub das integrações  127.0.0.1:<porta livre>  (dentro do processo de teste)
```

- A CCO.Api é compilada em `ccomobility-backend/CCO.Api/bin/E2E` (ignorada pelo git) e recebe a URL do stub por
  argumento de linha de comando, que vence qualquer appsettings.
- O stub (`Stub/StubIntegracoesSiera.cs`) responde o que cada teste programar e registra o que recebeu.
- As instâncias em 5173, 5225 e 59528 não são tocadas.
- Saída dos processos: `bin/Debug/net10.0/siera-logs/` (`build-cco-api.log`, `cco-api.log`, `vite.log`).

## Pré-requisitos

- `PostGIS` e `MongoDB` rodando (`docker start PostGIS MongoDB`) — a CCO.Api de teste usa os mesmos bancos locais.
- `npm install` feito no `ccomobility-frontend`.
- Chromium do Playwright instalado (veja `Features/Logistica/README.md`).

### Setup único local

**1. Código de integração da empresa.** A fachada recorta a frota da Siera por `Empresa.IntegracaoCodigo` e recusa
a consulta sem ele. Qualquer número serve — o stub monta a frota com o valor que estiver na empresa:

```bash
docker exec -it PostGIS psql -U postgres -d ccomobility-dev
```
```sql
update "Base"."Empresa" set "IntegracaoCodigo" = 990001 where "Nome" = 'DESENVOLVIMENTO (IDEAL)';
```

Sem isso, os testes que precisam de dados são ignorados com essa instrução no resultado.

**2. Usuário não-master (opcional).** Todos os usuários locais são master. Para provar o 403 da fachada, crie um
usuário sem perfil master vinculado à empresa e informe `E2E_LOGIN_NAO_MASTER` / `E2E_SENHA_NAO_MASTER`. Sem elas,
esse teste é ignorado.

## Variáveis de ambiente

| Variável | Obrigatória | Padrão | Uso |
|---|---|---|---|
| `E2E_LOGIN` / `E2E_SENHA` / `E2E_EMPRESA` | para os testes com dados | — | Usuário master, como na logística |
| `E2E_LOGIN_NAO_MASTER` / `E2E_SENHA_NAO_MASTER` | não | — | Usuário sem master, para o teste de 403 |
| `E2E_BACKEND_DIR` | não | `../ccomobility-backend` | Raiz do backend |
| `E2E_FRONTEND_DIR` | não | `../ccomobility-frontend/ccomobility-frontend` | Raiz do front |
| `E2E_INTEGRACOES_DIR` | não | `../ccomobility-telematics-integrations` | De onde são lidos (só) os hosts da Siera a proibir |
| `E2E_SIERA_SEM_BUILD` | não | — | `1` reaproveita o build anterior da CCO.Api |

## Como rodar

```bash
export E2E_LOGIN=master
export E2E_SENHA='***'
export E2E_EMPRESA='DESENVOLVIMENTO (IDEAL)'

# toda a feature (a primeira execução compila a CCO.Api)
dotnet test --filter "Category=Siera"

# sem recompilar a CCO.Api, com o navegador visível
E2E_SIERA_SEM_BUILD=1 HEADED=1 dotnet test --filter "FullyQualifiedName~IsolamentoRedeSieraTests"
```

Sem `E2E_LOGIN`, rodam só os testes que não dependem de usuário (401 anônimo e o autoteste do monitor de rede).

## Como a rede é verificada

`Shared/Rede/MonitorRede.cs` registra toda requisição do navegador e reprova:

- **chamada de dados** (XHR, fetch, WebSocket, EventSource) para qualquer origem que não seja a CCO.Api — a única
  exceção é o WebSocket de HMR do próprio Vite;
- **qualquer requisição** às portas das integrações (59527/59528), aos hosts da Siera configurados nelas ou ao
  stub — exceto o documento do iframe cuja URL é exatamente a `playerUrl` que a CCO.Api devolveu.

Assets estáticos de terceiros que o `index.html` já carrega em todas as telas (fontes, CDNs, tiles do mapa) não
reprovam. O teste `MonitorRede_ReprovaChamadaDiretaAsIntegracoes` garante que o detector de fato pega uma chamada
direta.

## O que é coberto

| Arquivo | Cenários |
|---|---|
| `Tests/IsolamentoRedeSieraTests.cs` | Percorre Telemetria, Alarmes (ativos e histórico) e Vídeo sem nenhuma chamada fora da CCO.Api; iframe usa exatamente a `playerUrl` da CCO.Api; autoteste do detector |
| `Tests/IntegracaoTelaSieraTests.cs` | Tela mostra só a frota da empresa; consulta chega às integrações pela CCO.Api, sem token e com correlação; aviso traduzido para indisponibilidade e timeout; guarda da rota barra sessão sem master |
| `Tests/ContratoFachadaSieraTests.cs` | 401 sem token em todas as rotas da tela; 403 para não-master; recorte da frota; 403 para rastreador de outra empresa; histórico restrito aos rastreadores da empresa; corpo repassado com casing original; erro com/sem `code`, queda e lentidão viram 404/502/502/504 com a `messageKey` certa |

## Limitações conhecidas

- A tela não tem item de menu (está comentado em `PageBase.vue`), então é aberta pela URL.
- O download de evidência de alarme não é exercitado: exigiria um ZIP no stub.
- O serviço de integrações aplica CORS liberando `http://localhost:5173` em todas as rotas e não tem autenticação.
  Estes testes provam que o front não o chama, mas o serviço em si não impede um navegador de chamá-lo.
