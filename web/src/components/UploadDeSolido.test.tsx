// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { UploadDeSolido } from './UploadDeSolido'
import { respostaBinaria, respostaJson } from '../testes/api'
import { inicializar, _resetParaTeste } from '../api/client'

// `apiFetch` exige `inicializar()` — sem isto toda chamada estoura "client nao inicializado".
// Molde de `SeletorComBusca.test.tsx`.
beforeEach(() => {
  _resetParaTeste()
  inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function arquivoStl(nome = 'cubo.stl'): File {
  // 684 bytes: o cubo de 12 triângulos. O conteúdo não importa aqui — a validação de forma é do
  // backend, e este teste prova o ENVIO, não o formato.
  return new File([new Uint8Array(684)], nome, { type: 'application/octet-stream' })
}

describe('UploadDeSolido', () => {
  it('mostra que o componente ainda não tem sólido', () => {
    render(
      <UploadDeSolido
        componenteId={7}
        temSolido={false}
        nomeDoSolido={null}
        tamanhoDoSolidoEmBytes={null}
        aoEnviar={() => {}}
      />,
    )
    expect(screen.getByText(/sem sólido/i)).toBeTruthy()
  })

  it('envia o arquivo escolhido como multipart e avisa o pai', async () => {
    const aoEnviar = vi.fn()
    const fetchMock = vi.fn((_url: string | URL, _init?: RequestInit) => Promise.resolve(respostaJson({})))
    vi.stubGlobal('fetch', fetchMock)

    render(
      <UploadDeSolido
        componenteId={7}
        temSolido={false}
        nomeDoSolido={null}
        tamanhoDoSolidoEmBytes={null}
        aoEnviar={aoEnviar}
      />,
    )
    const campo = screen.getByLabelText(/sólido/i) as HTMLInputElement
    fireEvent.change(campo, { target: { files: [arquivoStl()] } })

    await waitFor(() => expect(aoEnviar).toHaveBeenCalled())
    const corpo = fetchMock.mock.calls[0][1]?.body
    expect(corpo).toBeInstanceOf(FormData)
    // O nome do campo é contrato com o parâmetro `arquivo` do controller: errá-lo dá 400 no
    // servidor e nada na tela.
    expect((corpo as FormData).get('arquivo')).toBeInstanceOf(File)
    expect(String(fetchMock.mock.calls[0][0])).toContain('/api/componentes/7/solido')
  })

  it('mostra erro quando o envio falha, e não avisa o pai', async () => {
    const aoEnviar = vi.fn()
    vi.stubGlobal('fetch', vi.fn(() =>
      Promise.resolve(respostaJson({ erro: 'O arquivo nao e um STL valido.' }, 400))))

    render(
      <UploadDeSolido
        componenteId={7}
        temSolido={false}
        nomeDoSolido={null}
        tamanhoDoSolidoEmBytes={null}
        aoEnviar={aoEnviar}
      />,
    )
    fireEvent.change(screen.getByLabelText(/sólido/i), { target: { files: [arquivoStl()] } })

    await waitFor(() => expect(screen.getByRole('alert')).toBeTruthy())
    // O fallback, e não a mensagem do servidor: `enviarSolido` não popula `detalhe` (item 4 da
    // caixa de correção do brief da Task 6). Afirmar o texto prova que o `catch` passou por
    // `mensagemDeErro`.
    expect(screen.getByRole('alert').textContent).toContain('Envie um arquivo .stl de até 16 MiB')
    expect(aoEnviar).not.toHaveBeenCalled()
  })

  it('mostra estado de enviando enquanto a requisição está em voo', async () => {
    const aoEnviar = vi.fn()
    // Promise que não resolve, para o estado intermediário ser observável — molde do teste de
    // carregando de `SeletorComBusca.test.tsx`.
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})))

    render(
      <UploadDeSolido
        componenteId={7}
        temSolido={false}
        nomeDoSolido={null}
        tamanhoDoSolidoEmBytes={null}
        aoEnviar={aoEnviar}
      />,
    )
    fireEvent.change(screen.getByLabelText(/sólido/i), { target: { files: [arquivoStl()] } })

    expect(await screen.findByRole('status')).toBeTruthy()
    expect(aoEnviar).not.toHaveBeenCalled()
  })

  it('quando já tem sólido, mostra nome e tamanho e oferece substituir e baixar', () => {
    render(
      <UploadDeSolido
        componenteId={7}
        temSolido
        nomeDoSolido="suporte.stl"
        tamanhoDoSolidoEmBytes={684}
        aoEnviar={() => {}}
      />,
    )
    expect(screen.getByText(/suporte\.stl/)).toBeTruthy()
    expect(screen.getByText(/684 bytes/)).toBeTruthy()
    expect(screen.getByText(/substituir/i)).toBeTruthy()
    expect(screen.getByRole('button', { name: /baixar/i })).toBeTruthy()
  })

  it('baixar busca o binário autenticado e revoga o object URL', async () => {
    vi.stubGlobal('fetch', vi.fn(() =>
      Promise.resolve(respostaBinaria(new Uint8Array(684), 'suporte.stl'))))

    // jsdom não implementa `URL.createObjectURL`/`revokeObjectURL`: stube os dois e afirme que o
    // URL criado e o URL revogado são o MESMO (revogar outro vazaria o blob).
    const urlCriada = 'blob:http://localhost/fake-solido'
    const criar = vi.fn(() => urlCriada)
    const revogar = vi.fn()
    vi.stubGlobal('URL', { ...URL, createObjectURL: criar, revokeObjectURL: revogar })

    render(
      <UploadDeSolido
        componenteId={7}
        temSolido
        nomeDoSolido="suporte.stl"
        tamanhoDoSolidoEmBytes={684}
        aoEnviar={() => {}}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /baixar/i }))

    await waitFor(() => expect(revogar).toHaveBeenCalled())
    expect(criar).toHaveBeenCalledTimes(1)
    expect(revogar).toHaveBeenCalledWith(urlCriada)
  })
})
