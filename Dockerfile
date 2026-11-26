#
# Imagem unica do Rastru: a API, o build do SPA em /app/wwwroot e os scripts do banco em /app/sql
# (C7, C8 e secao 5.3 da spec docs/superpowers/specs/2026-11-25-ci-cd-design.md). Os scripts que vao
# na imagem sao os da versao que esta subindo, e o deploy usa os da propria imagem.
#
# CA extra opcional, so para construir em ambiente cujo proxy de saida re-assina o TLS (a sessao na nuvem
# do Claude Code): passe `--secret id=ca_extra,src=/caminho/ca-bundle.crt`. Ela vale so no RUN que baixa
# pacotes, nos dois estagios de build, e o estagio final nunca a ve. O CI nao a passa.

# ---- front: build do Vite ------------------------------------------------------------------------
FROM node:22-bookworm-slim AS front
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=secret,id=ca_extra,required=false \
    if [ -f /run/secrets/ca_extra ]; then export NODE_EXTRA_CA_CERTS=/run/secrets/ca_extra; fi; \
    npm ci
COPY web/ ./
RUN npm run build

# ---- api: restore e publish ----------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /repo
COPY src/Rastreamento.Api/Rastreamento.Api.csproj src/Rastreamento.Api/
COPY src/Rastreamento.Application/Rastreamento.Application.csproj src/Rastreamento.Application/
COPY src/Rastreamento.Domain/Rastreamento.Domain.csproj src/Rastreamento.Domain/
COPY src/Rastreamento.Infrastructure/Rastreamento.Infrastructure.csproj src/Rastreamento.Infrastructure/
RUN --mount=type=secret,id=ca_extra,required=false \
    if [ -f /run/secrets/ca_extra ]; then export SSL_CERT_FILE=/run/secrets/ca_extra; fi; \
    dotnet restore src/Rastreamento.Api/Rastreamento.Api.csproj
COPY src/ src/
RUN dotnet publish src/Rastreamento.Api/Rastreamento.Api.csproj -c Release -o /app --no-restore

# ---- final: runtime com ICU, usuario nao-root ----------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=api /app ./
COPY --from=front /web/dist ./wwwroot
COPY specs/02-modelo-de-dados.sql sql/specs/02-modelo-de-dados.sql
COPY db/seed.sql db/seed-demo.sql db/massa-vitrine.sql sql/db/
COPY db/migracoes/ sql/db/migracoes/
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Rastreamento.Api.dll"]
