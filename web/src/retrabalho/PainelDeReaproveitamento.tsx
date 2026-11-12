import { useEffect, useState, type FormEvent } from 'react'
import { mensagemDeErro } from '../api/erros'
import {
  preverReaproveitamento, reaproveitar, type ItemDaSobraDto, type PreviaDoReaproveitamentoDto,
} from '../api/retrabalho'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { CampoDeQuantidade, useEscritaDoPainel } from '../expedicao/escritaDoPainel'
import { formatarQuantidade } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'

interface Props {
  /** O Pedido de Retrabalho que recebe a sobra. */
  retrabalhoId: number
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
  aoFechar: () => void
}

function chaveDoItem(i: ItemDaSobraDto): string {
  return `${i.estruturaItemId}|${i.posicao}|${i.setorId ?? ''}|${i.ordem ?? ''}`
}

function ondeEsta(i: ItemDaSobraDto): string {
  return i.posicao === 'AguardandoColeta'
    ? `Aguardando coleta — ${i.setorNome}, passo ${i.ordem}`
    : `Aguardando montagem — ${i.setorNome}`
}

/** Zero é uma leitura válida aqui (a posição fica de fora); qualquer outro valor passa por `lerQuantidade`. */
function lerDaSobra(texto: string, sobra: number): { valor: number; erro: null } | { valor: null; erro: string } {
  const t = texto.trim().replace(',', '.')
  if (t === '' || /^0+(\.0{0,4})?$/.test(t)) return { valor: 0, erro: null }
  return lerQuantidade(texto, sobra)
}

function arredondar(n: number): number {
  return Math.round(n * 10_000) / 10_000
}

/**
 * "Usar a sobra": a prévia do servidor diz, por nó e posição do original, quanto sobra, quanto o Retrabalho precisa e
 * o sugerido; cada quantidade começa no sugerido e se edita de zero até a sobra. O teto de `precisa` e o resto são do
 * servidor, cuja frase fica dentro do painel, com o digitado. A prévia vazia não oferece o enviar.
 */
export function PainelDeReaproveitamento({ retrabalhoId, aoConcluir, aoFechar }: Props) {
  const [previa, setPrevia] = useState<PreviaDoReaproveitamentoDto | null>(null)
  const [erroDaPrevia, setErroDaPrevia] = useState<string | null>(null)
  const [textos, setTextos] = useState<Record<string, string>>({})
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível usar a sobra.')

  useEffect(() => {
    const controle = new AbortController()
    preverReaproveitamento(retrabalhoId, controle.signal)
      .then(setPrevia)
      .catch((e) => {
        if (!controle.signal.aborted) setErroDaPrevia(mensagemDeErro(e, 'Não foi possível carregar a sobra.'))
      })
    return () => controle.abort()
  }, [retrabalhoId])

  const itens = previa?.itens ?? []
  const linhas = itens.map((i) => {
    const chave = chaveDoItem(i)
    const texto = textos[chave] ?? quantidadeParaCampo(i.sugerido)
    return { item: i, chave, texto, leitura: lerDaSobra(texto, i.sobra) }
  })
  const comErro = linhas.some((l) => l.leitura.erro !== null)
  const total = arredondar(linhas.reduce((soma, l) => soma + (l.leitura.valor ?? 0), 0))
  const podeEnviar = !comErro && total > 0 && !enviando

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!podeEnviar) return
    const usados = linhas.filter((l) => (l.leitura.valor ?? 0) > 0).map((l) => ({
      estruturaItemId: l.item.estruturaItemId,
      posicao: l.item.posicao,
      setorId: l.item.setorId,
      ordem: l.item.ordem,
      quantidade: l.leitura.valor as number,
    }))
    escrever(() => reaproveitar(retrabalhoId, { itens: usados }))
  }

  const vazia = previa !== null && itens.length === 0

  return (
    <PainelDeEscrita
      titulo="Usar a sobra"
      subtitulo="Pronto que sobrou do pedido original e serve a este retrabalho."
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      {previa === null && erroDaPrevia === null && <EstadoCarregando />}
      <BannerDeErro mensagem={erroDaPrevia} />
      {vazia && <p className="text-sm text-tinta-fraca">Nada da sobra serve a este retrabalho.</p>}
      {linhas.map((l) => (
        <div key={l.chave} className="flex flex-col gap-1">
          <p className="text-sm text-tinta-fraca">
            {`${ondeEsta(l.item)} · sobra ${formatarQuantidade(l.item.sobra)} · precisa ${formatarQuantidade(l.item.precisa)}`}
          </p>
          <CampoDeQuantidade
            rotulo={l.item.codigo ? `${l.item.descricao} (${l.item.codigo})` : l.item.descricao}
            texto={l.texto}
            erro={l.leitura.erro}
            aoMudar={(texto) => setTextos((antes) => ({ ...antes, [l.chave]: texto }))}
            desabilitado={enviando}
          />
        </div>
      ))}
      <BannerDeErro mensagem={erro} />
      {!vazia && previa !== null && (
        <Botao
          type="submit"
          carregando={enviando}
          rotuloCarregando="Registrando…"
          disabled={!podeEnviar}
          className="self-start"
        >
          {total > 0 ? `Usar ${formatarQuantidade(total)}` : 'Usar'}
        </Botao>
      )}
    </PainelDeEscrita>
  )
}
