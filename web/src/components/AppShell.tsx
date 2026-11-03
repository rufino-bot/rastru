import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { contarTarefas } from '../api/execucao'
import { aoMudarOLivro } from '../api/sinalDoLivro'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'

interface ItemDeNavegacao {
  para: string
  rotulo: string
}

/** Uma entrada do menu: ou um link direto, ou um grupo de links que abre e fecha. */
type EntradaDoMenu =
  | { tipo: 'link'; item: ItemDeNavegacao }
  | { tipo: 'grupo'; id: string; rotulo: string; itens: ItemDeNavegacao[] }

/**
 * Todos os itens aparecem para todos os perfis, de propósito: a leitura de todos estes recursos é
 * liberada para qualquer usuário autenticado no backend (conferido em 2026-08-10). O gating de
 * perfil vive na AÇÃO — formulário e botões de (in)ativar —, não aqui.
 *
 * Medido em 2026-08-10, Chrome headless contra o CSS do build, em 768px (a âncora que a spec §11
 * exige): os 5 links de então cabiam sem estourar; a barra estourava na horizontal a partir do
 * **sétimo** link (86px de estouro).
 *
 * **O sétimo link chegou na Fase 3** (Fila e Tarefas, spec da Fase 3 §6.1 e §6.2), e a saída foi
 * mover a troca barra → gaveta de `md` (768px) para `lg` (1024px), não o agrupamento com submenu
 * que este comentário previa (desvio D4 do plano 3). Remedido em 2026-09-25, Chromium headless
 * contra o CSS do build, com os sete links e o contador "Tarefas 12": o cabeçalho pede **888px**;
 * em 768px estourava 120px, e em 1024px sobram 136px. O celular e o tablet em retrato já usavam a
 * gaveta, e continuam usando.
 *
 * **O oitavo link chegou na Fase 4** (Separação, spec da Fase 4 §6.1), e não coube: na conferência do
 * usuário no navegador (2026-10-19), o cabeçalho quebrou em duas linhas em 1024px com o contador
 * "Tarefas N". A saída foi tirar o "Início" do menu, na barra e na gaveta: a marca "Rastru" do
 * cabeçalho virou o link para a Home. São sete links de novo, mas não os sete medidos em 2026-09-25
 * (Separação no lugar de Início), e esta mudança **não foi medida** com instrumento (a largura que o
 * cabeçalho pede não foi remedida): o usuário a conferiu no navegador e a aprovou na conferência de
 * 2026-10-19. Antes do nono link (Qualidade, Fase 5), meça.
 *
 * **Expedição e Liberações chegaram na Fase 5A** (spec da Fase 5A §6.6), e o menu passou de sete links a nove. A
 * largura que o cabeçalho pede **não foi medida** com os nove: a medição que este comentário pedia antes do nono
 * link fica para a conferência no navegador da fase (com o contador "Tarefas N").
 *
 * **Os nove não couberam, e o menu virou quatro entradas** (conferência da Fase 5A, 2026-11-03): na captura da
 * conferência, o contador do "Tarefas" caiu para a linha de baixo do rótulo (a largura da janela não ficou
 * registrada). A saída é a que este comentário previa lá em cima e a Fase 3 trocou pela troca barra → gaveta:
 * agrupar. Produção (Fila, Tarefas, Separação), Expedição (Expedição, Liberações) e Cadastros (Componentes,
 * Materiais, Setores) são grupos que abrem e fecham; Pedidos fica como link direto. A barra passa a ter quatro
 * entradas, e não nove. A largura que o cabeçalho pede com as quatro **não foi medida** com instrumento — fica
 * para o usuário conferir no navegador, em 1024px (a largura em que a barra já ficou apertada na Fase 4), com o
 * contador "Tarefas N" (que aparece no botão "Produção" com o grupo fechado).
 *
 * A marca é `Link`, não `NavLink`: a Home não é item do menu, então não ganha `aria-current` nem o
 * estilo de item ativo. Por isso nenhum item precisa mais do `end` que impedia `/` de casar com toda
 * rota.
 */
