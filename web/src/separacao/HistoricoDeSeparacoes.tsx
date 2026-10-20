import { useEffect, useId, useRef, useState } from 'react'
import { obterHistoricoDeSeparacoes, estornarSeparacao, type SeparacaoDto } from '../api/separacao'
import { ehConflito } from '../api/execucao'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { Botao } from '../components/Botao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade } from '../execucao/formatacao'

/**
 * Do mais recente ao mais antigo, como o `HistoricoDoNo`: quem abre o histórico quer ver primeiro o que
 * acabou de acontecer. O servidor já manda nessa ordem; reordenar aqui só tira da tela a dependência
 * disso. A comparação é pelo instante (`Date.parse` respeita o offset), e no empate o Id maior primeiro.
 */
function doMaisRecente(separacoes: SeparacaoDto[]): SeparacaoDto[] {
  return [...separacoes].sort((a, b) => Date.parse(b.dataHora) - Date.parse(a.dataHora) || b.id - a.id)
}

function tituloDa(s: SeparacaoDto): string {
  const quantidade = `${formatarQuantidade(s.quantidade)} ${s.unidadeMedida}`
  return s.tipo === 'Estorno' ? `Estorno de ${quantidade}` : `Separação de ${quantidade}`
}

/** O que o Separador lê para reconhecer a linha no botão e no diálogo: quantidade, material e instante. */
function descricaoDa(s: SeparacaoDto): string {
  return `${formatarQuantidade(s.quantidade)} ${s.unidadeMedida} de ${s.materialCodigo}, em ${formatarDataHora(s.dataHora)}`
}

interface Props {
  noId: number
  /** O autor, ou PCP/Administrador (`usePermissoesDaSeparacao`). Quem decide é o 403 `Proibido`. */
  podeEstornar: (autorId: number) => boolean
  /** Um estorno mudou o separado — o painel recarrega os materiais do nó. */
  aoEstornar: () => void
}

/**
 * O livro de separação de UM nó (spec da Fase 4, seção 6.2; regra 34): quem separou o quê, quando, e o
 * que já foi estornado, com "Estornar" para quem pode. O livro é só de inclusão: estornar grava a linha
 * inversa, e as duas continuam aqui.
 *
 * O botão aparece só onde o servidor disse `estornavel` (uma `Separacao` sem estorno) e a sessão pode
 * estornar aquele autor — o servidor calcula `estornavel` para quem pede, mas o Separador só estorna o
 * que é dele, e a tela não oferece o que receberia o 403.
 */
export function HistoricoDeSeparacoes({ noId, podeEstornar, aoEstornar }: Props) {
  const idDoTitulo = useId()
  const [separacoes, setSeparacoes] = useState<SeparacaoDto[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [aConfirmar, setAConfirmar] = useState<SeparacaoDto | null>(null)
  const [erroDoEstorno, setErroDoEstorno] = useState<string | null>(null)
  // Mesmo padrão do `HistoricoDoNo`: o `ref` segura o segundo toque no mesmo quadro, e `estornando`
  // desabilita todo "Estornar" enquanto o pedido está em voo.
  const [estornando, setEstornando] = useState(false)
  const enviandoRef = useRef(false)

  async function carregar(id: number) {
    setErroDeCarga(null)
    try {
      setSeparacoes(await obterHistoricoDeSeparacoes(id))
    } catch (e) {
      setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar as separações.'))
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
      await estornarSeparacao(alvo.id)
      await carregar(noId)
      aoEstornar()
    } catch (e) {
      setErroDoEstorno(mensagemDeErro(e, 'Não foi possível estornar a separação.'))
      // 409 (`JaEstornada`, `EstornoDeEstorno`) é tela velha: outra pessoa estornou antes. O histórico
      // e os materiais recarregam, como no `HistoricoDoNo`.
      if (ehConflito(e)) {
        await carregar(noId)
        aoEstornar()
      }
    } finally {
      enviandoRef.current = false
      setEstornando(false)
    }
  }

  const estornadas = new Set(
    (separacoes ?? []).filter((s) => s.estornoDeId !== null).map((s) => s.estornoDeId),
  )

  function linhaDoEstorno(estornoDeId: number): string {
    const desfeita = separacoes?.find((s) => s.id === estornoDeId)
    return desfeita
      ? `Desfaz a separação de ${formatarDataHora(desfeita.dataHora)}.`
      : 'Desfaz uma separação anterior.'
  }

  return (
    <section aria-labelledby={idDoTitulo} className="flex flex-col gap-3">
      <h3 id={idDoTitulo} className="font-medium text-tinta">Separações</h3>
      <BannerDeErro mensagem={erroDeCarga} />
      <BannerDeErro mensagem={erroDoEstorno} />
      {separacoes === null && erroDeCarga === null && <EstadoCarregando />}
      {separacoes !== null && separacoes.length === 0 && <EstadoVazio titulo="Nenhuma separação registrada." />}
      {separacoes !== null && separacoes.length > 0 && (
        <ListaDeCadastro rotulo="Separações, da mais recente à mais antiga">
          {doMaisRecente(separacoes).map((s) => (
            <ItemComAcao
              key={s.id}
              acao={s.estornavel && podeEstornar(s.usuarioId) && (
                <Botao
                  variante="secundario"
                  aria-label={`Estornar a separação de ${descricaoDa(s)}`}
                  onClick={() => setAConfirmar(s)}
                  disabled={estornando}
                >
                  Estornar
                </Botao>
              )}
            >
              <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                {tituloDa(s)}
                {s.tipo === 'Separacao' && estornadas.has(s.id) && <Pilula>estornada</Pilula>}
              </span>
              <span className="text-sm text-tinta">{`${s.materialCodigo} ${s.materialDescricao}`}</span>
              <span className="text-xs text-tinta-fraca">{`${s.usuarioNome} · ${formatarDataHora(s.dataHora)}`}</span>
              {s.estornoDeId !== null && (
                <span className="text-xs text-tinta-fraca">{linhaDoEstorno(s.estornoDeId)}</span>
              )}
            </ItemComAcao>
          ))}
        </ListaDeCadastro>
      )}
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && `Estornar a separação de ${descricaoDa(aConfirmar)}? O estorno fica no histórico.`}
        rotuloConfirmar="Estornar"
        // Estorno é correção, não destruição: nada some do livro. Por isso `primario`, não `perigo`.
        varianteConfirmar="primario"
        aoConfirmar={confirmar}
        aoCancelar={() => setAConfirmar(null)}
      />
    </section>
  )
}
