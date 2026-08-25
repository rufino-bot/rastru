import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import {
  obterComponente, listarMateriais, listarSetores,
  type ComponenteDto, type MaterialDto, type SetorDto,
} from '../api/cadastros'
import {
  listarFilhosPadrao, listarMateriaisPadrao, listarRoteiroPadrao,
  salvarFilhosPadrao, salvarMateriaisPadrao, salvarRoteiroPadrao,
  type FilhoPadraoDto, type MaterialPadraoDto, type RoteiroPadraoDto,
  type LinhaDeFilho, type LinhaDeMaterial, type LinhaDeRoteiro,
} from '../api/receitaPadrao'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { SeletorComBusca } from '../components/SeletorComBusca'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'

interface PropsDaSecao {
  idTitulo: string
  titulo: string
  carregando: boolean
  erro: unknown
  /** Frase específica desta seção, para `mensagemDeErro(erro, fallback)` — a assinatura pede os
      dois argumentos (`web/src/api/erros.ts:31`), um só não compila. */
  erroFallback: string
  /** Quantas linhas a seção tem. Zero + sem erro = estado vazio. */
  quantidade: number
  /** Texto do estado vazio. Distingue "não montei ainda" de "não achei". */
  vazio: string
  children: ReactNode
  /**
   * Conteúdo de escrita (linha nova + Salvar), Task 11. Mostrado assim que a carga assenta —
   * INDEPENDENTE de `quantidade`, porque adicionar o primeiro item de uma receita vazia também é
   * escrita válida: se o rodapé só aparecesse com `quantidade > 0`, uma receita vazia ficaria sem
   * como ganhar a primeira linha. `undefined` quando o perfil não pode escrever (gating).
   */
  rodape?: ReactNode
}

/**
 * `aria-labelledby` liga a `<section>` ao `id` do próprio `<h2>`, virando landmark `region` com
 * nome acessível — decisão 2 do brief da Task 10. Sem isso, com as 3 seções carregando (ou
 * falhando) ao mesmo tempo, `getByRole('status')`/`findByRole('alert')` global lançaria por
 * múltiplos matches; com o landmark, o teste escopa com `within()` e prova QUAL seção está em
 * qual estado — uma contagem global não distingue isso.
 */
function Secao({ idTitulo, titulo, carregando, erro, erroFallback, quantidade, vazio, children, rodape }: PropsDaSecao) {
  return (
    <section aria-labelledby={idTitulo} className="flex flex-col gap-3">
      <h2 id={idTitulo} className="text-lg font-medium text-tinta">{titulo}</h2>
      {carregando && <EstadoCarregando />}
      {!carregando && erro !== null && <BannerDeErro mensagem={mensagemDeErro(erro, erroFallback)} />}
      {/* `erro === null &&` NÃO é redundante com o `!carregando`: sem ele, uma falha na carga
          mostra o estado vazio JUNTO do banner de erro. É o Critical que a Fase 1D pagou duas
          vezes (Tasks 8 e 10). Não remova. */}
      {!carregando && erro === null && quantidade === 0 && <EstadoVazio titulo={vazio} />}
      {!carregando && erro === null && quantidade > 0 && children}
      {/* `erro === null &&` aqui TAMBÉM não é redundante com o `!carregando`, e por um motivo
          diferente do vazio acima: se o GET desta seção falhou, o estado fica `[]` — igual ao
          vazio legítimo. Sem a guarda, o rodapé (formulário + Salvar) apareceria mesmo assim, o
          usuário adicionaria uma linha (`sujo = true`), clicaria Salvar, e o POST mandaria SÓ essa
          linha nova — APAGANDO no servidor a receita inteira que ele nunca chegou a ver, porque a
          gravação substitui a lista inteira (não há PATCH por linha). Não remova. */}
      {!carregando && erro === null && rodape}
    </section>
  )
}

