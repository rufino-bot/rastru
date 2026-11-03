import { useRef, useState } from 'react'
import { ehConflito } from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'

/**
 * O que os cinco painéis do cartão da Peça (Aferir, Contar, Mandar para conserto, Pedir liberação, Expedir remessa)
 * fazem igual ao gravar, no molde do `PainelDeSeparacao`:
 *
 * - o `ref` guarda a função inteira contra o segundo toque no mesmo quadro, antes de o React redesenhar o botão
 *   desabilitado (o Enter num campo também chega aqui), e `enviando` trava o submit e o `Cancelar`;
 * - no sucesso, `aoConcluir` (quem abriu recarrega) e `aoFechar`;
 * - na recusa, a frase do servidor fica dentro do painel, com o digitado; no 409 (a tela estava velha: outra pessoa
 *   mexeu na Peça com o painel aberto) quem abriu recarrega também, e o painel continua aberto.
 */
export function useEscritaDoPainel(aoConcluir: () => void, aoFechar: () => void, falhaPadrao: string) {
  const [enviando, setEnviando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const enviandoRef = useRef(false)

  async function escrever(acao: () => Promise<unknown>) {
    if (enviandoRef.current) return
    enviandoRef.current = true
    setEnviando(true)
    setErro(null)
    try {
      await acao()
      aoConcluir()
      aoFechar()
    } catch (falha) {
      setErro(mensagemDeErro(falha, falhaPadrao))
      if (ehConflito(falha)) aoConcluir()
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return { enviando, erro, escrever }
}

interface PropsDoCampo {
  rotulo: string
  texto: string
  /** A leitura de `texto`; o erro dela vira a dica do campo e o `aria-invalid`. */
  erro: string | null
  aoMudar: (texto: string) => void
  desabilitado: boolean
  /** O nome acessível, quando o rótulo visível não basta para distinguir o campo na tela (o lote). */
  nomeAcessivel?: string
}

/**
 * O campo de quantidade dos painéis e do lote da expedição: texto com teclado decimal, porque o
 * `<input type="number">` recusa "2,5" em alguns navegadores (a leitura é a de `execucao/quantidade.ts`).
 */
export function CampoDeQuantidade({ rotulo, texto, erro, aoMudar, desabilitado, nomeAcessivel }: PropsDoCampo) {
  return (
    <Campo rotulo={rotulo} dica={erro ?? undefined}>
      {(id, idDaDica) => (
        <input
          id={id}
          type="text"
          inputMode="decimal"
          autoComplete="off"
          value={texto}
          onChange={(e) => aoMudar(e.target.value)}
          aria-label={nomeAcessivel}
          aria-describedby={idDaDica}
          aria-invalid={erro !== null}
          disabled={desabilitado}
          className={CLASSES_DE_CONTROLE}
        />
      )}
    </Campo>
  )
}
