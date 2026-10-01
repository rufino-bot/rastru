import { useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, criarComponente, definirAtivoComponente, ehConflito,
  type ComponenteDto, type NovoComponente, type OrdemDeComponentes, type TipoDeComponente,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { useBuscaPaginada, type FiltroDeBusca, type PaginaDeBusca } from '../hooks/useBuscaPaginada'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { SeletorDeOrdem, type OpcaoDeOrdem } from '../components/SeletorDeOrdem'
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

const OPCOES_DE_ORDEM: readonly OpcaoDeOrdem<OrdemDeComponentes>[] = [
  { valor: 'recentes', rotulo: 'Mais recentes' },
  { valor: 'codigo', rotulo: 'Código (A→Z)' },
  { valor: 'descricao', rotulo: 'Descrição (A→Z)' },
]

/**
 * Adapta o `FiltroDeBusca` do hook (que carrega a `ordem` dentro de `filtros`, como faceta de fora)
 * ao `FiltroDeComponentes` da API. Função de módulo, e não lambda: o hook a guarda num ref, mas a
 * estável é mais clara.
 */
function buscarComponentes(f: FiltroDeBusca): Promise<PaginaDeBusca<ComponenteDto>> {
  return listarComponentes({
    busca: f.busca,
    incluirInativos: f.incluirInativos,
    pagina: f.pagina,
    tamanho: f.tamanho,
    ordem: f.filtros?.ordem?.[0] as OrdemDeComponentes | undefined,
  })
}

export function ComponentesPage() {
  const [form, setForm] = useState<NovoComponente>(FORMULARIO_VAZIO)
  const [painelAberto, setPainelAberto] = useState(false)
  // A ordem é estado da tela, não da URL, e vai ao servidor como faceta de fora do hook.
  const [ordem, setOrdem] = useState<OrdemDeComponentes>('recentes')
  // `erroDeEscrita`: salvar e "Reativar o existente", dentro do painel. `erroDeAcao`: Inativar e
  // Reativar do item, fora dele — com o painel fechado um erro de Inativar não teria onde aparecer
  // (decisão D8 do plano da 1F).
  const [erroDeEscrita, setErroDeEscrita] = useState<string | null>(null)
  const [erroDeAcao, setErroDeAcao] = useState<string | null>(null)
  const [idReativavel, setIdReativavel] = useState<number | null>(null)
  const [enviando, setEnviando] = useState(false)

  const podeEscrever = usePodeEscrever('componentes')

  const filtros = useMemo(() => ({ ordem: [ordem] }), [ordem])
  const lista = useBuscaPaginada<ComponenteDto>({ buscar: buscarComponentes, filtros })

  // O erro de LEITURA vem do hook e é apagado pela recarga seguinte; os de ESCRITA e de AÇÃO
  // (conflito de código, 403) são estados da tela e sobrevivem à recarga que a própria ação dispara.
  // Um estado só faria a mensagem de duplicidade piscar e sumir.
  const erroDeLeitura = lista.erro === null
    ? null
    : mensagemDeErro(lista.erro, 'Não foi possível carregar os componentes.')

  function abrirPainel() {
    setErroDeEscrita(null)
    setIdReativavel(null)
    setForm(FORMULARIO_VAZIO)
    setPainelAberto(true)
  }

  function fecharPainel() {
    setPainelAberto(false)
    setForm(FORMULARIO_VAZIO)
    setErroDeEscrita(null)
    setIdReativavel(null)
  }

  // Desfecho de sucesso de um componente NOVO (ou reativado): fecha o painel e devolve a consulta ao
  // padrão — busca, página, inativos e ordem —, para o item aparecer (decisão 7 da spec da 1F). A
  // ordem é daqui e o resto é do hook; no mesmo handler os dois viram UMA requisição, então não há
  // `recarregar` junto: o `voltarAoInicio` já recarrega, mesmo com a consulta já no padrão.
  function concluirComSucesso() {
    fecharPainel()
    setErroDeAcao(null)
    setOrdem('recentes')
    lista.voltarAoInicio()
  }

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
      concluirComSucesso()
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
      setErroDeAcao(null)
      await lista.recarregar()
    } catch (e) {
      setErroDeAcao(mensagemDeErro(e, 'Não foi possível alterar o componente.'))
    }
  }

  async function reativar(id: number) {
    try {
      await definirAtivoComponente(id, true)
      concluirComSucesso()
    } catch (e) {
      setErroDeEscrita(mensagemDeErro(e, 'Não foi possível reativar o componente.'))
    }
  }

  const buscando = lista.textoDaBusca.trim() !== ''

  return (
    <Pagina
      titulo="Componentes"
      acao={podeEscrever && !painelAberto && <Botao onClick={abrirPainel}>Novo componente</Botao>}
    >
      {podeEscrever && painelAberto && (
        <PainelDeEscrita titulo="Novo componente" aoEnviar={salvar} aoFechar={fecharPainel}>
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
          <BannerDeErro mensagem={erroDeEscrita} />
          {idReativavel !== null && (
            <Botao variante="secundario" onClick={() => reativar(idReativavel)} className="self-start">
              Reativar o existente
            </Botao>
          )}
          <Botao type="submit" carregando={enviando} rotuloCarregando="Salvando…" className="self-start">
            Adicionar
          </Botao>
        </PainelDeEscrita>
      )}

      <BannerDeErro mensagem={erroDeAcao ?? erroDeLeitura} />

      {/*
        A barra de filtros é o que não cabia em 448px (spec §7). Em `max-w-3xl` os controles cabem
        lado a lado a partir de `sm`, e empilham no celular sem rolagem horizontal.
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
        <SeletorDeOrdem opcoes={OPCOES_DE_ORDEM} valor={ordem} aoMudar={setOrdem} />
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
              : podeEscrever ? 'Use o botão Novo componente para criar o primeiro.' : undefined
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
