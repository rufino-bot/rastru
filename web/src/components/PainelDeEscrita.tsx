import { useEffect, useId, useRef } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Botao } from './Botao'

interface Props {
  /** O nome da ação ("Novo setor"). Vira o `<h2>` e o nome acessível do `<form>`. */
  titulo: string
  /** Opcional: identifica o alvo (o nó sendo editado, o setor sendo editado). */
  subtitulo?: ReactNode
  aoEnviar: (e: FormEvent<HTMLFormElement>) => void
  /** O `Cancelar`. Quem descarta o digitado é a tela, aqui dentro. */
  aoFechar: () => void
  /** Só para os testes existentes da tela do Agrupamento (decisão da spec: parâmetro, não fixo). */
  testId?: string
  /** Campos, banner de erro de escrita e o submit — tudo que varia por tela. */
  children: ReactNode
}

/**
 * Painel de escrita: o formulário que abre sob demanda, no lugar de ficar fixo no topo da página.
 *
 * Extraída do painel que a tela do Agrupamento (`AgrupamentoDetalhePage`) escreveu à mão para
 * editar e acrescentar filho; a forma é a mesma, mais o `id`/`aria-labelledby` que aquele não tinha,
 * para o `<form>` ter nome acessível e o teste achá-lo por papel.
 *
 * Não fecha no `Escape` nem prende o foco: não é modal. O painel convive com a lista e com o resto
 * da tela, e quem quer sair usa o `Cancelar` ou salva (spec da Fase 1F, seção "O painel").
 *
 * O foco inicial vai ao primeiro controle do CONTEÚDO, e não do `<form>` inteiro (decisão D9 do
 * plano da 1F): o `Cancelar` vem antes, no cabeçalho, e seria o primeiro focável do DOM. O foco é
 * dado só na montagem — quem precisa dele de novo (trocar o alvo com o painel aberto) remonta o
 * painel por `key`.
 */
export function PainelDeEscrita({ titulo, subtitulo, aoEnviar, aoFechar, testId, children }: Props) {
  const idDoTitulo = useId()
  const conteudo = useRef<HTMLDivElement>(null)

  useEffect(() => {
    conteudo.current
      ?.querySelector<HTMLElement>(
        'input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled])',
      )
      ?.focus()
  }, [])

  return (
    <form
      onSubmit={aoEnviar}
      aria-labelledby={idDoTitulo}
      data-testid={testId}
      className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4"
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id={idDoTitulo} className="text-lg font-medium text-tinta">
            {titulo}
          </h2>
          {subtitulo && <p className="text-xs text-tinta-fraca">{subtitulo}</p>}
        </div>
        <Botao variante="secundario" onClick={aoFechar}>Cancelar</Botao>
      </div>
      <div ref={conteudo} className="flex flex-col gap-4">
        {children}
      </div>
    </form>
  )
}
