import { apiFetch } from './client'
import { ErroDeApi } from './erros'
import type { NoDaEstrutura } from './estrutura'

/** `'Catalogo'`: o código divergente fica com a receita do catálogo; `'Importada'`: com a do BOM. */
export type EscolhaDeReceita = 'Catalogo' | 'Importada'

export type PendenciaDoNo = 'Novo' | 'Inativo' | 'Divergente' | 'SemSolido'

/** Um nó da árvore expandida da conferência (espelha `NoDaImportacaoDto`). */
export interface NoDaImportacaoDto {
  /** Registro do rascunho que o nó representa; `null` quando o nó veio só do catálogo. */
  registroId: number | null
  componenteId: number | null
  /** Linha da receita lida que pendurou o nó; é por ela que a tela corrige a quantidade. */
  filhoId: number | null
  codigo: string
  descricao: string
  quantidadePorPai: number | null
  origem: 'Bom' | 'Catalogo'
  pendencias: PendenciaDoNo[]
  filhos: NoDaImportacaoDto[]
}

export interface LinhaDoComparativoDto {
  codigo: string
  descricao: string
  noCatalogo: number | null
  noBom: number | null
  situacao: 'Igual' | 'QuantidadeMuda' | 'Entra' | 'Sai'
}

export interface EfeitoDto {
  retira: number
  traz: number
}

/**
 * A situação de um registro do rascunho (um por código distinto). `tipo`, `ativo`,
 * `codigoDoCatalogo` e `descricaoDoCatalogo` são do Componente casado, e nulos para o registro novo.
 */
export interface SituacaoDoComponenteDto {
  registroId: number
  codigoLido: string | null
  descricaoLida: string
  componenteId: number | null
  codigoDoCatalogo: string | null
  descricaoDoCatalogo: string | null
  tipo: string | null
  ativo: boolean | null
  temSolido: boolean
  temSolidoPendente: boolean
  nomeDoSolido: string | null
  tamanhoDoSolidoEmBytes: number | null
  codigoNovo: string | null
  descricaoNova: string | null
  tipoNovo: string | null
  divergente: boolean
  escolhaDeReceita: EscolhaDeReceita | null
  comparativo: LinhaDoComparativoDto[]
  efeitoDeManterCatalogo: EfeitoDto | null
  naArvoreFinal: boolean
}

export type TipoDeBloqueio =
  | 'SemSolido'
  | 'DivergenciaSemEscolha'
  | 'CodigoVazio'
  | 'CodigoJaExiste'
  | 'QuantidadeDaPecaAusente'
  | 'QuantidadeForaDaFaixa'
  | 'CicloNaReceita'
  | 'EstruturaProfundaDemais'
  | 'EstruturaGrandeDemais'

/** Um motivo que impede a confirmação; `mensagem` já é texto de tela, em português. */
export interface BloqueioDto {
  tipo: TipoDeBloqueio
  registroId: number | null
  componenteId: number | null
  mensagem: string
}

/**
 * O rascunho inteiro como a tela o lê. O estado (bloqueios, pendências) é calculado a cada leitura.
 * `versao` é o `ROWVERSION` do cabeçalho em base64 (decisão P14 do plano do import): toda escrita a
 * devolve, e versão velha é 409. `raiz` é nula quando a expansão é recusada — o motivo está em
 * `bloqueios`.
 */
export interface ImportacaoDto {
  id: number
  agrupamentoId: number
  nomeDoArquivo: string
  criadoPor: string
  /** ISO 8601 com offset -03:00, como os demais DTOs. */
  criadoEm: string
  atualizadoEm: string
  versao: string
  quantidadeDaPeca: number | null
  requerRelatorioDimensional: boolean
  raiz: NoDaImportacaoDto | null
  componentes: SituacaoDoComponenteDto[]
  bloqueios: BloqueioDto[]
}

/** Uma linha da lista de rascunhos de um Agrupamento. */
export interface ResumoDeImportacaoDto {
  id: number
  nomeDoArquivo: string
  criadoPor: string
  criadoEm: string
  atualizadoEm: string
}

/**
 * O que o corpo de `alterarComponente` carrega. `componenteId` preenchido casa o registro com aquele
 * Componente (e descarta os dados do "criar novo"); `null` o deixa "criar novo", e aí `codigoNovo`,
 * `descricaoNova` e `tipoNovo`, quando preenchidos, são os dados dele (`null` mantém o que está).
 * `escolhaDeReceita` é o estado inteiro da escolha: `null` a limpa.
 */
