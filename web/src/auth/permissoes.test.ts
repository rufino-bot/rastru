import { describe, it, expect } from 'vitest'
import { podeEscrever } from './permissoes'

describe('podeEscrever', () => {
  it('deixa Administrador escrever em tudo', () => {
    for (const r of [
      'setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos', 'estrutura',
      'apontamento', 'entrega', 'roteiro', 'estorno', 'pausa',
      'separacao', 'estornoDeSeparacao', 'materiaisDoNo',
      'expedicao', 'liberacao', 'estornoDaExpedicao',
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

  it('não deixa Operador, Separador, Qualidade nem Gestao escreverem em nenhum cadastro', () => {
    // `Gestao` sem acento: é o valor que está em `db/seed.sql`, e o perfil chega do backend como
    // claim. Escrever "Gestão" aqui faria a comparação falhar em silêncio.
    // CONFERIDO no pré-flight de 2026-08-10, remedido na Fase 3 (2026-09-25): o `MERGE` de perfis de
    // `db/seed.sql` traz os 7 perfis sem acento — o Movimentador entrou na Fase 3.
    // Título reescrito na Task 10 da Fase 3: o Operador passou a escrever em `apontamento` e
    // `estorno` (ver `Fase 3: cada ação de execução é de quem a faz no chão de fábrica`), então
    // "em nada" ficou falso para ele — este `for` continua valendo, só que restrito aos CADASTROS
    // (os cinco recursos de antes da Fase 3).
    for (const p of ['Operador', 'Separador', 'Qualidade', 'Gestao']) {
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
    for (const p of ['Separador', 'Qualidade', 'Gestao']) {
      for (const r of ['apontamento', 'entrega', 'roteiro'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
    for (const p of ['Separador', 'Gestao']) {
      expect(podeEscrever(p, 'estorno'), p).toBe(false)
    }
  })

  it('Fase 5A: a Qualidade expede e estorna o conserto, a Gestão responde à liberação, e ninguém mais faz nenhum dos dois', () => {
    // Espelha `ExpedicaoController` (Qualidade), `LiberacaoController` (Gestão) e a entrada nova de `estorno`
    // (`EstornoController`: o conserto se estorna pela rota genérica da movimentação).
    expect(podeEscrever('Qualidade', 'expedicao')).toBe(true)
    expect(podeEscrever('Gestao', 'expedicao')).toBe(false)
    expect(podeEscrever('Gestao', 'liberacao')).toBe(true)
    expect(podeEscrever('Qualidade', 'liberacao')).toBe(false)
    expect(podeEscrever('Qualidade', 'estorno')).toBe(true)
    for (const p of ['Operador', 'Movimentador', 'Separador', 'PCP']) {
      for (const r of ['expedicao', 'liberacao'] as const) {
        expect(podeEscrever(p, r), `${p} / ${r}`).toBe(false)
      }
    }
  })

  it('Fase 5A: estornar o registro da expedição é de quem pode ser autor dele, mais o PCP', () => {
    // Espelha `EstornoDaExpedicaoController`. O botão ainda compara o autor.
    for (const p of ['Qualidade', 'Gestao', 'PCP', 'Administrador']) {
      expect(podeEscrever(p, 'estornoDaExpedicao'), p).toBe(true)
    }
    for (const p of ['Operador', 'Movimentador', 'Separador']) {
      expect(podeEscrever(p, 'estornoDaExpedicao'), p).toBe(false)
    }
  })

  it('Fase 3D: pausar é do PCP e da Gestão, e de mais ninguém do chão', () => {
    // Espelha `PausaDePedidoController`: prioridade é decisão de planejamento, e a Gestão entra aqui
    // pela primeira vez como escritora.
    for (const p of ['PCP', 'Gestao']) {
      expect(podeEscrever(p, 'pausa'), p).toBe(true)
    }
    for (const p of ['Operador', 'Movimentador', 'Separador', 'Qualidade']) {
      expect(podeEscrever(p, 'pausa'), p).toBe(false)
    }
  })

  it('Fase 4: o Separador separa e estorna separação, e não escreve em materiais do nó nem em cadastro', () => {
    // Espelha `SeparacaoController` e `EstornoDeSeparacaoController`.
    expect(podeEscrever('Separador', 'separacao')).toBe(true)
    expect(podeEscrever('Separador', 'estornoDeSeparacao')).toBe(true)
    expect(podeEscrever('Separador', 'materiaisDoNo')).toBe(false)
    for (const r of ['setores', 'materiais', 'componentes', 'pedidos', 'agrupamentos', 'estrutura'] as const) {
      expect(podeEscrever('Separador', r), r).toBe(false)
    }
  })

  it('Fase 4: o PCP edita os materiais do nó e estorna separação, mas não separa', () => {
    // Espelha `MateriaisDoNoController` (PUT) e `EstornoDeSeparacaoController`.
    expect(podeEscrever('PCP', 'materiaisDoNo')).toBe(true)
    expect(podeEscrever('PCP', 'estornoDeSeparacao')).toBe(true)
    expect(podeEscrever('PCP', 'separacao')).toBe(false)
  })

  it('Fase 4: Operador, Movimentador, Qualidade e Gestão não fazem nenhuma das três', () => {
    for (const p of ['Operador', 'Movimentador', 'Qualidade', 'Gestao']) {
      for (const r of ['separacao', 'estornoDeSeparacao', 'materiaisDoNo'] as const) {
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
