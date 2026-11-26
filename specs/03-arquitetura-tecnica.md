# 03 - Arquitetura Técnica

## Backend — .NET (C#)

Estrutura recomendada em camadas (Clean Architecture simplificada), para manter regras de
negócio (recursão, conservação de quantidade do lote, transição de status) isoladas de EF Core e
da API:

```
src/
  Rastreamento.Domain/          # Entidades, regras de negócio, interfaces de repositório
  Rastreamento.Application/     # Casos de uso (ex.: AbrirPedido, RegistrarEntradaSetor,
                                 # RegistrarSaidaSetor, AvaliarDimensional, AbrirRetrabalho)
  Rastreamento.Infrastructure/  # EF Core, repositórios, DbContext, migrations
  Rastreamento.Api/             # Controllers ASP.NET Core, DTOs, validação de entrada
tests/
  Rastreamento.Domain.Tests/
  Rastreamento.Application.Tests/
```

- **EF Core**: usar *Database First* neste projeto — o DDL (`02-modelo-de-dados.sql`) já
  é a fonte de verdade do schema; gerar/mapear as entidades a partir dele em vez de deixar
  o EF criar o banco via migrations do zero. Isso evita divergência com as constraints e
  índices já definidos (o índice filtrado de indivisibilidade foi removido — o lote é divisível).
- Regras que hoje estão como `CHECK` no banco (ex.: Peça sem pai, Retrabalho exige
  PedidoOrigemId) devem **também** ser validadas na camada `Application`, retornando erro
  de negócio claro em vez de deixar a exceção de banco estourar até a API.
- Transição de status do Pedido/Agrupamento (ex.: fechar Agrupamento → verificar se é o
  último → fechar Pedido) deve ser um caso de uso explícito
  (`ConcluirAgrupamentoUseCase`), não um trigger de banco — mais fácil de testar e de
  auditar.
- `AbrirRetrabalhoUseCase` recebe `MotivoRetrabalho` (obrigatório, um dos valores
  `ReprovacaoDimensional`/`ErroInterno`/`SolicitacaoCliente`/`Perda`) e, opcionalmente, o
  `RelatorioDimensionalAvaliacaoId` (reprovação) **ou** o `PerdaId` (perda) que motivou a
  abertura — reprovação não gera retrabalho automaticamente, é uma ação separada do
  usuário.

## Autenticação e Autorização

- **Login próprio** (usuário/senha) com **JWT** — não usar Windows Authentication.
- Tabelas `Usuario` e `Perfil` já estão no DDL (`02-modelo-de-dados.sql`).
- Perfis do MVP: Operador, Separador, Movimentador, PCP, Qualidade, Gestão, Administrador
  (ver `00-visao-geral.md`). O Movimentador foi decidido em 2026-09-15 e passa a existir no
  sistema na Fase 3. O Separador é o Almoxarifado renomeado na Fase 4 (D5 da spec da Fase 4).
- **Decidido na Fase 0:** hash de senha com **BCrypt** (`BCrypt.Net-Next`) — não ASP.NET
  Core Identity (mais enxuto e compatível com o Database First das tabelas `Usuario`/`Perfil`).
  Emissão de JWT manual contendo `PerfilNome` como claim `role`; autorização por perfil via
  `[Authorize(Roles = "...")]` nos controllers.
- **Decidido na Fase 0:** sessão com **access token curto (em memória no front) + refresh
  token opaco** rotacionado. O refresh token é guardado no front em **cookie httpOnly/Secure**
  e no backend como **hash SHA-256** numa tabela `RefreshToken` (revogável) — ver
  `docs/superpowers/specs/2026-07-23-fase-0-walking-skeleton-design.md` e a tabela
  `RefreshToken` em `02-modelo-de-dados.sql`. Nunca usar `localStorage` para tokens.
