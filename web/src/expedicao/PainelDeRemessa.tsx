import { useState, type FormEvent } from 'react'
import { expedir, type PecaNaExpedicaoDto, type TipoDeAviso } from '../api/expedicao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { formatarQuantidade, quantidadeConcordada, rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'
import { CampoDeQuantidade, useEscritaDoPainel } from './escritaDoPainel'
import { avisosDaRemessa, tirarDaRemessa, type AvisoDaRemessa } from './tiradaDaRemessa'

function textoDoAviso({ tipo, quantidade }: AvisoDaRemessa): string {
  switch (tipo) {
    case 'SemAfericao': return `${formatarQuantidade(quantidade)} sem aferição`
    case 'SemMedicao': return `${formatarQuantidade(quantidade)} sem medição`
    case 'NaoConformeSemLiberacao':
      return `${quantidadeConcordada(quantidade, 'não conforme', 'não conformes')} sem liberação`
  }
}

/**
 * Cor (seção 6.5 da spec da Fase 5A): só a não conforme sem liberação é reprovação, e usa `negativo`. Sem aferição e
 * sem medição não são aprovação nem reprovação, e ficam no texto neutro.
 */
const CLASSE_DO_AVISO: Record<TipoDeAviso, string> = {
  SemAfericao: 'text-tinta',
  SemMedicao: 'text-tinta',
  NaoConformeSemLiberacao: 'font-medium text-negativo',
}

/**
 * A prévia do que a remessa de `quantidade` de uma Peça vai avisar (D8 da spec da Fase 5A), calculada no front por
 * `tirarDaRemessa` e `avisosDaRemessa` (decisão Q2 do plano de front da Fase 5A). Nada quando não há aviso, ou
 * quando a quantidade não vale: o campo já diz por quê. Avisa e não trava: o texto do fim diz que dá para expedir.
 * Serve ao painel "Expedir remessa" e ao campo da Peça marcada no lote, onde o aviso vale por Peça.
 */
export function AvisosDaRemessa({ peca, quantidade }: { peca: PecaNaExpedicaoDto; quantidade: number | null }) {
  const tiradas = quantidade === null ? null : tirarDaRemessa(peca.posicoes, quantidade)
  const avisos = tiradas === null ? [] : avisosDaRemessa(peca.situacao, tiradas)
  if (avisos.length === 0) return null
  return (
    <div className="flex flex-col gap-1 rounded-lg border border-borda px-3 py-2">
      <ul aria-label="Avisos da remessa" className="flex flex-col gap-0.5">
        {avisos.map((a) => (
          <li key={a.tipo} className={`text-sm ${CLASSE_DO_AVISO[a.tipo]}`}>{textoDoAviso(a)}</li>
        ))}
      </ul>
      <p className="text-sm text-tinta-fraca">Dá para expedir assim mesmo; fica registrado.</p>
    </div>
  )
}

interface Props {
  peca: PecaNaExpedicaoDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
}

/**
 * "Expedir remessa" de UMA Peça (C8, C10 da spec da Fase 5A): a Qualidade só diz a quantidade, e o servidor escolhe
 * de que posição tirar. O campo abre com o expedível (tudo menos o que aguarda a Gestão), como o "Separar" da Fase 4,
 * e o painel mostra ANTES de confirmar o que vai sair sem aferição, sem medição ou não conforme sem liberação, sem
 * travar (D8). Os avisos que o `201` devolve não são mostrados de novo: a prévia já foi vista.
 *
 * O duplo toque no celular gravaria duas remessas: o `ref` de `useEscritaDoPainel` segura o segundo `submit` no mesmo
 * quadro, e `enviando` trava o botão e o `Cancelar` até a resposta.
 */
export function PainelDeRemessa({ peca, aoFechar, aoConcluir }: Props) {
  const [texto, setTexto] = useState<string | null>(null)
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível expedir.')

  // O campo intocado acompanha o expedível atual: a recarga depois de um 409 o corrige.
  const mostrado = texto ?? quantidadeParaCampo(peca.expedivel)
  const leitura = lerQuantidade(mostrado, peca.expedivel)

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (leitura.erro !== null) return
    const quantidade = leitura.valor
    escrever(() => expedir([{ estruturaItemId: peca.id, quantidade }]))
  }

  return (
    <PainelDeEscrita
      titulo="Expedir remessa"
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
      <AvisosDaRemessa peca={peca} quantidade={leitura.valor} />
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Expedindo…"
        disabled={leitura.erro !== null}
        className="self-start"
      >
        {leitura.erro === null ? `Expedir ${formatarQuantidade(leitura.valor)}` : 'Expedir'}
      </Botao>
    </PainelDeEscrita>
  )
}
