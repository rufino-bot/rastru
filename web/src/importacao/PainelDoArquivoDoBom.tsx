import { useRef, useState } from 'react'
import type { ChangeEvent, FormEvent, ReactNode } from 'react'
import { mensagemDeErro } from '../api/erros'
import { ErroDeBom, LIMITE_DO_BOM_LEGIVEL, TAMANHO_MAXIMO_DO_BOM_EM_BYTES } from '../api/importacao'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'

interface Props {
  /** O nome da ação ("Importar BOM", "Reimportar BOM"): o `<h2>` e o nome acessível do `<form>`. */
  titulo: string
  subtitulo: ReactNode
  /** O texto do botão de envio e o dele enquanto o envio está em voo. */
  rotuloDoEnvio: string
  rotuloEnviando: string
  /** A frase da falha que não tem explicação melhor ("Não foi possível importar o BOM…"). */
  fallbackDoErro: string
  /**
   * Faz o envio. A promessa **rejeita** na falha, e o painel decide o que mostrar: `ErroDeBom` vira
   * a lista de linhas, e o resto vira `mensagemDeErro(erro, fallbackDoErro)`. Quem fecha o painel
   * no sucesso é a tela, que o desmonta.
   */
  aoEnviar: (arquivo: File) => Promise<void>
  aoFechar: () => void
  /**
   * A frase própria da tela para uma falha que ela conhece (a versão velha do rascunho), ou `null`
   * para deixar o painel decidir. É lida antes do `ErroDeBom` e do `mensagemDeErro`.
   */
  mensagemDoErro?: (erro: unknown) => string | null
}

/**
 * O painel de escrita que recebe o arquivo do BOM, compartilhado pelo "Importar BOM" do Agrupamento
 * e pelo "Reimportar BOM" da conferência: os dois pedem o mesmo campo, o mesmo limite de tamanho e
 * o mesmo tratamento do arquivo recusado.
 *
 * O arquivo só entra no estado depois de passar do limite de tamanho (a recusa vira erro dentro do
 * painel), e **qualquer** falha do envio o descarta: o usuário o escolhe de novo, porque o conteúdo
 * pode ter mudado em disco e reenviar o `File` velho às cegas não é o que ele quer.
 */
export function PainelDoArquivoDoBom({
  titulo, subtitulo, rotuloDoEnvio, rotuloEnviando, fallbackDoErro, aoEnviar, aoFechar, mensagemDoErro,
}: Props) {
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [linhas, setLinhas] = useState<string[]>([])
  const campo = useRef<HTMLInputElement>(null)

  function escolher(e: ChangeEvent<HTMLInputElement>) {
    const escolhido = e.target.files?.[0] ?? null
    // Zera o valor do campo (o File já está em `escolhido`): sem isso, escolher DE NOVO o mesmo
    // caminho, depois de corrigir a planilha, não dispara `onChange` e o File velho seguiria em
    // estado. Mesmo cuidado de `UploadDeSolido`.
    e.target.value = ''
    setErro(null)
    setLinhas([])
    // Antes de qualquer requisição, e `>` e não `>=`: o backend aceita o limite exato. Acima dele o
    // servidor fecha a conexão com o corpo subindo e o `fetch` rejeitaria sem resposta, então a
    // frase certa só pode vir daqui.
    if (escolhido && escolhido.size > TAMANHO_MAXIMO_DO_BOM_EM_BYTES) {
      setArquivo(null)
      setErro(
        `O arquivo passa do limite de ${LIMITE_DO_BOM_LEGIVEL} do BOM. Exporte só a tabela da lista de materiais e envie de novo.`,
      )
      return
    }
    setArquivo(escolhido)
  }

  async function enviar(e: FormEvent) {
    e.preventDefault()
    if (!arquivo) return
    setErro(null)
    setLinhas([])
    setEnviando(true)
    try {
      await aoEnviar(arquivo)
    } catch (falha) {
      setArquivo(null)
      if (campo.current) campo.current.value = ''
      const daTela = mensagemDoErro?.(falha) ?? null
      if (daTela !== null) {
        setErro(daTela)
      } else if (falha instanceof ErroDeBom) {
        // O texto do servidor é ASCII sem acento: o título é nosso, e as linhas vão como vieram.
        setErro('O arquivo tem problemas:')
        setLinhas(falha.linhas)
      } else {
        setErro(mensagemDeErro(falha, fallbackDoErro))
      }
    } finally {
      setEnviando(false)
    }
  }

  return (
    <PainelDeEscrita
      titulo={titulo}
      subtitulo={subtitulo}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <Campo rotulo="Arquivo do BOM (.xlsx ou .csv)" dica={`Até ${LIMITE_DO_BOM_LEGIVEL}.`}>
        {(idDoCampo, idDaDica) => (
          <input
            id={idDoCampo}
            ref={campo}
            type="file"
            accept=".xlsx,.csv"
            disabled={enviando}
            onChange={escolher}
            aria-describedby={idDaDica}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <BannerDeErro mensagem={erro} linhas={linhas} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando={rotuloEnviando}
        disabled={!arquivo}
        className="self-start"
      >
        {rotuloDoEnvio}
      </Botao>
    </PainelDeEscrita>
  )
}
