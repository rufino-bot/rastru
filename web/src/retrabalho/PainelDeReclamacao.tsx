import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import type { AgrupamentoDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { obterEstrutura } from '../api/estrutura'
import { registrarReclamacao } from '../api/retrabalho'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { PainelDeEscrita } from '../components/PainelDeEscrita'
import { lerQuantidade } from '../execucao/quantidade'
import { CampoDeQuantidade, useEscritaDoPainel } from '../expedicao/escritaDoPainel'

/** O texto do cliente é `NVARCHAR(500)`; o servidor recusa acima disso (`TextoLongoDemais`). */
const TAMANHO_MAXIMO = 500

interface PecaOferecida {
  id: number
  rotulo: string
}

interface Props {
  pedidoId: number
  /** Os Agrupamentos já carregados pela tela: as Peças oferecidas são as deles. */
  agrupamentos: AgrupamentoDto[]
  aoConcluir: () => void
  aoFechar: () => void
}

/**
 * "Registrar reclamação": a Peça do Pedido, a quantidade e o que o cliente disse. Oferece só Peças (nó de topo) dos
 * Agrupamentos do Pedido, lidas das árvores ao abrir; a regra de quanto se pode reclamar (o expedido menos o já
 * reclamado) é do servidor, que responde `SaldoInsuficiente` com a frase dentro do painel.
 */
export function PainelDeReclamacao({ pedidoId, agrupamentos, aoConcluir, aoFechar }: Props) {
  const [pecas, setPecas] = useState<PecaOferecida[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const [pecaId, setPecaId] = useState('')
  const [quantidade, setQuantidade] = useState('')
  const [texto, setTexto] = useState('')
  const { enviando, erro, escrever } = useEscritaDoPainel(aoConcluir, aoFechar, 'Não foi possível registrar a reclamação.')

  // Uma leitura por abertura do painel; a resposta de um painel já desmontado não escreve em estado.
  useEffect(() => {
    let vivo = true
    Promise.all(agrupamentos.map((a) => obterEstrutura(a.id).then((nos) => ({ a, nos })))).then(
      (arvores) => {
        if (!vivo) return
        setPecas(arvores.flatMap(({ a, nos }) => nos
          .filter((n) => n.nivelHierarquico === 'Peca')
          .map((n) => ({
            id: n.id,
            rotulo: `${n.codigoDoComponente ? `${n.codigoDoComponente} · ` : ''}${n.descricao} (${a.codigo})`,
          }))))
      },
      (e: unknown) => { if (vivo) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar as Peças do pedido.')) },
    )
    return () => { vivo = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const leitura = quantidade.trim() === '' ? null : lerQuantidade(quantidade, Number.MAX_SAFE_INTEGER)
  const textoAparado = texto.trim()
  const podeEnviar = pecaId !== '' && leitura !== null && leitura.erro === null && textoAparado !== '' && !enviando

  function enviar(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!podeEnviar || leitura === null || leitura.valor === null) return
    const valor = leitura.valor
    escrever(() => registrarReclamacao(pedidoId, { estruturaItemId: Number(pecaId), quantidade: valor, descricao: textoAparado }))
  }

  return (
    <PainelDeEscrita titulo="Registrar reclamação" aoEnviar={enviar} aoFechar={aoFechar} enviando={enviando}>
      <BannerDeErro mensagem={erroDeCarga} />
      {pecas === null && erroDeCarga === null && <EstadoCarregando />}
      <Campo rotulo="Peça">
        {(id) => (
          <select id={id} value={pecaId} onChange={(e) => setPecaId(e.target.value)} disabled={enviando || pecas === null} className={CLASSES_DE_CONTROLE}>
            <option value="">Escolha a Peça</option>
            {(pecas ?? []).map((p) => <option key={p.id} value={p.id}>{p.rotulo}</option>)}
          </select>
        )}
      </Campo>
      <CampoDeQuantidade
        rotulo="Quantidade" texto={quantidade} erro={leitura?.erro ?? null} aoMudar={setQuantidade} desabilitado={enviando}
      />
      <Campo rotulo="O que o cliente disse">
        {(id) => (
          <textarea
            id={id} value={texto} onChange={(e) => setTexto(e.target.value)} maxLength={TAMANHO_MAXIMO} rows={3}
            required disabled={enviando} className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <BannerDeErro mensagem={erro} />
      <Botao type="submit" carregando={enviando} rotuloCarregando="Registrando…" disabled={!podeEnviar} className="self-start">
        Registrar reclamação
      </Botao>
    </PainelDeEscrita>
  )
}
