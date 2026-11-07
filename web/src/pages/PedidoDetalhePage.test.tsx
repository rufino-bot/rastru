// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { PedidoDetalhePage } from './PedidoDetalhePage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra (Task 10). Default `'PCP'`: pode
// escrever agrupamentos (`podeEscrever` em `web/src/auth/permissoes.ts` libera `PCP` e
// `Administrador`), o que preserva o comportamento dos testes existentes (formulário e botão de
// excluir precisam estar visíveis para eles).
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
  dataEntrega: '2026-10-22', atrasado: false,
  criadoPorUsuarioId: 1, pausa: null,
}

const AGRUPAMENTO = {
  id: 21,
  pedidoId: 7,
  codigo: 'AGR-01',
  tipo: 'Kit',
  criadoEm: '2026-08-06T10:00:00-03:00',
  criadoPorUsuarioId: 1,
}

// A tela lê `:id` da rota, então ela precisa nascer DENTRO de uma rota casada — renderizar o
// componente solto deixaria `useParams()` vazio e `pedidoId` viraria NaN.
function renderizarDetalhe() {
  return render(
    <MemoryRouter initialEntries={['/pedidos/7']}>
      <Routes>
        <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
      </Routes>
    </MemoryRouter>,
  )
}

// O PUT de editar e o GET do cabeçalho caem no mesmo caminho: o mock separa pelo método.
function apiComPut(put: () => Response | Promise<Response>) {
  return vi.fn((url: string | URL, init?: RequestInit) => {
    const caminho = String(url).split('?')[0]
    if (caminho === '/api/pedidos/7') return Promise.resolve(init?.method === 'PUT' ? put() : respostaJson(PEDIDO))
    if (caminho === '/api/pedidos/7/agrupamentos') return Promise.resolve(respostaJson([AGRUPAMENTO]))
    if (caminho === '/api/pedidos/7/baixas') return Promise.resolve(respostaJson([]))
    return Promise.reject(new Error(`fetch não esperado no teste: ${url}`))
  })
}

