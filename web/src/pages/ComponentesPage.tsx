import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type TipoDeComponente,
} from '../api/cadastros'

const FORMULARIO_VAZIO: NovoComponente = { codigo: '', descricao: '', tipo: 'Fabricado' }

/** As três opções de `CK_Componente_Tipo`. Lista fechada, ao contrário de `unidadeMedida`. */
const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

/** Dentro do teto de 100 do backend, de propósito: um valor acima viraria 400. */
const TAMANHOS = [20, 50, 100]

export function ComponentesPage() {
  const [componentes, setComponentes] = useState<ComponenteDto[]>([])
  const [total, setTotal] = useState(0)
  const [busca, setBusca] = useState('')
  const [pagina, setPagina] = useState(1)
  const [tamanho, setTamanho] = useState(20)
  const [incluirInativos, setIncluirInativos] = useState(false)
  const [form, setForm] = useState<NovoComponente>(FORMULARIO_VAZIO)
  const [erro, setErro] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)

  // Guarda de sequência da corrida de resposta fora de ordem (I3 da review): quem ganha não pode
  // ser a última requisição a RESPONDER, e sim a última a ser ENVIADA. `sequenciaRef` é
  // incrementado a cada chamada de `carregar`; cada chamada captura o próprio número antes do
  // `await` e só aplica os efeitos pós-`await` se ainda for a mais recente emitida. Não é
  // `AbortController` de propósito — isso exigiria `listarComponentes` aceitar um `AbortSignal`,
  // ou seja, mudar `cadastros.ts`, fora do escopo desta task.
  const sequenciaRef = useRef(0)

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  async function carregar(b: string, inc: boolean, p: number, t: number) {
    const minhaSequencia = ++sequenciaRef.current
    setCarregando(true)
    try {
      const resposta = await listarComponentes({ busca: b, incluirInativos: inc, pagina: p, tamanho: t })
      if (minhaSequencia !== sequenciaRef.current) return
      setComponentes(resposta.itens)
      setTotal(resposta.total)
      setErro(null)
    } catch {
      if (minhaSequencia !== sequenciaRef.current) return
      setErro('Não foi possível carregar os componentes.')
    } finally {
      if (minhaSequencia === sequenciaRef.current) setCarregando(false)
    }
  }

  useEffect(() => {
    carregar(busca, incluirInativos, pagina, tamanho)
  }, [busca, incluirInativos, pagina, tamanho])

  // Trocar a busca, o tamanho de pagina ou o filtro de inativos VOLTA para a pagina 1. Sem isto,
  // buscar algo que cabe em 2 paginas estando na pagina 7 mostra lista vazia, com cara de bug.
  function mudarBusca(valor: string) {
    setPagina(1)
    setBusca(valor)
  }

  function mudarTamanho(valor: number) {
    setPagina(1)
    setTamanho(valor)
  }

  function mudarInativos(valor: boolean) {
    setPagina(1)
    setIncluirInativos(valor)
  }

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErro(null)
    setIdReativavel(null)
    try {
      const resultado = await criarComponente(form)
      if (ehConflito(resultado)) {
        // O conflito e sempre sobre o codigo (UQ_Componente_Codigo); descricao repetida passa.
        if (resultado.existeInativo) {
          setErro(`Já existe um componente com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErro('Já existe um componente com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await carregar(busca, incluirInativos, pagina, tamanho)
    } catch {
      setErro('Não foi possível salvar o componente.')
    }
  }

  // O 403 do backend e a fronteira de perfil (o link aparece para todos de proposito, e
  // PATCH /componentes/{id}/ativo e [Authorize(Roles = "Administrador,PCP")]), entao aqui e onde
  // um usuario sem permissao descobre isso — sem try/catch viraria uma promise rejeitada sem
  // tratamento e a tela nao diria nada.
  async function alternarAtivo(componente: ComponenteDto) {
    try {
      await definirAtivoComponente(componente.id, !componente.ativo)
      setErro(null)
      await carregar(busca, incluirInativos, pagina, tamanho)
    } catch {
      setErro('Não foi possível alterar o componente.')
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar(busca, incluirInativos, pagina, tamanho)
    } catch {
      setErro('Não foi possível reativar o componente.')
    }
  }

  return (
    <div className="min-h-screen p-6 max-w-md mx-auto flex flex-col gap-4">
      <Link to="/" className="text-sm text-gray-500">&larr; Início</Link>
      <h1 className="text-2xl font-semibold">Componentes</h1>

      <form onSubmit={salvar} className="flex flex-col gap-2">
        <input
          value={form.codigo}
          onChange={(e) => setForm({ ...form, codigo: e.target.value })}
          placeholder="Código"
          required
          className="border rounded px-3 py-2"
        />
        <input
          value={form.descricao}
          onChange={(e) => setForm({ ...form, descricao: e.target.value })}
          placeholder="Descrição"
          required
          className="border rounded px-3 py-2"
        />
        {/* Lista fechada (CK_Componente_Tipo): select, nao input livre. */}
        <select
          value={form.tipo}
          onChange={(e) => setForm({ ...form, tipo: e.target.value as TipoDeComponente })}
          aria-label="Tipo"
          className="border rounded px-3 py-2"
        >
          {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
        <button type="submit" className="border rounded px-3 py-2 self-start">Adicionar</button>
      </form>

      {erro && <p className="text-red-600 text-sm">{erro}</p>}
      {idReativavel !== null && (
        <button onClick={() => reativar(idReativavel)} className="border rounded px-3 py-2 self-start">
          Reativar o existente
        </button>
      )}

      <input
        value={busca}
        onChange={(e) => mudarBusca(e.target.value)}
        placeholder="Buscar por código ou descrição"
        className="border rounded px-3 py-2"
      />

      <label className="flex items-center gap-2 text-sm text-gray-600">
        <input
          type="checkbox"
          checked={incluirInativos}
          onChange={(e) => mudarInativos(e.target.checked)}
        />
        Mostrar inativos
      </label>

      <label className="flex items-center gap-2 text-sm text-gray-600">
        Por página
        <select
          value={tamanho}
          onChange={(e) => mudarTamanho(Number(e.target.value))}
          className="border rounded px-2 py-1"
        >
          {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
        </select>
      </label>

      {carregando ? <p className="text-gray-600">Carregando…</p> : (
        <ul className="flex flex-col gap-2">
          {componentes.map((c) => (
            <li key={c.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className={c.ativo ? '' : 'text-gray-400 line-through'}>
                <strong>{c.codigo}</strong> — {c.descricao} ({c.tipo})
              </span>
              <button onClick={() => alternarAtivo(c)} className="text-sm border rounded px-2 py-1">
                {c.ativo ? 'Inativar' : 'Reativar'}
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="flex items-center gap-3 text-sm">
        <button
          onClick={() => setPagina(pagina - 1)}
          disabled={pagina <= 1}
          className="border rounded px-3 py-1 disabled:opacity-40"
        >
          Anterior
        </button>
        <span className="text-gray-600">
          Página {pagina} de {totalDePaginas} — {total} no total
        </span>
        <button
          onClick={() => setPagina(pagina + 1)}
          disabled={pagina >= totalDePaginas}
          className="border rounded px-3 py-1 disabled:opacity-40"
        >
          Próxima
        </button>
      </div>
    </div>
  )
}
