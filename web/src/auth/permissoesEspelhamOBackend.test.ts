import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, existsSync } from 'node:fs'
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

/**
 * Um `Recurso` do front pode ser servido por MAIS DE UM controller — daí o array, e não uma
 * string. Hoje o caso é `componentes`.
 *
 * **O que a forma deste mapa AFIRMA**, e é decisão de desenho registrada em 2026-08-24: os perfis
 * de escrita da receita padrão são, POR DESENHO, os mesmos do próprio Componente — é um conceito
 * de permissão só. O backend escreve isso literalmente na `TabelaAprovada` de
 * `PerfisDeEscritaDeclaradosTests` ("Receita padrao (Fase 1C): mesmos perfis do proprio
 * Componente"), e a tela das Tasks 10/11 mostra o Componente e a receita dele na mesma página.
 *
 * A alternativa descartada era um `Recurso` novo (`receitaPadrao`) com o mesmo valor: isso faria
 * `usePodeEscrever('componentes')` e `usePodeEscrever('receitaPadrao')` funcionarem os DOIS hoje,
 * e digitar o errado só apareceria no dia em que divergissem — em silêncio.
 *
 * No dia em que os perfis dos dois controllers DEIXAREM de coincidir, esta guarda fica VERMELHA
 * (a asserção abaixo compara TODO conjunto de TODOS os arquivos do array contra a MESMA entrada da
 * tabela), e o conserto certo passa a ser separar em dois `Recurso` — que é o trabalho que se faria
 * de qualquer jeito. Falha alta na hora certa, e é por isso que esta forma foi escolhida.
 */
const CONTROLLERS_POR_RECURSO: Record<Recurso, readonly string[]> = {
  setores: ['SetoresController.cs'],
  materiais: ['MateriaisController.cs'],
  componentes: ['ComponentesController.cs', 'ReceitaPadraoController.cs'],
  pedidos: ['PedidosController.cs'],
  agrupamentos: ['AgrupamentosController.cs'],
}

/**
 * Arquivos de `Controllers/` que NÃO espelham nenhum `Recurso` do front — por desenho, não por
 * esquecimento. Cada entrada carrega o motivo por escrito: isenção sem motivo vira permissão
 * eterna para qualquer arquivo futuro de mesmo nome.
 *
 * Duas asserções cobram esta lista, e é isso que a impede de virar chave morta protegendo nada:
 * `todo nome isento existe no disco` (a dívida aberta no backend, que não confere as listas dele
 * contra a realidade) e `nenhum isento declara [Authorize(Roles)]` — porque o dia em que um destes
 * ganhar `Roles` é o dia em que a isenção passa a mentir.
 */
