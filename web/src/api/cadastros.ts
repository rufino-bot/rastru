import { apiFetch } from './client'

export interface SetorDto {
  id: number
  nome: string
  ativo: boolean
}

/** Corpo do 409 de duplicidade. `existeInativo` habilita o botão de reativar. */
export interface ConflitoDeCadastro {
  erro: 'ValorDuplicado'
  campo: string
  existeInativo: boolean
  idExistente: number
}

export function ehConflito(r: unknown): r is ConflitoDeCadastro {
  return typeof r === 'object' && r !== null && (r as ConflitoDeCadastro).erro === 'ValorDuplicado'
}

/**
 * Só serve para endpoints `MontarConflito`/`TraduzirFalha`-backed (ex.: `POST /setores`), cujo
 * único 409 possível é o formato `ValorDuplicado`. Endpoints `TraduzirResultado`-backed (ex.:
 * `PATCH /{id}/ativo`) devolvem 409 num formato pelado (`{ erro: "<código>" }`) que não é
 * `ConflitoDeCadastro` — para esses, trate a resposta com `if (!resp.ok) throw`, como
 * `definirAtivoSetor` já faz.
 */
async function lerOuFalhar<T>(resp: Response): Promise<T | ConflitoDeCadastro> {
  if (resp.status === 409) {
    const corpo = await resp.json()
    if (ehConflito(corpo)) return corpo
    throw new Error('Falha na requisição (409): formato de conflito inesperado.')
  }
  if (!resp.ok) throw new Error(`Falha na requisição (${resp.status}).`)
  return (await resp.json()) as T
}

export async function listarSetores(incluirInativos: boolean): Promise<SetorDto[]> {
  const resp = await apiFetch(`/setores?incluirInativos=${incluirInativos}`)
  if (!resp.ok) throw new Error(`Falha ao listar setores (${resp.status}).`)
  return (await resp.json()) as SetorDto[]
}

export function criarSetor(nome: string): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch('/setores', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ nome }),
  }).then(lerOuFalhar<SetorDto>)
}

export async function definirAtivoSetor(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/setores/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new Error(`Falha ao alterar o setor (${resp.status}).`)
}

export interface MaterialDto {
  id: number
  codigo: string
  descricao: string
  unidadeMedida: string
  ativo: boolean
}

/**
 * `unidadeMedida` e texto livre: `dbo.Material.UnidadeMedida` e NVARCHAR(10) sem `CHECK`, e o
 * backend nao impoe lista fechada. Nada de enum aqui — seria restricao que o schema nao tem.
 */
export interface NovoMaterial {
  codigo: string
  descricao: string
  unidadeMedida: string
}

export async function listarMateriais(incluirInativos: boolean): Promise<MaterialDto[]> {
  const resp = await apiFetch(`/materiais?incluirInativos=${incluirInativos}`)
  if (!resp.ok) throw new Error(`Falha ao listar materiais (${resp.status}).`)
  return (await resp.json()) as MaterialDto[]
}

/** O unico 409 possivel aqui e `ValorDuplicado` sobre `codigo` (UQ_Material_Codigo). */
export function criarMaterial(m: NovoMaterial): Promise<MaterialDto | ConflitoDeCadastro> {
  return apiFetch('/materiais', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(m),
  }).then(lerOuFalhar<MaterialDto>)
}

export async function definirAtivoMaterial(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/materiais/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new Error(`Falha ao alterar o material (${resp.status}).`)
}

export interface PedidoDto {
  id: number
  numero: string
  cliente: string
  tipo: string
  status: string
  /** ISO 8601 com offset -03:00 — a API ja converteu (HorarioDeBrasiliaJsonConverter). */
  dataAbertura: string
  criadoPorUsuarioId: number
}

export interface NovoPedido {
  numero: string
  cliente: string
}

