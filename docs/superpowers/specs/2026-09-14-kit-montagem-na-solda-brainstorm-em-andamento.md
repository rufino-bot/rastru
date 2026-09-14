# Kit — trava de montagem na Solda e aviso de Kit pronto (brainstorm EM ANDAMENTO)

> **Isto não é spec aprovada.** É o estado de um brainstorm (`superpowers:brainstorming`)
> interrompido em 2026-09-14 para o usuário responder de outro dispositivo. Quando o desenho
> for aprovado, este arquivo vira `2026-09-14-kit-montagem-na-solda-design.md` (ou é substituído
> por ele).

## Como retomar (para a sessão que abrir este arquivo)

1. Carregue a skill `superpowers:brainstorming`.
2. Leia este arquivo inteiro. O contexto da seção "O que a documentação diz hoje" **já foi
   levantado** — não refaça a busca, só confira se `main` mudou algo em `specs/01-dominio-e-regras-de-negocio.md`
   (glossário "Agrupamento", regras 9 e 16) e na §2.1/§2.2 da spec da Fase 2.
3. A conversa parou na **Pergunta 2**, abaixo, aguardando a resposta do usuário. Continue dali,
   uma pergunta por vez.
4. Esta ideia **não é da Fase 2B** e não deve entrar na branch `fase-2b-solido-3d`.

## A ideia, nas palavras do usuário

Duas partes, com pesos diferentes:

1. **Trava de montagem (mexe no fluxo — é validação de caso de uso).** Uma Peça montada por
   Itens precisa de **todos os seus Itens no Setor de Solda** para ser fabricada/soldada. Se nem
   todos chegaram, o operador **não pode marcar a Peça como pronta** nem movê-la ao próximo Setor
   do Roteiro dela — fisicamente ela não poderia ser fabricada.
2. **Aviso de Kit pronto (não mexe no fluxo, pode tocar arquitetura).** Avisar os
   movimentadores/separadores de que um Kit **pode ser montado** — todos os Itens estão prontos e
   podem ser ou já foram levados à Solda. O usuário pensou numa **mensageria simples** dirigida a
   esse perfil.
   - Extensão **adiada de propósito** pelo usuário: o mesmo aviso quando um Item fica pronto e
     pode ir ao próximo Setor do Roteiro. Só entra se a mensageria do Kit der certo.

## O que a documentação diz hoje (levantado em 2026-09-14)

- **Glossário, "Agrupamento"**: Tipo `'Kit'` = "peças que vão para a solda, juntas"; `'Avulso'` =
  "peças que não passam por solda". **"O Tipo é descritivo — não impõe roteiro."**
- **Spec `2026-07-27-alteracoes-dominio-lote-divisivel-e-agrupamento-design.md`, "Mudança #2"**:
  decidiu explicitamente que o Tipo **não impõe regra dura** (não valida "Kit obriga Solda no
  roteiro", não bloqueia expedição); Solda é um Setor comum. **Esta ideia reverte essa decisão em
  parte** — o desenho final precisa registrar a reversão nessa linha do glossário e dizer por quê.
- Schema: só `CK_Agrupamento_Tipo`. Nenhum caso de uso, endpoint ou fluxo usa o Tipo para nada.
- **Não existe Setor marcado como "Solda"**: `dbo.Setor` é só nome + ativo.
- **Não existe perfil Movimentador/Separador**: os perfis são Operador, Almoxarifado, PCP,
  Qualidade, Gestão, Administrador (regra 15).
- **Fase certa**: a trava vive no apontamento de Setor (`EstruturaSetorHistorico`), que é a
  **Fase 3**. A "separação" da **Fase 4** (`MaterialSeparacao`) é de material bruto de estoque para
  um `EstruturaItem` — não é o "separador" desta ideia, que recolhe/leva Itens prontos. O usuário
  descreveu a ideia como "no momento da separação de material"; a leitura acima foi apresentada a
  ele e ainda **não foi confirmada nem contestada** explicitamente.
- **Quantidade é absoluta e sem invariante de razão** (spec da Fase 2, §2.1 e §2.2): Peça de 10 com
  "4 suportes por unidade" guarda o filho como 40; a razão 4 não é guardada, e um filho de 45 para
  uma Peça de 10 é legítimo (sobra de refugo).
- **Lote divisível** (regra 9) e **expedição parcial** (regra 16).
- Horizonte do sistema: vida esperada curta (1-2 meses) — "não escala" não decide nada aqui.

## Decisões já tomadas

- **D1 — Unidade da trava: os Itens da própria Peça** (resposta à Pergunta 1). Cada Peça de um
  Agrupamento Kit espera os **próprios** filhos na Solda; Peças do mesmo Kit são independentes
  entre si. O Tipo do Agrupamento decide **se** a trava vale.
  - Descartadas: (B) o Agrupamento Kit inteiro trava junto; (C) toda Peça com filhos trava,
    independente do Tipo — descartada porque um Avulso pode ter filhos montados de outro jeito
    (ex.: parafusados), e a trava seria falsa ali.

## Pergunta 2 — ABERTA, aguardando o usuário

**A Solda de verdade monta parte da quantidade de uma Peça?**

Exemplo: Peça de 10, filho "suporte" de 40. Chegaram 24 suportes na Solda.

- **A) Não, só com o lote inteiro.** Nenhuma das 10 é soldada até os 40 estarem lá. No sistema: a
  Peça só avança quando **toda** a quantidade de cada filho está na Solda. Simples, sem razão.
  Custo: um suporte atrasado ou perdido trava as 10, e isso briga com a expedição parcial (regra 16).