export interface AlteracaoDeComponente {
  componenteId: number | null
  codigoNovo: string | null
  descricaoNova: string | null
  tipoNovo: string | null
  escolhaDeReceita: EscolhaDeReceita | null
}

/**
 * Maior arquivo de BOM aceito, em bytes: 5 MiB. Espelha `LeitorDeBom.TamanhoMaximoEmBytes` do
 * backend (decisão P11 do plano do import) — mudou lá, muda aqui — e com a mesma fronteira: um
 * arquivo de exatamente este tamanho é aceito. Existe no front pelo mesmo motivo de
 * `TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES`: acima do limite do endpoint o servidor fecha a conexão com o
 * corpo ainda subindo, e o `fetch` rejeita sem resposta.
 */
export const TAMANHO_MAXIMO_DO_BOM_EM_BYTES = 5 * 1024 * 1024

/** "5 MiB", derivado da constante — o número do limite não é escrito à mão em nenhum texto da tela. */
export const LIMITE_DO_BOM_LEGIVEL = `${TAMANHO_MAXIMO_DO_BOM_EM_BYTES / (1024 * 1024)} MiB`

/**
 * O arquivo não passou na leitura (400 `BomInvalido`). `linhas` é a `mensagem` do servidor quebrada
 * por `\n` — uma linha por erro do arquivo (decisão P13 do plano do import). O texto do servidor é
 * ASCII sem acento: quem o mostra escreve o título em português e lista as linhas como vierem.
 */
export class ErroDeBom extends ErroDeApi {
  readonly linhas: string[]

  constructor(linhas: string[]) {
    super(400, 'O arquivo do BOM tem problemas.', undefined, 'BomInvalido')
    this.linhas = linhas
    this.name = 'ErroDeBom'
  }
}

interface CorpoDeErro {
  erro?: string
  mensagem?: string
}

async function lerCorpoDeErro(resp: Response): Promise<CorpoDeErro> {
  try {
    const corpo: unknown = await resp.json()
    return typeof corpo === 'object' && corpo !== null ? (corpo as CorpoDeErro) : {}
  } catch {
    return {}
  }
}

/**
 * Lê o 200/201 de uma escrita do rascunho ou lança. O `codigo` do servidor vai no `ErroDeApi`
 * (`ImportacaoDesatualizada` no 409 de versão velha, por exemplo); o texto do servidor não vai em
 * `detalhe`, porque é ASCII sem acento — a tela decide a frase. Com `lerBom`, o 400 `BomInvalido`
 * vira `ErroDeBom`.
 */
async function lerEscrita(resp: Response, fallback: string, lerBom = false): Promise<ImportacaoDto> {
  if (resp.ok) return (await resp.json()) as ImportacaoDto
  const corpo = await lerCorpoDeErro(resp)
  if (lerBom && resp.status === 400 && corpo.erro === 'BomInvalido') {
    throw new ErroDeBom((corpo.mensagem ?? '').split('\n').filter((l) => l.trim() !== ''))
  }
  throw new ErroDeApi(resp.status, `${fallback} (${resp.status}).`, undefined, corpo.erro)
}

function corpoJson(corpo: unknown): RequestInit {
  return {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(corpo),
  }
}

/** `FormData` sem `Content-Type`: quem põe o boundary é o browser (ver `enviarSolido`). */
function corpoMultipart(arquivo: File, versao?: string): FormData {
  const corpo = new FormData()
  corpo.append('arquivo', arquivo)
  if (versao !== undefined) corpo.append('versao', versao)
  return corpo
}

/** Lê o arquivo e abre um rascunho no Agrupamento. 400 `BomInvalido` vira `ErroDeBom`. */
export async function criarImportacao(agrupamentoId: number, arquivo: File): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/agrupamentos/${agrupamentoId}/importacoes`, {
    method: 'POST',
    body: corpoMultipart(arquivo),
  })
  return lerEscrita(resp, 'Falha ao importar o BOM', true)
}

export async function listarImportacoes(agrupamentoId: number): Promise<ResumoDeImportacaoDto[]> {
  const resp = await apiFetch(`/agrupamentos/${agrupamentoId}/importacoes`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar as importações (${resp.status}).`)
  return (await resp.json()) as ResumoDeImportacaoDto[]
}