- Frontend esconde/desabilita rotas e ações conforme o perfil do usuário logado.
- **Hardening da Fase 0 (implementado):** detecção de reuso de refresh token (reapresentar um token
  já rotacionado revoga toda a família de tokens ativos do usuário); lockout temporário de conta
  (`Usuario.FalhasConsecutivas` / `Usuario.BloqueadoAte`, configurável em `Lockout`); rate limit
  por IP no `/auth/login` (middleware nativo do ASP.NET Core, configurável em `RateLimit`); logging
  dos eventos de auth via `ILogger`. Todas as falhas continuam com resposta única e genérica —
  nenhuma dessas defesas pode ser observada pelo corpo ou pelo status da resposta. `RefreshToken`
  tem `RowVersion` (`ROWVERSION`/token de concorrência otimista) para fechar a corrida entre a
  queima de família e uma rotação já em voo — ver `CLAUDE.md` para os gatilhos benignos que também
  disparam a queima.
- **Requisito vinculante do cliente HTTP do frontend:** `/auth/refresh` **deve** ser single-flight
  — no máximo uma chamada em voo por vez. Qualquer código que tome 401 durante uma renovação em
  andamento **deve** esperar o resultado dela em vez de disparar um segundo refresh (fila/promise
  compartilhada, não uma segunda chamada). Consequência de não seguir isto: dois `/auth/refresh`
  concorrentes com o mesmo cookie fazem o segundo apresentar um token que o primeiro já rotacionou
  — a API detecta como reuso e queima a família inteira, deslogando o usuário de todos os
  dispositivos por um bug do próprio cliente, não por um ataque. Registrado agora porque o
  frontend só nasce na Fase 1 e este requisito tem que valer desde o primeiro cliente HTTP escrito.

### Queima de família — o que single-flight resolve e o que não resolve

Contexto para quem for investigar um deslogamento inexplicado, ou avaliar afrouxar a detecção.

**Single-flight cobre só um dos três gatilhos benignos.** Os outros dois são de rede e nenhum
código de cliente elimina:

| Gatilho | Origem | Single-flight resolve? |
|---|---|---|
| Duplo-refresh concorrente | corrida no cliente | **sim** |
| Resposta perdida no retry (rotaciona A→B, o 200 se perde, o cliente repete A) | rede | não |
| Replay pós-logout (o 204 se perde, o cookie sobrevive) | rede | não |

O segundo é o mais provável neste deploy — navegador Android no WiFi de fábrica.

**O sintoma engana de propósito.** Quando um desses gatilhos dispara, o log grava
`"Reuso de refresh token detectado"` e todas as sessões do usuário caem. Quem investigar vai
procurar invasor, não corrida de cliente ou pacote perdido. Antes de tratar como incidente de
segurança, verificar se houve **um** usuário afetado logo após instabilidade de rede (benigno) ou
se há padrão de repetição por conta/IP (aí sim, suspeito).

**A saída avaliada e deliberadamente NÃO escolhida** (decisão de 2026-07-28, hardening de auth):
janela de graça no backend — aceitar um token revogado há menos de N segundos **cuja cadeia
`SubstituidoPorTokenHash` esteja íntegra**, rejeitando-o com o 401 genérico sem queimar a família.
A coluna já existe e é populada na rotação, então o custo é baixo. Foi descartada porque afrouxa a
detecção: o ladrão ganha N segundos de reuso impune. Se os falsos positivos incomodarem em produção,
**este é o caminho já analisado** — não precisa ser redescoberto do zero, só re-decidido com dados
reais de frequência.

## Frontend — React + TypeScript

- Mobile-first responsivo: prioridade em telas de operador de setor, que serão usadas
  majoritariamente em celular Android via navegador.
- Sugestão de stack: Vite + React + TypeScript, React Query (ou equivalent) para
  cache/sincronização com a API, biblioteca de componentes leve (ex.: MUI ou
  Tailwind + componentes próprios) — decisão fina pode ficar para a Fase 0 do roadmap.
- **Decidido na Fase 0:** biblioteca de UI = **Tailwind CSS + componentes próprios** (não MUI),
  por controle do visual e bundle leve mobile-first.
- **Toda chamada de API leva o prefixo `/api`, aplicado num lugar só** — o `rota()` de
  `web/src/api/client.ts`. Quem escreve chamada nova passa o caminho **sem** o prefixo
  (`/setores`), como está em `05-api-endpoints.md`; escrever `/api/...` no call site duplicaria.
  O motivo, e o fechamento do prefixo, estão em `05-api-endpoints.md`.
