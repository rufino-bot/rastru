import { useState, type FormEvent } from 'react'
import { darBaixa, ROTULO_DO_MOTIVO, type MotivoDaBaixa, type NovaBaixa, type OrigemDaBaixa } from '../api/baixas'
import { mensagemDeErro } from '../api/erros'
import type { LocalDto, SaldoDto } from '../api/execucao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { CampoDeCaixa } from '../components/CampoDeCaixa'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { CampoDeQuantidade, useEscritaDoPainel } from '../expedicao/escritaDoPainel'
import { formatarQuantidade, rotuloDoSaldo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo, type LeituraDeQuantidade } from '../execucao/quantidade'
import { chaveDoSaldo, saldosParaBaixa } from './posicoesDaBaixa'
import { PreviaDaBaixa } from './PreviaDaBaixa'
import { usePreviaDaBaixa } from './usePreviaDaBaixa'

/** Onde a baixa de parte tira, quando a tela já sabe (o "Matar" e o descarte da sobra). */
export type OrigemFixa = Pick<LocalDto, 'posicao'> & Partial<Pick<LocalDto, 'setorId' | 'ordem'>>

interface Props {
  no: { id: number; descricao: string; ehPeca: boolean }
  /** Os saldos do nó, como vêm de `GET /agrupamentos/{id}/posicoes`; o painel separa os que servem de origem. */
  saldos: SaldoDto[]
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
  aoFechar: () => void
  /** Motivo que a tela decidiu (o "Matar" é `MortaEmProcesso`, o descarte da sobra é `Descarte`): some a escolha. */
  motivoFixo?: MotivoDaBaixa
  /** Origem que a tela decidiu: é uma baixa de parte com um campo só, sem escolher "Tudo" nem a posição. */
  origemFixa?: OrigemFixa
  /** Teto do campo da `origemFixa`; sem ele, o saldo da posição. A sobra limita à da linha (C17). */
  maximo?: number
  /** O nome da ação, que é o do formulário; "Dar baixa" sem ele. */
  titulo?: string
  /** Frase que a tela quer ao alto do painel (o que o "Matar" faz com as unidades). */
  aviso?: string
}

type Forma = 'tudo' | 'parte'

const MOTIVOS: MotivoDaBaixa[] = ['PerdaArmazem', 'MortaEmProcesso', 'Descarte']

/** A coluna de observação é `NVARCHAR(500)`; o servidor recusa acima disso (C19). */
const TAMANHO_MAXIMO_DA_OBSERVACAO = 500

const FRASE_DA_LIBERACAO = 'aguarda a resposta da Gestão; estorne o pedido de liberação para dar baixa'

interface CampoDaOrigem {
  chave: string
  rotulo: string
  maximo: number
  origem: Omit<OrigemDaBaixa, 'quantidade'>
  texto: string
  /** `null` com o campo vazio: a posição fica de fora da baixa. */
  leitura: LeituraDeQuantidade | null
}

function arredondar(n: number): number {
  return Math.round(n * 10_000) / 10_000
}

/** O verbo do enviar: as baixas de motivo fixo têm o nome que a pessoa conhece ("Matar", "Descartar"). */
function verboDoEnviar(motivoFixo: MotivoDaBaixa | undefined): string {
  if (motivoFixo === 'MortaEmProcesso') return 'Matar'
  if (motivoFixo === 'Descarte') return 'Descartar'
  return 'Dar baixa em'
}

/**
 * "Dar baixa" (D9, D3, D4 da spec da Fase 5B-1): perda ou descarte de um nó. Motivo, depois "Tudo o que está em
 * produção" (o nó e a árvore abaixo dele, D11) ou "Uma parte", com a quantidade por posição de onde tirar (C4).
 *
 * O que a baixa faz vem do servidor (C14): `usePreviaDaBaixa` consulta com o pedido de agora e o enviar só destrava
 * quando essa prévia voltou sem recusa — a conta percorre a árvore e a recusa (`SaldoInsuficiente`...) é do
 * servidor. A pergunta do Item (D13, D14): se a prévia traz `travaNoTopo`, uma caixa, marcada, oferece dar baixa
 * também das unidades da Peça do topo que a perda trava; com `Descarte` de tudo não há trava (C22), e o servidor
 * recusaria `incluirTopo`, então o painel não o pede.
 *
 * Nenhuma escolha vem marcada: motivo e forma são deliberados, não um primeiro item distraído.
 */