export async function obterImportacao(id: number): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/importacoes/${id}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar a importação (${resp.status}).`)
  return (await resp.json()) as ImportacaoDto
}

/** Quantidade da Peça e "requer relatório dimensional". Escritas devolvem o rascunho relido. */
export async function alterarPeca(
  id: number,
  versao: string,
  quantidadeDaPeca: number | null,
  requerRelatorioDimensional: boolean,
): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/importacoes/${id}`, corpoJson({ versao, quantidadeDaPeca, requerRelatorioDimensional }))
  return lerEscrita(resp, 'Falha ao alterar a Peça')
}

/**
 * Casa o registro com um Componente, ou o deixa "criar novo" com os dados dele, e guarda a escolha
 * de receita. Trocar o casamento manda `escolhaDeReceita: null` (a escolha era sobre a receita do
 * casamento antigo e não vale para o novo): o backend recusa a escolha que sobrevive a um casamento
 * trocado, e quem chama não deve depender dessa recusa.
 */
export async function alterarComponente(
  id: number,
  registroId: number,
  versao: string,
  alteracao: AlteracaoDeComponente,
): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/importacoes/${id}/componentes/${registroId}`, corpoJson({ versao, ...alteracao }))
  return lerEscrita(resp, 'Falha ao alterar o componente')
}

/** Corrige a quantidade por pai de uma linha da receita lida. */
export async function alterarFilho(
  id: number,
  filhoId: number,
  versao: string,
  quantidade: number,
): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/importacoes/${id}/filhos/${filhoId}`, corpoJson({ versao, quantidade }))
  return lerEscrita(resp, 'Falha ao alterar a quantidade')
}

/** O sólido pendente de um registro novo — vira o do Componente só na confirmação. */
export async function enviarSolidoPendente(
  id: number,
  registroId: number,
  versao: string,
  arquivo: File,
): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/importacoes/${id}/componentes/${registroId}/solido`, {
    method: 'POST',
    body: corpoMultipart(arquivo, versao),
  })
  return lerEscrita(resp, 'Falha ao enviar o sólido')
}

/** O caminho do binário do sólido pendente, SEM o prefixo `/api` (ver `caminhoDoSolido`). */
export function caminhoDoSolidoPendente(id: number, registroId: number): string {
  return `/importacoes/${id}/componentes/${registroId}/solido`
}

/** Troca o arquivo do BOM mantendo o que já foi decidido. 400 `BomInvalido` vira `ErroDeBom`. */
export async function reimportar(id: number, versao: string, arquivo: File): Promise<ImportacaoDto> {
  const resp = await apiFetch(`/importacoes/${id}/arquivo`, {
    method: 'POST',
    body: corpoMultipart(arquivo, versao),
  })
  return lerEscrita(resp, 'Falha ao reimportar o BOM', true)
}

export async function descartarImportacao(id: number): Promise<void> {
  const resp = await apiFetch(`/importacoes/${id}`, { method: 'DELETE' })
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao descartar a importação (${resp.status}).`)
}

export type RecusaDaConfirmacao = 'ImportacaoComBloqueios' | 'ReceitaDoCatalogoMudou' | 'ImportacaoDesatualizada'

const RECUSAS_DA_CONFIRMACAO: readonly string[] = [
  'ImportacaoComBloqueios', 'ReceitaDoCatalogoMudou', 'ImportacaoDesatualizada',
]

/**
 * Confirma o rascunho: 201 devolve a Peça criada. As três recusas que a tela trata chegam como
 * retorno, e o resto lança. `ImportacaoComBloqueios` (400) vem sem a lista — a tela relê o `GET`
 * (decisão P15 do plano do import).
 */
export async function confirmarImportacao(
  id: number,
  versao: string,
): Promise<NoDaEstrutura | RecusaDaConfirmacao> {
  const resp = await apiFetch(`/importacoes/${id}/confirmacao`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ versao }),
  })
  if (resp.ok) return (await resp.json()) as NoDaEstrutura
  const corpo = await lerCorpoDeErro(resp)
  if ((resp.status === 400 || resp.status === 409) && corpo.erro !== undefined
    && RECUSAS_DA_CONFIRMACAO.includes(corpo.erro)) {
    return corpo.erro as RecusaDaConfirmacao
  }
  throw new ErroDeApi(resp.status, `Falha ao confirmar a importação (${resp.status}).`, undefined, corpo.erro)
}
