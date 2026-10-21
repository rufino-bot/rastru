import type { DestinoDto, Estornavel, LocalDto, NoResumoDto, ResumoDeMaterial, SaldoDto, TipoDeMovimentacao } from '../api/execucao'
import { formatarDataHora } from '../api/cadastros'

const NUMERO = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 4 })

/**
 * Quantidade para tela: vírgula decimal e até quatro casas (`DECIMAL(18,4)`). O JSON manda `6.0`
 * como `6` e `2.5` como `2.5`; o operador lê `6` e `2,5`. Sem separador de milhar forçado por
 * nada daqui — o `Intl` do pt-BR já põe o ponto em `1.234`.
 */
export function formatarQuantidade(n: number): string {
  return NUMERO.format(n)
}

/**
 * A quantidade seguida da palavra no número certo: singular abaixo de 2 ("1 pronto", "1,5 pronto"),
 * plural de 2 em diante ("2 prontos"). A fração fica no singular porque lê-se "um e meio pronto".
 */
export function quantidadeConcordada(n: number, singular: string, plural: string): string {
  return `${formatarQuantidade(n)} ${n < 2 ? singular : plural}`
}

/**
 * O resumo de material da Fila (regra 34): "Material completo" quando todo material do almoxarifado no
 * planejado está completo, senão quantos estão de quantos. Conta materiais, não quantidades.
 */
