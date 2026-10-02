# Ambiente de homologação, CI/CD e manutenção do banco — desenho

Data: 2026-10-02. Branch: `claude/sharp-darwin-yrr1o5`, a partir da `main` em `75df075` (merge do
rufino-bot/rastru#27). Desenho aprovado pelo usuário em chat na mesma data, seção a seção (as sete
seções da conversa viraram as seções 3 a 9 abaixo). Texto da spec aprovado pelo usuário na mesma data; o plano de
implementação **ainda não foi escrito**, por decisão dele (fica para quando voltar a esta branch).

**Quando executa.** Fora da ordem das fases, por decisão do usuário — mas **não agora**: depois de
terminadas as fases 3X que ainda faltam. Hoje isso é a **3B** (a 3D está concluída, e a 3C já está
decidida para depois da Fase 5). A ordem fica: filtros da demanda → Fase 1F → Fase 3B → **este
desenho** → Fase 4. Os ajustes em `specs/03-arquitetura-tecnica.md`, `specs/06-roadmap-mvp.md` e
`CLAUDE.md` que oficializam a exceção são feitos **depois** desta spec aprovada, como o usuário pediu
(seção 10).

## 1. O que se quer, e o que conta como pronto

**Pedido do usuário:** deixar o Rastru rodando num domínio próprio como um **HML** (homologação), para
ser validado pelo uso; um **CI/CD** que leve cada mudança aprovada ao HML sem deploy manual; e
**rotinas de manutenção do banco** (verificação e reconstrução de índices, e o que vem antes delas).

**Quem usa e com que dado** (opção C da conversa): o HML nasce com a massa de `db/seed-demo.sql` para
as pessoas da fábrica explorarem e lançarem um pedido de teste de vez em quando. Não é uso real ainda.
O banco pode ser recriado até o dia em que o usuário decidir **promover o ambiente a PRD** (seção 6.5);
dali em diante o dado importa.

**Pronto é:**

- um merge na `main` aparece sozinho no HML, com HTTPS no domínio;
- o banco tem backup **fora da VPS**, e a restauração desse backup é provada automaticamente todo mês;
- os quatro pontos de pré-deploy da seção "Pontos em aberto" de `specs/03-arquitetura-tecnica.md`
  (TLS, `ForwardedHeaders`, `SigningKey`, limite de corpo do proxy) estão fechados — este HML **é** o
  "primeiro deploy público" que os dispara;
- os critérios de aceite da seção 9.3 passaram na VPS real.

## 2. Decisões tomadas na conversa

| # | Decisão | Alternativas descartadas |
|---|---|---|
| D1 | Opção C: HML com `seed-demo`, recriável até a promoção a PRD | só demonstração; piloto real desde o início |
| D2 | Deploy **a cada merge na `main`**, automático. O controle de "quando chega" é o usuário segurar o PR; hotfix na `main` cai direto para quem testa | deploy por tag; só botão manual |
| D3 | Spec **independente de fornecedor**, com requisitos mínimos (seção 3.4). O usuário ainda avalia HostGator, Hetzner e Contabo | fixar um fornecedor agora |
| D4 | **Compose versionado + GitHub Actions + Caddy** (abordagem A) | Coolify gerenciando a stack — registrado como saída futura (seção 3.5) |
| D5 | Backup fora da VPS no **Cloudflare R2**, via `rclone` | Backblaze B2; Google Drive pessoal |
| D6 | Contas de teste **compartilhadas por perfil** no HML; **uma por pessoa** na promoção a PRD | só compartilhadas; só individuais |
| D7 | Modos do ambiente: `RASTRU_AMBIENTE=HML` ou `PRD` (nome do usuário, no lugar do "demo/real" proposto) | `HML_MODO=demo/real` |
| D8 | Em **PRD**, deploy exige **pré-veredito de um revisor automático** (check exigível) **e depois** a aprovação do usuário no environment `prd`. Configuração com gatilho "primeiro deploy de HML" (seção 6.6) | aprovação humana de outra pessoa; só a review do fluxo `subagent-driven-development` |
| D9 | Proteção da `main` por **dois rulesets** versionados (seção 5.4) | só configuração manual sem registro |
| D10 | Manutenção pela **Maintenance Solution do Ola Hallengren**, em banco próprio, agendada por `cron` | scripts próprios; SQL Agent (não existe no Express) |

## 3. Topologia

### 3.1 Uma VPS, um Compose, três serviços

