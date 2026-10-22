import { useState, type FormEvent } from 'react'
import { pedirLiberacao, type PecaNaExpedicaoDto } from '../api/expedicao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'
import { CampoDeQuantidade, useEscritaDoPainel } from './escritaDoPainel'

interface Props {
  peca: PecaNaExpedicaoDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
}

/** A coluna `Texto` de `dbo.LiberacaoDimensional` é `NVARCHAR(500)`; o servidor recusa acima disso. */
const TAMANHO_MAXIMO_DA_JUSTIFICATIVA = 500

/**
 * "Pedir liberação" (D7, C6 da spec da Fase 5A): a Qualidade pede à Gestão para expedir não conformes assim mesmo.
 * A justificativa é obrigatória — se o cliente reclamar, o porquê é o que se procura — e vai sem os espaços das
 * pontas. O campo de quantidade abre com todas as que aguardam decisão.
 */
export function PainelDePedidoDeLiberacao({ peca, aoFechar, aoConcluir }: Props) {
  const [texto, setTexto] = useState<string | null>(null)
  const [justificativa, setJustificativa] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível pedir a liberação.')

  const maximo = peca.posicoes.naoConforme
  // O campo intocado acompanha o saldo atual: a recarga depois de um 409 o corrige.
  const mostrado = texto ?? quantidadeParaCampo(maximo)
  const leitura = lerQuantidade(mostrado, maximo)
  const justificativaLimpa = justificativa.trim()
  const invalido = leitura.erro !== null || justificativaLimpa === ''

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (invalido || leitura.erro !== null) return
    const quantidade = leitura.valor
    escrever(() => pedirLiberacao(peca.id, quantidade, justificativaLimpa))
  }

  return (
    <PainelDeEscrita
      titulo="Pedir liberação"
      subtitulo={rotuloDoNo(peca)}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <CampoDeQuantidade
        rotulo="Quantidade"
        texto={mostrado}
        erro={leitura.erro}
        aoMudar={setTexto}
        desabilitado={enviando}
      />
      <Campo rotulo="Justificativa">
        {(id) => (
          <textarea
            id={id}
            value={justificativa}
            onChange={(e) => setJustificativa(e.target.value)}
            maxLength={TAMANHO_MAXIMO_DA_JUSTIFICATIVA}
            rows={3}
            disabled={enviando}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={invalido}
        className="self-start"
      >
        Pedir liberação
      </Botao>
    </PainelDeEscrita>
  )
}
