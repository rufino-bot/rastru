import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, listarMateriais, listarSetores, listarPedidos,
  type PedidoDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { STATUS_DO_PEDIDO, ENCERRADOS } from '../pedidos/statusDoPedido'
import { LinhaDePedido } from '../pedidos/LinhaDePedido'
import { Pagina } from '../components/Pagina'
import { Pilula } from '../components/Pilula'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'

/** Quantos pedidos a seção "há mais tempo" mostra. Cinco, pela spec §3.2. */
const QUANTOS_MAIS_ANTIGOS = 5

// `pedidosAbertos` saiu daqui na 1E: o array de pedidos vive em estado próprio (a Home deriva
// TRÊS coisas dele agora), e guardar a contagem em paralelo criaria duas verdades sobre o mesmo
// dado, que podem divergir.
interface Contagens {
  componentes: number
  materiais: number
  setores: number
}

function CartaoDeContagem({ titulo, valor, para, resumo }: {
  titulo: string
  valor: number | null
  para: string
  /** Conteúdo extra dentro do cartão. NÃO pode conter `<a>`: o cartão já é um `<Link>`. */
  resumo?: ReactNode
}) {
  return (
    <Link
      to={para}
      className="flex flex-col gap-1 rounded-lg border border-borda bg-superficie px-5 py-6 transition-colors hover:border-acao focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-acao"
    >
      {/* Traço em vez de zero enquanto carrega: "0 pedidos" é uma afirmação, e ela seria falsa. */}
      <span className="text-3xl font-semibold text-tinta">{valor === null ? '—' : valor}</span>
      <span className="text-sm text-tinta-fraca">{titulo}</span>
      {resumo}
    </Link>
  )
}

