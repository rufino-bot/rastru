import { useEffect, useRef, useState } from 'react'
import {
  estornarRegistroDaExpedicao, obterHistoricoDaExpedicao, type RegistroDaExpedicaoDto,
} from '../api/expedicao'
import { ehConflito } from '../api/execucao'
import { formatarDataHora } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { ListaRecolhivel } from '../components/ListaRecolhivel'
import { Pilula } from '../components/Pilula'
import { formatarQuantidade, quantidadeConcordada } from '../execucao/formatacao'

/** Do mais recente ao mais antigo, pelo instante (`Date.parse` respeita o offset); no empate, o Id maior primeiro. */
function doMaisRecente(registros: RegistroDaExpedicaoDto[]): RegistroDaExpedicaoDto[] {
  return [...registros].sort((a, b) => Date.parse(b.dataHora) - Date.parse(a.dataHora) || b.id - a.id)
}

const q = (n: number | null) => formatarQuantidade(n ?? 0)

/** "Contagem: 4 conformes e 2 não conformes"; a parcela zero não aparece (a contagem grava só as que não são zero). */
function textoDaContagem(r: RegistroDaExpedicaoDto): string {
  const partes = [
    (r.conformes ?? 0) > 0 ? quantidadeConcordada(r.conformes!, 'conforme', 'conformes') : null,
    (r.naoConformes ?? 0) > 0 ? quantidadeConcordada(r.naoConformes!, 'não conforme', 'não conformes') : null,
  ].filter((p) => p !== null)
  return `Contagem: ${partes.join(' e ')}`
}

/** O que a linha diz, pelo tipo do registro. */
function tituloDo(r: RegistroDaExpedicaoDto): string {
  switch (r.tipo) {
    case 'Afericao': return r.resultado === 'Conforme' ? 'Aferição conforme' : 'Aferição não conforme'
    case 'Contagem': return textoDaContagem(r)
    case 'Conserto': return `Conserto de ${q(r.quantidade)} em ${r.setorNome ?? ''}`
    case 'PedidoDeLiberacao': return `Pedido de liberação de ${q(r.quantidade)}`
    // Liberar zero é a recusa (C5).
    case 'RespostaDeLiberacao': return r.quantidade ? `Liberação de ${q(r.quantidade)}` : 'Liberação recusada'
    case 'Remessa': return `Remessa de ${q(r.quantidade)}`
  }
}

/** A justificativa do pedido e a observação da resposta; os outros tipos não têm texto. */
function textoDo(r: RegistroDaExpedicaoDto): string | null {
  if (r.texto === null) return null
  if (r.tipo === 'PedidoDeLiberacao') return `Justificativa: ${r.texto}`
  if (r.tipo === 'RespostaDeLiberacao') return `Observação: ${r.texto}`
  return null
}

/** Para reconhecer o registro no botão e na confirmação: o quê, de quem e quando. */
function descricaoDo(r: RegistroDaExpedicaoDto): string {
  const titulo = tituloDo(r)
  return `${titulo[0].toLowerCase()}${titulo.slice(1)} de ${r.usuarioNome} em ${formatarDataHora(r.dataHora)}`
}

/**
 * A remessa nunca se estorna daqui (C20 da spec da Fase 5A): ela desfaz mais de uma Peça, e o lugar dela é
 * "Remessas recentes". A guarda é do tipo, e não só do `estornavel`, para uma remessa que chegue estornável não
 * virar botão nem chamada.
 */
const estornavelAqui = (r: RegistroDaExpedicaoDto) => r.tipo !== 'Remessa' && r.estornavel

interface Props {
  pecaId: number
  /** O autor, ou PCP/Administrador (`usePermissoesDaExpedicao`). Quem decide é o 403 `Proibido`. */
  podeEstornar: (autorId: number) => boolean
  /** Um estorno mudou a Peça: quem mostra o cartão recarrega. */
  aoEstornar: () => void
  /**
   * Muda quando outra escrita do cartão mexeu na Peça: o histórico já carregado recarrega. O que nunca foi aberto
   * não busca nada, e carrega o atual quando abrir.
   */
  versao?: number
}

/**
 * O histórico da Peça na expedição (seção 6.1 da spec da Fase 5A): aferições, contagens, consertos, pedidos e
 * respostas de liberação e remessas, com autor e data. Nasce recolhido numa `ListaRecolhivel` e só busca ao abrir:
 * a tela tem um cartão por Peça, e um `GET` por cartão na abertura seria uma rajada para um histórico que quase
 * ninguém abre. O resumo é "Histórico" até a primeira leitura, e "Histórico (N)" depois.
 *
 * Correção é estorno (C7): "Estornar" aparece onde o servidor disse `estornavel` e a sessão pode estornar aquele
 * autor, e cada tipo vai pela rota dele (`estornarRegistroDaExpedicao`). O estorno de conserto pode voltar 409 se a
 * quantidade já andou (o `estornavel` só olha a autoria): a frase do servidor aparece aqui e tudo recarrega.
 */
