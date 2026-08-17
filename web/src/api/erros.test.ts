import { describe, it, expect } from 'vitest'
import { ErroDeApi, mensagemDeErro } from './erros'

const PADRAO = 'Não foi possível carregar os setores.'

describe('mensagemDeErro', () => {
  it('explica sessão expirada no 401', () => {
    expect(mensagemDeErro(new ErroDeApi(401, 'x'), PADRAO)).toBe('Sua sessão expirou. Entre novamente.')
  })

  it('explica falta de permissão no 403', () => {
    // O caso mais frequente na fábrica: Operador clicando em ação de Administrador. Hoje ele lê
    // "Não foi possível alterar o setor", tenta de novo, e recebe o mesmo 403.
    expect(mensagemDeErro(new ErroDeApi(403, 'x'), PADRAO))
      .toBe('Seu perfil não tem permissão para esta ação.')
  })

  it('explica registro inexistente no 404', () => {
    expect(mensagemDeErro(new ErroDeApi(404, 'x'), PADRAO)).toBe('Este registro não existe mais.')
  })

  it('explica falha do servidor no 500', () => {
    expect(mensagemDeErro(new ErroDeApi(500, 'x'), PADRAO))
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })

  it('explica falha do servidor no 503 também, não só no 500', () => {
    // A guarda é `>= 500`, não `=== 500`: hardcodar o 500 deixaria 502/503/504 — os status que um
    // proxy ou uma API fora do ar realmente devolvem — caindo no texto genérico.
    expect(mensagemDeErro(new ErroDeApi(503, 'x'), PADRAO))
      .toBe('O servidor não respondeu como esperado. Tente de novo em instantes.')
  })

  it('cai no texto da tela para status que não têm explicação própria', () => {
    // 400 é falha de validação: quem sabe o que dizer sobre ela é a tela, não esta função.
    expect(mensagemDeErro(new ErroDeApi(400, 'x'), PADRAO)).toBe(PADRAO)
  })

  it('explica queda de rede', () => {
    // `fetch` rejeita com TypeError quando a requisição nem sai — o sintoma do wifi de fábrica.
    expect(mensagemDeErro(new TypeError('Failed to fetch'), PADRAO))
      .toBe('Sem conexão com o servidor. Verifique a rede e tente de novo.')
  })

  it('cai no texto da tela para erro que não é de API nem de rede', () => {
    expect(mensagemDeErro(new Error('qualquer outra coisa'), PADRAO)).toBe(PADRAO)
    expect(mensagemDeErro('nem erro é', PADRAO)).toBe(PADRAO)
  })
})

describe('ErroDeApi', () => {
  it('carrega o status e continua sendo um Error', () => {
    // O `instanceof Error` é o que mantém os 16 `rejects.toThrow()` de cadastros.test.ts válidos.
    const e = new ErroDeApi(409, 'Falha na requisição (409).')
    expect(e).toBeInstanceOf(Error)
    expect(e.status).toBe(409)
    expect(e.message).toBe('Falha na requisição (409).')
    expect(e.name).toBe('ErroDeApi')
  })
})
