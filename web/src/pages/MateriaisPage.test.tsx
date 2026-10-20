// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MateriaisPage } from './MateriaisPage'
import { inicializar, _resetParaTeste } from '../api/client'
import { respostaJson, fetchPorRota } from '../testes/api'

afterEach(cleanup)

// O perfil da sessão passa a governar o que a tela mostra, então ele é variável de teste agora.
let perfil = 'Administrador'
vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({
    estado: { status: 'autenticado', usuario: { id: 1, nomeUsuario: 'u', nomeCompleto: 'U', perfil } },
    login: async () => {},
    logout: async () => {},
  }),
}))

const CHAPA = { id: 1, codigo: 'CH-001', descricao: 'Chapa de aco 3mm', unidadeMedida: 'KG', ativo: true }

// A tela abre em leitura: o formulário só existe depois do clique em "Novo material" (o botão do
// cabeçalho; o `<h2>` do painel tem o mesmo texto, por isso a busca é por papel).
async function abrirNovoMaterial() {
  fireEvent.click(await screen.findByRole('button', { name: 'Novo material' }))
}

async function esperarPainelFechar() {
  await waitFor(() => {
    expect(screen.queryByRole('form')).toBeNull()
  })
}

describe('MateriaisPage', () => {
  beforeEach(() => {
    perfil = 'Administrador'
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

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra os materiais que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    // M7 (achado da review da Task 8): sem esta linha, nenhuma asserção da suíte olhava o texto
    // da `Pilula` de unidade — o `KG` da fixture aparecia por acidente (sempre no `grep`, nunca
    // numa asserção), e trocar `{m.unidadeMedida}` por um campo inexistente sobrevivia.
    expect(await screen.findByText('KG')).toBeTruthy()
  })

  // I1 (achado da review de branch da 1B): `carregar` escreve `erro` no `catch` mas nunca o limpa
  // no caminho de sucesso. A carga inicial falha; marcar "Mostrar inativos" dispara uma recarga que
  // dá certo — a lista nova tem que aparecer E a mensagem tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([CHAPA])
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os materiais.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('CH-001')
    expect(screen.queryByText('Não foi possível carregar os materiais.')).toBeNull()
  })

  it('explica o 403 em vez do texto genérico', async () => {
    // A dívida da Task 2 chegando à tela: sem `mensagemDeErro`, quem recebesse 403 leria "Não foi
    // possível alterar o material" e tentaria de novo, indefinidamente.
    // m2 (achado da review da Task 8): depois do gating, quem não escreve `materiais` NÃO VÊ o
    // botão de inativar — este teste roda como `Administrador`. O 403 continua real como
    // fronteira (F2): perfil mudado no servidor depois do login, tabela do front
    // (`permissoes.ts`) defasada em relação ao backend, ou chamada feita por fora desta tela —
    // não alguém clicando um botão que a interface já esconde dele.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => respostaJson([CHAPA]),
      '/api/materiais/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('mostra estado vazio quando não há materiais', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum material cadastrado')).toBeTruthy()
    expect(await screen.findByText('Use o botão Novo material para criar o primeiro.')).toBeTruthy()
  })

  // C1 (achado da review da Task 8): a lista fica `[]` no `catch` (nunca é preenchida), então
  // `materiais.length === 0` sozinho também é verdade quando a causa é falha de rede — o "Nenhum
  // material cadastrado" apareceria JUNTO do banner de erro, afirmando um fato sobre o banco a
  // partir de uma falha de conexão.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => Promise.reject(new Error('rede caiu')) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    await screen.findByText('Não foi possível carregar os materiais.')
    expect(screen.queryByText('Nenhum material cadastrado')).toBeNull()
  })

  it('desabilita o botão enquanto o cadastro está em voo, e reabilita depois', async () => {
    // M4 (achado da review da Task 8, mesmo desenho da `SetoresPage`): o mock anterior pendurava
    // TODA chamada depois da primeira — inclusive a 3ª (o GET de recarga que `carregar` dispara
    // depois do `liberar`). O teste terminava sem nunca esperar o botão voltar, e por isso remover
    // o `finally { setEnviando(false) }` sobrevivia. Agora o mock CONTA as chamadas: 1ª (GET)
    // devolve a lista; 2ª (POST) devolve a promise que `liberar` resolve; 3ª (GET de recarga)
    // resolve normal.
    let liberar: (r: Response) => void = () => {}
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return new Promise<Response>((r) => { liberar = r })
        return respostaJson([{ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }])
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil retangular' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    liberar(respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }, 201))

    // No sucesso o painel fecha, então o botão some junto: a reabilitação se prova ao reabrir o
    // painel, depois da recarga. `salvar` solta o envio em dois lugares (antes da recarga e no
    // `finally`), e aqui qualquer um dos dois basta: o teste morre só sem os dois, quando o submit
    // novo nasceria preso em "Salvando…". Que a liberação vem ANTES da recarga é o teste
    // `salvar solta o envio antes da recarga…` que prova.
    await esperarPainelFechar()
    await abrirNovoMaterial()
    const botaoDepois = await screen.findByText('Adicionar')
    expect((botaoDepois as HTMLButtonElement).disabled).toBe(false)
  })

  // I3 (achado da review da Task 8): o segundo cadastro tem de começar vazio. Sem a limpeza, os
  // campos continuariam com o valor cadastrado e um segundo clique tentaria recriar o mesmo código
  // — 409 sobre o cadastro que a própria tela acabou de fazer.
  it('limpa o formulário depois de cadastrar com sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        // 1ª chamada = GET inicial; 2ª = POST do cadastro; 3ª = GET da recarga que `salvar`
        // dispara no sucesso — as duas GETs precisam devolver ARRAY, senão `materiais.map`
        // quebra no próximo render.
        if (chamadas === 2) {
          return respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil retangular', unidadeMedida: 'M', ativo: true }, 201)
        }
        return respostaJson([])
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil retangular' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    // O painel fecha no sucesso, então os campos só voltam à tela quando ele é reaberto. O que se
    // prova é o que o usuário vê: o painel reaberto vem vazio. NÃO prova qual função limpou,
    // porque tanto o fechamento quanto a abertura do painel (`fecharPainel` e `abrirPainel`) zeram
    // o formulário, e cada uma sozinha basta para este teste.
    await esperarPainelFechar()
    await abrirNovoMaterial()
    expect((await screen.findByLabelText('Código') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Unidade') as HTMLInputElement).value).toBe('')
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    // PCP lê materiais mas não escreve — `[Authorize(Roles = "Administrador")]` no backend.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    expect(screen.queryByLabelText('Código')).toBeNull()
    // A tela abre em leitura, então a ausência do campo, sozinha, já é verdade para qualquer
    // perfil: o que prende o gating é a ausência do botão que abre o painel.
    expect(screen.queryByRole('button', { name: 'Novo material' })).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
  })

  it('abre em leitura: sem formulario antes do clique', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Novo material' })).toBeNull()
    expect(screen.queryByLabelText('Código')).toBeNull()
  })

  it('Novo material abre o painel e some enquanto ele esta aberto', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()

    expect(screen.getByRole('form', { name: 'Novo material' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Novo material' })).toBeNull()
  })

  it('Cancelar fecha o painel e descarta o digitado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('form', { name: 'Novo material' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Novo material' })).toBeTruthy()
    await abrirNovoMaterial()
    expect((screen.getByLabelText('Código') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Descrição') as HTMLInputElement).value).toBe('')
  })

  it('salvar com sucesso fecha o painel, volta a Mais recentes e o material novo e o primeiro', async () => {
    const ANTIGO = { id: 1, codigo: 'AA-001', descricao: 'Antigo', unidadeMedida: 'UN', ativo: true }
    const NOVO = { id: 9, codigo: 'ZZ-009', descricao: 'Novo', unidadeMedida: 'UN', ativo: true }
    let criou = false
    const fetchMock = fetchPorRota({
      '/api/materiais': () => {
        // O POST e o GET dividem o caminho; o método é o que os distingue.
        const ultima = fetchMock.mock.calls[fetchMock.mock.calls.length - 1][1] as RequestInit | undefined
        if (ultima?.method === 'POST') { criou = true; return respostaJson(NOVO, 201) }
        return respostaJson(criou ? [ANTIGO, NOVO] : [ANTIGO])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('AA-001')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
    fireEvent.click(screen.getByLabelText('Mostrar inativos'))
    await waitFor(() => {
      expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=true')
    })

    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'ZZ-009' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Novo' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'UN' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    await waitFor(() => {
      expect(screen.getAllByRole('listitem')[0].textContent).toContain('ZZ-009')
    })
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('recentes')
    expect((screen.getByLabelText('Mostrar inativos') as HTMLInputElement).checked).toBe(false)
    expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=false')
  })

  it('conflito mantem o painel aberto com o erro e o Reativar dentro dele', async () => {
    const base = fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) })
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 9 }, 409))
        : base(url, init))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const painel = await screen.findByRole('form', { name: 'Novo material' })
    expect(await within(painel).findByText('Já existe um material com o código "PR-001" inativo.')).toBeTruthy()
    expect(within(painel).getByRole('button', { name: 'Reativar o existente' })).toBeTruthy()
  })

  it('reativar com sucesso fecha o painel e volta a Mais recentes', async () => {
    const REATIVADO = { id: 9, codigo: 'PR-001', descricao: 'Perfil', unidadeMedida: 'M', ativo: true }
    let reativou = false
    const base = fetchPorRota({
      '/api/materiais': () => respostaJson(reativou ? [CHAPA, REATIVADO] : [CHAPA]),
      '/api/materiais/9/ativo': () => { reativou = true; return respostaJson({}, 200) },
    })
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 9 }, 409))
        : base(url, init))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('CH-001')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))
    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await esperarPainelFechar()
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('recentes')
    expect(await screen.findByText('PR-001')).toBeTruthy()
  })

  it('salvar solta o envio antes da recarga: reabrir o painel durante ela mostra Adicionar habilitado', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return respostaJson({ id: 2, codigo: 'PR-001', descricao: 'Perfil', unidadeMedida: 'M', ativo: true }, 201)
        // A recarga pós-salvar nunca termina: o que se mede é o estado do painel enquanto ela voa.
        return new Promise<Response>(() => {})
      },
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    await waitFor(() => { expect(chamadas).toBe(3) })
    await abrirNovoMaterial()
    expect((screen.getByRole('button', { name: 'Adicionar' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('falha de rede ao salvar mantem o painel aberto com o erro dentro dele', async () => {
    const base = fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST' ? Promise.reject(new Error('rede caiu')) : base(url, init)))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const painel = await screen.findByRole('form', { name: 'Novo material' })
    expect(await within(painel).findByText('Não foi possível salvar o material.')).toBeTruthy()
  })

  it('erro de Inativar aparece fora do painel', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/materiais': () => respostaJson([CHAPA]),
      '/api/materiais/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    const banner = await screen.findByText('Seu perfil não tem permissão para esta ação.')
    expect(screen.queryByRole('form')).toBeNull()
    expect(banner.getAttribute('role')).toBe('alert')
  })

  it('ordenar por codigo e por descricao reordena no cliente sem nova requisicao', async () => {
    const fetchMock = fetchPorRota({
      // As três ordens são diferentes entre si, até no primeiro item: "Mais recentes" (Id
      // decrescente) dá C-03, A-01, B-02; código dá A-01, B-02, C-03; descrição dá B-02 (Alumínio),
      // A-01 (Latão), C-03 (Zinco). Uma ordem trocada por outra não passa.
      '/api/materiais': () => respostaJson([
        { id: 1, codigo: 'B-02', descricao: 'Alumínio', unidadeMedida: 'KG', ativo: true },
        { id: 3, codigo: 'C-03', descricao: 'Zinco', unidadeMedida: 'KG', ativo: true },
        { id: 2, codigo: 'A-01', descricao: 'Latão', unidadeMedida: 'KG', ativo: true },
      ]),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await screen.findByText('C-03')
    const linhas = () => screen.getAllByRole('listitem').map((li) => li.textContent ?? '')
    expect(linhas()[0]).toContain('C-03')
    const antes = fetchMock.mock.calls.length

    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
    expect(linhas()[0]).toContain('A-01')
    expect(linhas()[1]).toContain('B-02')
    expect(linhas()[2]).toContain('C-03')

    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'descricao' } })
    expect(linhas()[0]).toContain('Alumínio')
    expect(linhas()[1]).toContain('Latão')
    expect(linhas()[2]).toContain('Zinco')
    expect(fetchMock.mock.calls.length).toBe(antes)
  })

  it('quem nao pode escrever ve a lista e o seletor de ordem, e nao ve o botao Novo material', async () => {
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

    expect(await screen.findByText('CH-001')).toBeTruthy()
    expect(screen.getByLabelText('Ordenar por')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Novo material' })).toBeNull()
  })

  it('Cancelar devolve o foco ao Novo material', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Novo material' }))
  })

  it('com o cadastro em voo, Cancelar fica desabilitado', async () => {
    const base = fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST' ? new Promise<Response>(() => {}) : base(url, init)))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await screen.findByText('Salvando…')
    const cancelar = screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement
    expect(cancelar.disabled).toBe(true)
    fireEvent.click(cancelar)
    expect(screen.getByRole('form', { name: 'Novo material' })).toBeTruthy()
  })

  it('com o Reativar o existente em voo, Cancelar fica desabilitado', async () => {
    const base = fetchPorRota({ '/api/materiais': () => respostaJson([CHAPA]) })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 9 }, 409))
      }
      if (String(url).includes('/ativo')) return new Promise<Response>(() => {})
      return base(url, init)
    }))

    render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
    await abrirNovoMaterial()
    fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
    fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
    fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
    fireEvent.click(screen.getByText('Adicionar'))
    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    })
  })
  // "Fica em" e edição (Fase 4). O `GET /setores` só é pedido para quem escreve, e só traz os ativos.
  describe('Fica em e edição', () => {
    const CORTE = { id: 3, nome: 'Corte a Laser', atividade: null, utilizaKit: false, ativo: true }
    const SOLDA = { id: 4, nome: 'Solda', atividade: null, utilizaKit: true, ativo: true }
    const NO_CORTE = { ...CHAPA, ficaEm: { setorId: 3, nome: 'Corte a Laser' } }
    const NO_ALMOXARIFADO = { ...CHAPA, ficaEm: null }

    // Mock com os três recursos; `aoEscrever` recebe o corpo do POST/PUT de /materiais.
    function mockDaTela(materiais: unknown[], opcoes: { setores?: unknown[]; aoEscrever?: (metodo: string, corpo: unknown) => Response } = {}) {
      const fetchMock = fetchPorRota({
        '/api/setores': () => respostaJson(opcoes.setores ?? [CORTE, SOLDA]),
        '/api/materiais': () => {
          const init = fetchMock.mock.calls[fetchMock.mock.calls.length - 1][1] as RequestInit | undefined
          if (init?.method === 'POST' && opcoes.aoEscrever) return opcoes.aoEscrever('POST', JSON.parse(String(init.body)))
          return respostaJson(materiais)
        },
        '/api/materiais/1': () => {
          const init = fetchMock.mock.calls[fetchMock.mock.calls.length - 1][1] as RequestInit
          return opcoes.aoEscrever
            ? opcoes.aoEscrever(String(init.method), JSON.parse(String(init.body)))
            : respostaJson(NO_CORTE)
        },
      })
      vi.stubGlobal('fetch', fetchMock)
      return fetchMock
    }

    function corpoDaEscrita(fetchMock: ReturnType<typeof mockDaTela>, metodo: string) {
      const chamada = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === metodo)
      expect(chamada).toBeTruthy()
      return JSON.parse(String((chamada![1] as RequestInit).body))
    }

    it('o painel de novo material traz "Fica em" com Almoxarifado e os setores ativos', async () => {
      mockDaTela([NO_ALMOXARIFADO])

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      await abrirNovoMaterial()

      const campo = await screen.findByRole('combobox', { name: 'Fica em' }) as HTMLSelectElement
      await waitFor(() => {
        expect(within(campo).getAllByRole('option').map((o) => o.textContent))
          .toEqual(['Almoxarifado', 'Corte a Laser', 'Solda'])
      })
      expect(campo.value).toBe('')
    })

    it('salvar com um setor manda o setorDeEstoqueId, e Almoxarifado manda null', async () => {
      const fetchMock = mockDaTela([], { aoEscrever: () => respostaJson({ ...CHAPA, id: 2, ficaEm: null }, 201) })

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      const preencher = async () => {
        await abrirNovoMaterial()
        fireEvent.change(await screen.findByLabelText('Código'), { target: { value: 'PR-001' } })
        fireEvent.change(screen.getByLabelText('Descrição'), { target: { value: 'Perfil' } })
        fireEvent.change(screen.getByLabelText('Unidade'), { target: { value: 'M' } })
      }

      await preencher()
      await screen.findByRole('option', { name: 'Corte a Laser' })
      fireEvent.change(screen.getByRole('combobox', { name: 'Fica em' }), { target: { value: '3' } })
      fireEvent.click(screen.getByText('Adicionar'))
      await esperarPainelFechar()
      expect(corpoDaEscrita(fetchMock, 'POST').setorDeEstoqueId).toBe(3)

      fetchMock.mockClear()
      await preencher()
      fireEvent.click(screen.getByText('Adicionar'))
      await esperarPainelFechar()
      const corpo = corpoDaEscrita(fetchMock, 'POST')
      expect('setorDeEstoqueId' in corpo).toBe(true)
      expect(corpo.setorDeEstoqueId).toBeNull()
    })

    it('a lista mostra "Fica em: Corte a Laser" só quando o material não é do almoxarifado', async () => {
      mockDaTela([
        NO_CORTE,
        { id: 2, codigo: 'PR-001', descricao: 'Perfil', unidadeMedida: 'M', ativo: true, ficaEm: null },
      ])

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      await screen.findByText('CH-001')

      const itens = screen.getAllByRole('listitem')
      const doCorte = itens.find((li) => li.textContent?.includes('CH-001'))!
      const doAlmoxarifado = itens.find((li) => li.textContent?.includes('PR-001'))!
      expect(within(doCorte).getByText('Fica em: Corte a Laser')).toBeTruthy()
      expect(within(doAlmoxarifado).queryByText(/Fica em/)).toBeNull()
    })

    it('Editar abre "Editar material" com os campos do material e salva por PUT', async () => {
      const fetchMock = mockDaTela([NO_CORTE], {
        aoEscrever: () => respostaJson({ ...NO_CORTE, descricao: 'Chapa de aco 5mm' }),
      })

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))

      const painel = screen.getByRole('form', { name: 'Editar material' })
      expect((within(painel).getByLabelText('Código') as HTMLInputElement).value).toBe('CH-001')
      expect((within(painel).getByLabelText('Descrição') as HTMLInputElement).value).toBe('Chapa de aco 3mm')
      expect((within(painel).getByLabelText('Unidade') as HTMLInputElement).value).toBe('KG')
      await within(painel).findByRole('option', { name: 'Solda' })
      expect((within(painel).getByRole('combobox', { name: 'Fica em' }) as HTMLSelectElement).value).toBe('3')

      fireEvent.change(within(painel).getByLabelText('Descrição'), { target: { value: 'Chapa de aco 5mm' } })
      fireEvent.change(within(painel).getByRole('combobox', { name: 'Fica em' }), { target: { value: '4' } })
      fireEvent.click(within(painel).getByRole('button', { name: 'Salvar alterações' }))

      await esperarPainelFechar()
      const chamada = fetchMock.mock.calls.find((c) => (c[1] as RequestInit | undefined)?.method === 'PUT')!
      expect(String(chamada[0])).toBe('/api/materiais/1')
      expect(corpoDaEscrita(fetchMock, 'PUT')).toEqual({
        codigo: 'CH-001', descricao: 'Chapa de aco 5mm', unidadeMedida: 'KG', setorDeEstoqueId: 4,
      })
    })

    // O PUT é substituição inteira: omitir o campo devolveria o material ao almoxarifado.
    it('o PUT da edição leva sempre o setorDeEstoqueId: o atual, ou null no almoxarifado', async () => {
      const fetchMock = mockDaTela([NO_CORTE, { ...CHAPA, id: 2, codigo: 'PR-001', ficaEm: null }])
      fetchMock.mockClear()

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))
      await screen.findByRole('option', { name: 'Solda' })
      fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
      await esperarPainelFechar()
      expect(corpoDaEscrita(fetchMock, 'PUT').setorDeEstoqueId).toBe(3)
    })

    it('editar um material do almoxarifado salva setorDeEstoqueId null', async () => {
      const fetchMock = mockDaTela([NO_ALMOXARIFADO], { aoEscrever: () => respostaJson(NO_ALMOXARIFADO) })

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))
      await screen.findByRole('option', { name: 'Solda' })
      fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
      await esperarPainelFechar()
      const corpo = corpoDaEscrita(fetchMock, 'PUT')
      expect('setorDeEstoqueId' in corpo).toBe(true)
      expect(corpo.setorDeEstoqueId).toBeNull()
    })

    it('editar recarrega mantendo a ordem e os inativos', async () => {
      const fetchMock = mockDaTela([NO_CORTE], { aoEscrever: () => respostaJson(NO_CORTE) })

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      await screen.findByText('CH-001')
      fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'codigo' } })
      fireEvent.click(screen.getByLabelText('Mostrar inativos'))
      await waitFor(() => {
        expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=true')
      })
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))
      fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
      await esperarPainelFechar()

      await waitFor(() => {
        expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=true')
      })
      expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('codigo')
      expect((screen.getByLabelText('Mostrar inativos') as HTMLInputElement).checked).toBe(true)
    })

    it('conflito de código ao editar mostra o erro dentro do painel, sem Reativar o existente', async () => {
      mockDaTela([NO_CORTE], {
        aoEscrever: () => respostaJson({ erro: 'ValorDuplicado', campo: 'codigo', existeInativo: true, idExistente: 9 }, 409),
      })

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))
      fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))

      const painel = await screen.findByRole('form', { name: 'Editar material' })
      expect(await within(painel).findByText('Já existe um material com este código.')).toBeTruthy()
      expect(within(painel).queryByRole('button', { name: 'Reativar o existente' })).toBeNull()
    })

    it('ao fechar a edição o foco volta ao Editar daquele material', async () => {
      mockDaTela([NO_CORTE, { ...CHAPA, id: 2, codigo: 'PR-001', ficaEm: null }])

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar PR-001' }))
      fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar PR-001' }))
    })

    it('salvar a edição devolve o foco ao Editar do mesmo material depois da recarga', async () => {
      mockDaTela([NO_CORTE], { aoEscrever: () => respostaJson(NO_CORTE) })

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))
      fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
      await esperarPainelFechar()

      await waitFor(() => {
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar CH-001' }))
      })
    })

    it('o Fica em de um setor inativo aparece selecionado na edição', async () => {
      // Review Focus 4: o `GET /setores` só traz ativos; sem a opção extra o <select> cairia em
      // "Almoxarifado" e salvar trocaria o setor em silêncio.
      const fetchMock = mockDaTela(
        [{ ...CHAPA, ficaEm: { setorId: 7, nome: 'Setor Antigo' } }],
        { aoEscrever: () => respostaJson(CHAPA) },
      )

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))

      const campo = await screen.findByRole('combobox', { name: 'Fica em' }) as HTMLSelectElement
      await within(campo).findByRole('option', { name: 'Solda' })
      expect(campo.selectedOptions[0].textContent).toBe('Setor Antigo (inativo)')
      expect(campo.value).toBe('7')

      fireEvent.click(screen.getByRole('button', { name: 'Salvar alterações' }))
      await esperarPainelFechar()
      expect(corpoDaEscrita(fetchMock, 'PUT').setorDeEstoqueId).toBe(7)
    })

    it('um Setor inativo não aparece como opção no painel de novo material', async () => {
      mockDaTela([{ ...CHAPA, ficaEm: { setorId: 7, nome: 'Setor Antigo' } }])

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      await abrirNovoMaterial()
      await screen.findByRole('option', { name: 'Solda' })

      expect(screen.queryByRole('option', { name: /Setor Antigo/ })).toBeNull()
    })

    it('falha ao carregar os setores avisa dentro do painel e mantém o setor atual na edição', async () => {
      vi.stubGlobal('fetch', fetchPorRota({
        '/api/setores': () => Promise.reject(new Error('rede caiu')),
        '/api/materiais': () => respostaJson([NO_CORTE]),
      }))

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)
      fireEvent.click(await screen.findByRole('button', { name: 'Editar CH-001' }))

      const painel = screen.getByRole('form', { name: 'Editar material' })
      expect(await within(painel).findByText('Não foi possível carregar os setores.')).toBeTruthy()
      expect((within(painel).getByRole('combobox', { name: 'Fica em' }) as HTMLSelectElement).value).toBe('3')
    })

    it('quem não escreve em materiais não vê Editar nem pede os setores', async () => {
      perfil = 'PCP'
      const fetchMock = mockDaTela([NO_CORTE])

      render(<MemoryRouter><MateriaisPage /></MemoryRouter>)

      expect(await screen.findByText('CH-001')).toBeTruthy()
      expect(screen.queryByRole('button', { name: /^Editar/ })).toBeNull()
      expect(fetchMock.mock.calls.some((c) => String(c[0]).startsWith('/api/setores'))).toBe(false)
    })
  })
})