/**
 * Formata o ISO que a API mandou SEM passar por `Date`: a data ja vem em GMT-3, e
 * `new Date(x).toLocaleString()` a reconverteria para o fuso do aparelho — num tablet fora do
 * fuso da fabrica o horario apareceria deslocado.
 */
export function formatarDataHora(isoComOffset: string): string {
  const [data, hora] = isoComOffset.split('T')
  const [ano, mes, dia] = data.split('-')
  return `${dia}/${mes}/${ano} ${hora.slice(0, 5)}`
}

export async function listarPedidos(): Promise<PedidoDto[]> {
  const resp = await apiFetch('/pedidos')
  if (!resp.ok) throw new Error(`Falha ao listar pedidos (${resp.status}).`)
  return (await resp.json()) as PedidoDto[]
}

export async function obterPedido(id: number): Promise<PedidoDto> {
  const resp = await apiFetch(`/pedidos/${id}`)
  if (!resp.ok) throw new Error(`Falha ao carregar o pedido (${resp.status}).`)
  return (await resp.json()) as PedidoDto
}

export function criarPedido(p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro> {
  return apiFetch('/pedidos', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerOuFalhar<PedidoDto>)
}

// Sem editarPedido aqui, de proposito: o PUT /pedidos/{id} existe e esta testado no backend
// (Task 8), mas nenhuma tela de 1A tem UI de edicao — exportar a funcao sem chamador seria
// codigo morto. Ela nasce junto com a tela que a usar.

export interface AgrupamentoDto {
  id: number
  pedidoId: number
  codigo: string
  tipo: string
  /** ISO 8601 com offset -03:00, como `PedidoDto.dataAbertura`. */
  criadoEm: string
  criadoPorUsuarioId: number
}

export interface NovoAgrupamento {
  codigo: string
  tipo: 'Kit' | 'Avulso'
}

/** Desfechos do DELETE. A tela precisa distinguir os dois 409 para explicar o que houve. */
export type ResultadoExclusao = 'ok' | 'AgrupamentoNaoVazio' | 'PedidoNaoAberto' | 'NaoEncontrado'

export async function listarAgrupamentos(pedidoId: number): Promise<AgrupamentoDto[]> {
  const resp = await apiFetch(`/pedidos/${pedidoId}/agrupamentos`)
  if (!resp.ok) throw new Error(`Falha ao listar agrupamentos (${resp.status}).`)
  return (await resp.json()) as AgrupamentoDto[]
}

export function criarAgrupamento(
  pedidoId: number,
  a: NovoAgrupamento,
): Promise<AgrupamentoDto | ConflitoDeCadastro> {
  return apiFetch(`/pedidos/${pedidoId}/agrupamentos`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(a),
  }).then(lerOuFalhar<AgrupamentoDto>)
}

/**
 * DELETE /agrupamentos/{id} e TraduzirResultado-backed: o 409 chega pelado (`{ erro: "<codigo>" }`,
 * ver F5), nao no formato ConflitoDeCadastro — por isso a traducao e feita aqui, nao via
 * lerOuFalhar. A ordem das guardas do Excluir no backend e existe -> Pedido Aberto -> vazio, entao
 * PedidoNaoAberto e o codigo que chega primeiro na pratica: um Agrupamento com estrutura num
 * Pedido nao Aberto responde PedidoNaoAberto, nunca AgrupamentoNaoVazio.
 */
export async function excluirAgrupamento(id: number): Promise<ResultadoExclusao> {
  const resp = await apiFetch(`/agrupamentos/${id}`, { method: 'DELETE' })
  if (resp.status === 204) return 'ok'
  if (resp.status === 404) return 'NaoEncontrado'
  if (resp.status === 409) {
    const corpo = (await resp.json()) as { erro?: string }
    return corpo.erro === 'PedidoNaoAberto' ? 'PedidoNaoAberto' : 'AgrupamentoNaoVazio'
  }
  throw new Error(`Falha ao excluir o agrupamento (${resp.status}).`)
}
