import { useEffect, useRef, useState } from 'react'
import { obterRoteiroDoNo, substituirRoteiroDoNo, ehConflito, type RoteiroDoNoDto } from '../api/execucao'
import { listarSetores, type SetorDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Botao } from '../components/Botao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { Pilula } from '../components/Pilula'

/** Um passo na tela. `chave` é local: o mesmo Setor pode estar duas vezes (regra 21). */
interface PassoNaTela {
  chave: number
  setorId: number
  nome: string
  alcancado: boolean
}

function paraTela(r: RoteiroDoNoDto, proximaChave: () => number): PassoNaTela[] {
  return [...r.passos]
    .sort((a, b) => a.ordem - b.ordem)
    .map((p) => ({ chave: proximaChave(), setorId: p.setorId, nome: p.nome, alcancado: p.alcancado }))
}

interface Props {
  noId: number
  podeEditar: boolean
  /** O Roteiro mudou no servidor — a árvore recarrega (o "Sem Roteiro" pode ter mudado). */
  aoSalvar: () => void
}

/**
 * O Roteiro de UM nó (spec da Fase 3 §4.6 e §6.3), editável pelo PCP. Mesmo molde do roteiro padrão
 * da `ComponenteDetalhePage`: adicionar no fim, remover, e salvar a lista inteira — o número de cada
 * passo é a POSIÇÃO na lista, quem o grava é o servidor.
 *
 * Passo **alcançado** (já aparece no livro do nó) é histórico: não tem "Remover", e nenhum passo
 * novo entra antes dele. Como só se adiciona no fim e os alcançados são sempre o começo do Roteiro
 * (nenhum movimento pula passo), a tela não consegue montar uma lista que o servidor recusaria por
 * `PassoJaAlcancado` — se recusar mesmo assim (outra pessoa andou com o nó enquanto a tela estava
 * aberta), a frase aparece e o Roteiro recarrega.
 *
 * Setor inativo não entra (`RoteiroInvalido`): o seletor só oferece os ativos. O que já está num
 * Roteiro e foi inativado depois continua nele (spec §4.6).
 */
