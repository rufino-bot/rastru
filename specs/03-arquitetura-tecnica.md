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
- Perfis do MVP: Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador (ver
  `00-visao-geral.md`).
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
  O motivo, e o estado de transição em que isso está, estão em `05-api-endpoints.md`.
- **PWA/offline: não é necessário no MVP.** Confirmado com o negócio — pode entrar depois
  se o uso em campo mostrar necessidade real (ex.: instabilidade de wifi na fábrica). Não
  desenhar a camada de estado pensando nisso agora, para não adicionar complexidade
  desnecessária cedo.

## Banco de dados

- SQL Server on-premise, conforme `02-modelo-de-dados.sql`.
- Sugestão: ambiente de desenvolvimento local via Docker (`mcr.microsoft.com/mssql/server`)
  para os agents/desenvolvedores rodarem o schema sem depender do servidor da empresa
  durante o desenvolvimento; deploy final aponta para o servidor on-premise real.

## Hospedagem on-premise

- Backend: IIS (hosting model padrão .NET) ou container Docker + reverse proxy
  (ex.: nginx), a definir conforme o que já existe de infraestrutura na empresa.
- Frontend: build estático (Vite build) servido pelo próprio IIS/nginx, ou embutido como
  arquivos estáticos servidos pela API ASP.NET Core — mais simples para deploy on-premise
  com uma única aplicação publicada.
- **Se o SPA e a API ficarem na mesma origem** (o segundo caso acima, e o mais provável: o cookie
  de refresh é `SameSite=Strict`, que inviabiliza cross-site), **é obrigatório que os caminhos nus
  da API tenham parado de responder** antes do deploy — hoje ainda respondem, junto com `/api`.
  Senão a colisão entre rota de SPA e rota de API volta, e em produção não há dev server para
  interceptar: `GET /pedidos` como navegação de documento cai na API e responde 401.
  Ver a seção de prefixo em `05-api-endpoints.md`.

## CI/CD

- **Não existe pipeline hoje.** Deploy inicial será manual — publicar build do backend
  (IIS ou container) e do frontend direto no servidor on-premise. Automatizar (Azure
  DevOps, GitHub Actions self-hosted, etc.) fica como melhoria futura, fora do MVP.

## Pontos em aberto

Biblioteca de componentes React e estratégia de auth já resolvidas (ver acima). Resta apenas
o hosting exato (IIS vs. container), que pode ser decidido no deploy sem bloquear o
desenvolvimento.
