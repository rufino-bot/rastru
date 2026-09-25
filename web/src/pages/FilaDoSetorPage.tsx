import { useEffect, useId, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  obterFila, type FilaDoSetorDto, type GrupoAguardandoMontagem, type LinhaDeSobra, type NoResumoDto,
} from '../api/execucao'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import { caminhoDoNo, descreverDestino, formatarQuantidade, rotuloDoNo } from '../execucao/formatacao'
import { lembrarSetor } from '../execucao/setorLembrado'
import type { EstadoDaEscolhaDeSetor } from './FilaPage'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { ItemComAcao } from '../components/ItemComAcao'

const ESCOLHER: EstadoDaEscolhaDeSetor = { escolher: true }

function TrocarDeSetor() {
  return (
    <Link
      to="/fila"
      state={ESCOLHER}
      className="text-sm font-medium rounded underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      Trocar de Setor
    </Link>
  )
}

/** `/fila/:setorId` — a fila de um Setor (spec §6.1). */
export function FilaDoSetorPage() {
  const { setorId } = useParams<{ setorId: string }>()
  const id = Number(setorId)
  if (!Number.isInteger(id) || id <= 0) {
    return (
      <Pagina titulo="Fila do Setor" acao={<TrocarDeSetor />}>
        <BannerDeErro mensagem="Este Setor não existe." />
      </Pagina>
    )
  }
  // `key`: trocar de Setor desmonta a fila anterior inteira — formulário aberto incluído.
  return <FilaDoSetor key={id} setorId={id} />
}

function FilaDoSetor({ setorId }: { setorId: number }) {
  const { dados: fila, carregando, erro } = useCargaPeriodica(
    () => obterFila(setorId), setorId, INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar a fila.',
  )

  // Lembra só o Setor cuja fila CARREGOU: um Id digitado na barra que dá 404 não vira lembrança.
  useEffect(() => { if (fila?.setorId === setorId) lembrarSetor(setorId) }, [fila, setorId])

  const titulo = fila ? `Fila — ${fila.setorNome}` : 'Fila do Setor'

  return (
    <Pagina titulo={titulo} acao={<TrocarDeSetor />}>
      {/* Com a fila já na tela, este banner é de uma ATUALIZAÇÃO que falhou: a lista abaixo é a da
          última carga boa (decisão 2 de `useCargaPeriodica`). */}
      <BannerDeErro mensagem={erro} />
      {carregando && <EstadoCarregando />}
      {fila && <SecoesDaFila fila={fila} />}
    </Pagina>
  )
}

