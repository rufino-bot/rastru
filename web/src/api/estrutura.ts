import { apiFetch } from './client'
import { ErroDeApi } from './erros'

export type NivelHierarquico = 'Peca' | 'Item'

export interface MaterialDoNo {
  materialId: number
  nome: string
  quantidade: number
}

export interface PassoDoRoteiro {
  setorId: number
  nome: string
  ordem: number
}

/**
 * Um nó da árvore de `EstruturaItem` (Peça, quando `nivelHierarquico === 'Peca'`, ou Item, quando
 * tem pai). `descricao` já vem RESOLVIDA pelo backend (regra 19: `EstruturaItem.Descricao` quando
 * não-nula, senão a descrição do Componente) — este cliente não faz esse fallback.
 */
export interface NoDaEstrutura {
  id: number
  componenteId: number | null
  codigoDoComponente: string | null
  descricao: string
  quantidade: number
  nivelHierarquico: NivelHierarquico
  requerRelatorioDimensional: boolean
  materiais: MaterialDoNo[]
  roteiro: PassoDoRoteiro[]
  filhos: NoDaEstrutura[]
}

/** Espelha `NovaPecaDto` (backend): cria a Peça (nó de topo) copiando a receita do catálogo. */
export interface NovaPeca {
  componenteId: number
  quantidade: number
  requerRelatorioDimensional: boolean
}

/**
 * Espelha `NovoFilhoDto`. `componenteId` nulo = nó ad-hoc — nesse caso `descricao` é obrigatória
 * (regra 19: sem Componente para herdar dela, um nó sem descrição chegaria anônimo à tela).
 */
export interface NovoFilho {
  componenteId: number | null
  descricao: string | null
  quantidade: number
}

/** Espelha `EdicaoDeNoDto`. `descricao` vazia/nula volta a herdar a do Componente (regra 19). */
export interface EdicaoDeNo {
  descricao: string | null
  quantidade: number
}

/**
 * Os quatro códigos de conflito (409) que `EstruturaController.Recusar` pode emitir para os
 * endpoints de escrita. Nem toda função pode devolver todos: `CriarPeca`/`AcrescentarFilho` só
 * emitem os três de `PlanejadorDeCopia` (ciclo/profundidade/tamanho); `ExcluirNo` só emite
 * `PedidoNaoAberto`; `EditarNo` nunca emite 409. O tipo fica genérico porque o formato do corpo é
 * o mesmo `{ erro, mensagem? }` nos quatro casos — restringir por função não ganharia nada.
 */
export type CodigoDeConflitoDeEstrutura =
  | 'CicloNaReceita'
  | 'EstruturaProfundaDemais'
  | 'EstruturaGrandeDemais'
  | 'PedidoNaoAberto'

/**
 * Corpo do 409 dos endpoints de escrita da árvore: `{ "erro": "<código>", "mensagem": "<frase>" }`.
 *
 * `mensagem` é OPCIONAL de propósito, e não por preguiça de tipagem: o backend a OMITE do JSON
 * quando não há frase (`EstruturaController.Recusar` — `detalhe is null ? new { erro } : ...`), o
 * que é o caso de `PedidoNaoAberto`. Nos três códigos de `PlanejadorDeCopia`, porém, `mensagem`
 * SEMPRE vem — e para `CicloNaReceita` ela nomeia o CAMINHO do ciclo (ex.: "A receita tem um
 * ciclo: 2 -> 3 -> 2. ..."), a única informação que permite ao operador consertar a receita: o
 * front não tem como reconstruir essa frase sozinho, porque não sabe qual foi o caminho percorrido.
 * Essa frase levou três rodadas de review e um fix pass inteiro para chegar até aqui (ver
 * `MontagemDeEstruturaUseCase.PlanejarCopiaDoCatalogo`) — um cliente que lesse só `erro` e
 * descartasse `mensagem` derrubaria essa cadeia no último elo.
 */
export interface ConflitoDeEstrutura {
  erro: CodigoDeConflitoDeEstrutura
  mensagem?: string
}

const codigosDeConflito: readonly CodigoDeConflitoDeEstrutura[] = [
  'CicloNaReceita', 'EstruturaProfundaDemais', 'EstruturaGrandeDemais', 'PedidoNaoAberto',
]

