import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { TABELA_DE_ESCRITA, type Recurso } from './permissoes'

/**
 * `permissoes.ts` é uma cópia MANUAL dos `[Authorize(Roles = …)]` do backend, e o teste dela é
 * auto-referente: afirma a tabela contra si mesma. Sem esta guarda, mudar um atributo em `src/`
 * não quebra nada aqui, e o sentido perigoso da divergência (liberar de MENOS) não produz erro
 * nenhum em lugar nenhum — a ação some para quem tinha direito a ela.
 *
 * Lê o `.cs` como texto de propósito: nada de compilar C# a partir do vitest. O acoplamento é o
 * caminho do diretório, e ele quebra BARULHENTO se a pasta se mover, o que é o modo de falha certo.
 */
const RAIZ = new URL('../../../src/Rastreamento.Api/Controllers/', import.meta.url)

const CONTROLLER_POR_RECURSO: Record<Recurso, string> = {
  setores: 'SetoresController.cs',
  materiais: 'MateriaisController.cs',
  componentes: 'ComponentesController.cs',
  pedidos: 'PedidosController.cs',
  agrupamentos: 'AgrupamentosController.cs',
}

/** `[Authorize(Roles = "A,B")]` e também `[Authorize(Roles = NomeDeConst)]`. */
const ATRIBUTO = /\[Authorize\(Roles\s*=\s*(?:"([^"]*)"|([A-Za-z_]\w*))\)\]/g
/** `private const string PerfisDeEscrita = "Administrador,PCP";` — ComponentesController usa isto. */
const CONSTANTE = /const\s+string\s+([A-Za-z_]\w*)\s*=\s*"([^"]*)"\s*;/g

/** Conjuntos de perfis declarados no arquivo, um por atributo `Roles`, na ordem em que aparecem. */
function perfisDeclarados(nomeDoArquivo: string): string[][] {
  const fonte = readFileSync(fileURLToPath(new URL(nomeDoArquivo, RAIZ)), 'utf8')

  const constantes = new Map<string, string>()
  for (const [, nome, valor] of fonte.matchAll(CONSTANTE)) constantes.set(nome, valor)

  const encontrados: string[][] = []
  for (const [, literal, identificador] of fonte.matchAll(ATRIBUTO)) {
    const bruto = literal ?? constantes.get(identificador)
    // Const declarada em OUTRO arquivo cairia aqui. Hoje não acontece; se acontecer, é para
    // falhar e ser resolvido, nunca para ser ignorado em silêncio.
    expect(bruto, `${nomeDoArquivo}: não resolvi os perfis de \`${identificador}\``).toBeTruthy()
    encontrados.push(bruto!.split(',').map((p) => p.trim()).sort())
  }
  return encontrados
}

describe('a tabela de escrita do front espelha os [Authorize(Roles)] do backend', () => {
  it.each(Object.keys(CONTROLLER_POR_RECURSO) as Recurso[])(
    '%s: os perfis da tabela são os mesmos do controller',
    (recurso) => {
      const doBackend = perfisDeclarados(CONTROLLER_POR_RECURSO[recurso])
      const daTabela = [...TABELA_DE_ESCRITA[recurso]].sort()

      // Ordem não importa (o backend escreve "Administrador,PCP" em um e "PCP,Administrador" em
      // outro), então a comparação é por conjunto ordenado.
      for (const conjunto of doBackend) {
        expect(conjunto, `${recurso}: backend ${conjunto} vs tabela ${daTabela}`).toEqual(daTabela)
      }
    },
  )

  it('não passa calada: todo controller tem pelo menos um atributo de perfis', () => {
    // ESTE é o teste que impede a guarda de virar decoração. Se os `Roles` deixarem de ser
    // literal no atributo — policy, claims, permissão em banco (dívida registrada em 2026-08-10) —
    // a varredura passa a não achar nada, e sem esta asserção o `for` acima ficaria VERDE
    // percorrendo lista vazia, exatamente quando parou de vigiar.
    for (const [recurso, arquivo] of Object.entries(CONTROLLER_POR_RECURSO)) {
      expect(perfisDeclarados(arquivo).length, `${recurso}: nenhum [Authorize(Roles)] em ${arquivo}`)
        .toBeGreaterThan(0)
    }
  })
})
