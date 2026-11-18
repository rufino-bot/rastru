import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { buscarPanorama } from '../api/panorama'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { CabecalhoOrdenavel } from '../components/CabecalhoOrdenavel'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { FiltroDeDemanda } from '../components/FiltroDeDemanda'
import { IntervaloDeDatas } from '../components/IntervaloDeDatas'
import { Pagina } from '../components/Pagina'
import { SeletorDeOrdem, type OpcaoDeOrdem } from '../components/SeletorDeOrdem'
import { useSelecaoNaUrl } from '../hooks/useSelecaoNaUrl'
import { INTERVALO_DA_EXECUCAO_MS, useCargaPeriodica } from '../hooks/useCargaPeriodica'
import { CartaoDoPanorama } from './CartaoDoPanorama'
import { CHAVES_DO_PANORAMA, facetasDoPanorama, filtrarPanorama } from './filtroDoPanorama'
import { LinhaDoPanorama } from './LinhaDoPanorama'
import { ordemDaUrl, ordemParaUrl, ordenarPanorama, type OrdemDoPanorama } from './ordenarPanorama'

/** O valor do seletor para "sem ordem escolhida": a ordem do servidor, que não vai à URL. */
const ORIGINAL = 'original'

const OPCOES_DE_ORDEM: readonly OpcaoDeOrdem<string>[] = [
  { valor: ORIGINAL, rotulo: 'Ordem da tela' },
  { valor: 'prazo-asc', rotulo: 'Prazo (mais próximo)' },
  { valor: 'prazo-desc', rotulo: 'Prazo (mais distante)' },
  { valor: 'pedido-asc', rotulo: 'Pedido (crescente)' },
  { valor: 'pedido-desc', rotulo: 'Pedido (decrescente)' },
  { valor: 'codigo-asc', rotulo: 'Código (A→Z)' },
  { valor: 'codigo-desc', rotulo: 'Código (Z→A)' },
  { valor: 'descricao-asc', rotulo: 'Descrição (A→Z)' },
  { valor: 'descricao-desc', rotulo: 'Descrição (Z→A)' },
  { valor: 'qtd-desc', rotulo: 'Qtd (maior)' },
  { valor: 'qtd-asc', rotulo: 'Qtd (menor)' },
]

const CLASSES_DE_COLUNA = 'px-3 py-2 text-left font-medium text-tinta'

function horaDe(momento: Date): string {
  return momento.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
}

/**
 * `/panorama` — toda Peça em aberto, uma por linha: Desenho, Pedido, código, descrição, quantidade, prazo, onde está,
 * aferição e "Embalado". Tabela a partir de `lg`, cartões abaixo, com o mesmo dado e o mesmo filtro.
 *
 * A lista chega inteira, na ordem do servidor, e tudo se aplica no cliente: as facetas (`FiltroDeDemanda`), a
 * busca, o intervalo do prazo e a ordem. A vista inteira mora na URL, com `replace` (marcar, digitar e ordenar não
 * criam entrada de histórico), e cada parte é apagada quando volta ao vazio; a ordem original não é escrita, e
 * uma ordem desconhecida lida da URL vale a original. A tela se atualiza sozinha: só a primeira carga mostra o
 * `EstadoCarregando`, e a recarga periódica troca os dados sem desmontar a lista, para a rolagem e o filtro ficarem.
 */
