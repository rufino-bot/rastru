import { useEffect, useState, type ReactNode } from 'react'
import { useParams } from 'react-router-dom'
import { obterComponente, type ComponenteDto } from '../api/cadastros'
import {
  listarFilhosPadrao, listarMateriaisPadrao, listarRoteiroPadrao,
  type FilhoPadraoDto, type MaterialPadraoDto, type RoteiroPadraoDto,
} from '../api/receitaPadrao'
import { mensagemDeErro } from '../api/erros'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'

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
}

/**
 * `aria-labelledby` liga a `<section>` ao `id` do próprio `<h2>`, virando landmark `region` com
 * nome acessível — decisão 2 do brief da Task 10. Sem isso, com as 3 seções carregando (ou
 * falhando) ao mesmo tempo, `getByRole('status')`/`findByRole('alert')` global lançaria por
 * múltiplos matches; com o landmark, o teste escopa com `within()` e prova QUAL seção está em
 * qual estado — uma contagem global não distingue isso.
 */
function Secao({ idTitulo, titulo, carregando, erro, erroFallback, quantidade, vazio, children }: PropsDaSecao) {
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
    </section>
  )
}

/**
 * Leitura da receita padrão de um Componente (Task 10 da Fase 1C). A Task 11 acrescenta rascunho
 * local e Salvar por seção nesta mesma tela — aqui é só leitura, de propósito (o corte deixa um
 * revisor aprovar a leitura e reprovar a escrita separadamente).
 *
 * 4 buscas independentes (o componente, para o cabeçalho, e as 3 receitas), cada uma com o
 * PRÓPRIO `carregando`/`erro`: um `useState`/`useEffect` por busca, não um hook genérico
 * compartilhado — é isto que faz "uma falha em materiais não apaga a lista de filhos que
 * carregou bem" (decisão 2) ser verdade por construção, não por disciplina.
 */
export function ComponenteDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const componenteId = Number(id)
  const idValido = !Number.isNaN(componenteId)

  const [componente, setComponente] = useState<ComponenteDto | null>(null)
  const [carregandoComponente, setCarregandoComponente] = useState(true)
  const [erroComponente, setErroComponente] = useState<unknown>(null)

  const [filhos, setFilhos] = useState<FilhoPadraoDto[]>([])
  const [carregandoFilhos, setCarregandoFilhos] = useState(true)
  const [erroFilhos, setErroFilhos] = useState<unknown>(null)

  const [materiais, setMateriais] = useState<MaterialPadraoDto[]>([])
  const [carregandoMateriais, setCarregandoMateriais] = useState(true)
  const [erroMateriais, setErroMateriais] = useState<unknown>(null)

  const [roteiro, setRoteiro] = useState<RoteiroPadraoDto[]>([])
  const [carregandoRoteiro, setCarregandoRoteiro] = useState(true)
  const [erroRoteiro, setErroRoteiro] = useState<unknown>(null)

  useEffect(() => {
    if (!idValido) return
    setCarregandoComponente(true)
    obterComponente(componenteId)
      .then(setComponente)
      .catch(setErroComponente)
      .finally(() => setCarregandoComponente(false))
  }, [componenteId, idValido])

  useEffect(() => {
    if (!idValido) return
    setCarregandoFilhos(true)
    listarFilhosPadrao(componenteId)
      .then(setFilhos)
      .catch(setErroFilhos)
      .finally(() => setCarregandoFilhos(false))
  }, [componenteId, idValido])

  useEffect(() => {
    if (!idValido) return
    setCarregandoMateriais(true)
    listarMateriaisPadrao(componenteId)
      .then(setMateriais)
      .catch(setErroMateriais)
      .finally(() => setCarregandoMateriais(false))
  }, [componenteId, idValido])

  useEffect(() => {
    if (!idValido) return
    setCarregandoRoteiro(true)
    listarRoteiroPadrao(componenteId)
      .then(setRoteiro)
      .catch(setErroRoteiro)
      .finally(() => setCarregandoRoteiro(false))
  }, [componenteId, idValido])

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
      >
        <ListaDeCadastro rotulo="Componentes filhos">
          {filhos.map((f) => (
            <ItemDeCadastro key={f.id}>
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
      >
        <ListaDeCadastro rotulo="Materiais">
          {materiais.map((m) => (
            <ItemDeCadastro key={m.id}>
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
      >
        {/* Ordem da API preservada (já vem ordenada por `ordem`) e SEM deduplicar por `setorId`:
            o roteiro pode repetir o mesmo setor — é retorno ao setor, não duplicata. A `key` é
            `r.id`, o id da própria linha de `ComponenteRoteiroPadrao`, e nunca `r.setorId`, que
            colidiria entre a primeira e a segunda passagem pelo mesmo setor. */}
        <ListaDeCadastro rotulo="Roteiro">
          {roteiro.map((r) => (
            <ItemDeCadastro key={r.id}>
              {/* `nome` num `<span>` próprio, e não texto solto ao lado da ordem. MEDIDO ao
                  escrever a tela: com `{r.ordem}. {r.nome}` solto, nenhum elemento tem
                  "Corte" como texto próprio — o mais interno vira "1. Corte" —, e
                  `findAllByText('Corte')` acha ZERO. O `<span>` é o que dá ao nome do setor um
                  elemento só dele, e é dele que depende o teste do retorno ao setor. */}
              <span className="text-tinta-fraca">{r.ordem}.</span>
              {' '}
              <span>{r.nome}</span>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      </Secao>
    </Pagina>
  )
}
