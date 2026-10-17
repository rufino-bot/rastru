import { useRef, useState, type FormEvent } from 'react'
import { separar, type LinhaDeMaterial, type MaterialPendente, type PendenciaDeSeparacaoDto } from '../api/separacao'
import { ehConflito } from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'

/** O que um campo diz: não vai no corpo (vazio ou zero), vai com este valor, ou está errado. */
type LeituraDoCampo = { tipo: 'nao-vai' } | { tipo: 'vai'; valor: number } | { tipo: 'erro'; erro: string }

// Zero escrito de qualquer jeito ("0", "0,0", "00.00"): é o Separador dizendo "deste não levei", não erro.
const ZERO = /^0+([.,]0+)?$/

/**
 * Lê um campo sem máximo: acima do planejado é aceito (D9 da spec da Fase 4 — o parafuso que caiu), então
 * `lerQuantidade` recebe infinito e só recusa o formato.
 */
function lerCampo(texto: string): LeituraDoCampo {
  const aparado = texto.trim()
  if (aparado === '' || ZERO.test(aparado)) return { tipo: 'nao-vai' }
  const leitura = lerQuantidade(aparado, Number.POSITIVE_INFINITY)
  return leitura.erro === null ? { tipo: 'vai', valor: leitura.valor } : { tipo: 'erro', erro: leitura.erro }
}

/** O texto embaixo do campo: o erro, o aviso de "acima" (neutro) ou quanto falta. */
function dicaDo(m: MaterialPendente, leitura: LeituraDoCampo): string {
  if (leitura.tipo === 'erro') return leitura.erro
  if (leitura.tipo === 'vai' && leitura.valor > m.pendencia) {
    return `acima do planejado em ${formatarQuantidade(leitura.valor - m.pendencia)} ${m.unidadeMedida}`
  }
  return `Falta ${formatarQuantidade(m.pendencia)} ${m.unidadeMedida}`
}

interface Props {
  pendencia: PendenciaDeSeparacaoDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega a lista. */
  aoSalvar: () => void
}

/**
 * "Separar material" de UM nó da lista do Separador (spec da Fase 4, seção 6.1): um campo por material
 * pendente, preenchido com a pendência (D17 — o caso comum é um toque só), e um `POST` só com o que não
 * ficou vazio nem zero, tudo ou nada no servidor.
 *
 * Passar da pendência não é erro: o campo avisa "acima do planejado em …", como texto neutro, e envia
 * assim mesmo (D9). O erro de escrita mora aqui dentro. No 409 (o material saiu do plano com o painel
 * aberto, `MaterialForaDoPlanejado`) a frase do servidor fica e a lista recarrega; o campo do material
 * que saiu some, porque os campos seguem a `pendencia` que chega, e o que já foi digitado nos outros fica.
 *
 * O `enviandoRef` guarda a função inteira contra o segundo toque no mesmo quadro, antes de o React
 * redesenhar o botão desabilitado, como no `FormularioDeQuantidade`; o Enter num campo também chega aqui.
 */
export function PainelDeSeparacao({ pendencia, aoFechar, aoSalvar }: Props) {
  // Só o que o Separador mexeu; o campo intocado mostra a pendência ATUAL do material.
  const [textos, setTextos] = useState<Record<number, string>>({})
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const enviandoRef = useRef(false)

  const campos = pendencia.materiais.map((m) => {
    const texto = textos[m.materialId] ?? quantidadeParaCampo(m.pendencia)
    return { material: m, texto, leitura: lerCampo(texto) }
  })
  const algumErro = campos.some((c) => c.leitura.tipo === 'erro')
  const linhas: LinhaDeMaterial[] = campos.flatMap((c) =>
    c.leitura.tipo === 'vai' ? [{ materialId: c.material.materialId, quantidade: c.leitura.valor }] : [])

  async function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current || algumErro || linhas.length === 0) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await separar(pendencia.no.id, linhas)
      aoSalvar()
      aoFechar()
    } catch (falha) {
      setErro(mensagemDeErro(falha, 'Não foi possível registrar a separação.'))
      if (ehConflito(falha)) aoSalvar()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return (
    <PainelDeEscrita
      titulo="Separar material"
      subtitulo={rotuloDoNo(pendencia.no)}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      {campos.map(({ material: m, texto, leitura }) => (
        <Campo key={m.materialId} rotulo={`${m.codigo} ${m.descricao} (${m.unidadeMedida})`} dica={dicaDo(m, leitura)}>
          {(id, idDaDica) => (
            <input
              id={id}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={texto}
              onChange={(ev) => {
                const valor = ev.target.value
                setTextos((atuais) => ({ ...atuais, [m.materialId]: valor }))
              }}
              aria-describedby={idDaDica}
              aria-invalid={leitura.tipo === 'erro'}
              disabled={enviando}
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>
      ))}
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={algumErro || linhas.length === 0}
        className="self-start"
      >
        Registrar separação
      </Botao>
    </PainelDeEscrita>
  )
}
