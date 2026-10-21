import { apiFetch } from './client'
import { estornarMovimentacao, falhar, ler, type MovimentacaoDto } from './execucao'
import { avisarQueOLivroMudou } from './sinalDoLivro'
import type { PausaResumoDto } from './cadastros'

/**
 * Cliente da aferição, da expedição e da liberação (Fase 5A). Os tipos espelham o contrato JSON da seção
 * "Contrato JSON novo" do plano de backend da Fase 5A, conferido contra os DTOs de `ExpedicaoDtos.cs` — é o
 * código do servidor a fonte, não este arquivo: campo novo nasce lá primeiro.
 *
 * Toda recusa vira `ErroDeApi` lançado, com `codigo` (o campo `erro` do corpo) e `detalhe` (o campo
 * `mensagem`), pelos mesmos `ler` e `falhar` da execução: `mensagemDeErro` é o mesmo.
 *
 * Toda escrita avisa o contador de Tarefas (`avisarQueOLivroMudou`) porque mexe no livro de movimentações,
 * menos a aferição e o estorno dela: a aferição é um registro à parte (`dbo.RelatorioDimensional`), não move
 * unidade de posição nenhuma.
 */

/** A situação da aferição da Peça (tabela 3.2 da spec da Fase 5A). */
export type SituacaoDaAfericao = 'SemRelatorio' | 'Pendente' | 'Conforme' | 'NaoConforme'

export type ResultadoDaAfericao = 'Conforme' | 'NaoConforme'

/** Uma aferição da Peça. `estornada`: existe um estorno dela. `estornavel`: quem lê pode estorná-la agora (C7). */
export interface AfericaoDto {
  id: number
  resultado: ResultadoDaAfericao
  /** ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`, nunca com `Date`. */
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  estornavel: boolean
}

/** Uma contagem: as duas linhas `Medicao` do livro que apontam para ela, numa só (C17). */
export interface ContagemDto {
  id: number
  conformes: number
  naoConformes: number
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  estornavel: boolean
}

/**
 * O pedido de liberação (`tipo: Pedido`, `quantidade` a pedida, `texto` a justificativa) ou a resposta da Gestão
 * (`tipo: Resposta`, `pedidoDeLiberacaoId` o pedido, `quantidade` a liberada, `texto` a observação).
 */
export interface LiberacaoDto {
  id: number
  tipo: 'Pedido' | 'Resposta'
  /** Só na `Resposta`: o pedido que ela responde. */
  pedidoDeLiberacaoId: number | null
  estruturaItemId: number
  quantidade: number
  texto: string | null
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  estornavel: boolean
}

/** O pedido de liberação em aberto da Peça (C18: no máximo um), como a lista da expedição o mostra. */
export interface LiberacaoPendenteDaPecaDto {
  id: number
  quantidade: number
  justificativa: string
  dataHora: string
  usuarioNome: string
}

/** Um item de `GET /liberacoes/pendentes`: o pedido que a Gestão ainda não respondeu, com a Peça e o Pedido. */
export interface LiberacaoPendenteDto {
  id: number
  quantidade: number
  justificativa: string
  dataHora: string
  usuarioNome: string
  peca: { id: number; descricao: string; codigoDoComponente: string | null }
  pedido: { id: number; numero: string; cliente: string; dataEntrega: string; atrasado: boolean }
}

/** A quantidade da Peça por posição do fim do caminho. `emConserto` soma as três posições do conserto. */
export interface PosicoesDaPecaDto {
  naExpedicao: number
  conforme: number
  naoConforme: number
  aguardandoLiberacao: number
  liberada: number
  emConserto: number
  expedido: number
}

/** Quanto da Peça está numa posição do conserto de um Setor. */
export interface ConsertoEmCursoDto {
  setorId: number
  setorNome: string
  posicao: 'AguardandoConserto' | 'EmConserto' | 'ConsertoPronto'
  quantidade: number
}

/** Uma Peça da lista da expedição. `afericao` é a válida, ou `null`; `expedivel` é o que a remessa pode tirar (C10). */
export interface PecaNaExpedicaoDto {
  id: number
  /** Já com o fallback da regra 19. */
  descricao: string
  codigoDoComponente: string | null
  agrupamentoId: number
  agrupamentoCodigo: string
  quantidade: number
  requerRelatorioDimensional: boolean
  situacao: SituacaoDaAfericao
  afericao: AfericaoDto | null
  posicoes: PosicoesDaPecaDto
  consertos: ConsertoEmCursoDto[]
  liberacaoPendente: LiberacaoPendenteDaPecaDto | null
  expedivel: number
}

