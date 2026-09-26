import { describe, it, expect } from 'vitest'
import { podeEscrever } from './permissoes'

describe('podeEscrever', () => {
  it('deixa Administrador escrever em tudo', () => {
    for (const r of [
      'setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos', 'estrutura',
      'apontamento', 'entrega', 'roteiro', 'estorno',
    ] as const) {
      expect(podeEscrever('Administrador', r), r).toBe(true)
    }
  })

  it('deixa PCP escrever em componentes, pedidos e agrupamentos', () => {
    expect(podeEscrever('PCP', 'componentes')).toBe(true)
    expect(podeEscrever('PCP', 'pedidos')).toBe(true)
    expect(podeEscrever('PCP', 'agrupamentos')).toBe(true)
  })

  it('NÃO deixa PCP escrever em setores nem materiais', () => {
    // Espelha `[Authorize(Roles = "Administrador")]` de SetoresController e MateriaisController.
    // Se esta linha inverter, o PCP ganha um formulário que o backend vai recusar com 403.
    expect(podeEscrever('PCP', 'setores')).toBe(false)
    expect(podeEscrever('PCP', 'materiais')).toBe(false)
  })

  it('não deixa Operador, Almoxarifado, Qualidade nem Gestao escreverem em nenhum cadastro', () => {
    // `Gestao` sem acento: é o valor que está em `db/seed.sql`, e o perfil chega do backend como
    // claim. Escrever "Gestão" aqui faria a comparação falhar em silêncio.
    // CONFERIDO no pré-flight de 2026-08-10, remedido na Fase 3 (2026-09-25): o `MERGE` de perfis de
    // `db/seed.sql` traz os 7 perfis sem acento — o Movimentador entrou na Fase 3.
    // Título reescrito na Task 10 da Fase 3: o Operador passou a escrever em `apontamento` e
    // `estorno` (ver o teste "Fase 3" abaixo), então "em nada" ficou falso para ele — este `for`
    // continua valendo, só que restrito aos CADASTROS (os cinco recursos de antes da Fase 3).
    for (const p of ['Operador', 'Almoxarifado', 'Qualidade', 'Gestao']) {
      for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
  })

  it('Fase 3: cada ação de execução é de quem a faz no chão de fábrica', () => {
    // Espelha os quatro controllers da execução (desvio D1 do plano 2 da Fase 3).
    expect(podeEscrever('Operador', 'apontamento')).toBe(true)
    expect(podeEscrever('Movimentador', 'apontamento')).toBe(false)
    expect(podeEscrever('Movimentador', 'entrega')).toBe(true)
    expect(podeEscrever('Operador', 'entrega')).toBe(false)
    expect(podeEscrever('PCP', 'roteiro')).toBe(true)
    expect(podeEscrever('Operador', 'roteiro')).toBe(false)
    for (const p of ['Operador', 'Movimentador', 'PCP']) {
      expect(podeEscrever(p, 'estorno'), p).toBe(true)
    }
    for (const p of ['Almoxarifado', 'Qualidade', 'Gestao']) {
      for (const r of ['apontamento', 'entrega', 'roteiro', 'estorno'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
  })

  it('nega perfil desconhecido em vez de liberar', () => {
    // Perfil novo no banco sem entrada aqui tem que cair no lado seguro.
    expect(podeEscrever('PerfilQueNaoExiste', 'pedidos')).toBe(false)
    expect(podeEscrever('', 'pedidos')).toBe(false)
  })
})