function SecoesDaFila({ fila }: { fila: FilaDoSetorDto }) {
  const vazia = fila.aIniciar.length === 0 && fila.emTrabalho.length === 0
    && fila.aguardandoColeta.length === 0 && fila.aguardandoMontagem.length === 0 && fila.sobra.length === 0
  if (vazia) {
    return (
      <EstadoVazio
        titulo="Nada neste Setor agora"
        descricao="O que chegar para iniciar, trabalhar, coletar ou montar aqui aparece nesta tela."
      />
    )
  }

  return (
    <>
      {fila.aIniciar.length > 0 && (
        <Secao titulo="A iniciar aqui">
          {fila.aIniciar.map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} a iniciar · passo ${l.ordem}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.emTrabalho.length > 0 && (
        <Secao titulo="Em trabalho">
          {fila.emTrabalho.map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} em trabalho · passo ${l.ordem}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.aguardandoColeta.length > 0 && (
        <Secao titulo="Aguardando coleta">
          {fila.aguardandoColeta.map((l) => (
            <ItemComAcao key={`${l.no.id}-${l.ordem}`}>
              <CabecalhoDoNo no={l.no} />
              <Detalhe>{`${formatarQuantidade(l.quantidade)} aguardando coleta · passo ${l.ordem}`}</Detalhe>
              <Detalhe>{`Destino: ${descreverDestino(l.destino, l.no)}`}</Detalhe>
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.aguardandoMontagem.length > 0 && (
        <Secao titulo="Aguardando montagem">
          {fila.aguardandoMontagem.map((g) => (
            <ItemComAcao key={g.pai.id}>
              <GrupoDeMontagem grupo={g} />
            </ItemComAcao>
          ))}
        </Secao>
      )}
      {fila.sobra.length > 0 && (
        <Secao titulo="Sobra">
          {fila.sobra.map((s) => (
            <ItemComAcao key={`${s.no.id}-${s.origem}-${s.ordem ?? ''}`}>
              <CabecalhoDoNo no={s.no} />
              <DetalheDaSobra sobra={s} />
            </ItemComAcao>
          ))}
        </Secao>
      )}
    </>
  )
}

function Secao({ titulo, children }: { titulo: string; children: ReactNode }) {
  const id = useId()
  return (
    <section aria-labelledby={id} className="flex flex-col gap-3">
      <h2 id={id} className="text-lg font-medium text-tinta">{titulo}</h2>
      <ListaDeCadastro rotulo={titulo}>{children}</ListaDeCadastro>
    </section>
  )
}

function CabecalhoDoNo({ no }: { no: NoResumoDto }) {
  return (
    <>
      <span className="font-medium text-tinta">{rotuloDoNo(no)}</span>
      <span className="text-xs text-tinta-fraca">{caminhoDoNo(no)}</span>
    </>
  )
}

function Detalhe({ children }: { children: ReactNode }) {
  return <span className="text-sm text-tinta">{children}</span>
}

/**
 * "Dá para montar N; falta X de Y para a próxima" (spec §7.6). O "falta" é por filho, e só existe
 * enquanto há próxima unidade a montar — a API manda `null` quando não há.
 */
function GrupoDeMontagem({ grupo }: { grupo: GrupoAguardandoMontagem }) {
  return (
    <>
      <CabecalhoDoNo no={grupo.pai} />
      <Detalhe>
        {`Dá para montar ${formatarQuantidade(grupo.daParaMontar)}; falta montar ${formatarQuantidade(grupo.faltaMontar)}.`}
      </Detalhe>
      <ul aria-label={`Filhos de ${grupo.pai.descricao}`} className="flex flex-col gap-1 text-sm text-tinta-fraca">
        {grupo.filhos.map((f) => (
          <li key={f.no.id}>
            {`${rotuloDoNo(f.no)}: ${formatarQuantidade(f.presente)} aqui, ${formatarQuantidade(f.quantidadePorPai)} por unidade`}
            {f.faltaParaProxima !== null && f.faltaParaProxima > 0 && f.necessarioParaProxima !== null
              && ` — falta ${formatarQuantidade(f.faltaParaProxima)} de ${formatarQuantidade(f.necessarioParaProxima)} para a próxima`}
          </li>
        ))}
      </ul>
    </>
  )
}

/**
 * A sobra (spec §7.5, regra 30) é só informada: o descarte é registrado na Fase 5, pelo ator da
 * perda. Quando o filho aguarda montagem em mais de um Setor, o texto diz que não dá para saber em
 * qual está a unidade a mais, em vez de escolher um por conta própria.
 */
function DetalheDaSobra({ sobra }: { sobra: LinhaDeSobra }) {
  const q = formatarQuantidade(sobra.quantidade)
  if (sobra.origem === 'UltimoPasso') {
    return <Detalhe>{`${q} a mais no passo ${sobra.ordem}: o pai já tem o que precisa.`}</Detalhe>
  }
  return (
    <>
      <Detalhe>{`${q} a mais aguardando montagem do que o pai precisa.`}</Detalhe>
      {sobra.emMaisDeUmSetor && (
        <span className="text-xs text-tinta-fraca">
          Este item aguarda montagem em mais de um Setor; não dá para saber em qual está a unidade a mais.
        </span>
      )}
    </>
  )
}