export function HistoricoDaExpedicao({ pecaId, podeEstornar, aoEstornar, versao = 0 }: Props) {
  const [registros, setRegistros] = useState<RegistroDaExpedicaoDto[] | null>(null)
  const [pedido, setPedido] = useState(false)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [aConfirmar, setAConfirmar] = useState<RegistroDaExpedicaoDto | null>(null)
  const [erroDoEstorno, setErroDoEstorno] = useState<string | null>(null)
  const [estornando, setEstornando] = useState(false)
  const enviandoRef = useRef(false)
  // A resposta que chega depois de uma mais nova é descartada: duas recargas seguidas não terminam na velha.
  const sequencia = useRef(0)

  async function carregar() {
    const minha = ++sequencia.current
    setErroDeCarga(null)
    try {
      const lidos = await obterHistoricoDaExpedicao(pecaId)
      if (minha === sequencia.current) setRegistros(lidos)
    } catch (e) {
      if (minha === sequencia.current) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar o histórico.'))
    }
  }

  function abrir() {
    if (pedido) return
    setPedido(true)
    carregar()
  }

  // Só a versão dispara: a primeira leitura é a do `abrir`.
  const versaoLida = useRef(versao)
  useEffect(() => {
    if (versao === versaoLida.current) return
    versaoLida.current = versao
    if (pedido) carregar()
  })

  async function confirmar() {
    if (!aConfirmar || enviandoRef.current || !estornavelAqui(aConfirmar)) return
    enviandoRef.current = true
    setEstornando(true)
    const alvo = aConfirmar
    setAConfirmar(null)
    setErroDoEstorno(null)
    try {
      await estornarRegistroDaExpedicao(alvo)
      await carregar()
      aoEstornar()
    } catch (e) {
      setErroDoEstorno(mensagemDeErro(e, 'Não foi possível estornar.'))
      // 409 é tela velha (outra pessoa estornou, ou a quantidade já andou): o histórico e o cartão recarregam.
      if (ehConflito(e)) {
        await carregar()
        aoEstornar()
      }
    } finally {
      enviandoRef.current = false
      setEstornando(false)
    }
  }

  return (
    <>
      <ListaRecolhivel rotulo="Histórico" contagem={registros?.length} temAcao={false} aoAbrir={abrir}>
        <div className="flex flex-col gap-2">
          <BannerDeErro mensagem={erroDeCarga} />
          <BannerDeErro mensagem={erroDoEstorno} />
          {pedido && registros === null && erroDeCarga === null && <EstadoCarregando />}
          {registros !== null && registros.length === 0 && <EstadoVazio titulo="Nenhum registro na expedição." />}
          {registros !== null && registros.length > 0 && (
            <ListaDeCadastro rotulo="Histórico da expedição, do mais recente ao mais antigo">
              {doMaisRecente(registros).map((r) => {
                const texto = textoDo(r)
                return (
                  <ItemComAcao
                    key={`${r.tipo}-${r.id}`}
                    acao={estornavelAqui(r) && podeEstornar(r.usuarioId) && (
                      <Botao
                        variante="secundario"
                        aria-label={`Estornar ${descricaoDo(r)}`}
                        onClick={() => setAConfirmar(r)}
                        disabled={estornando}
                      >
                        Estornar
                      </Botao>
                    )}
                  >
                    <span className="flex flex-wrap items-center gap-2 text-sm font-medium text-tinta">
                      {tituloDo(r)}
                      {r.estornada && <Pilula>estornado</Pilula>}
                    </span>
                    {texto && <span className="text-sm text-tinta">{texto}</span>}
                    <span className="text-xs text-tinta-fraca">{`${r.usuarioNome} · ${formatarDataHora(r.dataHora)}`}</span>
                    {r.tipo === 'Remessa' && (
                      <span className="text-xs text-tinta-fraca">Estorna-se em Remessas recentes.</span>
                    )}
                  </ItemComAcao>
                )
              })}
            </ListaDeCadastro>
          )}
        </div>
      </ListaRecolhivel>
      {/* Fora do `<details>`: o conteúdo de um `<details>` fechado não é desenhado, e o diálogo sumiria com ele. */}
      <Confirmacao
        aberto={aConfirmar !== null}
        mensagem={aConfirmar && `Estornar ${descricaoDo(aConfirmar)}? O estorno fica no histórico.`}
        rotuloConfirmar="Estornar"
        // Estorno é correção, não destruição: nada some do registro. Por isso `primario`, não `perigo`.
        varianteConfirmar="primario"
        aoConfirmar={confirmar}
        aoCancelar={() => setAConfirmar(null)}
      />
    </>
  )
}
