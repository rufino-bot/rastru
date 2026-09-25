import { useEffect, useState } from 'react'
import { obterRoteiroDoNo, type NoResumoDto, type SetorResumidoDto } from '../api/execucao'
import { mensagemDeErro } from '../api/erros'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { Botao } from '../components/Botao'
import { FormularioDeQuantidade } from './FormularioDeQuantidade'

interface Props {
  pai: NoResumoDto
  /** O Setor onde o filho aguarda montagem agora — sai da lista de destinos (spec §4.3: S' ≠ S). */
  setorAtualId: number
  maximo: number
  aoConfirmar: (destinoSetorId: number, quantidade: number) => Promise<void>
  aoCancelar: () => void
}

/**
 * "Levar para outro Setor" (spec §6.1): o filho foi entregue para montagem no Setor errado, e o
 * Movimentador o redireciona para outro Setor do Roteiro do PAI (decisão 2.4). A fila não traz o
 * Roteiro do pai, então este formulário o busca quando abre.
 *
 * `<select>` simples, e não `SeletorComBusca`: são os poucos Setores de um Roteiro, não um catálogo
 * paginado — o mesmo motivo da spec §6.2 para a tela de Tarefas. Setor repetido no Roteiro (regra
 * 21) aparece uma vez só: o destino é o Setor, não o passo.
 */
export function FormularioDeRedirecionamento({ pai, setorAtualId, maximo, aoConfirmar, aoCancelar }: Props) {
  const [setores, setSetores] = useState<SetorResumidoDto[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [destino, setDestino] = useState<number | null>(null)

  useEffect(() => {
    let cancelado = false
    obterRoteiroDoNo(pai.id)
      .then((r) => {
        if (cancelado) return
        const distintos = new Map<number, SetorResumidoDto>()
        for (const p of r.passos) {
          if (p.setorId !== setorAtualId && !distintos.has(p.setorId)) distintos.set(p.setorId, { id: p.setorId, nome: p.nome })
        }
        setSetores([...distintos.values()])
      })
      .catch((e) => { if (!cancelado) setErro(mensagemDeErro(e, 'Não foi possível carregar os Setores do Roteiro do pai.')) })
    return () => { cancelado = true }
  }, [pai.id, setorAtualId])

  if (erro) {
    return (
      <div className="flex flex-col gap-3 border-t border-borda pt-3">
        <BannerDeErro mensagem={erro} />
        <Botao variante="secundario" onClick={aoCancelar} className="self-start">Cancelar</Botao>
      </div>
    )
  }
  if (setores === null) return <EstadoCarregando />
  if (setores.length === 0) {
    return (
      <div className="flex flex-col gap-3 border-t border-borda pt-3">
        <p className="text-sm text-tinta">{`O Roteiro de ${pai.descricao} não tem outro Setor para onde levar.`}</p>
        <Botao variante="secundario" onClick={aoCancelar} className="self-start">Cancelar</Botao>
      </div>
    )
  }

  return (
    <FormularioDeQuantidade
      rotulo="Levar"
      maximo={maximo}
      completo={destino !== null}
      aoConfirmar={(q) => aoConfirmar(destino!, q)}
      aoCancelar={aoCancelar}
    >
      <Campo rotulo="Setor de destino">
        {(id) => (
          <select
            id={id}
            value={destino ?? ''}
            onChange={(e) => setDestino(e.target.value ? Number(e.target.value) : null)}
            className={CLASSES_DE_CONTROLE}
          >
            <option value="">Escolha o Setor</option>
            {setores.map((s) => <option key={s.id} value={s.id}>{s.nome}</option>)}
          </select>
        )}
      </Campo>
    </FormularioDeQuantidade>
  )
}
