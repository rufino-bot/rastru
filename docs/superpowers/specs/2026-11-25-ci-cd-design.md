# CI e CD do Rastru — desenho

Data: 2026-11-25. Branch: `claude/sweet-cannon-jo9k4m`, a partir da `main` em `722a09d` (pós-merge da Fase 6A,
rufino-bot/rastru#41). Conversado com o usuário em chat numa sessão de nuvem em 2026-11-25. As decisões do usuário são
D1 a D11 (seção 2.1); as chamadas do desenho, que o usuário confere ao ler, são C1 a C24 (seção 2.2). O builder check (um
revisor lendo a spec como quem vai planejar, conferindo as premissas contra o código) achou a C13 sem valor de prova, o
backup da HML faltando, uma premissa falsa sobre a conexão dos testes e a massa da vitrine vazia de Pedidos; os três
primeiros foram emendados como chamada do desenho (C13, seção 6.1, seção 4), e a massa virou a D11. O documento
escrito foi **aprovado pelo usuário em 2026-11-25**.

## 1. O que se quer

**O problema.** O Rastru está inteiro na `main`, mas só roda na máquina de quem desenvolve. Não há endereço que os
operadores possam abrir, e cada verificação depende de alguém subir o ambiente à mão. Também não há nada que rode as
suítes sozinho: hoje um PR só é verde porque alguém executou `dotnet test` e `npm test` e escreveu o resultado.

**O que se quer, agora.** Uma **vitrine** pública — a homologação (HML) — em `https://hml.rastru.app.br`, onde os
operadores entram com o perfil deles e conferem as telas, com massa de demonstração (D1). Cada merge na `main` chega lá
sozinho, com as suítes passando antes. **Depois**, a produção (PRD) em `https://rastru.app.br`, por pelo menos um mês, com
a mesma imagem que foi conferida na HML (D2).

**O que conta como pronto (este desenho inteiro).**

1. Todo PR para a `main` roda build e suítes (backend contra SQL Server real, front com Vitest e `npm run build`), e o
   resultado aparece no PR.
2. Um merge na `main` com tudo verde publica na HML sem ninguém tocar na VPS, aplicando antes os scripts de schema
   novos, e o pipeline confere que a versão subiu.
3. Os dados que os operadores criaram na HML sobrevivem a um deploy (D6). Existe um botão para recriar a HML do zero.
4. Nenhum usuário da HML ou da PRD tem a senha que está no repositório (público).
5. Existe o botão "promover para PRD", com aprovação do usuário, que publica na PRD a mesma imagem da HML. **Ligar a PRD**
   (seção 9) é a última parte e só acontece quando o usuário decidir.

**Não-objetivos.** Alta disponibilidade, mais de uma instância da API (o `CacheDasIdades` é singleton em memória e
pressupõe uma só), deploy sem nenhum segundo de indisponibilidade, monitoramento com alerta, Kubernetes, IaC do provedor
(a VPS é criada à mão, uma vez).

**Anti-objetivos** (funcionaria tecnicamente e ainda assim seria fracasso):

- um usuário com senha do repositório num endereço público — `admin/Admin@123` está escrito em `db/seed.sql`, que é
  público;
- a PRD rodando um binário que não é o que foi conferido na HML;
- o pipeline de deploy decidindo algo sozinho (nada de agente de IA no caminho da publicação; D9);
- o CI vermelho por flakiness virar hábito de "roda de novo até passar" sem registro.

## 2. Decisões

### 2.1 Do usuário (D)

- **D1.** O primeiro deploy é **vitrine e conferência dos operadores**, com massa de demonstração — não uso real.
- **D2.** Depois, **produção por pelo menos um mês**, com dados reais. Fica para a seção 9, ligada quando o usuário decidir.
- **D3.** Reabre a decisão "CI/CD fora do MVP" de `specs/03-arquitetura-tecnica.md` (seção "CI/CD") e passa este trabalho
  **na frente da Fase 6B**. Registrar como exceção em `specs/06-roadmap-mvp.md` e no «Ordem de implementação» do
  `CLAUDE.md`.
- **D4.** **VPS paga**, de um dos provedores estudados na conversa (Hetzner, KingHost ou similar), escolhida pelo usuário
  depois; **não** o servidor caseiro de um amigo. Só o Rastru roda nela (a ideia de projetos pessoais saiu).
- **D5.** Domínio **`rastru.app.br`**, registrado pelo usuário no registro.br.
- **D6.** A HML **preserva os dados** entre deploys: o deploy aplica só os scripts de schema novos e anota o que já
  rodou. Recriar a HML é ação manual.
- **D7.** Abordagem **A** (seção 3): o GitHub Actions gera a imagem, publica no registro de imagens do GitHub (GHCR) e
  entra na VPS por SSH para baixar, migrar, trocar e conferir.
- **D8.** HML e PRD na **mesma VPS** (o usuário trouxe as duas; a divisão numa máquina só é a proposta C3, que ele não
  contestou).
- **D9.** **Sem Copilot** por enquanto (nem review de PR nem agente); os créditos ficam guardados.
- **D10.** Começar enxuto onde a vitrine permite (backup e rollback simples na HML), sem abrir mão de TLS e de senhas fora
  do repositório.
- **D11.** A HML nasce com uma **massa de vitrine nova**, `db/massa-vitrine.sql`: o catálogo do `db/seed-demo.sql` mais
  uns 6 a 8 Pedidos com nomes, clientes e prazos plausíveis, cobrindo os estágios que as telas mostram — um atrasado, um
  pausado, um no meio da produção (com Peças divididas entre Setores e algo aguardando coleta), um com Kit na Solda, um
  aguardando expedição (com aferição), e ao menos um com perda/reposição. Não as `db/massa-conferencia-*.sql`, que têm cara
  de teste (`CONF-*`, entrega em 2099) e não foram feitas para rodar juntas.

### 2.2 Chamadas do desenho (C) — conferir ao ler

- **C1. Máquina:** Linux x86-64 (Ubuntu LTS), **4 GB de RAM no mínimo**, 2 vCPU, 40 GB+ de disco; 8 GB se o preço
  permitir, para HML e PRD ligadas juntas com folga. ARM está fora (o SQL Server para Linux não roda em ARM).
- **C2. Tudo em Docker Compose** na VPS: `sqlserver` (Express), `api-hml`, `api-prd` (só na seção 9) e `caddy`.
- **C3. Um SQL Server, dois bancos:** `Rastreamento_HML` e `Rastreamento_PRD`. Dois SQL Servers não cabem em 4 GB.
- **C4. SQL Server Express** (`MSSQL_PID=Express`, gratuito), com `MSSQL_MEMORY_LIMIT_MB` ≈ 2048. Teto do Express:
  10 GB por banco e ~1,4 GB de cache — sobra para a vitrine; para a PRD, os sólidos e miniaturas em blob contam nos 10 GB
  (medir antes de ligar a PRD). A porta 1433 **não** é publicada para fora da rede do Compose.
- **C5. Caddy** na frente: TLS automático (Let's Encrypt) para `rastru.app.br` e `hml.rastru.app.br`, sem limite de corpo
  por padrão (resolve o item 4 dos «Pontos em aberto» de `specs/03`), e `encode zstd gzip` — que fecha a pendência
  "resposta sem compressão" registrada na 6A sem código na API.
- **C6. A API serve o front.** A imagem leva o build do Vite em `wwwroot`; uma origem só por ambiente (o cookie de refresh
  é `SameSite=Strict`). Ver seção 5.1 para a ordem do pipeline.
- **C7. Imagem única**, multi-stage: build do front (Node) e da API (.NET 10 SDK), runtime `aspnet` **com ICU** — o código
  usa `CultureInfo.GetCultureInfo("pt-BR")` (`Quantidades`, `AvaliadorDeImportacao`), então nada de
  `InvariantGlobalization` nem imagem *chiseled* sem a variante `-extra`. Fuso não precisa de `tzdata`: o
  `HorarioDeBrasiliaJsonConverter` usa offset fixo, de propósito.
- **C8. Tag da imagem é o SHA do commit** (`ghcr.io/rufino-bot/rastru:<sha>`); nada de `latest` no deploy. A imagem é
  pública no GHCR, como o código; **nenhum segredo entra nela**.
- **C9. Migrações versionadas:** pasta nova `db/migracoes/`, scripts `NNNN-descricao.sql` aplicados em ordem, e a tabela
  `dbo.MigracaoAplicada (Nome PK, AplicadaEm)` anotando o que rodou. Seção 6.
- **C10. Convenção nova de schema:** de agora em diante, mudança de schema = `specs/02-modelo-de-dados.sql` atualizado
  **e** um script em `db/migracoes/`. Os `db/alter-*.sql` existentes ficam (servem a bancos de dev antigos) e não viram
  migração: um banco novo nasce do `02`, que já os contém. A seção «Comandos» do `CLAUDE.md` ganha a regra.
- **C11. Banco novo** (HML na criação, HML recriada, PRD na primeira vez): `02` + perfis + usuários com senha de segredo
  + (só HML) `db/seed-demo.sql` e `db/massa-vitrine.sql` (D11), e **todas** as migrações existentes anotadas como aplicadas (o `02` já as contém).
- **C12. Usuários com senha de segredo.** Os nomes da HML são os mesmos do `db/seed.sql` (`admin`, `pcp`, `operador`,
  `movimentador`, `separador`, `qualidade`, `gestao`), para a conferência por perfil funcionar igual; as senhas vêm de
  segredos do ambiente e o hash é gerado com o `BCryptPasswordHasher` de produção (seção 6.3). O `db/seed.sql` continua
  igual para dev.
- **C13. Verificação depois do deploy** (o pipeline falha se qualquer uma falhar): `GET /api/saude` responde 200; `GET /`
  devolve o `index.html`; `GET /pedidos` (rota do SPA, como F5) devolve o `index.html`; e **nenhum dos sete usuários do
  `db/seed.sql` tem no banco do ambiente a senha do seed** — a guarda executável do anti-objetivo 1. Essa última é um
  comando da imagem, irmão do `definir-senha` (6.3), que lê os hashes do banco e os confere com o `BCryptPasswordHasher`
  contra as senhas do seed, **sem passar pelo login**. *Emenda do builder check:* a versão anterior desta chamada
  testava o login `admin/Admin@123` esperando 401, e isso não prova nada — `AutenticarUsuarioUseCase` responde 401 a conta
  trancada antes de conferir a senha, e cinco deploys seguidos, cada um contando uma falha, trancariam o `admin`; além
  disso, só olhava um dos sete.
- **C14. Rota de saúde** `GET /api/saude`, anônima, que confere a conexão com o banco (sem detalhe do erro no corpo).
- **C15. `ForwardedHeaders`** ligado com `KnownNetworks` **só** a sub-rede fixa da rede Docker do Compose (onde o Caddy
  mora) — nunca listas limpas nem `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (o `CLAUDE.md` registra por quê). Fecha o item 2
  dos «Pontos em aberto» de `specs/03`: o rate limit do login volta a ser por cliente.
- **C16. `SigningKey` por ambiente**, de segredo, por variável (`Jwt__SigningKey`); o `JwtOptionsValidator` já derruba a
  API se faltar. Fecha o item 3 de `specs/03`. Connection string também por variável.
- **C17. A API conecta com login SQL próprio** por ambiente (`rastru_hml`, `rastru_prd`), dono só do próprio banco; `sa`
  só para criar banco e migrar. Assim a HML não alcança o banco da PRD.
- **C18. GitHub Environments** `hml` e `prd`, cada um com seus segredos; `prd` com **revisor obrigatório** (o usuário) —
  é isso o "botão com aprovação".
- **C19. Deploys serializados** por ambiente (`concurrency` do Actions): dois merges seguidos não se atropelam.
- **C20. Rollback** = rodar o deploy de novo com o SHA anterior (`workflow_dispatch` com o SHA). Migrações só avançam:
  um rollback por cima de migração que quebra a versão anterior não é suportado; quem escreve a migração a mantém
  compatível com a versão anterior quando puder, e diz no PR quando não puder.
- **C21. Preparação da VPS** por um script versionado (`deploy/preparar-vps.sh`), rodado **pelo usuário**, uma vez, com
  root: Docker, usuário `deploy` só com chave, SSH sem senha e sem root, firewall com 22/80/443, swap de 2 GB,
  atualização automática de segurança, rotação de log do Docker.
- **C22. CI em todo PR para a `main` e em todo push na `main`**; o deploy da HML depende do CI do mesmo commit verde.
  Tornar o CI **check obrigatório** na `main` é configuração do repositório que o usuário faz (o plano lista o passo).
- **C23. Partição** em quatro partes, cada uma com plano e PR próprios, nesta ordem (seção 8). As partes 1 e 2 não
  dependem da VPS; a 3 depende dela e do domínio; a 4 é a PRD.
- **C24. Documentação:** `specs/03` (seções «Hospedagem», «CI/CD», «Pontos em aberto»), a linha "CI/CD: nenhum ainda" do
  `CLAUDE.md`, um `deploy/LEIAME.md` com o passo a passo do usuário (contratar, preparar a VPS, DNS, segredos, check
  obrigatório, recriar a HML, rollback).

## 3. Abordagens consideradas

**A. O GitHub gera a imagem; a VPS só baixa e sobe (escolhida, D7).** Merge na `main` → CI verde → build da imagem →
GHCR → SSH na VPS: `docker compose pull`, migrar, `up -d`, verificar. A PRD recebe a mesma imagem. A VPS não compila,
cabe em 4 GB; toda versão fica guardada com nome; rollback é subir a anterior. Custo: SSH aberto ao GitHub, com usuário
e chave dedicados.

**B. O GitHub manda a VPS compilar** (SSH + `git pull` + `docker compose build` na VPS). Mais simples, mas compilar .NET
e Vite disputa memória com o SQL Server em 4 GB, e a PRD seria recompilada — não o binário conferido.

**C. Runner do GitHub instalado na VPS.** Dispensa SSH de entrada, mas o repositório é público: o próprio GitHub
desaconselha runner próprio em repo público (PR de fork pode rodar código na máquina). Descartada.

**D. A VPS se atualiza sozinha (Watchtower consultando o GHCR).** Sem SSH, mas não garante "migrar antes de trocar o
container" nem o botão de promover com aprovação — perde o que D6 pede.

## 4. O CI

Arquivo `.github/workflows/ci.yml`, em `pull_request` para `main` e `push` em `main`. Dois jobs em paralelo, runners
hospedados do GitHub (gratuitos em repo público).

**Backend.** Serviço `mcr.microsoft.com/mssql/server:2022-latest` na porta 1433, com o banco **`Rastreamento`** e a senha
que as suítes já esperam (`sa` / `Your_strong_Pass123` — a de dev, já pública; o banco do CI é descartável). Não há
`appsettings` de teste: a conexão está fixa em `TesteComBanco` e em `ArquivoDeComponenteRepositoryTests`, e a `Api.Tests`
usa o `appsettings.json` da API — então nome do banco, host e porta têm de ser exatamente esses, e variável de ambiente
não os muda.
Aplica `specs/02-modelo-de-dados.sql` e `db/seed.sql` com `sqlcmd -b -f 65001` (o mesmo que o `scripts/backend-na-nuvem`
faz), depois `dotnet build Rastreamento.slnx -warnaserror` e `dotnet test Rastreamento.slnx -m:1` (o `-m:1` é obrigatório;
ver «Processos de teste também competem pelo banco» no `CLAUDE.md`). Publica os `.trx` como artefato.

**Front.** Em `web/`: `npm ci`, `npm test -- --run --maxWorkers=2`, `npm run build` (o build faz parte do ciclo porque o
Vitest não faz typecheck).

**Flakiness.** Há um flaky conhecido no front (`lembra o Setor cuja fila carregou`, 1 em 1.912, causa não investigada) e
um histórico de deadlock na suíte de Api, já mitigado. Regra: CI vermelho que passa ao rodar de novo **é registrado**
(issue ou ledger, com o nome do teste), não só re-rodado.

**Ao builder:** cache de NuGet e npm, versão exata das actions, como esperar o SQL Server ficar pronto, se o `sqlcmd` vem
do próprio container de serviço ou de instalação no runner.

## 5. A imagem e as mudanças na API

### 5.1 Servir o SPA sem quebrar a guarda do `/api`

Hoje a guarda em `Program.cs` devolve 404 para todo `Path` que não começa com `/api` (ordinal). Com o SPA na mesma
aplicação, o comportamento novo é:

- `Path` começa com `/api` (ordinal): pipeline da API, como hoje.
- `Path` começa com `/api` só ignorando caixa (`/API/setores`): **continua 404** — é o caso que a guarda existe para
  proteger (cookie de refresh sob `Path` de outra caixa).
- Qualquer outro `GET`/`HEAD`: arquivo estático de `wwwroot` se existir; senão, `index.html` (fallback do SPA, para o F5
  em `/pedidos/5`).
- Outros métodos fora de `/api`: 404.

O comentário da guarda e a seção «Prefixo `/api`» do `CLAUDE.md` são reescritos junto, e `AuthEndpointsTests.PrefixoDeApi`
ganha os casos novos. Nos testes (sem `wwwroot`), o fallback não pode mascarar o 404 que os testes atuais esperam para
caminhos nus — **ao builder** decidir como (ex.: fallback só registrado quando `wwwroot/index.html` existe), com teste
que mostre os dois modos.

**Cache:** `index.html` sem cache; os assets com hash do Vite com cache longo.

### 5.2 Saúde, cabeçalhos encaminhados, segredos

- `GET /api/saude` (C14): anônimo, fora do rate limit do login, 200 com o banco alcançável, 503 sem.
- `ForwardedHeaders` (C15), configurado por opção (a sub-rede vem de configuração), desligado em Development e nos testes.
- `SigningKey` e connection string por variável de ambiente (C16); nada muda no `appsettings.json` commitado.

### 5.3 Dockerfile

Na raiz, multi-stage (C7). `.dockerignore` deixa fora `.superpowers/`, `node_modules`, `bin/obj`, `tests/` e `db/massa-*` **exceto**
`db/massa-vitrine.sql`.
A imagem roda como usuário não-root e expõe só a porta HTTP interna (o Caddy termina o TLS). A imagem leva também
`specs/02-modelo-de-dados.sql`, `db/seed.sql`, `db/seed-demo.sql`, `db/massa-vitrine.sql` e `db/migracoes/` — o deploy usa os scripts **da
versão que está subindo**, não os de outra.

## 6. Banco: criar, migrar, usuários

### 6.1 `db/migracoes/` e `dbo.MigracaoAplicada`

- `dbo.MigracaoAplicada (Nome NVARCHAR(200) PK, AplicadaEm DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME())` entra no
  `specs/02-modelo-de-dados.sql` (é schema; o `02` continua a fonte de verdade) e num script de migração próprio para
  bancos de dev existentes.
- O migrador lista `db/migracoes/*.sql` em ordem de nome, pula os que estão na tabela e aplica cada um com
  `sqlcmd -b -f 65001`, anotando o nome **na mesma transação** do script quando o script permitir; se um falha, o deploy
  para antes de trocar o container.
- Hoje a pasta nasce **vazia** (ou só com o script da própria tabela): todo o schema atual está no `02`.
- **Backup antes de migrar, também na HML** (chamada do desenho, do builder check): quando há migração pendente, o
  deploy faz um `BACKUP DATABASE` local do banco do ambiente antes da primeira, e guarda os **3 últimos** no volume do SQL
  Server. Motivo: os scripts têm vários lotes `GO`, não rodam inteiros numa transação, e uma migração que falhe no meio
  deixaria a HML meio migrada — o que D6 (os dados dos operadores sobrevivem) não aceita. O Express não comprime backup;
  com a massa de vitrine o tamanho é pequeno. Restaurar é manual, pelo `LEIAME`. Na PRD vale o mesmo, **mais** o backup
  fora da VPS da seção 9.

### 6.2 Banco novo (C11)

Se o banco do ambiente não existe: `CREATE DATABASE`; aplica o `02`; aplica a parte de perfis do seed; cria o login SQL
do ambiente (C17); cria os usuários (6.3); na HML, aplica `db/seed-demo.sql` e depois `db/massa-vitrine.sql`; anota **todas** as migrações de
`db/migracoes/` como aplicadas.

**Ao builder:** como aplicar só os perfis sem duplicar a lista (ex.: separar `db/seed.sql` em `db/seed-perfis.sql` +
usuários de dev, com o `seed.sql` chamando os dois, ou o deploy rodando o `seed.sql` inteiro e trocando todas as senhas
antes de a API subir — desde que a API não suba entre os dois passos).

### 6.3 Senhas fora do repositório

Um comando da própria imagem (ex.: `dotnet Rastreamento.Api.dll definir-senha <usuario>`, lendo a senha de variável de
ambiente, nunca de argumento) gera o hash com o `BCryptPasswordHasher` e grava. Roda no banco novo e pode rodar de novo
para trocar uma senha. Os segredos por ambiente: `SENHA_ADMIN`, `SENHA_PCP`, `SENHA_OPERADOR`… (o builder pode reunir
num segredo só, em JSON). A C13 confere o resultado em cada deploy.

### 6.4 Recriar a HML

`workflow_dispatch` "recriar homologação", só para o ambiente `hml`: derruba `api-hml`, `DROP DATABASE Rastreamento_HML`,
segue 6.2, sobe `api-hml`, verifica. Nunca existe para `prd`.

## 7. O CD

### 7.1 Arquivos na VPS

`/opt/rastru/` com `compose.yaml` (versionado em `deploy/`), `Caddyfile` (versionado), e um `.env` por ambiente com os
segredos, escrito **pelo pipeline** a cada deploy a partir dos segredos do GitHub Environment (a VPS não é a fonte de
verdade dos segredos). Volume nomeado para os dados do SQL Server.

### 7.2 Workflow `deploy.yml`

- **Gatilho HML:** `workflow_run` do CI concluído com sucesso em `push` na `main` (ou um job depois do CI no mesmo
  workflow — **ao builder**), e `workflow_dispatch` com SHA (rollback, C20).
- **Passos:** build da imagem com tag do SHA → push no GHCR → SSH (chave do segredo, `known_hosts` fixado em segredo,
  não aceito na hora) → copiar `compose.yaml`/`Caddyfile` → escrever o `.env` → `docker compose pull` → banco novo ou
  migrar (6.1/6.2) → `docker compose up -d api-hml` → verificação C13 contra `https://hml.rastru.app.br`, com algumas
  tentativas → `docker image prune` mantendo as últimas imagens.
- **Promover para PRD:** `workflow_dispatch` com o SHA (padrão: o que está na HML), `environment: prd` (aprovação
  obrigatória, C18); **não** builda — usa a imagem que já está no GHCR; mesmos passos com o banco `Rastreamento_PRD`.
  Antes de migrar a PRD, um backup (seção 9).

### 7.3 DNS

No registro.br, registros A (e AAAA se a VPS tiver IPv6) de `rastru.app.br` e `hml.rastru.app.br` para o IP da VPS. O
Caddy só consegue o certificado depois disso; o `LEIAME` põe esse passo antes do primeiro deploy.

## 8. Partição (C23)

1. **CI** — `ci.yml` e a regra de flakiness. Não depende de nada. Pronto: um PR aberto mostra os dois jobs verdes.
2. **A aplicação pronta para container** — Dockerfile, SPA servido (5.1), saúde, `ForwardedHeaders`, comando de senha,
   `db/migracoes/` + `dbo.MigracaoAplicada` + migrador + criação de banco novo, `db/massa-vitrine.sql` (D11),
   `deploy/compose.yaml` e `Caddyfile`.
   Verificável sem VPS: o Compose sobe localmente (com Caddy em modo local) e passa a C13.
3. **HML no ar** — `deploy.yml` (HML, recriar, rollback), `deploy/preparar-vps.sh`, `deploy/LEIAME.md`. Depende de o
   usuário contratar a VPS, rodar o preparo, apontar o DNS e cadastrar os segredos. **Conferência do usuário no navegador**
   (PC e celular, os perfis) antes da review de branch, como manda o `CLAUDE.md`.
4. **PRD** — seção 9. Só quando o usuário decidir.

## 9. A PRD (parte 4) — o que já se sabe e o que falta decidir

Já desenhado: o ambiente `prd` com aprovação, a promoção da mesma imagem, o banco `Rastreamento_PRD` com login próprio,
`api-prd` no Compose atrás do Caddy em `rastru.app.br`.

**Falta decidir, com o usuário, antes de ligar:**

- **Backup fora da VPS** — onde (um backup na mesma máquina não salva nada se ela morrer), com que frequência, quanto
  tempo guardar, e um teste de restauração feito uma vez.
- **Usuários reais** — não há tela de gestão de usuários; cada conta hoje nasce por SQL. Quem são, e se o comando de
  senha (6.3) basta.
- **Os 10 GB do Express** — medir o tamanho do banco com os sólidos reais.
- **4 ou 8 GB** — com HML e PRD ligadas juntas, 4 GB fica justo; ou a HML passa a ficar desligada fora da conferência.

## 10. Riscos e o que fica com o builder

- **Tempo do CI:** a suíte de Api levou de ~1 min a ~10 min conforme a massa no banco; no CI o banco nasce limpo. Não
  medido no runner do GitHub.
- **SQL Server no runner:** o container de serviço do SQL Server 2022 em runner `ubuntu-latest` é caminho conhecido, mas
  não medido aqui.
- **Let's Encrypt** tem limite de emissões: o Caddy guarda o certificado em volume, para não pedir de novo a cada deploy.
- **Indisponibilidade no deploy:** alguns segundos na troca do container; aceito (não-objetivo).
- **A massa de vitrine (D11) segue as regras do livro:** só movimentos válidos (o que a API faria), idempotente, chaveada
  por número de Pedido e nome, carregada com `-b -f 65001`, no molde do `db/massa-carga-6a.sql` — com a conservação de
  quantidade (regra 9) fechando em todo nó. Quando o schema mudar, ela muda junto (só roda em banco novo, mas roda a cada
  "recriar a HML"); o CI a carrega num banco limpo para ela não apodrecer em silêncio.
- **Ao builder:** a forma exata dos workflows, nomes de segredos, o formato do `.env`, o `Caddyfile`, os testes do
  fallback do SPA, e o migrador (shell com `sqlcmd` do container do SQL Server, ou comando .NET na imagem), desde que as
  regras das seções 5 a 7 valham.