```
internet ──443/80──▶ caddy ──/api/*──▶ api:8080 ──1433──▶ sqlserver
                       │
                       └── demais caminhos ──▶ build do Vite (try_files → index.html)
```

Arquivo `deploy/hml/docker-compose.hml.yml`. Só o `caddy` publica portas no host (80 e 443).

- **`caddy`** — emite e renova o certificado do domínio sozinho (Let's Encrypt); a porta 80 só
  redireciona para HTTPS. Serve o `dist/` do front e repassa `/api/*` para a API **sem tirar o
  prefixo** — a guarda do prefixo em `Program.cs` exige o `/api` no `Path`. Qualquer outro caminho cai
  no `index.html`, para o F5 numa rota do SPA funcionar. SPA e API ficam na **mesma origem**, e é isso
  que deixa o cookie de refresh (`SameSite=Strict`, `Path` em `/api/auth`) funcionar sem CORS.
- **`api`** — a imagem do ASP.NET publicada (`net10.0`). Configuração toda por variável de ambiente,
  nada de segredo em `appsettings` (seção 7.4).
- **`sqlserver`** — `mcr.microsoft.com/mssql/server:2022-latest` com `MSSQL_PID=Express` (a edição
  Express é gratuita e pode ser usada em produção; a Developer, usada em dev, é licenciada só para
  desenvolvimento e teste). Volume nomeado para os dados e outro para os backups.
  `MSSQL_MEMORY_LIMIT_MB=2048`, para sobrar memória à API e ao sistema numa VPS de 4 GB.

**A porta 1433 e a 8080 nunca são publicadas no host.** Ver o invariante da seção 9.1: porta publicada
pelo Docker passa por fora do `ufw`. Acesso ao banco de fora (SSMS, por exemplo) é por túnel SSH.

A rede do Compose tem **sub-rede fixa** (`172.30.0.0/24`, valor ilustrativo; o plano fixa o valor), da
qual depende a configuração de `ForwardedHeaders` (seção 4.1).

### 3.2 De onde vem cada peça

- **Duas imagens por commit**, publicadas no **GHCR** do próprio repositório com a tag do SHA:
  `rastru-api` e `rastru-web`. O build do front entra **dentro da imagem do Caddy** (`rastru-web`): as
  duas imagens saem do mesmo commit, e não há como subir o front de uma versão com a API de outra.
- `docker-compose.hml.yml`, `Caddyfile`, os scripts de deploy e manutenção e o runbook vivem em
  `deploy/hml/`, versionados e revisados como código.

### 3.3 Domínio

Um registro A apontando para o IP da VPS. A spec não fixa nome nem registrador. Na conversa, o
`.com.br` do Registro.br (R$ 40/ano, mesma tarifa na renovação) e a Cloudflare Registrar (preço de
custo) foram as recomendações; TLDs de primeiro ano a US$ 1–2 renovam muito acima disso. Se o DNS
ficar na Cloudflare, o registro fica em **"DNS only"** (sem o proxy laranja): é o que deixa o Caddy
emitir o certificado e ver o IP real do cliente.

### 3.4 Requisitos mínimos da VPS

x86-64 (a imagem do SQL Server **não** roda em ARM — isso descarta as máquinas ARM da Oracle e da
Hetzner), **4 GB de RAM** (o SQL Server exige 2 GB só para subir), 2 vCPU, 40 GB de disco, Ubuntu
24.04, acesso root.

### 3.5 Fora desta topologia, de propósito

- **Coolify.** Avaliado na conversa: consome ~1 GB de RAM sozinho (mínimo do Coolify passa a VPS para
  8 GB), o backup automático dele **não** suporta SQL Server (as rotinas da seção 8 continuariam
  manuais), e ele não substitui o CI — só o passo de deploy. Fica como **saída futura**: o
  `docker-compose.hml.yml` desta spec é o mesmo arquivo que o Coolify consumiria, então migrar depois
  troca quem executa o deploy, não a stack.
- **Vercel** só hospedaria o front; a API e o banco precisariam de outro lugar de qualquer jeito, e
  front em outra origem traz CORS e o cookie de refresh entre subdomínios.
- Staging por PR, Kubernetes, um segundo ambiente (seção 6.5).

## 4. Mudanças na aplicação

Cada uma fecha um ponto da seção "Pontos em aberto" de `specs/03-arquitetura-tecnica.md`.

### 4.1 `ForwardedHeaders` — código novo (ponto 2)

O Caddy roda em outro container: para a API ele **não é loopback**, e os padrões do ASP.NET (que só
confiam em loopback) ignorariam os cabeçalhos — o rate limit do `/auth/login` continuaria
particionado pelo IP do Caddy, ou seja, global.

- `UseForwardedHeaders` entra **primeiro no pipeline**, antes da guarda do prefixo `/api` e do rate
  limiter, com `XForwardedFor | XForwardedProto` e `ForwardLimit = 1`.
- `KnownNetworks` recebe **só a sub-rede fixa do Compose**, lida da configuração
  (`ForwardedHeaders__KnownNetworks`). Sem a chave — dev e testes — a lista fica como o padrão e nada
  muda.
- **Proibido**, e a spec registra por quê (citando a seção "Rate limit atrás de proxy reverso" do
  `CLAUDE.md`): limpar `KnownProxies`/`KnownNetworks` ou usar `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. É
  o que tornaria os cabeçalhos forjáveis por qualquer cliente.
- O que fecha o resto: a porta da API não é publicada, então só o Caddy a alcança; e o Caddy não
  repassa `X-Forwarded-For` vindo do cliente sem `trusted_proxies` configurado — **documentação do
  Caddy, não medição**; o critério de aceite 4 da seção 9.3 mede o efeito.
- **Testes:** um `X-Forwarded-For` vindo da rede conhecida muda o IP que o rate limit vê; vindo de
  fora dela, é ignorado.

### 4.2 TLS — configuração no Caddy (ponto 1)

O Caddy termina o TLS e redireciona a porta 80; a API fala HTTP dentro da rede, e o
`X-Forwarded-Proto` faz `Request.IsHttps` ser verdadeiro. **Não** entra `UseHttpsRedirection` na API
(o redirecionamento já é do Caddy; duplicar arrisca loop). O HSTS é cabeçalho do Caddy. É o que faz o
refresh funcionar: o cookie é `Secure`, e fora de `localhost` navegador não o grava sem TLS.

### 4.3 `SigningKey` — procedimento (ponto 3)

`Jwt__SigningKey` vem do `.env` da VPS (seção 7.4). O `JwtOptionsValidator` que já existe derruba a
API no startup se ela faltar ou for o placeholder. Mesmo caminho para a connection string
(`ConnectionStrings__Rastreamento`).

### 4.4 Limite de corpo do proxy — hipótese a medir (ponto 4)

A crença é que o Caddy **não** limita o corpo da requisição por padrão (o nginx limita a 1 MiB). **Não
foi verificado.** O critério de aceite 5 envia um STL de ~5 MB pelo HML; se falhar, o `Caddyfile`
ganha `request_body { max_size … }` acima do limite do endpoint de upload (16 MiB mais a margem
multipart).

### 4.5 Endpoint de saúde — código novo

`GET /api/saude`, anônimo. Responde **200** quando a API alcança o banco e **503** quando não. O corpo
não diz mais nada — nem versão, nem detalhe de erro. Serve ao healthcheck do Docker e ao passo do
deploy que espera a API ficar saudável (seção 6.3). Entra em `specs/05-api-endpoints.md`, com teste.
Limite conhecido: ele prova conexão, **não** schema — é por isso que existe a guarda da seção 5.3.

### 4.6 Empacotamento

- `src/Rastreamento.Api/Dockerfile` em dois estágios (SDK para compilar, `aspnet:10.0` para rodar),
  executando como usuário sem privilégio. A imagem leva também a ferramenta de contas (seção 7.2).
- `deploy/hml/Dockerfile.web` em dois estágios: Node compila o front, `caddy:2` recebe o `dist/` e o
  `Caddyfile`. O front **não precisa de variável de build**: o `rota()` de `web/src/api/client.ts` já
  usa o caminho relativo `/api`.

### 4.7 O que fica igual

A guarda do prefixo `/api`; o cookie `Secure` + `SameSite=Strict`; os valores de rate limit e lockout.
`ASPNETCORE_ENVIRONMENT=Production` em HML **e** em PRD: a aplicação se comporta igual nos dois, o que
muda é a política do banco (seção 6). Assim o HML usa os valores do `appsettings.json` base, não os
folgados do `Development`, e não nasce um `appsettings.Hml.json` que possa divergir em silêncio.

## 5. CI e proteção da `main`

### 5.1 `ci.yml` — em todo PR e em todo push na `main`

Três jobs em paralelo:

- **`backend`** — SQL Server 2022 como *service container*, na 1433 e com a senha de dev (os testes
  de Infrastructure leem `localhost,1433` fixo em `TesteComBanco`, e os de Api sobem a API com o
  `appsettings.json` dela, que aponta para o mesmo endereço — nenhum muda); aplica
  `specs/02-modelo-de-dados.sql` + `db/seed.sql` pelo `sqlcmd`; `dotnet build Rastreamento.slnx
  -warnaserror` e `dotnet test Rastreamento.slnx -m:1` — os comandos do `CLAUDE.md`.
- **`frontend`** — `npm ci`, `npm test`, `npm run build` (o build está aí porque o Vitest não faz
  typecheck). O `npm run lint` (oxlint) entra **só se estiver verde hoje**: o plano mede antes; se não
  estiver, fica fora e registrado, para o CI não nascer vermelho por dívida antiga.
- **`schema`** — a guarda da seção 5.3.

### 5.2 `deploy.yml` — depois do `ci.yml` verde num push na `main`, e por botão manual

1. Gera `rastru-api` e `rastru-web` e publica no GHCR com a tag do SHA.
2. Copia por `scp` o `docker-compose.hml.yml`, os scripts de `deploy/hml/` e o diretório `db/` para a
   VPS.
3. Roda `deploy.sh <sha>` por SSH (seção 6.3).
4. **Pull sem token guardado na VPS:** o próprio job faz `docker login ghcr.io` na VPS com o
   `GITHUB_TOKEN` temporário do workflow, faz o pull e o logout. O token expira com o job; funciona com
   imagem pública ou privada.
5. `concurrency: deploy` sem cancelar o que está rodando: dois merges seguidos entram na fila, e um
   deploy nunca é interrompido no meio.

O **botão manual** recebe um SHA opcional — é também o rollback: reimplantar as imagens de um commit
anterior, que continuam no GHCR. O schema não volta (seção 6.4).

**Acesso SSH:** usuário `deploy` na VPS, no grupo `docker`, só por chave, sem login de root. Segredos
no **environment `hml`** do GitHub: `HML_SSH_HOST`, `HML_SSH_KEY` (chave gerada só para isso) e
`HML_SSH_KNOWN_HOSTS` (impressão digital do servidor fixada — **nunca** `StrictHostKeyChecking=no`). Os
segredos da aplicação **não passam pelo GitHub** (seção 7.4): comprometer o GitHub dá poder de deploy,
não os segredos de dados.

**Custo:** o repositório é público (conferido em 2026-10-02), então os minutos de Actions são
ilimitados.

### 5.3 Guarda de schema — job `schema`

O risco central do desenho: alguém muda `02-modelo-de-dados.sql` e esquece o `alter-*.sql`. O CI de
testes passa (cria o banco do zero a partir do `02`), o deploy aplica alters que não trazem a mudança,
e a API quebra no HML — sem o endpoint de saúde perceber.

- **Banco A:** o `02` da **base** do PR + todos os `db/alter-*.sql` do **head**, em ordem.
- **Banco B:** o `02` do **head**.
- Compara, pelos catálogos do SQL Server, tabelas, colunas (tipo, tamanho, nulidade, default, se é
  calculada e a expressão), constraints (PK, FK, `UNIQUE`, `CHECK` e suas definições) e índices
  (colunas, unicidade, filtro). **Falha se A ≠ B**, listando a diferença.
- Prova de graça que todos os alters rodam sem erro sobre o schema atual.
- Num push na `main`, a "base" é o commit anterior.
- **Limite conhecido:** compara schema, não dado. Um alter que preenche dado errado passa.

### 5.4 Proteção da `main` — dois rulesets

Rulesets são gratuitos e aplicados em repositório público (em privado, no plano Free, não são). Ficam
versionados em JSON em `deploy/github/` e são **aplicados pelo usuário** em *Settings → Rules →
Rulesets* (as ferramentas de GitHub desta sessão não alcançam a API de rulesets). O GitHub oferece
importar ruleset por JSON — **não testado aqui**; o plano confirma ou descreve o passo manual.

- **`main-integridade`** — bloqueia **exclusão** e **force-push** da `main`. Exceção: o papel
  *Repository admin*, ou seja, o usuário (em repositório de conta pessoal a lista de exceções não
  aceita usuário individual, só o papel). Um agente — inclusive o GitHub App do Claude —, um
  colaborador futuro ou um token vazado não reescrevem nem apagam a `main`. Pode ser ativado já.
- **`main-ci`** — **exige PR** (zero aprovações: o usuário trabalha só, e o GitHub não deixa aprovar o
  próprio PR) e exige os jobs `backend`, `frontend` e `schema` verdes. Exceção: *Repository admin*,
  **só no modo "apenas via pull request"** — hotfix de emergência mergeia com CI vermelho, push direto
  continua barrado. **Só pode ser ativado depois** de o `ci.yml` existir na `main` e ter rodado uma
  vez; antes disso os checks exigidos não existem e todo PR travaria.

Limite registrado: o ledger (`rufino-bot/rastru-tcc`) é privado, e lá rulesets não seriam aplicados
no plano Free.

## 6. Banco no deploy

### 6.1 Dois modos: `RASTRU_AMBIENTE=HML` ou `PRD`

No `.env` da VPS.

| | HML | PRD |
|---|---|---|
| Recriar o banco | permitido, só pelo workflow manual (seção 6.2) | **recusado** |
| Massa inicial | `seed.sql` + `seed-demo.sql` | `seed.sql`, sem demo |
| Contas | compartilhadas por perfil (seção 7.3) | uma por pessoa |
| Recovery model / backup | `SIMPLE`, completo diário | `FULL`, completo diário + log a cada hora (seção 8.3) |
| Deploy | automático no merge | revisor automático + aprovação do usuário (seção 6.6) |

### 6.2 Recriar: só manual, nunca a cada deploy

Recriar a cada deploy apagaria os pedidos de teste. Workflow manual "Recriar banco" →
`recriar-banco.sh` na VPS, que **se recusa com `RASTRU_AMBIENTE=PRD`**:

1. para a API;
2. `DROP DATABASE`;
3. aplica `02-modelo-de-dados.sql`, `seed.sql` e, em HML, `seed-demo.sql` — com `-b -f 65001` (a
   armadilha de codepage registrada no `CLAUDE.md`: acentuação corrompida entra e o banco fica verde);
4. garante o login da aplicação (seção 7.1) e troca as senhas do seed pelas do `.env` (seção 7.3);
5. sobe a API.

A API fica parada entre o seed e a troca de senha: **não há janela** em que `admin`/`Admin@123` — cujo
hash está commitado num repositório público — funcione no ar.

### 6.3 `deploy.sh <sha>`

1. **Backup completo `COPY_ONLY`**, local e enviado ao R2 — o ponto de volta deste deploy.
2. `docker compose pull` das imagens do SHA.
3. **Aplica todos os `db/alter-*.sql`**, em ordem alfabética, com `-b -f 65001`. Eles já são
   idempotentes; rodar todos a cada deploy dispensa tabela de versão.
4. Garante o login da aplicação (seção 7.1).
5. `docker compose up -d` com o SHA novo.
6. Espera `/api/saude` responder 200 por **até 90 s**. Se não responder: volta às imagens do SHA
   anterior (guardado num arquivo na VPS), e o script sai com erro — o workflow fica **vermelho**.
7. Registra o SHA atual.

**Convenção que a guarda pressupõe:** todo `alter-*.sql` novo é idempotente e mantém a ordem
alfabética igual à ordem de aplicação (o padrão atual, `alter-fase-<id>.sql`, já faz isso).

### 6.4 Rollback e schema

O rollback automático volta **só a aplicação**. Funciona porque os alters **acrescentam**: uma versão
anterior da API ignora uma coluna nova. Um alter **destrutivo** (`DROP COLUMN`, por exemplo) quebra
esse rollback; a volta, nesse caso, é restaurar o backup do passo 1 da seção 6.3 — manual, no runbook.
Limite conhecido, registrado.

### 6.5 Promoção a PRD

Ato do usuário, uma vez:

1. última recriação **sem** `seed-demo.sql`;
2. `RASTRU_AMBIENTE=PRD` no `.env`;
3. contas individuais pela ferramenta (seção 7.2); as `.hml` são **desativadas**, não apagadas, para o
   histórico continuar apontando para elas.

Consequência registrada: **depois da promoção não existe mais HML.** Um lugar para validar antes de
chegar ao pessoal vira outro ambiente (um segundo projeto Compose na mesma VPS, com outro subdomínio e
outro banco) — fora desta spec.

### 6.6 Trava de deploy em PRD

Decisão D8: em PRD o deploy exige **um check de revisor automático verde no PR** e, depois, a
**aprovação do usuário** no environment `prd` do GitHub (*required reviewer*: o job espera o clique).
Hoje o repositório não tem revisor automático (não existe `.github/`). **O desenho do revisor fica
para o primeiro deploy de HML**, por decisão do usuário. Como PRD só existe depois da promoção, essa
pendência nunca bloqueia o HML; ela é **pré-condição da promoção**.

## 7. Contas e segredos

### 7.1 A API deixa de usar `sa`

- **`sa`** — senha forte do `.env`, usado **só** pelos scripts de deploy, recriação e manutenção, de
  dentro da VPS.
- **`rastru_app`** — login da API, com só `db_datareader` + `db_datawriter`. Conferido por leitura de
  código em 2026-10-02: a API só faz DML; os únicos `FromSql` (em `ExecucaoRepository`) são `SELECT`
  com `UPDLOCK, HOLDLOCK`, e não há `EnsureCreated`, `Migrate` nem DDL. Garantido por SQL idempotente
  a cada deploy e a cada recriação.

### 7.2 Ferramenta de contas — `src/Rastreamento.Ferramentas`

O SQL não calcula BCrypt, e o hash commitado no seed é o problema. Um projeto de console pequeno,
usando o `BCryptPasswordHasher` existente (fator 11):

- `criar-usuario <nomeUsuario> <Perfil> "<Nome completo>"`;
- `definir-senha <nomeUsuario>` — também zera o lockout e **revoga os refresh tokens ativos** do
  usuário (troca de senha derruba as sessões antigas);
- `destrancar <nomeUsuario>` — para a conta compartilhada trancada por um colega que errou a senha,
  sem esperar os 15 min;
- `desativar <nomeUsuario>`.

A senha entra pela **entrada padrão, nunca por argumento** (argumento aparece no `ps` e no histórico).
Casos de uso na camada `Application`, no padrão do projeto, com testes. Vai dentro da imagem da API e
roda com `docker compose run --rm api dotnet Rastreamento.Ferramentas.dll …`. É também quem cria as
contas reais na promoção, e substitui o "cada conta de Movimentador nasce por SQL na VPS" que
`specs/06-roadmap-mvp.md` registra hoje.

### 7.3 Contas do HML

`deploy/hml/contas-hml.sh`, que chama a ferramenta: `admin` e `pcp` (do seed, senhas
**sobrescritas**) e as compartilhadas `operador.hml`, `almoxarifado.hml`, `movimentador.hml`,
`qualidade.hml` e `gestao.hml`, cada uma com senha própria do `.env`.

O lockout (5 falhas, 15 min) vale no HML: com conta compartilhada, um colega que erra a senha tranca o
perfil para todos. O `destrancar` e um aviso a quem for testar resolvem; não muda o desenho.

Quando a spec do vínculo Operador ↔ Setor existir (seção 10), `operador.hml` vira uma conta por Setor.

### 7.4 Segredos

Só no `.env` da VPS (`chmod 600`, dono `deploy`), com um **`deploy/hml/.env.exemplo` versionado** que
tem só os nomes:

- `RASTRU_AMBIENTE`, `RASTRU_DOMINIO`;
- `MSSQL_SA_PASSWORD`, `RASTRU_APP_DB_PASSWORD`;
- `JWT_SIGNING_KEY` — `openssl rand -base64 48`, bem acima dos 32 bytes do validador;
- `SENHA_ADMIN`, `SENHA_PCP` e uma `SENHA_<PERFIL>` por conta compartilhada;
- `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`;
- `HC_PING_*` — as URLs de aviso do healthchecks.io (seção 8.4).

Residual registrado: o backup contém os hashes de senha. O bucket é privado e a chave do R2 é
restrita a ele.

## 8. Manutenção do banco

### 8.1 Por que a reconstrução de índice é a menor das rotinas

Medido no schema em 2026-10-02: as tabelas têm chave clusterizada em `INT IDENTITY` — linha nova entra
no fim do índice, e o livro `Movimentacao` (só de inclusão) segue o mesmo padrão. Não há chave
aleatória (GUID). Quem fragmenta são os índices secundários sobre FK (`IX_Movimentacao_EstruturaItem`,
`IX_EstruturaItem_Pai`…), devagar. Banco pequeno em NVMe quase não sente fragmentação. A exceção futura
é o `RefreshToken`: cresce sem limpeza (dívida já registrada no `CLAUDE.md`), e a limpeza, quando vier,
fragmenta por delete em massa. Por isso a ordem de prioridade é **backup → `CHECKDB` → estatísticas →
índices**.

### 8.2 Ferramenta e agendamento

A Maintenance Solution do Ola Hallengren (licença MIT), **versionada** em `deploy/sql/ola/`, com a
licença preservada e versão fixa — nada é baixado na hora de rodar. Instalada num **banco separado,
`DbaRastru`**, para não misturar procedimentos de terceiros com o schema do domínio (Database First,
`02-modelo-de-dados.sql` como fonte de verdade). A tabela `CommandLog` dela registra cada execução. O
limite de 10 GB do Express é por banco, então `DbaRastru` não ocupa espaço do `Rastreamento`.

O Express **não tem SQL Agent**: o `cron` do usuário `deploy` chama `deploy/hml/manutencao.sh
<tarefa>`, que roda o `sqlcmd` dentro do container.

### 8.3 As rotinas

| Quando (`America/Sao_Paulo`) | Tarefa | Detalhe |
|---|---|---|
| Todo dia, 03:00 | **Backup completo** + envio ao R2 | `DatabaseBackup` com `CHECKSUM` e verificação. Retenção: 3 dias local, 30 dias no R2 (o script apaga os mais velhos). Caminho: `rastru/<ambiente>/full/` |
| Só em PRD, de hora em hora | **Backup de log** + envio ao R2 | perda máxima de 1h. Em HML (`SIMPLE`) não existe; o `manutencao.sh` lê `RASTRU_AMBIENTE` |
| Domingo, 04:00 | **`CHECKDB`** | `DatabaseIntegrityCheck` em `Rastreamento`, `DbaRastru` e bancos de sistema |
| Domingo, depois do `CHECKDB` | **Índices + estatísticas** | `IndexOptimize`: reorganiza acima de 5%, reconstrói acima de 30%, ignora índices com menos de 1000 páginas, atualiza só estatísticas modificadas. A reconstrução trava a tabela (`ONLINE` é só da Enterprise), daí a madrugada de domingo |
| Domingo, depois dos índices | **Métrica de tamanho** | tamanho do arquivo de dados, quanto é sólido (`SUM(TamanhoEmBytes)` de `ArquivoDeComponente`) e quanto é sólido **histórico** (não referenciado por `Componente.ArquivoSolidoId`). Avisa acima de 5 GB (seção 8.5) |
| 1º domingo do mês | **Teste de restauração** | baixa do R2 o backup mais recente, restaura como `Rastreamento_Restauro`, roda `CHECKDB` nele, confere a contagem de algumas tabelas, apaga a cópia |

O teste de restauração é o que transforma "temos backup" em "**o backup de fora da VPS restaura**".

### 8.4 Saber que falhou

Backup que falha em silêncio é igual a não ter backup.

- **healthchecks.io** (grátis, 20 checks; usamos 5: backup, log, `CHECKDB`+índices, métrica,
  restauração) — cada tarefa do cron avisa ao terminar com sucesso; se o aviso não chega no horário,
  e-mail. Pega a falha e também o cron que nem rodou (VPS desligada, disco cheio).
- **Monitor de disponibilidade** (UptimeRobot ou similar, grátis) consultando `/api/saude` a cada 5 min.

### 8.5 O limite de 10 GB do Express — ponto em aberto

Achado na conversa, lendo o código: substituir um sólido **mantém o arquivo antigo** na tabela, por
desenho (o comentário de `IArquivoDeComponenteRepository` o trata como histórico — "o único registro de
que a peça teve outra geometria"). Então o crescimento é catálogo **e** revisões de geometria.

Saídas levantadas, da mais barata à mais cara — **nenhuma decidida**:

1. medir e ter gatilho (o que esta spec faz: a métrica da seção 8.3, aviso a 5 GB, e ainda sobram 5 GB
   de tempo);
2. comprimir o STL ao gravar (ganho **não medido**; mexe no contrato da 2B: `TamanhoEmBytes` e
   `Sha256` passariam a ser do conteúdo comprimido);
3. retenção do histórico de geometria (últimas N por Componente) — perde informação, decisão do usuário;
4. sólidos no R2, banco só com metadado — resolve de vez, mas **reverte a decisão da Fase 2B** (backup
   único, impossibilidade de divergir registro e arquivo); só com justificativa nova.

Descartadas: FILESTREAM (não existe no SQL Server para Linux), segundo banco para os sólidos (perde a FK
e o backup único sem os ganhos da saída 4), edição Standard (custo de licença), PostgreSQL (reabre a
stack).

**Pendente do usuário:** tamanho típico de um STL das peças e tamanho esperado do catálogo — para
trocar "anos" por uma estimativa. Se não vier antes, a estimativa sai da primeira medição no HML.

## 9. Provisionamento, verificação e runbook

### 9.1 `deploy/hml/provisionar.sh`

Rodado uma vez, como root; idempotente.

- usuário `deploy` com acesso só por chave; SSH sem login de root e sem senha; `fail2ban` no SSH;
- `ufw` aceitando só 22, 80 e 443;
- `unattended-upgrades` para atualizações de segurança;
- Docker Engine e plugin Compose pelo repositório oficial do Docker, com rotação de log (10 MB × 3
  por container);
- 2 GB de swap; fuso `America/Sao_Paulo`;
- `rclone`, `/srv/rastru/` com o `.env` a partir do exemplo, entradas do cron.

**Invariante:** porta publicada pelo Docker **passa por fora do `ufw`** (o Docker escreve as próprias
regras de iptables). O firewall não protege 1433 nem 8080; o que os protege é **nunca** serem
publicadas no Compose. O critério de aceite 7 confere de fora.

### 9.2 `deploy/hml/RUNBOOK.md`

Passo a passo de: provisionamento e primeiro deploy, recriar banco, rollback por SHA, restaurar
backup (inclusive o caso do alter destrutivo, seção 6.4), destrancar conta, trocar segredos e
promoção a PRD.

### 9.3 Critérios de aceite — na VPS real, registrados em relatório

1. Merge na `main` → `deploy.yml` verde → versão nova no ar (tag da imagem conferida via SSH).
2. HTTPS válido; `http://` redireciona; F5 em `/pedidos` carrega a tela.
3. **Sessão sobrevive além de 15 min** — o refresh funciona sobre TLS (cookie `Secure`).
4. **Rate limit vê o IP real** — 11 logins seguidos de um IP dão 429 enquanto outro IP ainda entra; o
   log de auth grava o IP do cliente, não o do Caddy.
5. Upload de STL de ~5 MB funciona (fecha a hipótese da seção 4.4).
6. `admin`/`Admin@123` **não** entra.
7. Portas 1433 e 8080 **fechadas** num `nmap` de fora.
8. Backup aparece no R2; o teste de restauração passa; o healthchecks recebe os avisos.
9. **Deploy quebrado de propósito** (API que não fica saudável) → rollback automático e workflow
   vermelho.
10. **Guarda de schema** — um PR que muda o `02` sem `alter-*.sql` fica vermelho.
11. Rulesets ativos — force-push e exclusão da `main` recusados para quem não é admin.

## 10. Fora do escopo, com gatilho

- **Revisor automático + aprovação de deploy em PRD** (seção 6.6) — gatilho: primeiro deploy de HML.
- **Vínculo do Operador ao Setor** — spec própria, a brainstormar depois desta aprovada. Decisões já
  dadas pelo usuário na conversa: um Setor por operador; `Usuario.SetorId` com **FK composta** contra
  `Perfil(Id, ExigeSetor)` e `CHECK` local (um `CHECK` não consulta outra tabela, e comparar com o Id
  do perfil seria frágil); **leitura de todas as filas livre, ação só no próprio Setor**; 403 no
  backend; `/fila` abre direto no Setor do operador; o Administrador continua sem vínculo. **Reverte**
  a decisão da spec da Fase 3, seção 6.1 ("conveniência por aparelho, não vínculo do usuário ao
  Setor"), e a reversão tem de ficar escrita lá.
- **Limite de 10 GB** (seção 8.5) — gatilho: aviso de 5 GB ou a estimativa do usuário.
- Segundo ambiente depois da promoção a PRD; Coolify (seção 3.5).
- **Retrancamento de conta sem limite** (seção "Defesas de autenticação em vigor" do `CLAUDE.md`) —
  continua sem conserto; com a VPS pública a exposição cresce, como aquele bullet já registra.
- **Ajustes de documentação depois da aprovação**, pedidos pelo usuário: `specs/03-arquitetura-tecnica.md`
  (seções "Hospedagem", "CI/CD" e "Pontos em aberto"), `specs/06-roadmap-mvp.md` (a exceção de ordem e
  "deploy manual, sem CI/CD") e `CLAUDE.md` (a lista de exceções de ordem, a stack — "CI/CD: nenhum
  ainda" — e a seção de comandos).
