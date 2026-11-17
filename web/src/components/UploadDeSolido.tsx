import { useState, type ChangeEvent } from 'react'
import { apiFetch } from '../api/client'
import { TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import { BannerDeErro } from './BannerDeErro'
import { Botao } from './Botao'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'
import { formatarTamanho } from './tamanhoDeArquivo'
import { desenharMiniatura } from '../solido/miniatura'

interface Props {
  /** Caminho do binário a baixar, SEM o prefixo `/api` (ex.: `caminhoDoSolido(id)`). */
  caminho: string
  /** Faz o envio do arquivo e da miniatura (`null` quando não foi possível desenhá-la); rejeitar mostra
      o erro no banner. O componente valida o tamanho e desenha a miniatura. */
  enviar: (arquivo: File, miniatura: Blob | null) => Promise<void>
  /** Trava o campo de arquivo (quem chama tem uma escrita em voo, ou o alvo não aceita upload). */
  desabilitado?: boolean
  temSolido: boolean
  nomeDoSolido: string | null
  tamanhoDoSolidoEmBytes: number | null
  /** Chamado depois de um envio com sucesso — a tela precisa reler o componente, senão
      `temSolido`/nome/tamanho continuam velhos e a interface mente. */
  aoEnviar: () => void
}

/** "16 MiB", derivado da constante — o número do limite não é escrito à mão em nenhum texto da tela. */
const LIMITE_LEGIVEL = `${TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES / (1024 * 1024)} MiB`

const MENSAGEM_DE_ARQUIVO_GRANDE_DEMAIS =
  `O arquivo passa do limite de ${LIMITE_LEGIVEL} do sólido. Exporte o STL com uma malha menos `
  + 'detalhada e envie de novo.'

/** C13: a miniatura é acessório; sem ela o sólido segue enviado, e o aviso é neutro. */
const AVISO_SEM_MINIATURA = 'O sólido foi enviado, mas a miniatura não pôde ser gerada neste aparelho.'

const FALLBACK_DO_ENVIO = `Não foi possível enviar o sólido. Envie um arquivo .stl de até ${LIMITE_LEGIVEL}.`

/**
 * Upload (e substituição) do sólido STL de um Componente — Task 6 da Fase 2B. Visível só sob
 * `usePodeEscrever('componentes')` (§7.1 da spec, no chamador): quem não escreve vê o sólido pelo
 * `VisualizadorDeSolido` (Task 7), não por aqui.
 */
export function UploadDeSolido({
  caminho, enviar, desabilitado = false, temSolido, nomeDoSolido, tamanhoDoSolidoEmBytes, aoEnviar,
}: Props) {
  const [enviando, setEnviando] = useState(false)
  const [fase, setFase] = useState<'gerando' | 'enviando'>('enviando')
  const [semMiniatura, setSemMiniatura] = useState(false)
  // A frase pronta, e não o erro cru: o envio tem duas origens de recusa — o tamanho, checado aqui
  // antes de enviar, e a falha da requisição, traduzida por `mensagemDeErro` —, e as duas saem pelo
  // mesmo banner.
  const [erroEnvio, setErroEnvio] = useState<string | null>(null)
  const [baixando, setBaixando] = useState(false)
  const [erroDownload, setErroDownload] = useState<unknown>(null)

  async function aoEscolherArquivo(e: ChangeEvent<HTMLInputElement>) {
    const arquivo = e.target.files?.[0]
    // Zera o valor do input mesmo sem arquivo escolhido (usuário cancelou o seletor) — sem isto,
    // escolher DE NOVO o mesmo arquivo (para tentar de novo depois de um erro) não dispara
    // `onChange`, porque o valor do input não mudou.
    e.target.value = ''
    if (!arquivo) return

    // Antes de qualquer requisição. `>` e não `>=`: o backend aceita o limite exato.
    if (arquivo.size > TAMANHO_MAXIMO_DO_SOLIDO_EM_BYTES) {
      setErroEnvio(MENSAGEM_DE_ARQUIVO_GRANDE_DEMAIS)
      return
    }

    setEnviando(true)
    setErroEnvio(null)
    setSemMiniatura(false)
    try {
      setFase('gerando')
      const miniatura = await desenharMiniatura(await arquivo.arrayBuffer())
      setFase('enviando')
      await enviar(arquivo, miniatura)
      if (miniatura === null) setSemMiniatura(true)
      aoEnviar()
    } catch (erro) {
      setErroEnvio(mensagemDeErro(erro, FALLBACK_DO_ENVIO))
    } finally {
      setEnviando(false)
    }
  }

  // DECISÃO: baixar é um Botao, não um <a href> cru — um <a> cru não manda
  // `Authorization: Bearer` e o endpoint responderia 401. O botão busca pelo `apiFetch`, cria um
  // object URL em memória e dispara o download por um <a download> criado (e nunca renderizado)
  // na hora, revogando o object URL logo depois.
  async function aoBaixar() {
    setBaixando(true)
    setErroDownload(null)
    try {
      const resp = await apiFetch(caminho)
      if (!resp.ok) throw new ErroDeApi(resp.status, `Falha ao baixar o sólido (${resp.status}).`)
      const blob = await resp.blob()
      const url = URL.createObjectURL(blob)
      try {
        const link = document.createElement('a')
        link.href = url
        link.download = nomeDoSolido ?? 'solido.stl'
        link.click()
      } finally {
        URL.revokeObjectURL(url)
      }
    } catch (erro) {
      setErroDownload(erro)
    } finally {
      setBaixando(false)
    }
  }

  return (
    <div className="flex flex-col gap-3 rounded-lg border border-borda bg-superficie p-4">
      <Campo rotulo="Sólido (.stl)">
        {(idDoCampo) => (
          <input
            id={idDoCampo}
            type="file"
            accept=".stl"
            disabled={enviando || desabilitado}
            onChange={aoEscolherArquivo}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>

      {enviando && (
        <p role="status" className="text-tinta-fraca">{fase === 'gerando' ? 'Gerando miniatura…' : 'Enviando…'}</p>
      )}
      {!enviando && semMiniatura && <p role="status" className="text-tinta-fraca">{AVISO_SEM_MINIATURA}</p>}
      <BannerDeErro mensagem={erroEnvio} />

      {!temSolido && <p className="text-tinta-fraca">Sem sólido.</p>}
      {temSolido && (
        <div className="flex flex-wrap items-center gap-3">
          <p className="text-tinta">
            {nomeDoSolido ?? 'Sólido do catálogo'}
            {tamanhoDoSolidoEmBytes !== null && (
              <>
                {' '}
                <span className="text-tinta-fraca">({formatarTamanho(tamanhoDoSolidoEmBytes)})</span>
              </>
            )}
          </p>
          <p className="text-tinta-fraca">Substituir: escolha outro arquivo acima.</p>
          <Botao
            variante="secundario"
            onClick={aoBaixar}
            disabled={baixando}
            carregando={baixando}
            rotuloCarregando="Baixando…"
          >
            Baixar
          </Botao>
        </div>
      )}
      <BannerDeErro
        mensagem={erroDownload == null ? null : mensagemDeErro(erroDownload, 'Não foi possível baixar o sólido.')}
      />
    </div>
  )
}
