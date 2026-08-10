import { describe, it, expect } from 'vitest'
import { podeEscrever } from './permissoes'

describe('podeEscrever', () => {
  it('deixa Administrador escrever em tudo', () => {
    for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
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

  it('não deixa Operador, Almoxarifado, Qualidade nem Gestao escreverem em nada', () => {
    // `Gestao` sem acento: é o valor que está em `db/seed.sql`, e o perfil chega do backend como
    // claim. Escrever "Gestão" aqui faria a comparação falhar em silêncio.
    // CONFERIDO no pré-flight de 2026-08-10: `db/seed.sql:3` traz os 6 perfis sem acento.
    for (const p of ['Operador', 'Almoxarifado', 'Qualidade', 'Gestao']) {
      for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos'] as const) {
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
