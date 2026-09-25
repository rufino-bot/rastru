import { useEffect, useState } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { listarSetores, type SetorDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { esquecerSetor, setorLembrado } from '../execucao/setorLembrado'
import { Pagina } from '../components/Pagina'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'

/** O que o "Trocar de Setor" da fila manda junto: sem isto, `/fila` redirecionaria de volta. */
export interface EstadoDaEscolhaDeSetor {
  escolher: true
}

/**
 * `/fila` — a escolha do Setor (spec §6.1). Quem chega pelo menu com um Setor lembrado NESTE
 * aparelho vai direto para a fila dele; quem chega pelo "Trocar de Setor" (com
 * `state.escolher`) vê a lista.
 *
 * O redirecionamento espera a lista de Setores ativos: um Setor lembrado que foi inativado ou
 * apagado desde a última visita não pode mandar o operador para uma fila que não é mais dele
 * (Review Focus 5). Nesse caso a lembrança é esquecida e a lista aparece.
 */
export function FilaPage() {
  const location = useLocation()
  const querEscolher = (location.state as Partial<EstadoDaEscolhaDeSetor> | null)?.escolher === true

  const [setores, setSetores] = useState<SetorDto[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    let cancelado = false
    listarSetores(false)
      .then((lista) => {
        if (cancelado) return
        const lembrado = setorLembrado()
        if (lembrado !== null && !lista.some((s) => s.id === lembrado)) esquecerSetor()
        setSetores(lista)
      })
      .catch((e) => { if (!cancelado) setErro(mensagemDeErro(e, 'Não foi possível carregar os setores.')) })
    return () => { cancelado = true }
  }, [])

  if (setores !== null && !querEscolher) {
    const lembrado = setorLembrado()
    if (lembrado !== null && setores.some((s) => s.id === lembrado)) {
      return <Navigate to={`/fila/${lembrado}`} replace />
    }
  }

  return (
    <Pagina titulo="Fila do Setor">
      <BannerDeErro mensagem={erro} />
      {setores === null && erro === null && <EstadoCarregando />}
      {setores !== null && setores.length === 0 && (
        <EstadoVazio titulo="Nenhum Setor ativo cadastrado" />
      )}
      {setores !== null && setores.length > 0 && (
        <>
          <p className="text-sm text-tinta-fraca">
            Escolha o seu Setor. Este aparelho lembra a escolha na próxima vez.
          </p>
          <ListaDeCadastro rotulo="Setores">
            {setores.map((s) => (
              <ItemDeCadastro key={s.id}>
                <Link
                  to={`/fila/${s.id}`}
                  className="font-medium rounded hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
                >
                  {s.nome}
                </Link>
              </ItemDeCadastro>
            ))}
          </ListaDeCadastro>
        </>
      )}
    </Pagina>
  )
}
