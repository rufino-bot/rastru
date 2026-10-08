import { useEffect, useRef, useState } from 'react'
import { caminhoDoSolido, type ComponenteDto, type TipoDeComponente } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import {
  alterarComponente, AVISO_IMPORTACAO_DESATUALIZADA, caminhoDoSolidoPendente, ehImportacaoDesatualizada,
  enviarSolidoPendente,
  type AlteracaoDeComponente, type ImportacaoDto, type NoDaImportacaoDto, type SituacaoDoComponenteDto,
} from '../api/importacao'
import { usePodeEscrever } from '../auth/usePermissao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { Pilula } from '../components/Pilula'
import { SeletorComBusca } from '../components/SeletorComBusca'
import { UploadDeSolido } from '../components/UploadDeSolido'
import { VisualizadorDeSolido } from '../components/VisualizadorDeSolido'
import { ComparativoDeReceita } from './ComparativoDeReceita'

/**
 * A escrita do rascunho da tela: põe a ação na fila, roda-a com o rascunho mais recente (de onde ela
 * tira a versão e o estado do registro), põe a resposta no estado e trata o 409 de versão velha. Com
 * `noPainel`, a falha é relançada para quem a pediu mostrá-la.
 */
export type EscreverNoRascunho = (
  acao: (atual: ImportacaoDto) => Promise<ImportacaoDto>, noPainel?: boolean,
) => Promise<void>

interface Props {
  importacao: ImportacaoDto
  /** O registro do rascunho selecionado; `null` quando o nó veio só da receita do catálogo. */
  registroId: number | null
  componenteId: number | null
  escrever: EscreverNoRascunho
  /** Há escrita em voo ou na fila: as escolhas travam, e os textos esperam a fila para ressincronizar. */
  desabilitado?: boolean
  /** Muda ao fim de toda escrita da tela, com sucesso ou não: os textos voltam ao valor do servidor. */
  revisao?: number
}

const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

/**
 * Quanto tempo a recusa local do casamento fica na tela. Ela só diz que a escolha não foi aceita e o
 * que fazer, não pede resposta: some sozinha, ou antes, na próxima ação do usuário no painel.
 */
const TEMPO_DA_RECUSA_LOCAL_MS = 8_000

const TEXTO_DO_NO_DO_CATALOGO = 'Este item vem da receita do catálogo; o sólido se envia no cadastro dele.'

/** A frase da falha de uma escrita do painel: o 409 de versão velha tem a dele, o resto a do status. */
function fraseDaFalha(e: unknown): string {
  return ehImportacaoDesatualizada(e)
    ? AVISO_IMPORTACAO_DESATUALIZADA
    : mensagemDeErro(e, 'Não foi possível salvar a alteração.')
}

function acharNo(
  no: NoDaImportacaoDto, quando: (n: NoDaImportacaoDto) => boolean,
): NoDaImportacaoDto | undefined {
  if (quando(no)) return no
  for (const f of no.filhos) {
    const achado = acharNo(f, quando)
    if (achado) return achado
  }
  return undefined
}

function iguais(a: string | null, b: string | null): boolean {
  return (a ?? '').trim().toLowerCase() === (b ?? '').trim().toLowerCase()
}

function plural(n: number): string {
  return n === 1 ? 'item' : 'itens'
}

/**
 * O painel fixo da conferência: o Componente do nó selecionado, com o sólido (visualizar e enviar),
 * o casamento (casar com outro Componente ou criar novo) e, quando a receita do BOM diverge da do
 * catálogo, o comparativo e a escolha entre as duas.
 *
 * De `lg` para cima ele é a coluna lateral da tela, fixo (`sticky`) ao lado da árvore e com rolagem
 * própria; abaixo disso é um bloco comum no alto da tela, porque fixo no topo ele cobria a árvore. O
 * conteúdo fica numa coluna só, que é a largura da coluna lateral.
 *
 * O painel não guarda estado de servidor: cada escrita passa por `escrever`, que devolve o rascunho
 * inteiro à tela, e o que ele mostra sai sempre do `ImportacaoDto` que recebe.
 */
