import { apiFetch } from './client'
import { ErroDeApi } from './erros'

export interface SetorDto {
  id: number
  nome: string
  ativo: boolean
  /** Completa os botões da fila ("Iniciar montagem"); `null` = "Iniciar"/"Terminar". */
  atividade: string | null
  /** Marca o Setor onde o Kit é montado (regra 25). */
  utilizaKit: boolean
}

export interface NovoSetor {
  nome: string
  /** Opcional; em branco, o servidor grava nula. */
  atividade: string | null
  /** Marca o Setor onde o Kit é montado (regra 25). */
  utilizaKit: boolean
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
    throw new ErroDeApi(409, 'Falha na requisição (409): formato de conflito inesperado.')
  }
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha na requisição (${resp.status}).`)
  return (await resp.json()) as T
}

export async function listarSetores(incluirInativos: boolean): Promise<SetorDto[]> {
  const resp = await apiFetch(`/setores?incluirInativos=${incluirInativos}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar setores (${resp.status}).`)
  return (await resp.json()) as SetorDto[]
}

export function criarSetor(s: NovoSetor): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch('/setores', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s),
  }).then(lerOuFalhar<SetorDto>)
}

/** `PUT` é substituição inteira: nome, atividade e utilizaKit vão sempre juntos. */
export function editarSetor(id: number, s: NovoSetor): Promise<SetorDto | ConflitoDeCadastro> {
  return apiFetch(`/setores/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s),
  }).then(lerOuFalhar<SetorDto>)
}

export async function definirAtivoSetor(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/setores/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao alterar o setor (${resp.status}).`)
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
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar materiais (${resp.status}).`)
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
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao alterar o material (${resp.status}).`)
}

/** A pausa aberta de um Pedido (spec da Fase 3D, §2.5). `desde`: ISO 8601 com offset -03:00. */
export interface PausaResumoDto {
  desde: string
  porUsuarioNome: string
  motivo: string | null
}

export interface PedidoDto {
  id: number
  numero: string
  cliente: string
  tipo: string
  status: string
  /** ISO 8601 com offset -03:00 — a API ja converteu (HorarioDeBrasiliaJsonConverter). */
  dataAbertura: string
  /** Dia do prazo, `aaaa-mm-dd`, sem hora nem fuso (`DateOnly` no servidor). Exibir com `formatarData`. */
  dataEntrega: string
  /** Decidido no servidor com o "hoje" de Brasília (regra 33 do `01`); a tela só desenha a pílula. */
  atrasado: boolean
  criadoPorUsuarioId: number
  /** `null` quando o Pedido não está pausado. */
  pausa: PausaResumoDto | null
}

export interface NovoPedido {
  numero: string
  cliente: string
  /** `aaaa-mm-dd`, o valor do `<input type="date">`; vazio só no formulário em branco. */
  dataEntrega: string
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

/**
 * `"2026-10-22"` → `22/10/2026`, cortando a string. NÃO passa por `Date`: `new Date("2026-10-22")` é
 * lido como meia-noite UTC e, num navegador em Brasília, cai no dia 21.
 */
export function formatarData(iso: string): string {
  const [ano, mes, dia] = iso.split('-')
  return `${dia}/${mes}/${ano}`
}

/** Ordem de `GET /pedidos`; `'entrega'` é a padrão do servidor e não vai na URL (D10 da spec da data de entrega). */
export type OrdemDePedidos = 'entrega' | 'recentes' | 'numero' | 'cliente'

/** Ordem de `GET /componentes`; `'recentes'` é a padrão do servidor e não vai na URL (decisão D3 do plano da 1F). */
export type OrdemDeComponentes = 'recentes' | 'codigo' | 'descricao'

export interface FiltroDePedidos {
  busca: string
  /** Status marcados; vazio não manda o parâmetro. Só valores que o servidor aceita (400 senão). */
  status: string[]
  /** Ids de Material marcados (do nó, não do catálogo); vazio não manda o parâmetro. */
  material: string[]
  pagina: number
  tamanho: number
  /** Ausente ou `'entrega'`: o parâmetro não vai, e o servidor aplica a padrão. */
  ordem?: OrdemDePedidos
}

/** Materiais que aparecem em algum nó de Pedido — as opções da faceta Material da tela de Pedidos. */
export interface MaterialResumoDto {
  id: number
  codigo: string
  descricao: string
}

export interface ContagemDeStatusDto {
  status: string
  quantidade: number
}

/**
 * O que a Home mostra dos Pedidos, contado no servidor sobre TODOS eles: `porStatus` traz sempre os
 * cinco status, na ordem do `CK_Pedido_Status`, zeros inclusive; `maisUrgentes` são até cinco
 * Pedidos fora de `Concluido`/`Cancelado`, do prazo mais antigo ao mais novo (o mais atrasado primeiro).
 */
export interface ResumoDePedidosDto {
  porStatus: ContagemDeStatusDto[]
  maisUrgentes: PedidoDto[]
}

/**
 * Como `listarComponentes`, a montagem da URL mora aqui para ser provável em teste. `status` e
 * `material` só entram quando há algo marcado, e vão como lista separada por vírgula.
 */
export async function listarPedidos(f: FiltroDePedidos): Promise<PaginaDe<PedidoDto>> {
  const params = new URLSearchParams({ busca: f.busca })
  if (f.status.length > 0) params.set('status', f.status.join(','))
  if (f.material.length > 0) params.set('material', f.material.join(','))
  params.set('pagina', String(f.pagina))
  params.set('tamanho', String(f.tamanho))
  if (f.ordem !== undefined && f.ordem !== 'entrega') params.set('ordem', f.ordem)
  const resp = await apiFetch(`/pedidos?${params}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar pedidos (${resp.status}).`)
  return (await resp.json()) as PaginaDe<PedidoDto>
}

export async function obterResumoDePedidos(): Promise<ResumoDePedidosDto> {
  const resp = await apiFetch('/pedidos/resumo')
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar o resumo de pedidos (${resp.status}).`)
  return (await resp.json()) as ResumoDePedidosDto
}

