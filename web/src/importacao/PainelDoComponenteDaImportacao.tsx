import { useEffect, useRef, useState } from 'react'
import { caminhoDoSolido, type ComponenteDto, type TipoDeComponente } from '../api/cadastros'
import {
  alterarComponente, caminhoDoSolidoPendente, enviarSolidoPendente,
  type EscolhaDeReceita, type ImportacaoDto, type NoDaImportacaoDto, type SituacaoDoComponenteDto,
} from '../api/importacao'
import { usePodeEscrever } from '../auth/usePermissao'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { Pilula } from '../components/Pilula'
import { SeletorComBusca } from '../components/SeletorComBusca'
import { UploadDeSolido } from '../components/UploadDeSolido'
import { VisualizadorDeSolido } from '../components/VisualizadorDeSolido'
import { ComparativoDeReceita } from './ComparativoDeReceita'

/** A escrita do rascunho da tela: roda a ação, põe a resposta no estado e trata o 409 de versão velha. */
export type EscreverNoRascunho = (acao: () => Promise<ImportacaoDto>) => Promise<void>

interface Props {
  importacao: ImportacaoDto
  /** O registro do rascunho selecionado; `null` quando o nó veio só da receita do catálogo. */
  registroId: number | null
  componenteId: number | null
  escrever: EscreverNoRascunho
  /** Há uma escrita em voo na tela: o painel não manda outra por cima da versão que ela gasta. */
  desabilitado?: boolean
}

const TIPOS: TipoDeComponente[] = ['Bruto', 'Fabricado', 'Montagem']

const TEXTO_DO_NO_DO_CATALOGO = 'Este item vem da receita do catálogo; o sólido se envia no cadastro dele.'

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
 * O painel não guarda estado de servidor: cada escrita passa por `escrever`, que devolve o rascunho
 * inteiro à tela, e o que ele mostra sai sempre do `ImportacaoDto` que recebe.
 */
export function PainelDoComponenteDaImportacao({
  importacao, registroId, componenteId, escrever, desabilitado = false,
}: Props) {
  const podeEscrever = usePodeEscrever('estrutura')
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

  /** Escreve o registro com o casamento atual, trocando só o que `mudar` traz. */
  function gravar(s: SituacaoDoComponenteDto, alteracao: {
    componenteId: number | null
    codigoNovo: string | null
    descricaoNova: string | null
    tipoNovo: string | null
    escolhaDeReceita: EscolhaDeReceita | null
  }) {
    return escrever(() => alterarComponente(importacao.id, s.registroId, importacao.versao, alteracao))
  }

  // Trocar o casamento zera a escolha de receita: ela era sobre a
  // receita do casamento antigo, e o servidor recusa a escolha que viaja com a troca. A escolha nova
  // é uma escrita à parte, depois.
  function casarCom(s: SituacaoDoComponenteDto, c: ComponenteDto) {
    if (c.id === s.componenteId) return
    void gravar(s, { componenteId: c.id, codigoNovo: null, descricaoNova: null, tipoNovo: null, escolhaDeReceita: null })
  }

  function criarNovo(s: SituacaoDoComponenteDto) {
    void gravar(s, { componenteId: null, codigoNovo: null, descricaoNova: null, tipoNovo: null, escolhaDeReceita: null })
  }

  function escolherReceita(s: SituacaoDoComponenteDto, escolha: EscolhaDeReceita) {
    void gravar(s, {
      componenteId: s.componenteId, codigoNovo: s.codigoNovo, descricaoNova: s.descricaoNova,
      tipoNovo: s.tipoNovo, escolhaDeReceita: escolha,
    })
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
      className="rounded-lg border border-borda bg-superficie p-4 md:sticky md:top-0 md:z-10 md:max-h-[85vh] md:overflow-y-auto"
    >
      {!no && !situacao ? (
        <p className="text-tinta-fraca">Nenhum componente para mostrar.</p>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
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
                  enviar={(arquivo) => escrever(
                    () => enviarSolidoPendente(importacao.id, registroId, importacao.versao, arquivo),
                  )}
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
              <div className="flex flex-col gap-3">
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
                    desabilitado={desabilitado}
                    gravar={(parte) => gravar(situacao, {
                      componenteId: null,
                      codigoNovo: parte.codigoNovo ?? situacao.codigoNovo,
                      descricaoNova: parte.descricaoNova ?? situacao.descricaoNova,
                      tipoNovo: parte.tipoNovo ?? situacao.tipoNovo,
                      escolhaDeReceita: situacao.escolhaDeReceita,
                    })}
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
                    Usar a importada grava a receita do BOM no catálogo, no lugar da de hoje.
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
 * tiraria o foco e o digitado dele. Cada campo ressincroniza com o servidor só quando não está focado.
 */
function CamposDoNovo({ situacao, desabilitado, gravar }: {
  situacao: SituacaoDoComponenteDto
  desabilitado: boolean
  gravar: (parte: { codigoNovo?: string; descricaoNova?: string; tipoNovo?: string }) => void
}) {
  return (
    <div className="flex flex-col gap-3">
      <CampoDeTexto
        rotulo="Código"
        mono
        valorDoServidor={situacao.codigoNovo ?? ''}
        aoGravar={(texto) => gravar({ codigoNovo: texto })}
      />
      <CampoDeTexto
        rotulo="Descrição"
        valorDoServidor={situacao.descricaoNova ?? ''}
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
            {TIPOS.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        )}
      </Campo>
    </div>
  )
}

function CampoDeTexto({ rotulo, valorDoServidor, aoGravar, mono = false }: {
  rotulo: string
  valorDoServidor: string
  aoGravar: (texto: string) => void
  mono?: boolean
}) {
  const [texto, setTexto] = useState(valorDoServidor)
  const focado = useRef(false)

  // O servidor responde depois de a pessoa ter ido para o campo seguinte: o campo que ela deixou
  // aceita o valor novo, o que ela está digitando não.
  useEffect(() => {
    if (!focado.current) setTexto(valorDoServidor)
  }, [valorDoServidor])

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