const ITENS: EntradaDoMenu[] = [
  // Produção primeiro: Fila, Tarefas e Separação são as telas do chão de fábrica, abertas o dia inteiro no
  // celular — os cadastros são do escritório.
  {
    tipo: 'grupo',
    id: 'producao',
    rotulo: 'Produção',
    itens: [
      { para: '/fila', rotulo: 'Fila' },
      { para: '/tarefas', rotulo: 'Tarefas' },
      { para: '/separacao', rotulo: 'Separação' },
    ],
  },
  {
    tipo: 'grupo',
    id: 'expedicao',
    rotulo: 'Expedição',
    itens: [
      { para: '/expedicao', rotulo: 'Expedição' },
      { para: '/liberacoes', rotulo: 'Liberações' },
    ],
  },
  { tipo: 'link', item: { para: '/pedidos', rotulo: 'Pedidos' } },
  {
    tipo: 'grupo',
    id: 'cadastros',
    rotulo: 'Cadastros',
    itens: [
      { para: '/componentes', rotulo: 'Componentes' },
      { para: '/materiais', rotulo: 'Materiais' },
      { para: '/setores', rotulo: 'Setores' },
    ],
  },
]

/** O mesmo critério do `NavLink` (sem `end`): a rota é a do item ou uma filha dela. */
const rotaPertence = (para: string, pathname: string) => pathname === para || pathname.startsWith(`${para}/`)

const grupoDaRota = (pathname: string) =>
  ITENS.find((e) => e.tipo === 'grupo' && e.itens.some((i) => rotaPertence(i.para, pathname)))

// Sem `font-medium` aqui: o peso é decidido por estado, mais abaixo. Duas classes de peso no
// mesmo elemento não se resolvem pela ordem em que você as escreve — quem ganha é a que vier
// depois no CSS gerado, e isso não é controlável a partir daqui.
const CONTROLE_BASE =
  'rounded-lg px-3 py-2 text-sm transition-colors ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca'

// Sobre o chrome escuro o anel de foco é `marca`, não `acao`: MEDIDO em 2026-08-10, `acao` sobre
// `chrome` dá 1,640 contra os 3,0 exigidos, e o foco sumiria justamente na navegação por teclado.
// `marca` dá 6,405. É por isto que o `Botao` da Task 5 não serve aqui sem uma variante nova.
const BOTAO_DO_CHROME =
  `${CONTROLE_BASE} font-medium border border-chrome-borda text-superficie hover:bg-chrome-hover`

/**
 * O contador do item Tarefas (spec §6.2). Branco sobre o chrome: é o par `superficie`/`chrome` já
 * medido em `contraste.test.ts` ("texto da barra de navegação"), com os papéis trocados — razão de
 * contraste é simétrica. Nem verde nem vermelho: um número de tarefas não é estado de aprovação.
 */
const CONTADOR =
  'inline-flex min-w-5 items-center justify-center rounded-full bg-superficie px-1.5 text-xs font-semibold text-chrome'

/** Rótulo com o contador de Tarefas ao lado, quando há e é para mostrar; o número nunca é 0. */
function Rotulo({ rotulo, contador }: { rotulo: string; contador: number | null }) {
  if (contador === null || contador === 0) return <>{rotulo}</>
  return (
    <>
      {/* O espaço é texto de verdade, não só a margem: sem ele o nome acessível vira "Tarefas3" — o leitor de
          tela lê a margem como nada. */}
      {`${rotulo} `}
      <span className={CONTADOR}>{contador}</span>
    </>
  )
}

function classesDoLink({ isActive }: { isActive: boolean }): string {
  // Três dimensões de distinção: fundo, tinta e PESO. O peso entrou por decisão do usuário em
  // 2026-08-10, depois de ler o protótipo: o fundo do ativo dá só 1,518 contra o chrome, então
  // fundo+tinta não bastavam "de bate e pronto". Clarear o fundo era a alternativa, e é troca
  // ruim — no teto do que ainda passa AA (25% de branco) o destaque sobe para 1,974 e o rótulo
  // CAI de 6,240 para 4,801. Peso não entra nessa troca: não altera contraste nenhum.
  // `whitespace-nowrap`: rótulo e contador do "Tarefas" ficam na mesma linha (o contador chegou a quebrar para a
  // de baixo, na conferência da Fase 5A).
  return `${CONTROLE_BASE} whitespace-nowrap ${
    isActive
      ? 'bg-chrome-ativo font-semibold text-superficie'
      : 'font-medium text-chrome-tinta-fraca hover:bg-chrome-hover hover:text-superficie'
  }`
}

