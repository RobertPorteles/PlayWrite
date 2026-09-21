# Feature Atendimento — testes E2E

Testes do **modo atendimento**: o menu lateral restrito ao grupo "Atendimento" (Módulos, Rotas, Chip, Estoque,
Malote...). Há duas formas de entrar nele:

- **Master, ou quem já está logado numa empresa:** item "Atendimento" (ícone de telefone) do menu do avatar. Ele
  chama `POST v1/autenticador/EntrarAtendimento`, com o token da sessão.
- **Usuário com o perfil de atendimento:** entra direto pelo login. Não passa pela escolha de empresa. Se também
  tiver empresas, troca depois pelo botão da fábrica.

Eles também guardam a correção de segurança: `POST v1/autenticador/atendimento` (anônimo, usado no rail da tela de
login) passou a exigir login **e senha**.

## Dados

A base local não tem perfis e todos os usuários locais são master. Por isso, `UsuariosAtendimento.cs` (um
`SetUpFixture`) cria pela CCO.Api, como master (`E2E_LOGIN`):

- o perfil `E2E Atendimento <sufixo>`, com todas as funcionalidades de atendimento (811000–811015);
- `e2e.atend.so.<sufixo>@e2e.local`, que tem só o vínculo sem empresa;
- `e2e.atend.misto.<sufixo>@e2e.local`, com o vínculo sem empresa e também a empresa de `E2E_EMPRESA`.

A senha é a padrão do cadastro (`123@mudar`). Tudo é apagado no fim. Antes de criar, a fixture remove sobras com
esses prefixos de execuções abortadas.

Na vida real, o mesmo arranjo é feito pelas telas: `/perfil`, marcando o grupo Atendimento, e `/usuario`, aba
Acesso, com o perfil e **sem** empresa.

## Classes (como aparecem no `./testes.sh`)

- `Atendimento/PerfilAtendimentoTests E2E`: **login como perfil de atendimento**, com os usuários criados pela fixture.
  Cobre entrada direta, Módulos, senha errada e usuário com empresa trocando de empresa.
- `Atendimento/CriacaoPerfilAtendimentoTests E2E`: o **caminho completo pelas telas**. O master cria o perfil em
  `/perfil`: marca o grupo "Atendimento", que fica na 2ª página da tabela Base, e as filhas 811001–811015 vêm junto.
  Depois cria o usuário em `/usuario`, na aba Acesso, com esse perfil. O teste confere na API o que foi gravado, sai,
  entra com o novo usuário (direto no atendimento) e abre Módulos. Roda em dois casos:
  - **sem empresa:** a sessão fica "Atendimento";
  - **com empresa** (a de `E2E_EMPRESA`): o usuário também entra direto no atendimento, mas a sessão mostra a empresa,
    porque o token leva o `EmpresaId` do vínculo. Pelo botão da fábrica ele sai do atendimento e passa para a empresa.

  Perfil e usuário são apagados no TearDown.
- `Atendimento/ModoAtendimentoTests E2E`: master entrando no atendimento pelo menu do avatar ou pelo rail do login.
- `Atendimento/ContratoLoginAtendimentoTests`: as mesmas regras direto na API, sem navegador. Por isso não tem o
  final "E2E": o `testes.sh` só marca os testes que abrem o navegador.

Não é preciso configurar login para o perfil de atendimento: a fixture cria os usuários a cada execução, usando só o
master de `E2E_LOGIN`/`E2E_SENHA`/`E2E_EMPRESA`.

## Rodar

- Pré-requisitos: `PostGIS` e `MongoDB` rodando, CCO.Api local e front em modo dev apontando para ela.
- A tela `/equipamento` é respondida pelo `ApiEquipamentoStub`, porque as chamadas legadas sairiam para o
  concentrador.
- Os testes nunca mandam login inexistente ao `v1/Autenticador/login`: para esse caso, a API consulta o WS
  externo do permissionário.

```bash
set -a; . ./.env.e2e; set +a
dotnet test --filter "FullyQualifiedName~.Atendimento."
```
