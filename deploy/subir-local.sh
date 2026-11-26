#!/usr/bin/env bash
# Sobe o Rastru do zero na máquina local, com segredos aleatórios, e roda a verificação.
#   bash deploy/subir-local.sh              sobe e verifica
#   bash deploy/subir-local.sh --derrubar   derruba tudo, apaga os volumes e deploy/local/
#   bash deploy/subir-local.sh --logs       mostra os logs de todos os serviços (inclusive o banco-hml)
set -euo pipefail

aqui="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
raiz="$(cd "$aqui/.." && pwd)"
local_dir="$aqui/local"
compose=(docker compose -f "$aqui/compose.yaml")

export RASTRU_IMAGEM="${RASTRU_IMAGEM:-rastru:local}"
export RASTRU_DOMINIO_HML="hml.localhost"
export RASTRU_SEGREDOS="$local_dir"
export NO_PROXY="hml.localhost,localhost,127.0.0.1${NO_PROXY:+,$NO_PROXY}"
export no_proxy="$NO_PROXY"

if [ "${1:-}" = "--derrubar" ]; then
  mkdir -p "$local_dir"
  # Os env_file precisam existir para o Compose ler a configuração.
  for f in sqlserver hml banco-hml; do [ -f "$local_dir/$f.env" ] || : > "$local_dir/$f.env"; done
  "${compose[@]}" --profile ferramentas down -v --remove-orphans
  rm -rf "$local_dir"
  echo "Tudo derrubado: contêineres, volumes e deploy/local/ removidos."
  exit 0
fi

if [ "${1:-}" = "--logs" ]; then
  mkdir -p "$local_dir"
  for f in sqlserver hml banco-hml; do [ -f "$local_dir/$f.env" ] || : > "$local_dir/$f.env"; done
  "${compose[@]}" --profile ferramentas logs --no-color
  exit 0
fi

# Senha que cumpre a política do SQL Server (maiúscula, minúscula, número) e não tem caractere que
# atrapalhe connection string ou env_file.
senha() { echo "Ra1-$(openssl rand -hex 16)"; }

mkdir -p "$local_dir"
if [ ! -f "$local_dir/sqlserver.env" ] || [ ! -f "$local_dir/hml.env" ] || [ ! -f "$local_dir/banco-hml.env" ]; then
  sa="$(senha)"
  login="$(senha)"
  conexao="Server=sqlserver,1433;Database=Rastreamento_HML;User Id=rastru_hml;Password=$login;TrustServerCertificate=True;Encrypt=True"
  admin="Server=sqlserver,1433;Database=master;User Id=sa;Password=$sa;TrustServerCertificate=True;Encrypt=True"
  umask 077
  printf 'MSSQL_SA_PASSWORD=%s\n' "$sa" > "$local_dir/sqlserver.env"
  printf 'ConnectionStrings__Rastreamento=%s\nJwt__SigningKey=%s\n' "$conexao" "$(openssl rand -base64 48 | tr -d '\n')" > "$local_dir/hml.env"
  {
    printf 'Banco__ConexaoAdministrativa=%s\n' "$admin"
    printf 'Banco__Nome=Rastreamento_HML\nBanco__Login=rastru_hml\nBanco__SenhaDoLogin=%s\nBanco__Vitrine=true\n' "$login"
    for u in admin pcp operador movimentador separador qualidade gestao; do
      printf 'Senhas__%s=%s\n' "$u" "$(senha)"
    done
  } > "$local_dir/banco-hml.env"
  echo "Segredos gerados em deploy/local/ (fora do repositório)."
else
  echo "Segredos de deploy/local/ já existem: mantidos."
fi

if ! docker image inspect "$RASTRU_IMAGEM" >/dev/null 2>&1; then
  echo "Imagem $RASTRU_IMAGEM não existe: construindo."
  docker build -t "$RASTRU_IMAGEM" "$raiz"
fi

echo "Subindo o SQL Server..."
"${compose[@]}" up -d --wait sqlserver
echo "Preparando o banco da HML..."
"${compose[@]}" --profile ferramentas run --rm -T banco-hml
echo "Subindo a API e o Caddy..."
"${compose[@]}" up -d api-hml caddy

echo "Copiando a CA local do Caddy..."
raiz_ca=""
for tentativa in $(seq 1 30); do
  if "${compose[@]}" cp caddy:/data/caddy/pki/authorities/local/root.crt "$local_dir/caddy-raiz.crt" >/dev/null 2>&1; then
    raiz_ca="$local_dir/caddy-raiz.crt"
    break
  fi
  sleep 2
done
[ -n "$raiz_ca" ] || { echo "FALHOU: a CA local do Caddy não apareceu em 60 s." >&2; exit 1; }
export CURL_CA_BUNDLE="$raiz_ca"

bash "$aqui/verificar.sh" "https://$RASTRU_DOMINIO_HML" banco-hml
echo "Pronto: https://$RASTRU_DOMINIO_HML (usuários e senhas em deploy/local/banco-hml.env)."
