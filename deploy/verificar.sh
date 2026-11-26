#!/usr/bin/env bash
# Verificação do que subiu (C13): bash deploy/verificar.sh <url-base> <servico-do-banco>
# Para no primeiro item que falha. Respeita CURL_CA_BUNDLE (CA local) e RASTRU_SEGREDOS/RASTRU_IMAGEM do Compose.
set -u

if [ $# -ne 2 ]; then
  echo "Uso: verificar.sh <url-base> <servico-do-banco>" >&2
  exit 2
fi
base="${1%/}"
servico="$2"
aqui="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

falhar() {
  echo "FALHOU: $1" >&2
  exit 1
}

status() { curl -s -o /dev/null -w '%{http_code}' --max-time 20 "$@" 2>/dev/null; }

echo "Verificando $base ..."

codigo=000
for tentativa in $(seq 1 20); do
  codigo="$(status "$base/api/saude")"
  [ "$codigo" = "200" ] && break
  echo "  /api/saude respondeu $codigo (tentativa $tentativa de 20); aguardando 5 s"
  sleep 5
done
[ "$codigo" = "200" ] || falhar "GET /api/saude não chegou a 200 (último: $codigo)."
echo "  ok: GET /api/saude 200"

for caminho in / /pedidos; do
  corpo="$(curl -s --max-time 20 -w '\n%{http_code}' "$base$caminho" 2>/dev/null)"
  codigo="${corpo##*$'\n'}"
  [ "$codigo" = "200" ] || falhar "GET $caminho respondeu $codigo, esperado 200."
  printf '%s' "$corpo" | grep -q '<div id="root">' || falhar "GET $caminho não trouxe o <div id=\"root\"> do SPA."
  echo "  ok: GET $caminho 200 com o SPA"
done

codigo="$(status "$base/api/setores")"
[ "$codigo" = "401" ] || falhar "GET /api/setores respondeu $codigo, esperado 401 (a API atrás do proxy)."
echo "  ok: GET /api/setores 401"

codigo="$(status "$base/API/setores")"
[ "$codigo" = "404" ] || falhar "GET /API/setores respondeu $codigo, esperado 404."
echo "  ok: GET /API/setores 404"

if ! docker compose -f "$aqui/compose.yaml" --profile ferramentas run --rm -T "$servico" conferir-senhas >/dev/null 2>&1; then
  falhar "conferir-senhas não saiu 0: algum usuário ainda tem a senha pública do repositório (ou o comando falhou)."
fi
echo "  ok: nenhum dos sete usuários tem a senha do repositório"

echo "Verificação concluída: tudo verde."
