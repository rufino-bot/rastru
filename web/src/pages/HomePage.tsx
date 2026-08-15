import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, listarMateriais, listarSetores, listarPedidos,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'

interface Contagens {
  pedidosAbertos: number
  componentes: number
  materiais: number
  setores: number
}

function CartaoDeContagem({ titulo, valor, para }: { titulo: string; valor: number | null; para: string }) {
  return (
    <Link
      to={para}
      className="flex flex-col gap-1 rounded-lg border border-borda bg-superficie px-5 py-6 transition-colors hover:border-acao focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      {/* Traço em vez de zero enquanto carrega: "0 pedidos" é uma afirmação, e ela seria falsa. */}
      <span className="text-3xl font-semibold text-tinta">{valor === null ? '—' : valor}</span>
      <span className="text-sm text-tinta-fraca">{titulo}</span>
    </Link>
  )
}

export function HomePage() {
  const [contagens, setContagens] = useState<Contagens | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    setErro(null)
    try {
      // `tamanho: 1` no de componentes: só o `total` interessa, e assim nenhum item trafega. As
      // outras três listagens ainda não são paginadas no backend (dívida rastreada da 1B: o
      // `PaginaDto<T>` não foi migrado para Setor/Material) — quando forem, este cartão vira o
      // molde das outras.
      const [paginaDeComponentes, pedidos, materiais, setores] = await Promise.all([
        listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 }),
        listarPedidos(),
        listarMateriais(false),
        listarSetores(false),
      ])
      setContagens({
        pedidosAbertos: pedidos.filter((p) => p.status === 'Aberto').length,
        componentes: paginaDeComponentes.total,
        materiais: materiais.length,
        setores: setores.length,
      })
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar os números do sistema.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  return (
    <Pagina titulo="Início">
      <BannerDeErro mensagem={erro} />

      {carregando && <p className="text-tinta-fraca">Carregando…</p>}

      <div className="grid gap-4 sm:grid-cols-2">
        <CartaoDeContagem titulo="pedidos abertos" valor={contagens?.pedidosAbertos ?? null} para="/pedidos" />
        <CartaoDeContagem titulo="componentes ativos" valor={contagens?.componentes ?? null} para="/componentes" />
        <CartaoDeContagem titulo="materiais ativos" valor={contagens?.materiais ?? null} para="/materiais" />
        <CartaoDeContagem titulo="setores ativos" valor={contagens?.setores ?? null} para="/setores" />
      </div>
    </Pagina>
  )
}
