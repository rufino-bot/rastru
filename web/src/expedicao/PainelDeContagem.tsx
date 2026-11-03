import { useState, type FormEvent } from 'react'
import { contar, type PecaNaExpedicaoDto } from '../api/expedicao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, type LeituraDeQuantidade } from '../execucao/quantidade'
import { CampoDeQuantidade, useEscritaDoPainel } from './escritaDoPainel'

interface Props {
  peca: PecaNaExpedicaoDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
}

// A soma é feita em décimos de milésimo inteiros, como em `tiradaDaRemessa`: 0,1 + 0,2 tem de dar 0,3.
const ESCALA = 10_000
const emUnidades = (q: number) => Math.round(q * ESCALA)

/**
 * Uma das duas parcelas. O campo vazio é zero ("medi 6, todas conformes" não pede digitar 0 nas não conformes), e
 * zero digitado também vale; o resto passa pela leitura de sempre, com o "a medir" como máximo.
 */
function lerParcela(texto: string, aMedir: number): LeituraDeQuantidade {
  const normalizado = texto.trim().replace(',', '.')
  if (normalizado === '' || /^0+(\.0+)?$/.test(normalizado)) return { valor: 0, erro: null }
  return lerQuantidade(texto, aMedir)
}

/**
 * "Contar as unidades" (D4 da spec da Fase 5A): depois de uma aferição não conforme, mede-se uma a uma o que está
 * no local de expedição ainda sem medição ("a medir", a posição `NaExpedicao`), e se registra quantas passaram e
 * quantas não. As duas parcelas vão juntas, tudo ou nada; a soma não passa do "a medir" e não pode ser zero.
 */
export function PainelDeContagem({ peca, aoFechar, aoConcluir }: Props) {
  const [conformes, setConformes] = useState('')
  const [naoConformes, setNaoConformes] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível registrar a contagem.')

  const aMedir = peca.posicoes.naExpedicao
  const leituraConformes = lerParcela(conformes, aMedir)
  const leituraNaoConformes = lerParcela(naoConformes, aMedir)
  const soma = leituraConformes.erro === null && leituraNaoConformes.erro === null
    ? emUnidades(leituraConformes.valor) + emUnidades(leituraNaoConformes.valor)
    : null
  const acimaDoAMedir = soma !== null && soma > emUnidades(aMedir)
  const invalido = soma === null || soma === 0 || acimaDoAMedir

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (invalido || leituraConformes.erro !== null || leituraNaoConformes.erro !== null) return
    const c = leituraConformes.valor
    const n = leituraNaoConformes.valor
    escrever(() => contar(peca.id, c, n))
  }

  return (
    <PainelDeEscrita
      titulo="Contar as unidades"
      subtitulo={rotuloDoNo(peca)}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <p className="text-sm text-tinta-fraca">{`${formatarQuantidade(aMedir)} a medir.`}</p>
      <div className="grid gap-4 sm:grid-cols-2">
        <CampoDeQuantidade
          rotulo="Conformes"
          texto={conformes}
          erro={leituraConformes.erro}
          aoMudar={setConformes}
          desabilitado={enviando}
        />
        <CampoDeQuantidade
          rotulo="Não conformes"
          texto={naoConformes}
          erro={leituraNaoConformes.erro}
          aoMudar={setNaoConformes}
          desabilitado={enviando}
        />
      </div>
      {acimaDoAMedir && (
        <p className="text-sm text-tinta">
          {`As duas somam ${formatarQuantidade(soma / ESCALA)}; há ${formatarQuantidade(aMedir)} a medir.`}
        </p>
      )}
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={invalido}
        className="self-start"
      >
        Registrar contagem
      </Botao>
    </PainelDeEscrita>
  )
}
