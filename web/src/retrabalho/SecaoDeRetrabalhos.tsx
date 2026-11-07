import { useCallback, useEffect, useId, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { formatarData } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { ROTULO_DO_MOTIVO_DE_RETRABALHO, type RetrabalhoResumoDto, type RetrabalhosDoPedidoDto } from '../api/retrabalho'
import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { EstadoVazio } from '../components/EstadoVazio'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { Pilula } from '../components/Pilula'
import { rotuloDoStatus } from '../pedidos/statusDoPedido'

interface Props {
  carregar: () => Promise<RetrabalhosDoPedidoDto>
  /** Muda quando a tela gravou algo que altera os Retrabalhos: a lista recarrega. A primeira leitura é a da montagem. */
  versao: number
}

/**
 * A seção "Retrabalhos" do Pedido original: cada Retrabalho aberto a partir dele, com o número (link para o detalhe),
 * o motivo, o status e o prazo. Abrir mora nas linhas da reposição e no `acao` da página, não aqui. Status é pílula
 * neutra: o Retrabalho não é aprovado nem reprovado.
 */
export function SecaoDeRetrabalhos({ carregar, versao }: Props) {
  const idDoTitulo = useId()
  const [lista, setLista] = useState<RetrabalhoResumoDto[] | null>(null)
  const [erroDeCarga, setErroDeCarga] = useState<string | null>(null)
  const sequencia = useRef(0)
  const carregarRef = useRef(carregar)
  carregarRef.current = carregar

  // A resposta que chega depois de uma mais nova é descartada.
  const recarregar = useCallback(async () => {
    const minha = ++sequencia.current
    setErroDeCarga(null)
    try {
      const lida = await carregarRef.current()
      if (minha === sequencia.current) setLista(lida.retrabalhos)
    } catch (e) {
      if (minha === sequencia.current) setErroDeCarga(mensagemDeErro(e, 'Não foi possível carregar os retrabalhos.'))
    }
  }, [])

  const versaoLida = useRef<number | null>(null)
  useEffect(() => {
    if (versaoLida.current === versao) return
    versaoLida.current = versao
    recarregar()
  }, [versao, recarregar])

  return (
    <section aria-labelledby={idDoTitulo} className="flex flex-col gap-3">
      <h2 id={idDoTitulo} className="text-lg font-medium text-tinta">Retrabalhos</h2>
      <BannerDeErro mensagem={erroDeCarga} />
      {erroDeCarga !== null && (
        <Botao variante="secundario" className="self-start" onClick={recarregar}>Tentar de novo</Botao>
      )}
      {lista === null && erroDeCarga === null && <EstadoCarregando />}
      {lista !== null && erroDeCarga === null && lista.length === 0 && (
        <EstadoVazio titulo="Nenhum retrabalho aberto a partir deste pedido" />
      )}
      {lista !== null && lista.length > 0 && (
        <ListaDeCadastro rotulo="Retrabalhos, do mais recente ao mais antigo">
          {lista.map((r) => (
            <ItemDeCadastro key={r.id}>
              <span className="flex flex-wrap items-center gap-2 text-sm text-tinta">
                <Link to={`/pedidos/${r.id}`} className="font-mono font-semibold underline">{r.numero}</Link>
                {r.motivoRetrabalho && <span>{ROTULO_DO_MOTIVO_DE_RETRABALHO[r.motivoRetrabalho]}</span>}
                <Pilula>{rotuloDoStatus(r.status)}</Pilula>
                <span className="text-tinta-fraca">{`Entrega ${formatarData(r.dataEntrega)}`}</span>
              </span>
            </ItemDeCadastro>
          ))}
        </ListaDeCadastro>
      )}
    </section>
  )
}
