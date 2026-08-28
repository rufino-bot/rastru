import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type TipoDeComponente,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { useBuscaPaginada } from '../hooks/useBuscaPaginada'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { ControlesDePaginacao } from '../components/ControlesDePaginacao'

const FORMULARIO_VAZIO: NovoComponente = { codigo: '', descricao: '', tipo: 'Fabricado' }

/** As três opções de `CK_Componente_Tipo`. Lista fechada, ao contrário de `unidadeMedida`. */
const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

/** Dentro do teto de 100 do backend, de propósito: um valor acima viraria 400. */
const TAMANHOS = [20, 50, 100]

export function ComponentesPage() {
  const [form, setForm] = useState<NovoComponente>(FORMULARIO_VAZIO)
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('componentes')

  // `listarComponentes` é passada direto por ser estável (função de módulo) e por a assinatura
  // dela ser estruturalmente compatível com a que o hook pede — `FiltroDeComponentes`/`PaginaDe<T>`
  // de um lado, `FiltroDeBusca`/`PaginaDeBusca<T>` do outro: nomes diferentes, mesma forma
  // (compila, MEDIDO). Nada de lambda inline aqui: o hook a guarda num ref justamente para tolerar
  // isso, mas passar a estável é mais claro.
  const lista = useBuscaPaginada<ComponenteDto>({ buscar: listarComponentes })

  // Dois erros, e não um: o de LEITURA vem do hook e é apagado pela recarga seguinte; o de
  // ESCRITA (conflito de código, 403) tem de sobreviver à recarga que o próprio salvar dispara.
  // Um estado só faria a mensagem de duplicidade piscar e sumir — o defeito que a review da Task 11
  // da Fase 1A chamou de "erro que pisca". É por causa DESTA divisão que a 9A podia viver com um
  // `erro` único e a 9B não pode.
  const erroDeLeitura = lista.erro === null
    ? null
    : mensagemDeErro(lista.erro, 'Não foi possível carregar os componentes.')

  async function salvar(e: FormEvent) {
    e.preventDefault()
    setErroDeEscrita(null)
    setIdReativavel(null)
    setEnviando(true)
    try {
      const resultado = await criarComponente(form)
      if (ehConflito(resultado)) {
        // O conflito é sempre sobre o código (UQ_Componente_Codigo); descrição repetida passa.
        if (resultado.existeInativo) {
          setErroDeEscrita(`Já existe um componente com o código "${form.codigo}" inativo.`)
          setIdReativavel(resultado.idExistente)
        } else {
          setErroDeEscrita('Já existe um componente com este código.')
        }
        return
      }
      setForm(FORMULARIO_VAZIO)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível salvar o componente.'))
    } finally {
      setEnviando(false)
    }
  }

  // O 403 do backend é a fronteira real de perfil (F2): esconder o botão é conveniência, e o
  // try/catch é o que faz a tela dizer alguma coisa quando ele chega assim mesmo.
  async function alternarAtivo(componente: ComponenteDto) {
    try {
      await definirAtivoComponente(componente.id, !componente.ativo)
      setErroDeEscrita(null)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível alterar o componente.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      setErroDeEscrita(null)
      setIdReativavel(null)
      setForm(FORMULARIO_VAZIO)
      await lista.recarregar()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o componente.'))
    }
  }

  const buscando = lista.textoDaBusca.trim() !== ''

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

      <BannerDeErro mensagem={erroDeEscrita ?? erroDeLeitura} />

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
                value={lista.textoDaBusca}
                onChange={(e) => lista.mudarBusca(e.target.value)}
                className={CLASSES_DE_CONTROLE}
              />
            )}
          </Campo>
        </div>
        <label className="flex items-center gap-2 text-sm text-tinta-fraca sm:pb-2.5">
          <input
            type="checkbox"
            checked={lista.incluirInativos}
            onChange={(e) => lista.mudarInativos(e.target.checked)}
            className="size-4 accent-acao"
          />
          Mostrar inativos
        </label>
        <Campo rotulo="Por página">
          {(id) => (
            <select
              id={id}
              value={lista.tamanho}
              onChange={(e) => lista.mudarTamanho(Number(e.target.value))}
              className={CLASSES_DE_CONTROLE}
            >
              {TAMANHOS.map((t) => <option key={t} value={t}>{t}</option>)}
            </select>
          )}
        </Campo>
      </div>

      {lista.carregando ? (
        <EstadoCarregando />
      ) : erroDeLeitura === null && lista.itens.length === 0 ? (
        // DECISÃO U1 (usuário, 2026-08-13), e ela CORRIGE o que a versão anterior deste bloco
        // dizia. Sem o `erroDeLeitura === null &&`, a tela mostra o banner de erro E "Nenhum
        // componente cadastrado" ao mesmo tempo sob GET 500 — MEDIDO por sonda no pré-flight —,
        // afirmando "não há componentes" a partir de uma falha de rede. É a mesma forma do
        // Critical que o fix pass da Task 8 pagou (`SetoresPage.tsx:129`). Usa-se o derivado
        // `erroDeLeitura`, e não `lista.erro`, porque ele é `null` exatamente quando `lista.erro`
        // é, e lê melhor ao lado do `BannerDeErro` logo acima.
        //
        // Os três vazios que a spec §9 manda distinguir: busca sem resultado, catálogo vazio e —
        // acima, no banner — erro de rede. Antes os três renderizavam a mesma lista muda.
        <EstadoVazio
          titulo={buscando ? 'Nenhum componente encontrado' : 'Nenhum componente cadastrado'}
          descricao={
            buscando
              ? `Nada corresponde a "${lista.textoDaBusca}".`
              : podeEscrever ? 'Use o formulário acima para criar o primeiro.' : undefined
          }
        />
      ) : (
        <ListaDeCadastro>
          {lista.itens.map((c) => (
            <ItemDeCadastro
              key={c.id}
              ativo={c.ativo}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => alternarAtivo(c)}>
                  {c.ativo ? 'Inativar' : 'Reativar'}
                </Botao>
              )}
            >
              {/*
                SEM overlay de propósito (decisão da Task 10 — ver o aviso do m6 em
                ItemDeCadastro.tsx): este item TEM uma `acao` (Inativar/Reativar) no mesmo `<li>`,
                e estender a área clicável do link ao cartão inteiro engoliria o botão — clicar
                nele devolveria o link, não a ação. O link cobre só o texto (código — descrição);
                a `acao` fica fora dele, e o alvo de clique menor é o custo aceito.
              */}
              <Link
                to={`/componentes/${c.id}`}
                className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
              >
                <span className="font-mono font-semibold">{c.codigo}</span>
                {' — '}
                {c.descricao}
              </Link>
              {' '}
              <Pilula>{c.tipo}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}

      <ControlesDePaginacao
        pagina={lista.pagina}
        totalDePaginas={lista.totalDePaginas}
        total={lista.total}
        aoMudarPagina={lista.irParaPagina}
      />
    </Pagina>
  )
}