export function PainelDoComponenteDaImportacao({
  importacao, registroId, componenteId, escrever, desabilitado = false, revisao = 0,
}: Props) {
  const podeEscrever = usePodeEscrever('estrutura')
  // O erro da última escrita do painel, com o registro dela: trocar de nó não o leva junto.
  const [erro, setErro] = useState<{ registroId: number; mensagem: string } | null>(null)
  // A recusa local do casamento (o Componente já é de outro código), que não chega ao servidor. É
  // estado à parte do `erro` porque a vida dela é outra: some por tempo, na próxima ação do painel e
  // na troca de nó, e não volta ao reabrir o nó. O erro de escrita fica até a escrita seguinte.
  const [recusa, setRecusa] = useState<{ registroId: number; mensagem: string } | null>(null)

  // Cada recusa tem o próprio temporizador: o cleanup do efeito o limpa quando outra recusa a
  // substitui (o objeto novo refaz o efeito), quando a recusa some por outro caminho e na desmontagem.
  useEffect(() => {
    if (recusa === null) return
    const temporizador = setTimeout(() => setRecusa(null), TEMPO_DA_RECUSA_LOCAL_MS)
    return () => clearTimeout(temporizador)
  }, [recusa])

  // Trocar de nó descarta a recusa: ela é do nó que estava aberto, e não deve esperar por ele.
  useEffect(() => { setRecusa(null) }, [registroId, componenteId])
  const situacao = registroId === null
    ? undefined
    : importacao.componentes.find((c) => c.registroId === registroId)
  const no = importacao.raiz
    ? acharNo(importacao.raiz, (n) => (registroId !== null
      ? n.registroId === registroId
      : n.registroId === null && n.componenteId === componenteId))
    : undefined
  const travado = desabilitado || !podeEscrever

  // O sólido que o painel mostra: o pendente, quando existe, senão o do catálogo. O envio é sempre
  // para o pendente — o do catálogo só muda na confirmação.
  const doCatalogo = registroId === null
    ? importacao.componentes.find((c) => c.componenteId !== null && c.componenteId === componenteId)
    : undefined
  const temPendente = situacao?.temSolidoPendente ?? false
  const temSolidoNoCatalogo = situacao?.temSolido
    ?? doCatalogo?.temSolido
    ?? (no ? !no.pendencias.includes('SemSolido') : false)
  const temSolido = temPendente || temSolidoNoCatalogo
  const idDoComponenteDoSolido = situacao?.componenteId ?? componenteId
  const caminhoDoSolidoMostrado = temPendente && registroId !== null
    ? caminhoDoSolidoPendente(importacao.id, registroId)
    : idDoComponenteDoSolido !== null ? caminhoDoSolido(idDoComponenteDoSolido) : null
  const nomeDoSolido = situacao?.nomeDoSolido ?? doCatalogo?.nomeDoSolido ?? null
  const tamanhoDoSolido = situacao?.tamanhoDoSolidoEmBytes ?? doCatalogo?.tamanhoDoSolidoEmBytes ?? null

  /**
   * Escreve o registro. `montar` recebe o registro como ele está no rascunho mais recente, na hora em
   * que a escrita sai da fila — e não como estava quando foi pedida, que uma escrita anterior ainda em
   * voo pode ter mudado. A falha fica no painel.
   */
  async function gravar(id: number, montar: (s: SituacaoDoComponenteDto) => AlteracaoDeComponente) {
    setErro(null)
    setRecusa(null)
    try {
      await escrever((atual) => {
        const s = atual.componentes.find((c) => c.registroId === id)
        // O registro saiu do rascunho (um reimport na fila antes desta escrita): não há o que gravar.
        return s ? alterarComponente(atual.id, id, atual.versao, montar(s)) : Promise.resolve(atual)
      }, true)
    } catch (e) {
      setErro({ registroId: id, mensagem: fraseDaFalha(e) })
    }
  }

  // Trocar o casamento zera a escolha de receita: ela era sobre a
  // receita do casamento antigo, e o servidor recusa a escolha que viaja com a troca. A escolha nova
  // é uma escrita à parte, depois.
  function casarCom(s: SituacaoDoComponenteDto, c: ComponenteDto) {
    if (c.id === s.componenteId) return
    // A mesma regra do servidor: o catálogo tem UMA receita por Componente, e dois códigos casados
    // com ele se pisariam. Conferida aqui para o PCP ler por quê, em vez da recusa genérica.
    const outro = importacao.componentes.find((x) => x.registroId !== s.registroId && x.componenteId === c.id)
    if (outro) {
      const quem = outro.codigoLido === null ? 'outro código do BOM' : `o código ${outro.codigoLido} do BOM`
      setErro(null)
      setRecusa({
        registroId: s.registroId,
        mensagem: `${c.codigo} já está casado com ${quem}. `
          + 'Escolha outro Componente, ou case aquele código com outro antes.',
      })
      return
    }
    void gravar(s.registroId, () => ({
      componenteId: c.id, codigoNovo: null, descricaoNova: null, tipoNovo: null, escolhaDeReceita: null,
    }))
  }

  function criarNovo(s: SituacaoDoComponenteDto) {
    void gravar(s.registroId, () => ({
      componenteId: null, codigoNovo: null, descricaoNova: null, tipoNovo: null, escolhaDeReceita: null,
    }))
  }

  function escolherReceita(s: SituacaoDoComponenteDto, escolha: AlteracaoDeComponente['escolhaDeReceita']) {
    void gravar(s.registroId, (atual) => ({
      componenteId: atual.componenteId, codigoNovo: atual.codigoNovo, descricaoNova: atual.descricaoNova,
      tipoNovo: atual.tipoNovo, escolhaDeReceita: escolha,
    }))
  }

  /** O envio do STL: a falha volta ao `UploadDeSolido`, que a mostra no próprio campo. */
  async function enviarSolido(id: number, arquivo: File) {
    setRecusa(null)
    try {
      await escrever((atual) => enviarSolidoPendente(atual.id, id, atual.versao, arquivo), true)
    } catch (e) {
      throw ehImportacaoDesatualizada(e)
        ? new ErroDeApi(409, AVISO_IMPORTACAO_DESATUALIZADA, AVISO_IMPORTACAO_DESATUALIZADA)
        : e
    }
  }

  const casado = situacao !== undefined && situacao.componenteId !== null
  const codigo = casado ? situacao.codigoDoCatalogo : (situacao?.codigoNovo ?? situacao?.codigoLido ?? no?.codigo ?? '')
  const descricao = casado
    ? situacao.descricaoDoCatalogo
    : (situacao?.descricaoNova ?? situacao?.descricaoLida ?? no?.descricao ?? '')
  const chaveDoSolido = `${registroId ?? `c${componenteId}`}-${temPendente}-${nomeDoSolido}-${tamanhoDoSolido}`

  return (
    <section
      aria-label="Componente selecionado"
      className="rounded-lg border border-borda bg-superficie p-4 lg:sticky lg:top-4 lg:max-h-[calc(100dvh-2rem)] lg:overflow-y-auto"
    >
      {erro && erro.registroId === registroId && (
        <div className="mb-4"><BannerDeErro mensagem={erro.mensagem} /></div>
      )}
      {recusa && recusa.registroId === registroId && (
        <div className="mb-4"><BannerDeErro mensagem={recusa.mensagem} /></div>
      )}
      {!no && !situacao ? (
        <p className="text-tinta-fraca">Nenhum componente para mostrar.</p>
      ) : (
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-3">
            {caminhoDoSolidoMostrado !== null && temSolido && (
              <VisualizadorDeSolido key={chaveDoSolido} caminho={caminhoDoSolidoMostrado} />
            )}
            {registroId === null ? (
              <>
                <UploadDeSolido
                  key={`c${componenteId}`}
                  caminho={caminhoDoSolidoMostrado ?? ''}
                  enviar={async () => {}}
                  desabilitado
                  temSolido={temSolido}
                  nomeDoSolido={nomeDoSolido}
                  tamanhoDoSolidoEmBytes={tamanhoDoSolido}
                  aoEnviar={() => {}}
                />
                <p className="text-sm text-tinta-fraca">{TEXTO_DO_NO_DO_CATALOGO}</p>
              </>
            ) : (
              podeEscrever && (
                <UploadDeSolido
                  key={registroId}
                  caminho={caminhoDoSolidoMostrado ?? ''}
                  enviar={(arquivo) => enviarSolido(registroId, arquivo)}
                  desabilitado={desabilitado}
                  temSolido={temSolido}
                  nomeDoSolido={nomeDoSolido}
                  tamanhoDoSolidoEmBytes={tamanhoDoSolido}
                  aoEnviar={() => {}}
                />
              )
            )}
          </div>

          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-sm text-tinta-fraca">{codigo}</span>
                {registroId === null && <Pilula>Do catálogo</Pilula>}
                {situacao && !casado && <Pilula>Novo</Pilula>}
                {casado && situacao.tipo && <Pilula>{situacao.tipo}</Pilula>}
                {casado && situacao.ativo === false && <Pilula tom="atencao">Inativo</Pilula>}
              </div>
              <span className="text-tinta">{descricao}</span>
              {casado && situacao.codigoLido !== null && !iguais(situacao.codigoLido, situacao.codigoDoCatalogo) && (
                <span className="text-sm text-tinta-fraca">{`No BOM: código ${situacao.codigoLido}`}</span>
              )}
              {casado && !iguais(situacao.descricaoLida, situacao.descricaoDoCatalogo) && (
                <span className="text-sm text-tinta-fraca">{`No BOM: ${situacao.descricaoLida}`}</span>
              )}
              {casado && situacao.ativo === false && (
                <span className="text-sm text-tinta-fraca">Este Componente está inativo e é reativado na confirmação.</span>
              )}
            </div>

            {situacao && podeEscrever && (
              // `onInput` sobe de todo campo de texto deste bloco (a busca e os do Componente novo): digitar
              // é a próxima ação do usuário, e a recusa local já cumpriu o papel dela.
              <div className="flex flex-col gap-3" onInput={() => setRecusa(null)}>
                <SeletorComBusca
                  rotulo="Casar com outro Componente"
                  valorSelecionado={casado ? {
                    id: situacao.componenteId!,
                    codigo: situacao.codigoDoCatalogo ?? '',
                    descricao: situacao.descricaoDoCatalogo ?? '',
                    tipo: situacao.tipo ?? '',
                    ativo: situacao.ativo ?? true,
                    temSolido: situacao.temSolido,
                  } : null}
                  aoSelecionar={(c) => casarCom(situacao, c)}
                  desabilitado={desabilitado}
                />
                {casado ? (
                  <div>
                    <Botao variante="secundario" disabled={desabilitado} onClick={() => criarNovo(situacao)}>
                      Criar novo
                    </Botao>
                  </div>
                ) : (
                  <CamposDoNovo
                    key={situacao.registroId}
                    situacao={situacao}
                    temFilhos={(no?.filhos.length ?? 0) > 0}
                    desabilitado={desabilitado}
                    revisao={revisao}
                    gravar={(parte) => {
                      void gravar(situacao.registroId, (atual) => ({
                        componenteId: null,
                        codigoNovo: parte.codigoNovo ?? atual.codigoNovo,
                        descricaoNova: parte.descricaoNova ?? atual.descricaoNova,
                        tipoNovo: parte.tipoNovo ?? atual.tipoNovo,
                        escolhaDeReceita: atual.escolhaDeReceita,
                      }))
                    }}
                  />
                )}
              </div>
            )}

            {situacao?.divergente && (
              <div className="flex flex-col gap-3">
                <ComparativoDeReceita linhas={situacao.comparativo} />
                <fieldset className="flex flex-col gap-2">
                  <legend className="mb-1 text-sm font-medium text-tinta">Receita deste Componente</legend>
                  {([
                    ['Catalogo', 'Manter a receita do catálogo'],
                    ['Importada', 'Usar a receita importada'],
                  ] as const).map(([valor, rotulo]) => (
                    <label key={valor} className="flex items-center gap-2 text-tinta">
                      <input
                        type="radio"
                        name={`receita-${situacao.registroId}`}
                        checked={situacao.escolhaDeReceita === valor}
                        disabled={travado}
                        onChange={() => escolherReceita(situacao, valor)}
                        className="size-4 accent-acao"
                      />
                      <span>{rotulo}</span>
                    </label>
                  ))}
                  {situacao.efeitoDeManterCatalogo && (
                    <p className="text-sm text-tinta-fraca">
                      {`Manter a do catálogo retira ${situacao.efeitoDeManterCatalogo.retira} ${plural(situacao.efeitoDeManterCatalogo.retira)} do BOM `
                        + `e traz ${situacao.efeitoDeManterCatalogo.traz} do catálogo.`}
                    </p>
                  )}
                  <p className="text-sm text-tinta-fraca">
                    Usar a importada substitui a receita do catálogo pela do BOM.
                  </p>
                </fieldset>
              </div>
            )}
          </div>
        </div>
      )}
    </section>
  )
}

