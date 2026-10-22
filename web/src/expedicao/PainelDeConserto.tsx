import { useState, type FormEvent } from 'react'
import { mandarParaConserto, type PecaNaExpedicaoDto } from '../api/expedicao'
import type { SetorDto } from '../api/cadastros'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { rotuloDoNo } from '../execucao/formatacao'
import { lerQuantidade, quantidadeParaCampo } from '../execucao/quantidade'
import { CampoDeQuantidade, useEscritaDoPainel } from './escritaDoPainel'

interface Props {
  peca: PecaNaExpedicaoDto
  /** Os Setores do cadastro; o painel oferece só os ativos (C19). */
  setores: SetorDto[]
  aoFechar: () => void
  /** O servidor mudou: no sucesso e no 409, quem abriu o painel recarrega. */
  aoConcluir: () => void
}

/**
 * "Mandar para conserto" (D6, C19 da spec da Fase 5A): a não conforme aguardando decisão vai a um Setor ativo
 * qualquer, inclusive o Setor Expedição, e o Movimentador a leva. O campo abre com todas as que aguardam decisão.
 *
 * O Setor é um `<select>` da lista inteira, e não o `SeletorComBusca`: a lista de Setores não é catálogo paginado.
 * Nada vem escolhido, para o conserto não ir ao primeiro Setor da lista por um toque distraído.
 */
export function PainelDeConserto({ peca, setores, aoFechar, aoConcluir }: Props) {
  const [setorId, setSetorId] = useState('')
  const [texto, setTexto] = useState<string | null>(null)
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível mandar para conserto.')

  const maximo = peca.posicoes.naoConforme
  // O campo intocado acompanha o saldo atual: a recarga depois de um 409 o corrige.
  const mostrado = texto ?? quantidadeParaCampo(maximo)
  const leitura = lerQuantidade(mostrado, maximo)
  const ativos = setores.filter((s) => s.ativo)
  const invalido = setorId === '' || leitura.erro !== null

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (invalido || leitura.erro !== null) return
    const quantidade = leitura.valor
    escrever(() => mandarParaConserto(peca.id, Number(setorId), quantidade))
  }

  return (
    <PainelDeEscrita
      titulo="Mandar para conserto"
      subtitulo={rotuloDoNo(peca)}
      aoEnviar={enviar}
      aoFechar={aoFechar}
      enviando={enviando}
    >
      <Campo rotulo="Setor">
        {(id) => (
          <select
            id={id}
            value={setorId}
            onChange={(e) => setSetorId(e.target.value)}
            disabled={enviando}
            className={CLASSES_DE_CONTROLE}
          >
            <option value="">Escolha o Setor</option>
            {ativos.map((s) => <option key={s.id} value={String(s.id)}>{s.nome}</option>)}
          </select>
        )}
      </Campo>
      <CampoDeQuantidade
        rotulo="Quantidade"
        texto={mostrado}
        erro={leitura.erro}
        aoMudar={setTexto}
        desabilitado={enviando}
      />
      <BannerDeErro mensagem={erro} />
      <Botao
        type="submit"
        carregando={enviando}
        rotuloCarregando="Registrando…"
        disabled={invalido}
        className="self-start"
      >
        Mandar para conserto
      </Botao>
    </PainelDeEscrita>
  )
}
