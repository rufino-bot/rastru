import { useEffect, useState, type FormEvent } from 'react'
import type { BaixaDto, PreviaDaBaixaDto } from '../api/baixas'
import { mensagemDeErro } from '../api/erros'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { useEscritaDoPainel } from '../expedicao/escritaDoPainel'
import { formatarQuantidade } from '../execucao/formatacao'

interface Props {
  /** O nome da ação, que é o do formulário ("Descartar tudo"). */
  titulo: string
  /** De que se descarta tudo: entra na frase "Depois disto, o ... fica concluído". */
  escopo: 'Pedido' | 'Agrupamento'
  /** A prévia do servidor (C14): o painel a pede ao abrir, e o enviar só destrava depois que ela voltou sem recusa. */
  prever: () => Promise<PreviaDaBaixaDto>
  descartar: (observacao?: string) => Promise<BaixaDto>
  /** O servidor mudou: no sucesso e no 409, quem abriu recarrega. */
  aoConcluir: () => void
  aoFechar: () => void
}

/** A coluna de observação é `NVARCHAR(500)`; o servidor recusa acima disso (C19). */
const TAMANHO_MAXIMO_DA_OBSERVACAO = 500

interface Resumo {
  nos: number
  unidades: number
  posicoes: number
}

/**
 * Conta o que a prévia diz: nós distintos, unidades e posições distintas (posição, Setor e passo). A linha da prévia
 * traz o nó mas não a Peça do topo, e a baixa de tudo leva também os descendentes; por isso a contagem é de nós, e
 * não de Peças.
 */
function resumir(previa: PreviaDaBaixaDto): Resumo {
  const nos = new Set(previa.linhas.map((l) => l.estruturaItemId))
  const posicoes = new Set(previa.linhas.map((l) => `${l.origemPosicao}|${l.origemSetorId ?? ''}|${l.origemOrdem ?? ''}`))
  const unidades = Math.round(previa.linhas.reduce((total, l) => total + l.quantidade, 0) * 10_000) / 10_000
  return { nos: nos.size, unidades, posicoes: posicoes.size }
}

function fraseDoResumo({ nos, unidades, posicoes }: Resumo): string {
  return `${nos} ${nos === 1 ? 'nó' : 'nós'} com algo em produção, `
    + `${formatarQuantidade(unidades)} ${unidades === 1 ? 'unidade' : 'unidades'} `
    + `em ${posicoes} ${posicoes === 1 ? 'posição' : 'posições'}`
}

/**
 * "Descartar tudo" do Pedido e do Agrupamento (D7, D8, D11 da spec da Fase 5B-1): leva tudo o que está em produção,
 * sem escolha, e conclui o escopo. A prévia é a do servidor (C14), pedida ao abrir: ela traz também a recusa
 * `LiberacaoEmAberto` (C15) antes de a pessoa tentar. O duplo toque é segurado pelo `ref` do `useEscritaDoPainel`.
 */
export function PainelDeDescarteTotal({ titulo, escopo, prever, descartar, aoConcluir, aoFechar }: Props) {
  const [previa, setPrevia] = useState<PreviaDaBaixaDto | null>(null)
  const [erroDaPrevia, setErroDaPrevia] = useState<unknown>(null)
  const [observacao, setObservacao] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível descartar.')

  // Uma consulta por abertura do painel; `prever` é lido só na montagem, e a resposta de um painel já desmontado
  // não escreve em estado.
  useEffect(() => {
    let vivo = true
    prever().then(
      (p) => { if (vivo) setPrevia(p) },
      (e: unknown) => { if (vivo) setErroDaPrevia(e) },
    )
    return () => { vivo = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const podeEnviar = previa !== null && erroDaPrevia === null && !enviando

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!podeEnviar) return
    const texto = observacao.trim()
    escrever(() => descartar(texto === '' ? undefined : texto))
  }

  return (
    <PainelDeEscrita titulo={titulo} aoEnviar={enviar} aoFechar={aoFechar} enviando={enviando}>
      {previa === null && erroDaPrevia === null && <EstadoCarregando />}
      {previa !== null && <p className="text-sm font-medium text-tinta">{fraseDoResumo(resumir(previa))}</p>}
      <BannerDeErro
        mensagem={erroDaPrevia === null ? null : mensagemDeErro(erroDaPrevia, 'Não foi possível calcular o que o descarte faz.')}
      />
      <p className="text-sm text-tinta-fraca">O que já foi expedido e o que já foi montado ficam onde estão.</p>
      <p className="text-sm text-tinta-fraca">{`Depois disto, o ${escopo} fica concluído.`}</p>

      <Campo rotulo="Observação (opcional)">
        {(id) => (
          <textarea
            id={id}
            value={observacao}
            onChange={(e) => setObservacao(e.target.value)}
            maxLength={TAMANHO_MAXIMO_DA_OBSERVACAO}
            rows={2}
            disabled={enviando}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>

      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        variante="perigo"
        carregando={enviando}
        rotuloCarregando="Descartando…"
        disabled={!podeEnviar}
        className="self-start"
      >
        Descartar tudo
      </Botao>
    </PainelDeEscrita>
  )
}