/**
 * Código, descrição e `Tipo` de um Componente que a confirmação vai criar. Os textos só escrevem ao
 * sair do campo (uma requisição por tecla gastaria a versão do rascunho a cada caractere) e só se
 * mudaram; o `Tipo` é uma escolha e escreve ao trocar.
 *
 * Os campos de texto **não** travam durante uma escrita e o formulário não remonta quando o servidor
 * responde: quem sai do Código com Tab já está digitando na Descrição, e travar ou remontar o campo
 * tiraria o foco e o digitado dele. Cada campo ressincroniza com o servidor quando não está focado e
 * a fila de escritas esvaziou — inclusive depois de uma escrita que falhou, para o texto não salvo não
 * ficar na tela como se estivesse.
 */
function CamposDoNovo({ situacao, temFilhos, desabilitado, revisao, gravar }: {
  situacao: SituacaoDoComponenteDto
  /** O código tem filhos no BOM: só uma folha passa a `Bruto`, que não exige sólido. */
  temFilhos: boolean
  desabilitado: boolean
  revisao: number
  gravar: (parte: { codigoNovo?: string; descricaoNova?: string; tipoNovo?: string }) => void
}) {
  // O `Bruto` que o reimport manteve num código que passou a ter filhos continua na lista: é o valor
  // atual, e o servidor só recusa quem PASSA a `Bruto` com filhos.
  const tipos = temFilhos && situacao.tipoNovo !== 'Bruto' ? TIPOS.filter((t) => t !== 'Bruto') : TIPOS
  return (
    <div className="flex flex-col gap-3">
      <CampoDeTexto
        rotulo="Código"
        mono
        valorDoServidor={situacao.codigoNovo ?? ''}
        revisao={revisao}
        ocupado={desabilitado}
        aoGravar={(texto) => gravar({ codigoNovo: texto })}
      />
      <CampoDeTexto
        rotulo="Descrição"
        valorDoServidor={situacao.descricaoNova ?? ''}
        revisao={revisao}
        ocupado={desabilitado}
        aoGravar={(texto) => gravar({ descricaoNova: texto })}
      />
      <Campo rotulo="Tipo">
        {(id) => (
          <select
            id={id}
            value={situacao.tipoNovo ?? ''}
            disabled={desabilitado}
            onChange={(e) => gravar({ tipoNovo: e.target.value })}
            className={CLASSES_DE_CONTROLE}
          >
            {situacao.tipoNovo === null && <option value="" disabled>Escolha o tipo</option>}
            {tipos.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        )}
      </Campo>
    </div>
  )
}

function CampoDeTexto({ rotulo, valorDoServidor, revisao, ocupado, aoGravar, mono = false }: {
  rotulo: string
  valorDoServidor: string
  /** Muda ao fim de toda escrita: com o valor do servidor igual, só ela traz o campo de volta. */
  revisao: number
  /** Há escrita na fila: o texto que o campo pediu para gravar ainda pode estar nela. */
  ocupado: boolean
  aoGravar: (texto: string) => void
  mono?: boolean
}) {
  const [texto, setTexto] = useState(valorDoServidor)
  const focado = useRef(false)

  // O servidor responde depois de a pessoa ter ido para o campo seguinte: o campo que ela deixou
  // aceita o valor novo, o que ela está digitando não. Com a fila ainda ocupada, o campo espera: o
  // texto dele pode ser a próxima escrita.
  useEffect(() => {
    if (!focado.current && !ocupado) setTexto(valorDoServidor)
  }, [valorDoServidor, revisao, ocupado])

  return (
    <Campo rotulo={rotulo}>
      {(id) => (
        <input
          id={id}
          type="text"
          value={texto}
          onChange={(e) => setTexto(e.target.value)}
          onFocus={() => { focado.current = true }}
          onBlur={() => {
            focado.current = false
            const limpo = texto.trim()
            if (limpo !== valorDoServidor) aoGravar(limpo)
          }}
          className={mono ? `${CLASSES_DE_CONTROLE} font-mono` : CLASSES_DE_CONTROLE}
        />
      )}
    </Campo>
  )
}