export function PainelDeBaixa({
  no, saldos, aoConcluir, aoFechar, motivoFixo, origemFixa, maximo, titulo = 'Dar baixa', aviso,
}: Props) {
  const [motivoEscolhido, setMotivoEscolhido] = useState<MotivoDaBaixa | null>(null)
  const [formaEscolhida, setFormaEscolhida] = useState<Forma | null>(null)
  const [textos, setTextos] = useState<Record<string, string>>({})
  const [incluirTopo, setIncluirTopo] = useState(true)
  const [observacao, setObservacao] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível dar baixa.')

  const motivo = motivoFixo ?? motivoEscolhido
  const forma: Forma | null = origemFixa ? 'parte' : formaEscolhida
  const origens = saldosParaBaixa(saldos)
  const presosNaLiberacao = saldos.filter((s) => s.posicao === 'AguardandoLiberacao' && s.quantidade > 0)

  // Os campos de quantidade: um por posição com saldo; com `origemFixa`, o único. O campo intocado da origem fixa
  // acompanha o teto atual (a recarga depois de um 409 o corrige), como nos painéis da expedição.
  const campos: CampoDaOrigem[] = origemFixa
    ? [campoDaOrigemFixa(origemFixa, origens, maximo, textos)]
    : origens.map((s) => {
        const chave = chaveDoSaldo(s)
        const texto = textos[chave] ?? ''
        return {
          chave, rotulo: rotuloDoSaldo(s), maximo: s.quantidade,
          origem: { posicao: s.posicao, setorId: s.setorId, ordem: s.ordem },
          texto, leitura: texto.trim() === '' ? null : lerQuantidade(texto, s.quantidade),
        }
      })
  const comErro = campos.some((c) => c.leitura?.erro != null)
  const lidas = campos.flatMap((c) => (c.leitura?.valor != null ? [{ ...c.origem, quantidade: c.leitura.valor }] : []))
  const somaDaParte = arredondar(lidas.reduce((total, o) => total + o.quantidade, 0))
  const somaDeTudo = arredondar(origens.reduce((total, s) => total + s.quantidade, 0))
  const quantidadeDoEnviar = forma === 'tudo' ? somaDeTudo : somaDaParte

  // D14: o topo só se pede onde o servidor o aceita — num Item, e não no descarte de tudo.
  const topoPermitido = !no.ehPeca && !(forma === 'tudo' && motivo === 'Descarte')

  let corpo: NovaBaixa | null = null
  if (motivo !== null && forma === 'tudo') {
    corpo = { motivo, observacao: null, tudo: true, incluirTopo: topoPermitido && incluirTopo }
  } else if (motivo !== null && forma === 'parte' && !comErro && lidas.length > 0) {
    corpo = { motivo, observacao: null, tudo: false, origens: lidas, incluirTopo: topoPermitido && incluirTopo }
  }

  const { previa, erro: erroDaPrevia, carregando } = usePreviaDaBaixa(no.id, corpo, saldos)
  const trava = topoPermitido ? previa?.travaNoTopo ?? null : null
  const previaPassou = corpo !== null && !carregando && erroDaPrevia === null && previa !== null
  const podeEnviar = previaPassou && !enviando

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!podeEnviar || corpo === null) return
    const texto = observacao.trim()
    const dela: NovaBaixa = {
      ...corpo, observacao: texto === '' ? null : texto, incluirTopo: corpo.incluirTopo && trava !== null,
    }
    escrever(() => darBaixa(no.id, dela))
  }

  const rotuloDoEnviar = quantidadeDoEnviar > 0
    ? `${verboDoEnviar(motivoFixo)} ${formatarQuantidade(quantidadeDoEnviar)}`
    : verboDoEnviar(motivoFixo).replace(/ em$/, '')

  return (
    <PainelDeEscrita titulo={titulo} subtitulo={no.descricao} aoEnviar={enviar} aoFechar={aoFechar} enviando={enviando}>
      {aviso !== undefined && <p className="text-sm text-tinta-fraca">{aviso}</p>}
      {motivoFixo === undefined && (
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium text-tinta">Motivo</legend>
          {MOTIVOS.map((m) => (
            <label key={m} className="flex items-center gap-2 text-sm text-tinta">
              <input
                type="radio"
                name={`motivo-da-baixa-${no.id}`}
                checked={motivoEscolhido === m}
                onChange={() => setMotivoEscolhido(m)}
                disabled={enviando}
                className="size-4 accent-acao"
              />
              {ROTULO_DO_MOTIVO[m]}
            </label>
          ))}
        </fieldset>
      )}

      {origemFixa === undefined && (
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium text-tinta">Quanto</legend>
          {([['tudo', 'Tudo o que está em produção'], ['parte', 'Uma parte']] as const).map(([valor, rotulo]) => (
            <label key={valor} className="flex items-center gap-2 text-sm text-tinta">
              <input
                type="radio"
                name={`forma-da-baixa-${no.id}`}
                checked={formaEscolhida === valor}
                onChange={() => setFormaEscolhida(valor)}
                disabled={enviando}
                className="size-4 accent-acao"
              />
              {rotulo}
            </label>
          ))}
          {forma === 'tudo' && (
            <p className="text-sm text-tinta-fraca">Leva tudo o que está em produção neste nó e nos que estão abaixo dele.</p>
          )}
        </fieldset>
      )}

      {forma === 'parte' && (
        <div className="flex flex-col gap-3">
          {campos.map((c) => (
            <CampoDeQuantidade
              key={c.chave}
              rotulo={c.rotulo}
              texto={c.texto}
              erro={c.leitura?.erro ?? null}
              aoMudar={(texto) => setTextos((antes) => ({ ...antes, [c.chave]: texto }))}
              desabilitado={enviando}
            />
          ))}
          {origemFixa === undefined && presosNaLiberacao.map((s) => (
            <p key={chaveDoSaldo(s)} className="text-sm text-tinta-fraca">
              {`${rotuloDoSaldo(s)}: ${FRASE_DA_LIBERACAO}.`}
            </p>
          ))}
        </div>
      )}

      {corpo !== null && (
        <div className="flex flex-col gap-3" aria-busy={carregando}>
          {previa !== null && <PreviaDaBaixa previa={previa} noId={no.id} />}
          {previa === null && carregando && <EstadoCarregando />}
          <BannerDeErro
            mensagem={erroDaPrevia === null ? null : mensagemDeErro(erroDaPrevia, 'Não foi possível calcular o que a baixa faz.')}
          />
          {trava !== null && (
            <CampoDeCaixa
              marcado={incluirTopo}
              aoMudar={setIncluirTopo}
              desabilitado={enviando}
              dica={trava.aIniciar < trava.quantidade
                ? `Só ${formatarQuantidade(trava.aIniciar)} ainda estão a iniciar; as outras ficam onde estão.`
                : undefined}
            >
              {`Isso impede montar ${formatarQuantidade(trava.quantidade)} ${trava.descricao}. Dar baixa também delas`}
            </CampoDeCaixa>
          )}
        </div>
      )}

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
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={!podeEnviar}
        className="self-start"
      >
        {rotuloDoEnviar}
      </Botao>
    </PainelDeEscrita>
  )
}

function campoDaOrigemFixa(
  fixa: OrigemFixa, origens: SaldoDto[], maximo: number | undefined, textos: Record<string, string>,
): CampoDaOrigem {
  const chave = chaveDoSaldo({ posicao: fixa.posicao, setorId: fixa.setorId ?? null, ordem: fixa.ordem ?? null })
  const saldo = origens.find((s) => chaveDoSaldo(s) === chave)
  const teto = maximo ?? saldo?.quantidade ?? 0
  const texto = textos[chave] ?? quantidadeParaCampo(teto)
  return {
    chave, rotulo: 'Quantidade', maximo: teto,
    origem: { posicao: fixa.posicao, setorId: fixa.setorId ?? null, ordem: fixa.ordem ?? null },
    texto, leitura: lerQuantidade(texto, teto),
  }
}
