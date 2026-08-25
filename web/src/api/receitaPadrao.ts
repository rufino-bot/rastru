import { apiFetch } from './client'
import { ErroDeApi } from './erros'

export interface FilhoPadraoDto {
  id: number
  componenteFilhoId: number
  codigo: string
  descricao: string
  quantidadePadrao: number
}

export interface MaterialPadraoDto {
  id: number
  materialId: number
  codigo: string
  descricao: string
  unidadeMedida: string
  quantidadePadrao: number
}

export interface RoteiroPadraoDto {
  id: number
  setorId: number
  nome: string
  ordem: number
}

export interface LinhaDeFilho {
  componenteFilhoId: number
  quantidadePadrao: number
}

export interface LinhaDeMaterial {
  materialId: number
  quantidadePadrao: number
}

/**
 * Só `setorId`: a `ordem` é a POSIÇÃO no array e quem a atribui é o servidor. Mandar `ordem` daqui
 * reabriria buraco e duplicata na sequência.
 */
export interface LinhaDeRoteiro {
  setorId: number
}

/**
 * Lê o campo `erro` do corpo, que é o formato de erro do backend
 * (`BadRequest(new { erro = resultado.Erro })`). Devolve `undefined` em vez de lançar quando o
 * corpo não é o esperado: um erro ao ler a explicação do erro não pode substituir o erro original,
 * senão a tela mostra "SyntaxError: Unexpected token" no lugar de "não foi possível salvar".
 */
async function detalheDoCorpo(resp: Response): Promise<string | undefined> {
  try {
    const corpo: unknown = await resp.json()
    if (corpo && typeof corpo === 'object' && 'erro' in corpo) {
      const erro = (corpo as { erro: unknown }).erro
      if (typeof erro === 'string' && erro.trim() !== '') return erro
    }
  } catch {
    // Corpo vazio ou não-JSON: sem detalhe, e o chamador cai no texto da tela.
  }
  return undefined
}

/**
 * O `detalhe` é lido em TODA resposta de erro, e não só no 400: o servidor pode nomear o motivo em
 * qualquer status, e `mensagemDeErro` já decide o que fazer com ele. O caso que motivou isto é o
 * 400 de ciclo — ver `ErroDeApi.detalhe` e a spec §1.3 da Fase 1C.
 */
async function ler<T>(resp: Response, oQue: string): Promise<T> {
  if (!resp.ok) {
    const detalhe = await detalheDoCorpo(resp)
    throw new ErroDeApi(resp.status, `Falha ao ${oQue} (${resp.status}).`, detalhe)
  }
  return (await resp.json()) as T
}

/**
 * O envelope `{ linhas }` não é decorativo: corpo sem o campo é 400 no backend, de propósito —
 * lista VAZIA significa "apague a receita", e campo AUSENTE significa requisição malformada. Se o
 * cliente achatasse o array direto no corpo, `POST []` viraria ambíguo.
 */
function gravar<T>(caminho: string, linhas: unknown[], oQue: string): Promise<T> {
  return apiFetch(caminho, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ linhas }),
  }).then((r) => ler<T>(r, oQue))
}

export function listarFilhosPadrao(componenteId: number): Promise<FilhoPadraoDto[]> {
  return apiFetch(`/componentes/${componenteId}/filhos-padrao`)
    .then((r) => ler<FilhoPadraoDto[]>(r, 'carregar os componentes filhos'))
}

export function salvarFilhosPadrao(
  componenteId: number,
  linhas: LinhaDeFilho[],
): Promise<FilhoPadraoDto[]> {
  return gravar(`/componentes/${componenteId}/filhos-padrao`, linhas, 'salvar os componentes filhos')
}

export function listarMateriaisPadrao(componenteId: number): Promise<MaterialPadraoDto[]> {
  return apiFetch(`/componentes/${componenteId}/materiais-padrao`)
    .then((r) => ler<MaterialPadraoDto[]>(r, 'carregar os materiais'))
}

export function salvarMateriaisPadrao(
  componenteId: number,
  linhas: LinhaDeMaterial[],
): Promise<MaterialPadraoDto[]> {
  return gravar(`/componentes/${componenteId}/materiais-padrao`, linhas, 'salvar os materiais')
}

export function listarRoteiroPadrao(componenteId: number): Promise<RoteiroPadraoDto[]> {
  return apiFetch(`/componentes/${componenteId}/roteiro-padrao`)
    .then((r) => ler<RoteiroPadraoDto[]>(r, 'carregar o roteiro'))
}

export function salvarRoteiroPadrao(
  componenteId: number,
  linhas: LinhaDeRoteiro[],
): Promise<RoteiroPadraoDto[]> {
  return gravar(`/componentes/${componenteId}/roteiro-padrao`, linhas, 'salvar o roteiro')
}