- **PWA/offline: não é necessário no MVP.** Confirmado com o negócio — pode entrar depois
  se o uso em campo mostrar necessidade real (ex.: instabilidade de wifi na fábrica). Não
  desenhar a camada de estado pensando nisso agora, para não adicionar complexidade
  desnecessária cedo. **Ressalva de 2026-09-15:** o **PWA mínimo para notificação push**
  (manifesto, ícones e service worker **sem cache de API**) está previsto na Fase 3C de
  `06-roadmap-mvp.md` e **não reabre** esta decisão — ela trata de funcionar sem rede, e o push
  não precisa disso.

## Banco de dados

- SQL Server na VPS, conforme `02-modelo-de-dados.sql`.
- Sugestão: ambiente de desenvolvimento local via Docker (`mcr.microsoft.com/mssql/server`)
  para os agents/desenvolvedores rodarem o schema sem depender do banco da VPS
  durante o desenvolvimento; deploy final aponta para a VPS.

## Hospedagem

VPS paga com domínio próprio, escolhida em 2026-09-12. Motivo declarado pelo usuário: deixar o
link de acesso do Rastru mais "produto" — em vez de um endereço de rede interna, um domínio
próprio.

- Backend: **decidido (2026-11-25, parte 2 do CI/CD): container Docker, com o Caddy como proxy reverso.** Uma imagem
  única (`Dockerfile` multi-stage: build do Vite, publish da API e as ferramentas do banco), e o Compose em `deploy/`
  (`compose.yaml` e `Caddyfile`) sobe o SQL Server, a API e o Caddy, que termina o TLS (certificado automático na VPS,
  CA local em `localhost`) e manda `X-Forwarded-For` e `X-Forwarded-Proto`. `deploy/subir-local.sh` sobe tudo do zero
  na máquina local e roda a verificação (`deploy/verificar.sh`); IIS e nginx ficam descartados. O deploy público em si
  (VPS, DNS, `deploy.yml`) é da parte 3.
- Frontend: **decidido (2026-11-25, parte 2 do CI/CD): a API serve o build do Vite.** O build vai
  para o `wwwroot` da imagem e, quando o `wwwroot` tem `index.html`, `SpaDaApi.UsarSpa` registra o
  ramo do SPA **antes** da guarda do prefixo `/api` (ela é um 404 cego para tudo que não começa
  com `/api`, e registrada antes dos estáticos mataria `index.html`, os assets e toda rota do SPA).
  Sem `index.html` a API fica só-API, como antes. Arquivo que existe é servido (sob `/assets/`
  com cache de um ano e `immutable`, o resto com `no-cache`); caminho sem extensão que não é
  arquivo devolve o `index.html` (o F5 numa rota do SPA); caminho com extensão que não existe é
  404; outro método fora de `/api` é 404; `/API/x`, só na caixa, continua 404. Ver a seção
  «Prefixo `/api`» do `CLAUDE.md`.
- **Se o SPA e a API ficarem na mesma origem** (o segundo caso acima, e o mais provável: o cookie
  de refresh é `SameSite=Strict`, que inviabiliza cross-site), era obrigatório que os caminhos nus
  da API tivessem parado de responder antes do deploy — senão a colisão entre rota de SPA e rota
  de API voltaria, e em produção não há dev server para interceptar: `GET /pedidos` como
  navegação de documento cairia na API e responderia 401. **Feito:** desde o fechamento do
  prefixo (2026-08-04), só `/api` responde; os caminhos nus devolvem 404.
  Ver a seção de prefixo em `05-api-endpoints.md`.

## CI/CD