/** Um Pedido da lista da expedição com as Peças dele que têm algo no local de expedição ou em conserto. */
export interface PedidoNaExpedicaoDto {
  id: number
  numero: string
  cliente: string
  /** `aaaa-mm-dd`, sem hora nem fuso. Exibir com `formatarData`. */
  dataEntrega: string
  atrasado: boolean
  /** `null` quando o Pedido não está pausado. */
  pausa: PausaResumoDto | null
  pecas: PecaNaExpedicaoDto[]
}

/** `GET /expedicao`: os Pedidos abertos com Peça no fim do caminho, do prazo mais próximo ao mais distante. */
export interface ExpedicaoDto {
  pedidos: PedidoNaExpedicaoDto[]
}

/** De onde a remessa tirou a quantidade (C10): tudo do local de expedição menos `AguardandoLiberacao`. */
export type OrigemDaLinhaDaRemessa = 'NaExpedicao' | 'Conforme' | 'NaoConforme' | 'Liberada'

/** Uma linha `Expedicao` da remessa. `pedidoConcluido` é o status do Pedido AGORA, não o da hora da remessa. */
export interface LinhaDaRemessaDto {
  pecaId: number
  pecaDescricao: string
  codigoDoComponente: string | null
  pedidoId: number
  pedidoNumero: string
  pedidoConcluido: boolean
  origem: OrigemDaLinhaDaRemessa
  quantidade: number
}

export interface RemessaDto {
  id: number
  dataHora: string
  usuarioId: number
  usuarioNome: string
  estornada: boolean
  /** Nunca de Pedido `Cancelado` (C13). */
  estornavel: boolean
  linhas: LinhaDaRemessaDto[]
}

export type TipoDeAviso = 'SemAfericao' | 'SemMedicao' | 'NaoConformeSemLiberacao'

/** Um aviso de expedir (D8) de uma Peça da remessa, e quantas unidades saíram assim. */
export interface AvisoDaPecaDto {
  pecaId: number
  tipo: TipoDeAviso
  quantidade: number
}

/** O `201` de `POST /remessas`: a remessa gravada e os avisos, calculados no servidor. */
export interface ResultadoDaRemessaDto {
  remessa: RemessaDto
  avisos: AvisoDaPecaDto[]
}

export type TipoDeRegistroDaExpedicao =
  | 'Afericao'
  | 'Contagem'
  | 'Conserto'
  | 'PedidoDeLiberacao'
  | 'RespostaDeLiberacao'
  | 'Remessa'

/**
 * Um registro do histórico da Peça. Os campos que o `tipo` não usa vão `null`. O `id` é o do registro do tipo
 * (aferição, contagem, liberação, remessa) ou, no `Conserto`, o da linha do livro, que é o que o estorno
 * genérico recebe. A `Remessa` é uma só por remessa, com a soma das linhas da Peça em `quantidade`, e nunca
 * `estornavel` aqui: ela se estorna em "Remessas recentes".
 */
export interface RegistroDaExpedicaoDto {
  tipo: TipoDeRegistroDaExpedicao
  id: number
  dataHora: string
  usuarioId: number
  usuarioNome: string
  /** Só na `Afericao`. */
  resultado: ResultadoDaAfericao | null
  /** Só na `Contagem`. */
  conformes: number | null
  naoConformes: number | null
  /** No `Conserto`, nos dois pedidos de liberação e na `Remessa`. */
  quantidade: number | null
  /** Só no `Conserto`. */
  setorNome: string | null
  /** A justificativa do pedido ou a observação da resposta. */
  texto: string | null
  estornada: boolean
  estornavel: boolean
}

/**
 * Toda escrita da expedição passa por aqui. Com `avisa`, é a mesma regra de `enviar` em `execucao.ts`: o
 * aviso sai no sucesso e no 409 (o que estava na tela ficou velho), ANTES de ler o corpo — a escrita já foi
 * gravada, e um corpo ilegível não pode esconder isso do contador.
 */
