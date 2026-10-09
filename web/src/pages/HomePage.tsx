import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  listarComponentes, listarMateriais, listarSetores, obterResumoDePedidos,
  type ResumoDePedidosDto,
} from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { ENCERRADOS, rotuloDoStatus } from '../pedidos/statusDoPedido'
import { LinhaDePedido } from '../pedidos/LinhaDePedido'
import { Pagina } from '../components/Pagina'
import { Pilula } from '../components/Pilula'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { ListaDeCadastro, ItemDeCadastro } from '../components/ListaDeCadastro'
import { EstadoVazio } from '../components/EstadoVazio'

// Sem `pedidosAbertos` aqui: o resumo de pedidos vive em estado próprio (a Home deriva TRÊS coisas
// dele), e guardar a contagem em paralelo criaria duas verdades sobre o mesmo dado, que podem
// divergir.
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
  const [resumo, setResumo] = useState<ResumoDePedidosDto | null>(null)
  const [contagens, setContagens] = useState<Contagens | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(true)

  async function carregar() {
    setCarregando(true)
    setErro(null)
    try {
      // `tamanho: 1` no de componentes: só o `total` interessa, e assim nenhum item trafega. As
      // listagens de materiais e setores ainda não são paginadas no backend (dívida rastreada da
      // 1B: o `PaginaDto<T>` não foi migrado para Setor/Material) — quando forem, este cartão vira
      // o molde delas. Pedidos vêm pelo resumo, contado no servidor sobre todos eles: uma lista
      // paginada de Pedidos daria "contagem da primeira página".
      const [paginaDeComponentes, resumoDePedidos, materiais, setores] = await Promise.all([
        listarComponentes({ busca: '', incluirInativos: false, pagina: 1, tamanho: 1 }),
        obterResumoDePedidos(),
        listarMateriais(false),
        listarSetores(false),
      ])
      setResumo(resumoDePedidos)
      setContagens({
        componentes: paginaDeComponentes.total,
        materiais: materiais.length,
        setores: setores.length,
      })
    } catch (e) {
      // Sem isto, uma falha numa releitura futura deixaria a seção "Prazos de entrega" mostrando dado
      // velho ao lado do banner de erro — o que a spec §3.4 proíbe. Hoje `carregar` roda uma vez só
      // e não há caminho que exercite isto; está aqui porque a alternativa é depender de a Home
      // nunca ganhar um botão de recarregar.
      setResumo(null)
      setErro(mensagemDeErro(e, 'Não foi possível carregar os números do sistema.'))
    } finally {
      setCarregando(false)
    }
  }

  useEffect(() => { carregar() }, [])

  // Derivado, não guardado: uma fonte de verdade só. `null` enquanto o dado não chegou — e o
  // resumo NÃO renderiza nesse estado (nem com zeros, que seriam falsos, nem com traços, que
  // seriam ruído; o número grande do cartão já diz "—").
  //
  // "Aberto" é todo Pedido fora de `ENCERRADOS`, não só o status `Aberto`: desde a Fase 3 o
  // primeiro Início passa o Pedido a `EmProducao`, e contar só `Aberto` fazia o cartão dizer "12
  // abertos" ao lado de "Em produção 2". `.some(===)` e não `.includes`: `ENCERRADOS` é tupla
  // `readonly`, e `.includes` exigiria um cast para aceitar um `status` que pode não estar nela.
  const porStatus = resumo?.porStatus ?? null
  const abertos = porStatus === null ? null
    : porStatus
      .filter(({ status }) => !ENCERRADOS.some((encerrado) => encerrado === status))
      .reduce((soma, { quantidade }) => soma + quantidade, 0)

  // Os mais urgentes chegam prontos: a regra (só os não encerrados, do prazo mais antigo ao mais
  // novo, no máximo cinco) é do servidor, e a Home apresenta na ordem em que vieram.
  const urgentes = resumo?.maisUrgentes ?? null

  // Derivado aqui, e não no JSX, porque lá dentro o TypeScript não estreita `porStatus` pela porta
  // `urgentes !== null` — são duas variáveis diferentes para ele, mesmo que nasçam do mesmo dado.
  const cadastroVazio = porStatus !== null && porStatus.every(({ quantidade }) => quantidade === 0)

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
                // linha de "Prazos de entrega" (via `LinhaDePedido`) continuam
                // fazendo, e continua certo lá. Aqui a pílula é rótulo de uma CONTAGEM, não de um
                // pedido — "Concluido 0" em verde ou "Cancelado 0" em vermelho estaria colorindo
                // um zero, e um zero não é nem aprovação nem erro para alarmar sobre. Achado
                // olhando a tela renderizada em 375px, não por teste: nenhuma das três guardas de
                // tema (paleta, contraste, opacidade) mede SEMÂNTICA de cor, e por isso o teste
                // `nao usa cor de estado no resumo...` existe — sem ele, uma recolorização futura
                // deste resumo passaria a suíte inteira em verde. MEDIDO, e não deduzido: repondo
                // `tom={tomDoStatus(status)}` aqui — mais o import de `tomDoStatus`, que este
                // arquivo não tem, sem o qual a reposição nem compila — a suíte fecha com UMA
                // vermelha, e a vermelha é esse teste.
                <Pilula key={status}>{`${rotuloDoStatus(status)} ${quantidade}`}</Pilula>
              ))}
            </div>
          )}
        />
        <CartaoDeContagem titulo="componentes ativos" valor={contagens?.componentes ?? null} para="/componentes" />
        <CartaoDeContagem titulo="materiais ativos" valor={contagens?.materiais ?? null} para="/materiais" />
        <CartaoDeContagem titulo="setores ativos" valor={contagens?.setores ?? null} para="/setores" />
      </div>

      {/* `urgentes !== null` cobre carregando E erro de uma vez: nos dois casos `resumo` é
          `null`. Não troque por `urgentes?.length` — durante o carregando isso renderizaria o
          `EstadoVazio`, que tem `role="status"` igual ao `EstadoCarregando`, e o teste
          `mostra o indicador de carregando` faz `getByRole('status')`, que LANÇA com dois. Além
          de, claro, dizer "não há pedidos abertos" quando a verdade é "ainda não perguntei". */}
      {urgentes !== null && (
        <section className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold text-tinta">Prazos de entrega</h2>
          {urgentes.length === 0 ? (
            // Duas causas diferentes caem neste mesmo `length === 0`, e o `CLAUDE.md` exige que o
            // vazio distinga "não achei" de "não há nada": com o cadastro vazio, dizer que "todos
            // estão concluídos ou cancelados" afirma algo sobre um conjunto que não existe.
            <EstadoVazio
              titulo="Nenhum pedido em aberto."
              descricao={
                cadastroVazio
                  ? 'Nenhum pedido foi cadastrado ainda.'
                  : 'Todos os pedidos cadastrados estão concluídos ou cancelados.'
              }
            />
          ) : (
            <ListaDeCadastro rotulo="Prazos de entrega">
              {urgentes.map((p) => (
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
