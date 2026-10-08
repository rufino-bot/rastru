// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, within } from '@testing-library/react'
import { PainelDoArquivoDoBom } from './PainelDoArquivoDoBom'
import { ErroDeBom, TAMANHO_MAXIMO_DO_BOM_EM_BYTES } from '../api/importacao'
import { ErroDeApi } from '../api/erros'

afterEach(cleanup)

function renderizar(
  aoEnviar: (arquivo: File) => Promise<void> = async () => {},
  extras: Partial<Parameters<typeof PainelDoArquivoDoBom>[0]> = {},
) {
  return render(
    <PainelDoArquivoDoBom
      titulo="Importar BOM"
      subtitulo="Subtítulo do painel."
      rotuloDoEnvio="Importar"
      rotuloEnviando="Importando…"
      fallbackDoErro="Não foi possível enviar o BOM."
      aoEnviar={aoEnviar}
      aoFechar={() => {}}
      {...extras}
    />,
  )
}

/** Escolhe um arquivo no campo; `tamanho` força o `size` sem alocar o conteúdo. */
function escolherArquivo(nome: string, tamanho?: number) {
  const arquivo = new File(['a;b'], nome)
  if (tamanho !== undefined) Object.defineProperty(arquivo, 'size', { value: tamanho })
  fireEvent.change(screen.getByLabelText(/Arquivo do BOM/), { target: { files: [arquivo] } })
  return arquivo
}

/**
 * Registra o que o painel escreve em `value` do campo de arquivo. O jsdom não reflete o `files`
 * injetado pelo `fireEvent.change` no `value`, então olhar `campo.value` não distingue "zerou" de
 * "nunca teve valor": o que o teste afirma é a escrita. Chame antes de escolher o arquivo.
 */
function rastrearEscritasDoValor(): string[] {
  const escritas: string[] = []
  const campo = screen.getByLabelText(/Arquivo do BOM/) as HTMLInputElement
  Object.defineProperty(campo, 'value', {
    configurable: true,
    get: () => '',
    set: (v: string) => { escritas.push(v) },
  })
  return escritas
}

function botaoDeEnvio() {
  return screen.getByRole('button', { name: 'Importar' }) as HTMLButtonElement
}

