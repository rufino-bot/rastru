// @vitest-environment jsdom
//
// Ambiente por ARQUIVO, e não em `vite.config.ts`: os testes de `api/` rodam em ambiente `node` e
// usam `new Response(...)`; trocar o ambiente global arriscaria mexer nos globals deles sem ganho.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { PedidosPage } from './PedidosPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

// O auto-cleanup do RTL depende de um `afterEach` GLOBAL, que só existe com `globals: true` no
// Vitest — e este projeto importa `describe`/`it`/`expect` explicitamente, ou seja, globals off.
// Sem esta linha o segundo teste renderiza por cima do primeiro e os `getBy*` falham com
// "found multiple elements".
afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra. Default `'PCP'`: pode escrever pedidos
// (`podeEscrever` em `web/src/auth/permissoes.ts` libera `PCP` e `Administrador`), o que preserva o
// comportamento dos 4 testes existentes (o formulário precisa estar visível para eles).
let perfil = 'PCP'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const PEDIDO = {
  id: 7,
  numero: 'PED-001',
  cliente: 'Fábrica Alfa',
  tipo: 'Normal',
  status: 'Aberto',
  dataAbertura: '2026-08-06T09:30:00-03:00',
  criadoPorUsuarioId: 1,
}

describe('PedidosPage', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('mostra os pedidos que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([PEDIDO]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    // O markup novo separa `p.numero` num `<span className="font-mono">` ANINHADO dentro do
    // `<span className="font-medium">` — `getNodeText` do Testing Library só concatena os
    // `TEXT_NODE` DIRETOS de um elemento, então nenhum nó tem mais o texto completo
    // 'PED-001 — Fábrica Alfa'. A propriedade a provar é a ORDEM (número antes do cliente), não
    // mais a string inteira num nó único — daí a asserção sobre o `textContent` do `<li>`.
    const item = await screen.findByText('PED-001')
    const li = item.closest('li')!
    expect(li.textContent).toMatch(/PED-001.*Fábrica Alfa/)
  })

  it('mostra a data de abertura no fuso que a API mandou, sem reconverter', async () => {
    // Offset +05:30 (Asia/Kolkata, fuso real — não um valor arbitrário): esta suíte roda em
    // -03:00, e é isso que fica provado aqui. Se alguém trocar `formatarDataHora` por
    // `new Date(...).toLocaleString()`, o horário reconverte para o fuso da máquina e deixa de
    // bater com 09:30 em qualquer máquina cujo fuso local não seja +05:30 — o que cobre esta
    // suíte, mas não é um absoluto universal (numa máquina em IST a mutação sobreviveria). Um
    // offset -03:00 na fixture coincidiria com o fuso local por acidente e deixaria a mutação
    // sobreviver aqui sem provar nada.
    const pedidoComFusoDistinto = { ...PEDIDO, dataAbertura: '2026-08-06T09:30:00+05:30' }
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([pedidoComFusoDistinto]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText(/06\/08\/2026 09:30/)).toBeTruthy()
  })

  it('mostra erro quando a listagem falha', async () => {
    // 500: `mensagemDeErro` (`web/src/api/erros.ts:36`) mapeia status >= 500 para a mensagem de
    // servidor, não mais o fallback fixo desta tela.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')).toBeTruthy()
  })

  // C1 (achado da review da Task 8): a lista fica `[]` no `catch` (nunca é preenchida), então
  // `pedidos.length === 0` sozinho também é verdade quando a causa é falha de rede — o "Nenhum
  // pedido aberto" apareceria JUNTO do banner de erro, afirmando um fato sobre o banco a partir
  // de uma falha de conexão.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/pedidos': () => respostaJson({ erro: 'Falhou' }, 500) }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    await screen.findByText('O servidor não respondeu como esperado. Tente de novo em instantes.')
    expect(screen.queryByText('Nenhum pedido aberto')).toBeNull()
  })

  // M10 (achado da review da Task 8): `SetoresPage` e `MateriaisPage` têm o teste equivalente
  // (I1 da review de branch da 1B) — `carregar` escreve `erro` no `catch` mas só limpa no
  // sucesso. Acrescentado aqui pela mesma razão. **Medido, não presumido**: a mutação M10
  // (remover o `setErro(null)` de dentro de `carregar`) SOBREVIVE a este teste — é mutante
  // equivalente NESTA tela, não falta de cobertura. `salvar` já chama `setErro(null)` no
  // início de si mesma (`PedidosPage.tsx:54`), ANTES de chamar `carregar()`; como o único outro
  // disparo de `carregar()` é o efeito de montagem (onde `erro` já nasce `null`), não existe
  // caminho em que o `erro` seja não nulo no momento em que o `setErro(null)` DE DENTRO de
  // `carregar` seria o responsável por limpá-lo — ao contrário de `Setores`/`Materiais`, cujo
  // checkbox "Mostrar inativos" dispara uma `carregar` independente de `salvar`. Mantido porque
  // ainda prova um comportamento real (o banner de erro da carga inicial não sobrevive a um
  // cadastro bem-sucedido).
  it('limpa a mensagem de erro da carga inicial quando o cadastro seguinte tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([PEDIDO])
      },
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os pedidos.')

    fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByLabelText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    await screen.findByText('PED-001')
    expect(screen.queryByText('Não foi possível carregar os pedidos.')).toBeNull()
  })

  it('limpa o formulário e recarrega a lista depois de abrir um pedido', async () => {
    let listagens = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => {
        listagens += 1
        return respostaJson(listagens === 1 ? [] : [PEDIDO])
      },
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)
    await screen.findByLabelText('Código do pedido')

    fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-001' } })
    fireEvent.change(screen.getByLabelText('Cliente'), { target: { value: 'Fábrica Alfa' } })
    fireEvent.click(screen.getByText('Abrir pedido'))

    // O POST cai na MESMA rota da listagem: `fetchPorRota` casa por caminho, e o mock devolve a
    // lista nova. O que se prova aqui é o ramo de SUCESSO — campo limpo e lista recarregada —,
    // que é a metade que nenhum teste do projeto cobria antes desta task.
    expect(await screen.findByText(/PED-001/)).toBeTruthy()
    expect((screen.getByLabelText('Código do pedido') as HTMLInputElement).value).toBe('')
  })

  it('mostra estado vazio quando não há pedidos', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/pedidos': () => respostaJson([]) }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum pedido aberto')).toBeTruthy()
  })

  it('esconde o formulário para quem não pode escrever, e a lista continua visível', async () => {
    // Operador lê pedidos mas não escreve — `POST /pedidos` é `[Authorize(Roles = "PCP,Administrador")]`.
    perfil = 'Operador'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/pedidos': () => respostaJson([PEDIDO]) }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByText('PED-001')).toBeTruthy()
    expect(screen.queryByLabelText('Código do pedido')).toBeNull()
  })

  // I2 (achado da review da Task 8): o teste acima usa `Operador`, que não escreve NEM `pedidos`
  // NEM `setores` — prova só a metade negativa do gating. Quem prende o RECURSO (`usePodeEscrever`
  // apontando para `'pedidos'`, e não para outro) é a metade positiva com `PCP`, que escreve
  // `pedidos` (`['PCP','Administrador']`) mas NÃO `setores` (`['Administrador']`,
  // `web/src/auth/permissoes.ts`): se o recurso mudar para `'setores'`, o PCP perde a escrita e o
  // formulário desaparece.
  it('mostra o formulário para quem pode escrever pedidos mas não setores', async () => {
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/pedidos': () => respostaJson([PEDIDO]) }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    expect(await screen.findByLabelText('Código do pedido')).toBeTruthy()
  })

  it('mostra o status como pílula, com o tom certo por status', async () => {
    // Não basta asserir que os textos aparecem: `Pilula` renderiza `children` independente de
    // `tom` (M8 do Step 11 trocaria `tomDoStatus` para sempre 'positivo' sem mudar texto nenhum).
    // A prova precisa alcançar a CLASSE — o token `positivo-*`/`negativo-*` que `Pilula` aplica.
    const concluido = { ...PEDIDO, id: 1, numero: 'PED-002', status: 'Concluido' }
    const cancelado = { ...PEDIDO, id: 2, numero: 'PED-003', status: 'Cancelado' }
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos': () => respostaJson([concluido, cancelado]),
    }))

    render(<MemoryRouter><PedidosPage /></MemoryRouter>)

    const pilulaConcluido = await screen.findByText('Concluido')
    const pilulaCancelado = await screen.findByText('Cancelado')

    expect(pilulaConcluido.className).toMatch(/positivo-/)
    expect(pilulaCancelado.className).toMatch(/negativo-/)
  })
})