- **B) Sim, monta o que dá.** Com 24 suportes, soldam-se 6. Exige a **razão por unidade** —
  **reabre a §2.1 da Fase 2**, que descartou guardá-la (provável coluna nova em `EstruturaItem`).
- **C) Sim, mas o operador informa quantas montou.** O sistema só confere que **cada filho tem
  alguma quantidade** na Solda. Sem razão, mas a trava fica fraca: com 1 suporte presente, aceitaria
  soldar as 10.

Nenhuma recomendação foi dada ainda, de propósito: depende de como a fábrica opera, não de
técnica. Se for **A**, o desenho é pequeno; se for **B**, a ideia deixa de ser pequena.

## Perguntas ainda não feitas (fila, uma por vez, nesta ordem provável)

1. **Como o sistema sabe qual Setor é a Solda?** Opções a apresentar: flag no `Setor` (ex.:
   "setor de montagem", pode haver mais de um); nome fixo (frágil); ou "o Setor do Roteiro da Peça
   em que os filhos terminam o Roteiro deles" (sem schema novo, mas implícito).
2. **O que acontece com a quantidade do Item depois de soldado?** Ele "some" dentro da Peça. A
   conservação de quantidade (regra 9) fala da Peça; hoje não há bucket "consumido na montagem"
   para Item. Pode já estar respondido quando a Fase 3 for desenhada — confirmar antes de inventar.
3. **Item perdido** (regra 17): com a opção A da Pergunta 2, uma perda de suporte trava a Peça para
   sempre? Conta o perdido como "resolvido" e libera menos Peças? Liga com o ponto em aberto
   "Descontinuar uma Peça trava o fechamento do Pedido", do mesmo arquivo de domínio.
4. **Sub-Itens**: a trava vale só para os filhos diretos da Peça, ou um Item com filhos (submontagem)
   também espera os dele numa Solda anterior?
5. **Quem recebe o aviso**: perfil novo (Movimentador) — que exige mexer em `permissoes.ts` e nos
   `[Authorize(Roles)]`, ver a dívida de permissão hardcoded —, ou um perfil existente
   (Almoxarifado? Operador?).
6. **Mecanismo do aviso** (arquitetura): sem PWA no MVP, então sem push do sistema operacional.
   Candidatos a comparar: tela/contador "Kits prontos para coleta" com polling; SignalR;
   tabela de notificação lida/não lida. Considerar o horizonte curto do sistema.
7. **Momento do aviso**: quando o **último** Item fica pronto no Setor anterior (pode ser levado) ou
   quando o último Item **chega** na Solda (pode ser montado)? O usuário disse "podem ser/foram
   alocados" — as duas leituras cabem.
8. **Em que fase entra**: a trava depende da Fase 3; decidir se vira parte da Fase 3 ou uma fase
   própria logo depois dela. `06-roadmap-mvp.md` precisa refletir.
