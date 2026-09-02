import { useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import {
  obterEstrutura, criarPeca, ehConflitoDeEstrutura, type NoDaEstrutura,
} from '../api/estrutura'
import type { ComponenteDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { usePodeEscrever } from '../auth/usePermissao'
import { Pagina } from '../components/Pagina'
import { Botao } from '../components/Botao'
import { Campo, CLASSES_DE_CONTROLE } from '../components/Campo'
import { BannerDeErro } from '../components/BannerDeErro'
import { EstadoVazio } from '../components/EstadoVazio'
import { EstadoCarregando } from '../components/EstadoCarregando'
import { SeletorComBusca } from '../components/SeletorComBusca'
import { ArvoreDeEstrutura } from '../components/ArvoreDeEstrutura'

/**
 * Detalhe de um Agrupamento (Fase 2): a árvore de `EstruturaItem` dele, e o formulário para criar
 * a primeira Peça (nó de topo, copiando a receita do catálogo — `MontagemDeEstruturaUseCase`).
 *
 * **Sem `obterAgrupamento` de propósito.** O brief da Task 8 escopa as interfaces consumidas a
 * `estrutura.ts` (Task 6) + `ArvoreDeEstrutura` (Task 7) + `SeletorComBusca`/`usePodeEscrever` —
 * `cadastros.ts` não entra na lista, e `AgrupamentoDto` não tem um `obterAgrupamento(id)` hoje
 * (só `listarAgrupamentos`/`criarAgrupamento`/`excluirAgrupamento`). Buscar o cabeçalho do
 * Agrupamento (código/tipo) exigiria uma chamada nova fora do escopo desta task — decisão de
 * design, não esquecimento. O título usa o `id` da rota, disponível desde o primeiro frame sem
 * depender de nenhuma resposta de rede (molde de `ComponenteDetalhePage`, título fixo).
 *
 * Acrescentar Item filho, editar nó e excluir nó (as outras três ações de `ArvoreDeEstrutura`)
 * ficam de fora desta task — o brief só pede o formulário de criação da Peça (nó de topo) e a
 * leitura da árvore. `onAcrescentarFilho`/`onEditar`/`onExcluir` não são passados, e a árvore
 * então não desenha ação nenhuma por nó — comportamento coberto pela própria suíte de
 * `ArvoreDeEstrutura` ("sem permissão de escrita, as ações não são renderizadas" cobre `podeEscrever
 * && (onX...)` para qualquer combinação sem callback).
 */
export function AgrupamentoDetalhePage() {
  const { id } = useParams<{ id: string }>()
  const agrupamentoId = Number(id)
  const idValido = !Number.isNaN(agrupamentoId)

  const [nos, setNos] = useState<NoDaEstrutura[]>([])
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const [componente, setComponente] = useState<ComponenteDto | null>(null)
  const [quantidade, setQuantidade] = useState('')
  const [requerRelatorioDimensional, setRequerRelatorioDimensional] = useState(false)
  const [enviando, setEnviando] = useState(false)
  // Estado PRÓPRIO da escrita, separado de `erro` (que é só da carga) — Important 1 do fix pass da
  // Task 8: com um estado só compartilhado entre carga e escrita, um erro ao criar a Peça (o 409 de
  // ciclo, por exemplo) marcava o MESMO `erro` que a guarda `erro === null &&` do ramo da árvore
  // usa para decidir se mostra `<ArvoreDeEstrutura>` — e apagava a árvore já carregada, justo no
  // cenário em que o usuário mais precisa dela (a mensagem do ciclo existe para ensinar como
  // consertar a receita, e ele precisa ver a árvore para aplicar o conserto). Molde de
  // `ComponenteDetalhePage`, que já separa `erroComponente` de `erroSalvarFilhos`/etc.
  const [erroEscrita, setErroEscrita] = useState<string | null>(null)

  const podeEscrever = usePodeEscrever('estrutura')

  // Recebe o id como argumento (não fecha sobre `agrupamentoId` de fora): mesmo motivo do
  // comentário equivalente em `PedidoDetalhePage.tsx` — o exhaustive-deps cobra 'carregar' como
  // dependência faltando se o corpo não usar o parâmetro.
  async function carregar(id: number) {
    setCarregando(true)
    try {
      const dados = await obterEstrutura(id)
      setNos(dados)
    } catch (e) {
      setErro(mensagemDeErro(e, 'Não foi possível carregar a estrutura.'))
    } finally {
      setCarregando(false)
    }
  }

  // m4 do fix pass da Task 8: com `:id` não numérico (`/agrupamentos/abc` -> `NaN`) não há
  // agrupamento nenhum para buscar — sem a guarda, o efeito ainda disparava
  // `GET /agrupamentos/NaN/estrutura`. Molde de `ComponenteDetalhePage` (`idValido`).
  useEffect(() => { if (idValido) carregar(agrupamentoId) }, [agrupamentoId, idValido])

  async function salvar(e: FormEvent) {
    e.preventDefault()
    if (!componente) return
    setErroEscrita(null)
    setEnviando(true)
    try {
      const resultado = await criarPeca(agrupamentoId, {
        componenteId: componente.id,
        quantidade: Number(quantidade),
        requerRelatorioDimensional,
      })
      if (ehConflitoDeEstrutura(resultado)) {
        // `mensagem` nomeia o caminho do ciclo quando o código é `CicloNaReceita` (sempre vem
        // nesse caso — ver o comentário de `ConflitoDeEstrutura` em `api/estrutura.ts`); os
        // outros três códigos podem chegar sem `mensagem`, daí o fallback genérico.
        setErroEscrita(resultado.mensagem ?? 'Não foi possível criar a Peça: conflito na estrutura.')
        return
      }
      setComponente(null)
      setQuantidade('')
      setRequerRelatorioDimensional(false)
      await carregar(agrupamentoId)
    } catch (e) {
      // I2 do fix pass da Task 8: este `catch` é o único caminho que transforma um 403 em texto na
      // tela ("Seu perfil não tem permissão para esta ação.") em vez de deixar a exceção subir — o
      // 403 é a fronteira REAL (adendo F2), esconder o formulário no front não é segurança.
      setErroEscrita(mensagemDeErro(e, 'Não foi possível criar a Peça.'))
    } finally {
      setEnviando(false)
    }
  }

  // m4 do fix pass da Task 8: `id` inválido não tenta nenhuma busca (a guarda do `useEffect`
  // acima) e mostra só este banner — mesmo molde de `ComponenteDetalhePage`.
  if (!idValido) {
    return (
      <Pagina titulo="Agrupamento">
        <BannerDeErro mensagem="Este agrupamento não existe." />
      </Pagina>
    )
  }

  return (
    <Pagina titulo={`Agrupamento ${agrupamentoId}`}>
      {podeEscrever && (
        <form onSubmit={salvar} className="flex flex-col gap-4 rounded-lg border border-borda bg-superficie p-4">
          <div className="grid gap-4 sm:grid-cols-[1fr_auto]">
            <SeletorComBusca
              rotulo="Componente"
              valorSelecionado={componente}
              aoSelecionar={setComponente}
            />
            <Campo rotulo="Quantidade">
              {(idDoCampo) => (
                <input
                  id={idDoCampo}
                  type="number"
                  step="any"
                  value={quantidade}
                  onChange={(e) => setQuantidade(e.target.value)}
                  className={CLASSES_DE_CONTROLE}
                />
              )}
            </Campo>
          </div>
          <label className="flex items-center gap-2 text-sm text-tinta-fraca">
            <input
              type="checkbox"
              checked={requerRelatorioDimensional}
              onChange={(e) => setRequerRelatorioDimensional(e.target.checked)}
              className="size-4 accent-acao"
            />
            Requer relatório dimensional
          </label>
          <Botao
            type="submit"
            carregando={enviando}
            rotuloCarregando="Salvando…"
            disabled={!componente || !(Number(quantidade) > 0)}
            className="self-start"
          >
            Criar Peça
          </Botao>
        </form>
      )}

      {/* `erro` (carga) e `erroEscrita` (escrita) nunca aparecem ao mesmo tempo na prática — a
          escrita só é tentada depois de a carga assentar —, mas o banner não precisa saber disso:
          mostra o que houver. */}
      <BannerDeErro mensagem={erro ?? erroEscrita} />

      {carregando ? (
        <EstadoCarregando />
      ) : erro === null && nos.length === 0 ? (
        // Sem busca nesta tela (diferente de `SeletorComBusca`), então não há "não achei" a
        // distinguir de "não há nada" — só existe UM caminho para a lista vazia: a Peça ainda não
        // foi criada. `erro === null &&` continua obrigatório pelo mesmo motivo do Critical (C1)
        // que `PedidoDetalhePage`/`ComponenteDetalhePage` já pagaram: sem ele, uma falha de rede
        // deixaria `nos` em `[]` e este estado vazio apareceria JUNTO do banner de erro.
        <EstadoVazio
          titulo="Este agrupamento ainda não tem estrutura"
          descricao={podeEscrever ? 'Use o formulário acima para criar a primeira Peça.' : undefined}
        />
      ) : (
        erro === null && <ArvoreDeEstrutura nos={nos} podeEscrever={podeEscrever} />
      )}
    </Pagina>
  )
}