const ISENTOS: Record<string, string> = {
  'AuthController.cs':
    'rotas de sessão (login/refresh/logout), anônimas por contrato — não há perfil a espelhar, e ' +
    'o par de backend delas é `IsentosDeAutenticacaoPorDesenho` em PerfisDeEscritaDeclaradosTests.',
  'MeController.cs':
    'o gate é `[Authorize]` de classe, SEM `Roles`: leitura da própria sessão é de qualquer ' +
    'autenticado. `permissoes.ts` só fala de ESCRITA por perfil, então não há entrada a fazer.',
  'CadastroControllerBase.cs':
    'base abstrata, sem rota própria — os perfis vivem nos controllers concretos que herdam dela, ' +
    'e cada um já está mapeado. Se um `[Authorize(Roles)]` nascer AQUI, ele passa a valer para os ' +
    'cinco de uma vez: é exatamente o que a asserção de "nenhum isento declara Roles" pega.',
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

/** Todo arquivo do mapa, achatado — a lista do que a guarda de valores realmente lê. */
const MAPEADOS: readonly string[] = Object.values(CONTROLLERS_POR_RECURSO).flat()

describe('a tabela de escrita do front espelha os [Authorize(Roles)] do backend', () => {
  it.each(Object.keys(CONTROLLERS_POR_RECURSO) as Recurso[])(
    '%s: os perfis da tabela são os mesmos do controller',
    (recurso) => {
      const daTabela = [...TABELA_DE_ESCRITA[recurso]].sort()

      for (const arquivo of CONTROLLERS_POR_RECURSO[recurso]) {
        // Ordem não importa (o backend escreve "Administrador,PCP" em um e "PCP,Administrador" em
        // outro), então a comparação é por conjunto ordenado.
        for (const conjunto of perfisDeclarados(arquivo)) {
          expect(
            conjunto,
            `${recurso}/${arquivo}: backend ${conjunto} vs tabela ${daTabela}`,
          ).toEqual(daTabela)
        }
      }
    },
  )

  it('não passa calada: todo controller mapeado tem pelo menos um atributo de perfis', () => {
    // ESTE é o teste que impede a guarda de virar decoração. Se os `Roles` deixarem de ser
    // literal no atributo — policy, claims, permissão em banco (dívida registrada em 2026-08-10) —
    // a varredura passa a não achar nada, e sem esta asserção o `for` acima ficaria VERDE
    // percorrendo lista vazia, exatamente quando parou de vigiar.
    //
    // Por ARQUIVO e não por recurso, e isto foi MEDIDO em 2026-08-24: apagando os TRÊS
    // `[Authorize(Roles)]` de `ReceitaPadraoController.cs`, a forma somada por recurso
    // (`arquivos.flatMap(perfisDeclarados).length > 0`) fica VERDE 9/9 — os 3 atributos do
    // `ComponentesController.cs` carregam o arquivo mudo de carona. Na forma por arquivo a mesma
    // mutação fica vermelha nomeando o arquivo.
    for (const [recurso, arquivos] of Object.entries(CONTROLLERS_POR_RECURSO)) {
      for (const arquivo of arquivos) {
        expect(perfisDeclarados(arquivo).length, `${recurso}: nenhum [Authorize(Roles)] em ${arquivo}`)
          .toBeGreaterThan(0)
      }
    }
  })
})

/**
 * A varredura de DESCOBERTA. O mapa acima é iterado a partir do FRONT: ele nunca acha um controller
 * NOVO que ninguém lançou nele — foi assim que `ReceitaPadraoController.cs` viveu a Fase 1C inteira
 * sem espelho nenhum, com `npm test` verde (achado M5). Aqui o lado iterado é o DIRETÓRIO REAL,
 * então arquivo novo aparece por construção, inclusive o que reaproveita um `Recurso` que já existe
 * — que é justamente o caso que o compilador NÃO cobra (nenhum `Recurso` novo nasce, o
 * `Record<Recurso, …>` continua exaustivo e satisfeito).
 *
 * ENUMERA TODO `.cs` DO DIRETÓRIO, e não um glob `*Controller.cs`. A decisão e o custo, por
 * escrito: o glob não casaria `CadastroControllerBase.cs`, o que dispensaria uma entrada de
 * isenção — conveniente, e frágil pelo pior motivo possível, porque base abstrata é exatamente a
 * forma que produziu o achado C1 do backend (um único atributo na base valendo para os cinco
 * controllers concretos de uma vez). O custo de varrer tudo é real e está aceito: um helper ou um
 * arquivo de DTO que venha a morar nesta pasta vai exigir uma linha de isenção com motivo, mesmo
 * não tendo perfil nenhum. Trocamos ruído previsível por invisibilidade silenciosa.
 *
 * O QUE ELA NÃO FAZ, declarado para não encenar precisão:
 * - Lê TEXTO, não roteamento. Ela pergunta "que arquivo existe nesta pasta", não "que endpoint o
 *   ASP.NET roteia" — controller em OUTRO diretório, em outro assembly, ou minimal API em
 *   `Program.cs` são invisíveis aqui. Quem pergunta pela tabela de rotas real é o par de backend
 *   (`PerfisDeEscritaDeclaradosTests`, que resolve o `EndpointDataSource`); esta guarda é a rede do
 *   front, não a do roteamento.
 * - Não vê subdiretório: `readdirSync` sem recursão. Uma pasta nova dentro de `Controllers/` passa.
 * - Não confere a REMOÇÃO de um `[Authorize]` — limite herdado e já registrado em `permissoes.ts`.
 */
describe('a varredura de Controllers/ não deixa arquivo fora do mapa', () => {
  const NO_DISCO = readdirSync(fileURLToPath(RAIZ)).filter((n) => n.endsWith('.cs')).sort()

  it('todo arquivo de Controllers/ está mapeado ou explicitamente isento', () => {
    // A varredura não vale nada se a pasta ficar vazia (mudou de lugar, glob errado): sem isto o
    // `filter` abaixo percorreria lista vazia e a guarda ficaria verde justamente quando cegou.
    expect(NO_DISCO.length, `nenhum .cs em ${fileURLToPath(RAIZ)} — a pasta mudou de lugar?`)
      .toBeGreaterThan(0)

    const semCobertura = NO_DISCO
      .filter((n) => !MAPEADOS.includes(n) && !(n in ISENTOS))

    expect(
      semCobertura,
      `arquivo em Controllers/ que não está em CONTROLLERS_POR_RECURSO nem em ISENTOS: ` +
      `${semCobertura.join(', ')}. Se ele declara [Authorize(Roles)], acrescente-o ao array do ` +
      `Recurso que ele serve (ou crie o Recurso, se for permissão nova, junto da entrada em ` +
      `permissoes.ts). Se não declara perfil nenhum, isente-o COM O MOTIVO por escrito.`,
    ).toEqual([])
  })

  it('todo nome isento existe no disco — isenção não protege arquivo que não existe', () => {
    // A dívida que o backend abriu e esta guarda não repete: lá as listas de isentos não são
    // conferidas contra a realidade, e um arquivo isento renomeado deixa a chave morta no lugar,
    // em silêncio, valendo para qualquer arquivo futuro que reuse o nome.
    const fantasmas = Object.keys(ISENTOS)
      .filter((n) => !existsSync(fileURLToPath(new URL(n, RAIZ))))

    expect(
      fantasmas,
      `isenção para arquivo que não existe mais: ${fantasmas.join(', ')}. Foi renomeado ou ` +
      `apagado? Remova a entrada de ISENTOS — ela hoje não protege nada e amanhã protege o ` +
      `arquivo errado.`,
    ).toEqual([])
  })

  it('nenhum arquivo isento declara [Authorize(Roles)]', () => {
    // O que torna a isenção verificável em vez de uma palavra dada. Os três motivos escritos em
    // ISENTOS dizem, cada um a seu modo, "aqui não há perfil a espelhar"; esta asserção MEDE isso
    // a cada execução. Vale sobretudo para `CadastroControllerBase.cs`: um `Roles` na base valeria
    // para os cinco controllers concretos de uma vez, e nenhuma outra asserção deste arquivo
    // olharia para lá.
    //
    // Modo de falha ACOPLADO, medido e aceito: se um isento for RENOMEADO, esta asserção também
    // fica vermelha, mas com o `ENOENT` cru do `readFileSync` (nomeando o caminho completo), e não
    // com a mensagem escrita abaixo. Quem explica o que aconteceu, nesse caso, é a asserção do
    // fantasma logo acima, que falha junto. Duas vermelhas para uma causa — barulhento, nunca
    // silencioso.
    for (const arquivo of Object.keys(ISENTOS)) {
      expect(
        perfisDeclarados(arquivo),
        `${arquivo} está em ISENTOS ("${ISENTOS[arquivo]}") mas declara [Authorize(Roles)]. ` +
        `A isenção virou mentira: mapeie-o num Recurso.`,
      ).toEqual([])
    }
  })
})