export function HomePage() {
  const [pedidos, setPedidos] = useState<PedidoDto[] | null>(null)
  const [contagens, setContagens] = useState<Contagens | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    setErro(null)
    try {
      // `tamanho: 1` no de componentes: só o `total` interessa, e assim nenhum item trafega. As
      // outras três listagens ainda não são paginadas no backend (dívida rastreada da 1B: o
      // `PaginaDto<T>` não foi migrado para Setor/Material) — quando forem, este cartão vira o
      // molde das outras.
      const [paginaDeComponentes, pedidos, materiais, setores] = await Promise.all([
        listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 }),
        listarPedidos(),
        listarMateriais(false),
        listarSetores(false),
      ])
      setPedidos(pedidos)
      setContagens({
        componentes: paginaDeComponentes.total,
        materiais: materiais.length,
        setores: setores.length,
      })
    } catch (e) {
      // Sem isto, uma falha numa releitura futura deixaria a seção "há mais tempo" (Task 5)
      // mostrando dado velho ao lado do banner de erro — o que a spec §3.4 proíbe. Hoje `carregar`
      // roda uma vez só e não há caminho que exercite isto; está aqui porque a alternativa é
      // depender de a Home nunca ganhar um botão de recarregar.
      setPedidos(null)
      setErro(mensagemDeErro(e, 'Não foi possível carregar os números do sistema.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  // Derivado, não guardado: uma fonte de verdade só. `null` enquanto o dado não chegou — e o
  // resumo NÃO renderiza nesse estado (nem com zeros, que seriam falsos, nem com traços, que
  // seriam ruído; o número grande do cartão já diz "—").
  const abertos = pedidos === null ? null : pedidos.filter((p) => p.status === 'Aberto').length
  const porStatus = pedidos === null ? null : STATUS_DO_PEDIDO.map((status) => ({
    status,
    quantidade: pedidos.filter((p) => p.status === status).length,
  }))

  // Ordenação pela string ISO, e não por `Date`: `dataAbertura` chega em GMT-3 com offset
  // explícito (`HorarioDeBrasiliaJsonConverter`), e ISO 8601 com o mesmo offset ordena
  // lexicograficamente na mesma ordem que cronologicamente. Passar por `new Date()` reconverteria
  // para o fuso do aparelho — o mesmo motivo que fez `formatarDataHora` não usar `Date`.
  //
  // `.filter()` já devolve array novo, então o `.sort()` abaixo não ordena o estado no lugar.
  //
  // `.some(===)` e não `.includes`: `ENCERRADOS` é tupla `readonly`, e `.includes` exigiria um
  // cast para aceitar um `status` que pode não estar nela.
  const maisAntigos = pedidos === null ? null : pedidos
    .filter((p) => !ENCERRADOS.some((encerrado) => encerrado === p.status))
    .sort((a, b) => a.dataAbertura.localeCompare(b.dataAbertura))
    .slice(0, QUANTOS_MAIS_ANTIGOS)

  return (
    <Pagina titulo="Início">
      <BannerDeErro mensagem={erro} />

      {carregando && <EstadoCarregando />}

      <div className="grid gap-4 sm:grid-cols-2">
        <CartaoDeContagem
          titulo="pedidos abertos"
          valor={abertos}
          para="/pedidos"
          resumo={porStatus && (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {porStatus.map(({ status, quantidade }) => (
                // Rótulo e contagem numa ÚNICA string, e não em dois elementos: o teste
                // `conta só os pedidos abertos` faz `within(cartao).getByText('2')`, e
                // `getByText` LANÇA quando casa mais de um nó. Contagem em elemento próprio
                // colidiria com o número grande do cartão.
                //
                // SEM `tom` de propósito — a pílula fica no tom neutro padrão. Verde e vermelho
                // ficam reservados a ESTADO de um pedido concreto: é o que `PedidosPage` e a
                // linha de "Pedidos abertos há mais tempo" (via `LinhaDePedido`) continuam
                // fazendo, e continua certo lá. Aqui a pílula é rótulo de uma CONTAGEM, não de um
                // pedido — "Concluido 0" em verde ou "Cancelado 0" em vermelho estaria colorindo
                // um zero, e um zero não é nem aprovação nem erro para alarmar sobre. Achado
                // olhando a tela renderizada em 375px, não por teste: nenhuma das três guardas de
                // tema (paleta, contraste, opacidade) mede SEMÂNTICA de cor, e por isso o teste
                // `nao usa cor de estado no resumo...` existe — sem ele, uma recolorização futura
                // deste resumo passaria a suíte inteira em verde.
                <Pilula key={status}>{`${status} ${quantidade}`}</Pilula>
              ))}
            </div>
          )}
        />
        <CartaoDeContagem titulo="componentes ativos" valor={contagens?.componentes ?? null} para="/componentes" />
        <CartaoDeContagem titulo="materiais ativos" valor={contagens?.materiais ?? null} para="/materiais" />
        <CartaoDeContagem titulo="setores ativos" valor={contagens?.setores ?? null} para="/setores" />
      </div>

      {/* `maisAntigos !== null` cobre carregando E erro de uma vez: nos dois casos `pedidos` é
          `null`. Não troque por `maisAntigos?.length` — durante o carregando isso renderizaria o
          `EstadoVazio`, que tem `role="status"` igual ao `EstadoCarregando`, e o teste
          `mostra o indicador de carregando` faz `getByRole('status')`, que LANÇA com dois. Além
          de, claro, dizer "não há pedidos abertos" quando a verdade é "ainda não perguntei". */}
      {maisAntigos !== null && (
        <section className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold text-tinta">Pedidos abertos há mais tempo</h2>
          {maisAntigos.length === 0 ? (
            <EstadoVazio
              titulo="Nenhum pedido em aberto."
              descricao="Todos os pedidos cadastrados estão concluídos ou cancelados."
            />
          ) : (
            <ListaDeCadastro rotulo="Pedidos abertos há mais tempo">
              {maisAntigos.map((p) => (
                <ItemDeCadastro key={p.id}>
                  <LinhaDePedido pedido={p} />
                </ItemDeCadastro>
              ))}
            </ListaDeCadastro>
          )}
        </section>
      )}
    </Pagina>
  )
}
