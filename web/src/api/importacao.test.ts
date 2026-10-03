import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  criarImportacao, listarImportacoes, obterImportacao, alterarPeca, alterarComponente, alterarFilho,
  enviarSolidoPendente, caminhoDoSolidoPendente, reimportar, descartarImportacao, confirmarImportacao,
  ErroDeBom, TAMANHO_MAXIMO_DO_BOM_EM_BYTES,
} from './importacao'
import { inicializar, _resetParaTeste } from './client'
import { ErroDeApi } from './erros'
import { respostaJson } from '../testes/api'

const IMPORTACAO = { id: 5, agrupamentoId: 21, nomeDoArquivo: 'bom.xlsx', versao: 'AAAAAAAAB9E=' }

function simular(resposta: Response) {
  const mock = vi.fn().mockResolvedValue(resposta)
  vi.stubGlobal('fetch', mock)
  return mock
}

function chamada(mock: ReturnType<typeof vi.fn>): [string, RequestInit] {
  return mock.mock.calls[0] as [string, RequestInit]
}

describe('api/importacao', () => {
  beforeEach(() => {
    _resetParaTeste()
    inicializar({ getToken: () => 'token', setToken: () => {}, onSessionLost: () => {} })
  })

  afterEach(() => { vi.unstubAllGlobals() })

  it('o limite do arquivo do BOM é de 5 MiB', () => {
    expect(TAMANHO_MAXIMO_DO_BOM_EM_BYTES).toBe(5 * 1024 * 1024)
  })

  it('criarImportacao_manda_multipart_sem_content_type', async () => {
    const mock = simular(respostaJson(IMPORTACAO, 201))
    const arquivo = new File(['a;b'], 'bom.csv', { type: 'text/csv' })

    const r = await criarImportacao(21, arquivo)

    const [url, init] = chamada(mock)
    expect(url).toBe('/api/agrupamentos/21/importacoes')
    expect(init.method).toBe('POST')
    expect(init.body).toBeInstanceOf(FormData)
    expect((init.body as FormData).get('arquivo')).toBeInstanceOf(File)
    expect(new Headers(init.headers).has('Content-Type')).toBe(false)
    expect(r.id).toBe(5)
  })

  it('criarImportacao_400_BomInvalido_vira_ErroDeBom_com_uma_linha_por_erro', async () => {
    simular(respostaJson({ erro: 'BomInvalido', mensagem: 'Linha 3: quantidade invalida.\nLinha 9: codigo vazio.' }, 400))

    const erro = await criarImportacao(21, new File(['x'], 'bom.csv')).catch((e) => e)

    expect(erro).toBeInstanceOf(ErroDeBom)
    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeBom).linhas).toEqual(['Linha 3: quantidade invalida.', 'Linha 9: codigo vazio.'])
  })

  it('criarImportacao_com_outro_400_lanca_ErroDeApi_e_nao_ErroDeBom', async () => {
    simular(respostaJson({ erro: 'OutraCoisa' }, 400))

    const erro = await criarImportacao(21, new File(['x'], 'bom.csv')).catch((e) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect(erro).not.toBeInstanceOf(ErroDeBom)
    expect((erro as ErroDeApi).status).toBe(400)
  })

  it('listarImportacoes e obterImportacao leem pelas rotas do contrato', async () => {
    const lista = simular(respostaJson([{ id: 5 }]))
    expect(await listarImportacoes(21)).toEqual([{ id: 5 }])
    expect(chamada(lista)[0]).toBe('/api/agrupamentos/21/importacoes')

    const um = simular(respostaJson(IMPORTACAO))
    expect((await obterImportacao(5)).id).toBe(5)
    expect(chamada(um)[0]).toBe('/api/importacoes/5')
  })

  it('obterImportacao_404 lança ErroDeApi com o status', async () => {
    simular(respostaJson({}, 404))
    const erro = await obterImportacao(5).catch((e) => e)
    expect((erro as ErroDeApi).status).toBe(404)
  })

  it('alterarPeca manda a versão, a quantidade e o relatório no PUT', async () => {
    const mock = simular(respostaJson(IMPORTACAO))

    await alterarPeca(5, 'v1', 2, true)

    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({
      versao: 'v1', quantidadeDaPeca: 2, requerRelatorioDimensional: true,
    })
  })

  it('alterarComponente manda o corpo inteiro, com a escolha nula quando se troca o casamento', async () => {
    const mock = simular(respostaJson(IMPORTACAO))

    await alterarComponente(5, 8, 'v1', {
      componenteId: 30, codigoNovo: null, descricaoNova: null, tipoNovo: null, escolhaDeReceita: null,
    })

    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5/componentes/8')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({
      versao: 'v1', componenteId: 30, codigoNovo: null, descricaoNova: null, tipoNovo: null, escolhaDeReceita: null,
    })
  })

  it('alterarFilho manda a versão e a quantidade', async () => {
    const mock = simular(respostaJson(IMPORTACAO))

    await alterarFilho(5, 12, 'v1', 3.5)

    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5/filhos/12')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body as string)).toEqual({ versao: 'v1', quantidade: 3.5 })
  })

  it('409 de versão velha lança ErroDeApi 409 com o código do servidor', async () => {
    simular(respostaJson({ erro: 'ImportacaoDesatualizada' }, 409))

    const erro = await alterarFilho(5, 12, 'velha', 1).catch((e) => e)

    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(409)
    expect((erro as ErroDeApi).codigo).toBe('ImportacaoDesatualizada')
  })

  it('enviarSolidoPendente manda arquivo e versao como campos do multipart, sem Content-Type', async () => {
    const mock = simular(respostaJson(IMPORTACAO))

    await enviarSolidoPendente(5, 8, 'v1', new File(['solid'], 'peca.stl'))

    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5/componentes/8/solido')
    expect(init.method).toBe('POST')
    const corpo = init.body as FormData
    expect(corpo.get('versao')).toBe('v1')
    expect((corpo.get('arquivo') as File).name).toBe('peca.stl')
    expect(new Headers(init.headers).has('Content-Type')).toBe(false)
  })

  it('caminhoDoSolidoPendente não leva o prefixo /api', () => {
    expect(caminhoDoSolidoPendente(5, 8)).toBe('/importacoes/5/componentes/8/solido')
  })

  it('reimportar manda arquivo e versao, e o 400 BomInvalido vira ErroDeBom', async () => {
    const mock = simular(respostaJson(IMPORTACAO))
    await reimportar(5, 'v1', new File(['x'], 'novo.xlsx'))
    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5/arquivo')
    expect((init.body as FormData).get('versao')).toBe('v1')

    simular(respostaJson({ erro: 'BomInvalido', mensagem: 'Linha 2: item repetido.' }, 400))
    const erro = await reimportar(5, 'v1', new File(['x'], 'novo.xlsx')).catch((e) => e)
    expect(erro).toBeInstanceOf(ErroDeBom)
    expect((erro as ErroDeBom).linhas).toEqual(['Linha 2: item repetido.'])
  })

  it('descartarImportacao chama DELETE e lança no que não é 2xx', async () => {
    const mock = simular(new Response(null, { status: 204 }))
    await descartarImportacao(5)
    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5')
    expect(init.method).toBe('DELETE')

    simular(respostaJson({}, 500))
    await expect(descartarImportacao(5)).rejects.toBeInstanceOf(ErroDeApi)
  })

  it('confirmarImportacao_mapeia_os_tres_codigos', async () => {
    simular(respostaJson({ erro: 'ImportacaoComBloqueios' }, 400))
    expect(await confirmarImportacao(5, 'v1')).toBe('ImportacaoComBloqueios')

    simular(respostaJson({ erro: 'ReceitaDoCatalogoMudou', mensagem: 'x' }, 409))
    expect(await confirmarImportacao(5, 'v1')).toBe('ReceitaDoCatalogoMudou')

    simular(respostaJson({ erro: 'ImportacaoDesatualizada' }, 409))
    expect(await confirmarImportacao(5, 'v1')).toBe('ImportacaoDesatualizada')
  })

  it('confirmarImportacao devolve a Peça no 201 e manda só a versão', async () => {
    const mock = simular(respostaJson({ id: 100, descricao: 'Chassi' }, 201))

    const r = await confirmarImportacao(5, 'v1')

    expect(r).toEqual({ id: 100, descricao: 'Chassi' })
    const [url, init] = chamada(mock)
    expect(url).toBe('/api/importacoes/5/confirmacao')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ versao: 'v1' })
  })

  it('confirmarImportacao lança no que não é um dos três códigos', async () => {
    simular(respostaJson({ erro: 'Outro' }, 409))
    const erro = await confirmarImportacao(5, 'v1').catch((e) => e)
    expect(erro).toBeInstanceOf(ErroDeApi)
    expect((erro as ErroDeApi).status).toBe(409)

    simular(respostaJson({}, 404))
    await expect(confirmarImportacao(5, 'v1')).rejects.toBeInstanceOf(ErroDeApi)
  })
})