/**
 * Leitura E escrita da receita padrão de um Componente (Task 10 + Task 11 da Fase 1C).
 *
 * 4 buscas independentes (o componente, para o cabeçalho, e as 3 receitas), cada uma com o
 * PRÓPRIO `carregando`/`erro`: um `useState`/`useEffect` por busca, não um hook genérico
 * compartilhado — é isto que faz "uma falha em materiais não apaga a lista de filhos que
 * carregou bem" (decisão 2) ser verdade por construção, não por disciplina.
 *
 * ## Escrita (Task 11)
 *
 * Cada seção reaproveita o MESMO array de estado da leitura (`filhos`/`materiais`/`roteiro`) como
 * rascunho: Adicionar/Remover mutam esse array diretamente, e um booleano `*Sujo` — não uma
 * comparação estrutural com o que veio do servidor — marca "há alteração pendente". Salvar manda a
 * lista INTEIRA da seção (substituição, não incremento: não existe endpoint de PATCH por linha,
 * `web/src/api/receitaPadrao.ts`). Linhas novas recebem um `id` local NEGATIVO só para servir de
 * `key` do React até a resposta do servidor trazer os ids reais — nunca colide com um id real
 * (sempre positivo, `IDENTITY`).
 *
 * O roteiro não manda `ordem` nenhuma ao servidor (`LinhaDeRoteiro` só carrega `setorId` — quem
 * numera de verdade, ao salvar, é o servidor). NA TELA, porém, o número exibido é a POSIÇÃO no
 * array (`i + 1`), não o `r.ordem` que a última leitura trouxe — decisão da review das Tasks
 * 10-12: `r.ordem` fica desatualizado a cada edição local (remover o passo do meio e adicionar
 * um novo produzia número pulado, número repetido, e dois botões "Remover" com o MESMO nome
 * acessível), e a posição no array é sempre densa e sequencial por construção.
 *
 * `usePodeEscrever('componentes')`, não `'receitaPadrao')`: a receita padrão e o Componente são um
 * conceito de permissão só (ver `web/src/auth/permissoes.ts`). Gating na AÇÃO — o rodapé de cada
 * seção (formulário + Salvar) some para quem não escreve, e a LISTA continua visível, porque
 * leitura é de todo perfil. O `try/catch` de cada `salvar*` continua obrigatório mesmo assim: o
 * 403 do backend é a fronteira real, esconder botão não é segurança (adendo F2).
 *
 * FRONTEIRA DECLARADA (spec §3.2): sair desta página com rascunho não salvo PERDE o rascunho. Não
 * há bloqueio de navegação — está fora de escopo da Fase 1C, com gatilho escrito na spec §6. Não é
 * esquecimento: é corte deliberado, registrado aqui para o próximo leitor não reabrir sozinho.
 */
