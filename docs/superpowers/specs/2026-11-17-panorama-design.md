# Panorama — desenho

Data: 2026-11-17. Branch: `claude/cool-cray-ijm0ib`, a partir da `main` em `7d41e09` (pós-merge da Fase 5C,
rufino-bot/rastru#38). O desenho foi conversado com o usuário em chat numa sessão de nuvem entre 2026-11-16 e 2026-11-17,
parte por texto, parte por uma captura da planilha que a Gestão usa hoje e parte por dois áudios que explicam as cores da
planilha, transcritos na sessão com o Whisper (`medium`, pelo `faster-whisper`). Os mockups da conversa são uma página
privada do usuário (`https://claude.ai/artifact/5C6xzuS4nzimnrZQ5HPqif`, versão 3). As decisões do usuário são D1 a D14
(seção 2.1). As chamadas do desenho, que o usuário confere ao ler, são C1 a C20 (seção 2.2). O builder check (um revisor
lendo a spec como quem vai planejar, conferindo as premissas contra o código) achou quatro premissas falsas sobre o código
(a ordem da `CalculadoraDeExecucao.Saldos`, o atraso calculado no cliente, o critério do `GET /expedicao` e os dois
sentidos de "em produção") e um corpo de 403 que o projeto não manda; foram corrigidos em C2, C8, C18 e nas seções 5 e 6. Duas perguntas voltaram ao usuário e viraram D13 e D14. O documento escrito foi **aprovado pelo
usuário em 2026-11-17**.

**Ponto de partida.** A Fase 5 está concluída. O próximo item do roadmap seria a 3C (push) ou a Fase 6 (KPIs), sem ordem
decidida entre as duas. Este trabalho entra antes de ambas, logo depois da **miniatura isométrica do sólido**, que
tem spec própria (ainda não escrita) e **executa antes deste** (D11), porque a coluna "Desenho" do Panorama depende dela.
Ver a seção 9 para a posição no roadmap.

## 1. O que se quer, e o que conta como pronto

**O problema.** A Gestão acompanha a fábrica por uma planilha alimentada à mão: uma linha por Peça de Pedido, com OS,
código, descrição, saldo, prazo, desenho, e uma cor de fundo que diz onde a Peça está (sem cor: não começou; lilás:
solda; azul: acabamento; verde: chegou à Qualidade/Expedição; amarelo: embalada; vermelho: atenção, por alteração de
projeto ou atraso grande). A letra vermelha marca a Peça que exige relatório dimensional. As colunas "Observações" e
"Localização" são o histórico escrito à mão ("Solda 28/10", "15 pçs embaladas 31/10, 2 pçs repintura").

A planilha "só funciona quando é atualizada": é olhada principalmente de manhã e consultada ao longo do dia, e envelhece
conforme o dia passa.

**O que se quer.** Uma tela do Rastru, o **Panorama**, que mostra a mesma coisa sem que ninguém a alimente: ela lê o que
o chão de fábrica já registra. A única escrita nova é a marca **"Embalado"**, que hoje só existe na planilha.

**Pronto é:**

- a Gestão abre `/panorama` e vê, para cada Peça de Pedido em aberto, onde estão as unidades agora, na ordem do Roteiro,
  com prazo, atraso, pausa, aferição e "Embalado", sem que ninguém tenha digitado nada além do apontamento de sempre;
- a tela se atualiza sozinha enquanto aberta;
- a Qualidade marca "Embalado" na própria tela, só quando a Peça inteira saiu da produção, e a marca some sozinha se uma
  unidade voltar para a produção;
- os filtros (incluindo o intervalo de prazo) e a ordenação por cabeçalho funcionam no PC e no celular.

**Sucesso**, nas palavras da conversa: a Gestão deixa de manter a planilha.

### 1.1 Não objetivos

- **O que acontece fora da fábrica.** A pintura terceirizada (as colunas "Lucas" e "Zincagem" da planilha) continua fora
  do escopo, como `specs/00-visao-geral.md` já registra. Se fizer falta, o caminho é cadastrar um Setor comum ("Pintura
  - Terceiros") que a Qualidade movimenta, sem código novo (D1).
- **A cor de pintura** (coluna "Cor" da planilha: Amarelo, Cinza, Zincagem…): é só referência e fica fora (D9).
- **As colunas Solda/Usinagem/Dobra (Sim/Não), "Observações" e "Localização"**: o Roteiro da Peça e a distribuição as
  substituem (C5).
- **KPI e história.** O Panorama é a foto de agora. Tempo médio por Setor e tempo por Pedido continuam na Fase 6.
- **Sub-Itens.** O Panorama mostra Peças (nó sem pai), como a planilha. Item não tem linha própria (C3).

### 1.2 Anti-objetivos (falharia mesmo funcionando)

- Pedir digitação nova só para alimentar a tela.
- Mostrar uma marca que já não vale: o Panorama existe para não mentir, e por isso o "Embalado" some sozinho (D5).
- Codificar Setor por cor: os Setores são cadastráveis e a paleta cresceria sem limite, além de contrariar a regra de
  cor do `CLAUDE.md` (cor de estado nunca decora). Setor aparece por **nome** (C6).

## 2. Decisões

### 2.1 Do usuário

| # | Decisão |
|---|---|
| D1 | Só o que acontece **dentro da fábrica**. A pintura de fora, se necessária, vira um Setor comum ("Pintura - Terceiros"), movimentado pela Qualidade. |
| D2 | Peça dividida mostra **tudo, na ordem do Roteiro**, numa linha só: `2 a iniciar · 3 Solda · 10 Acabamento` (opção A de três: tudo; só a parte mais atrasada; uma linha por lugar). |
| D3 | "Embalado" é um **estado novo**, marcado **no próprio Panorama**, por uma caixa na coluna "Embalado". Persistência pela forma mais simples. |
| D4 | A caixa só pode ser marcada quando a Peça **inteira** saiu da produção: toda unidade está no local de expedição ou foi consumida em baixa, e o total fecha com a quantidade da Peça no Pedido. |
| D5 | Se, depois de marcada, a condição deixa de valer, a marca **some sozinha** (em vez de continuar com aviso). |
| D6 | **Miniatura** do sólido: trabalho **separado**, com spec própria, que executa **antes** do Panorama. |
| D7 | Lista **desagrupada** (uma linha por Peça, a OS repetida), não agrupada por Pedido: o agrupamento da planilha é organização interna deles, que o Rastru não absorve. |
| D8 | **Filtro por data de entrega**, de preferência como um componente de intervalo de datas. |
| D9 | A **Cor** de pintura fica de fora por enquanto: é referência. Ela servia para ver onde a Peça estava sem ler o Setor perdido nas observações, o que o Rastru resolve ao filtrar e separar a informação. |
| D10 | **Cabeçalhos de coluna ordenam**: 1 clique crescente, 2 decrescente, 3 volta à ordem original da tela. Só as colunas em que ordenar faz sentido. |
| D11 | Persistência do "Embalado": **tabela de registro só de inclusão, com a validade calculada na leitura** (forma 1 de três; seção 8). |
| D12 | A tabela do "Embalado" pode crescer por ação de má-fé (marcar e desmarcar sem parar). Fica **pendente para a manutenção do banco** uma rotina que limpa a tabela a partir de uma data, **sem perder o último registro de cada Peça** (o último diz quando ela deixou de ser mexida e, muitas vezes, quando foi expedida). Seção 3.5. |
| D13 | A Peça montada a partir de Itens mostra **por onde andam os Itens, só por nome de Setor** (`5 a iniciar · Itens em: Corte, Dobra`), e o filtro por Setor acha a Peça quando ela ou algum Item dela está naquele Setor (opção b de três: deixar como está; Setores dos Itens sem número; a distribuição de cada Item com número). Resposta ao builder check (C3). |
| D14 | **Posição no roadmap:** miniatura → Panorama, **antes da 3C**. A posição em relação à Fase 6 não foi dita (e a ordem entre a 3C e a Fase 6 continua não decidida). A renomeação do push (sair do nome "3C" e virar fase própria, ideia do usuário de 2026-11-16) continua pendente e não entra aqui. |

### 2.2 Do desenho (o usuário confere)

| # | Chamada |
|---|---|
| C1 | Nome **"Panorama"**, primeira entrada da barra do `AppShell`, como link direto (não grupo), antes de Produção. Rota `/panorama`. |
| C2 | **Quais Peças aparecem:** toda Peça (nó sem pai) de Pedido **não encerrado** (nem `Concluido` nem `Cancelado`), inclusive Pedido `Aberto` que ninguém começou e Pedido de Retrabalho, enquanto ela tiver alguma unidade que não foi expedida nem baixada. Peça toda expedida ou baixada sai da lista. O critério é **mais largo** que o do `GET /expedicao` (que só lista Peça com unidade no local de expedição ou em conserto), então a consulta da expedição não serve como está; a fonte que serve é a de `ListarNosEmProducaoAsync`, filtrada aos nós sem pai, que já inclui Pedido `Aberto`, exclui `Concluido` e `Cancelado` e traz a pausa aberta com o `Motivo`. |
| C3 | Sub-Itens não têm linha. A Peça com filhos mostra a própria distribuição e, ao lado, **"Itens em: {Setores}"** (D13): os Setores, sem repetir e em ordem alfabética, em que algum descendente da Peça (qualquer nível) tem quantidade em `NoSetor`, `AguardandoColeta`, `AguardandoMontagem` ou numa posição de conserto. Sem número, porque somar unidades de Itens diferentes não quer dizer nada. Item "a iniciar" não entra (não está em Setor nenhum); sem descendente em Setor, o trecho não aparece. O "Embalado" olha só a própria Peça. |
| C4 | **Colunas** (PC): Desenho · OS · Código · Descrição · Qtd · Prazo · Onde está · Aferição · Embalado. "Qtd" é a quantidade da Peça no Pedido (`EstruturaItem.Quantidade`). |
| C5 | Solda/Usinagem/Dobra, Observações e Localização saem: o Roteiro e a distribuição dizem o mesmo sem digitação. |
| C6 | Cor só para estado, nas regras de hoje: atraso `atraso` (roxo), pausa `atencao` (âmbar, com o motivo), aferição como na expedição ("Conforme" `positivo`, "Não conforme" `negativo`, o resto neutro). Setor e posição em pílula/segmento **neutro**. |
| C7 | O "vermelho de atenção" da planilha vira duas coisas que o Rastru já tem: o **atraso** do Pedido e a **pausa** do Pedido, mostrada com o motivo ("Pausado: alteração de projeto"). A pausa é do Pedido inteiro, não de uma Peça (seção 7). |
| C8 | **Distribuição** ("Onde está"), na **ordem do Roteiro**, montada pelo servidor (a `CalculadoraDeExecucao.Saldos` ordena primeiro pelo tipo de posição, depois pelo passo, e por isso não serve como está): primeiro `AIniciar`; depois **um segmento por passo** do Roteiro, na ordem do passo, com o Setor e a soma de `NoSetor` e `AguardandoColeta` daquele passo ("3 Solda"), e o aguardando coleta como detalhe dentro do segmento ("3 Solda, 1 aguardando coleta"); um Setor que aparece duas vezes no Roteiro (retorno ao setor) dá dois segmentos; depois o **conserto**, um segmento por Setor de conserto ("1 em conserto na Solda", as três posições do conserto somadas); depois o **local de expedição**, as cinco posições somadas num segmento "Expedição"; por último, **expedidas** e **baixadas**, num traço diferente (já saíram da fábrica, mas contam para o "Embalado"). Segmento com zero não aparece. |
| C9 | **Ordem original da tela:** prazo de entrega (mais próximo primeiro), depois número do Pedido, depois código da Peça, e o `Id` do nó por último. Aplicada pelo servidor; a tela só reordena quando um cabeçalho manda. |
| C10 | **Colunas que ordenam:** OS, Código, Descrição, Qtd e Prazo. **Não ordenam:** Desenho; Onde está (a Peça está em vários lugares ao mesmo tempo, não há um valor só para comparar); Aferição e Embalado (já têm filtro). Uma coluna ordena por vez; clicar noutra começa o ciclo dela. Empate numa ordenação escolhida cai na ordem original. |
| C11 | **Filtros** (`FiltroDeDemanda`, no cliente): Pedido, Setor (Peças com alguma unidade naquele Setor, em trabalho, aguardando coleta ou em conserto, **ou com algum Item nele**, pelo "Itens em" de C3), Situação (Atrasado, Pausado, No prazo), Relatório (Exige relatório, Sem relatório), Embalado (Embalado, Pode embalar, Em produção). Mais uma busca por texto (OS, código, descrição) e o **intervalo de prazo**. |
| C12 | **Intervalo de datas**: primitiva nova, dois campos de data nativos ("de" e "até") num componente só, com "limpar"; qualquer lado pode ficar vazio; os limites são inclusivos. Nativo porque no Android cada campo abre o calendário do próprio celular. |
| C13 | Filtros, busca, intervalo e ordenação vão para a **URL** (como na `PedidosPage`), com `replace`, para F5 e link mantendo a vista. A ordem original não é escrita na URL. |
| C14 | **Celular:** uma Peça por cartão, os mesmos filtros, e a ordenação por um `SeletorDeOrdem` ("Ordem da tela", "Prazo (mais próximo)", "Prazo (mais distante)", "OS (crescente/decrescente)", "Código (A→Z/Z→A)", "Descrição (A→Z/Z→A)", "Qtd (maior/menor)"). A tabela a partir de `lg`; o cartão abaixo. A tela usa `<Pagina largura="ampla">`. |
| C15 | **Recarga automática** pelo `useCargaPeriodica`, no `INTERVALO_DA_EXECUCAO_MS` (hoje 30 s), com "Atualizado às HH:MM" no topo. A recarga mantém filtros, ordenação e rolagem. |
| C16 | **Leitura é de todos** os perfis (regra "gating vai na ação, não no link"). A regra da Fase 6, "só a Gestão vê", **não** se aplica: o Panorama não é KPI. |
| C17 | **Quem marca e desmarca o "Embalado":** Qualidade, PCP e Administrador (chave nova `embalagem` em `permissoes.ts`, espelhando o `[Authorize(Roles)]`). Desmarcar não exige ser o autor. A Gestão vê o estado, sem a caixa. |
| C18 | Conta do "Embalado" (D4 em números): **pode marcar** quando a Peça tem **zero** em produção (`AIniciar`, `NoSetor`, `AguardandoColeta`, `AguardandoMontagem`, `AguardandoConserto`, `EmConserto`, `ConsertoPronto`) e **ao menos uma** unidade no local de expedição. Equivale a `local de expedição + Expedido + Perdido = Quantidade`, e por isso a Peça com parte já expedida pode ser marcada (proposto na conversa, sem objeção do usuário; D4 não citava as expedidas). As sete posições são um **conjunto próprio** do Panorama: **não** é o `CalculadoraDeExecucao.EmProducao`, que segue o sentido da regra 9 e conta o local de expedição como em produção (com ele, nenhuma Peça seria marcável). Toda posição do local de expedição conta (inclusive `NaoConforme` e `AguardandoLiberacao`: embalar é físico, a aferição é outro assunto e aparece na linha). |
| C19 | **"Some sozinho"** é calculado, nunca gravado (seção 3.2): nenhum caso de uso existente (conserto, baixa, remessa, estornos, reposição) ganha código para desmarcar. |
| C20 | O código da Peça na linha é link para a página do Agrupamento da Peça, onde estão a árvore, as posições, o histórico e (pelo Componente) o visualizador 3D. |

## 3. Regras de domínio

Entra a **regra 40** em `specs/01-dominio-e-regras-de-negocio.md`, com o conteúdo desta seção.

### 3.1 O registro do "Embalado"

Tabela nova **`dbo.Embalagem`**, um registro **só de inclusão** (no molde de `dbo.MaterialSeparacao`): cada linha é um
**marcar** ou um **desmarcar** de uma Peça, com quem e quando.

| Coluna | Tipo | Observação |
|---|---|---|
| `Id` | `INT IDENTITY` | PK |
| `EstruturaItemId` | `INT NOT NULL` | FK para `dbo.EstruturaItem`. Só Peça (nó sem pai): a aplicação recusa Item. |
| `Tipo` | `NVARCHAR(10) NOT NULL` | `CK_Embalagem_Tipo`: `'Marcar'` ou `'Desmarcar'`. |
| `DataHora` | `DATETIME2 NOT NULL` | UTC, preenchida pelo caso de uso (o `DEFAULT (SYSUTCDATETIME())` é rede, não contrato, como no livro). |
| `UsuarioId` | `INT NOT NULL` | FK para `dbo.Usuario`. |

Índice `IX_Embalagem_EstruturaItem` em `(EstruturaItemId, Id DESC)`: toda leitura quer a **última** linha de cada Peça.

Nenhum código da aplicação edita nem apaga uma linha. A única exceção é a rotina de limpeza **pendente** da seção 3.5,
que é manutenção de banco e preserva a última linha de cada Peça.

### 3.2 Quando o "Embalado" vale

O "Embalado" de uma Peça **vale** quando as três coisas são verdade, calculadas na leitura:

1. a **última** linha de `dbo.Embalagem` da Peça é `Marcar`;
2. a condição de C18 vale **agora** (zero em produção e ao menos uma unidade no local de expedição); e
3. **nenhuma** linha do livro de movimentações (`dbo.Movimentacao`) da Peça com `DataHora` **posterior** à da marca tem
   **destino** numa posição de produção (as sete de C18).

O item 3 é o que faz a marca "sumir sozinha" de verdade: sem ele, a unidade que vai ao conserto e volta à expedição faria
a marca **ressuscitar** sem ninguém ter embalado de novo. Com ele, qualquer volta à produção depois da marca a invalida
para sempre; para valer de novo, alguém marca outra vez. O item 3 também fecha o caso da Peça cuja quantidade aumenta
depois da marca: a unidade nova começa em `AIniciar` sem linha no livro (item 2 já falha), e quando ela é iniciada a
linha `Inicio` tem destino `NoSetor` (item 3 falha para sempre).

Linhas que **não** invalidam, porque o destino não é produção: aferir, contar, liberar e recusar (posições do local de
expedição), expedir (`Expedido`), dar baixa (`Perdido`), e os estornos cujo destino também fica fora da produção (o
estorno de uma remessa devolve ao local de expedição). Os estornos que devolvem à produção (estorno de baixa feita num
Setor, estorno de uma entrega ao local de expedição) invalidam, e é o certo: a unidade voltou.

**Propriedade que a limpeza da seção 3.5 depende:** a validade lê **só a última linha** de `dbo.Embalagem` de cada Peça.
Apagar as anteriores não muda nenhum "Embalado" calculado.

### 3.3 Marcar e desmarcar

- **Marcar** grava uma linha `Marcar`. Recusa:
  - 404 se o nó não existe; 400 `NaoEhPeca` se é Item (o mesmo código da expedição);
  - 409 `PedidoFechado` se o Pedido está `Concluido` ou `Cancelado`;
  - 409 `ProducaoNaoTerminou` se a condição de C18 não vale, com `faltam` (a quantidade ainda em produção) no corpo,
    para a frase "faltam N em produção";
  - 409 `JaEmbalado` se o "Embalado" já **vale** (seção 3.2). Se a última linha é `Marcar` mas ela deixou de valer, marcar
    de novo é permitido e grava outra `Marcar`.
- **Desmarcar** grava uma linha `Desmarcar`. Recusa 404 e 400 como acima, e 409 `NaoEmbalado` se o "Embalado" não vale
  agora (inclusive quando a marca sumiu sozinha: não há o que desmarcar).
- A pausa do Pedido **não** recusa marcar nem desmarcar: a pausa recusa só o início (regra 31), e embalar registra o que
  aconteceu no chão.
- As duas escritas correm na transação da execução (`EmTransacaoAsync`, SERIALIZABLE, com o retry de 1205), porque a
  condição lê o livro: nenhuma movimentação pode entrar entre a conferência e a gravação.
- As recusas `JaEmbalado` e `NaoEmbalado` também limitam o crescimento da tabela: cada linha nova alterna com a anterior
  (seção 3.5).

### 3.4 O que o "Embalado" não é

- Não é posição do livro e não mexe em quantidade: a conservação (regra 9) não muda.
- Não trava nem condiciona a remessa: expedir uma Peça não embalada continua possível (no espírito da regra 37, o
  Rastru registra e não trava).
- Não é por quantidade: é a Peça inteira, de uma vez (D4).

### 3.5 Pendente: a limpeza da tabela (D12) — fica para a manutenção do banco

**Não se implementa neste trabalho.** Registro do que o usuário pediu, para a manutenção do banco (seção 8 de
`docs/superpowers/specs/2026-10-02-hml-cicd-e-manutencao-do-banco-design.md`, onde ganha uma nota apontando para cá):

- **Por quê:** marcar e desmarcar sem parar, de má-fé, faz `dbo.Embalagem` crescer sem limite. As recusas `JaEmbalado`
  e `NaoEmbalado` impedem duas linhas iguais seguidas, mas não impedem alternar.
- **O quê:** uma rotina que apaga as linhas de `dbo.Embalagem` **anteriores a uma data X**, **preservando a última linha de
  cada Peça**, qualquer que seja a data dela. A última linha diz quando a Peça deixou de ser mexida e, muitas vezes,
  quando foi expedida.
- **Por que é seguro:** a validade lê só a última linha (seção 3.2), então a limpeza não muda nenhum "Embalado".
- **Em aberto, para quando for implementada:** o valor de X (uma idade fixa, como "mais de 90 dias"), a frequência e se
  ela roda pelo agendamento das rotinas do Ola Hallengren (D10 daquela spec) ou à parte. É a única exceção ao "só de
  inclusão" desta tabela, e vale só para a rotina de manutenção, nunca para o código da aplicação.

## 4. Banco

- `specs/02-modelo-de-dados.sql` ganha `dbo.Embalagem` (seção 3.1) primeiro, como manda o `CLAUDE.md`.
- `db/alter-panorama.sql`, idempotente, no formato dos anteriores (`-b -f 65001`): cria a tabela, a constraint e o índice
  se faltam. Não muda tabela existente; não precisa regenerar o banco.
- Mapeamento EF Database First da entidade `Embalagem`.

## 5. Backend

### 5.1 `GET /panorama` *(qualquer perfil autenticado)*

Devolve **inteira** a lista de Peças de C2, na ordem de C9, e a tela filtra e ordena no cliente, como o `GET /expedicao`.
Forma proposta (o builder ajusta nomes ao padrão dos DTOs vizinhos):

```jsonc
{
  "pecas": [
    {
      "estruturaItemId": 812,
      "agrupamentoId": 77,
      "pedido": { "id": 41, "numero": "293146", "dataEntrega": "2026-11-06", "atrasado": true, "retrabalho": false,
                  "pausa": { "motivo": "Alteração de projeto" } },   // null sem pausa aberta
      "componente": { "id": 300, "codigo": "038190-C", "descricao": "…", "miniaturaVersao": "9f2c…" },   // C12 da spec da miniatura; null sem miniatura
      "quantidade": 5,
      "itensEm": [ { "setorId": 3, "nome": "Corte" } ],   // C3; vazio sem descendente em Setor
      "distribuicao": [ /* segmentos já na ordem de C8 */ ],
      "afericao": { /* o resumo que a expedição já mostra para a Peça: exige relatório, situação, conforme/não conforme */ },
      "embalagem": {
        "vale": false,
        "podeMarcar": false,
        "emProducao": 3,                       // "faltam N em produção"
        "ultima": { "tipo": "Marcar", "dataHora": "…", "usuario": "qualidade" }   // null sem registro
      }
    }
  ]
}
```

- **Saldos:** os saldos vêm do cálculo da Execução (`CalculadoraDeExecucao.Saldos`, o mesmo de
  `GET /agrupamentos/{id}/posicoes`), mas a **ordem e os segmentos de C8 são montados no servidor**, numa função pura
  com teste próprio: a ordem daquela calculadora é por tipo de posição, não por passo. O DTO troca `saldos` por
  `distribuicao`, uma lista de segmentos já na ordem de C8 (tipo do segmento, Setor e passo quando houver, quantidade e,
  no segmento de passo, o aguardando coleta à parte). A tela só desenha.
- **Aferição:** reaproveitar o que `GET /expedicao` calcula para a Peça. O que importa na linha é: exige relatório ou
  não, e a situação (a medir, conforme, não conforme, liberada), com as cores de C6.
- **Atraso:** o servidor manda `atrasado` no Pedido, como `GET /expedicao` e `GET /pedidos` já fazem (regra 33: data de
  Brasília e status do Pedido). A tela não recalcula.
- **"Itens em":** a leitura percorre também os descendentes de cada Peça (C3), não só a Peça.
- **Custo:** a lista inteira percorre o livro das Peças abertas e dos descendentes delas. O builder mede com a massa de demonstração e, se a
  consulta pesar, lê os saldos de uma vez para todas as Peças (como a Fila e a Expedição já fazem), não Peça a Peça.

### 5.2 `POST /estrutura/{id}/embalagem` *(Qualidade, PCP, Administrador)*

Corpo `{ "embalado": true }` para marcar e `{ "embalado": false }` para desmarcar. Responde 204. Recusas da seção 3.3,
no contrato de erro do projeto (código de erro e mensagem em português), com `faltam` no corpo do
`ProducaoNaoTerminou`. Os outros perfis recebem o 403 do `[Authorize(Roles)]`, sem corpo, como as outras rotas de
escrita. O `specs/05-api-endpoints.md` ganha a seção «Panorama» com
as duas rotas e o contrato de erro.

### 5.3 Testes

- Application, com fakes: as três condições da seção 3.2, uma a uma, e cada recusa da seção 3.3. Casos que têm de
  existir: a marca que **não** ressuscita depois do conserto e volta; o estorno de remessa que **não** invalida; o
  estorno de baixa feita num Setor que invalida; a Peça com quantidade aumentada depois da marca; marcar de novo depois
  de a marca sumir sozinha; desmarcar a marca que sumiu (409 `NaoEmbalado`).
- Infrastructure: mapeamento de `dbo.Embalagem` e o índice. Respeitar as regras de `[Collection]` e de asserção escopada
  do `CLAUDE.md` (nada de contagem global de tabela compartilhada).
- Api: as duas rotas ponta a ponta, o gating por perfil (incluindo a Gestão recebendo 403 no `POST`) e a forma do
  `GET /panorama` com uma Peça dividida, uma pausada, uma de Retrabalho, uma toda expedida (que não aparece) e uma com
  Itens em dois Setores e em dois níveis (o "Itens em" sem repetição).

## 6. Front

- **Módulo** `web/src/panorama/`, no molde da expedição e da separação: a `PanoramaPage` (rota `/panorama`, em
  `web/src/App.tsx`), a linha da tabela, o cartão do celular, o `filtroDoPanorama` (as facetas de C11, com
  `casaComFiltro`), a ordenação pura (`ordenarPanorama`, com a ordem original como desempate) e o
  `usePermissoesDaEmbalagem`, **único lugar que lê `permissoes.ts` para o "Embalado"**. Cliente HTTP em
  `web/src/api/panorama.ts`.
- **Barra:** `AppShell` ganha "Panorama" como primeira entrada, link direto (C1).
- **Primitivas novas** em `web/src/components/`, cada uma com teste próprio, e cada uma vira bullet na seção «Interface»
  do `CLAUDE.md`:
  - **`IntervaloDeDatas`** (C12): controlado, recebe `de`/`ate` (ISO `aaaa-mm-dd` ou vazio) e devolve por `aoMudar`; não
    sabe de URL. Rótulo visível ("Prazo de entrega"), dois `<input type="date">` com nome acessível ("de", "até"), e
    "limpar". Se `de` > `ate`, a primitiva não troca os dois: a tela mostra "nenhuma Peça casa com o filtro".
  - **`CabecalhoOrdenavel`** (D10, C10): o `<th>` com um botão que cicla crescente → decrescente → original, e
    `aria-sort` (`ascending`, `descending`, `none`) no `<th>`. Controlado: recebe a coluna e a ordem atual, devolve a
    próxima por `aoMudar`. O botão é da primitiva, como o `<button>` da `Botao`; não é exceção de "botão de chrome".
  - É a **quinta primitiva de consulta**, ao lado das quatro que o bullet do `FiltroDeDemanda` enumera, e o gatilho dela é
    "ordenar uma **tabela** por coluna". O `SeletorDeOrdem` continua sendo o de lista de cadastro, e é o que o Panorama
    usa no celular (C14).
- **Distribuição:** reaproveitar `ResumoDePosicoes` se ele aceitar o agrupamento de "Expedição" e o traço das saídas sem
  virar outro componente; senão, um componente do módulo que usa as mesmas funções de rótulo (`rotuloDoSaldo`). Fica a
  critério do builder, com a regra de C6 (tudo neutro).
- **"Embalado":** `CampoDeCaixa` (a caixa com texto visível) com o texto "Embalado". Hoje a caixa dele é `size-4` sem
  área de toque de 44 px, ao contrário da `CaixaDeLote`; como a Qualidade marca pelo celular, o `CampoDeCaixa` ganha a
  área mínima de 44 × 44 px (com teste), o que também melhora os usos dele na baixa. **Travada**, com "faltam N em
  produção" ao lado. **Marcada**, com quem e quando. Para quem não pode marcar, uma `Pilula` neutra "Embalado" e a data,
  sem caixa. A escrita trava o segundo toque (o `ref` do `useEscritaDoPainel`, ou o mesmo mecanismo) e recarrega a lista;
  a recusa do servidor aparece junto da linha, sem derrubar a tela.
- **Desenho:** a miniatura da spec da miniatura (D6), pela primitiva `MiniaturaDoSolido` e pelo
  `miniaturaVersao` que aquela spec define (C11 e C12 dela; até a aprovação dela, a seção 5.1 trazia um `temMiniatura`
  provisório), com um espaço neutro quando a Peça não tem miniatura. **O plano do Panorama
  só é escrito depois de a spec da miniatura ser aprovada**, e usa o contrato dela; como a miniatura executa antes, a
  coluna existe desde o primeiro commit do Panorama.
- **Os três estados** (carregando, vazio, erro), cada um com teste que morre se sumir. O vazio distingue "Nenhuma Peça
  em aberto." de "Nenhuma Peça casa com o filtro.".
- **Recarga:** `useCargaPeriodica` com `INTERVALO_DA_EXECUCAO_MS` (C15). A falha numa recarga segue o que o
  `useCargaPeriodica` já faz nas outras telas que o usam (o builder confere e mantém o mesmo comportamento, sem inventar
  um próprio).
- **Testes** de tela com `web/src/testes/api.ts`, `// @vitest-environment jsdom` e `afterEach(cleanup)`. `npm run build`
  no ciclo.

## 7. Riscos conhecidos, aceitos

- **A pausa é do Pedido inteiro.** Se a alteração de projeto costuma pegar uma Peça e deixar as outras do Pedido
  seguirem, a pausa marca demais. Não há marca de atenção por Peça neste trabalho.
- **"Itens em" não tem número** (D13): a Gestão vê que há Itens no Corte, não quantos nem quais. O detalhe está na página
  do Agrupamento (C20).
- **Recarga a cada 30 s com a lista inteira.** Com centenas de Peças abertas, é uma consulta pesada a cada meio minuto
  por tela aberta. Medir (seção 5.1); se pesar, alongar o intervalo do Panorama antes de paginar.
- **A tabela do "Embalado" cresce** até a limpeza da seção 3.5 existir.

## 8. Abordagens consideradas

**Para o "Embalado"** (a escolha de verdade deste desenho):

1. **Tabela de registro só de inclusão, com a validade calculada na leitura** — **escolhida (D11)**. Ninguém precisa
   lembrar de desmarcar; conserto, baixa, remessa, estornos e reposição não mudam; o histórico fica inteiro. Custo: a
   leitura do Panorama consulta o livro depois da marca (seção 3.2).
2. **Duas colunas na Peça** (`EmbaladoEm`, `EmbaladoPorUsuarioId`), apagadas ao desmarcar. Mais simples de gravar, mas
   o "some sozinho" exigiria o mesmo cálculo na leitura ou código de desmarcar em cada caso de uso que devolve unidade à
   produção (uns cinco, e o próximo esqueceria), e o histórico se perderia num `UPDATE`.
3. **"Embalado" como posição do livro**, por quantidade. O mais fiel ao físico, mas mexe nas cinco posições do local de
   expedição, na ordem de tirada da remessa, nas constraints do livro e no estorno, para uma marca que é da Peça inteira.
   Descartada.

**Para a leitura:** lista inteira filtrada no cliente (escolhida, como a Expedição e a Fila) contra paginação no servidor
(como Pedidos). Paginar faria sentido com milhares de Peças abertas, mas as opções dos filtros (Pedido, Setor) deixariam de
sair da própria lista.

**Para a disposição:** lista solta (escolhida, D7) contra agrupada por Pedido (com prazo, atraso e pausa uma vez por
grupo). Para a Peça dividida: tudo na ordem do Roteiro (escolhida, D2) contra só a parte mais atrasada, ou uma linha por
lugar.

## 9. Ajustes de documentação (na mesma branch)

- `specs/01-dominio-e-regras-de-negocio.md`: regra 40 (seção 3) e o termo "Embalado" no glossário.
- `specs/02-modelo-de-dados.sql`: `dbo.Embalagem`.
- `specs/04-fluxos-de-usuario.md`: o fluxo do Panorama (Gestão consulta; Qualidade marca o "Embalado").
- `specs/05-api-endpoints.md`: seção «Panorama».
- `specs/06-roadmap-mvp.md`: seção do Panorama e a da miniatura, e a posição das duas (ver abaixo).
- `CLAUDE.md`: a exceção de ordem, as duas primitivas novas na «Interface», o `db/alter-panorama.sql` em «Comandos».
- `docs/superpowers/specs/2026-10-02-hml-cicd-e-manutencao-do-banco-design.md`, seção 8: nota apontando a limpeza
  pendente da seção 3.5.

**Posição no roadmap (D14):** miniatura → Panorama, antes da 3C. A seção do roadmap registra que a posição em relação à
Fase 6 não foi dita. A renomeação do push não entra nesta edição.

## 10. Fora do escopo

- A rotina de limpeza de `dbo.Embalagem` (seção 3.5): pendente para a manutenção do banco.
- A miniatura em si: spec própria (D6).
- Cor de pintura, pintura terceirizada, nota fiscal.
- Marca de atenção por Peça (seção 7).
- Exportar o Panorama para planilha.
