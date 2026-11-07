import type { FormEvent } from 'react'
import { useState } from 'react'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { useEscritaDoPainel } from '../expedicao/escritaDoPainel'

/** A coluna da justificativa é `NVARCHAR(500)`; o servidor recusa acima disso (`TextoLongoDemais`). */
const TAMANHO_MAXIMO = 500

interface Props {
  /** Grava a dispensa da baixa ou da reclamação; rejeita na falha. */
  naoRepor: (justificativa: string) => Promise<unknown>
  /** O servidor mudou: no sucesso e no 409, quem abriu recarrega a seção e o cabeçalho. */
  aoConcluir: () => void
  aoFechar: () => void
  /** O alvo, para quem lê o painel saber de que linha ele é. */
  subtitulo?: string
}

/**
 * "Não repor": dispensa a reposição de uma baixa ou de uma reclamação, com a justificativa obrigatória. O duplo toque
 * é segurado pelo `ref` do `useEscritaDoPainel`; a recusa do servidor fica dentro do painel, com o digitado.
 */
export function PainelDeNaoRepor({ naoRepor, aoConcluir, aoFechar, subtitulo }: Props) {
  const [justificativa, setJustificativa] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível dispensar a reposição.')
  const texto = justificativa.trim()

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (texto === '') return
    escrever(() => naoRepor(texto))
  }

  return (
    <PainelDeEscrita titulo="Não repor" subtitulo={subtitulo} aoEnviar={enviar} aoFechar={aoFechar} enviando={enviando}>
      <Campo rotulo="Justificativa" dica="Fica no histórico, junto de quem dispensou.">
        {(id, idDaDica) => (
          <textarea
            id={id}
            aria-describedby={idDaDica}
            value={justificativa}
            onChange={(e) => setJustificativa(e.target.value)}
            maxLength={TAMANHO_MAXIMO}
            rows={3}
            required
            disabled={enviando}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <BannerDeErro mensagem={erro} />
      <Botao type="submit" carregando={enviando} rotuloCarregando="Gravando…" disabled={texto === '' || enviando} className="self-start">
        Não repor
      </Botao>
    </PainelDeEscrita>
  )
}
