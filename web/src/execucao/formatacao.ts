import type { DestinoDto, LocalDto, NoResumoDto, SaldoDto, TipoDeMovimentacao } from '../api/execucao'

const NUMERO = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 4 })

/**
 * Quantidade para tela: vírgula decimal e até quatro casas (`DECIMAL(18,4)`). O JSON manda `6.0`
 * como `6` e `2.5` como `2.5`; o operador lê `6` e `2,5`. Sem separador de milhar forçado por
 * nada daqui — o `Intl` do pt-BR já põe o ponto em `1.234`.
 */
export function formatarQuantidade(n: number): string {
  return NUMERO.format(n)
}

/** "SUP-01 — Suporte", ou só "Suporte" no nó ad-hoc (sem Componente, sem código). */
export function rotuloDoNo(no: Pick<NoResumoDto, 'codigoDoComponente' | 'descricao'>): string {
  return no.codigoDoComponente ? `${no.codigoDoComponente} — ${no.descricao}` : no.descricao
}

/**
 * "Pedido › Agrupamento › pai" (spec §5.2): onde o nó mora, para o operador que só vê a linha da
 * fila. Na Peça, sem pai, o caminho para no Agrupamento.
 */
export function caminhoDoNo(no: NoResumoDto): string {
  const partes = [no.pedidoNumero, no.agrupamentoCodigo]
  if (no.paiDescricao !== null) partes.push(no.paiDescricao)
  return partes.join(' › ')
}

/**
 * Para onde vai o que aguarda coleta (spec §7.3). O nome do pai vem do NÓ, não do destino: o
 * `DestinoDto` só traz `paiId`, e a linha da fila e da tarefa já carrega `paiDescricao`.
 */
export function descreverDestino(destino: DestinoDto, no: NoResumoDto): string {
  if (destino.tipo === 'ProximoPasso') return `${destino.setorNome} (passo ${destino.ordem})`
  if (destino.tipo === 'Expedicao') return 'Local de expedição'
  const pai = no.paiDescricao ?? 'o pai'
  if (destino.paiSemRoteiro) return `Montagem de ${pai} — o pai não tem Roteiro`
  const sugestao = destino.setoresPossiveis.find((s) => s.id === destino.sugestaoSetorId)
  return sugestao ? `Montagem de ${pai} (sugestão: ${sugestao.nome})` : `Montagem de ${pai}`
}

/**
 * Uma posição do saldo em palavras, para as pílulas da árvore (spec §6.3: "4 a iniciar · 6 no
 * Corte · 2 aguardando coleta"). "em Corte", e não "no Corte": o nome do Setor é livre, e
 * "no/na" dependeria do gênero dele ("na Solda"). O passo aparece sempre que a posição o tem,
 * porque o mesmo Setor pode estar duas vezes no Roteiro (regra 21).
 */
export function rotuloDoSaldo(s: SaldoDto): string {
  const q = formatarQuantidade(s.quantidade)
  switch (s.posicao) {
    case 'AIniciar': return `${q} a iniciar`
    case 'NoSetor': return `${q} em ${s.setorNome} (passo ${s.ordem})`
    case 'AguardandoColeta': return `${q} aguardando coleta em ${s.setorNome} (passo ${s.ordem})`
    case 'AguardandoMontagem': return `${q} aguardando montagem em ${s.setorNome}`
    case 'NaExpedicao': return `${q} no local de expedição`
    case 'Montado': return `${q} montados no pai`
  }
}

/** Uma ponta de um movimento do livro, para o histórico do nó: "Corte (passo 1)", "a iniciar"… */
export function rotuloDoLocal(l: LocalDto): string {
  switch (l.posicao) {
    case 'AIniciar': return 'a iniciar'
    case 'NoSetor': return `${l.setorNome} (passo ${l.ordem})`
    case 'AguardandoColeta': return `aguardando coleta em ${l.setorNome} (passo ${l.ordem})`
    case 'AguardandoMontagem': return `aguardando montagem em ${l.setorNome}`
    case 'NaExpedicao': return 'local de expedição'
    case 'Montado': return 'montado no pai'
  }
}

/** O tipo do movimento com acento, como o operador fala. `Montagem` no livro é a BAIXA do filho. */
export function rotuloDoTipo(tipo: TipoDeMovimentacao): string {
  switch (tipo) {
    case 'Inicio': return 'Início'
    case 'Termino': return 'Término'
    case 'Entrega': return 'Entrega'
    case 'Montagem': return 'Baixa de montagem'
    case 'Estorno': return 'Estorno'
  }
}
