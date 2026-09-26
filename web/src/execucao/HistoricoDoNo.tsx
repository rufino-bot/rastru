import { useEffect, useRef, useState } from 'react'
import {
  obterLivroDoNo, estornarMovimentacao, estornarMontagem, ehConflito,
  type LivroDoNoDto, type MontagemDto, type MovimentacaoDto,
} from '../api/execucao'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Botao } from '../components/Botao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade, rotuloDoLocal, rotuloDoTipo } from './formatacao'

type Estornavel =
  | { tipo: 'movimentacao'; registro: MovimentacaoDto }
  | { tipo: 'montagem'; registro: MontagemDto }

interface Props {
  noId: number
  /** Autor, ou PCP/Administrador (spec §4.5). Quem decide é o 403 `Proibido` do backend. */
  podeEstornar: (autorId: number) => boolean
  /** Um estorno mudou o livro — a árvore recarrega as posições. */
  aoEstornar: () => void
}

/**
 * O livro de UM nó (spec da Fase 3 §6.3): quem fez o quê, quando, e o que já foi estornado — com
 * **Estornar** para quem pode. O livro é só de inclusão: estornar grava o movimento inverso, e as
 * duas linhas continuam aqui (regra do `CLAUDE.md`, "O livro de movimentações é só de inclusão").
 *
 * O que NÃO tem "Estornar", e por quê (spec §4.5):
 * - um `Estorno` — estorno não se estorna; o registro errado se corrige registrando de novo;
 * - o que já foi estornado (`JaEstornado`);
 * - a baixa de um filho numa montagem: estorna-se a montagem inteira, na linha do PAI.
 */
