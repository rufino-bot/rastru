import { apiFetch } from './client'
import { falhar, ler } from './execucao'
import type { PausaResumoDto } from './cadastros'

/**
 * Cliente da separação de material (Fase 4, regra 34). Os tipos espelham o contrato JSON da seção
 * "Separação de materiais" de `specs/05-api-endpoints.md`, campo a campo — é ela a fonte, não este arquivo.
 *
 * Toda recusa vira `ErroDeApi` lançado, com `codigo` (o campo `erro` do corpo) e `detalhe` (o campo
 * `mensagem`), pelos mesmos `ler` e `falhar` da execução: `mensagemDeErro` é o mesmo.
 *
 * Nenhuma escrita daqui chama `avisarQueOLivroMudou`: a separação não mexe no livro de movimentações,
 * então o contador de Tarefas não tem o que recontar.
 */

/** Onde o Material fica (regra 4); `null` no material = almoxarifado. */
export interface SetorDeEstoque {
  setorId: number
  nome: string
}

/**
 * Um material do nó com a conta da regra 34. O que saiu do plano e tem separação vem com
 * `noPlanejado: false`, `planejado: 0`, `pendencia: 0` e `acimaDoPlanejado: false`. A `pendencia` só é maior
 * que zero no material do almoxarifado que está no planejado.
 */
export interface MaterialDoNoComSeparacao {
  materialId: number
  codigo: string
  descricao: string
  unidadeMedida: string
  ficaEm: SetorDeEstoque | null
  planejado: number
  separado: number
  pendencia: number
  noPlanejado: boolean
  /** Verdadeiro só no material do plano com `separado` maior que `planejado`. */
  acimaDoPlanejado: boolean
}

export interface MateriaisDoNoDto {
  estruturaItemId: number
  materiais: MaterialDoNoComSeparacao[]
}

export interface LinhaDeMaterial {
  materialId: number
  quantidade: number
}

export interface SeparacaoDto {
  id: number
  tipo: 'Separacao' | 'Estorno'
  materialId: number
  materialCodigo: string
  materialDescricao: string
  unidadeMedida: string
  /** Sempre positiva; o `Estorno` subtrai pelo `tipo`. */
  quantidade: number
  /** ISO 8601 com offset -03:00 — mostrar com `formatarDataHora`, nunca com `Date`. */
  dataHora: string
  usuarioId: number
  usuarioNome: string
  /** Só no `Estorno`: a linha que ele desfaz. */
  estornoDeId: number | null
  /** Calculado para quem pede: uma `Separacao` sem estorno que o leitor pode estornar. */
  estornavel: boolean
}

export interface ResultadoDaSeparacaoDto {
  separacoes: SeparacaoDto[]
  /** Só os materiais que a separação tocou. */
  materiais: MaterialDoNoComSeparacao[]
}

/** Um nó do lote de separação, com as linhas dele como no `POST` individual. */
export interface NoDoLoteDeSeparacao {
  estruturaItemId: number
  linhas: LinhaDeMaterial[]
}

/** Uma entrada da resposta do lote: o nó e o mesmo conteúdo da resposta individual. */
export interface ResultadoDaSeparacaoDoNo extends ResultadoDaSeparacaoDto {
  estruturaItemId: number
}

export interface MaterialPendente {
  materialId: number
  codigo: string
  descricao: string
  unidadeMedida: string
  planejado: number
  separado: number
  pendencia: number
}

/** Um nó com material do almoxarifado pendente, na lista do Separador. `destino` nulo = nó sem Roteiro. */
export interface PendenciaDeSeparacaoDto {
  no: {
    id: number
    descricao: string
    codigoDoComponente: string | null
    agrupamentoId: number
    agrupamentoCodigo: string
  }
  pedido: {
    id: number
    numero: string
    cliente: string
    /** `yyyy-MM-dd`. */
    dataEntrega: string
    atrasado: boolean
    pausa: PausaResumoDto | null
  }
  /** O Setor do primeiro passo do Roteiro do nó (regra 5). */
  destino: { setorId: number; nome: string } | null
  materiais: MaterialPendente[]
}

async function escrever<T>(caminho: string, metodo: 'POST' | 'PUT', corpo: unknown, oQue: string): Promise<T> {
  const init: RequestInit = { method: metodo }
  if (corpo !== undefined) {
    init.headers = { 'Content-Type': 'application/json' }
    init.body = JSON.stringify(corpo)
  }
  const resp = await apiFetch(caminho, init)
  if (!resp.ok) return falhar(resp, oQue)
  return (await resp.json()) as T
}

export function obterMateriaisDoNo(noId: number): Promise<MateriaisDoNoDto> {
  return ler(`/estrutura/${noId}/materiais`, 'carregar os materiais do item')
}

/** Substitui a lista inteira; `[]` limpa o planejado. */
export function substituirMateriaisDoNo(noId: number, linhas: LinhaDeMaterial[]): Promise<MateriaisDoNoDto> {
  return escrever(`/estrutura/${noId}/materiais`, 'PUT', { linhas }, 'salvar os materiais do item')
}

/** Tudo ou nada: se uma linha for recusada, nenhuma é gravada. */
export function separar(noId: number, linhas: LinhaDeMaterial[]): Promise<ResultadoDaSeparacaoDto> {
  return escrever(`/estrutura/${noId}/separacoes`, 'POST', { linhas }, 'registrar a separação')
}

/**
 * Vários nós num `POST` só, tudo ou nada: se um nó for recusado, nada é gravado, e a frase do servidor traz
 * o nome dele. A resposta vem na ordem do corpo.
 */
export function separarEmLote(nos: NoDoLoteDeSeparacao[]): Promise<ResultadoDaSeparacaoDoNo[]> {
  return escrever('/separacoes', 'POST', { nos }, 'registrar a separação')
}

export function obterHistoricoDeSeparacoes(noId: number): Promise<SeparacaoDto[]> {
  return ler(`/estrutura/${noId}/separacoes`, 'carregar as separações')
}

export function estornarSeparacao(id: number): Promise<SeparacaoDto> {
  return escrever(`/separacoes/${id}/estorno`, 'POST', undefined, 'estornar a separação')
}

export function listarPendenciasDeSeparacao(): Promise<PendenciaDeSeparacaoDto[]> {
  return ler('/separacoes/pendencias', 'carregar a lista de separação')
}
