import { useRef, useState, type FormEvent } from 'react'
import { separar, type PendenciaDeSeparacaoDto } from '../api/separacao'
import { ehConflito } from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { rotuloDoNo } from '../execucao/formatacao'
import { camposDoNo, camposInvalidos, dicaDoCampo, linhasDosCampos } from './loteDaSeparacao'

interface Props {
  pendencia: PendenciaDeSeparacaoDto
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega a lista. */
  aoSalvar: () => void
}

/**
 * "Separar material" de UM nó da lista do Separador (spec da Fase 4, seção 6.1): um campo por material
 * pendente, preenchido com a pendência (D17 — o caso comum é um toque só), e um `POST` só com o que não
 * ficou vazio nem zero, tudo ou nada no servidor.
 *
 * Passar da pendência não é erro: o campo avisa "acima do planejado em …", como texto neutro, e envia
 * assim mesmo (D9). O erro de escrita mora aqui dentro. No 409 (o material saiu do plano com o painel
 * aberto, `MaterialForaDoPlanejado`) a frase do servidor fica e a lista recarrega; o campo do material
 * que saiu some, porque os campos seguem a `pendencia` que chega, e o que já foi digitado nos outros fica.
 *
 * O `enviandoRef` guarda a função inteira contra o segundo toque no mesmo quadro, antes de o React
 * redesenhar o botão desabilitado, como no `FormularioDeQuantidade`; o Enter num campo também chega aqui.
 */
export function PainelDeSeparacao({ pendencia, aoFechar, aoSalvar }: Props) {
  // Só o que o Separador mexeu; o campo intocado mostra a pendência ATUAL do material.
  const [textos, setTextos] = useState<Record<number, string>>({})
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const enviandoRef = useRef(false)

  const campos = camposDoNo(pendencia, textos)
  const invalido = camposInvalidos(campos)
  const linhas = linhasDosCampos(campos)

  async function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (enviandoRef.current || invalido) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await separar(pendencia.no.id, linhas)
      aoSalvar()
      aoFechar()
    } catch (falha) {
      setErro(mensagemDeErro(falha, 'Não foi possível registrar a separação.'))
      if (ehConflito(falha)) aoSalvar()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return (
    <PainelDeEscrita
      titulo="Separar material"
      subtitulo={rotuloDoNo(pendencia.no)}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      {campos.map(({ material: m, texto, leitura }) => (
        <Campo key={m.materialId} rotulo={`${m.codigo} ${m.descricao} (${m.unidadeMedida})`} dica={dicaDoCampo(m, leitura)}>
          {(id, idDaDica) => (
            <input
              id={id}
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={texto}
              onChange={(ev) => {
                const valor = ev.target.value
                setTextos((atuais) => ({ ...atuais, [m.materialId]: valor }))
              }}
              aria-describedby={idDaDica}
              aria-invalid={leitura.tipo === 'erro'}
              disabled={enviando}
              className={CLASSES_DE_CONTROLE}
            />
          )}
        </Campo>
      ))}
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={invalido}
        className="self-start"
      >
        Registrar separação
      </Botao>
    </PainelDeEscrita>
  )
}
