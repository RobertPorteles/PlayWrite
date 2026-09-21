#!/usr/bin/env bash
# Janela (zenity) para escolher quais classes de teste E2E rodar. Nada marcado = todas.
cd "$(dirname "$0")"
set -a; [ -f .env.e2e ] && . ./.env.e2e; set +a
# Navegador visível e mais lento por padrão; HEADED=0 ./testes.sh roda sem janela.
export HEADED="${HEADED:-1}" SLOWMO_MS="${SLOWMO_MS:-500}"

linhas=()
for f in Features/*/Tests/*.cs; do
  feature=${f#Features/}; feature=${feature%%/*}
  # Abre o navegador (usa a propriedade Page do Playwright) = E2E; os de contrato só chamam a API.
  sufixo=; grep -qw Page "$f" && sufixo=" E2E"
  linhas+=(FALSE "$feature/$(basename "$f" .cs)$sufixo")
done

escolhidos=$(zenity --list --checklist --title="Testes E2E" --width=500 --height=600 \
  --text="Marque os testes (nenhum = todos)" --column="" --column="Teste" \
  --separator=$'\n' "${linhas[@]}") || exit 0

[ -z "$escolhidos" ] && exec dotnet test "$@"

filtro=$(while read -r t; do t=${t% E2E}; echo -n "FullyQualifiedName~.${t%/*}.Tests.${t#*/}|"; done <<< "$escolhidos")
exec dotnet test --filter "${filtro%|}" "$@"
