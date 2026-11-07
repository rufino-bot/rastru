import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { listarAgrupamentos, listarSetores, type PedidoDto, type SetorDto } from '../api/cadastros'
import type { BaixaDto } from '../api/baixas'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import { obterEstrutura } from '../api/estrutura'
import {
  abrirRetrabalho, listarRetrabalhos, ROTULO_DO_DESTINO, ROTULO_DO_MOTIVO_DE_RETRABALHO,
  type DestinoDaReclamacao, type MotivoDeRetrabalho, type NovoRetrabalho, type ReclamacaoDto,
} from '../api/retrabalho'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { formatarQuantidade } from '../execucao/formatacao'
import { lerQuantidade } from '../execucao/quantidade'
import { CampoDeQuantidade, useEscritaDoPainel } from '../expedicao/escritaDoPainel'

/** De onde o Retrabalho nasce: uma baixa, uma reclamação, ou nenhuma das duas (avulsa, do cabeçalho do Pedido). */
export type PortaDoRetrabalho =
  | { tipo: 'Baixa'; baixa: BaixaDto }
  | { tipo: 'Reclamacao'; reclamacao: ReclamacaoDto }
  | { tipo: 'Avulsa' }

interface Props {
  /** O Pedido original: o número sugerido é o dele e o prazo vem preenchido com o dele. */
  pedido: Pick<PedidoDto, 'id' | 'dataEntrega'>
  porta: PortaDoRetrabalho
  /** O Retrabalho foi aberto: quem abriu leva a tela ao detalhe dele. */
  aoAbrir: (retrabalho: PedidoDto) => void
  aoFechar: () => void
  /** O servidor recusou por tela velha (409): quem abriu recarrega a baixa, a reclamação e o cabeçalho. */
  aoMudar?: () => void
}

interface PecaOferecida {
  id: number
  rotulo: string
}

const MOTIVOS = Object.keys(ROTULO_DO_MOTIVO_DE_RETRABALHO) as MotivoDeRetrabalho[]
const DESTINOS = Object.keys(ROTULO_DO_DESTINO) as DestinoDaReclamacao[]

/** O motivo que a porta sugere; a avulsa não sugere nenhum, e escolher é obrigatório. */
function motivoSugerido(porta: PortaDoRetrabalho): MotivoDeRetrabalho | '' {
  if (porta.tipo === 'Reclamacao') return 'SolicitacaoDoCliente'
  if (porta.tipo === 'Baixa') {
    // A baixa da não conforme ("Matar") é a que a Qualidade reprovou.
    return porta.baixa.linhas.some((l) => l.origemPosicao === 'NaoConforme') ? 'ReprovacaoDimensional' : 'Perda'
  }
  return ''
}

/** Quanto a baixa ou a reclamação manda repor: o `aRepor` do servidor, que o servidor também usa ao abrir. */
function descricaoDaReposicao(porta: PortaDoRetrabalho): string | null {
  if (porta.tipo === 'Baixa') {
    const topo = porta.baixa.linhas[0]
    return `Repor ${formatarQuantidade(porta.baixa.aRepor)}${topo ? ` de ${topo.descricao}` : ''}`
  }
  if (porta.tipo === 'Reclamacao') {
    return `Repor ${formatarQuantidade(porta.reclamacao.aRepor)} de ${porta.reclamacao.descricao}`
  }
  return null
}

/** O 409 do cadastro de Pedido (número repetido) chega só com o código; a frase é a da tela de Pedidos. */
function falhaDaAbertura(e: unknown): unknown {
  if (e instanceof ErroDeApi && e.codigo === 'ValorDuplicado') {
    return new ErroDeApi(e.status, e.message, 'Já existe um pedido com este número.', e.codigo)
  }
  return e
}

/**
 * "Abrir retrabalho": o Pedido que repõe o que se perdeu ou voltou do cliente. Três portas — a baixa, a reclamação e a
 * avulsa, do cabeçalho do Pedido —, e o painel é o mesmo: o número vem do `numeroSugerido` do servidor (a tela não o
 * calcula), o prazo do original, e o motivo da porta; os três são editáveis. A quantidade da baixa e da reclamação
 * é do servidor (só se mostra); a avulsa pede a Peça e a quantidade. A recusa fica dentro do painel, que continua
 * aberto com o digitado.
 */