export function ehConflitoDeEstrutura(r: unknown): r is ConflitoDeEstrutura {
  if (typeof r !== 'object' || r === null) return false
  const codigo = (r as { erro?: unknown }).erro
  return typeof codigo === 'string'
    && (codigosDeConflito as readonly string[]).includes(codigo)
}

/**
 * Lê o corpo de uma resposta de escrita da árvore (POST/PUT), traduzindo o 409 para
 * `ConflitoDeEstrutura` e QUALQUER outro status não-ok (400, 403, 404 pelado, 500...) para
 * `ErroDeApi` lançado — mesmo padrão de `lerOuFalhar` em `cadastros.ts`. Um 409 fora do formato
 * esperado também lança, em vez de devolver o corpo cru como se fosse um `NoDaEstrutura`.
 */
async function lerNoOuConflito(resp: Response): Promise<NoDaEstrutura | ConflitoDeEstrutura> {
  if (resp.status === 409) {
    const corpo = await resp.json()
    if (ehConflitoDeEstrutura(corpo)) return corpo
    throw new ErroDeApi(409, 'Falha na requisição (409): formato de conflito inesperado.')
  }
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha na requisição (${resp.status}).`)
  return (await resp.json()) as NoDaEstrutura
}

/** `GET /agrupamentos/{id}/estrutura` — árvore completa de `EstruturaItem` do Agrupamento. */
export async function obterEstrutura(agrupamentoId: number): Promise<NoDaEstrutura[]> {
  const resp = await apiFetch(`/agrupamentos/${agrupamentoId}/estrutura`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar a estrutura (${resp.status}).`)
  return (await resp.json()) as NoDaEstrutura[]
}

/** `POST /agrupamentos/{id}/estrutura` — cria a Peça (nó de topo), copiando a receita do catálogo. */
export function criarPeca(
  agrupamentoId: number,
  p: NovaPeca,
): Promise<NoDaEstrutura | ConflitoDeEstrutura> {
  return apiFetch(`/agrupamentos/${agrupamentoId}/estrutura`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerNoOuConflito)
}

/** `POST /estrutura/{paiId}/filhos` — acrescenta um Item filho a um nó já existente. */
export function acrescentarFilho(
  paiId: number,
  f: NovoFilho,
): Promise<NoDaEstrutura | ConflitoDeEstrutura> {
  return apiFetch(`/estrutura/${paiId}/filhos`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(f),
  }).then(lerNoOuConflito)
}

/** `PUT /estrutura/{id}` — edita descrição/quantidade de um nó já existente. */
export function editarNo(
  id: number,
  e: EdicaoDeNo,
): Promise<NoDaEstrutura | ConflitoDeEstrutura> {
  return apiFetch(`/estrutura/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(e),
  }).then(lerNoOuConflito)
}

/** Desfechos do DELETE. A tela precisa distinguir para explicar o que houve. */
export type ResultadoDeEstrutura =
  | 'ok'
  | 'PedidoNaoAberto'
  | 'CicloNaReceita'
  | 'EstruturaProfundaDemais'
  | 'EstruturaGrandeDemais'
  | 'NaoEncontrado'

/**
 * `DELETE /estrutura/{id}` — apaga um nó e a subárvore inteira. O 404 é DESFECHO, não exceção
 * (mesmo padrão de `excluirAgrupamento`): a tela não pode tratar "o nó já não existe" lançando uma
 * exceção que o catch dela nunca esperou.
 *
 * Na prática `ExcluirNo` só emite `PedidoNaoAberto` no 409 (não chama `PlanejadorDeCopia`), mas a
 * tradução reaproveita `ehConflitoDeEstrutura`/`ConflitoDeEstrutura` em vez de comparar o literal
 * à mão — um único lugar decide o que é um código de conflito válido.
 */
export async function excluirNo(id: number): Promise<ResultadoDeEstrutura> {
  const resp = await apiFetch(`/estrutura/${id}`, { method: 'DELETE' })
  if (resp.status === 204) return 'ok'
  if (resp.status === 404) return 'NaoEncontrado'
  if (resp.status === 409) {
    const corpo = await resp.json()
    if (ehConflitoDeEstrutura(corpo)) return corpo.erro
    throw new ErroDeApi(409, 'Falha na exclusão (409): formato de conflito inesperado.')
  }
  throw new ErroDeApi(resp.status, `Falha ao excluir o nó (${resp.status}).`)
}
