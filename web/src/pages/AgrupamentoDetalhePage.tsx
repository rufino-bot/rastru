import { useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import {
  obterEstrutura, criarPeca, acrescentarFilho, editarNo, excluirNo, ehConflitoDeEstrutura,
  type NoDaEstrutura, type NovoFilho, type EdicaoDeNo, type ResultadoDeEstrutura,
} from '../api/estrutura'
import { obterAgrupamento, type ComponenteDto, type AgrupamentoDto } from '../api/cadastros'
import { obterPosicoes, type PosicoesDoNoDto } from '../api/execucao'
import { listarFilhosPadrao, type FilhoPadraoDto } from '../api/receitaPadrao'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { SeletorComBusca } from '../components/SeletorComBusca'
import { ArvoreDeEstrutura } from '../components/ArvoreDeEstrutura'
import { Confirmacao } from '../components/Confirmacao'

/** O que o painel de escrita combinado (acrescentar filho / editar nó) está fazendo agora. Os dois
    modos nunca coexistem — um único painel, uma única `<form>` — então um estado discriminado é
    mais simples que dois pares de estado independentes que precisariam ser mantidos mutuamente
    exclusivos à mão. */
type PainelDeEscrita =
  | { tipo: 'acrescentarFilho'; paiId: number }
  | { tipo: 'editar'; no: NoDaEstrutura }

/** Busca em profundidade um nó pelo id, na árvore inteira (raízes + descendentes). Usada só para
    dar ao painel de "acrescentar filho" uma referência legível do PAI — `onAcrescentarFilho` (Task
    7) só entrega o `paiId`, não o nó inteiro. */
function localizarNo(lista: NoDaEstrutura[], id: number): NoDaEstrutura | null {
  for (const no of lista) {
    if (no.id === id) return no
    const achado = localizarNo(no.filhos, id)
    if (achado) return achado
  }
  return null
}

/** Desfechos não-`'ok'` de `excluirNo`. Na prática só `PedidoNaoAberto` e `NaoEncontrado` ocorrem
    (`ExcluirNo` não chama `PlanejadorDeCopia`), mas o `Record` é exaustivo sobre o tipo inteiro —
    se o backend um dia passar a emitir um dos três códigos de `PlanejadorDeCopia` aqui, o `tsc`
    cobra a frase em vez de a tela ficar muda no `catch`-all silencioso de um índice ausente. */
const MOTIVO_DA_RECUSA_EXCLUSAO: Record<Exclude<ResultadoDeEstrutura, 'ok'>, string> = {
  NaoEncontrado: 'Este nó já não existe mais.',
  PedidoNaoAberto: 'O pedido não está mais aberto: não dá para excluir nós dele.',
  CicloNaReceita: 'Não foi possível excluir: conflito na estrutura.',
  EstruturaProfundaDemais: 'Não foi possível excluir: conflito na estrutura.',
  EstruturaGrandeDemais: 'Não foi possível excluir: conflito na estrutura.',
  // Fase 3: `ExcluirNo` roda no esquema de trava da execução (spec §8.1). O primeiro só existe no
  // `EditarNo`, mas o `Record` é exaustivo sobre o tipo inteiro.
  QuantidadeAbaixoDoMovimentado: 'Não foi possível excluir: conflito na estrutura.',
  ConflitoDeConcorrencia: 'Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.',
}

/**
 * Detalhe de um Agrupamento (Fase 2): a árvore de `EstruturaItem` dele, o formulário para criar a
 * primeira Peça (nó de topo, copiando a receita do catálogo), e — Task 8b — as três ações que
 * faltavam para a fase cumprir "montar" a estrutura: acrescentar sub-Item, editar um nó e excluir
 * um nó com a subárvore. `onAcrescentarFilho`/`onEditar`/`onExcluir` (Task 7) tinham ZERO
 * consumidores até esta task — só `onAcrescentarFilho`/`onEditar`/`onExcluir` sem callback nenhum,
 * então a árvore nunca desenhava as três ações.
 *
 * ## Cabeçalho (Task 8b, decisão do usuário de 2026-09-02)
 *
 * O título mostra `código — tipo` do Agrupamento (o que o usuário reconhece), não o `id` sozinho.
 * `obterAgrupamento` é chamado à parte, sem estado de carregamento nem banner PRÓPRIOS: se a busca
 * falhar, o título cai para `Agrupamento {id}` (o que a tela já mostrava antes desta task) e segue
 * — um terceiro banner disputando espaço repetiria a mesma família de defeito que os Minor m8/m9
 * herdados da Task 8 já pagaram (estados de erro mal separados). O Id da rota continua exposto,
 * discreto, ao lado do título (via `acao` de `Pagina`) — é a referência técnica para rastrear logs.
 *
 * ## Painel de escrita combinado
 *
 * `acrescentarFilho` e `editar` reaproveitam o MESMO painel (`painel: PainelDeEscrita | null`):
 * nunca aparecem ao mesmo tempo, e um estado discriminado evita ter que zerar dois formulários
 * independentes toda vez que um deles fecha. `acrescentarFilho` tem dois modos — catálogo
 * (`SeletorComBusca`, `descricao: null`, sempre herda a do Componente) e ad-hoc (campo de texto,
 * `componenteId: null`, `descricao` obrigatória) — os dois que `NovoFilho` já modela.
 *
 * `editar` manda SÓ `descricao`/`quantidade` (D4, por asserção de corpo — não um terceiro campo
 * "vazando" no PUT). O `EdicaoDeNo.mensagem` de conflito nunca vem do backend aqui (`editarNo`
 * nunca emite 409), mas o branch de `ehConflitoDeEstrutura` continua tratado — o TIPO de retorno é
 * a união, então o `tsc` cobra os dois ramos independente do que o backend faz hoje.
 *
 * ## Exclusão
 *
 * Pede confirmação (`Confirmacao`, nova primitiva — não havia uma antes desta task) porque apaga a
 * SUBÁRVORE inteira, não só o nó. O 404 (`NaoEncontrado`) é DESFECHO, não exceção — o nó já não
 * existe, a tela informa e recarrega, não cai no banner genérico do `catch`.
 *
 * ## Erros de escrita, três estados independentes
 *
 * `erroEscrita` (criar Peça), `erroPainel` (acrescentar filho / editar) e `erroExcluir` (excluir)
 * são estados SEPARADOS — nunca um `??` disputando um slot só, e cada `set*Erro(null)` no início da
 * própria ação (não só no `catch`) para não herdar, para a escrita, o defeito que o Important C1 e
 * o Minor m7 já pagaram para a carga: banner que nunca some depois de a ação voltar a dar certo.
 * `erroEscrita` mora DENTRO do `<form>` de criar Peça (m9 herdado da Task 8): antes ele ficava
 * empilhado logo abaixo de `erro`, e com os dois mostrando a MESMA frase genérica de rede (offline)
 * o usuário via a frase duplicada sem saber qual banner era de qual ação. Dentro do formulário, o
 * banner fica ao lado do botão que o produz — vale para o painel combinado também.
 */
export function AgrupamentoDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const agrupamentoId = Number(id)
  const idValido = !Number.isNaN(agrupamentoId)

  const [nos, setNos] = useState<NoDaEstrutura[]>([])
  const [posicoes, setPosicoes] = useState<ReadonlyMap<number, PosicoesDoNoDto>>(new Map())
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const [agrupamento, setAgrupamento] = useState<AgrupamentoDto | null>(null)

  const [componente, setComponente] = useState<ComponenteDto | null>(null)
  const [quantidade, setQuantidade] = useState('')
  const [requerRelatorioDimensional, setRequerRelatorioDimensional] = useState(false)
  const [enviando, setEnviando] = useState(false)
  const [erroEscrita, setErroEscrita] = useState<string | null>(null)

  const [painel, setPainel] = useState<PainelDeEscrita | null>(null)
  const [modoNovoFilho, setModoNovoFilho] = useState<'catalogo' | 'adhoc'>('catalogo')
  const [componenteNovoFilho, setComponenteNovoFilho] = useState<ComponenteDto | null>(null)
  const [descricaoPainel, setDescricaoPainel] = useState('')
  const [quantidadePainel, setQuantidadePainel] = useState('')
  const [razaoPainel, setRazaoPainel] = useState('')
  const [receitaDoPai, setReceitaDoPai] = useState<FilhoPadraoDto[] | null>(null)
  const [enviandoPainel, setEnviandoPainel] = useState(false)
  const [erroPainel, setErroPainel] = useState<string | null>(null)

  const [noParaExcluir, setNoParaExcluir] = useState<NoDaEstrutura | null>(null)
  const [erroExcluir, setErroExcluir] = useState<string | null>(null)

  const podeEscrever = usePodeEscrever('estrutura')

  // Recebe o id como argumento (não fecha sobre `agrupamentoId` de fora): mesmo motivo do
  // comentário equivalente em `PedidoDetalhePage.tsx` — o exhaustive-deps cobra 'carregar' como
  // dependência faltando se o corpo não usar o parâmetro.
  async function carregar(id: number) {
    setCarregando(true)
    // I3 do segundo fix pass da Task 8: sem este `setErro(null)`, `erro` só era ESCRITO (no
    // `catch` abaixo) e nunca zerado — uma carga que falhasse prendia o banner (e a guarda
    // `erro === null &&` do ramo da árvore) para sempre, mesmo depois de uma recarga
    // bem-sucedida. Molde de `ComponenteDetalhePage`: o `setErroComponente(null)` que abre o efeito
    // de carga do componente, no INÍCIO de cada carga, não só no `catch`.
    setErro(null)
    try {
      // Fase 3: a árvore e onde está cada nó chegam juntas, e falham juntas — uma árvore sem as
      // pílulas de posição mentiria sobre o critério de pronto da fase ("dá para acompanhar, item
      // por item, onde cada peça está"). Um banner só, o de carga.
      const [dados, saldos] = await Promise.all([obterEstrutura(id), obterPosicoes(id)])
      setNos(dados)
      setPosicoes(new Map(saldos.map((p) => [p.estruturaItemId, p])))
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar a estrutura.'))
    } finally {
      setCarregando(false)
    }
  }

  // m4 do fix pass da Task 8: com `:id` não numérico (`/agrupamentos/abc` -> `NaN`) não há
  // agrupamento nenhum para buscar — sem a guarda, o efeito ainda disparava
  // `GET /agrupamentos/NaN/estrutura`. Molde de `ComponenteDetalhePage` (`idValido`).
  useEffect(() => { if (idValido) carregar(agrupamentoId) }, [agrupamentoId, idValido])

  // Busca do cabeçalho, Task 8b. SEM `carregando`/`erro` próprios de propósito (ver o comentário
  // do componente): falha aqui não pode aparecer como um terceiro banner — o título só cai de
  // volta para `Agrupamento {id}`. `cancelado` evita que uma resposta tardia de um `:id` antigo
  // escreva por cima do agrupamento do `:id` novo (molde de `ComponenteDetalhePage`).
  useEffect(() => {
    if (!idValido) return
    let cancelado = false
    obterAgrupamento(agrupamentoId)
      .then((a) => { if (!cancelado) setAgrupamento(a) })
      .catch(() => {})
    return () => { cancelado = true }
  }, [agrupamentoId, idValido])

  // A razão do filho vem PRÉ-PREENCHIDA quando a receita do Componente do pai lista aquele filho
  // (spec da Fase 3 §3.4) — é a mesma `QuantidadePadrao` que a cópia da receita usaria. A receita é
  // buscada quando o painel abre sobre um pai de catálogo; um pai ad-hoc não tem receita. Falha
  // aqui é silenciosa: o pré-preenchimento é conveniência, e o campo continua editável e exigido.
  const paiIdDoPainel = painel?.tipo === 'acrescentarFilho' ? painel.paiId : null
  useEffect(() => {
    setReceitaDoPai(null)
    if (paiIdDoPainel === null) return
    const componenteDoPai = localizarNo(nos, paiIdDoPainel)?.componenteId ?? null
    if (componenteDoPai === null) return
    let cancelado = false
    listarFilhosPadrao(componenteDoPai)
      .then((r) => { if (!cancelado) setReceitaDoPai(r) })
      .catch(() => {})
    return () => { cancelado = true }
  }, [paiIdDoPainel, nos])

  function escolherComponenteNovoFilho(c: ComponenteDto | null) {
    setComponenteNovoFilho(c)
    const daReceita = c && receitaDoPai?.find((f) => f.componenteFilhoId === c.id)
    if (daReceita) setRazaoPainel(String(daReceita.quantidadePadrao))
  }

  async function salvar(e: FormEvent) {
    e.preventDefault()
    if (!componente) return
    setErroEscrita(null)
    setEnviando(true)
    try {
      const resultado = await criarPeca(agrupamentoId, {
        componenteId: componente.id,
        quantidade: Number(quantidade),
        requerRelatorioDimensional,
      })
      if (ehConflitoDeEstrutura(resultado)) {
        // `mensagem` nomeia o caminho do ciclo quando o código é `CicloNaReceita` (sempre vem
        // nesse caso — ver o comentário de `ConflitoDeEstrutura` em `api/estrutura.ts`); os
        // outros três códigos podem chegar sem `mensagem`, daí o fallback genérico.
        setErroEscrita(resultado.mensagem ?? 'Não foi possível criar a Peça: conflito na estrutura.')
        return
      }
      setComponente(null)
      setQuantidade('')
      setRequerRelatorioDimensional(false)
      await carregar(agrupamentoId)
    } catch (e) {
      // I2 do fix pass da Task 8: este `catch` é o único caminho que transforma um 403 em texto na
      // tela ("Seu perfil não tem permissão para esta ação.") em vez de deixar a exceção subir — o
      // 403 é a fronteira REAL (adendo F2), esconder o formulário no front não é segurança.
      setErroEscrita(mensagemDeErro(e, 'Não foi possível criar a Peça.'))
    } finally {
      setEnviando(false)
    }
  }

  // As duas funções abaixo também fecham a confirmação de exclusão (`setNoParaExcluir(null)`):
  // sem isso, abrir "Acrescentar filho"/"Editar" num nó enquanto a confirmação de excluir OUTRO
  // nó está na tela deixaria dois diálogos de escrita simultâneos — e os dois têm um botão
  // "Cancelar" com o mesmo nome acessível, o tipo de colisão que `getByRole` rejeita.
  function abrirAcrescentarFilho(paiId: number) {
    setNoParaExcluir(null)
    setPainel({ tipo: 'acrescentarFilho', paiId })
    setModoNovoFilho('catalogo')
    setComponenteNovoFilho(null)
    setDescricaoPainel('')
    setQuantidadePainel('')
    setRazaoPainel('')
    setErroPainel(null)
  }

  function abrirEditar(no: NoDaEstrutura) {
    setNoParaExcluir(null)
    setPainel({ tipo: 'editar', no })
    // Pré-preenchido com a descrição JÁ RESOLVIDA pelo backend (regra 19) — o usuário edita a
    // partir do que vê na tela, não de um campo em branco que ele não sabe se está herdando ou não.
    //
    // I3 do fix pass da Task 8b, decisão CONSCIENTE do usuário (2026-09-02, caminho (a) — registrar
    // e manter): salvar esta edição SEM tocar na descrição grava `descricao` própria no nó (mesmo
    // valor pré-preenchido, mas agora persistido — ver `corpoDaEdicao`), e o nó PARA de acompanhar
    // o Componente do catálogo (regra 19: só uma descrição NULA herda). O front não distingue hoje
    // "descrição própria" de "descrição herdada" — `EstruturaItemDto.Descricao` já vem resolvida,
    // sem essa bandeira (`Rastreamento.Application/Estrutura/EstruturaDtos.cs`). Consertar isso é
    // trabalho FUTURO e separado (trazer a referência do catálogo do backend), fora do escopo desta
    // task.
    setDescricaoPainel(no.descricao)
    setQuantidadePainel(String(no.quantidade))
    setRazaoPainel(no.quantidadePorPai === null ? '' : String(no.quantidadePorPai))
    setErroPainel(null)
  }

  // Mesmo motivo do `setNoParaExcluir(null)` que abre `abrirEditar`, na direção oposta: pedir a
  // exclusão de um nó fecha o painel de acrescentar/editar que porventura esteja aberto.
  function pedirExclusao(no: NoDaEstrutura) {
    fecharPainel()
    setNoParaExcluir(no)
  }

  function fecharPainel() {
    setPainel(null)
    setModoNovoFilho('catalogo')
    setComponenteNovoFilho(null)
    setDescricaoPainel('')
    setQuantidadePainel('')
    setRazaoPainel('')
    setErroPainel(null)
  }

  async function salvarPainel(e: FormEvent) {
    e.preventDefault()
    if (!painel) return
    // Mesmo padrão de `salvar` (`if (!componente) return`): a guarda real é o `disabled` do botão
    // (`painelInvalido`), mas este `return` explícito documenta a invariante que
    // `corpoDoNovoFilho` assume ao ler `componenteNovoFilho!.id` — sem ele, um clique fora do
    // fluxo normal (ex.: `form.requestSubmit()` disparado por outro caminho) chamaria a API com
    // `componenteId` de um `null` forçado a número.
    if (painel.tipo === 'acrescentarFilho' && modoNovoFilho === 'catalogo' && !componenteNovoFilho) return
    // m2 do fix pass da Task 8b: esta linha é REDUNDANTE, medido e não suposto — `abrirAcrescentarFilho`,
    // `abrirEditar` e `fecharPainel` já zeram `erroPainel`, e `fecharPainel` roda em todo sucesso.
    // A única diferença OBSERVÁVEL é a janela em voo: com ela, o banner de uma tentativa anterior
    // some assim que "Salvando…" começa; sem ela, ficaria até a resposta chegar. Defensável, mas é
    // a mesma classe do M10 (defesa sem prova própria) — mantida por essa razão, não por simetria
    // com o `catch`.
    setErroPainel(null)
    setEnviandoPainel(true)
    try {
      const resultado = painel.tipo === 'acrescentarFilho'
        ? await acrescentarFilho(painel.paiId, corpoDoNovoFilho())
        : await editarNo(painel.no.id, corpoDaEdicao(painel.no))
      if (ehConflitoDeEstrutura(resultado)) {
        const fallback = painel.tipo === 'acrescentarFilho'
          ? 'Não foi possível acrescentar o item: conflito na estrutura.'
          : 'Não foi possível editar o nó: conflito na estrutura.'
        setErroPainel(resultado.mensagem ?? fallback)
        // Fix round 1 (Important da review da Task 8): um 409 de EDITAR quase sempre significa que
        // a tela ficou velha (spec §8.3) — os dois códigos novos desta task
        // (`QuantidadeAbaixoDoMovimentado`, `ConflitoDeConcorrencia`) são exatamente esse caso, e
        // as pílulas de posição ficariam com saldo velho ao lado da frase do servidor. Recarrega
        // SEM fechar o painel (`fecharPainel` não é chamado): o operador vê a frase e não perde o
        // que digitou. Só no `editar` — `acrescentarFilho` não estava no achado da review, e os
        // três conflitos que ele emite (`CicloNaReceita`/`EstruturaProfundaDemais`/
        // `EstruturaGrandeDemais`) não são do tipo "dado ficou velho".
        if (painel.tipo === 'editar') await carregar(agrupamentoId)
        return
      }
      fecharPainel()
      await carregar(agrupamentoId)
    } catch (e) {
      const fallback = painel.tipo === 'acrescentarFilho'
        ? 'Não foi possível acrescentar o item.'
        : 'Não foi possível editar o nó.'
      setErroPainel(mensagemDeErro(e, fallback))
    } finally {
      setEnviandoPainel(false)
    }
  }

  function corpoDoNovoFilho(): NovoFilho {
    const quantidadePorPai = Number(razaoPainel)
    return modoNovoFilho === 'catalogo'
      ? { componenteId: componenteNovoFilho!.id, descricao: null, quantidade: Number(quantidadePainel), quantidadePorPai }
      : { componenteId: null, descricao: descricaoPainel, quantidade: Number(quantidadePainel), quantidadePorPai }
  }

  // D4 da Fase 2, estendido na Fase 3: descrição, quantidade e a razão — por asserção de corpo,
  // nenhum quarto campo "vazando" no PUT. A razão é do Item; na Peça vai `null`, que é o que o
  // backend exige (regra 26). Descrição vazia/nula volta a herdar a do Componente (regra 19); o
  // campo aceita os dois porque o `<input>` só produz string, nunca `null` sozinho.
  function corpoDaEdicao(no: NoDaEstrutura): EdicaoDeNo {
    return {
      descricao: descricaoPainel.trim() === '' ? null : descricaoPainel,
      quantidade: Number(quantidadePainel),
      quantidadePorPai: no.nivelHierarquico === 'Item' ? Number(razaoPainel) : null,
    }
  }

  function cancelarExclusao() {
    setNoParaExcluir(null)
  }

  // `excluirNo` só lança para status fora de 204/404/409 — o 404 (NaoEncontrado) e o 409
  // (PedidoNaoAberto, na prática) chegam como retorno normal, tratados por MOTIVO_DA_RECUSA_EXCLUSAO
  // — mesmo molde de `excluir`/`MOTIVO_DA_RECUSA` em `PedidoDetalhePage.tsx`: recarrega mesmo na
  // recusa (a árvore pode ter mudado sob os pés do usuário), só não recarrega se a chamada lançar.
  async function confirmarExclusao() {
    if (!noParaExcluir) return
    const id = noParaExcluir.id
    setNoParaExcluir(null)
    setErroExcluir(null)
    try {
      const desfecho = await excluirNo(id)
      if (desfecho !== 'ok') setErroExcluir(MOTIVO_DA_RECUSA_EXCLUSAO[desfecho])
      await carregar(agrupamentoId)
    } catch (e) {
      setErroExcluir(mensagemDeErro(e, 'Não foi possível excluir o nó.'))
    }
  }

  // m4 do fix pass da Task 8: `id` inválido não tenta nenhuma busca (as guardas dos `useEffect`
  // acima) e mostra só este banner — mesmo molde de `ComponenteDetalhePage`.
  if (!idValido) {
    return (
      <Pagina titulo="Agrupamento">
        <BannerDeErro mensagem="Este agrupamento não existe." />
      </Pagina>
    )
  }

  const titulo = agrupamento ? `${agrupamento.codigo} — ${agrupamento.tipo}` : `Agrupamento ${agrupamentoId}`

  const paiDoPainel = painel?.tipo === 'acrescentarFilho' ? localizarNo(nos, painel.paiId) : null
  const rotuloDoNoDoPainel = painel?.tipo === 'editar'
    ? `${painel.no.codigoDoComponente ?? painel.no.descricao} (Id ${painel.no.id})`
    : painel?.tipo === 'acrescentarFilho'
      ? `${paiDoPainel ? (paiDoPainel.codigoDoComponente ?? paiDoPainel.descricao) : 'nó'} (Id ${painel.paiId})`
      : ''

  const quantidadePainelValida = Number(quantidadePainel) > 0
  // Regra 26: todo Item tem razão. Todo filho acrescentado é Item; na edição, só o Item a pede.
  const pedeRazao = painel?.tipo === 'acrescentarFilho'
    || (painel?.tipo === 'editar' && painel.no.nivelHierarquico === 'Item')
  const razaoPainelValida = !pedeRazao || Number(razaoPainel) > 0
  const painelInvalido = !painel || !quantidadePainelValida || !razaoPainelValida || (
    painel.tipo === 'acrescentarFilho'
      ? (modoNovoFilho === 'catalogo' ? !componenteNovoFilho : descricaoPainel.trim() === '')
      // m5 do fix pass da Task 8b: regra 19, mesma guarda do modo ad-hoc do acrescentar (linhas
      // acima) — um nó AD-HOC (`componenteId === null`) não tem Componente de catálogo para
      // herdar a descrição, então esvaziá-la na edição mandaria `descricao: null` ao backend, que
      // recusa com 400 (`ErroDeDescricaoObrigatoria`). Um nó de catálogo pode ficar em branco
      // (volta a herdar do Componente, regra 19) — a guarda é só para o ad-hoc.
      : (painel.no.componenteId === null && descricaoPainel.trim() === '')
  )

  return (
    <Pagina
      titulo={titulo}
      // Id da rota, discreto — "referência técnica" (decisão do usuário, Task 8b): o cabeçalho
      // agora mostra código/tipo, mas o Id continua tendo valor para rastrear logs. `acao` é
      // documentado para a ação principal da tela, mas aceita qualquer `ReactNode` — reaproveitado
      // aqui porque é o único slot de `Pagina` que fica ao lado do `<h1>`, sem criar uma segunda
      // linha de cabeçalho só para isto.
      acao={<span className="font-mono text-xs text-tinta-fraca">{`Id ${agrupamentoId}`}</span>}
    >
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
            <SeletorComBusca
              rotulo="Componente"
              valorSelecionado={componente}
              aoSelecionar={setComponente}
              // Regra 18: só aqui, porque este seletor escolhe o Componente de origem da PEÇA. O
              // `SeletorComBusca` do painel de acrescentar filho (`modoNovoFilho === 'catalogo'`)
              // fica de fora — Item pode ser ad-hoc.
              exigirSolido
            />
            <Campo rotulo="Quantidade">
              {(idDoCampo) => (
                <input
                  id={idDoCampo}
                  type="number"
                  step="any"
                  value={quantidade}
                  onChange={(e) => setQuantidade(e.target.value)}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <label className="flex items-center gap-2 text-sm text-tinta-fraca">
            <input
              type="checkbox"
              checked={requerRelatorioDimensional}
              onChange={(e) => setRequerRelatorioDimensional(e.target.checked)}
              className="size-4 accent-acao"
            />
            Requer relatório dimensional
          </label>
          {/* m9 herdado da Task 8: o banner de escrita mora DENTRO do formulário, ao lado do botão
              que o produz — não empilhado com `erro` (carga) abaixo do form, onde os dois podiam
              mostrar a MESMA frase genérica de rede sem indicar qual ação falhou. */}
          <BannerDeErro mensagem={erroEscrita} />
          <Botao
            type="submit"
            carregando={enviando}
            rotuloCarregando="Salvando…"
            disabled={!componente || !(Number(quantidade) > 0)}
            className="self-start"
          >
            Criar Peça
          </Botao>
        </form>
      )}

      {painel && (
        <form
          onSubmit={salvarPainel}
          data-testid="painel-de-escrita"
          className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4"
        >
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-lg font-medium text-tinta">
                {painel.tipo === 'acrescentarFilho' ? 'Acrescentar sub-Item' : 'Editar nó'}
              </h2>
              <p className="text-xs text-tinta-fraca">{rotuloDoNoDoPainel}</p>
            </div>
            <Botao variante="secundario" onClick={fecharPainel}>Cancelar</Botao>
          </div>

          {painel.tipo === 'acrescentarFilho' && (
            <fieldset className="flex flex-col gap-2">
              <legend className="text-sm font-medium text-tinta">Origem do sub-Item</legend>
              <label className="flex items-center gap-2 text-sm text-tinta-fraca">
                <input
                  type="radio"
                  name="modo-novo-filho"
                  checked={modoNovoFilho === 'catalogo'}
                  onChange={() => setModoNovoFilho('catalogo')}
                  className="size-4 accent-acao"
                />
                Do catálogo
              </label>
              <label className="flex items-center gap-2 text-sm text-tinta-fraca">
                <input
                  type="radio"
                  name="modo-novo-filho"
                  checked={modoNovoFilho === 'adhoc'}
                  onChange={() => setModoNovoFilho('adhoc')}
                  className="size-4 accent-acao"
                />
                Ad-hoc
              </label>
            </fieldset>
          )}

          <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
            {painel.tipo === 'acrescentarFilho' && modoNovoFilho === 'catalogo' && (
              <SeletorComBusca
                rotulo="Componente"
                valorSelecionado={componenteNovoFilho}
                aoSelecionar={escolherComponenteNovoFilho}
              />
            )}
            {(painel.tipo === 'editar' || (painel.tipo === 'acrescentarFilho' && modoNovoFilho === 'adhoc')) && (
              <Campo rotulo="Descrição">
                {(idDoCampo) => (
                  <input
                    id={idDoCampo}
                    type="text"
                    value={descricaoPainel}
                    onChange={(e) => setDescricaoPainel(e.target.value)}
                    className={CLASSES_DE_CONTROLE}
                  />
                )}
              </Campo>
            )}
            <Campo rotulo="Quantidade">
              {(idDoCampo) => (
                <input
                  id={idDoCampo}
                  type="number"
                  step="any"
                  value={quantidadePainel}
                  onChange={(e) => setQuantidadePainel(e.target.value)}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
            {pedeRazao && (
              <Campo rotulo="Quantidade por pai" dica="Quantos entram em uma unidade do pai (regra 26).">
                {(idDoCampo, idDaDica) => (
                  <input
                    id={idDoCampo}
                    type="number"
                    step="any"
                    value={razaoPainel}
                    onChange={(e) => setRazaoPainel(e.target.value)}
                    aria-describedby={idDaDica}
                    className={CLASSES_DE_CONTROLE}
                  />
                )}
              </Campo>
            )}
          </div>

          <BannerDeErro mensagem={erroPainel} />

          <Botao
            type="submit"
            carregando={enviandoPainel}
            rotuloCarregando="Salvando…"
            disabled={painelInvalido}
            className="self-start"
          >
            {painel.tipo === 'acrescentarFilho' ? 'Acrescentar' : 'Salvar edição'}
          </Botao>
        </form>
      )}

      {/* m5 do segundo fix pass da Task 8: `erro` (carga) e `erroEscrita` (escrita) PODEM
          coexistir — medido, não suposto. Um banner por estado, cada um no seu slot — nunca um
          `??` disputando um só. `erroExcluir` segue o mesmo molde: ação PRÓPRIA (o clique em
          "Excluir" de um nó, dentro da árvore), estado PRÓPRIO. */}
      <BannerDeErro mensagem={erro} />
      <BannerDeErro mensagem={erroExcluir} />

      {carregando ? (
        <EstadoCarregando />
      ) : erro === null && nos.length === 0 ? (
        // Sem busca nesta tela (diferente de `SeletorComBusca`), então não há "não achei" a
        // distinguir de "não há nada" — só existe UM caminho para a lista vazia: a Peça ainda não
        // foi criada. `erro === null &&` continua obrigatório pelo mesmo motivo do Critical (C1)
        // que `PedidoDetalhePage`/`ComponenteDetalhePage` já pagaram: sem ele, uma falha de rede
        // deixaria `nos` em `[]` e este estado vazio apareceria JUNTO do banner de erro.
        <EstadoVazio
          titulo="Este agrupamento ainda não tem estrutura"
          descricao={podeEscrever ? 'Use o formulário acima para criar a primeira Peça.' : undefined}
        />
      ) : (
        erro === null && (
          <ArvoreDeEstrutura
            nos={nos}
            posicoes={posicoes}
            podeEscrever={podeEscrever}
            // Gating na AÇÃO por duas camadas: `ArvoreDeEstrutura` já esconde os botões quando o
            // PRÓPRIO `podeEscrever` que ela recebe é falso, mas a TELA também só passa os
            // callbacks quando pode escrever — sem isso, um perfil sem escrita ainda "poderia"
            // acionar acrescentar/editar/excluir se a primitiva um dia parasse de gatear sozinha.
            // M10/Concern 2 da review da Task 8b: esta camada NÃO TEM MATADOR — `ArvoreDeEstrutura`
            // recebe o mesmo `podeEscrever`, e o `temAcao` dela já começa por `podeEscrever &&`,
            // então remover os três `podeEscrever ? … : undefined` abaixo não quebra teste nenhum
            // (medido pela review, mutação M-F). Fica como redundância honesta — defesa em
            // profundidade sem prova própria —, não por engano.
            onAcrescentarFilho={podeEscrever ? abrirAcrescentarFilho : undefined}
            onEditar={podeEscrever ? abrirEditar : undefined}
            onExcluir={podeEscrever ? pedirExclusao : undefined}
          />
        )
      )}

      <Confirmacao
        aberto={noParaExcluir !== null}
        mensagem={
          noParaExcluir && (
            <>
              Excluir{' '}
              <strong className="font-mono">
                {noParaExcluir.codigoDoComponente ?? noParaExcluir.descricao}
              </strong>
              ? A subárvore inteira (todos os filhos) será excluída junto. Esta ação não pode ser
              desfeita.
            </>
          )
        }
        rotuloConfirmar="Excluir"
        aoConfirmar={confirmarExclusao}
        aoCancelar={cancelarExclusao}
      />
    </Pagina>
  )
}
