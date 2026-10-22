import { useRef, useState, type ReactNode } from 'react'
import type { PecaNaExpedicaoDto, PosicoesDaPecaDto } from '../api/expedicao'
import { formatarDataHora, type SetorDto } from '../api/cadastros'
import { Botao } from '../components/Botao'
import { ItemComAcao } from '../components/ItemComAcao'
import { Pilula, type TomDePilula } from '../components/Pilula'
import { formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { useDevolverFoco } from '../hooks/useDevolverFoco'
import { CampoDeQuantidade } from './escritaDoPainel'
import { ROTULOS_DA_AFERICAO } from './filtroDaExpedicao'
import { HistoricoDaExpedicao } from './HistoricoDaExpedicao'
import { campoDaPeca, podeEntrarNoLote } from './loteDaExpedicao'
import { PainelDeAfericao } from './PainelDeAfericao'
import { PainelDeConserto } from './PainelDeConserto'
import { PainelDeContagem } from './PainelDeContagem'
import { PainelDePedidoDeLiberacao } from './PainelDePedidoDeLiberacao'
import { AvisosDaRemessa, PainelDeRemessa } from './PainelDeRemessa'
import { usePermissoesDaExpedicao } from './usePermissoesDaExpedicao'

/** A marcação da Peça no lote "Expedir" da tela (C9 da spec da Fase 5A). O estado é da tela; o cartão só mostra. */
export interface LoteDoCartao {
  marcado: boolean
  /** O texto do campo, ou `null` enquanto ninguém o mexeu (mostra o expedível atual; ver `LoteDaExpedicao`). */
  texto: string | null
  /** O lote está em voo: a caixa e o campo travam. */
  desabilitado: boolean
  aoMarcar: (marcado: boolean) => void
  aoDigitar: (texto: string) => void
}

interface Props {
  peca: PecaNaExpedicaoDto
  /** Entra no nome acessível dos controles: a mesma Peça (o mesmo código) aparece em Pedidos diferentes. */
  pedidoNumero: string
  /** Os Setores do cadastro, para o conserto (o painel oferece só os ativos). */
  setores: SetorDto[]
  /** Uma escrita do cartão mudou a Peça (ou um 409 disse que a tela estava velha): a tela recarrega. */
  aoRegistrar: () => void
  /** Sem ele, o cartão não tem caixa de lote. */
  lote?: LoteDoCartao
  /** A tela está recarregando: o foco de um painel que fechou espera a lista nova para saber aonde voltar. */
  recarregando?: boolean
  /**
   * Um painel abriu neste cartão. Serve à tela que precisa saber de qual cartão veio o último painel: se a
   * recarga tirar o cartão da lista (a remessa que conclui o Pedido), quem devolve o foco é ela.
   */
  aoAbrirPainel?: () => void
}

type Painel = 'afericao' | 'contagem' | 'conserto' | 'liberacao' | 'remessa'

// Somas em décimos de milésimo inteiros, como em `tiradaDaRemessa`: 0,1 + 0,2 tem de dar 0,3.
const ESCALA = 10_000
const somar = (...ns: number[]) => ns.reduce((total, n) => total + Math.round(n * ESCALA), 0) / ESCALA

/** O local de expedição inteiro: as cinco posições dele (seção 3.1 da spec da Fase 5A). O conserto não conta. */
const noLocal = (p: PosicoesDaPecaDto) => somar(p.naExpedicao, p.conforme, p.naoConforme, p.aguardandoLiberacao, p.liberada)

/** Tudo o que já foi medido uma a uma e ainda não saiu: as conformes e as não conformes, onde estiverem. */
const medidas = (p: PosicoesDaPecaDto) => somar(p.conforme, p.naoConforme, p.aguardandoLiberacao, p.liberada, p.emConserto)

/**
 * Cor (seção 6.5 da spec da Fase 5A): "Conforme" é aprovação e "Não conforme" é reprovação; "Aferição pendente",
 * "Exige relatório" e "Sem relatório" não são nenhuma das duas, e ficam neutras.
 */
const TOM_DA_SITUACAO = { SemRelatorio: 'neutro', Pendente: 'neutro', Conforme: 'positivo', NaoConforme: 'negativo' } as const satisfies Record<PecaNaExpedicaoDto['situacao'], TomDePilula>

/** O conserto em curso por Setor (as três posições do conserto somadas), na ordem em que o servidor os manda. */
function consertosPorSetor(peca: PecaNaExpedicaoDto): { setorNome: string; quantidade: number }[] {
  const porSetor = new Map<number, { setorNome: string; quantidade: number }>()
  for (const c of peca.consertos) {
    const atual = porSetor.get(c.setorId)
    porSetor.set(c.setorId, { setorNome: c.setorNome, quantidade: somar(atual?.quantidade ?? 0, c.quantidade) })
  }
  return [...porSetor.values()]
}

/**
 * A contagem depois de uma aferição não conforme (seção 6.1 da spec da Fase 5A): as conformes, as não conformes e,
 * dentro destas, onde estão; e o "a medir". As linhas de dentro e o "a medir" só aparecem com algo; as duas de
 * cima, sempre. Texto neutro: a cor do estado já está na pílula.
 */
function ContagemDaPeca({ peca }: { peca: PecaNaExpedicaoDto }) {
  const p = peca.posicoes
  const linhas: { rotulo: string; valor: number; dentro?: boolean; sempre?: boolean }[] = [
    { rotulo: 'Conformes', valor: p.conforme, sempre: true },
    { rotulo: 'Não conformes', valor: somar(p.naoConforme, p.aguardandoLiberacao, p.liberada, p.emConserto), sempre: true },
    { rotulo: '· liberadas pela Gestão', valor: p.liberada, dentro: true },
    ...consertosPorSetor(peca).map((c) => ({ rotulo: `· em conserto (${c.setorNome})`, valor: c.quantidade, dentro: true })),
    { rotulo: '· aguardando liberação', valor: p.aguardandoLiberacao, dentro: true },
    { rotulo: '· aguardando decisão', valor: p.naoConforme, dentro: true },
    { rotulo: 'A medir', valor: p.naExpedicao },
  ]
  return (
    <ul aria-label="Contagem das unidades" className="flex max-w-xs flex-col gap-0.5">
      {linhas.filter((l) => l.sempre || l.valor > 0).map((l) => (
        <li key={l.rotulo} className={`flex justify-between gap-4 text-sm ${l.dentro ? 'pl-3 text-tinta-fraca' : 'text-tinta'}`}>
          <span>{l.rotulo}</span>
          <span>{formatarQuantidade(l.valor)}</span>
        </li>
      ))}
    </ul>
  )
}

/**
 * O cartão de UMA Peça na tela "Expedição" (seção 6.1 da spec da Fase 5A, sobre o esboço do D10): código e
 * descrição, "N aqui" (o local de expedição) "de Q · E expedidas", a marca do relatório e a situação da aferição,
 * e, depois de uma aferição não conforme, a contagem. É um `<li>` (`ItemComAcao`): a tela o põe numa lista.
 *
 * As ações ficam sob `usePermissoesDaExpedicao().registrar` (gating na ação; a leitura é de todos), cada uma só
 * quando faz sentido para o servidor, para não oferecer o que ele recusaria:
 * - **Aferir**: a Peça exige relatório e ainda não tem aferição válida;
 * - **Contar**: aferição não conforme e algo a medir;
 * - **Mandar para conserto**: alguma não conforme aguardando decisão;
 * - **Pedir liberação**: idem, e nenhum pedido em aberto (C18);
 * - **Expedir**: algo expedível.
 *
 * Um painel aberto por vez no cartão, e as ações somem enquanto ele está aberto, como o botão que abre um
 * `PainelDeEscrita` em toda tela. Ao fechar, o foco volta ao botão que o abriu, depois da recarga (`recarregando`);
 * se o botão não voltou (a aferição registrada tira o "Aferir"), cai no nome da Peça. Mudar a marca do lote fecha o
 * painel do cartão, como na Separação.
 */
export function CartaoDaPeca({ peca, pedidoNumero, setores, aoRegistrar, lote, recarregando = false, aoAbrirPainel }: Props) {
  const { registrar, podeEstornar } = usePermissoesDaExpedicao()
  const [aberto, setAberto] = useState<Painel | null>(null)
  // A versão do histórico: cada escrita do cartão a muda, e o histórico já aberto recarrega.
  const [versao, setVersao] = useState(0)
  const ultimoAberto = useRef<Painel | null>(null)
  const botoes = useRef(new Map<Painel, HTMLButtonElement>())
  const titulo = useRef<HTMLSpanElement>(null)

  const marcado = lote?.marcado ?? false
  const [marcadoAntes, setMarcadoAntes] = useState(marcado)
  if (marcado !== marcadoAntes) {
    setMarcadoAntes(marcado)
    setAberto(null)
  }

  useDevolverFoco(
    aberto !== null,
    () => (ultimoAberto.current !== null ? botoes.current.get(ultimoAberto.current) : undefined) ?? titulo.current,
    !recarregando,
  )

  const nome = `${rotuloDoNo(peca)} (${pedidoNumero})`
  const p = peca.posicoes
  const naoConforme = peca.situacao === 'NaoConforme'

  function abrir(painel: Painel) {
    ultimoAberto.current = painel
    setAberto(painel)
    aoAbrirPainel?.()
  }

  // Fecha só se o painel ainda for este: a resposta de um envio que chega depois de outro painel abrir não o fecha.
  const fechar = (painel: Painel) => () => setAberto((atual) => (atual === painel ? null : atual))

  function concluir() {
    setVersao((v) => v + 1)
    aoRegistrar()
  }

  const acoes: { painel: Painel; rotulo: string; mostra: boolean }[] = [
    { painel: 'afericao', rotulo: 'Aferir', mostra: peca.situacao === 'Pendente' },
    { painel: 'contagem', rotulo: 'Contar', mostra: naoConforme && p.naExpedicao > 0 },
    { painel: 'conserto', rotulo: 'Mandar para conserto', mostra: p.naoConforme > 0 },
    { painel: 'liberacao', rotulo: 'Pedir liberação', mostra: p.naoConforme > 0 && peca.liberacaoPendente === null },
    { painel: 'remessa', rotulo: 'Expedir', mostra: peca.expedivel > 0 },
  ]
  const botoesVisiveis = registrar && aberto === null ? acoes.filter((a) => a.mostra) : []
  const caixa = registrar && lote !== undefined && podeEntrarNoLote(peca)

  const acao: ReactNode = (caixa || botoesVisiveis.length > 0) ? (
    <>
      {caixa && (
        <label className="flex items-center gap-2 text-sm text-tinta">
          <input
            type="checkbox"
            checked={lote.marcado}
            onChange={(e) => lote.aoMarcar(e.target.checked)}
            disabled={lote.desabilitado}
            aria-label={`Marcar ${nome}`}
            className="size-5 accent-acao"
          />
          Marcar
        </label>
      )}
      {botoesVisiveis.map((a) => (
        <Botao
          key={a.painel}
          ref={(el) => {
            if (el) botoes.current.set(a.painel, el)
            else botoes.current.delete(a.painel)
          }}
          variante="secundario"
          aria-label={`${a.rotulo} ${nome}`}
          onClick={() => abrir(a.painel)}
        >
          {a.rotulo}
        </Botao>
      ))}
    </>
  ) : null

  const props = { peca, aoConcluir: concluir }
  const campoDoLote = caixa && lote.marcado ? campoDaPeca(peca, lote.texto) : null

  return (
    <ItemComAcao
      acao={acao}
      painel={
        <>
          {caixa && campoDoLote && (
            <div className="flex flex-col gap-3 border-t border-borda pt-3">
              <CampoDeQuantidade
                rotulo="Quantidade"
                nomeAcessivel={`Quantidade — ${nome}`}
                texto={campoDoLote.texto}
                erro={campoDoLote.leitura.erro}
                aoMudar={lote.aoDigitar}
                desabilitado={lote.desabilitado}
              />
              <AvisosDaRemessa peca={peca} quantidade={campoDoLote.leitura.valor} />
            </div>
          )}
          {registrar && aberto === 'afericao' && <PainelDeAfericao {...props} aoFechar={fechar('afericao')} />}
          {registrar && aberto === 'contagem' && <PainelDeContagem {...props} aoFechar={fechar('contagem')} />}
          {registrar && aberto === 'conserto' && (
            <PainelDeConserto {...props} setores={setores} aoFechar={fechar('conserto')} />
          )}
          {registrar && aberto === 'liberacao' && (
            <PainelDePedidoDeLiberacao {...props} aoFechar={fechar('liberacao')} />
          )}
          {registrar && aberto === 'remessa' && <PainelDeRemessa {...props} aoFechar={fechar('remessa')} />}
          <HistoricoDaExpedicao pecaId={peca.id} podeEstornar={podeEstornar} aoEstornar={aoRegistrar} versao={versao} />
        </>
      }
    >
      <span className="flex flex-wrap items-center gap-2 font-medium text-tinta">
        <span ref={titulo} tabIndex={-1}>{rotuloDoNo(peca)}</span>
        <Pilula>{peca.requerRelatorioDimensional ? 'Exige relatório' : 'Sem relatório'}</Pilula>
        {peca.situacao !== 'SemRelatorio' && (
          <Pilula tom={TOM_DA_SITUACAO[peca.situacao]}>{ROTULOS_DA_AFERICAO[peca.situacao]}</Pilula>
        )}
      </span>
      <span className="text-xs text-tinta-fraca">{`Agrupamento ${peca.agrupamentoCodigo}`}</span>
      <span className="flex flex-wrap gap-x-1 text-sm">
        <span className="font-medium text-tinta">{`${formatarQuantidade(noLocal(p))} aqui`}</span>
        <span className="text-tinta-fraca">
          {`de ${formatarQuantidade(peca.quantidade)} · ${formatarQuantidade(p.expedido)} expedidas`}
        </span>
      </span>
      {(naoConforme || medidas(p) > 0) && <ContagemDaPeca peca={peca} />}
      {peca.liberacaoPendente && (
        <span className="text-sm text-tinta-fraca">
          {`Liberação pedida: ${formatarQuantidade(peca.liberacaoPendente.quantidade)}, por `
            + `${peca.liberacaoPendente.usuarioNome} em ${formatarDataHora(peca.liberacaoPendente.dataHora)} — `
            + peca.liberacaoPendente.justificativa}
        </span>
      )}
    </ItemComAcao>
  )
}
