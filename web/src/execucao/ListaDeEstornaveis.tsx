import type { Estornavel } from '../api/execucao'
import { Botao } from '../components/Botao'
import { ItemComAcao } from '../components/ItemComAcao'
import { ListaDeCadastro } from '../components/ListaDeCadastro'
import { rotuloDoEstornavel } from './formatacao'

interface Props {
  estornaveis: Estornavel[]
  aoEscolher: (e: Estornavel) => void
  aoCancelar: () => void
}

/**
 * A lista curta do estorno rápido (spec da Fase 3D, §2.4): uma linha da fila é um SALDO, que pode
 * somar vários registros, e o estorno desfaz um movimento inteiro — então o operador escolhe qual.
 * Quem abre esta lista é a fila, e só quando há mais de um; com um só, ela vai direto à confirmação.
 */
export function ListaDeEstornaveis({ estornaveis, aoEscolher, aoCancelar }: Props) {
  return (
    <div className="flex flex-col gap-3 border-t border-borda pt-3">
      <p className="text-sm text-tinta">Qual registro você quer estornar?</p>
      <ListaDeCadastro rotulo="Registros que dá para estornar">
        {estornaveis.map((e) => (
          <ItemComAcao
            key={`${e.tipo}-${e.id}`}
            acao={(
              <Botao variante="secundario" aria-label={`Estornar ${rotuloDoEstornavel(e)}`} onClick={() => aoEscolher(e)}>
                Estornar
              </Botao>
            )}
          >
            <span className="text-sm text-tinta">{rotuloDoEstornavel(e)}</span>
          </ItemComAcao>
        ))}
      </ListaDeCadastro>
      <Botao variante="secundario" onClick={aoCancelar} className="self-start">Cancelar</Botao>
    </div>
  )
}
