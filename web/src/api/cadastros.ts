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