export function EditorDeRoteiroDoNo({ noId, podeEditar, aoSalvar }: Props) {
  const chaveRef = useRef(0)
  const proximaChave = () => ++chaveRef.current

  const [passos, setPassos] = useState<PassoNaTela[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [sujo, setSujo] = useState(false)
  const [setores, setSetores] = useState<SetorDto[]>([])
  const [setorEscolhido, setSetorEscolhido] = useState<number | null>(null)
  const [salvando, setSalvando] = useState(false)
  const [erroAoSalvar, setErroAoSalvar] = useState<string | null>(null)
  const [erroDosSetores, setErroDosSetores] = useState<string | null>(null)
  // Fix pass (review Important 1): mesmo padrão de `FormularioDeQuantidade` — o `ref` fecha a janela
  // que sobra entre dois toques no mesmo quadro, antes de o React redesenhar o botão desabilitado.
  // A guarda de `ref` vem da Global Constraint do plano (toda escrita da execução tem defesa de
  // toque duplo), não da spec §8.1, que pede só o botão desabilitado. `salvando` sozinho já
  // desabilita "Salvar roteiro" (via `carregando` do `Botao`), mas não "Remover"/"Adicionar passo"
  // — o `ref` guarda a função inteira, os dois `disabled` abaixo guardam a interação.
  const enviandoRef = useRef(false)

  // Recebe o id como argumento (molde de `PedidoDetalhePage`): o exhaustive-deps cobraria
  // `carregar` como dependência do efeito se o corpo fechasse sobre `noId`.
  async function carregar(id: number) {
    setErroDeCarga(null)
    try {
      setPassos(paraTela(await obterRoteiroDoNo(id), proximaChave))
      setSujo(false)
    } catch (e) {
      setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar o roteiro.'))
    }
  }

  useEffect(() => { carregar(noId) }, [noId])

  useEffect(() => {
    if (!podeEditar) return
    let cancelado = false
    listarSetores(false)
      .then((s) => { if (!cancelado) setSetores(s) })
      .catch((e) => { if (!cancelado) setErroDosSetores(mensagemDeErro(e, 'Não foi possível carregar os setores.')) })
    return () => { cancelado = true }
  }, [podeEditar])

  function adicionar() {
    const setor = setores.find((s) => s.id === setorEscolhido)
    if (!setor || passos === null) return
    setPassos([...passos, { chave: proximaChave(), setorId: setor.id, nome: setor.nome, alcancado: false }])
    setSetorEscolhido(null)
    setSujo(true)
  }

  function remover(chave: number) {
    if (passos === null) return
    setPassos(passos.filter((p) => p.chave !== chave))
    setSujo(true)
  }

  async function salvar() {
    if (passos === null || enviandoRef.current) return
    enviandoRef.current = true
    setSalvando(true)
    setErroAoSalvar(null)
    try {
      setPassos(paraTela(await substituirRoteiroDoNo(noId, passos.map((p) => p.setorId)), proximaChave))
      setSujo(false)
      aoSalvar()
    } catch (e) {
      setErroAoSalvar(mensagemDeErro(e, 'Não foi possível salvar o roteiro.'))
      if (ehConflito(e)) {
        // 409 é escrita obsoleta (spec §8.3): não só o Roteiro recarrega — a árvore e as posições
        // também ficaram velhas (o `PassoJaAlcancado` típico é outra pessoa ter andado com o nó
        // enquanto a tela estava aberta), então o mesmo `aoSalvar` do caminho de sucesso dispara
        // aqui também (Important 3 do fix pass: o irmão `HistoricoDoNo` já faz isso no estorno).
        await carregar(noId)
        aoSalvar()
      }
    } finally {
      enviandoRef.current = false
      setSalvando(false)
    }
  }

  return (
    <section aria-label="Roteiro do nó" className="flex flex-col gap-3">
      <h3 className="font-medium text-tinta">Roteiro</h3>
      <BannerDeErro mensagem={erroDeCarga} />
      {passos === null && erroDeCarga === null && <EstadoCarregando />}
      {passos !== null && passos.length === 0 && (
        <p className="text-sm text-tinta-fraca">
          {podeEditar
            ? 'Este nó não tem Roteiro e não pode ser iniciado. Adicione os Setores na ordem em que ele passa por eles.'
            : 'Este nó não tem Roteiro e não pode ser iniciado. Quem cadastra o Roteiro é o PCP.'}
        </p>
      )}
      {passos !== null && passos.length > 0 && (
        <ol aria-label="Passos do roteiro" className="flex flex-col gap-2">
          {passos.map((p, i) => (
            <li key={p.chave} className="flex flex-wrap items-center justify-between gap-2 text-sm text-tinta">
              <span className="flex items-center gap-2">
                {`${i + 1}. ${p.nome}`}
                {p.alcancado && <Pilula>alcançado</Pilula>}
              </span>
              {podeEditar && !p.alcancado && (
                <Botao
                  variante="secundario"
                  aria-label={`Remover o passo ${i + 1} (${p.nome})`}
                  onClick={() => remover(p.chave)}
                  disabled={salvando}
                >
                  Remover
                </Botao>
              )}
            </li>
          ))}
        </ol>
      )}
      {podeEditar && passos !== null && (
        <div className="flex flex-col gap-3">
          <div className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
            <Campo rotulo="Setor">
              {(id) => (
                <select
                  id={id}
                  value={setorEscolhido ?? ''}
                  onChange={(e) => setSetorEscolhido(e.target.value ? Number(e.target.value) : null)}
                  className={CLASSES_DE_CONTROLE}
                >
                  <option value="">Selecione um setor</option>
                  {setores.map((s) => <option key={s.id} value={s.id}>{s.nome}</option>)}
                </select>
              )}
            </Campo>
            <Botao variante="secundario" onClick={adicionar} disabled={setorEscolhido === null || salvando}>
              Adicionar passo
            </Botao>
          </div>
          <BannerDeErro mensagem={erroDosSetores} />
          <BannerDeErro mensagem={erroAoSalvar} />
          <Botao
            onClick={salvar}
            disabled={!sujo}
            carregando={salvando}
            rotuloCarregando="Salvando…"
            className="self-start"
          >
            Salvar roteiro
          </Botao>
        </div>
      )}
    </section>
  )
}
