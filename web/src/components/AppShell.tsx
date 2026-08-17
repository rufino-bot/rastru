import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

interface ItemDeNavegacao {
  para: string
  rotulo: string
}

/**
 * Todos os itens aparecem para todos os perfis, de propósito: a leitura de todos estes recursos é
 * liberada para qualquer usuário autenticado no backend (conferido em 2026-08-10). O gating de
 * perfil vive na AÇÃO — formulário e botões de (in)ativar —, não aqui.
 *
 * Medido em 2026-08-10, Chrome headless contra o CSS do build, em 768px (a âncora que a spec §11
 * exige): os 5 links de hoje cabem sem estourar; a barra estoura na horizontal a partir do
 * **sétimo** link (86px de estouro), não de "10+" — Qualidade (Fase 5) e Expedição (Fase 6) são o
 * sexto e o sétimo link, não uma hipótese distante. O agrupamento (ex.: "Cadastros" com submenu,
 * spec §6) entra quando o sétimo link chegar, não antes; até lá o custo é conhecido e aceito.
 *
 * **O `end` do item `/` é REDUNDANTE nesta versão, e isso foi medido — não suposto.** Apagar os
 * dois `end={i.para === '/'}` deixa a suíte 10/10 VERDE (mutação M7, medida em 2026-08-10 com
 * react-router 7.18.1). O motivo está na implementação do `NavLink`: além de `startsWith`, ele
 * exige que o caractere logo após o caminho seja `/`, então `to="/"` em `/setores` compara
 * `charAt(1)`, que é `s`, e não casa. O `end` fica assim mesmo, por duas razões: declara a
 * intenção no ponto de uso, e esse comportamento do react-router já mudou uma vez — versões v6
 * iniciais casavam `/` com tudo, que é justamente o bug que o `end` existe para evitar.
 *
 * Consequência para quem for medir mutação aqui: **M7 é mutante EQUIVALENTE, não lacuna de
 * teste.** Não escreva teste tentando matá-la; não há comportamento que a distinga.
 */
const ITENS: ItemDeNavegacao[] = [
  { para: '/', rotulo: 'Início' },
  { para: '/pedidos', rotulo: 'Pedidos' },
  { para: '/componentes', rotulo: 'Componentes' },
  { para: '/materiais', rotulo: 'Materiais' },
  { para: '/setores', rotulo: 'Setores' },
]

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

function classesDoLink({ isActive }: { isActive: boolean }): string {
  // Três dimensões de distinção: fundo, tinta e PESO. O peso entrou por decisão do usuário em
  // 2026-08-10, depois de ler o protótipo: o fundo do ativo dá só 1,518 contra o chrome, então
  // fundo+tinta não bastavam "de bate e pronto". Clarear o fundo era a alternativa, e é troca
  // ruim — no teto do que ainda passa AA (25% de branco) o destaque sobe para 1,974 e o rótulo
  // CAI de 6,240 para 4,801. Peso não entra nessa troca: não altera contraste nenhum.
  return `${CONTROLE_BASE} ${
    isActive
      ? 'bg-chrome-ativo font-semibold text-superficie'
      : 'font-medium text-chrome-tinta-fraca hover:bg-chrome-hover hover:text-superficie'
  }`
}

export function AppShell() {
  const { estado, logout } = useAuth()
  const [gavetaAberta, setGavetaAberta] = useState(false)
  const location = useLocation()

  const usuario = estado.status === 'autenticado' ? estado.usuario : null

  // A gaveta fecha num efeito sobre `location.key`, não no `onClick` de cada link dela: o `onClick`
  // só reage ao clique NAQUELES links, e deixa aberta a gaveta quando a navegação vem de um link no
  // CONTEÚDO (a HomePage tem quatro), do botão voltar do navegador, ou de um redirecionamento
  // programático — é justamente o caso que o efeito serve, não o custo dele. `location.pathname` não
  // serve de dependência: não muda quando o link clicado é da MESMA tela em que o usuário já está, e
  // esse caso ficaria sem fechar. `location.key` muda mesmo nesse caso — medido em 2026-08-10 com
  // react-router 7.18.1 (`fecha a gaveta ao navegar para a tela em que já está`, abaixo): o `Link`
  // não reaproveita a chave da entrada atual do histórico ao navegar para o mesmo caminho. O efeito
  // roda uma vez por navegação, não por render.
  useEffect(() => {
    setGavetaAberta(false)
  }, [location.key])

  return (
    <div className="min-h-screen bg-fundo font-sans text-tinta">
      <header className="bg-chrome">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-3 sm:px-6">
          <span className="text-lg font-semibold tracking-tight text-marca">Rastru</span>

          {/* Barra: some abaixo de 768px, onde a gaveta assume. */}
          <nav aria-label="Principal" className="hidden md:flex md:items-center md:gap-1">
            {ITENS.map((i) => (
              <NavLink key={i.para} to={i.para} end={i.para === '/'} className={classesDoLink}>
                {i.rotulo}
              </NavLink>
            ))}
          </nav>

          <div className="hidden md:flex md:items-center md:gap-3">
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
            onClick={() => setGavetaAberta((a) => !a)}
            aria-expanded={gavetaAberta}
            aria-label={gavetaAberta ? 'Fechar menu' : 'Abrir menu'}
            className={`${BOTAO_DO_CHROME} md:hidden`}
          >
            {gavetaAberta ? '✕' : '☰'}
          </button>
        </div>

        {/* Gaveta: mesma lista, empilhada, só abaixo de 768px. O celular Android da fábrica é uso
            declarado, não hipótese. */}
        {gavetaAberta && (
          <nav aria-label="Menu" className="flex flex-col gap-1 border-t border-chrome-ativo px-4 pb-4 md:hidden">
            {ITENS.map((i) => (
              <NavLink key={i.para} to={i.para} end={i.para === '/'} className={classesDoLink}>
                {i.rotulo}
              </NavLink>
            ))}

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