export function PainelDeAbertura({ pedido, porta, aoAbrir, aoFechar, aoMudar }: Props) {
  const [numero, setNumero] = useState('')
  const [numeroLido, setNumeroLido] = useState(false)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [dataEntrega, setDataEntrega] = useState(pedido.dataEntrega)
  const [motivo, setMotivo] = useState<MotivoDeRetrabalho | ''>(() => motivoSugerido(porta))
  const [destino, setDestino] = useState<DestinoDaReclamacao | ''>('')
  const [setorId, setSetorId] = useState('')
  const [setores, setSetores] = useState<SetorDto[] | null>(null)
  const [pecas, setPecas] = useState<PecaOferecida[] | null>(null)
  const [pecaId, setPecaId] = useState('')
  const [quantidade, setQuantidade] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(() => {}, aoFechar, 'Não foi possível abrir o retrabalho.')

  // O número sugerido: uma leitura por abertura; a resposta de um painel já desmontado não escreve em estado.
  useEffect(() => {
    let vivo = true
    listarRetrabalhos(pedido.id).then(
      (r) => { if (vivo) { setNumero((atual) => (atual === '' ? r.numeroSugerido : atual)); setNumeroLido(true) } },
      (e: unknown) => { if (vivo) setErroDeCarga(mensagemDeErro(e, 'Não foi possível sugerir o número do retrabalho.')) },
    )
    return () => { vivo = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Os Setores ativos, só para quem pode consertar (a porta da reclamação).
  useEffect(() => {
    if (porta.tipo !== 'Reclamacao') return
    let vivo = true
    listarSetores(false).then(
      (s) => { if (vivo) setSetores(s.filter((x) => x.ativo)) },
      (e: unknown) => { if (vivo) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar os Setores.')) },
    )
    return () => { vivo = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // As Peças do Pedido, só para a avulsa: o nó de topo de cada Agrupamento.
  useEffect(() => {
    if (porta.tipo !== 'Avulsa') return
    let vivo = true
    listarAgrupamentos(pedido.id)
      .then((agrupamentos) => Promise.all(agrupamentos.map((a) => obterEstrutura(a.id).then((nos) => ({ a, nos })))))
      .then(
        (arvores) => {
          if (!vivo) return
          setPecas(arvores.flatMap(({ a, nos }) => nos
            .filter((n) => n.nivelHierarquico === 'Peca')
            .map((n) => ({
              id: n.id,
              rotulo: `${n.codigoDoComponente ? `${n.codigoDoComponente} · ` : ''}${n.descricao} (${a.codigo})`,
            }))))
        },
        (e: unknown) => { if (vivo) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar as Peças do pedido.')) },
      )
    return () => { vivo = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const avulsa = porta.tipo === 'Avulsa'
  const daReclamacao = porta.tipo === 'Reclamacao'
  const leitura = avulsa && quantidade.trim() !== '' ? lerQuantidade(quantidade, Number.MAX_SAFE_INTEGER) : null
  const numeroAparado = numero.trim()
  const portaCompleta = avulsa
    ? pecaId !== '' && leitura !== null && leitura.erro === null
    : daReclamacao
      ? destino !== '' && (destino === 'Refazer' || setorId !== '')
      : true
  const podeEnviar = numeroAparado !== '' && dataEntrega !== '' && motivo !== '' && portaCompleta && !enviando

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (motivo === '' || !podeEnviar) return
    const corpo: NovoRetrabalho = {
      perdaId: porta.tipo === 'Baixa' ? porta.baixa.id : null,
      reclamacaoId: porta.tipo === 'Reclamacao' ? porta.reclamacao.id : null,
      estruturaItemId: avulsa ? Number(pecaId) : null,
      quantidade: avulsa && leitura !== null ? leitura.valor : null,
      destino: daReclamacao && destino !== '' ? destino : null,
      setorId: daReclamacao && destino === 'Consertar' ? Number(setorId) : null,
      numero: numeroAparado,
      dataEntrega,
      motivoRetrabalho: motivo,
    }
    escrever(async () => {
      try {
        aoAbrir(await abrirRetrabalho(pedido.id, corpo))
      } catch (falha) {
        if (falha instanceof ErroDeApi && falha.status === 409) aoMudar?.()
        throw falhaDaAbertura(falha)
      }
    })
  }

  const reposicao = descricaoDaReposicao(porta)

  return (
    <PainelDeEscrita titulo="Abrir retrabalho" aoEnviar={enviar} aoFechar={aoFechar} enviando={enviando}>
      <BannerDeErro mensagem={erroDeCarga} />
      {reposicao && <p className="text-sm text-tinta">{reposicao}</p>}
      <div className="grid gap-4 sm:grid-cols-2">
        <Campo rotulo="Número do pedido" dica={numeroLido ? undefined : 'Buscando o número sugerido…'}>
          {(id, idDaDica) => (
            <input
              id={id} aria-describedby={idDaDica} value={numero} onChange={(e) => setNumero(e.target.value)}
              required disabled={enviando} className={`${CLASSES_DE_CONTROLE} font-mono`}
            />
          )}
        </Campo>
        <Campo rotulo="Data de entrega">
          {(id) => (
            <input
              id={id} type="date" value={dataEntrega} onChange={(e) => setDataEntrega(e.target.value)}
              required disabled={enviando} className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>
      </div>
      <Campo rotulo="Motivo">
        {(id) => (
          <select
            id={id} value={motivo} onChange={(e) => setMotivo(e.target.value as MotivoDeRetrabalho | '')}
            required disabled={enviando} className={CLASSES_DE_CONTROLE}
          >
            <option value="">Escolha o motivo</option>
            {MOTIVOS.map((m) => <option key={m} value={m}>{ROTULO_DO_MOTIVO_DE_RETRABALHO[m]}</option>)}
          </select>
        )}
      </Campo>
      {avulsa && (
        <>
          <Campo rotulo="Peça">
            {(id) => (
              <select
                id={id} value={pecaId} onChange={(e) => setPecaId(e.target.value)}
                disabled={enviando || pecas === null} className={CLASSES_DE_CONTROLE}
              >
                <option value="">Escolha a Peça</option>
                {(pecas ?? []).map((p) => <option key={p.id} value={p.id}>{p.rotulo}</option>)}
              </select>
            )}
          </Campo>
          <CampoDeQuantidade
            rotulo="Quantidade" texto={quantidade} erro={leitura?.erro ?? null} aoMudar={setQuantidade} desabilitado={enviando}
          />
        </>
      )}
      {daReclamacao && (
        <>
          <fieldset className="flex flex-col gap-2">
            <legend className="text-sm font-medium text-tinta">O que fazer com a Peça</legend>
            {DESTINOS.map((d) => (
              <label key={d} className="flex items-center gap-2 text-sm text-tinta">
                <input
                  type="radio" name="destino-do-retrabalho" checked={destino === d}
                  onChange={() => setDestino(d)} disabled={enviando} className="size-4 accent-acao"
                />
                {ROTULO_DO_DESTINO[d]}
              </label>
            ))}
          </fieldset>
          {destino === 'Consertar' && (
            <Campo rotulo="Setor">
              {(id) => (
                <select
                  id={id} value={setorId} onChange={(e) => setSetorId(e.target.value)}
                  disabled={enviando || setores === null} className={CLASSES_DE_CONTROLE}
                >
                  <option value="">Escolha o Setor</option>
                  {(setores ?? []).map((s) => <option key={s.id} value={s.id}>{s.nome}</option>)}
                </select>
              )}
            </Campo>
          )}
        </>
      )}
      <BannerDeErro mensagem={erro} />
      <Botao type="submit" carregando={enviando} rotuloCarregando="Abrindo…" disabled={!podeEnviar} className="self-start">
        Abrir retrabalho
      </Botao>
    </PainelDeEscrita>
  )
}