describe('PainelDoArquivoDoBom', () => {
  it('mostra o título, o subtítulo e o campo de arquivo com a dica do limite', () => {
    renderizar()

    const painel = screen.getByRole('form', { name: 'Importar BOM' })
    expect(within(painel).getByText('Subtítulo do painel.')).toBeTruthy()
    const campo = within(painel).getByLabelText(/Arquivo do BOM/) as HTMLInputElement
    expect(campo.type).toBe('file')
    expect(campo.accept).toBe('.xlsx,.csv')
    expect(within(painel).getByText('Até 5 MiB.')).toBeTruthy()
  })

  it('o envio fica desabilitado sem arquivo escolhido', () => {
    renderizar()

    expect(botaoDeEnvio().disabled).toBe(true)
  })

  it('arquivo acima do limite é recusado antes de enviar, citando o limite', () => {
    const aoEnviar = vi.fn(async () => {})
    renderizar(aoEnviar)

    escolherArquivo('bom-enorme.xlsx', TAMANHO_MAXIMO_DO_BOM_EM_BYTES + 1)

    expect(screen.getByRole('alert').textContent).toContain('5 MiB')
    expect(botaoDeEnvio().disabled).toBe(true)
    fireEvent.submit(screen.getByRole('form', { name: 'Importar BOM' }))
    expect(aoEnviar).not.toHaveBeenCalled()
  })

  it('arquivo de exatamente o limite é aceito', () => {
    renderizar()

    escolherArquivo('bom-no-limite.xlsx', TAMANHO_MAXIMO_DO_BOM_EM_BYTES)

    expect(screen.queryByRole('alert')).toBeNull()
    expect(botaoDeEnvio().disabled).toBe(false)
  })

  it('envia o arquivo escolhido', async () => {
    const aoEnviar = vi.fn(async () => {})
    renderizar(aoEnviar)
    const arquivo = escolherArquivo('bom.xlsx')

    fireEvent.click(botaoDeEnvio())

    await vi.waitFor(() => expect(aoEnviar).toHaveBeenCalledTimes(1))
    expect(aoEnviar).toHaveBeenCalledWith(arquivo)
  })

  it('ErroDeBom mostra o título e as linhas dentro do painel', async () => {
    renderizar(async () => { throw new ErroDeBom(['Linha 3: quantidade invalida.', 'Linha 9: codigo vazio.']) })
    escolherArquivo('bom.csv')

    fireEvent.click(botaoDeEnvio())

    const painel = screen.getByRole('form', { name: 'Importar BOM' })
    const alerta = await within(painel).findByRole('alert')
    expect(alerta.textContent).toContain('O arquivo tem problemas:')
    expect(within(alerta).getAllByRole('listitem').map((li) => li.textContent))
      .toEqual(['Linha 3: quantidade invalida.', 'Linha 9: codigo vazio.'])
  })

  it('a dica do campo é só o limite, com ou sem arquivo escolhido', () => {
    renderizar()
    const campo = screen.getByLabelText(/Arquivo do BOM/) as HTMLInputElement
    const dica = () => document.getElementById(campo.getAttribute('aria-describedby')!)!.textContent

    expect(dica()).toBe('Até 5 MiB.')
    escolherArquivo('estrutura do chassi.csv', 2048)

    expect(dica()).toBe('Até 5 MiB.')
    expect(screen.queryByText(/Escolhido:/)).toBeNull()
  })

  it('a escolha aceita não zera o campo: ele segue mostrando o arquivo escolhido', () => {
    renderizar()
    const escritas = rastrearEscritasDoValor()

    escolherArquivo('bom.csv')

    expect(escritas).toEqual([])
    expect(botaoDeEnvio().disabled).toBe(false)
  })

  it('a recusa por tamanho zera o campo', () => {
    renderizar()
    const escritas = rastrearEscritasDoValor()

    escolherArquivo('bom-enorme.xlsx', TAMANHO_MAXIMO_DO_BOM_EM_BYTES + 1)

    expect(escritas).toEqual([''])
  })

  it('a falha do envio zera o campo', async () => {
    renderizar(async () => { throw new ErroDeApi(422, 'Falha (422).') })
    const escritas = rastrearEscritasDoValor()
    escolherArquivo('bom.csv')
    expect(escritas).toEqual([])

    fireEvent.click(botaoDeEnvio())

    await screen.findByRole('alert')
    expect(escritas).toEqual([''])
  })

  it('qualquer falha descarta o arquivo escolhido: o envio volta a ficar desabilitado', async () => {
    renderizar(async () => { throw new ErroDeBom(['Linha 3: quantidade invalida.']) })
    escolherArquivo('bom.csv')
    const campo = screen.getByLabelText(/Arquivo do BOM/) as HTMLInputElement

    fireEvent.click(botaoDeEnvio())

    await screen.findByRole('alert')
    expect(campo.value).toBe('')
    expect(botaoDeEnvio().disabled).toBe(true)
  })

  it('falha genérica mostra a mensagem de erro com o fallback e descarta o arquivo', async () => {
    renderizar(async () => { throw new ErroDeApi(422, 'Falha (422).') })
    escolherArquivo('bom.xlsx')

    fireEvent.click(botaoDeEnvio())

    expect((await screen.findByRole('alert')).textContent).toBe('Não foi possível enviar o BOM.')
    expect(botaoDeEnvio().disabled).toBe(true)
  })

  it('falha com tradução própria da tela mostra a frase dela', async () => {
    renderizar(
      async () => { throw new ErroDeApi(409, 'Falha (409).', undefined, 'ImportacaoDesatualizada') },
      { mensagemDoErro: (e) => (e instanceof ErroDeApi && e.status === 409 ? 'Frase da tela.' : null) },
    )
    escolherArquivo('bom.xlsx')

    fireEvent.click(botaoDeEnvio())

    expect((await screen.findByRole('alert')).textContent).toBe('Frase da tela.')
    expect(botaoDeEnvio().disabled).toBe(true)
  })

  it('escolher outro arquivo limpa o erro anterior', async () => {
    renderizar(async () => { throw new ErroDeApi(422, 'Falha (422).') })
    escolherArquivo('bom.xlsx')
    fireEvent.click(botaoDeEnvio())
    await screen.findByRole('alert')

    escolherArquivo('bom-corrigido.xlsx')

    expect(screen.queryByRole('alert')).toBeNull()
    expect(botaoDeEnvio().disabled).toBe(false)
  })

  it('com o envio em voo, o rótulo muda e o Cancelar fica desabilitado', async () => {
    renderizar(() => new Promise<void>(() => {}))
    escolherArquivo('bom.xlsx')

    fireEvent.click(botaoDeEnvio())

    await screen.findByRole('button', { name: 'Importando…' })
    expect((screen.getByRole('button', { name: 'Cancelar' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('o foco inicial vai ao campo de arquivo', () => {
    renderizar()

    expect(document.activeElement).toBe(screen.getByLabelText(/Arquivo do BOM/))
  })
})