- **O CI existe** (desde 2026-11-25): o workflow `.github/workflows/ci.yml`, no GitHub Actions, roda em todo PR para a
  `main` e em todo push na `main`. O job `backend` sobe o SQL Server pelo `scripts/banco-de-teste` e roda
  `dotnet build -warnaserror` e `dotnet test -m:1`; o job `front` roda, em `web/`, `npm ci`, `npm test -- --run --maxWorkers=2` e
  `npm run build`; o job `imagem` (parte 2) constrói a imagem, sobe o Compose local (`bash deploy/subir-local.sh`) e roda
  a verificação C13. Primeira execução dos dois primeiros: PR rufino-bot/rastru#42, em `44d209e` (2026-11-25), run https://github.com/rufino-bot/rastru/actions/runs/45679448031. O job `backend` ficou verde em 7 min 01 s (build com 0 avisos; Api 617, Application 1.335, Infrastructure 367, 0 falhas; a `Api.Tests` levou 5 min 7 s; artefato `resultados-do-backend` publicado) e o job `front`, verde em 2 min 23 s (2.425 testes em 147 arquivos; `npm run build` ok). Primeira execução do `imagem`: PR rufino-bot/rastru#43, em `36b6b9b` (2026-11-26), run https://github.com/rufino-bot/rastru/actions/runs/45811611394: os três jobs ficaram verdes, `backend` em 7 min 19 s, `front` em 1 min 54 s e `imagem` em 1 min 26 s (constrói a imagem do zero num `ubuntu-24.04` limpo, cria o banco novo, garante o login `rastru_hml`, passa os seis itens da C13 e o `--derrubar` limpa tudo).
  Os três jobs são check obrigatório da `main` desde 2026-11-26 (configuração do repositório, feita pelo usuário).
- **O CD está desenhado, não construído.** A abordagem escolhida (A) é: o GitHub Actions gera a imagem, publica no
  GHCR e entra na VPS por SSH para baixar, migrar, trocar a versão e conferir; HML e PRD na mesma VPS, com Caddy na
  frente. Detalhes e partição em `docs/superpowers/specs/2026-11-25-ci-cd-design.md`. Até a HML existir, o deploy
  continua manual.

> **Emenda de 2026-11-25.** Esta seção dizia "Não existe pipeline hoje" e deixava automatizar como melhoria futura,
> "fora do MVP". O usuário **reabriu** essa decisão em 2026-11-25 (D3 da spec de CI/CD), e o trabalho passa na frente
> da Fase 6B. A seção «Hospedagem» mudou na parte 2 (2026-11-25): container e Caddy decididos.

## Pontos em aberto

Biblioteca de componentes React (ver a seção "Frontend — React + TypeScript") e estratégia de auth
(ver a seção "Autenticação e Autorização") já resolvidas. O hosting está decidido (container e Caddy, na seção "Hospedagem"); a escolha da VPS pública (ver a seção "Hospedagem") torna exigíveis três itens de
dívida de endurecimento — os dois primeiros porque é a **exposição pública** que os cria, o
terceiro porque é um **deploy real** que o cobra, com ou sem exposição —, e os três carregam o
mesmo gatilho, **obrigatório antes do primeiro deploy público**. O item 4 carrega o mesmo gatilho
sem ser endurecimento: é condição para o upload de sólido funcionar atrás de um proxy.

1. **TLS é pré-requisito de funcionamento, não melhoria** — *resolvido no desenho (2026-11-25): o Caddy termina o TLS,
   como o `deploy/Caddyfile` configura; o deploy público é da parte 3.* O cookie de refresh é gravado com
   `Secure = true` (`AuthController`), e navegador não grava cookie `Secure` em HTTP — **exceto em
   `localhost`, que os navegadores tratam como contexto seguro mesmo sem TLS** (é por isso que o
   refresh funciona em desenvolvimento; ver o comentário da entrada `/api` do proxy em
   `web/vite.config.ts`). Um domínio próprio numa VPS não tem essa isenção: sem TLS ali, o login
   funciona e o refresh **nunca**: a sessão morre em 15 minutos sem renovar, e o sintoma não aponta
   para a causa. `UseHttpsRedirection` não existe em `src/`.
