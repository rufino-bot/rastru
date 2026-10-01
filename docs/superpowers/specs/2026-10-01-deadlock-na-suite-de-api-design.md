# Deadlock na suíte de Api e no gravar árvore — desenho

Data: 2026-10-01. Branch: `claude/happy-hawking-pcofde`, a partir da `main` em `1594c23` (merge do
rufino-bot/rastru#26). Desenho aprovado pelo usuário em chat na mesma data, nas duas escolhas abaixo
("desligar paralelismo" e "na transação da execução").

## 1. O problema, medido

Medição do usuário em 2026-10-01, registrada no bloco "Fechamento" do ledger do Plano 2 dos filtros:

- `dotnet test Rastreamento.slnx -m:1`, com a API parada e o SQL Server do `docker compose`, falha de
  forma intermitente, e **sempre** em `tests/Rastreamento.Api.Tests`. Foram 4 execuções vermelhas em 7
  na branch `filtros-e-lote-plano-2` (`894fde5`) e 1 em 5 na `main` (`6d05c77`). Cada execução derruba
  um ou dois testes, nunca o mesmo.
- Testes que caíram: `ExecucaoEndpointsTests.Reduzir_a_quantidade_abaixo_do_que_andou_devolve_409_com_mensagem`,
  `CriterioDeProntoDaFase3Tests.Pedido_Avulso_A_com_B_e_C_percorrido_do_inicio_a_expedicao`,
  `ExecucaoEndpointsTests.Roteiro_marca_o_alcancado_e_recusa_mexer_nele`,
  `PausaDePedidoEndpointsTests.Motivo_com_mais_de_200_caracteres_da_400` e
  `PausaDePedidoEndpointsTests.Gestao_pausa_o_operador_nao_inicia_mas_termina_e_depois_de_retomar_inicia`.
- As mensagens apontam deadlock (1205) entre classes **paralelas do mesmo assembly**, sob SERIALIZABLE.
  O `-m:1` só serializa projetos (ver a seção "Processos de teste também competem pelo banco" do
  `CLAUDE.md`). Três falhas foram 409 `ConflitoDeConcorrencia`, porque o retry de
  `ExecucaoRepository.EmTransacaoAsync` se esgotou. Uma foi **500**, com a `SqlException` 1205 crua em
  `POST /estrutura/{id}/filhos`.

São dois defeitos diferentes, e o desenho os separa:

1. **Defeito de teste.** As classes de `Api.Tests` correm em paralelo contra um banco só. Se um
   deadlock entre duas delas sobrevive às 3 tentativas, um teste que esperava 201 recebe 409. Isso é
   residual aceito em produção (spec da Fase 3, seção 8.1: o 409 limpo é o desfecho de esgotar o
   retry), mas na suíte vira vermelho intermitente.
2. **Defeito de produção.** `EstruturaRepository.GravarArvoreAsync` abre uma transação SERIALIZABLE
   própria, sem retry e sem tradução de 1205/1222. O comentário dele diz que gravar árvore fica fora
   do esquema de trava da execução "porque só insere nós NOVOS, que nenhuma escrita da execução
   disputa". A medição refuta essa premissa, e a própria emenda de 2026-09-26 da seção 8.1 da spec da
   Fase 3 já explicava por quê: uma leitura SERIALIZABLE que varre até o fim de um índice esparso
   (`EstruturaRoteiro`, `Movimentacao`) trava o gap depois da última linha, e um nó **novo** inserido
   por outra transação cai sempre nesse gap. Inserir nó novo disputa, sim.

## 2. Item 1 — `Api.Tests` sem paralelismo entre classes

### 2.1 Decisão

Desligar a paralelização do xUnit **só no assembly `Rastreamento.Api.Tests`**, com
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` num arquivo próprio do projeto.

Alternativa descartada: uma `[Collection]` seletiva para as classes que escrevem no livro e na
estrutura. Ela depende de lembrar de pôr cada classe nova na coleção, e esquecer falha em silêncio,
com vermelho intermitente semanas depois. É o mesmo desenho que o `CLAUDE.md` já registrou falhando
duas vezes (`ColecaoQueEscreveEmComponente` e o caso entre processos). E o ciclo medido é de range lock
entre nós **sem relação**, então não existe um recorte de "classes que disputam" que se possa
enumerar com segurança.

### 2.2 O que não muda

- Testes que disparam concorrência **dentro** de um teste (`CorridaNaQueimaDeFamiliaTests`, as corridas
  da execução) continuam concorrentes: o atributo serializa coleções (classes), não as `Task`s que um
  teste lança.
- `Application.Tests` e `Infrastructure.Tests` não mudam. Application não usa banco. Infra não
  apareceu na medição, e o paralelismo dela fica como está (ver 2.5).
- Nenhum código de produção muda por causa deste item.

### 2.3 Guarda

Um teste em `Api.Tests` lê o `CollectionBehaviorAttribute` do próprio assembly e afirma
`DisableTestParallelization == true`. Ele morre se alguém tirar o atributo. Sem ele, tirar o atributo
deixaria a suíte verde na maioria das execuções, e a regressão só apareceria como intermitência.

### 2.4 Medição, antes e depois

- **Antes:** 10 execuções de `dotnet test Rastreamento.slnx -m:1 --no-build` no commit base, cada uma
  com `--logger "trx;LogFilePrefix=execN"` e `--results-directory` próprio (rodar de novo não pode
  apagar a amostra anterior), mais o tempo de parede de cada uma.
- **Bancada:** a sessão de nuvem tem 4 núcleos, e o xUnit usa por padrão um fio de execução paralela
  por núcleo. Se as 10 execuções "antes" saírem todas verdes, a bancada não reproduz o defeito, e um
  "depois" verde não provaria nada. Nesse caso, a medição "antes" é repetida com
  `-- xUnit.MaxParallelThreads=16` para simular a máquina do usuário, e a medição "depois" usa o mesmo
  parâmetro. Com a paralelização desligada, esse parâmetro deixa de ter efeito, e é justamente isso que
  se quer provar.
- **Depois:** as mesmas 10 execuções (e as 10 com `MaxParallelThreads=16`, se a amostra "antes" as
  exigiu) no commit final da branch.
- **Critério de pronto:** 0 vermelhas em todas as execuções "depois", numa bancada em que o "antes"
  teve pelo menos uma vermelha. O custo de tempo da suíte de Api também é registrado.
- Os `.trx` e o resumo (commit, rc, tempo e nomes das falhas) vão para o ledger da task.

### 2.5 Fora do escopo

`Infrastructure.Tests` também escreve no livro sob SERIALIZABLE, com classes em paralelo. Ela não
apareceu em nenhuma das 12 execuções do usuário. Se aparecer, o conserto é o mesmo e cabe em outra
branch. Não se desliga o paralelismo dela "por garantia".

## 3. Item 2 — `GravarArvoreAsync` no esquema da execução

### 3.1 Decisão

`CriarPeca` e `AcrescentarFilho` (`MontagemDeEstruturaUseCase`) passam a gravar a árvore **e** lê-la
de volta dentro de `_execucao.ExecutarAsync`, o mesmo molde do `EditarNo`. Com isso:

- 1205 (deadlock) repete do zero, até 3 tentativas no total, pelo mesmo laço que a execução já usa,
  `ComRetryDeDeadlockAsync`. Fica **uma** política de retry só no sistema, e o comentário de
  `ExecucaoRepository.LerAsync` ("não há segunda política de retry") continua verdadeiro.
- 1222 (lock timeout) e o retry esgotado viram 409, com o código
  `CodigosDaExecucao.ConflitoDeConcorrencia` e a frase `CodigosDaExecucao.MensagemDeConflito`, a mesma
  resposta que as escritas da execução dão. O `EstruturaController` já mapeia `TipoDeErro.Conflito`
  para 409.

Alternativa descartada: um laço de retry e uma tradução próprios dentro de `GravarArvoreAsync`.
Criaria uma segunda política de retry, que o `ExecucaoRepository` diz explicitamente não querer.

### 3.2 O que fica fora da transação, e por quê

Validação de entrada, existência do Agrupamento, do Componente e do pai, leitura do catálogo
(`LerReceitaCompletaAsync`) e planejamento (`PlanejadorDeCopia`) continuam **antes** da transação.
`LerReceitaCompletaAsync` lê as três tabelas da receita padrão inteiras, sem `WHERE`. Sob SERIALIZABLE,
isso travaria as três por faixa até o commit e abriria um ciclo novo com `ReceitaPadraoRepository`,
que também escreve nelas sob SERIALIZABLE. A transação cobre só o que precisa ser atômico: gravar a
árvore e montar o DTO de retorno a partir do que foi gravado.

Vale também para a leitura descartada do Pedido em `CriarPeca` (o `_ = await _pedidos.ObterPorIdAsync`
que existe como prova de alcance): ela fica onde está, fora da transação.

### 3.3 Sem trava de nó

A seção 8.1 da spec da Fase 3 manda as escritas daquela fase travarem os nós envolvidos com UPDLOCK.
Gravar árvore **não** trava o pai: acrescentar filho é livre, inclusive a nó já iniciado (spec da Fase
3, seção 4.7), e a escrita não valida nada contra o livro. O que esta mudança dá à gravação é o retry
e a tradução, não a serialização por nó.

### 3.4 `GravarArvoreAsync` exige transação

`GravarArvoreAsync` deixa de abrir a própria transação. Quem dá a atomicidade ("árvore toda ou nada")
passa a ser a transação de `EmTransacaoAsync`, aberta pelo chamador. Como `TravarNosAsync`, o método
recusa rodar fora dela: com `_db.Database.CurrentTransaction` nulo, lança `InvalidOperationException`
com mensagem que nomeia `EmTransacaoAsync`. Sem essa guarda, um chamador futuro que esquecesse a
transação gravaria meia árvore em silêncio quando um nó do meio falhasse.

Os comentários mudam junto. O XML doc de `IEstruturaRepository.GravarArvoreAsync` passa a dizer que
o método precisa de transação aberta. O de `EstruturaRepository.GravarArvoreAsync` perde o parágrafo
do "residual conhecido" e a premissa falsa, e ganha a explicação certa (nó novo cai no gap travado
pela leitura de outra transação, com o nome da medição de 2026-10-01).

### 3.5 Testes

- **Application** (`Rastreamento.Application.Tests/Estrutura`): com
  `FakeExecucaoRepo.ConflitoNaProximaTransacao = true`, `CriarPeca` e `AcrescentarFilho` devolvem
  falha `TipoDeErro.Conflito` com o código `ConflitoDeConcorrencia`, e o `FakeEstruturaRepo` registra
  **zero** gravações. Os dois morrem se a gravação voltar para fora do `ExecutarAsync`: nesse caso o
  fake gravaria antes do conflito simulado, ou o conflito nem seria lançado, e o resultado seria
  sucesso.
- **Infrastructure** (`EstruturaRepositoryTests`):
  - `GravarArvoreAsync` chamado sem transação lança `InvalidOperationException`. O teste morre se a
    guarda sair.
  - Os testes que chamam `GravarArvoreAsync` direto passam a abrir a transação pelo
    `ExecucaoRepository.EmTransacaoAsync`. O de atomicidade
    (`GravarArvoreAsync_e_atomico_erro_no_meio_da_arvore_nao_deixa_nada_gravado`) prova então a
    composição real: um erro no meio da árvore, dentro de `EmTransacaoAsync`, não deixa nada gravado.
- O retry em si (1205 repete, 1222 não, esgotado vira `ConflitoDeConcorrenciaException`) já é coberto
  por `RetryDeDeadlockEmTransacaoAsyncTests` e não ganha teste novo: o laço é o mesmo.
- O 500 original não ganha teste de Api que provoque deadlock real. Provocá-lo de forma determinística
  exigiria encenar o ciclo de range lock, e um teste assim seria intermitente pela própria natureza. A
  cobertura é a composição acima: Application prova que a gravação passa pelo `ExecutarAsync`, e Infra
  prova que o `ExecutarAsync` traduz.

### 3.6 Residual registrado, não consertado

O pai de `AcrescentarFilho` é lido **fora** da transação. Um `DELETE /estrutura/{id}` concorrente do
pai, entre essa leitura e a gravação, faz o INSERT do filho esbarrar na FK de `EstruturaPaiId` (547).
Como 547 não é 1205/1222, sobe cru, como 500. O caso exige um Pedido `Aberto` com dois usuários
editando a mesma estrutura ao mesmo tempo, um apagando o pai enquanto o outro acrescenta filho. Fica
registrado no comentário de `AcrescentarFilho` e no ledger, sem conserto nesta branch.

**Gêmeo em `CriarPeca`.** O Agrupamento também é lido fora da transação. Um
`DELETE /agrupamentos/{id}` concorrente de um Agrupamento ainda vazio (a única forma de ele passar a
guarda `AgrupamentoNaoVazio`), que faça commit antes do INSERT da Peça, esbarra na FK de
`AgrupamentoId` (547) e sobe cru, como 500. Mesmo pressuposto e mesma conclusão do residual acima.
Anterior a esta branch (antes, a gravação também lia o Agrupamento fora de qualquer transação) e não
piorado por ela; registrado no comentário de `CriarPeca`.

**Segundo residual: a leitura de volta, sob SERIALIZABLE, trava a tabela inteira.** A leitura de
volta dentro da transação (`MontadorDeArvoreDeEstrutura.MontarAsync`, que chama
`EstruturaRepository.ListarDoAgrupamentoAsync`: `WHERE AgrupamentoId = @p ORDER BY Id`) foi medida
pelo revisor da branch, em 2026-10-01, no banco de dev. Ela tomou `RangeS-S` em **toda** chave de
`PK_EstruturaItem`: varredura do índice clusterizado, não busca por `IX_EstruturaItem_Agrupamento`.
O banco de dev tinha 9 linhas em 3 Agrupamentos. Consequência: duas chamadas concorrentes de criar
Peça ou acrescentar filho, **mesmo em Agrupamentos diferentes**, seguram X na chave que cada uma
acabou de inserir e depois pedem `RangeS-S` na da outra. É um ciclo de deadlock que não existia
antes, quando a leitura de volta rodava depois do commit, em READ COMMITTED. O retry absorve: a
perdedora refaz a escrita inteira. Com três ou mais escritores concorrentes, ou receitas longas, o
409 é possível, e num 409 nada foi gravado. Depende do plano de consulta: com tabelas maiores o
otimizador pode preferir a busca pelo índice do Agrupamento (não medido). O `EditarNo` já lê de volta
dentro da transação do mesmo modo.

Decisão do controlador, mantida: a leitura de volta **fica** dentro da transação. Fora dela, uma
falha depois do commit tornaria o 409 ambíguo (a Peça foi gravada, mas o cliente recebe "conflito") e
convidaria a uma Peça duplicada no reenvio. Registrado, não consertado. Conserto futuro: fazer a
leitura usar o índice por Agrupamento e remedir.

## 4. Documentação

- **Spec da Fase 3, seção 8.1:** uma emenda de 2026-10-01 dizendo que criar Peça e acrescentar filho
  entram no retry e na tradução da execução (sem trava de nó, pela seção 3.3 desta spec), e por quê.
- **`CLAUDE.md`, seção "Processos de teste também competem pelo banco":** um parágrafo dizendo que o
  `-m:1` não basta dentro de um assembly, que `Api.Tests` roda com a paralelização desligada (com a
  medição de antes e depois) e qual teste é a guarda.
- **Ledger:** pasta própria em `.superpowers/sdd/`, com o progresso, os relatórios de task e de review,
  e as medições.

## 5. Critério de pronto

1. `dotnet build Rastreamento.slnx -warnaserror` com 0 avisos.
2. Suíte inteira verde com `-m:1`, e a medição "depois" da seção 2.4 com 0 vermelhas.
3. Os testes novos da seção 3.5 existem e morrem com a mutação que cada um nomeia, verificado ao
   aplicar a mutação.
4. O comentário falso de `GravarArvoreAsync` não existe mais.
5. PR aberto contra a `main`. O merge é do usuário.
