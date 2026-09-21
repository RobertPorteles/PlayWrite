#!/usr/bin/env bash
# Janela (zenity) para editar as variáveis de ambiente dos testes E2E. Salva em .env.e2e (fora do git),
# que o testes.sh carrega. Valor vazio = volta ao padrão do E2ESettings.
cd "$(dirname "$0")"
arquivo=.env.e2e
vars=(E2E_LOGIN E2E_SENHA E2E_EMPRESA E2E_VEICULO E2E_MOTORISTA E2E_LOGIN_NAO_MASTER E2E_SENHA_NAO_MASTER
      E2E_FRONT_URL E2E_API_URL E2E_BACKEND_DIR E2E_FRONTEND_DIR E2E_INTEGRACOES_DIR HEADED SLOWMO_MS)

while :; do
  unset "${vars[@]}"
  [ -f "$arquivo" ] && . "./$arquivo"

  linhas=()
  for v in "${vars[@]}"; do
    valor=${!v}
    [[ $v == *SENHA* && -n $valor ]] && valor='••••••'
    linhas+=("$v" "$valor")
  done

  escolhida=$(zenity --list --title="Variáveis E2E" --width=650 --height=520 \
    --text="Duplo clique para editar ($arquivo)" --column="Variável" --column="Valor" \
    --ok-label="Editar" --cancel-label="Fechar" "${linhas[@]}") || exit 0
  [ -z "$escolhida" ] && continue

  oculto=(); [[ $escolhida == *SENHA* ]] && oculto=(--hide-text)
  novo=$(zenity --entry --title="$escolhida" --text="Valor de $escolhida (vazio = padrão)" \
    --entry-text="${!escolhida}" "${oculto[@]}") || continue

  declare "$escolhida=$novo"
  : > "$arquivo"; chmod 600 "$arquivo"
  for v in "${vars[@]}"; do [ -n "${!v}" ] && printf '%s=%q\n' "$v" "${!v}" >> "$arquivo"; done
done
