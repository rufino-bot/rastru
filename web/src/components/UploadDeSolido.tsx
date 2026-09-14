import { useState, type ChangeEvent } from 'react'
import { apiFetch } from '../api/client'
import { caminhoDoSolido, enviarSolido } from '../api/cadastros'
import { ErroDeApi, mensagemDeErro } from '../api/erros'
import { BannerDeErro } from './BannerDeErro'
import { Botao } from './Botao'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'

interface Props {
  componenteId: number
  temSolido: boolean
  nomeDoSolido: string | null
  tamanhoDoSolidoEmBytes: number | null
  /** Chamado depois de um envio com sucesso — a tela precisa reler o componente, senão
      `temSolido`/nome/tamanho continuam velhos e a interface mente. */
  aoEnviar: () => void
}

/**
 * Abaixo de 1024 bytes, "N bytes"; abaixo de 1 MiB, KiB com uma casa; acima, MiB com uma casa —
 * sempre com `toLocaleString('pt-BR', …)`, para a vírgula decimal.
 */
function formatarTamanho(bytes: number): string {
  if (bytes < 1024) return `${bytes.toLocaleString('pt-BR')} bytes`
  const kib = bytes / 1024
  if (kib < 1024) {
    return `${kib.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} KiB`
  }
  const mib = kib / 1024
  return `${mib.toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} MiB`
}

/**
 * Upload (e substituição) do sólido STL de um Componente — Task 6 da Fase 2B. Visível só sob
 * `usePodeEscrever('componentes')` (§7.1 da spec, no chamador): quem não escreve vê o sólido pelo
 * `VisualizadorDeSolido` (Task 7), não por aqui.
 */
export function UploadDeSolido({
  componenteId, temSolido, nomeDoSolido, tamanhoDoSolidoEmBytes, aoEnviar,
}: Props) {
  const [enviando, setEnviando] = useState(false)
  const [erroEnvio, setErroEnvio] = useState<unknown>(null)
  const [baixando, setBaixando] = useState(false)
  const [erroDownload, setErroDownload] = useState<unknown>(null)

  async function aoEscolherArquivo(e: ChangeEvent<HTMLInputElement>) {
    const arquivo = e.target.files?.[0]
    // Zera o valor do input mesmo sem arquivo escolhido (usuário cancelou o seletor) — sem isto,
    // escolher DE NOVO o mesmo arquivo (para tentar de novo depois de um erro) não dispara
    // `onChange`, porque o valor do input não mudou.
    e.target.value = ''
    if (!arquivo) return

    setEnviando(true)
    setErroEnvio(null)
    try {
      await enviarSolido(componenteId, arquivo)
      aoEnviar()
    } catch (erro) {
      setErroEnvio(erro)
    } finally {
      setEnviando(false)
    }
  }

  // DECISÃO (item 3 da caixa de correção do brief): baixar é um Botao, não um <a href> cru — um
  // <a> cru não manda `Authorization: Bearer` e o endpoint responderia 401. O botão busca pelo
  // `apiFetch`, cria um object URL em memória e dispara o download por um <a download> criado (e
  // nunca renderizado) na hora, revogando o object URL logo depois.
  async function aoBaixar() {
    setBaixando(true)
    setErroDownload(null)
    try {
      const resp = await apiFetch(caminhoDoSolido(componenteId))
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
            disabled={enviando}
            onChange={aoEscolherArquivo}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>

      {enviando && <p role="status" className="text-tinta-fraca">Enviando…</p>}
      <BannerDeErro
        mensagem={erroEnvio == null
          ? null
          : mensagemDeErro(erroEnvio, 'Não foi possível enviar o sólido. Envie um arquivo .stl de até 16 MiB.')}
      />

      {!temSolido && <p className="text-tinta-fraca">Sem sólido.</p>}
      {temSolido && (
        <div className="flex flex-wrap items-center gap-3">
          <p className="text-tinta">
            {nomeDoSolido}
            {' '}
            <span className="text-tinta-fraca">
              ({tamanhoDoSolidoEmBytes === null ? '' : formatarTamanho(tamanhoDoSolidoEmBytes)})
            </span>
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
