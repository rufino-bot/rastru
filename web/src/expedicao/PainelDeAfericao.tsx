import { useState, type FormEvent } from 'react'
import { aferir, type PecaNaExpedicaoDto, type ResultadoDaAfericao } from '../api/expedicao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { rotuloDoNo } from '../execucao/formatacao'
import { useEscritaDoPainel } from './escritaDoPainel'

interface Props {
  peca: PecaNaExpedicaoDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
}

const OPCOES: { valor: ResultadoDaAfericao; rotulo: string }[] = [
  { valor: 'Conforme', rotulo: 'Conforme' },
  { valor: 'NaoConforme', rotulo: 'Não conforme' },
]

/**
 * "Aferir" (D3 da spec da Fase 5A): mede-se uma unidade, e ela decide o lote inteiro. Só o resultado entra no
 * Rastru, nunca as cotas (D2). Nada vem escolhido: aferir é decisão, e um toque distraído não pode registrá-la.
 */
export function PainelDeAfericao({ peca, aoFechar, aoConcluir }: Props) {
  const [resultado, setResultado] = useState<ResultadoDaAfericao | null>(null)
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível registrar a aferição.')

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (resultado === null) return
    escrever(() => aferir(peca.id, resultado))
  }

  return (
    <PainelDeEscrita
      titulo="Aferir"
      subtitulo={rotuloDoNo(peca)}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium text-tinta">Resultado</legend>
        {OPCOES.map((o) => (
          <label key={o.valor} className="flex items-center gap-2 text-sm text-tinta">
            <input
              type="radio"
              name={`resultado-da-afericao-${peca.id}`}
              checked={resultado === o.valor}
              onChange={() => setResultado(o.valor)}
              disabled={enviando}
              className="size-4 accent-acao"
            />
            {o.rotulo}
          </label>
        ))}
        <p className="text-sm text-tinta-fraca">Uma unidade medida decide o lote.</p>
      </fieldset>
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={resultado === null}
        className="self-start"
      >
        Registrar aferição
      </Botao>
    </PainelDeEscrita>
  )
}