async function escrever<T>(caminho: string, corpo: unknown, oQue: string, avisa: boolean): Promise<T> {
  const init: RequestInit = { method: 'POST' }
  if (corpo !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(corpo)
  }
  const resp = await apiFetch(caminho, init)
  if (avisa && (resp.ok || resp.status === 409)) avisarQueOLivroMudou()
  if (!resp.ok) return falhar(resp, oQue)
  return (await resp.json()) as T
}

export function obterExpedicao(): Promise<ExpedicaoDto> {
  return ler('/expedicao', 'carregar a expedição')
}

/** Mais recente primeiro. */
export function obterHistoricoDaExpedicao(pecaId: number): Promise<RegistroDaExpedicaoDto[]> {
  return ler(`/estrutura/${pecaId}/expedicao`, 'carregar o histórico da expedição')
}

/** Dos últimos 30 dias, mais recente primeiro. */
export function listarRemessasRecentes(): Promise<RemessaDto[]> {
  return ler('/remessas/recentes', 'carregar as remessas')
}

/** O mais antigo primeiro. */
export function listarLiberacoesPendentes(): Promise<LiberacaoPendenteDto[]> {
  return ler('/liberacoes/pendentes', 'carregar as liberações')
}

/** Uma unidade medida decide o lote (regra 36 de `specs/01`). Não mexe no livro. */
export function aferir(pecaId: number, resultado: ResultadoDaAfericao): Promise<AfericaoDto> {
  return escrever(`/estrutura/${pecaId}/afericoes`, { resultado }, 'registrar a aferição', false)
}

export function contar(pecaId: number, conformes: number, naoConformes: number): Promise<ContagemDto> {
  return escrever(`/estrutura/${pecaId}/contagens`, { conformes, naoConformes }, 'registrar a contagem', true)
}

export function mandarParaConserto(pecaId: number, setorId: number, quantidade: number): Promise<MovimentacaoDto> {
  return escrever(`/estrutura/${pecaId}/consertos`, { setorId, quantidade }, 'mandar para conserto', true)
}

export function pedirLiberacao(pecaId: number, quantidade: number, justificativa: string): Promise<LiberacaoDto> {
  return escrever(
    `/estrutura/${pecaId}/pedidos-de-liberacao`, { quantidade, justificativa }, 'pedir a liberação', true,
  )
}

/** `quantidadeLiberada` zero é a recusa. */
export function responderLiberacao(
  id: number, quantidadeLiberada: number, observacao: string | null,
): Promise<LiberacaoDto> {
  return escrever(`/liberacoes/${id}/resposta`, { quantidadeLiberada, observacao }, 'responder a liberação', true)
}

/** Uma ou mais Peças, de um ou mais Pedidos, numa remessa só: tudo ou nada. */
export function expedir(itens: { estruturaItemId: number; quantidade: number }[]): Promise<ResultadoDaRemessaDto> {
  return escrever('/remessas', { itens }, 'expedir', true)
}

/** Devolve a aferição original, já com `estornada: true`. */
export function estornarAfericao(id: number): Promise<AfericaoDto> {
  return escrever(`/afericoes/${id}/estorno`, undefined, 'estornar a aferição', false)
}

export function estornarContagem(id: number): Promise<MovimentacaoDto[]> {
  return escrever(`/contagens/${id}/estorno`, undefined, 'estornar a contagem', true)
}

/** Serve ao pedido e à resposta: cada um se estorna pela rota do registro dele. */
export function estornarLiberacao(id: number): Promise<MovimentacaoDto[]> {
  return escrever(`/liberacoes/${id}/estorno`, undefined, 'estornar a liberação', true)
}

export function estornarRemessa(id: number): Promise<MovimentacaoDto[]> {
  return escrever(`/remessas/${id}/estorno`, undefined, 'estornar a remessa', true)
}

/**
 * O estorno certo para o registro do histórico, pelo tipo. O `Conserto` não tem rota própria: o `id` dele é o
 * da linha do livro, e o estorno é o genérico da movimentação.
 */
export function estornarRegistroDaExpedicao(r: RegistroDaExpedicaoDto): Promise<unknown> {
  switch (r.tipo) {
    case 'Afericao': return estornarAfericao(r.id)
    case 'Contagem': return estornarContagem(r.id)
    case 'Conserto': return estornarMovimentacao(r.id)
    case 'PedidoDeLiberacao':
    case 'RespostaDeLiberacao': return estornarLiberacao(r.id)
    case 'Remessa': return estornarRemessa(r.id)
  }
}