2. **`ForwardedHeaders` — resolvido na aplicação (2026-11-25).** Sem ele, atrás do proxy reverso o rate limit por IP
   do `/auth/login` seria global e o log de auth gravaria o IP do proxy. A seção `ProxyReverso`, chave `Rede` (CIDR,
   `ProxyReverso__Rede`), liga o `UseForwardedHeaders` como primeiro middleware, só para essa rede, com
   `X-Forwarded-For` e `X-Forwarded-Proto` e `ForwardLimit = 1`; vazia, o middleware não é registrado, e CIDR inválido
   ou prefixo zero derruba a aplicação no startup. O `deploy/compose.yaml` fornece a sub-rede e preenche a `ProxyReverso__Rede`, e o Caddy manda os dois
   cabeçalhos. **Risco para a parte 3:** a rede do Compose não tem IPv6, então cliente IPv6 chegaria ao Caddy pelo
   gateway `172.30.0.1` e todos dividiriam uma partição só do rate limit; e o gateway, como qualquer processo do host,
   está dentro do `/24` confiável e pode forjar `X-Forwarded-For` direto na API. Opções: IPv6 na rede ou registro sem
   AAAA; confiar só no IP fixo do Caddy, como `/32`.
3. **A `SigningKey` tem validação e procedimento de deploy (2026-11-25).** O `JwtOptionsValidator` recusa, no startup
   (`.ValidateOnStart()`), o valor de placeholder commitado e exige no mínimo 32 bytes: esquecê-la **derruba a aplicação
   ao subir**, não deixa chave fraca em silêncio. O `deploy/compose.yaml` lê a `Jwt__SigningKey` de um `env_file` que o
   `subir-local.sh` gera aleatória; na VPS é o mesmo arquivo, preenchido no preparo da parte 3.
4. **O limite de corpo do proxy barra o upload de sólido** — *resolvido no desenho (2026-11-25): o proxy é o Caddy, que
   não impõe limite de corpo por padrão, e o `deploy/Caddyfile` não põe nenhum; o texto abaixo, sobre nginx e IIS,
   fica como histórico.* Se o caminho for container + nginx, o
   `client_max_body_size` do nginx tem de ser elevado acima do limite do endpoint de upload
   (`POST /componentes/{id}/solido`: 16 MiB mais uma margem de 4 KiB). O padrão do nginx é **1 MiB**,
   e acima dele o nginx responde 413 — isto é **documentação do nginx, não medição** (não houve
   nginx no ambiente da Fase 2B). Sem o ajuste, todo upload com corpo acima de 1 MiB seria recusado
   pelo proxy antes de chegar à API, e a tela não apontaria o proxy: mostraria o fallback "Envie um
   arquivo .stl de até 16 MiB" se o 413 chegar ao navegador, ou "Sem conexão com o servidor" se não
   chegar (a própria documentação do nginx avisa que navegadores não exibem esse erro corretamente)
   — nos dois casos para quem enviou, digamos, 5 MB. É configuração de deploy, não código. Sob IIS,
   o equivalente é o `maxAllowedContentLength` da filtragem de requisição, cujo padrão documentado
   (30.000.000 bytes) já comporta o limite do endpoint — também não medido.

**Os três itens de dívida de endurecimento não são desta fase.** Cada um vira item próprio na
fila, em branch separada — decisão do usuário: misturar infraestrutura na branch da Fase 2B
poluiria a review dela.

Um quinto ponto, com **gatilho próprio — início da Fase 3C** —, e por isso fora da lista numerada,
cujos quatro itens carregam todos o gatilho de pré-deploy: as **chaves VAPID** da notificação push
(`06-roadmap-mvp.md`, Fase 3C). Procedimento igual ao da `SigningKey`: as de produção são fornecidas
por variável de ambiente na VPS e nunca commitadas. Antes da Fase 3C elas não existem, e não há o
que configurar.

Um último ponto, que não é dívida nova e sim risco que muda de tamanho: o `CLAUDE.md` já registra
(seção "Defesas de autenticação em vigor", bullet "Lockout de conta") que retrancar conta não tem
limite. Era risco de alguém na rede interna da empresa; numa VPS pública o mesmo ataque fica
disponível a qualquer um na internet — a mudança de exposição está anotada junto daquele bullet.
Não é para consertar nesta fase, e não carrega o gatilho de pré-deploy de TLS, `ForwardedHeaders`
e `SigningKey`.
