import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatarDataHora } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import { descartarImportacao, listarImportacoes, type ResumoDeImportacaoDto } from '../api/importacao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Confirmacao } from '../components/Confirmacao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'

interface Props {
  agrupamentoId: number
  /**
   * Contador que a página incrementa para forçar a releitura (um rascunho novo acabou de nascer, ou
   * a árvore foi recarregada). Só o valor importa.
   */
  versao: number
}

// O que o `Link` de "Continuar" parece. Estas classes ESPELHAM `POR_VARIANTE.secundario` (mais a
// `BASE`) de `components/Botao.tsx`, que não serve a um `<a>`: mudou lá, muda aqui.
const CLASSES_DO_LINK =
  'inline-flex items-center justify-center rounded-lg border border-borda-campo px-4 py-2 text-tinta '
  + 'transition-colors hover:bg-acao-fundo '
  + 'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao'

/**
 * Os rascunhos de importação do BOM que ainda esperam conferência neste Agrupamento. A seção some
 * quando não há nenhum; um rascunho abandonado, porém, impede excluir o Agrupamento, e é por isso
 * que ele precisa estar à vista e poder ser descartado daqui.
 *
 * Quem lê a lista é este componente (a página só lhe diz quando reler, por `versao`): ela não faz
 * parte da árvore nem das posições, e uma falha dela não pode derrubar a tela da estrutura.
 */
export function ImportacoesEmConferencia({ agrupamentoId, versao }: Props) {
  const [lista, setLista] = useState<ResumoDeImportacaoDto[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [erroDoDescarte, setErroDoDescarte] = useState<string | null>(null)
  const [aDescartar, setADescartar] = useState<ResumoDeImportacaoDto | null>(null)
  const [recarga, setRecarga] = useState(0)

  useEffect(() => {
    let cancelado = false
    setErroDeCarga(null)
    listarImportacoes(agrupamentoId)
      .then((r) => { if (!cancelado) setLista(r) })
      .catch((e) => {
        if (!cancelado) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar as importações em conferência.'))
      })
    return () => { cancelado = true }
  }, [agrupamentoId, versao, recarga])

  const confirmarDescarte = useCallback(async () => {
    if (!aDescartar) return
    const alvo = aDescartar
    setADescartar(null)
    setErroDoDescarte(null)
    try {
      await descartarImportacao(alvo.id)
      setRecarga((n) => n + 1)
    } catch (e) {
      // 404: o rascunho já não existe (descartado em outra aba); reler a lista é a resposta certa.
      if (e instanceof ErroDeApi && e.status === 404) setRecarga((n) => n + 1)
      else setErroDoDescarte(mensagemDeErro(e, 'Não foi possível descartar a importação.'))
    }
  }, [aDescartar])

  if (lista !== null && lista.length === 0 && erroDeCarga === null && erroDoDescarte === null) return null

  return (
    <section aria-labelledby="titulo-importacoes-em-conferencia" className="flex flex-col gap-3">
      <h2 id="titulo-importacoes-em-conferencia" className="text-lg font-medium text-tinta">
        Importações em conferência
      </h2>
      <BannerDeErro mensagem={erroDeCarga} />
      <BannerDeErro mensagem={erroDoDescarte} />
      {lista === null && erroDeCarga === null && <EstadoCarregando />}
      {lista !== null && lista.length > 0 && (
        <ListaDeCadastro rotulo="Importações em conferência">
          {lista.map((i) => (
            <ItemComAcao
              key={i.id}
              acao={(
                <>
                  <Link
                    to={`/importacoes/${i.id}`}
                    aria-label={`Continuar ${i.nomeDoArquivo}`}
                    className={CLASSES_DO_LINK}
                  >
                    Continuar
                  </Link>
                  <Botao
                    variante="secundario"
                    aria-label={`Descartar ${i.nomeDoArquivo}`}
                    onClick={() => setADescartar(i)}
                  >
                    Descartar
                  </Botao>
                </>
              )}
            >
              <span className="font-mono text-sm font-semibold text-tinta">{i.nomeDoArquivo}</span>
              <span className="text-xs text-tinta-fraca">
                {`${i.criadoPor} · atualizado em ${formatarDataHora(i.atualizadoEm)}`}
              </span>
            </ItemComAcao>
          ))}
        </ListaDeCadastro>
      )}
      <Confirmacao
        aberto={aDescartar !== null}
        mensagem={aDescartar && (
          <>
            Descartar a importação de <strong className="font-mono">{aDescartar.nomeDoArquivo}</strong>?
            O que foi conferido nela se perde. Esta ação não pode ser desfeita.
          </>
        )}
        rotuloConfirmar="Descartar"
        aoConfirmar={confirmarDescarte}
        aoCancelar={() => setADescartar(null)}
      />
    </section>
  )
}
