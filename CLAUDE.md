# CLAUDE.md

Contexto para o Claude Code trabalhar neste repositório. Leia isto antes de qualquer
tarefa; para detalhes de domínio, arquitetura e roadmap, vá para a pasta `specs/`.

## Sobre o projeto

Sistema de rastreamento de peças dentro da fábrica: do cadastro do Pedido até a entrega
para a Expedição, passando pela estrutura recursiva de Peças/Itens, pela passagem por
Setores de produção, pela separação de Materiais e pelo Relatório Dimensional (com
possibilidade de abrir Retrabalho em caso de reprovação).

A fonte da verdade do domínio e das decisões já tomadas é a pasta `specs/`. Não
re-decida algo que já está resolvido lá sem perguntar antes.

## Stack

- **Backend**: .NET (C#), ASP.NET Core Web API
- **Frontend**: React + TypeScript (Vite), responsivo/mobile-first (uso em Android via navegador, sem PWA no MVP)
- **Banco**: SQL Server, on-premise
- **Auth**: login próprio (usuário/senha) + JWT, com perfis (Operador, Almoxarifado, PCP, Qualidade, Gestão, Administrador)
- **CI/CD**: nenhum ainda — deploy manual no MVP

## Mapa da pasta `specs/`

Leia o arquivo relevante para a tarefa em questão antes de codar — não é preciso carregar
todos de uma vez.

| Arquivo | Quando ler |
|---|---|
| `00-visao-geral.md` | Objetivo do sistema, escopo, perfis de usuário |
| `01-dominio-e-regras-de-negocio.md` | Antes de tocar em qualquer regra de negócio — glossário e regras numeradas |
| `02-modelo-de-dados.sql` | Fonte de verdade do schema do banco |
| `03-arquitetura-tecnica.md` | Estrutura de camadas, EF Core, autenticação, hospedagem |
| `04-fluxos-de-usuario.md` | Antes de implementar uma tela/fluxo — passo a passo por perfil |
| `05-api-endpoints.md` | Antes de criar/alterar um endpoint |
| `06-roadmap-mvp.md` | Para saber em que fase estamos e o que vem a seguir |

## Ordem de implementação

Seguir as fases de `06-roadmap-mvp.md` em sequência (Fase 0 → 6). Não implementar
funcionalidade de uma fase mais avançada antes da anterior estar concluída, mesmo que
pareça simples — a ordem existe para manter escopo fechado por etapa.

## Banco de dados — regra importante

`specs/02-modelo-de-dados.sql` é a **fonte de verdade** do schema. O mapeamento do EF
Core é *Database First*: o schema nasce no `.sql`, não em migrations do EF. Se uma
tarefa exigir mudança de schema:

1. Alterar `02-modelo-de-dados.sql` primeiro.
2. Atualizar o mapeamento EF Core a partir do novo schema.
3. Refletir a mudança em `01-dominio-e-regras-de-negocio.md` se for regra de negócio.

Nunca deixe o EF Core criar/alterar tabelas por conta própria via `Add-Migration` a
partir do zero.

## Convenções de nomenclatura

- **Nomes de domínio em português**, espelhando exatamente o DDL: `Pedido`, `Agrupamento`,
  `EstruturaItem`, `Componente`, `Material`, `Setor`, `Usuario`, `Perfil`,
  `RelatorioDimensional`, `RelatorioDimensionalAvaliacao`, `Expedicao`, `Perda`, etc. Não
  traduza entidades de negócio para inglês — isso cria divergência entre código, banco e
  as specs.
- **Nomes técnicos/padrões de projeto em inglês**: `Repository`, `UseCase`, `DTO`,
  `Controller` (ex.: `PedidoRepository`, `AbrirRetrabalhoUseCase`).
- Siga a estrutura de camadas descrita em `03-arquitetura-tecnica.md`
  (`Domain` / `Application` / `Infrastructure` / `Api`).

## Invariantes de negócio que não podem ser violadas

(resumo — ver `01-dominio-e-regras-de-negocio.md` para a lista completa)

- Um `EstruturaItem` (lote) é **divisível por quantidades livres**: pode ter quantidades em
  Setores diferentes ao mesmo tempo. Não há identidade de sub-lote (sem serial). O
  invariante a preservar é **conservação de quantidade**: soma em Setores + expedido
  (`Expedicao`) + perdido (`Perda`) = quantidade total da Peça (validado na aplicação).
- `EstruturaItem` é recursivo: nó sem pai = **Peça**, nó com pai = **Item**. Não crie
  tabelas separadas para Peça e Item.
- Reprovação no Relatório Dimensional **não** gera Retrabalho automaticamente — é uma
  ação separada e opcional do usuário (perfil Qualidade), com `MotivoRetrabalho`
  obrigatório quando aplicada. O mesmo vale para **perda**: registrar a perda não abre
  Retrabalho sozinho.
- Um Pedido só é concluído quando o **último** Agrupamento dele é concluído (e um Agrupamento,
  quando todas as suas Peças concluem — toda a quantidade expedida ou perdida).
- Rastreamento é por **lote agregado**, nunca por unidade física individual (serial).
- **Falha de autenticação é sempre genérica.** Login responde `"Usuário ou senha inválidos."` e
  refresh responde `"Refresh token inválido ou expirado."` — em **todos** os caminhos de falha,
  inclusive conta trancada (mesmo com a senha certa) e reuso de refresh token detectado. Variar
  corpo, status ou tipo de erro por condição vira oráculo de enumeração.
- **O BCrypt roda sempre no login**, inclusive para usuário inexistente, inativo ou trancado
  (`IPasswordHasher.HashFicticio`). Nenhum `return` antecipado antes da verificação de senha.

## O que evitar (decisões já descartadas — não reabrir sem justificativa nova)

- Windows Authentication (decidido: login próprio + JWT)
- PWA/offline no MVP (decidido: não necessário agora)
- Rastreamento por serial individual (decidido: lote agregado)
- Criar Peça e Item como tabelas separadas (decidido: tabela recursiva única)
- Roteiro de setores fixo por tipo de peça (decidido: pode variar por pedido/agrupamento)

## Comandos

Backend (solution `Rastreamento.slnx`, na raiz):

```bash
docker compose up -d          # PRÉ-REQUISITO dos testes de integração (ver abaixo)
dotnet build Rastreamento.slnx -warnaserror    # o build tem que ficar em 0 warnings
dotnet test  Rastreamento.slnx                 # suíte inteira
```

Um projeto de teste por vez, enquanto se itera:

```bash
dotnet test tests/Rastreamento.Domain.Tests           # (vazio por enquanto)
dotnet test tests/Rastreamento.Application.Tests      # casos de uso, com fakes — não precisa de banco
dotnet test tests/Rastreamento.Infrastructure.Tests   # hashers + mapeamento EF (parte precisa de banco)
dotnet test tests/Rastreamento.Api.Tests              # ponta a ponta (a maior parte precisa de banco)
```

Frontend: ainda não criado (Fase 1 em diante).

### Pré-requisito externo dos testes

Parte da suíte roda contra o **SQL Server real**, não contra banco em memória — é o que
prova o mapeamento EF, os lifetimes do DI, a atomicidade da rotação de refresh token, a
queima da família de tokens no reuso e o lockout de conta ponta a ponta.
Hoje são **41 dos 123 testes** (8 em `Infrastructure.Tests`, 33 em `Api.Tests`). Sem o
banco no ar eles falham com erro de conexão, não com mensagem útil.

```bash
docker compose up -d
# aplicar, uma vez, no banco `Rastreamento` de localhost:1433 (sa / Your_strong_Pass123):
#   specs/02-modelo-de-dados.sql   (schema — fonte de verdade)
#   db/seed.sql                    (perfis + usuário admin / Admin@123)
```

Num banco que já existia antes do hardening de auth, as colunas de lockout entram por `ALTER`
(o `.sql` é script de criação). É idempotente — e o `MSYS_NO_PATHCONV=1` é o que impede o Git Bash
de traduzir o caminho do `sqlcmd` dentro do container:

```bash
MSYS_NO_PATHCONV=1 docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Your_strong_Pass123' -C -I -d Rastreamento \
  -Q "IF COL_LENGTH('dbo.Usuario','FalhasConsecutivas') IS NULL ALTER TABLE dbo.Usuario ADD FalhasConsecutivas INT NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0), BloqueadoAte DATETIME2 NULL;"
```

O schema **não** é criado pelo EF (nada de `Add-Migration`/`EnsureCreated`): é Database
First, o `.sql` é a fonte de verdade.

### Defesas de autenticação em vigor

- **Trabalho constante + 401 genérico** no login e no refresh: nos três caminhos de falha do login
  (usuário inexistente, inativo/trancado, senha errada) o BCrypt roda sempre, contra um hash de
  mesmo custo, e o corpo da resposta é idêntico. Residual aceito, e **não corrigível em
  princípio**: a escrita no banco não é uniforme — só senha errada numa conta existente, ativa e
  destrancada faz `UPDATE` (`Senha_errada_incrementa_o_contador_e_persiste` afirma `Saves == 1`;
  `Usuario_inexistente_nao_escreve_nada` e `Tentativa_em_conta_trancada_nao_estende_a_trava`
  afirmam `Saves == 0`). A banda é baixa (~1–3 ms de `UPDATE` contra ~100–150 ms de BCrypt fator
  11) e não tem correção possível: não há como escrever uma linha de contador para um usuário que
  não tem linha.
- **Reuso de refresh token detectado:** reapresentar um token já rotacionado revoga **todos** os
  refresh tokens ativos daquele usuário e responde o mesmo 401 genérico. Limite inerente: só
  detecta quando o token **antigo** reaparece — se o atacante roubar o token atual e o legítimo
  nunca replayar o anterior, a defesa é a expiração natural do refresh.
- **Lockout de conta:** `Lockout:MaxFalhas` (5) falhas consecutivas trancam por
  `Lockout:DuracaoMinutos` (15). Cada trava expira sozinha, mas **retrancar não tem limite**: quem
  sabe o nome de usuário (inclusive `admin`) manda 5 senhas erradas a cada 15 min — 20
  requisições/hora contra um orçamento de rate limit de 600/hora (10 por minuto, ~3% dele) — e
  segura a conta trancada indefinidamente, de um único IP. O rate limit não cobre esse padrão
  porque a janela dele é curta (segundos), não de ciclos de 15 minutos; hoje não existe caminho de
  desbloqueio administrativo. Isso é inerente a lockout por contador (a própria OWASP registra o
  trade-off) — o desenho está certo, o que estava documentado errado aqui era o limite do dano.
  Concorrência: com N tentativas simultâneas o contador pode sub-contar em até N−1 (proporcional à
  concorrência, não um simples off-by-one), e existe a race reversa — um login concorrente
  bem-sucedido que leu a linha antes de uma falha travá-la grava `BloqueadoAte = null` depois,
  apagando uma trava recém-criada; benigno, porque quem venceu a race provou a senha certa. O que
  **não** acontece: uma trava que nunca libera. O timestamp da trava é capturado antes do BCrypt
  rodar, então uma requisição atrasada só consegue estender a trava pela duração dela mesma, nunca
  travar por mais tempo que isso.
- **Rate limit por IP no `/auth/login`:** `RateLimit:PermitLimit` (10) por
  `RateLimit:WindowSeconds` (60), janela fixa, 429 com `Retry-After`. O `/auth/refresh` fica de
  fora de propósito — ver `specs/05-api-endpoints.md`, que registra a isenção e a consequência dela
  (achado ainda em aberto, pendente de decisão). Em `appsettings.Development.json` o limite é
  folgado — no `TestServer` o IP é nulo e toda a suíte cairia numa partição só.
- **Logging de auth via `ILogger`** (não há tabela de auditoria persistente): login ok/falha e
  refresh ok/falha com IP no `AuthController`; trava de conta e reuso de refresh nos casos de uso;
  429 no `OnRejected`. **Nunca** se loga senha, refresh token (plano ou hash) nem access token.

### Trade-offs conhecidos de autenticação

**Logout não invalida o access token.** O logout revoga o refresh token no banco, mas o
access token é JWT stateless e o `/me` responde só a partir das claims, sem ida ao banco.
Ou seja: depois do logout — ou de desativar um usuário, ou de a família de tokens ser queimada por
reuso — a sessão ainda funciona até o access token expirar (`Jwt:AccessTokenMinutes`, hoje 15 min).
É o comportamento padrão de JWT stateless e está aceito no MVP; se um dia precisar ser imediato, a
saída é uma denylist de tokens ou validação por requisição, não um remendo no logout.

**Rate limit atrás de proxy reverso.** A partição usa `RemoteIpAddress`. Se entrar um proxy na
frente da API, configurar `ForwardedHeaders` — senão todos os clientes compartilham o IP do proxy e
o limite vira global por acidente. Flag de deploy, ainda não necessária (deploy manual, sem proxy).

**Ainda em aberto (deferido de propósito):** tabela de auditoria persistente; limpeza de linhas
`RefreshToken` expiradas; `SigningKey` como segredo de ambiente e `UseHttpsRedirection`; mensagem
dedicada de 429 no front (hoje cai no erro genérico de auth — só dispara sob abuso).