export function PanoramaPage() {
  const { dados, carregando, erro, recarregar } = useCargaPeriodica(
    buscarPanorama, 'panorama', INTERVALO_DA_EXECUCAO_MS, 'Não foi possível carregar o Panorama.',
  )
  const { selecao, mudarSelecao } = useSelecaoNaUrl(CHAVES_DO_PANORAMA)
  const [params, setParams] = useSearchParams()
  const busca = params.get('busca') ?? ''
  const de = params.get('de') ?? ''
  const ate = params.get('ate') ?? ''
  const textoDaOrdem = params.get('ordem')
  const ordem = useMemo(() => ordemDaUrl(textoDaOrdem), [textoDaOrdem])

  // A hora da última resposta boa: cada `dados` novo é uma carga que deu certo.
  const [atualizadoEm, setAtualizadoEm] = useState<Date | null>(null)
  useEffect(() => { if (dados !== null) setAtualizadoEm(new Date()) }, [dados])

  /** Escreve na URL só as chaves dadas, apagando a que vem vazia; as demais ficam como estão. */
  const escrever = useCallback((mudancas: Record<string, string | null>) => {
    setParams((anterior) => {
      const proxima = new URLSearchParams(anterior)
      for (const [chave, valor] of Object.entries(mudancas)) {
        if (valor) proxima.set(chave, valor)
        else proxima.delete(chave)
      }
      return proxima
    }, { replace: true })
  }, [setParams])

  const mudarOrdem = (proxima: OrdemDoPanorama) => escrever({ ordem: ordemParaUrl(proxima) })

  // Uma chamada só: o `setParams` de função não enfileira, e duas seguidas fariam a segunda desfazer a primeira.
  function limparTudo() {
    setParams((anterior) => {
      const proxima = new URLSearchParams(anterior)
      for (const chave of [...CHAVES_DO_PANORAMA, 'busca', 'de', 'ate']) proxima.delete(chave)
      return proxima
    }, { replace: true })
  }

  const pecas = dados?.pecas ?? null
  const facetas = useMemo(() => (pecas ? facetasDoPanorama(pecas, selecao) : []), [pecas, selecao])
  const visiveis = useMemo(
    () => (pecas ? ordenarPanorama(filtrarPanorama(pecas, { selecao, busca, de, ate }), ordem) : []),
    [pecas, selecao, busca, de, ate, ordem],
  )

  const vazia = pecas !== null && pecas.length === 0
  // Vem de `visiveis`, e não de a faceta ainda listar a opção: a opção marcada pode ter sumido da lista numa
  // recarga e continuar filtrando.
  const vaziaPeloFiltro = pecas !== null && pecas.length > 0 && visiveis.length === 0

  return (
    <Pagina titulo="Panorama" largura="ampla">
      <p className="text-sm text-tinta-fraca">
        Toda Peça em aberto.{atualizadoEm && ` Atualizado às ${horaDe(atualizadoEm)}`}
      </p>
      <BannerDeErro mensagem={erro} />
      {pecas !== null && pecas.length > 0 && (
        <div className="flex flex-col gap-4">
          <FiltroDeDemanda facetas={facetas} selecao={selecao} aoMudar={mudarSelecao} />
          <div className="flex flex-wrap items-end gap-4">
            <Campo rotulo="Buscar">
              {(id) => (
                <input
                  id={id}
                  type="search"
                  value={busca}
                  onChange={(e) => escrever({ busca: e.target.value })}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
            <IntervaloDeDatas rotulo="Prazo de entrega" de={de} ate={ate} aoMudar={(v) => escrever(v)} />
            <div className="lg:hidden">
              <SeletorDeOrdem
                opcoes={OPCOES_DE_ORDEM}
                valor={ordemParaUrl(ordem) ?? ORIGINAL}
                aoMudar={(valor) => mudarOrdem(valor === ORIGINAL ? null : ordemDaUrl(valor))}
              />
            </div>
          </div>
        </div>
      )}
      {carregando && <EstadoCarregando />}
      {vazia && <EstadoVazio titulo="Nenhuma Peça em aberto." />}
      {vaziaPeloFiltro && (
        <EstadoVazio
          titulo="Nenhuma Peça casa com o filtro."
          acao={<Botao variante="secundario" onClick={limparTudo}>Limpar filtros</Botao>}
        />
      )}
      {visiveis.length > 0 && (
        <>
          {/*
            Sem `overflow` aqui, de propósito: um ancestral com `overflow` vira o contêiner do `sticky` e o prende a
            ele, e o cabeçalho deixaria de acompanhar a rolagem da página. A largura vem da `Pagina` ampla. O fundo
            é o da página, para as linhas não aparecerem por baixo, e o `z-10` fica acima da miniatura e das pílulas.
          */}
          <div className="hidden lg:block">
            <table className="w-full border-collapse text-left">
              <thead className="sticky top-0 z-10 bg-fundo">
                <tr className="border-b border-borda">
                  <th scope="col" className={CLASSES_DE_COLUNA}>Desenho</th>
                  <CabecalhoOrdenavel coluna="pedido" rotulo="Pedido"ordem={ordem} aoMudar={mudarOrdem} />
                  <CabecalhoOrdenavel coluna="codigo" rotulo="Código" ordem={ordem} aoMudar={mudarOrdem} />
                  <CabecalhoOrdenavel coluna="descricao" rotulo="Descrição" ordem={ordem} aoMudar={mudarOrdem} />
                  <CabecalhoOrdenavel coluna="qtd" rotulo="Qtd" ordem={ordem} aoMudar={mudarOrdem} />
                  <CabecalhoOrdenavel coluna="prazo" rotulo="Prazo" ordem={ordem} aoMudar={mudarOrdem} />
                  <th scope="col" className={CLASSES_DE_COLUNA}>Onde está</th>
                  <th scope="col" className={CLASSES_DE_COLUNA}>Aferição</th>
                  <th scope="col" className={CLASSES_DE_COLUNA}>Embalado</th>
                </tr>
              </thead>
              <tbody>
                {visiveis.map((peca) => (
                  <LinhaDoPanorama key={peca.estruturaItemId} peca={peca} aoGravar={() => { void recarregar() }} />
                ))}
              </tbody>
            </table>
          </div>
          <ul aria-label="Peças em aberto" className="flex flex-col gap-3 lg:hidden">
            {visiveis.map((peca) => (
              <CartaoDoPanorama key={peca.estruturaItemId} peca={peca} aoGravar={() => { void recarregar() }} />
            ))}
          </ul>
        </>
      )}
    </Pagina>
  )
}
