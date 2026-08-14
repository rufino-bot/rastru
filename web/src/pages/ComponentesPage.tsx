import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type TipoDeComponente,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'

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
  const [enviando, setEnviando] = useState(false)

  // Guarda de sequência da corrida de resposta fora de ordem (I3 da review): quem ganha não pode
  // ser a última requisição a RESPONDER, e sim a última a ser ENVIADA. `sequenciaRef` é
  // incrementado a cada chamada de `carregar`; cada chamada captura o próprio número antes do
  // `await` e só aplica os efeitos pós-`await` se ainda for a mais recente emitida. Não é
  // `AbortController` de propósito — isso exigiria `listarComponentes` aceitar um `AbortSignal`,
  // ou seja, mudar `cadastros.ts`, fora do escopo desta task.
  const sequenciaRef = useRef(0)

  const totalDePaginas = Math.max(1, Math.ceil(total / tamanho))

  const podeEscrever = usePodeEscrever('componentes')

  async function carregar(b: string, inc: boolean, p: number, t: number) {
    const minhaSequencia = ++sequenciaRef.current
    setCarregando(true)
    try {
      const resposta = await listarComponentes({ busca: b, incluirInativos: inc, pagina: p, tamanho: t })
      if (minhaSequencia !== sequenciaRef.current) return
      setComponentes(resposta.itens)
      setTotal(resposta.total)
      setErro(null)
    } catch (e) {
      if (minhaSequencia !== sequenciaRef.current) return
      setErro(mensagemDeErro(e, 'Não foi possível carregar os componentes.'))
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
    setEnviando(true)
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
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível salvar o componente.'))
    } finally {
      setEnviando(false)
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
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível alterar o componente.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      setErro(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await carregar(busca, incluirInativos, pagina, tamanho)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível reativar o componente.'))
    }
  }

  const buscando = busca.trim() !== ''

  return (
    <Pagina titulo="Componentes">
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Campo rotulo="Código">
              {(id) => (
                <input
                  id={id}
                  value={form.codigo}
                  onChange={(e) => setForm({ ...form, codigo: e.target.value })}
                  required
                  className={`${CLASSES_DE_CONTROLE} font-mono`}
                />
              )}
            </Campo>
            {/* Lista fechada (CK_Componente_Tipo): select, não input livre. */}
            <Campo rotulo="Tipo">
              {(id) => (
                <select
                  id={id}
                  value={form.tipo}
                  onChange={(e) => setForm({ ...form, tipo: e.target.value as TipoDeComponente })}
                  className={CLASSES_DE_CONTROLE}
                >
                  {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
                </select>
              )}
            </Campo>
          </div>
          <Campo rotulo="Descrição">
            {(id) => (
              <input
                id={id}
                value={form.descricao}
                onChange={(e) => setForm({ ...form, descricao: e.target.value })}
                required
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </form>
      )}

      <BannerDeErro mensagem={erro} />

      {idReativavel !== null && (
        <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
          Reativar o existente
        </Botao>
      )}

      {/*
        A barra de filtros é o que não cabia em 448px (spec §7). Em `max-w-3xl` os três controles
        cabem lado a lado a partir de `sm`, e empilham no celular sem rolagem horizontal.
      */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end">
        <div className="flex-1">
          <Campo rotulo="Buscar por código ou descrição">
            {(id) => (
              <input
                id={id}
                value={busca}
                onChange={(e) => mudarBusca(e.target.value)}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
        <label className="flex items-center gap-2 text-sm text-tinta-fraca sm:pb-2.5">
          <input
            type="checkbox"
            checked={incluirInativos}
            onChange={(e) => mudarInativos(e.target.checked)}
            className="size-4 accent-acao"
          />
          Mostrar inativos
        </label>
        <Campo rotulo="Por página">
          {(id) => (
            <select
              id={id}
              value={tamanho}
              onChange={(e) => mudarTamanho(Number(e.target.value))}
              className={CLASSES_DE_CONTROLE}
            >
              {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          )}
        </Campo>
      </div>

      {carregando ? (
        <p className="text-tinta-fraca">Carregando…</p>
      ) : erro === null && componentes.length === 0 ? (
        // DECISÃO U1 (usuário, 2026-08-13). `erro === null &&` NÃO é enfeite: no `catch` de
        // `carregar`, `setComponentes` nunca é chamado, então a lista fica `[]` e `.length === 0`
        // sozinho também é verdade numa falha de rede — mostrando este estado vazio JUNTO do
        // banner, e afirmando "não há componentes" a partir de uma falha de conexão. É a MESMA
        // forma do Critical que o fix pass da Task 8 pagou; `SetoresPage.tsx:129` diz o mesmo.
        // Os três vazios que a spec §9 manda distinguir: busca sem resultado, catálogo vazio e —
        // acima, no banner — erro de rede. Antes os três renderizavam a mesma lista muda.
        <EstadoVazio
          titulo={buscando ? 'Nenhum componente encontrado' : 'Nenhum componente cadastrado'}
          descricao={
            buscando
              ? `Nada corresponde a "${busca}".`
              : podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined
          }
        />
      ) : (
        <ListaDeCadastro>
          {componentes.map((c) => (
            <ItemDeCadastro
              key={c.id}
              ativo={c.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(c)}>
                  {c.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{c.codigo}</span>
              {' — '}
              {c.descricao}
              {' '}
              <Pilula>{c.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      {/* PROVISÓRIO — a Task 9B troca este bloco inteiro por `<ControlesDePaginacao>`. Ele está
          aqui, com as primitivas em vez das classes antigas, só porque a 9A não pode adotar a
          primitiva de paginação sem adotar o hook junto (o `if (totalDePaginas <= 1) return null`
          dela quebra dois testes desta suíte, e essa quebra é da 9B). NÃO acrescente o
          `return null` de uma página só aqui: isso importaria a colisão da 9B para dentro da 9A. */}
      <nav aria-label="Paginação" className="flex flex-wrap items-center justify-between gap-3">
        <Botao variante="secundario" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>
          Anterior
        </Botao>
        <span className="text-sm text-tinta-fraca">
          Página {pagina} de {totalDePaginas} — {total} no total
        </span>
        <Botao variante="secundario" disabled={pagina >= totalDePaginas} onClick={() => setPagina(pagina + 1)}>
          Próxima
        </Botao>
      </nav>
    </Pagina>
  )
}