export function textoDoResumoDeMaterial(r: ResumoDeMaterial): string {
  return r.completos === r.total ? 'Material completo' : `Material: ${r.completos} de ${r.total} completos`
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
 * Para onde vai o que aguarda coleta (spec da Fase 3, §7.3, com a emenda da Fase 3D, §2.2). O nome
 * do pai vem do NÓ, não do destino: o `DestinoDto` só traz `paiId`, e a linha da fila e da tarefa já
 * carrega `paiDescricao`.
 */
export function descreverDestino(destino: DestinoDto, no: NoResumoDto): string {
  if (destino.tipo === 'ProximoPasso') return `${destino.setorNome} (passo ${destino.ordem})`
  if (destino.tipo === 'Expedicao') return 'Local de expedição'
  const pai = no.paiDescricao ?? 'o pai'
  if (destino.paiSemRoteiro) return `Montagem de ${pai} — o pai não tem Roteiro`
  return `Montagem de ${pai} em ${destino.setorNome}`
}

/**
 * Uma posição do saldo em palavras, para as pílulas da árvore (spec §6.3: "4 a iniciar · 6 no
 * Corte · 2 aguardando coleta"). "em Corte", e não "no Corte": o nome do Setor é livre, e
 * "no/na" dependeria do gênero dele ("na Solda"). O passo aparece sempre que a posição o tem,
 * porque o mesmo Setor pode estar duas vezes no Roteiro (regra 21).
 *
 * "Pronto, aguardando a montagem do pai" diz que são unidades distintas das que estão no Setor: o filho
 * terminou e espera o início do pai, que o consome. Sem isso, "1 em Solda" ao lado de "1 aguardando
 * montagem em Solda" se lia como a mesma unidade em dois estados.
 */
export function rotuloDoSaldo(s: SaldoDto): string {
  const q = formatarQuantidade(s.quantidade)
  switch (s.posicao) {
    case 'AIniciar': return `${q} a iniciar`
    case 'NoSetor': return `${q} em ${s.setorNome} (passo ${s.ordem})`
    case 'AguardandoColeta': return `${q} aguardando coleta em ${s.setorNome} (passo ${s.ordem})`
    case 'AguardandoMontagem': return `${quantidadeConcordada(s.quantidade, 'pronto', 'prontos')}, aguardando a montagem do pai em ${s.setorNome}`
    case 'AguardandoConserto': return `${q} aguardando levar ao conserto em ${s.setorNome}`
    case 'EmConserto': return `${q} em conserto em ${s.setorNome}`
    case 'ConsertoPronto': return `${q} pronto do conserto em ${s.setorNome}`
    case 'NaExpedicao': return `${q} no local de expedição`
    case 'Conforme': return `${quantidadeConcordada(s.quantidade, 'conforme', 'conformes')} no local de expedição`
    case 'NaoConforme': return `${quantidadeConcordada(s.quantidade, 'não conforme', 'não conformes')} no local de expedição`
    case 'AguardandoLiberacao': return `${q} aguardando liberação`
    case 'Liberada': return `${quantidadeConcordada(s.quantidade, 'liberada', 'liberadas')} pela Gestão`
    case 'Montado': return `${q} montados no pai`
    case 'Expedido': return quantidadeConcordada(s.quantidade, 'expedido', 'expedidos')
  }
}

/** Uma ponta de um movimento do livro, para o histórico do nó: "Corte (passo 1)", "a iniciar"… */
export function rotuloDoLocal(l: LocalDto): string {
  switch (l.posicao) {
    case 'AIniciar': return 'a iniciar'
    case 'NoSetor': return `${l.setorNome} (passo ${l.ordem})`
    case 'AguardandoColeta': return `aguardando coleta em ${l.setorNome} (passo ${l.ordem})`
    case 'AguardandoMontagem': return `aguardando a montagem do pai em ${l.setorNome}`
    case 'AguardandoConserto': return `aguardando levar ao conserto em ${l.setorNome}`
    case 'EmConserto': return `conserto em ${l.setorNome}`
    case 'ConsertoPronto': return `pronto do conserto em ${l.setorNome}`
    case 'NaExpedicao': return 'local de expedição'
    case 'Conforme': return 'local de expedição (conforme)'
    case 'NaoConforme': return 'local de expedição (não conforme)'
    case 'AguardandoLiberacao': return 'local de expedição (aguardando liberação)'
    case 'Liberada': return 'local de expedição (liberada pela Gestão)'
    case 'Montado': return 'montado no pai'
    case 'Expedido': return 'expedido'
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
    case 'Medicao': return 'Medição'
    case 'Conserto': return 'Conserto'
    case 'PedidoDeLiberacao': return 'Pedido de liberação'
    case 'Liberacao': return 'Liberação'
    case 'Recusa': return 'Recusa'
    case 'Expedicao': return 'Expedição'
  }
}

/**
 * O rótulo de um botão da fila: "Iniciar montagem" quando o Setor tem atividade, "Iniciar" quando não
 * tem (spec da Fase 3D, §2.3). O verbo vem fixo — início ou fim —, e a atividade só completa: um verbo
 * por Setor ("Soldar") não diria se o botão começa ou termina.
 */
export function rotuloDaAcao(verbo: 'Iniciar' | 'Terminar', atividade: string | null): string {
  const a = atividade?.trim()
  return a ? `${verbo} ${a}` : verbo
}

const NOME_DO_ESTORNAVEL: Record<Estornavel['tipo'], string> = {
  Inicio: 'início',
  Termino: 'término',
  Entrega: 'entrega',
  Montagem: 'início',
}

/** A descrição do registro: o quê e quanto; o início de um pai diz também que consumiu os filhos. */
function descricaoDoEstornavel(e: Estornavel): string {
  const consumo = e.tipo === 'Montagem' ? ' (com o consumo dos filhos)' : ''
  return `${NOME_DO_ESTORNAVEL[e.tipo]} de ${formatarQuantidade(e.quantidade)}${consumo}`
}

/** Uma linha da lista curta de estorno: "Término de 5 · Fulano · 28/09/2026 10:14". */
export function rotuloDoEstornavel(e: Estornavel): string {
  const descricao = descricaoDoEstornavel(e)
  return `${descricao[0].toUpperCase()}${descricao.slice(1)} · ${e.usuarioNome} · ${formatarDataHora(e.dataHora)}`
}

/** A pergunta da confirmação: o que se desfaz, e o que acontece depois (spec da Fase 3D, §2.4). */
export function mensagemDoEstorno(e: Estornavel): string {
  const artigo = e.tipo === 'Entrega' ? 'a' : 'o'
  const oQue = `Estornar ${artigo} ${descricaoDoEstornavel(e)}, `
    + `registrado por ${e.usuarioNome} em ${formatarDataHora(e.dataHora)}?`
  return e.tipo === 'Montagem'
    ? `${oQue} O pai volta para "a iniciar" e os filhos voltam a aguardar montagem, onde estavam antes; o estorno fica no histórico.`
    : `${oQue} O movimento inverso fica no histórico.`
}