/**
 * O botão que abre e fecha um grupo do menu (disclosure de navegação, não o padrão ARIA `menu`: as opções são
 * links comuns, com `aria-current` no ativo, e o leitor de tela as anuncia como links).
 *
 * É `<button>` cru, a quinta exceção de chrome (decisão do usuário, 2026-11-03), pelo mesmo motivo do "Sair" e do
 * hambúrguer: o anel de foco `marca` sobre o chrome escuro, que a primitiva `Botao` não dá. O destaque do grupo da
 * rota atual é o do link ativo (`classesDoLink`): fundo, tinta e peso.
 */
function BotaoDoGrupo({
  grupo, aberto, ativo, idDaLista, contador, naGaveta, aoAlternar, referencia,
}: {
  grupo: Extract<EntradaDoMenu, { tipo: 'grupo' }>
  aberto: boolean
  ativo: boolean
  idDaLista: string
  contador: number | null
  naGaveta: boolean
  aoAlternar: () => void
  referencia?: (el: HTMLButtonElement | null) => void
}) {
  return (
    <button
      type="button"
      ref={referencia}
      onClick={aoAlternar}
      aria-expanded={aberto}
      aria-controls={idDaLista}
      className={
        `${classesDoLink({ isActive: ativo })} flex items-center gap-2 ` +
        (naGaveta ? 'w-full justify-between text-left' : '')
      }
    >
      <span>
        <Rotulo rotulo={grupo.rotulo} contador={aberto ? null : contador} />
      </span>
      <span aria-hidden="true" className={`inline-block text-xs transition-transform ${aberto ? 'rotate-180' : ''}`}>
        ▾
      </span>
    </button>
  )
}