export async function listarMateriaisDosPedidos(): Promise<MaterialResumoDto[]> {
  const resp = await apiFetch('/pedidos/materiais')
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar os materiais dos pedidos (${resp.status}).`)
  return (await resp.json()) as MaterialResumoDto[]
}

export async function obterPedido(id: number): Promise<PedidoDto> {
  const resp = await apiFetch(`/pedidos/${id}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar o pedido (${resp.status}).`)
  return (await resp.json()) as PedidoDto
}

export function criarPedido(p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro> {
  return apiFetch('/pedidos', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerOuFalhar<PedidoDto>)
}

/** `PUT` é substituição inteira: número, cliente e data de entrega vão sempre juntos. */
export function editarPedido(id: number, p: NovoPedido): Promise<PedidoDto | ConflitoDeCadastro> {
  return apiFetch(`/pedidos/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(p),
  }).then(lerOuFalhar<PedidoDto>)
}

export interface AgrupamentoDto {
  id: number
  pedidoId: number
  /** Número do Pedido, para o título da página do Agrupamento. */
  pedidoNumero: string
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
export type ResultadoExclusao =
  | 'ok' | 'AgrupamentoNaoVazio' | 'PedidoNaoAberto' | 'AgrupamentoComImportacao' | 'NaoEncontrado'

export async function listarAgrupamentos(pedidoId: number): Promise<AgrupamentoDto[]> {
  const resp = await apiFetch(`/pedidos/${pedidoId}/agrupamentos`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar agrupamentos (${resp.status}).`)
  return (await resp.json()) as AgrupamentoDto[]
}

/**
 * Detalhe de um Agrupamento — o cabeçalho de `AgrupamentoDetalhePage` (Task 8b da Fase 2). Molde de
 * `obterPedido`. Nasce nesta task porque o título da tela até então usava só o `id` da rota: decisão
 * do usuário (2026-09-02) é mostrar código+tipo, que o usuário reconhece, e não o Id numérico sozinho.
 */
export async function obterAgrupamento(id: number): Promise<AgrupamentoDto> {
  const resp = await apiFetch(`/agrupamentos/${id}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar o agrupamento (${resp.status}).`)
  return (await resp.json()) as AgrupamentoDto
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
    // `AgrupamentoComImportacao`: há rascunho de importação do BOM esperando conferência.
    if (corpo.erro === 'PedidoNaoAberto' || corpo.erro === 'AgrupamentoComImportacao') return corpo.erro
    return 'AgrupamentoNaoVazio'
  }
  throw new ErroDeApi(resp.status, `Falha ao excluir o agrupamento (${resp.status}).`)
}

export interface ComponenteDto {
  id: number
  codigo: string
  descricao: string
  tipo: string
  ativo: boolean
  temSolido: boolean
}

/**
 * Detalhe de um Componente — o `GET /componentes/{id}` devolve isto, não `ComponenteDto` (que
 * segue servindo a listagem). Espelha `ComponenteDetalheDto` do backend (Task 4): os dois campos
 * novos são anuláveis JUNTOS — nulos quando o Componente não tem sólido.
 */
export interface ComponenteDetalheDto extends ComponenteDto {
  nomeDoSolido: string | null
  tamanhoDoSolidoEmBytes: number | null
}

/** Lista fechada de `CK_Componente_Tipo` — diferente de `unidadeMedida`, que é texto livre. */
export type TipoDeComponente = 'Bruto' | 'Fabricado' | 'Montagem'

export interface NovoComponente {
  codigo: string
  descricao: string
  tipo: TipoDeComponente
}

/** Espelha o `PaginaDto<T>` do backend. `total` é sob o mesmo filtro, não o tamanho de `itens`. */
export interface PaginaDe<T> {
  itens: T[]
  total: number
  pagina: number
  tamanho: number
}

export interface FiltroDeComponentes {
  busca: string
  incluirInativos: boolean
  pagina: number
  tamanho: number
  /** Ausente ou `'recentes'`: o parâmetro não vai, e o servidor aplica a padrão. */
  ordem?: OrdemDeComponentes
}

/**
 * A montagem da URL mora aqui, e não no componente, porque é isto que a torna provável em teste
 * (adendo F4 — a lição de `criarSetor`/`criarMaterial`, que ficaram 5 tasks sem prova de URL).
 * `URLSearchParams` preserva a ordem de inserção, então a URL é determinística e asserível.
 */
export async function listarComponentes(
  f: FiltroDeComponentes,
): Promise<PaginaDe<ComponenteDto>> {
  const params = new URLSearchParams({
    busca: f.busca,
    incluirInativos: String(f.incluirInativos),
    pagina: String(f.pagina),
    tamanho: String(f.tamanho),
  })
  if (f.ordem !== undefined && f.ordem !== 'recentes') params.set('ordem', f.ordem)
  const resp = await apiFetch(`/componentes?${params}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao listar componentes (${resp.status}).`)
  return (await resp.json()) as PaginaDe<ComponenteDto>
}

/**
 * Detalhe de um Componente — o cabeçalho da tela de receita padrão. Molde de `obterPedido`.
 * Componente inativo responde 200 (o backend não filtra por `Ativo` aqui), então a tela decide
 * o que mostrar; quem esconde inativo é a listagem, via `incluirInativos`.
 */
export async function obterComponente(id: number): Promise<ComponenteDetalheDto> {
  const resp = await apiFetch(`/componentes/${id}`)
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao carregar o componente (${resp.status}).`)
  return (await resp.json()) as ComponenteDetalheDto
}

/** O único 409 possível aqui é `ValorDuplicado` sobre `codigo` (UQ_Componente_Codigo). */
export function criarComponente(
  c: NovoComponente,
): Promise<ComponenteDto | ConflitoDeCadastro> {
  return apiFetch('/componentes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(c),
  }).then(lerOuFalhar<ComponenteDto>)
}