export function HistoricoDoNo({ noId, podeEstornar, aoEstornar }: Props) {
  const [livro, setLivro] = useState<LivroDoNoDto | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [aConfirmar, setAConfirmar] = useState<Estornavel | null>(null)
  const [erroDoEstorno, setErroDoEstorno] = useState<string | null>(null)
  // Fix pass (review Important 1): sem isto, o diálogo fecha antes do POST responder e a linha
  // continua com "Estornar" ativo — um segundo toque reabre a confirmação e manda um segundo
  // `POST /movimentacoes/:id/estorno` (o servidor recusa com `JaEstornado`, mas o operador lê como
  // se o estorno tivesse falhado). Mesmo padrão de `FormularioDeQuantidade`: `enviandoRef` guarda a
  // função inteira (o toque no mesmo quadro, antes do React redesenhar); `estornando` desabilita
  // TODO "Estornar" — de movimentação e de montagem — enquanto o pedido está em voo (spec §8.1).
  const [estornando, setEstornando] = useState(false)
  const enviandoRef = useRef(false)

  async function carregar(id: number) {
    setErroDeCarga(null)
    try {
      setLivro(await obterLivroDoNo(id))
    } catch (e) {
      setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar o histórico.'))
    }
  }

  useEffect(() => { carregar(noId) }, [noId])

  async function confirmar() {
    if (!aConfirmar || enviandoRef.current) return
    enviandoRef.current = true
    setEstornando(true)
    const alvo = aConfirmar
    setAConfirmar(null)
    setErroDoEstorno(null)
    try {
      if (alvo.tipo === 'movimentacao') await estornarMovimentacao(alvo.registro.id)
      else await estornarMontagem(alvo.registro.id)
      await carregar(noId)
      aoEstornar()
    } catch (e) {
      setErroDoEstorno(mensagemDeErro(e, 'Não foi possível estornar.'))
      if (ehConflito(e)) {
        await carregar(noId)
        aoEstornar()
      }
    } finally {
      enviandoRef.current = false
      setEstornando(false)
    }
  }

  const vazio = livro !== null && livro.movimentacoes.length === 0 && livro.montagens.length === 0

  return (
    <section aria-label="Histórico do nó" className="flex flex-col gap-3">
      <h3 className="font-medium text-tinta">Histórico</h3>
      <BannerDeErro mensagem={erroDeCarga} />
      <BannerDeErro mensagem={erroDoEstorno} />
      {livro === null && erroDeCarga === null && <EstadoCarregando />}
      {vazio && <p className="text-sm text-tinta-fraca">Nenhum registro ainda.</p>}
      {livro !== null && livro.movimentacoes.length > 0 && (
        <ListaDeCadastro rotulo="Movimentações">
          {livro.movimentacoes.map((m) => {
            const estornavel = !m.estornada && m.tipo !== 'Estorno' && m.montagemId === null
            return (
              <ItemComAcao
                key={m.id}
                acao={estornavel && podeEstornar(m.usuarioId) && (
                  <Botao
                    variante="secundario"
                    aria-label={`Estornar o registro nº ${m.id}`}
                    onClick={() => setAConfirmar({ tipo: 'movimentacao', registro: m })}
                    disabled={estornando}
                  >
                    Estornar
                  </Botao>
                )}
              >
                <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                  {`nº ${m.id} · ${rotuloDoTipo(m.tipo)} de ${formatarQuantidade(m.quantidade)}`}
                  {m.estornada && <Pilula>estornado</Pilula>}
                </span>
                <span className="text-sm text-tinta">{`${rotuloDoLocal(m.origem)} → ${rotuloDoLocal(m.destino)}`}</span>
                <span className="text-xs text-tinta-fraca">{`${m.usuarioNome} · ${formatarDataHora(m.dataHora)}`}</span>
                {m.estornoDeId !== null && (
                  <span className="text-xs text-tinta-fraca">{`Desfaz o registro nº ${m.estornoDeId}.`}</span>
                )}
                {m.montagemId !== null && m.tipo === 'Montagem' && !m.estornada && (
                  <span className="text-xs text-tinta-fraca">Parte de uma montagem do pai: estorna-se a montagem inteira, no pai.</span>
                )}
              </ItemComAcao>
            )
          })}
        </ListaDeCadastro>
      )}
      {livro !== null && livro.montagens.length > 0 && (
        <ListaDeCadastro rotulo="Montagens deste nó">
          {livro.montagens.map((mo) => (
            <ItemComAcao
              key={mo.id}
              acao={!mo.estornada && podeEstornar(mo.usuarioId) && (
                <Botao
                  variante="secundario"
                  aria-label={`Estornar a montagem nº ${mo.id}`}
                  onClick={() => setAConfirmar({ tipo: 'montagem', registro: mo })}
                  disabled={estornando}
                >
                  Estornar
                </Botao>
              )}
            >
              <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                {`Montagem nº ${mo.id} de ${formatarQuantidade(mo.quantidade)} em ${mo.setorNome}`}
                {mo.estornada && <Pilula>estornada</Pilula>}
              </span>
              <span className="text-xs text-tinta-fraca">{`${mo.usuarioNome} · ${formatarDataHora(mo.dataHora)}`}</span>
            </ItemComAcao>
          ))}
        </ListaDeCadastro>
      )}
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && (aConfirmar.tipo === 'movimentacao'
          ? `Estornar o registro nº ${aConfirmar.registro.id} (${rotuloDoTipo(aConfirmar.registro.tipo)} de ${formatarQuantidade(aConfirmar.registro.quantidade)})? O movimento inverso fica no histórico.`
          : `Estornar a montagem nº ${aConfirmar.registro.id} (${formatarQuantidade(aConfirmar.registro.quantidade)} em ${aConfirmar.registro.setorNome})? Os filhos voltam a aguardar montagem, e o estorno fica no histórico.`)}
        rotuloConfirmar="Estornar"
        // Estorno é correção, não destruição: nada some do livro. Por isso `primario`, não `perigo`.
        varianteConfirmar="primario"
        aoConfirmar={confirmar}
        aoCancelar={() => setAConfirmar(null)}
      />
    </section>
  )
}