export function AppShell() {
  const { estado, logout } = useAuth()
  const [gavetaAberta, setGavetaAberta] = useState(false)
  // Na barra, um grupo aberto por vez (a caixa cobre o conteúdo); na gaveta, quantos quiser (cada grupo empurra o
  // que vem depois).
  const [grupoAberto, setGrupoAberto] = useState<string | null>(null)
  const [gruposDaGaveta, setGruposDaGaveta] = useState<string[]>([])
  const barraRef = useRef<HTMLElement>(null)
  const botoesDosGrupos = useRef(new Map<string, HTMLButtonElement>())
  const location = useLocation()

  const usuario = estado.status === 'autenticado' ? estado.usuario : null

  // O contador é de TODO perfil, como o link (gating vai na ação, não no link). O `erro` do hook
  // nunca é lido aqui, então falha de rede não vira banner no shell — em NENHUM dos dois casos
  // abaixo, porque quem mostra os três estados é a tela de Tarefas, não o menu.
  // Falha na carga INICIAL: sem dado nenhum ainda, o número fica ausente (mostra só "Tarefas").
  // Falha numa atualização PERIÓDICA, depois de um sucesso anterior: o hook mantém o último número
  // na tela (D5, herdado de `useCargaPeriodica`) — o contador NÃO some, fica com um valor
  // possivelmente velho até a próxima consulta dar certo.
  const { dados: totalDeTarefas, recarregar: recontarTarefas } = useCargaPeriodica(
    contarTarefas, 'contagem-de-tarefas', INTERVALO_DA_EXECUCAO_MS, 'Não foi possível contar as tarefas.',
  )
  // Depois de uma ação NESTE aparelho, o número acompanha na hora, em vez de esperar o próximo
  // ciclo de 30 s (ver `aoMudarOLivro`). `recarregar` é estável (`useCallback` sem dependências).
  useEffect(() => aoMudarOLivro(() => { recontarTarefas() }), [recontarTarefas])

  // A gaveta fecha num efeito sobre `location.key`, não no `onClick` de cada link dela: o `onClick`
  // só reage ao clique NAQUELES links, e deixa aberta a gaveta quando a navegação vem de um link no
  // CONTEÚDO (a HomePage tem quatro), do botão voltar do navegador, ou de um redirecionamento
  // programático — é justamente o caso que o efeito serve, não o custo dele. `location.pathname` não
  // serve de dependência: não muda quando o link clicado é da MESMA tela em que o usuário já está, e
  // esse caso ficaria sem fechar. `location.key` muda mesmo nesse caso — medido em 2026-08-10 com
  // react-router 7.18.1 (`fecha a gaveta ao navegar para a tela em que já está`, abaixo): o `Link`
  // não reaproveita a chave da entrada atual do histórico ao navegar para o mesmo caminho. O efeito
  // roda uma vez por navegação, não por render.
  // O grupo aberto da barra fecha no mesmo efeito, pelo mesmo motivo.
  useEffect(() => {
    setGavetaAberta(false)
    setGrupoAberto(null)
  }, [location.key])

  // Clicar fora da barra fecha o grupo aberto. O botão do grupo está DENTRO da barra: o clique nele não conta como
  // "fora", e quem decide o que fazer é o `onClick` dele (alternar). Se contasse, o `pointerdown` fecharia o grupo e o
  // `click` o reabriria.
  useEffect(() => {
    if (grupoAberto === null) return
    const aoApontar = (e: PointerEvent) => {
      if (!barraRef.current?.contains(e.target as Node)) setGrupoAberto(null)
    }
    document.addEventListener('pointerdown', aoApontar)
    return () => document.removeEventListener('pointerdown', aoApontar)
  }, [grupoAberto])

  function aoTeclarNaBarra(e: KeyboardEvent<HTMLElement>) {
    if (e.key !== 'Escape' || grupoAberto === null) return
    setGrupoAberto(null)
    // As opções somem do DOM com o grupo, e o foco cairia no <body>: volta ao botão do grupo.
    botoesDosGrupos.current.get(grupoAberto)?.focus()
  }

  // A gaveta abre com o grupo da rota atual aberto, para o usuário ver onde está.
  function alternarGaveta() {
    if (!gavetaAberta) {
      const atual = grupoDaRota(location.pathname)
      setGruposDaGaveta(atual?.tipo === 'grupo' ? [atual.id] : [])
    }
    setGavetaAberta((a) => !a)
  }

  function alternarGrupoDaGaveta(id: string) {
    setGruposDaGaveta((g) => (g.includes(id) ? g.filter((x) => x !== id) : [...g, id]))
  }

  const contadorDe = (item: ItemDeNavegacao) => (item.para === '/tarefas' ? totalDeTarefas : null)
  const contadorDoGrupo = (itens: ItemDeNavegacao[]) =>
    itens.reduce<number | null>((acc, i) => acc ?? contadorDe(i), null)

  return (
    <div className="min-h-screen bg-fundo font-sans text-tinta">
      <header className="bg-chrome">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
          {/* A marca leva à Home (o "Início" saiu do menu). O nome acessível diz para onde ela leva; o anel
              de foco é o `marca` dos links do menu, pelo mesmo contraste sobre o chrome. A gaveta fecha
              pelo efeito sobre `location.key`, como em qualquer outra navegação. */}
          <Link
            to="/"
            aria-label="Rastru — Início"
            className={
              'rounded-lg text-lg font-semibold tracking-tight text-marca '
              + 'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-marca'
            }
          >
            Rastru
          </Link>

          {/* Barra: some abaixo de 1024px, onde a gaveta assume (desvio D4 do plano 3 da Fase 3). */}
          <nav
            ref={barraRef}
            aria-label="Principal"
            onKeyDown={aoTeclarNaBarra}
            className="hidden lg:flex lg:items-center lg:gap-1"
          >
            {ITENS.map((e) => {
              if (e.tipo === 'link') {
                return (
                  <NavLink key={e.item.para} to={e.item.para} className={classesDoLink}>
                    <Rotulo rotulo={e.item.rotulo} contador={contadorDe(e.item)} />
                  </NavLink>
                )
              }
              const aberto = grupoAberto === e.id
              const idDaLista = `grupo-da-barra-${e.id}`
              return (
                <div key={e.id} className="relative">
                  <BotaoDoGrupo
                    grupo={e}
                    aberto={aberto}
                    ativo={e.itens.some((i) => rotaPertence(i.para, location.pathname))}
                    idDaLista={idDaLista}
                    contador={contadorDoGrupo(e.itens)}
                    naGaveta={false}
                    aoAlternar={() => setGrupoAberto((g) => (g === e.id ? null : e.id))}
                    referencia={(el) => {
                      if (el) botoesDosGrupos.current.set(e.id, el)
                      else botoesDosGrupos.current.delete(e.id)
                    }}
                  />
                  {aberto && (
                    <ul
                      id={idDaLista}
                      className={
                        'absolute left-0 top-full z-20 mt-1 flex min-w-48 flex-col gap-1 rounded-lg border ' +
                        'border-chrome-borda bg-chrome p-1 shadow-lg'
                      }
                    >
                      {e.itens.map((i) => (
                        <li key={i.para}>
                          <NavLink to={i.para} className={(estado) => `${classesDoLink(estado)} block`}>
                            <Rotulo rotulo={i.rotulo} contador={contadorDe(i)} />
                          </NavLink>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )
            })}
          </nav>

          <div className="hidden lg:flex lg:items-center lg:gap-3">
            {usuario && (
              <span className="min-w-0 text-right text-sm leading-tight text-chrome-tinta-apagada">
                <span
                  className="block truncate font-medium text-superficie"
                  title={usuario.nomeCompleto}
                >
                  {usuario.nomeCompleto}
                </span>
                <span className="block text-xs">{usuario.perfil}</span>
              </span>
            )}
            <button type="button" onClick={logout} className={BOTAO_DO_CHROME}>
              Sair
            </button>
          </div>

          <button
            type="button"
            onClick={alternarGaveta}
            aria-expanded={gavetaAberta}
            aria-label={gavetaAberta ? 'Fechar menu' : 'Abrir menu'}
            className={`${BOTAO_DO_CHROME} lg:hidden`}
          >
            {gavetaAberta ? '✕' : '☰'}
          </button>
        </div>

        {/* Gaveta: mesma lista, empilhada, só abaixo de 1024px. O celular Android da fábrica é uso
            declarado, não hipótese. */}
        {gavetaAberta && (
          <nav aria-label="Menu" className="flex flex-col gap-1 border-t border-chrome-ativo px-4 pb-4 lg:hidden">
            {ITENS.map((e) => {
              if (e.tipo === 'link') {
                return (
                  <NavLink key={e.item.para} to={e.item.para} className={classesDoLink}>
                    <Rotulo rotulo={e.item.rotulo} contador={contadorDe(e.item)} />
                  </NavLink>
                )
              }
              const aberto = gruposDaGaveta.includes(e.id)
              const idDaLista = `grupo-da-gaveta-${e.id}`
              return (
                <div key={e.id} className="flex flex-col gap-1">
                  <BotaoDoGrupo
                    grupo={e}
                    aberto={aberto}
                    ativo={e.itens.some((i) => rotaPertence(i.para, location.pathname))}
                    idDaLista={idDaLista}
                    contador={contadorDoGrupo(e.itens)}
                    naGaveta
                    aoAlternar={() => alternarGrupoDaGaveta(e.id)}
                  />
                  {aberto && (
                    <ul id={idDaLista} className="flex flex-col gap-1 pl-3">
                      {e.itens.map((i) => (
                        <li key={i.para} className="flex flex-col">
                          <NavLink to={i.para} className={classesDoLink}>
                            <Rotulo rotulo={i.rotulo} contador={contadorDe(i)} />
                          </NavLink>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )
            })}

            {/* Pé da gaveta. O "Sair" ocupa a largura toda (sem `self-start`) por achado do
                usuário sobre o protótipo: com largura automática, o botão herdava a mesma padding
                dos links, então o TEXTO dele alinhava com os outros textos enquanto a CAIXA
                avançava para fora — duas linhas verticais competindo. Em largura total a borda
                coincide com o fundo do item ativo, e sobra uma linha só. */}
            <div className="mt-2 flex flex-col gap-2 border-t border-chrome-ativo pt-3">
              {usuario && (
                <span className="px-3 text-xs leading-snug text-chrome-tinta-apagada">
                  <span className="block text-sm font-medium">{usuario.nomeCompleto}</span>
                  {usuario.perfil}
                </span>
              )}
              <button type="button" onClick={logout} className={`${BOTAO_DO_CHROME} text-left`}>
                Sair
              </button>
            </div>
          </nav>
        )}
      </header>

      <main>
        <Outlet />
      </main>
    </div>
  )
}
