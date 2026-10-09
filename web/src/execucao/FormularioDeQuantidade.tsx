import { useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { formatarQuantidade } from './formatacao'
import { lerConjuntos } from './conjuntos'
import { lerQuantidade, quantidadeParaCampo } from './quantidade'

interface Props {
  /** Rótulo do botão de confirmar: "Iniciar", "Terminar" (com a atividade do Setor), "Levar". */
  rotulo: string
  /** Todo o disponível. O campo nasce com ele (spec §6.1) e não aceita mais do que ele. */
  maximo: number
  /** Rótulo do campo; padrão 'Quantidade'. */
  campo?: string
  /** Conjuntos de Kit: só inteiros, lidos por `lerConjuntos`. */
  inteiro?: boolean
  /** Campos a mais, antes da quantidade. */
  children?: ReactNode
  /** Validade dos campos a mais. `false` desabilita o confirmar, como uma quantidade inválida. */
  completo?: boolean
  /** Rejeição é do chamador: ele mostra a mensagem e decide se recarrega. O formulário só destrava. */
  aoConfirmar: (quantidade: number) => Promise<void>
  aoCancelar: () => void
}

/**
 * O campo de quantidade de toda ação da execução: nasce com todo o disponível, o operador pode
 * diminuir (lote divisível, regra 9), e o botão fica desabilitado enquanto envia.
 *
 * O desabilitar é a defesa do toque duplo no celular (spec §8.1). O `enviandoRef` fecha a janela que
 * sobra: dois `submit` no MESMO quadro, antes de o React redesenhar o botão desabilitado. Se o
 * segundo passar mesmo assim, a validação de saldo do backend limita o estrago e o estorno corrige.
 */
export function FormularioDeQuantidade({ rotulo, maximo, campo = 'Quantidade', inteiro = false, children, completo = true, aoConfirmar, aoCancelar }: Props) {
  const [texto, setTexto] = useState(() => (inteiro ? String(maximo) : quantidadeParaCampo(maximo)))
  const [enviando, setEnviando] = useState(false)
  const enviandoRef = useRef(false)

  const leitura = inteiro ? lerConjuntos(texto, maximo) : lerQuantidade(texto, maximo)

  async function enviar(e: FormEvent) {
    e.preventDefault()
    if (leitura.valor === null || !completo || enviandoRef.current) return
    enviandoRef.current = true
    setEnviando(true)
    try {
      await aoConfirmar(leitura.valor)
    } catch {
      // Quem chama já traduziu e mostrou a mensagem; aqui só se destrava o botão.
    } finally {
      enviandoRef.current = false
      setEnviando(false)
    }
  }

  return (
    <form onSubmit={enviar} className="flex flex-col gap-3 border-t border-borda pt-3">
      {children}
      <Campo rotulo={campo} dica={leitura.erro ?? `Disponível: ${formatarQuantidade(maximo)}`}>
        {(id, idDaDica) => (
          <input
            id={id}
            type="text"
            inputMode={inteiro ? 'numeric' : 'decimal'}
            autoComplete="off"
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            aria-describedby={idDaDica}
            aria-invalid={leitura.erro !== null}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <div className="flex flex-wrap gap-2">
        <Botao
          type="submit"
          carregando={enviando}
          rotuloCarregando="Enviando…"
          disabled={leitura.valor === null || !completo}
        >
          {rotulo}
        </Botao>
        <Botao variante="secundario" onClick={aoCancelar} disabled={enviando}>Cancelar</Botao>
      </div>
    </form>
  )
}