export async function definirAtivoComponente(id: number, ativo: boolean): Promise<void> {
  const resp = await apiFetch(`/componentes/${id}/ativo`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ ativo }),
  })
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao alterar o componente (${resp.status}).`)
}

// Sem editarComponente aqui, de proposito, pelo mesmo motivo do editarPedido acima: o
// PUT /componentes/{id} existe e esta testado no backend (Task 3), mas a tela de Componentes nao
// tem UI de edicao — exportar a funcao sem chamador seria codigo morto. Ela nasce junto com a
// tela que a usar.

/**
 * O caminho do binário do sólido, SEM o prefixo `/api` — quem o aplica é o `rota()` de
 * `client.ts`. Exportado em vez de embutido nos dois consumidores (upload e viewer) para a rota
 * existir num lugar só.
 */
export function caminhoDoSolido(componenteId: number): string {
  return `/componentes/${componenteId}/solido`
}

/**
 * Maior sólido aceito, em bytes: 16 MiB. Espelha `ValidadorDeArquivoStl.TamanhoMaximoEmBytes` do
 * backend — mudou lá, muda aqui — e com a mesma fronteira: lá o arquivo é recusado quando PASSA do
 * limite, então um arquivo de exatamente este tamanho é aceito.
 *
 * Existe no front porque o 400 do backend não chega ao navegador quando o arquivo é grande: acima
 * do `[RequestSizeLimit]` do endpoint o servidor fecha a conexão enquanto o corpo ainda sobe, e o
 * `fetch` rejeita sem resposta (medido no Chromium; ver o XML doc de
 * `ComponentesController.EnviarSolido`). Recusar aqui, antes de enviar, é o que deixa a tela dizer
 * que o problema é o tamanho — e poupa subir dezenas de MB só para ouvir "não".
 */
export const TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES = 16 * 1024 * 1024

/**
 * Envia (ou substitui) o sólido. `FormData` sem `Content-Type` explícito de propósito: quem põe o
 * boundary é o browser, e fixar o header à mão produz um corpo que o servidor não consegue
 * separar. O `apiFetch` não fixa `Content-Type`, então não há nada a remover.
 *
 * Sem `detalhe` no `ErroDeApi` de propósito: as mensagens do `ValidadorDeArquivoStl` são ASCII sem
 * acento ("extensao", "esta vazio"), e mostrá-las na tela poria português errado na interface —
 * quem explica o 400 ao usuário é o fallback de `UploadDeSolido`, não o servidor.
 */
export async function enviarSolido(componenteId: number, arquivo: File): Promise<void> {
  const corpo = new FormData()
  corpo.append('arquivo', arquivo)
  const resp = await apiFetch(caminhoDoSolido(componenteId), { method: 'POST', body: corpo })
  if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao enviar o sólido (${resp.status}).`)
}