export function ComponenteDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const componenteId = Number(id)
  const idValido = !Number.isNaN(componenteId)

  const podeEscrever = usePodeEscrever('componentes')

  const [componente, setComponente] = useState<ComponenteDto | null>(null)
  const [carregandoComponente, setCarregandoComponente] = useState(true)
  const [erroComponente, setErroComponente] = useState<unknown>(null)

  const [filhos, setFilhos] = useState<FilhoPadraoDto[]>([])
  const [carregandoFilhos, setCarregandoFilhos] = useState(true)
  const [erroFilhos, setErroFilhos] = useState<unknown>(null)
  const [filhoSelecionado, setFilhoSelecionado] = useState<ComponenteDto | null>(null)
  const [quantidadeFilho, setQuantidadeFilho] = useState('')
  const [filhosSujo, setFilhosSujo] = useState(false)
  const [salvandoFilhos, setSalvandoFilhos] = useState(false)
  const [erroSalvarFilhos, setErroSalvarFilhos] = useState<unknown>(null)
  const proximoIdFilhoRef = useRef(-1)

  const [materiais, setMateriais] = useState<MaterialPadraoDto[]>([])
  const [carregandoMateriais, setCarregandoMateriais] = useState(true)
  const [erroMateriais, setErroMateriais] = useState<unknown>(null)
  const [materiaisCadastro, setMateriaisCadastro] = useState<MaterialDto[]>([])
  const [materialSelecionadoId, setMaterialSelecionadoId] = useState<number | null>(null)
  const [quantidadeMaterial, setQuantidadeMaterial] = useState('')
  const [materiaisSujo, setMateriaisSujo] = useState(false)
  const [salvandoMateriais, setSalvandoMateriais] = useState(false)
  const [erroSalvarMateriais, setErroSalvarMateriais] = useState<unknown>(null)
  const proximoIdMaterialRef = useRef(-1)

  const [roteiro, setRoteiro] = useState<RoteiroPadraoDto[]>([])
  const [carregandoRoteiro, setCarregandoRoteiro] = useState(true)
  const [erroRoteiro, setErroRoteiro] = useState<unknown>(null)
  const [setoresCadastro, setSetoresCadastro] = useState<SetorDto[]>([])
  const [setorSelecionadoId, setSetorSelecionadoId] = useState<number | null>(null)
  const [roteiroSujo, setRoteiroSujo] = useState(false)
  const [salvandoRoteiro, setSalvandoRoteiro] = useState(false)
  const [erroSalvarRoteiro, setErroSalvarRoteiro] = useState<unknown>(null)
  const proximoIdRoteiroRef = useRef(-1)

  // Item 6 da review (Tasks 10-12): `/componentes/:id` NÃO desmonta ao mudar de parâmetro (é a
  // MESMA instância do componente React que recebe um `id` novo), então navegar de
  // `/componentes/1` para `/componentes/2` com a resposta de 1 ainda em voo faria o `.then`
  // antigo escrever por cima do que 2 acabou de carregar — e, se o usuário editasse e salvasse, o
  // POST iria para `/componentes/2/…` com as linhas de 1. `cancelado` fecha isso: a flag nasce
  // `false` a cada execução do efeito e vira `true` no cleanup, que roda ANTES do efeito da
  // dependência nova — nenhum `set*` de uma resposta tardia de um id velho passa pela guarda. Por
  // isso ela cobre também o `finally`: um `carregando` que baixasse sem a guarda mostraria a tela
  // de 2 como "carregada" com os dados de 1 ainda por cima. `erro` é zerado no INÍCIO do efeito,
  // não só o array de dados: sem isto, trocar de um id com falha para um id que carrega bem
  // deixaria erro e sucesso e o banner ficaria colado mesmo depois do sucesso.
  useEffect(() => {
    if (!idValido) return
    let cancelado = false
    setCarregandoComponente(true)
    setErroComponente(null)
    obterComponente(componenteId)
      .then((c) => { if (!cancelado) setComponente(c) })
      .catch((e) => { if (!cancelado) setErroComponente(e) })
      .finally(() => { if (!cancelado) setCarregandoComponente(false) })
    return () => { cancelado = true }
  }, [componenteId, idValido])

  useEffect(() => {
    if (!idValido) return
    let cancelado = false
    setCarregandoFilhos(true)
    setErroFilhos(null)
    listarFilhosPadrao(componenteId)
      .then((f) => { if (!cancelado) setFilhos(f) })
      .catch((e) => { if (!cancelado) setErroFilhos(e) })
      .finally(() => { if (!cancelado) setCarregandoFilhos(false) })
    return () => { cancelado = true }
  }, [componenteId, idValido])

  useEffect(() => {
    if (!idValido) return
    let cancelado = false
    setCarregandoMateriais(true)
    setErroMateriais(null)
    listarMateriaisPadrao(componenteId)
      .then((m) => { if (!cancelado) setMateriais(m) })
      .catch((e) => { if (!cancelado) setErroMateriais(e) })
      .finally(() => { if (!cancelado) setCarregandoMateriais(false) })
    return () => { cancelado = true }
  }, [componenteId, idValido])

  useEffect(() => {
    if (!idValido) return
    let cancelado = false
    setCarregandoRoteiro(true)
    setErroRoteiro(null)
    listarRoteiroPadrao(componenteId)
      .then((r) => { if (!cancelado) setRoteiro(r) })
      .catch((e) => { if (!cancelado) setErroRoteiro(e) })
      .finally(() => { if (!cancelado) setCarregandoRoteiro(false) })
    return () => { cancelado = true }
  }, [componenteId, idValido])

  // Catálogos das duas seções com `<select>` nativo. Só buscados para quem pode escrever — quem só
  // lê nunca vê o formulário que os consome, e pedir os dois catálogos de graça seria uma
  // requisição a mais por sessão sem uso nenhum. Falha aqui é engolida de propósito: ela só
  // impediria popular UMA lista de opções para adicionar linha nova, e não é motivo para um banner
  // de erro na tela — a receita já carregada (as 3 seções acima) não depende destes dois catálogos.
  // Mesma guarda de cancelamento dos quatro efeitos acima, pelo mesmo motivo (troca de `:id`).
  useEffect(() => {
    if (!idValido || !podeEscrever) return
    let cancelado = false
    listarMateriais(false).then((m) => { if (!cancelado) setMateriaisCadastro(m) }).catch(() => {})
    return () => { cancelado = true }
  }, [idValido, podeEscrever])

  useEffect(() => {
    if (!idValido || !podeEscrever) return
    let cancelado = false
    listarSetores(false).then((s) => { if (!cancelado) setSetoresCadastro(s) }).catch(() => {})
    return () => { cancelado = true }
  }, [idValido, podeEscrever])

  function aoAdicionarFilho() {
    const quantidade = Number(quantidadeFilho)
    if (!filhoSelecionado || !(quantidade > 0)) return
    const linha: FilhoPadraoDto = {
      id: proximoIdFilhoRef.current--,
      componenteFilhoId: filhoSelecionado.id,
      codigo: filhoSelecionado.codigo,
      descricao: filhoSelecionado.descricao,
      quantidadePadrao: quantidade,
    }
    setFilhos((atual) => [...atual, linha])
    setFilhosSujo(true)
    setFilhoSelecionado(null)
    setQuantidadeFilho('')
  }

  function removerFilho(id: number) {
    setFilhos((atual) => atual.filter((f) => f.id !== id))
    setFilhosSujo(true)
  }

  async function salvarFilhos() {
    setSalvandoFilhos(true)
    setErroSalvarFilhos(null)
    try {
      // A receita inteira, não só a linha nova: não há PATCH por linha, o POST substitui.
      const linhas: LinhaDeFilho[] = filhos.map((f) => ({
        componenteFilhoId: f.componenteFilhoId,
        quantidadePadrao: f.quantidadePadrao,
      }))
      const atualizado = await salvarFilhosPadrao(componenteId, linhas)
      setFilhos(atualizado)
      setFilhosSujo(false)
    } catch (e) {
      setErroSalvarFilhos(e)
    } finally {
      setSalvandoFilhos(false)
    }
  }

  function aoAdicionarMaterial() {
    const quantidade = Number(quantidadeMaterial)
    const material = materiaisCadastro.find((m) => m.id === materialSelecionadoId)
    if (!material || !(quantidade > 0)) return
    const linha: MaterialPadraoDto = {
      id: proximoIdMaterialRef.current--,
      materialId: material.id,
      codigo: material.codigo,
      descricao: material.descricao,
      unidadeMedida: material.unidadeMedida,
      quantidadePadrao: quantidade,
    }
    setMateriais((atual) => [...atual, linha])
    setMateriaisSujo(true)
    setMaterialSelecionadoId(null)
    setQuantidadeMaterial('')
  }

  function removerMaterial(id: number) {
    setMateriais((atual) => atual.filter((m) => m.id !== id))
    setMateriaisSujo(true)
  }

  async function salvarMateriais() {
    setSalvandoMateriais(true)
    setErroSalvarMateriais(null)
    try {
      const linhas: LinhaDeMaterial[] = materiais.map((m) => ({
        materialId: m.materialId,
        quantidadePadrao: m.quantidadePadrao,
      }))
      const atualizado = await salvarMateriaisPadrao(componenteId, linhas)
      setMateriais(atualizado)
      setMateriaisSujo(false)
    } catch (e) {
      setErroSalvarMateriais(e)
    } finally {
      setSalvandoMateriais(false)
    }
  }

  function aoAdicionarPassoRoteiro() {
    const setor = setoresCadastro.find((s) => s.id === setorSelecionadoId)
    if (!setor) return
    const linha: RoteiroPadraoDto = {
      id: proximoIdRoteiroRef.current--,
      setorId: setor.id,
      nome: setor.nome,
      // Campo exigido pelo tipo (o GET real sempre traz `ordem`), mas NÃO é o que a tela exibe —
      // ver o comentário de `roteiro.map((r, i) => …)` mais abaixo, e a DECISÃO no topo do
      // componente. Valor aqui é irrelevante para a exibição; existe só para o objeto tipar.
      ordem: 0,
    }
    setRoteiro((atual) => [...atual, linha])
    setRoteiroSujo(true)
    setSetorSelecionadoId(null)
  }

  function removerPassoRoteiro(id: number) {
    setRoteiro((atual) => atual.filter((r) => r.id !== id))
    setRoteiroSujo(true)
  }

  async function salvarRoteiro() {
    setSalvandoRoteiro(true)
    setErroSalvarRoteiro(null)
    try {
      // Sem `ordem` no corpo: quem numera é o servidor, pela posição (LinhaDeRoteiro).
      const linhas: LinhaDeRoteiro[] = roteiro.map((r) => ({ setorId: r.setorId }))
      const atualizado = await salvarRoteiroPadrao(componenteId, linhas)
      setRoteiro(atualizado)
      setRoteiroSujo(false)
    } catch (e) {
      setErroSalvarRoteiro(e)
    } finally {
      setSalvandoRoteiro(false)
    }
  }

  // `id` inválido (ex.: `/componentes/abc` → `NaN`) não tenta nenhuma das 4 buscas — não há
  // componente nenhum para buscar. Único banner, sem tratamento por seção: as 4 chamadas
  // dependeriam de um id que não existe.
  if (!idValido) {
    return (
      <Pagina titulo="Componente">
        <BannerDeErro mensagem="Este componente não existe." />
      </Pagina>
    )
  }

  return (
    // Título FIXO desde o primeiro frame (decisão 3): a 4ª busca (o componente, para o
    // cabeçalho) pode estar em voo ou ter falhado, e `Pagina` exige `titulo: string` — não dá
    // para esperar o componente chegar para decidir o título.
    <Pagina titulo="Componente">
      <div className="flex flex-col gap-2 rounded-lg border border-borda bg-superficie p-4">
        {carregandoComponente && <EstadoCarregando />}
        {!carregandoComponente && erroComponente !== null && (
          <BannerDeErro mensagem={mensagemDeErro(erroComponente, 'Não foi possível carregar o componente.')} />
        )}
        {!carregandoComponente && erroComponente === null && componente && (
          <p className="text-lg text-tinta">
            <span className="font-mono font-semibold">{componente.codigo}</span>
            {' — '}
            {componente.descricao}
          </p>
        )}
      </div>

      <Secao
        idTitulo="titulo-filhos"
        titulo="Componentes filhos"
        carregando={carregandoFilhos}
        erro={erroFilhos}
        erroFallback="Não foi possível carregar os componentes filhos."
        quantidade={filhos.length}
        vazio="Nenhum componente filho na receita."
        rodape={podeEscrever && (
          <div className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
            <div className="grid gap-3 sm:grid-cols-[1fr_auto_auto] sm:items-end">
              <SeletorComBusca
                rotulo="Componente filho"
                valorSelecionado={filhoSelecionado}
                aoSelecionar={setFilhoSelecionado}
              />
              <Campo rotulo="Quantidade">
                {(idDoCampo) => (
                  <input
                    id={idDoCampo}
                    type="number"
                    step="any"
                    value={quantidadeFilho}
                    onChange={(e) => setQuantidadeFilho(e.target.value)}
                    className={CLASSES_DE_CONTROLE}
                  />
                )}
              </Campo>
              <Botao
                variante="secundario"
                onClick={aoAdicionarFilho}
                disabled={!filhoSelecionado || !(Number(quantidadeFilho) > 0)}
              >
                Adicionar
              </Botao>
            </div>
            <BannerDeErro
              mensagem={erroSalvarFilhos == null ? null : mensagemDeErro(erroSalvarFilhos, 'Não foi possível salvar os componentes filhos.')}
            />
            <Botao
              onClick={salvarFilhos}
              disabled={!filhosSujo || salvandoFilhos}
              carregando={salvandoFilhos}
              rotuloCarregando="Salvando…"
              className="self-start"
            >
              Salvar componentes filhos
            </Botao>
          </div>
        )}
      >
        <ListaDeCadastro rotulo="Componentes filhos">
          {filhos.map((f) => (
            <ItemDeCadastro
              key={f.id}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => removerFilho(f.id)}>
                  {`Remover ${f.codigo}`}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{f.codigo}</span>
              {' — '}
              {f.descricao}
              {' '}
              <Pilula>{f.quantidadePadrao}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      </Secao>

      <Secao
        idTitulo="titulo-materiais"
        titulo="Materiais"
        carregando={carregandoMateriais}
        erro={erroMateriais}
        erroFallback="Não foi possível carregar os materiais."
        quantidade={materiais.length}
        vazio="Nenhum material na receita."
        rodape={podeEscrever && (
          <div className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
            <div className="grid gap-3 sm:grid-cols-[1fr_auto_auto] sm:items-end">
              <Campo rotulo="Material">
                {(idDoCampo) => (
                  <select
                    id={idDoCampo}
                    value={materialSelecionadoId ?? ''}
                    onChange={(e) => setMaterialSelecionadoId(e.target.value ? Number(e.target.value) : null)}
                    className={CLASSES_DE_CONTROLE}
                  >
                    <option value="">Selecione um material</option>
                    {materiaisCadastro.map((m) => (
                      <option key={m.id} value={m.id}>{m.codigo} — {m.descricao}</option>
                    ))}
                  </select>
                )}
              </Campo>
              <Campo rotulo="Quantidade">
                {(idDoCampo) => (
                  <input
                    id={idDoCampo}
                    type="number"
                    step="any"
                    value={quantidadeMaterial}
                    onChange={(e) => setQuantidadeMaterial(e.target.value)}
                    className={CLASSES_DE_CONTROLE}
                  />
                )}
              </Campo>
              <Botao
                variante="secundario"
                onClick={aoAdicionarMaterial}
                disabled={materialSelecionadoId === null || !(Number(quantidadeMaterial) > 0)}
              >
                Adicionar
              </Botao>
            </div>
            <BannerDeErro
              mensagem={erroSalvarMateriais == null ? null : mensagemDeErro(erroSalvarMateriais, 'Não foi possível salvar os materiais.')}
            />
            <Botao
              onClick={salvarMateriais}
              disabled={!materiaisSujo || salvandoMateriais}
              carregando={salvandoMateriais}
              rotuloCarregando="Salvando…"
              className="self-start"
            >
              Salvar materiais
            </Botao>
          </div>
        )}
      >
        <ListaDeCadastro rotulo="Materiais">
          {materiais.map((m) => (
            <ItemDeCadastro
              key={m.id}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => removerMaterial(m.id)}>
                  {`Remover ${m.codigo}`}
                </Botao>
              )}
            >
              <span className="font-mono font-semibold">{m.codigo}</span>
              {' — '}
              {m.descricao}
              {' '}
              <Pilula>{m.quantidadePadrao} {m.unidadeMedida}</Pilula>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      </Secao>

      <Secao
        idTitulo="titulo-roteiro"
        titulo="Roteiro"
        carregando={carregandoRoteiro}
        erro={erroRoteiro}
        erroFallback="Não foi possível carregar o roteiro."
        quantidade={roteiro.length}
        vazio="Nenhum setor no roteiro."
        rodape={podeEscrever && (
          <div className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
            <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
              <Campo rotulo="Setor">
                {(idDoCampo) => (
                  <select
                    id={idDoCampo}
                    value={setorSelecionadoId ?? ''}
                    onChange={(e) => setSetorSelecionadoId(e.target.value ? Number(e.target.value) : null)}
                    className={CLASSES_DE_CONTROLE}
                  >
                    <option value="">Selecione um setor</option>
                    {setoresCadastro.map((s) => <option key={s.id} value={s.id}>{s.nome}</option>)}
                  </select>
                )}
              </Campo>
              <Botao
                variante="secundario"
                onClick={aoAdicionarPassoRoteiro}
                disabled={setorSelecionadoId === null}
              >
                Adicionar passo
              </Botao>
            </div>
            <BannerDeErro
              mensagem={erroSalvarRoteiro == null ? null : mensagemDeErro(erroSalvarRoteiro, 'Não foi possível salvar o roteiro.')}
            />
            <Botao
              onClick={salvarRoteiro}
              disabled={!roteiroSujo || salvandoRoteiro}
              carregando={salvandoRoteiro}
              rotuloCarregando="Salvando…"
              className="self-start"
            >
              Salvar roteiro
            </Botao>
          </div>
        )}
      >
        {/* Ordem da API preservada (já vem ordenada por `ordem`) e SEM deduplicar por `setorId`:
            o roteiro pode repetir o mesmo setor — é retorno ao setor, não duplicata. A `key` é
            `r.id`, o id da própria linha de `ComponenteRoteiroPadrao` (ou o id local negativo de
            uma linha ainda não salva), e nunca `r.setorId`, que colidiria entre a primeira e a
            segunda passagem pelo mesmo setor.

            Item 7 da review (Tasks 10-12) — DECISÃO DO USUÁRIO: o número exibido é `i + 1`, a
            POSIÇÃO no array, não `r.ordem`. `r.ordem` é o rótulo que o SERVIDOR atribuiu na
            última leitura/gravação — correto no momento em que chegou, mas MENTIROSO depois de
            qualquer edição local: remover o passo do meio e adicionar um novo faz `r.ordem`
            pular um número, repetir outro, e produzir DOIS botões "Remover N. Nome" com o MESMO
            nome acessível (`getByRole` rejeita — era o defeito que a Task 11 mandou evitar ao
            exigir o código no rótulo). Numerar pela posição do array elimina os três sintomas de
            uma vez, porque a posição sempre é densa e sequencial por construção. A intenção
            original do brief continua valendo: `ordem` nunca é MANDADA ao servidor
            (`LinhaDeRoteiro` só carrega `setorId`) — só deixou de ser o que a TELA mostra. */}
        <ListaDeCadastro rotulo="Roteiro">
          {roteiro.map((r, i) => (
            <ItemDeCadastro
              key={r.id}
              acao={podeEscrever && (
                <Botao variante="secundario" onClick={() => removerPassoRoteiro(r.id)}>
                  {`Remover ${i + 1}. ${r.nome}`}
                </Botao>
              )}
            >
              {/* `nome` num `<span>` próprio, e não texto solto ao lado da ordem. MEDIDO ao
                  escrever a tela: com `{i + 1}. {r.nome}` solto, nenhum elemento tem
                  "Corte" como texto próprio — o mais interno vira "1. Corte" —, e
                  `findAllByText('Corte')` acha ZERO. O `<span>` é o que dá ao nome do setor um
                  elemento só dele, e é dele que depende o teste do retorno ao setor. */}
              <span className="text-tinta-fraca">{i + 1}.</span>
              {' '}
              <span>{r.nome}</span>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      </Secao>
    </Pagina>
  )
}