describe('PedidoDetalhePage', () => {
  beforeEach(() => {
    perfil = 'PCP'
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  // I1 (achado da review de branch da 1D): nenhuma das seis telas que buscam dados tinha teste
  // provando o indicador "Carregando…" — só vazio e erro tinham. Molde de
  // `LoginPage.test.tsx` ("desabilita o botão enquanto o login está em voo"): fetch que nunca
  // resolve, e asserção SÍNCRONA (sem `await`/`findBy*`) de que o indicador está na tela antes de
  // qualquer resposta chegar.
  it('mostra o indicador de carregando antes da resposta da API chegar', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => {})))

    renderizarDetalhe()

    // A seção "Baixas" também carrega e tem o indicador dela: o da página é o que está fora dessa seção.
    const secoes = ['Baixas', 'Reclamações'].map((nome) => screen.getByRole('region', { name: nome }))
    const daPagina = screen.getAllByRole('status').filter((i) => !secoes.some((s) => s.contains(i)))
    expect(daPagina).toHaveLength(1)
    expect(daPagina[0].textContent).toBe('Carregando…')
  })

  it('mostra o cabeçalho do pedido e os agrupamentos dele', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('PED-001')).toBeTruthy()
    expect(screen.getByText('Fábrica Alfa')).toBeTruthy()
    expect(await screen.findByText('AGR-01')).toBeTruthy()
  })

  it('o link do item cobre o cartao inteiro', async () => {
    // jsdom não calcula layout: a suíte afirma as classes do overlay, não a área clicável.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    const link = await screen.findByRole('link', { name: 'AGR-01' })
    expect(link.classList.contains('after:absolute')).toBe(true)
    expect(link.classList.contains('after:inset-0')).toBe(true)
    expect(link.getAttribute('href')).toBe('/agrupamentos/21')
  })

  it('o botao do item continua alcancavel pelo papel e nome, e o clique dispara a acao e nao a navegacao', async () => {
    // Em jsdom isto só prova o handler: o botão é achado por papel e nome, e o clique nele abre o
    // diálogo de exclusão sem navegar. Que o clique no CENTRO do botão não cai no overlay do link
    // depende de layout e é conferido no navegador.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))
    render(
      <MemoryRouter initialEntries={['/pedidos/7']}>
        <Routes>
          <Route path="/pedidos/:id" element={<PedidoDetalhePage />} />
          <Route path="/agrupamentos/:id" element={<p>agrupamento aberto</p>} />
        </Routes>
      </MemoryRouter>,
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Excluir' }))

    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(screen.queryByText('agrupamento aberto')).toBeNull()
  })

  it('mostra o status do Pedido pelo rótulo em português, não pelo valor cru do enum', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson({ ...PEDIDO, status: 'EmProducao' }),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('Em produção')).toBeTruthy()
    expect(screen.queryByText('EmProducao')).toBeNull()
  })

  describe('pausa do Pedido', () => {
    const PAUSA = { desde: '2026-09-28T10:14:00-03:00', porUsuarioNome: 'PCP', motivo: 'PED-9 urgente' }

    it('Pedido pausado mostra a pilula de atencao e o aviso com quem, quando e por que', async () => {
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/pedidos/7': () => respostaJson({ ...PEDIDO, pausa: PAUSA }),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      }))

      renderizarDetalhe()

      const pilula = await screen.findByText('Pausado')
      const classes = pilula.className.split(/\s+/)
      expect(classes).toContain('bg-atencao-fundo')
      expect(classes).toContain('text-atencao-texto')
      expect(classes.some((c) => /negativo-|positivo-/.test(c))).toBe(false)
      expect(screen.getByText(/Pausado desde 28\/09\/2026 10:14 por PCP — PED-9 urgente\./)).toBeTruthy()
    })

    it('Pedido livre nao mostra pilula nem aviso', async () => {
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/pedidos/7': () => respostaJson(PEDIDO),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      }))

      renderizarDetalhe()

      await screen.findByText('PED-001')
      expect(screen.queryByText('Pausado')).toBeNull()
      expect(screen.queryByText(/Pausado desde/)).toBeNull()
    })

    it('pausar recarrega o Pedido: a pilula e o Retomar aparecem sem sair da tela', async () => {
      let lidos = 0
      const fetchMock = fetchPorRota({
        '/api/pedidos/7': () => { lidos += 1; return respostaJson(lidos === 1 ? PEDIDO : { ...PEDIDO, pausa: PAUSA }) },
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
        '/api/pedidos/7/pausas': () => respostaJson({ id: 5 }, 201),
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Pausar' }))
      fireEvent.click(screen.getByRole('button', { name: 'Confirmar pausa' }))

      expect(await screen.findByRole('button', { name: 'Retomar' })).toBeTruthy()
      expect(screen.getByText('Pausado')).toBeTruthy()
      expect(lidos).toBe(2)
    })

    // Mesma fixture (Pedido livre), perfis dos dois lados: o controle some para quem nao pode e
    // aparece para quem pode — o gating vai na ACAO, e o aviso da pausa continua sendo de todos.
    it.each(['PCP', 'Gestao', 'Administrador'])('o perfil %s ve o botao Pausar', async (p) => {
      perfil = p
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/pedidos/7': () => respostaJson(PEDIDO),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      }))

      renderizarDetalhe()

      expect(await screen.findByRole('button', { name: 'Pausar' })).toBeTruthy()
    })

    it.each(['Operador', 'Qualidade'])('o perfil %s nao ve o botao Pausar', async (p) => {
      perfil = p
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/pedidos/7': () => respostaJson(PEDIDO),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      }))

      renderizarDetalhe()

      await screen.findByText('PED-001')
      expect(screen.queryByRole('button', { name: 'Pausar' })).toBeNull()
    })

    it('o Operador ve o aviso da pausa, mas nao o Retomar', async () => {
      perfil = 'Operador'
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/pedidos/7': () => respostaJson({ ...PEDIDO, pausa: PAUSA }),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      }))

      renderizarDetalhe()

      expect(await screen.findByText(/Pausado desde 28\/09\/2026 10:14/)).toBeTruthy()
      expect(screen.queryByRole('button', { name: 'Retomar' })).toBeNull()
    })
  })

  it('pede confirmação antes de excluir e só exclui depois do "Excluir" do diálogo', async () => {
    // Padrão de PedidosPage.test.tsx:73-79: primeira listagem devolve o agrupamento, as
    // seguintes devolvem [] — é o que torna o recarregamento pós-exclusão observável (M2/M6).
    let listagens = 0
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        listagens += 1
        return respostaJson(listagens === 1 ? [AGRUPAMENTO] : [])
      },
      '/api/agrupamentos/21': () => new Response(null, { status: 204 }),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))

    // O diálogo apareceu e NADA foi excluído ainda: a pausa deliberada é a propriedade sob teste.
    expect(screen.getByRole('dialog')).toBeTruthy()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(false)

    // Dois botões escritos "Excluir" na tela agora (o do item e o do diálogo): pegar pelo texto
    // dentro do diálogo é o que impede o teste de clicar no errado e passar por acidente.
    const dialogo = screen.getByRole('dialog')
    const confirmar = Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!
    fireEvent.click(confirmar)

    // A segunda listagem devolve [] (lista diferente da primeira): 'AGR-01' SUMIR da tela só
    // acontece se `excluir()` recarregar de fato depois do 204. Se apagar o `await
    // carregar(pedidoId)` de PedidoDetalhePage.tsx, a lista antiga (com AGR-01) fica na tela para
    // sempre e esta espera nunca resolve — timeout, não falso-positivo.
    await waitFor(() => expect(screen.queryByText('AGR-01')).toBeNull())
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(true)
  })

  it('cancelar fecha o diálogo sem excluir', async () => {
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('dialog')).toBeNull()
    expect(fetchMock.mock.calls.some((c) => String(c[0]).includes('/agrupamentos/21'))).toBe(false)
  })

  it('explica o motivo quando a exclusão é recusada por agrupamento não vazio', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'AgrupamentoNaoVazio' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(
      await screen.findByText('Este agrupamento já tem estrutura e não pode mais ser excluído.'),
    ).toBeTruthy()
  })

  it('Excluir_agrupamento_com_rascunho_mostra_a_mensagem_propria', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'AgrupamentoComImportacao' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(
      await screen.findByText('Este agrupamento tem importações em conferência. Descarte-as antes de excluir.'),
    ).toBeTruthy()
  })

  it('explica o motivo quando a exclusão é recusada porque o agrupamento não existe mais', async () => {
    // Segundo desfecho do mapa (M1): prova que a mensagem varia por código, não é constante —
    // com um único caso fixado, trocar o valor de `NaoEncontrado` em MOTIVO_DA_RECUSA mata 0.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'NaoEncontrado' }, 404),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(await screen.findByText('Este agrupamento já não existe mais.')).toBeTruthy()
  })

  it('explica o motivo quando a exclusão é recusada porque o pedido não está mais aberto', async () => {
    // Terceiro desfecho do mapa (A2 do segundo fix pass): segundo o comentário de
    // cadastros.ts:192-194, PedidoNaoAberto é o código que MAIS chega na prática (a ordem das
    // guardas no backend é existe -> Pedido Aberto -> vazio). O 409 é discriminado pelo campo
    // `erro` do corpo — sem este teste, mutar PedidoDetalhePage.tsx:25 matava 0.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'PedidoNaoAberto' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    expect(
      await screen.findByText('O pedido não está mais aberto: não dá para excluir agrupamentos dele.'),
    ).toBeTruthy()
  })

  // M5 do Step 4 (fase1d-task-10-brief.md): mover o `<BannerDeErro>` para depois do bloco
  // `carregando ? …` sobrevive a TODOS os testes acima — RTL não olha ordem de DOM por padrão, só
  // se o texto existe em algum lugar. Mas é exatamente essa posição relativa que causou o "erro
  // que pisca" da review da Task 11 no desenho antigo (o early return escondia o banner atrás do
  // "Carregando…"). Sem early return isso não pode mais acontecer por ESCONDER o banner, mas a
  // ORDEM ainda importa para quem usa a tela: o aviso de recusa precisa estar ACIMA da lista, não
  // abaixo dela, onde exigiria rolar para ver. Esta asserção prende a ordem no DOM.
  it('mostra o banner de erro antes da lista de agrupamentos, não depois', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
      '/api/pedidos/7/baixas': () => respostaJson([]),
      '/api/pedidos/7/reclamacoes': () => respostaJson([]),
      '/api/agrupamentos/21': () => respostaJson({ erro: 'AgrupamentoNaoVazio' }, 409),
    }))

    renderizarDetalhe()
    fireEvent.click(await screen.findByText('Excluir'))
    const dialogo = screen.getByRole('dialog')
    fireEvent.click(Array.from(dialogo.querySelectorAll('button')).find((b) => b.textContent === 'Excluir')!)

    const banner = await screen.findByRole('alert')
    const lista = screen.getByRole('list', { name: 'Agrupamentos' })
    expect(banner.compareDocumentPosition(lista) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('cadastra o agrupamento, limpa o formulário e recarrega a lista quando o salvar dá certo', async () => {
    // A3.1: nenhum dos testes acima exercita o formulário — só a exclusão. POST e GET caem na
    // MESMA rota (`/pedidos/7/agrupamentos`), então o contador precisa distinguir as TRÊS
    // chamadas na ordem em que acontecem: 1) GET no mount (lista vazia), 2) POST do submit
    // (sucesso, sem `erro`), 3) GET do `carregar(pedidoId)` pós-salvar (lista com o novo item).
    // 'AGR-01' aparecer só depois do submit é o que torna o recarregamento observável (padrão
    // M2/M6, como no teste de exclusão acima).
    let chamadas = 0
    const fetchMock = fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return respostaJson(AGRUPAMENTO, 201)
        return respostaJson([AGRUPAMENTO])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    renderizarDetalhe()
    await screen.findByText('PED-001')
    expect(screen.queryByText('AGR-01')).toBeNull()

    fireEvent.change(screen.getByLabelText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    // Prova o `await carregar(pedidoId)`: se ele for apagado de PedidoDetalhePage.tsx:73, a
    // terceira chamada nunca acontece, a lista fica vazia para sempre e este findByText estoura
    // por timeout, não por falso-positivo.
    expect(await screen.findByText('AGR-01')).toBeTruthy()
    // Prova o `setForm(FORMULARIO_VAZIO)`: se ele for apagado da linha 72, o campo continuaria
    // com 'AGR-01' digitado.
    expect((screen.getByLabelText('Código do agrupamento') as HTMLInputElement).value).toBe('')

    // B2: fetchPorRota (testes/api.ts:29-36) casa só por caminho — ignora método e corpo. Sem
    // esta asserção, trocar o POST de criarAgrupamento por GET, ou deixar de enviar o `form` no
    // corpo, sobrevive ao teste. `init` é o segundo argumento passado ao fetch global, montado
    // por fetchComToken (client.ts:43-47): `{ ...init, headers: Headers, credentials: 'include' }`.
    // O POST do agrupamento, e não o da prévia do descarte que o PCP também dispara ao abrir a página.
    const chamadaPost = fetchMock.mock.calls.find(
      (c) => (c[1] as RequestInit | undefined)?.method === 'POST' && String(c[0]).endsWith('/agrupamentos'),
    )
    expect(chamadaPost).toBeTruthy()
    expect(JSON.parse((chamadaPost![1] as RequestInit).body as string)).toEqual({ codigo: 'AGR-01', tipo: 'Kit' })
  })

  // M7 do Step 4: remover `setEnviando(false)` do `finally` de `salvar` sobrevive ao teste acima
  // (ele só olha o efeito COLATERAL do submit — a lista recarregada —, nunca o estado do próprio
  // botão). Sob a mutação, `enviando` fica travado em `true` para sempre e o botão "Adicionar"
  // nunca reabilita, mesmo depois do cadastro concluir com sucesso.
  it('reabilita o botão "Adicionar" depois que o cadastro conclui', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()
    await screen.findByText('PED-001')

    fireEvent.change(screen.getByLabelText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await waitFor(() => {
      expect((screen.getByText('Adicionar') as HTMLButtonElement).disabled).toBe(false)
    })
  })

  it('mostra o erro de duplicidade e mantém o formulário preenchido quando o salvar é recusado', async () => {
    // A3.1, ramo de conflito: o `return` de PedidoDetalhePage.tsx:70 acontece ANTES do
    // `setForm(FORMULARIO_VAZIO)` — sem ele, o fluxo continua para `setForm` e `carregar()`. A
    // mensagem de erro sozinha SOBREVIVE a essa mutação (nada a apaga depois dela), por isso é a
    // asserção do valor do campo que pega essa mutação. Para a asserção ser alcançada, o 409 vale
    // só na SEGUNDA chamada da rota (o POST); da terceira chamada em diante ela devolve lista
    // vazia com sucesso — senão a terceira chamada (a de `carregar()`, que só acontece sob a
    // mutação) bateria de novo no 409, `listarAgrupamentos` lançaria (cadastros.ts:174) e o catch
    // de PedidoDetalhePage.tsx:74 sobrescreveria a mensagem de conflito por 'Não foi possível
    // carregar o pedido.' antes da asserção do campo rodar.
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => {
        chamadas += 1
        if (chamadas === 2) return respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: false, idExistente: 1 }, 409)
        return respostaJson([])
      },
    }))

    renderizarDetalhe()
    await screen.findByText('PED-001')

    fireEvent.change(screen.getByLabelText('Código do agrupamento'), { target: { value: 'AGR-01' } })
    fireEvent.click(screen.getByText('Adicionar'))

    expect(
      await screen.findByText('Já existe um agrupamento com este código neste pedido.'),
    ).toBeTruthy()
    expect((screen.getByLabelText('Código do agrupamento') as HTMLInputElement).value).toBe('AGR-01')
  })

  // Task 10: o formulário e o botão de excluir passam a existir só para quem tem
  // `usePodeEscrever('agrupamentos')` — a lista continua visível para todo mundo, porque
  // `agrupamentos` é recurso de LEITURA aberta e ESCRITA restrita (PCP/Administrador).
  it('esconde o formulário e o botão de excluir para quem não pode escrever', async () => {
    perfil = 'Operador'
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('AGR-01')).toBeTruthy()
    expect(screen.queryByLabelText('Código do agrupamento')).toBeNull()
    expect(screen.queryByText('Excluir')).toBeNull()
  })

  // Teste 8 da Task 8 (Fase 2): o código do agrupamento vira link para a árvore de estrutura dele
  // — é por essa navegação que a Fase 2 fica alcançável a partir do Pedido.
  it('cada agrupamento da lista é um link para /agrupamentos/:id', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
    }))

    renderizarDetalhe()

    const link = await screen.findByRole('link', { name: 'AGR-01' })
    expect(link.getAttribute('href')).toBe('/agrupamentos/21')
  })

  it('mostra estado vazio quando o pedido não tem agrupamentos', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('Nenhum agrupamento neste pedido')).toBeTruthy()
  })

  // Achado Important da review da Task 10 (plan-mandated): o Step 2 do brief não trazia o
  // `erro === null &&` que as outras quatro telas retrofitadas têm. Sem ele, `agrupamentos` fica
  // `[]` no catch de `carregar` (nunca é preenchido), e `.length === 0` sozinho também é verdade
  // numa falha de rede — "Nenhum agrupamento neste pedido" apareceria JUNTO do banner de erro,
  // convidando a criar o primeiro a partir de uma falha de conexão. É a mesma forma do Critical
  // que o fix pass da Task 8 pagou (achado C1), e nenhum dos testes anteriores exercitava o
  // caminho de falha de leitura.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => Promise.reject(new Error('rede caiu')),
    }))

    renderizarDetalhe()

    await screen.findByText('Não foi possível carregar o pedido.')
    expect(screen.queryByText('Nenhum agrupamento neste pedido')).toBeNull()
  })

  it('mostra o prazo de entrega e a pilula Atrasado so quando o servidor diz que esta atrasado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson({ ...PEDIDO, atrasado: true }),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText(/entrega em 22\/10\/2026/)).toBeTruthy()
    const pilula = screen.getByText('Atrasado')
    expect(pilula.className.split(/\s+/)).toContain('text-atraso-texto')
    cleanup()

    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))
    renderizarDetalhe()
    await screen.findByText(/entrega em 22\/10\/2026/)
    expect(screen.queryByText('Atrasado')).toBeNull()
  })

  it('mostra o prazo e a abertura em blocos proprios que nao quebram por dentro', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson(PEDIDO),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()

    const entrega = await screen.findByText('entrega em 22/10/2026 ·')
    const abertura = screen.getByText('aberto em 06/08/2026 09:30')
    expect(entrega.className.split(/\s+/)).toContain('whitespace-nowrap')
    expect(abertura.className.split(/\s+/)).toContain('whitespace-nowrap')
  })

  it('Pedido concluído mostra "Concluído em {data}"', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      // A da remessa que concluiu: o servidor manda o instante com o offset de Brasília, e a tela mostra o dia.
      '/api/pedidos/7': () => respostaJson({ ...PEDIDO, status: 'Concluido', dataConclusao: '2026-10-20T23:45:00-03:00' }),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()

    expect(await screen.findByText('Concluído em 20/10/2026')).toBeTruthy()
  })

  it('Pedido que não concluiu não mostra "Concluído em"', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/pedidos/7': () => respostaJson({ ...PEDIDO, dataConclusao: null }),
      '/api/pedidos/7/agrupamentos': () => respostaJson([]),
    }))

    renderizarDetalhe()
    await screen.findByText(/entrega em 22\/10\/2026/)

    expect(screen.queryByText(/Concluído em/)).toBeNull()
  })

  describe('editar o pedido', () => {
    it('quem nao escreve pedidos nao ve o Editar pedido', async () => {
      perfil = 'Operador'
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(PEDIDO)))

      renderizarDetalhe()
      await screen.findByText('Fábrica Alfa')

      expect(screen.queryByRole('button', { name: 'Editar pedido' })).toBeNull()
    })

    it('abre o painel com numero, cliente e prazo preenchidos', async () => {
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(PEDIDO)))

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))

      const painel = screen.getByRole('form', { name: 'Editar pedido' })
      expect((within(painel).getByLabelText('Código do pedido') as HTMLInputElement).value).toBe('PED-001')
      expect((within(painel).getByLabelText('Cliente') as HTMLInputElement).value).toBe('Fábrica Alfa')
      expect((within(painel).getByLabelText('Data de entrega') as HTMLInputElement).value).toBe('2026-10-22')
    })

    it('salvar manda o PUT com os tres campos, aplica a resposta e devolve o foco ao Editar pedido', async () => {
      const editado = { ...PEDIDO, numero: 'PED-001-A', dataEntrega: '2026-12-01', atrasado: false }
      const fetchMock = apiComPut(() => respostaJson(editado))
      vi.stubGlobal('fetch', fetchMock)

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-001-A' } })
      fireEvent.change(screen.getByLabelText('Data de entrega'), { target: { value: '2026-12-01' } })
      fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))

      await waitFor(() => expect(screen.queryByRole('form', { name: 'Editar pedido' })).toBeNull())
      const put = fetchMock.mock.calls.find(([, init]) => init?.method === 'PUT')!
      expect(JSON.parse(String(put[1]!.body))).toEqual({ numero: 'PED-001-A', cliente: 'Fábrica Alfa', dataEntrega: '2026-12-01' })
      expect(screen.getByText(/entrega em 01\/12\/2026/)).toBeTruthy()
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar pedido' }))
      // A resposta do PUT é o Pedido novo: sem segundo GET do cabeçalho (decisão P2 do plano).
      expect(fetchMock.mock.calls.filter(([url, init]) => String(url) === '/api/pedidos/7' && init?.method !== 'PUT'))
        .toHaveLength(1)
    })

    it('numero duplicado mantem o painel aberto com a mensagem dentro dele', async () => {
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(
        { erro: 'ValorDuplicado', campo: 'numero', existeInativo: false, idExistente: 3 }, 409)))

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.change(screen.getByLabelText('Código do pedido'), { target: { value: 'PED-003' } })
      fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))

      const painel = await screen.findByRole('form', { name: 'Editar pedido' })
      expect(await within(painel).findByText('Já existe um pedido com este número.')).toBeTruthy()
      expect((within(painel).getByLabelText('Código do pedido') as HTMLInputElement).value).toBe('PED-003')
    })

    it('com a edicao em voo, Cancelar fica desabilitado', async () => {
      vi.stubGlobal('fetch', apiComPut(() => new Promise<Response>(() => {})))

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.click(screen.getByRole('button', { name: 'Salvar' }))

      await screen.findByText('Salvando…')
      expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('Cancelar fecha sem salvar e devolve o foco ao Editar pedido', async () => {
      const fetchMock = apiComPut(() => respostaJson(PEDIDO))
      vi.stubGlobal('fetch', fetchMock)

      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

      expect(screen.queryByRole('form', { name: 'Editar pedido' })).toBeNull()
      expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar pedido' }))
    })
  })

  describe('seção Baixas', () => {
    const BAIXA = {
      id: 3, motivo: 'Descarte', observacao: null, escopo: { tipo: 'Pedido', id: 7 }, tudo: true,
      dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 1, usuarioNome: 'Quê', estornada: false, estornavel: true,
      linhas: [
        { estruturaItemId: 9, descricao: 'Eixo', codigo: 'EX-1', origemPosicao: 'AIniciar', origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade: 5 },
      ],
    }

    it('lista as baixas do Pedido, com o número dele na frase do descarte, e estorna', async () => {
      let estornada = false
      const fetchMock = fetchPorRota({
        '/api/pedidos/7': () => respostaJson(PEDIDO),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
        '/api/pedidos/7/baixas': () => respostaJson([{ ...BAIXA, estornada, estornavel: !estornada }]),
        '/api/baixas/3/estorno': () => { estornada = true; return respostaJson([]) },
      })
      vi.stubGlobal('fetch', fetchMock)

      renderizarDetalhe()
      expect(await screen.findByText('Descartar tudo do Pedido PED-001')).toBeTruthy()
      expect(screen.getByText('5: 5 a iniciar')).toBeTruthy()

      fireEvent.click(screen.getByRole('button', { name: /Estornar a baixa/ }))
      fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Estornar' }))
      expect(await screen.findByText('Estornada')).toBeTruthy()
    })

    it('o título da seção é h2, como as outras da página', async () => {
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(PEDIDO)))
      renderizarDetalhe()
      await screen.findByText('Nenhuma baixa.')
      expect(screen.getByRole('heading', { level: 2, name: 'Baixas' })).toBeTruthy()
    })

    it('sem baixas diz "Nenhuma baixa."', async () => {
      vi.stubGlobal('fetch', apiComPut(() => respostaJson(PEDIDO)))
      renderizarDetalhe()
      expect(await screen.findByText('Nenhuma baixa.')).toBeTruthy()
    })
  })
  describe('Descartar tudo', () => {
    const PREVIA = '/api/pedidos/7/descarte/previa'
    const LINHA = {
      estruturaItemId: 9, descricao: 'Eixo', codigo: 'EX-1', origemPosicao: 'AIniciar', origemSetorId: null,
      origemSetorNome: null, origemOrdem: null, quantidade: 5,
    }
    const COM_ALGO = { linhas: [LINHA], viramSobra: [], travaNoTopo: null }

    /** O Pedido, os Agrupamentos e as baixas de sempre, mais a prévia e o descarte que o teste manda. */
    function montar(previa: () => Response, descarte: () => Response = () => respostaJson({ id: 1 }, 201)) {
      const fetchMock = fetchPorRota({
        '/api/pedidos/7': () => respostaJson(PEDIDO),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
        '/api/pedidos/7/baixas': () => respostaJson([]),
        '/api/pedidos/7/reclamacoes': () => respostaJson([]),
        [PREVIA]: previa,
        '/api/pedidos/7/descarte': descarte,
      })
      vi.stubGlobal('fetch', fetchMock)
      return fetchMock
    }
    const chamadasA = (fetchMock: ReturnType<typeof vi.fn>, rota: string) =>
      fetchMock.mock.calls.filter((c) => String(c[0]).split('?')[0] === rota).length

    it('com algo em produção, o PCP vê o botão ao lado do "Editar pedido"', async () => {
      montar(() => respostaJson(COM_ALGO))
      renderizarDetalhe()

      expect(await screen.findByRole('button', { name: 'Descartar tudo' })).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Editar pedido' })).toBeTruthy()
    })

    it('sem nada em produção (409 NadaEmProducao) o botão não aparece', async () => {
      const fetchMock = montar(() => respostaJson({ erro: 'NadaEmProducao', mensagem: 'O Pedido não tem nada em produção para descartar.' }, 409))
      renderizarDetalhe()

      await screen.findByRole('button', { name: 'Editar pedido' })
      await waitFor(() => expect(chamadasA(fetchMock, PREVIA)).toBe(1))
      await new Promise((r) => setTimeout(r, 20))
      expect(screen.queryByRole('button', { name: 'Descartar tudo' })).toBeNull()
    })

    it('com a prévia vazia o botão não aparece', async () => {
      const fetchMock = montar(() => respostaJson({ linhas: [], viramSobra: [], travaNoTopo: null }))
      renderizarDetalhe()

      await screen.findByRole('button', { name: 'Editar pedido' })
      await waitFor(() => expect(chamadasA(fetchMock, PREVIA)).toBe(1))
      await new Promise((r) => setTimeout(r, 20))
      expect(screen.queryByRole('button', { name: 'Descartar tudo' })).toBeNull()
    })

    it('com LiberacaoEmAberto o botão aparece e o painel mostra a frase do servidor', async () => {
      montar(() => respostaJson({ erro: 'LiberacaoEmAberto', mensagem: 'Estorne o pedido de liberação de CH-1.' }, 409))
      renderizarDetalhe()

      fireEvent.click(await screen.findByRole('button', { name: 'Descartar tudo' }))

      const painel = await screen.findByRole('form', { name: 'Descartar tudo' })
      expect(await within(painel).findByText('Estorne o pedido de liberação de CH-1.')).toBeTruthy()
      expect((within(painel).getByRole('button', { name: 'Descartar tudo' }) as HTMLButtonElement).disabled).toBe(true)
    })

    it('uma falha da prévia esconde o botão e não derruba a página', async () => {
      montar(() => respostaJson({ erro: 'x' }, 500))
      renderizarDetalhe()

      expect(await screen.findByText('PED-001')).toBeTruthy()
      await new Promise((r) => setTimeout(r, 20))
      expect(screen.queryByRole('button', { name: 'Descartar tudo' })).toBeNull()
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('quem não dá baixa não vê o botão e a prévia nem é pedida', async () => {
      perfil = 'Operador'
      const fetchMock = montar(() => respostaJson(COM_ALGO))
      renderizarDetalhe()

      await screen.findByText('PED-001')
      await new Promise((r) => setTimeout(r, 20))
      expect(screen.queryByRole('button', { name: 'Descartar tudo' })).toBeNull()
      expect(chamadasA(fetchMock, PREVIA)).toBe(0)
    })

    it('pede a prévia uma vez por carga, e não a cada render', async () => {
      const fetchMock = montar(() => respostaJson(COM_ALGO))
      renderizarDetalhe()

      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))
      await screen.findByRole('button', { name: 'Descartar tudo' })

      expect(chamadasA(fetchMock, PREVIA)).toBe(1)
    })

    it('abre o painel com a prévia e sem o botão do cabeçalho', async () => {
      montar(() => respostaJson(COM_ALGO))
      renderizarDetalhe()

      fireEvent.click(await screen.findByRole('button', { name: 'Descartar tudo' }))

      const painel = await screen.findByRole('form', { name: 'Descartar tudo' })
      expect(await within(painel).findByText('1 nó com algo em produção, 5 unidades em 1 posição')).toBeTruthy()
      expect(within(painel).getByText('Depois disto, o Pedido fica concluído.')).toBeTruthy()
      // Só o enviar do painel: o botão do cabeçalho some enquanto o painel está aberto.
      expect(screen.getAllByRole('button', { name: 'Descartar tudo' })).toHaveLength(1)
    })

    it('abrir o descarte fecha a edição do pedido, e o contrário', async () => {
      montar(() => respostaJson(COM_ALGO))
      renderizarDetalhe()

      fireEvent.click(await screen.findByRole('button', { name: 'Editar pedido' }))
      expect(screen.getByRole('form', { name: 'Editar pedido' })).toBeTruthy()
      fireEvent.click(await screen.findByRole('button', { name: 'Descartar tudo' }))
      expect(screen.queryByRole('form', { name: 'Editar pedido' })).toBeNull()
      expect(await screen.findByRole('form', { name: 'Descartar tudo' })).toBeTruthy()

      fireEvent.click(screen.getByRole('button', { name: 'Editar pedido' }))
      expect(screen.queryByRole('form', { name: 'Descartar tudo' })).toBeNull()
      expect(screen.getByRole('form', { name: 'Editar pedido' })).toBeTruthy()
    })

    it('descarta, recarrega a página e leva o foco ao título', async () => {
      let descartado = false
      const fetchMock = montar(
        () => (descartado
          ? respostaJson({ erro: 'NadaEmProducao', mensagem: 'sem nada' }, 409)
          : respostaJson(COM_ALGO)),
        () => { descartado = true; return respostaJson({ id: 1 }, 201) },
      )
      renderizarDetalhe()

      fireEvent.click(await screen.findByRole('button', { name: 'Descartar tudo' }))
      const painel = await screen.findByRole('form', { name: 'Descartar tudo' })
      const enviar = within(painel).getByRole('button', { name: 'Descartar tudo' }) as HTMLButtonElement
      await waitFor(() => expect(enviar.disabled).toBe(false))
      fireEvent.click(enviar)

      await waitFor(() => expect(screen.queryByRole('form', { name: 'Descartar tudo' })).toBeNull())
      expect(chamadasA(fetchMock, '/api/pedidos/7/descarte')).toBe(1)
      // O Pedido, a lista de baixas e a prévia foram pedidos de novo, e o botão sumiu: não há mais nada a descartar.
      await waitFor(() => expect(chamadasA(fetchMock, '/api/pedidos/7/baixas')).toBe(2))
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Descartar tudo' })).toBeNull())
      expect(chamadasA(fetchMock, '/api/pedidos/7')).toBeGreaterThanOrEqual(2)
      expect(chamadasA(fetchMock, PREVIA)).toBe(3)
      await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1 }).firstElementChild))
    })

    it('o Cancelar devolve o foco ao botão do cabeçalho', async () => {
      montar(() => respostaJson(COM_ALGO))
      renderizarDetalhe()

      fireEvent.click(await screen.findByRole('button', { name: 'Descartar tudo' }))
      fireEvent.click(await screen.findByRole('button', { name: 'Cancelar' }))

      await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Descartar tudo' })))
    })
  })

  describe('reposição', () => {
    function rotas(extra: Record<string, () => Response> = {}, pedido: object = PEDIDO) {
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/pedidos/7': () => respostaJson(pedido),
        '/api/pedidos/7/agrupamentos': () => respostaJson([AGRUPAMENTO]),
        '/api/pedidos/7/baixas': () => respostaJson([]),
        '/api/pedidos/7/reclamacoes': () => respostaJson([]),
        ...extra,
      }))
    }

    it('mostra a pílula "N a repor" em atencao no cabeçalho', async () => {
      rotas({}, { ...PEDIDO, aRepor: 3 })
      renderizarDetalhe()
      expect((await screen.findByText('3 a repor')).className).toMatch(/atencao/)
    })

    it('não mostra a pílula com aRepor 0', async () => {
      rotas()
      renderizarDetalhe()
      await screen.findByText('Fábrica Alfa')
      expect(screen.queryByText(/a repor/)).toBeNull()
    })

    it('a seção Reclamações mostra o vazio "Nenhuma reclamação"', async () => {
      rotas()
      renderizarDetalhe()
      expect(await screen.findByText('Nenhuma reclamação')).toBeTruthy()
    })

    it('a seção Reclamações mostra o erro com "Tentar de novo"', async () => {
      rotas({ '/api/pedidos/7/reclamacoes': () => respostaJson({}, 500) })
      renderizarDetalhe()
      expect(await screen.findByText(/Não foi possível carregar as reclamações|O servidor não respondeu/)).toBeTruthy()
      expect(screen.getByRole('button', { name: 'Tentar de novo' })).toBeTruthy()
    })

    it('"Registrar reclamação" abre o painel para quem pode e não existe para o Operador', async () => {
      rotas()
      renderizarDetalhe()
      fireEvent.click(await screen.findByRole('button', { name: 'Registrar reclamação' }))
      expect(screen.getByRole('form', { name: 'Registrar reclamação' })).toBeTruthy()
    })

    it('o Operador não vê "Registrar reclamação"', async () => {
      perfil = 'Operador'
      rotas()
      renderizarDetalhe()
      await screen.findByText('Fábrica Alfa')
      expect(screen.queryByRole('button', { name: 'Registrar reclamação' })).toBeNull()
    })

    it('depois de "Não repor" na reclamação, a seção e o cabeçalho recarregam e a pílula some', async () => {
      let dispensada = false
      const reclamacao = () => ({
        id: 3, pecaId: 812, descricao: 'Eixo', codigo: 'EX-1', quantidade: 2, texto: 'torto',
        dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 1, usuarioNome: 'Quê', estornada: false, estornavel: false,
        pedidoRetrabalho: null, aRepor: dispensada ? 0 : 2,
        dispensa: dispensada ? { id: 9, justificativa: 'ok', dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 1, usuarioNome: 'Quê' } : null,
      })
      rotas({
        '/api/pedidos/7': () => respostaJson({ ...PEDIDO, aRepor: dispensada ? 0 : 2 }),
        '/api/pedidos/7/reclamacoes': () => respostaJson([reclamacao()]),
        '/api/reclamacoes/3/dispensa': () => { dispensada = true; return respostaJson({ id: 9 }, 201) },
      })
      renderizarDetalhe()
      expect(await screen.findByText('2 a repor')).toBeTruthy()
      fireEvent.click(await screen.findByRole('button', { name: /Não repor/ }))
      const form = screen.getByRole('form', { name: 'Não repor' })
      fireEvent.change(within(form).getByLabelText('Justificativa'), { target: { value: 'ok' } })
      fireEvent.submit(form)
      await waitFor(() => expect(screen.queryByText('2 a repor')).toBeNull())
      expect(await screen.findByText('Não repor: ok')).toBeTruthy()
      expect(screen.queryByText('A repor')).toBeNull()
    })

    it('"Não repor" e "Desfazer" na baixa recarregam o cabeçalho: a pílula some e volta', async () => {
      let dispensada = false
      const baixaARepor = () => ({
        id: 1, motivo: 'PerdaArmazem', observacao: null, escopo: { tipo: 'Pedido', id: 7 }, tudo: true,
        dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 1, usuarioNome: 'Quê', estornada: false, estornavel: false,
        pedidoRetrabalho: null, aRepor: dispensada ? 0 : 2,
        dispensa: dispensada ? { id: 9, justificativa: 'sem custo', dataHora: '2026-11-04T10:00:00-03:00', usuarioId: 1, usuarioNome: 'Quê' } : null,
        linhas: [{ estruturaItemId: 5, descricao: 'Eixo', codigo: 'EX-1', origemPosicao: 'AIniciar', origemSetorId: null, origemSetorNome: null, origemOrdem: null, quantidade: 2 }],
      })
      rotas({
        '/api/pedidos/7': () => respostaJson({ ...PEDIDO, aRepor: dispensada ? 0 : 2 }),
        '/api/pedidos/7/baixas': () => respostaJson([baixaARepor()]),
        '/api/baixas/1/dispensa': () => { dispensada = true; return respostaJson({ id: 9 }, 201) },
        '/api/dispensas/9/estorno': () => { dispensada = false; return new Response(null, { status: 204 }) },
      })
      renderizarDetalhe()
      expect(await screen.findByText('2 a repor')).toBeTruthy()
      fireEvent.click(await screen.findByRole('button', { name: /Não repor/ }))
      const form = screen.getByRole('form', { name: 'Não repor' })
      fireEvent.change(within(form).getByLabelText('Justificativa'), { target: { value: 'sem custo' } })
      fireEvent.submit(form)
      expect(await screen.findByText('Não repor: sem custo')).toBeTruthy()
      await waitFor(() => expect(screen.queryByText('2 a repor')).toBeNull())
      fireEvent.click(screen.getByRole('button', { name: /Desfazer/ }))
      expect(await screen.findByText('2 a repor')).toBeTruthy()
      expect(screen.queryByText(/Não repor: sem custo/)).toBeNull()
    })
  })
})
