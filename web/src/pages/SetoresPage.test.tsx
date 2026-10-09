// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { SetoresPage } from './SetoresPage'
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

const CORTE = { id: 1, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false }

// A tela abre em leitura: o formulário só existe depois do clique em "Novo setor" (o botão do
// cabeçalho; o `<h2>` do painel tem o mesmo texto, por isso a busca é por papel).
async function abrirNovoSetor() {
  fireEvent.click(await screen.findByRole('button', { name: 'Novo setor' }))
}

async function esperarPainelFechar() {
  await waitFor(() => {
    expect(screen.queryByRole('form')).toBeNull()
  })
}

describe('SetoresPage', () => {
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

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(screen.getByRole('status').textContent).toBe('Carregando…')
  })

  it('mostra os setores que a API devolveu', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
  })

  // I1 (achado da review de branch da 1B): `carregar` escreve `erro` no `catch` mas nunca o limpa
  // no caminho de sucesso. A carga inicial falha; marcar "Mostrar inativos" dispara uma recarga que
  // dá certo — a lista nova tem que aparecer E a mensagem tem que sumir.
  it('limpa a mensagem de erro da carga inicial quando uma recarga subsequente tem sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return Promise.reject(new Error('rede caiu'))
        return respostaJson([CORTE])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Não foi possível carregar os setores.')

    fireEvent.click(screen.getByLabelText('Mostrar inativos'))

    await screen.findByText('Corte')
    expect(screen.queryByText('Não foi possível carregar os setores.')).toBeNull()
  })

  it('explica o 403 em vez do texto genérico', async () => {
    // A dívida da Task 2 chegando à tela: sem `mensagemDeErro`, quem recebesse 403 leria "Não foi
    // possível alterar o setor" e tentaria de novo, indefinidamente.
    // m2 (achado da review da Task 8): depois do gating, o Operador NÃO VÊ o botão de inativar —
    // este teste roda como `Administrador`. O 403 continua real como fronteira (F2): perfil
    // mudado no servidor depois do login, tabela do front (`permissoes.ts`) defasada em relação
    // ao backend, ou chamada feita por fora desta tela — não um Operador clicando um botão que a
    // interface já esconde dele.
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([CORTE]),
      '/api/setores/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    expect(await screen.findByText('Seu perfil não tem permissão para esta ação.')).toBeTruthy()
  })

  it('mostra estado vazio quando não há setores', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Nenhum setor cadastrado')).toBeTruthy()
    // m3 (achado da review da Task 8): a `descricao` do `EstadoVazio` (`podeEscrever ? '…' :
    // undefined`) não tinha cobertura em nenhum dos dois ramos. Este é o positivo — o ator
    // `Administrador` já está no teste, então prende a decisão sem inventar um caso novo.
    expect(await screen.findByText('Use o botão Novo setor para criar o primeiro.')).toBeTruthy()
  })

  // C1 (achado da review da Task 8): a lista fica `[]` no `catch` (nunca é preenchida), então
  // `setores.length === 0` sozinho também é verdade quando a causa é falha de rede — o "Nenhum
  // setor cadastrado" apareceria JUNTO do banner de erro, afirmando um fato sobre o banco a
  // partir de uma falha de conexão.
  it('não mostra o estado vazio quando a listagem falha', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => Promise.reject(new Error('rede caiu')) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    await screen.findByText('Não foi possível carregar os setores.')
    expect(screen.queryByText('Nenhum setor cadastrado')).toBeNull()
  })

  it('desabilita o botão enquanto o cadastro está em voo, e reabilita depois', async () => {
    // M4 (achado da review da Task 8): o desenho anterior pendurava TODA chamada depois da
    // primeira — inclusive a 3ª (o GET de recarga que `carregar` dispara depois do `liberar`).
    // O teste terminava sem nunca esperar o botão voltar, e por isso remover o
    // `finally { setEnviando(false) }` sobrevivia. Agora o mock CONTA as chamadas: 1ª (GET) devolve
    // a lista; 2ª (POST) devolve a promise que `liberar` resolve; 3ª (GET de recarga) resolve
    // normal — e só depois disso a asserção de reabilitação faz sentido.
    let liberar: (r: Response) => void = () => {}
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return new Promise<Response>((r) => { liberar = r })
        return respostaJson([{ id: 2, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const botao = await screen.findByText('Salvando…')
    expect((botao as HTMLButtonElement).disabled).toBe(true)

    liberar(respostaJson({ id: 2, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }, 201))

    // No sucesso o painel fecha, então o botão some junto: a reabilitação se prova ao reabrir o
    // painel, depois da recarga. `salvar` solta o envio em dois lugares (antes da recarga e no
    // `finally`), e aqui qualquer um dos dois basta: o teste morre só sem os dois, quando o submit
    // novo nasceria preso em "Salvando…". Que a liberação vem ANTES da recarga é o teste
    // `salvar solta o envio antes da recarga…` que prova.
    await esperarPainelFechar()
    await abrirNovoSetor()
    const botaoDepois = await screen.findByText('Adicionar')
    expect((botaoDepois as HTMLButtonElement).disabled).toBe(false)
  })

  // I3 (achado da review da Task 8): o segundo cadastro tem de começar vazio. Sem a limpeza, o
  // campo continuaria com o valor cadastrado e um segundo clique tentaria recriar o mesmo nome —
  // 409 sobre o cadastro que a própria tela acabou de fazer.
  it('limpa o campo depois de cadastrar com sucesso', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        // 1ª chamada = GET inicial; 2ª = POST do cadastro; 3ª = GET da recarga que `salvar`
        // dispara no sucesso — as duas GETs precisam devolver ARRAY, senão `setores.map`
        // quebra no próximo render.
        if (chamadas === 2) return respostaJson({ id: 2, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }, 201)
        return respostaJson([])
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    // O painel fecha no sucesso, então o campo só volta à tela quando ele é reaberto. O que se
    // prova é o que o usuário vê: o painel reaberto vem vazio. NÃO prova qual função limpou,
    // porque tanto o fechamento quanto a abertura do painel (`fecharPainel` e `abrirNovo`) zeram
    // o nome, e cada uma sozinha basta para este teste.
    await esperarPainelFechar()
    await abrirNovoSetor()
    expect((await screen.findByLabelText('Nome do setor') as HTMLInputElement).value).toBe('')
  })

  it('esconde formulário e ação de inativar para quem não pode escrever', async () => {
    // PCP lê setores mas não escreve — `[Authorize(Roles = "Administrador")]` no backend. O link
    // continua no shell de propósito; o que some é a ação.
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
    expect(screen.queryByLabelText('Nome do setor')).toBeNull()
    expect(screen.queryByText('Inativar')).toBeNull()
    expect(screen.queryByText('Editar')).toBeNull()
  })

  it('cadastrar manda a atividade nula quando o campo fica vazio', async () => {
    let chamadas = 0
    const fetchMock = fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 2) return respostaJson({ id: 2, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }, 201)
        return respostaJson([])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    const corpo = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)
    expect(corpo).toEqual({ nome: 'Solda', atividade: null, utilizaKit: false })
  })

  it('cadastrar manda o texto da atividade quando o campo está preenchido', async () => {
    let chamadas = 0
    const fetchMock = fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 2) return respostaJson({ id: 2, nome: 'Solda', ativo: true, atividade: 'montagem', utilizaKit: false }, 201)
        return respostaJson([])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.change(screen.getByLabelText('Atividade (opcional)'), { target: { value: 'montagem' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    const corpo = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)
    expect(corpo).toEqual({ nome: 'Solda', atividade: 'montagem', utilizaKit: false })
  })

  it('"Editar" carrega nome e atividade no painel, e "Salvar alterações" faz PUT e recarrega', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: 'solda', utilizaKit: false }
    let chamadasDeLista = 0
    const fetchMock = fetchPorRota({
      '/api/setores': () => {
        chamadasDeLista += 1
        return respostaJson([SOLDA])
      },
      '/api/setores/3': () => respostaJson({ ...SOLDA, atividade: 'montagem', utilizaKit: false }, 200),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))

    expect((screen.getByLabelText('Nome do setor') as HTMLInputElement).value).toBe('Solda')
    expect((screen.getByLabelText('Atividade (opcional)') as HTMLInputElement).value).toBe('solda')

    fireEvent.change(screen.getByLabelText('Atividade (opcional)'), { target: { value: 'montagem' } })
    fireEvent.click(screen.getByText('Salvar alterações'))

    await esperarPainelFechar()
    const chamadaPut = fetchMock.mock.calls.find((c) => String(c[0]).endsWith('/api/setores/3'))!
    expect((chamadaPut[1] as RequestInit).method).toBe('PUT')
    expect(JSON.parse((chamadaPut[1] as RequestInit).body as string)).toEqual({ nome: 'Solda', atividade: 'montagem', utilizaKit: false })
    expect(chamadasDeLista).toBe(2)
  })

  it('conflito ao editar com homônimo inativo não oferece "Reativar o existente"', async () => {
    // A oferta de reativar é ação de CRIAÇÃO (o `!editando &&` de `salvar`): editar para um nome
    // que colide com outro Setor inativo não é o mesmo caso — o `existeInativo` do 409 aqui se
    // refere ao homônimo, não ao próprio Setor em edição, então reativá-lo não resolveria nada.
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: 'solda', utilizaKit: false }
    const fetchMock = fetchPorRota({
      '/api/setores': () => respostaJson([SOLDA]),
      '/api/setores/3': () => respostaJson(
        { erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 9 }, 409,
      ),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))
    fireEvent.click(screen.getByText('Salvar alterações'))

    expect(await screen.findByText('Já existe um setor com este nome.')).toBeTruthy()
    expect(screen.queryByText('Reativar o existente')).toBeNull()
  })

  it('"Cancelar" na edição fecha o painel, e o painel de novo setor abre com o formulário vazio', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: 'solda', utilizaKit: false }
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([SOLDA]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))
    expect(screen.getByText('Salvar alterações')).toBeTruthy()

    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('form')).toBeNull()
    await abrirNovoSetor()
    expect(screen.getByText('Adicionar')).toBeTruthy()
    expect((screen.getByLabelText('Nome do setor') as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText('Atividade (opcional)') as HTMLInputElement).value).toBe('')
  })

  it('a lista mostra a atividade ao lado do nome, e nada ao lado de quem não tem uma', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([
        { id: 3, nome: 'Solda', ativo: true, atividade: 'montagem', utilizaKit: false },
        { id: 4, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false },
      ]),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('· montagem')).toBeTruthy()
    const linhaDoCorte = screen.getByText('Corte').closest('li')!
    expect(within(linhaDoCorte).queryByText(/·/)).toBeNull()
  })

  it('cadastrar com Utiliza Kit marcado manda utilizaKit verdadeiro', async () => {
    let chamadas = 0
    const fetchMock = fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 2) return respostaJson({ id: 2, nome: 'Solda', ativo: true, atividade: null, utilizaKit: true }, 201)
        return respostaJson([])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    const caixa = screen.getByRole('checkbox', { name: 'Utiliza Kit' }) as HTMLInputElement
    expect(caixa.checked).toBe(false)
    fireEvent.click(caixa)
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    const corpo = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)
    expect(corpo).toEqual({ nome: 'Solda', atividade: null, utilizaKit: true })
  })

  it('editar abre com a caixa no valor do setor, e desmarcar manda utilizaKit falso no PUT', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: true }
    const fetchMock = fetchPorRota({
      '/api/setores': () => respostaJson([SOLDA]),
      '/api/setores/3': () => respostaJson({ ...SOLDA, utilizaKit: false }, 200),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))

    const caixa = screen.getByRole('checkbox', { name: 'Utiliza Kit' }) as HTMLInputElement
    expect(caixa.checked).toBe(true)
    fireEvent.click(caixa)
    fireEvent.click(screen.getByText('Salvar alterações'))

    await esperarPainelFechar()
    const chamadaPut = fetchMock.mock.calls.find((c) => String(c[0]).endsWith('/api/setores/3'))!
    expect((chamadaPut[1] as RequestInit).method).toBe('PUT')
    expect(JSON.parse((chamadaPut[1] as RequestInit).body as string))
      .toEqual({ nome: 'Solda', atividade: null, utilizaKit: false })
  })

  it('editar um setor sem a marca abre com a caixa desmarcada, e abrir "Novo setor" depois também', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: true }
    const CORTE_SEM_MARCA = { id: 1, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false }
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([SOLDA, CORTE_SEM_MARCA]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))
    expect((screen.getByRole('checkbox', { name: 'Utiliza Kit' }) as HTMLInputElement).checked).toBe(true)

    fireEvent.click(screen.getByText('Cancelar'))
    await abrirNovoSetor()
    expect((screen.getByRole('checkbox', { name: 'Utiliza Kit' }) as HTMLInputElement).checked).toBe(false)
  })

  it('setor com Utiliza Kit mostra a pílula neutra na lista, e quem não tem a marca não mostra', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([
        { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: true },
        { id: 4, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false },
      ]),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    const linhaDaSolda = (await screen.findByText('Solda')).closest('li')!
    const pilula = within(linhaDaSolda).getByText('Utiliza Kit')
    const classes = pilula.className.split(/\s+/)
    expect(classes).toContain('bg-acao-fundo')
    expect(classes).toContain('text-acao')
    const linhaDoCorte = screen.getByText('Corte').closest('li')!
    expect(within(linhaDoCorte).queryByText('Utiliza Kit')).toBeNull()
  })

  it('abre em leitura: sem formulario antes do clique', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
    expect(screen.queryByRole('form', { name: 'Novo setor' })).toBeNull()
    expect(screen.queryByLabelText('Nome do setor')).toBeNull()
  })

  it('Novo setor abre o painel e some enquanto ele esta aberto', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()

    expect(screen.getByRole('form', { name: 'Novo setor' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Novo setor' })).toBeNull()
  })

  it('Cancelar fecha o painel e descarta o digitado', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Cancelar'))

    expect(screen.queryByRole('form', { name: 'Novo setor' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Novo setor' })).toBeTruthy()
    await abrirNovoSetor()
    expect((screen.getByLabelText('Nome do setor') as HTMLInputElement).value).toBe('')
  })

  it('salvar novo com sucesso fecha o painel, volta a Mais recentes e o setor novo e o primeiro', async () => {
    const ANTIGO = { id: 1, nome: 'Antigo', ativo: true, atividade: null, utilizaKit: false }
    const NOVO = { id: 9, nome: 'Zeta', ativo: true, atividade: null, utilizaKit: false }
    let criou = false
    const fetchMock = fetchPorRota({
      '/api/setores': () => {
        // O POST e o GET dividem o caminho; o método é o que os distingue.
        const ultima = fetchMock.mock.calls[fetchMock.mock.calls.length - 1][1] as RequestInit | undefined
        if (ultima?.method === 'POST') { criou = true; return respostaJson(NOVO, 201) }
        return respostaJson(criou ? [ANTIGO, NOVO] : [ANTIGO])
      },
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Antigo')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'nome' } })
    fireEvent.click(screen.getByLabelText('Mostrar inativos'))
    await waitFor(() => {
      expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=true')
    })

    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Zeta' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    await waitFor(() => {
      expect(screen.getAllByRole('listitem')[0].textContent).toContain('Zeta')
    })
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('recentes')
    expect((screen.getByLabelText('Mostrar inativos') as HTMLInputElement).checked).toBe(false)
    expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=false')
  })

  it('Editar abre o painel com o nome preenchido e o titulo Editar setor', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: 'solda', utilizaKit: false }
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([SOLDA]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))

    const painel = screen.getByRole('form', { name: 'Editar setor' })
    expect((within(painel).getByLabelText('Nome do setor') as HTMLInputElement).value).toBe('Solda')
    expect(within(painel).getByText('Solda', { selector: 'p' })).toBeTruthy()
    expect(within(painel).getByText('Salvar alterações')).toBeTruthy()
  })

  it('salvar edicao com sucesso fecha o painel e mantem a ordem escolhida', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }
    const fetchMock = fetchPorRota({
      '/api/setores': () => respostaJson([SOLDA]),
      '/api/setores/3': () => respostaJson(SOLDA),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Solda')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'nome' } })
    fireEvent.click(screen.getByLabelText('Mostrar inativos'))
    await waitFor(() => {
      expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('incluirInativos=true')
    })

    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))
    fireEvent.click(screen.getByText('Salvar alterações'))

    await esperarPainelFechar()
    await waitFor(() => {
      expect(String(fetchMock.mock.calls[fetchMock.mock.calls.length - 1][0])).toContain('/api/setores?incluirInativos=true')
    })
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('nome')
    expect((screen.getByLabelText('Mostrar inativos') as HTMLInputElement).checked).toBe(true)
  })

  it('Editar outro setor com o painel aberto troca o conteudo', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([
        { id: 3, nome: 'Solda', ativo: true, atividade: 'solda', utilizaKit: false },
        { id: 1, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false },
      ]),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Solda' }))
    fireEvent.click(screen.getByRole('button', { name: 'Editar Corte' }))

    expect(screen.getAllByRole('form')).toHaveLength(1)
    const painel = screen.getByRole('form', { name: 'Editar setor' })
    const campo = within(painel).getByLabelText('Nome do setor') as HTMLInputElement
    expect(campo.value).toBe('Corte')
    expect((within(painel).getByLabelText('Atividade (opcional)') as HTMLInputElement).value).toBe('')
    expect(within(painel).getByText('Corte', { selector: 'p' })).toBeTruthy()
    // O painel remonta por `key`, e o foco volta ao primeiro campo (decisão D6 do plano da 1F).
    expect(document.activeElement).toBe(campo)
  })

  it('conflito mantem o painel aberto com o erro e o Reativar dentro dele', async () => {
    const base = fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) })
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 9 }, 409))
        : base(url, init))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const painel = await screen.findByRole('form', { name: 'Novo setor' })
    expect(await within(painel).findByText('Já existe um setor "Solda" inativo.')).toBeTruthy()
    expect(within(painel).getByRole('button', { name: 'Reativar o existente' })).toBeTruthy()
  })

  it('reativar com sucesso fecha o painel e volta a Mais recentes', async () => {
    let reativou = false
    const base = fetchPorRota({
      '/api/setores': () => respostaJson(reativou ? [CORTE, { id: 9, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }] : [CORTE]),
      '/api/setores/9/ativo': () => { reativou = true; return respostaJson({}, 200) },
    })
    const fetchMock = vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 9 }, 409))
        : base(url, init))
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Corte')
    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'nome' } })
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))
    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await esperarPainelFechar()
    expect((screen.getByLabelText('Ordenar por') as HTMLSelectElement).value).toBe('recentes')
    expect(await screen.findByText('Solda')).toBeTruthy()
  })

  it('falha de rede ao salvar mantem o painel aberto com o erro dentro dele', async () => {
    const base = fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST' ? Promise.reject(new Error('rede caiu')) : base(url, init)))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    const painel = await screen.findByRole('form', { name: 'Novo setor' })
    expect(await within(painel).findByText('Não foi possível salvar o setor.')).toBeTruthy()
  })

  it('erro de Inativar aparece fora do painel', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([CORTE]),
      '/api/setores/1/ativo': () => respostaJson({ erro: 'proibido' }, 403),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByText('Inativar'))

    const banner = await screen.findByText('Seu perfil não tem permissão para esta ação.')
    expect(screen.queryByRole('form')).toBeNull()
    expect(banner.getAttribute('role')).toBe('alert')
  })

  it('ordenar por nome reordena a lista no cliente sem nova requisicao', async () => {
    const fetchMock = fetchPorRota({
      '/api/setores': () => respostaJson([
        { id: 1, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false },
        { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false },
        { id: 2, nome: 'Ajuste', ativo: true, atividade: null, utilizaKit: false },
      ]),
    })
    vi.stubGlobal('fetch', fetchMock)

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await screen.findByText('Solda')
    const nomes = () => screen.getAllByRole('listitem').map((li) => li.textContent ?? '')
    expect(nomes()[0]).toContain('Solda')
    const antes = fetchMock.mock.calls.length

    fireEvent.change(screen.getByLabelText('Ordenar por'), { target: { value: 'nome' } })

    const depois = nomes()
    expect(depois[0]).toContain('Ajuste')
    expect(depois[1]).toContain('Corte')
    expect(depois[2]).toContain('Solda')
    expect(fetchMock.mock.calls.length).toBe(antes)
  })

  it('quem nao pode escrever ve a lista e o seletor de ordem, e nao ve o botao Novo setor', async () => {
    perfil = 'PCP'
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)

    expect(await screen.findByText('Corte')).toBeTruthy()
    expect(screen.getByLabelText('Ordenar por')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Novo setor' })).toBeNull()
  })

  // O botão que abre o painel some enquanto ele está aberto (decisão D5 do plano da 1F), e o
  // controle focado sai do DOM com o painel: sem a devolução, o foco cairia no `<body>`.
  it('Cancelar devolve o foco ao Novo setor', async () => {
    vi.stubGlobal('fetch', fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Novo setor' }))
  })

  it('Cancelar na edicao devolve o foco ao Editar do mesmo setor', async () => {
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => respostaJson([
        { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false },
        { id: 1, nome: 'Corte', ativo: true, atividade: null, utilizaKit: false },
      ]),
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Corte' }))
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }))

    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Editar Corte' }))
  })

  it('salvar edicao devolve o foco ao Editar do mesmo setor depois da recarga', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }
    const CORTADO = { id: 1, nome: 'Corte fino', ativo: true, atividade: null, utilizaKit: false }
    let editou = false
    // A recarga fica pendurada até o teste soltá-la: é durante ela que a lista dá lugar ao
    // "Carregando…" e o "Editar" de destino não está no DOM.
    let soltarRecarga: () => void = () => {}
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => editou
        ? new Promise<Response>((r) => { soltarRecarga = () => r(respostaJson([SOLDA, CORTADO])) })
        : respostaJson([SOLDA, CORTE]),
      '/api/setores/1': () => { editou = true; return respostaJson(CORTADO) },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Corte' }))
    fireEvent.change(screen.getByLabelText('Nome do setor'), { target: { value: 'Corte fino' } })
    fireEvent.click(screen.getByText('Salvar alterações'))

    await esperarPainelFechar()
    expect(screen.getByRole('status').textContent).toBe('Carregando…')
    soltarRecarga()

    // O nome do botão mudou com a edição: o alvo é o setor, não o rótulo de antes.
    const editarDepois = await screen.findByRole('button', { name: 'Editar Corte fino' })
    await waitFor(() => { expect(document.activeElement).toBe(editarDepois) })
  })

  it('salvar edicao de um setor que nao voltou na recarga devolve o foco ao Novo setor', async () => {
    const SOLDA = { id: 3, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }
    let editou = false
    let soltarRecarga: () => void = () => {}
    vi.stubGlobal('fetch', fetchPorRota({
      // Outra pessoa o inativou entre a edição e a recarga, que não pede os inativos.
      '/api/setores': () => editou
        ? new Promise<Response>((r) => { soltarRecarga = () => r(respostaJson([SOLDA])) })
        : respostaJson([SOLDA, CORTE]),
      '/api/setores/1': () => { editou = true; return respostaJson(CORTE) },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    fireEvent.click(await screen.findByRole('button', { name: 'Editar Corte' }))
    fireEvent.click(screen.getByText('Salvar alterações'))

    await esperarPainelFechar()
    soltarRecarga()
    await waitFor(() => { expect(screen.queryByText('Corte')).toBeNull() })
    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Novo setor' }))
    })
  })

  it('com o cadastro em voo, Cancelar fica desabilitado', async () => {
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) =>
      init?.method === 'POST'
        ? new Promise<Response>(() => {})
        : fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) })(url, init)))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await screen.findByText('Salvando…')
    const cancelar = screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement
    expect(cancelar.disabled).toBe(true)
    fireEvent.click(cancelar)
    expect(screen.getByRole('form', { name: 'Novo setor' })).toBeTruthy()
  })

  it('com o Reativar o existente em voo, Cancelar fica desabilitado', async () => {
    const base = fetchPorRota({ '/api/setores': () => respostaJson([CORTE]) })
    vi.stubGlobal('fetch', vi.fn((url: string | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return Promise.resolve(respostaJson({ erro: 'ValorDuplicado', campo: 'nome', existeInativo: true, idExistente: 9 }, 409))
      }
      if (String(url).includes('/ativo')) return new Promise<Response>(() => {})
      return base(url, init)
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))
    fireEvent.click(await screen.findByRole('button', { name: 'Reativar o existente' }))

    await waitFor(() => {
      expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
    })
  })

  it('salvar solta o envio antes da recarga: reabrir o painel durante ela mostra Adicionar habilitado', async () => {
    let chamadas = 0
    vi.stubGlobal('fetch', fetchPorRota({
      '/api/setores': () => {
        chamadas += 1
        if (chamadas === 1) return respostaJson([])
        if (chamadas === 2) return respostaJson({ id: 2, nome: 'Solda', ativo: true, atividade: null, utilizaKit: false }, 201)
        // A recarga pós-salvar nunca termina: o que se mede é o estado do painel enquanto ela voa.
        return new Promise<Response>(() => {})
      },
    }))

    render(<MemoryRouter><SetoresPage /></MemoryRouter>)
    await abrirNovoSetor()
    fireEvent.change(await screen.findByLabelText('Nome do setor'), { target: { value: 'Solda' } })
    fireEvent.click(screen.getByText('Adicionar'))

    await esperarPainelFechar()
    await waitFor(() => { expect(chamadas).toBe(3) })
    await abrirNovoSetor()
    expect((screen.getByRole('button', { name: 'Adicionar' }) as HTMLButtonElement).disabled).toBe(false)
  })
})
