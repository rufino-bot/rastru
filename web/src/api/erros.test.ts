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

  it('traduz o código da execução quando o servidor não mandou frase', () => {
    // Spec da Fase 3 §8.3: cada código tem tradução. Vem ANTES do texto do status — o 403
    // `Proibido` diz quem pode estornar, e o genérico do 403 só diria "seu perfil".
    expect(mensagemDeErro(new ErroDeApi(403, 'x', undefined, 'Proibido'), PADRAO))
      .toBe('Só quem fez o registro, o PCP ou o Administrador pode estorná-lo.')
    expect(mensagemDeErro(new ErroDeApi(409, 'x', undefined, 'ConflitoDeConcorrencia'), PADRAO))
      .toBe('Outra pessoa registrou neste item ao mesmo tempo; atualize e tente de novo.')
  })

  it('a frase do servidor ganha da tradução do código', () => {
    expect(mensagemDeErro(new ErroDeApi(409, 'x', '*D*: 12 aqui, 16 necessários.', 'FilhosInsuficientes'), PADRAO))
      .toBe('*D*: 12 aqui, 16 necessários.')
  })

  it('código desconhecido não inventa frase: cai no status', () => {
    expect(mensagemDeErro(new ErroDeApi(409, 'x', undefined, 'CodigoQueNaoExiste'), PADRAO)).toBe(PADRAO)
    // `toString` existe em todo objeto; a busca é por chave PRÓPRIA da tabela, não herdada.
    expect(mensagemDeErro(new ErroDeApi(409, 'x', undefined, 'toString'), PADRAO)).toBe(PADRAO)
  })

  it('o 401 continua dizendo que a sessão expirou, mesmo com código', () => {
    expect(mensagemDeErro(new ErroDeApi(401, 'x', undefined, 'Proibido'), PADRAO))
      .toBe('Sua sessão expirou. Entre novamente.')
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
    // Sem terceiro argumento não há detalhe — é o que mantém os 12 pontos de `cadastros.ts`
    // exatamente como estavam.
    expect(e.detalhe).toBeUndefined()
  })
})

/**
 * Achado da verificação no navegador (Task 12 da Fase 1C): o backend recusa o ciclo com uma
 * mensagem que o NOMEIA — `"Esta receita criaria um ciclo: MT-1010 -> MT-1000 -> MT-1010."` — e a
 * tela mostrava o fallback genérico, porque todo 400 caía nele. A spec §1.3 EXIGE essa mensagem:
 * a regra de ciclo é estrita, então alguém pode ser barrado por um ciclo que não criou, e saber
 * onde ele está é o que torna a regra praticável.
 */
describe('mensagemDeErro com detalhe do servidor', () => {
  it('prefere a mensagem do servidor ao texto da tela', () => {
    const ciclo = 'Esta receita criaria um ciclo: MT-1010 -> MT-1000 -> MT-1010.'
    expect(mensagemDeErro(new ErroDeApi(400, 'Falha ao salvar (400).', ciclo), PADRAO)).toBe(ciclo)
  })

  /**
   * O detalhe ganha inclusive dos status que TÊM texto próprio. Se o servidor se deu ao trabalho
   * de dizer o motivo, ele sabe mais do que a tabela por status — e quem popula `detalhe` leu o
   * corpo de propósito, não por acidente.
   */
  it('o detalhe ganha do texto por status', () => {
    expect(mensagemDeErro(new ErroDeApi(403, 'x', 'Só o PCP altera a receita.'), PADRAO))
      .toBe('Só o PCP altera a receita.')
  })

  it('sem detalhe, nada muda para os outros clientes', () => {
    expect(mensagemDeErro(new ErroDeApi(400, 'x'), PADRAO)).toBe(PADRAO)
    expect(mensagemDeErro(new ErroDeApi(403, 'x'), PADRAO))
      .toBe('Seu perfil não tem permissão para esta ação.')
  })

  /**
   * Achado (Minor) da review das Tasks 10-12: `detalhe` NÃO ganha do 401, ao contrário de todos
   * os outros status. Hoje é inócuo — nenhum endpoint emite `{erro}` num 401 — mas se um dia
   * emitir, esta guarda impede que "Sua sessão expirou. Entre novamente." (a única mensagem
   * acionável do conjunto) seja substituída por um texto de validação qualquer.
   */
  it('o 401 NÃO cede para o detalhe — é o único status excluído do desvio', () => {
    expect(mensagemDeErro(new ErroDeApi(401, 'x', 'Algum detalhe do servidor.'), PADRAO))
      .toBe('Sua sessão expirou. Entre novamente.')
  })
})
