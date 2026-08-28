import { useEffect, useId, useState, type FocusEvent, type KeyboardEvent } from 'react'
import { listarComponentes, type ComponenteDto } from '../api/cadastros'
import { mensagemDeErro } from '../api/erros'
import { useBuscaPaginada } from '../hooks/useBuscaPaginada'
import { BannerDeErro } from './BannerDeErro'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'
import { EstadoCarregando } from './EstadoCarregando'
import { EstadoVazio } from './EstadoVazio'

interface Props {
  rotulo: string
  valorSelecionado: ComponenteDto | null
  aoSelecionar: (componente: ComponenteDto) => void
}

/** Nenhuma opção destacada. `Enter` nesse estado não seleciona nada — não há "o destacado". */
const NENHUM_DESTAQUE = -1

/** O texto que o campo mostra em repouso, quando há seleção. */
function rotuloDe(componente: ComponenteDto | null): string {
  return componente ? `${componente.codigo} — ${componente.descricao}` : ''
}

/**
 * Combobox de catálogo paginado: o usuário digita, a lista filtra pelo servidor, e a escolha sai
 * por `aoSelecionar`. Nasceu para a receita padrão (Fase 1C), onde escolher um Componente filho
 * entre dezenas não cabe num `<select>` de lista fechada.
 *
 * ## O duplo papel do campo, e como ele foi resolvido
 *
 * O mesmo `<input>` é BUSCA (o que o usuário digita) e DISPLAY (o rótulo do que já está escolhido).
 * Deixar os dois papéis implícitos é o que torna esse tipo de componente ambíguo no meio do
 * caminho, então a regra aqui é explícita: existe um `rascunho`.
 *
 * - `rascunho === null` → o campo está EM REPOUSO e mostra o rótulo do selecionado (ou vazio).
 * - `rascunho !== null` → o usuário está EDITANDO, e o campo mostra exatamente o que ele digitou.
 *
 * Daí saem as respostas aos casos-limite, todas pelo mesmo caminho (`voltarAoRepouso`):
 *
 * - **Digitar com seleção feita** substitui o rótulo pelo texto digitado. A seleção continua de pé;
 *   digitar é buscar, não desfazer.
 * - **Apagar tudo** deixa o rascunho `''` (lista inteira), e NÃO limpa a seleção: `aoSelecionar`
 *   só sabe dizer "escolhi este", não "não escolhi nada" — sem um `null` na assinatura, limpar
 *   seria uma decisão que o componente não tem como comunicar ao pai.
 * - **`Esc` depois de digitar** descarta o rascunho: o campo volta a mostrar o rótulo do
 *   selecionado. O texto digitado não sobrevive ao fechamento, senão o campo ficaria mostrando
 *   uma busca antiga como se fosse a escolha atual.
 * - **Perder o foco** faz o mesmo que `Esc`. A consequência é a propriedade que interessa: fora do
 *   modo de edição, o que está escrito no campo é SEMPRE a seleção real.
 *
 * `voltarAoRepouso` também zera o filtro do servidor. Sem isso o campo mostraria o rótulo do
 * selecionado enquanto a lista continuaria filtrada pelo texto anterior — um filtro invisível, que
 * o usuário lê como "sumiram itens do catálogo".
 *
 * ## O painel não nasce aberto
 *
 * Abre com foco, clique, digitação ou seta; fecha com `Esc`, blur ou seleção. A Task 11 põe mais de
 * um destes na mesma tela, e um painel aberto por montagem apareceria por cima do resto sem
 * ninguém ter pedido.
 */
export function SeletorComBusca({ rotulo, valorSelecionado, aoSelecionar }: Props) {
  const busca = useBuscaPaginada({ buscar: listarComponentes })
  const [aberto, setAberto] = useState(false)
  const [rascunho, setRascunho] = useState<string | null>(null)
  const [destaque, setDestaque] = useState(NENHUM_DESTAQUE)
  const id = useId()
  const idDaLista = `${id}-lista`
  const idDaOpcao = (indice: number) => `${id}-opcao-${indice}`

  const itens = busca.itens

  // Lista nova, destaque zerado. Sem isto o índice 2 de uma lista de três continuaria destacado
  // depois de a busca devolver uma lista de um item só — e `Enter` selecionaria outra coisa, ou
  // coisa nenhuma. `itens` só troca de identidade quando uma carga assenta.
  useEffect(() => { setDestaque(NENHUM_DESTAQUE) }, [itens])

  function voltarAoRepouso() {
    setAberto(false)
    setRascunho(null)
    setDestaque(NENHUM_DESTAQUE)
    // Só quando há filtro a zerar: senão todo blur do campo dispararia uma busca nova.
    if (busca.textoDaBusca !== '') busca.mudarBusca('')
  }

  function selecionar(componente: ComponenteDto) {
    aoSelecionar(componente)
    voltarAoRepouso()
  }

  function aoTeclar(evento: KeyboardEvent<HTMLInputElement>) {
    if (evento.key === 'ArrowDown') {
      // `preventDefault` para a seta não levar o cursor ao fim do texto enquanto anda na lista.
      evento.preventDefault()
      setAberto(true)
      setDestaque((atual) => Math.min(atual + 1, itens.length - 1))
      return
    }
    if (evento.key === 'ArrowUp') {
      evento.preventDefault()
      setAberto(true)
      setDestaque((atual) => Math.max(atual - 1, 0))
      return
    }
    if (evento.key === 'Enter') {
      if (!aberto || destaque < 0 || destaque >= itens.length) return
      // Sem isto, escolher com Enter enviaria o formulário da tela junto.
      evento.preventDefault()
      selecionar(itens[destaque])
      return
    }
    if (evento.key === 'Escape') {
      evento.preventDefault()
      voltarAoRepouso()
    }
  }

  // O blur borbulha até aqui (React o propaga). A checagem de `relatedTarget` deixa o painel aberto
  // quando o foco só andou para dentro do próprio widget. Ela NÃO cobre o clique numa opção: a
  // opção não é focável, então o `relatedTarget` do blur seria `null` e o painel fecharia antes de
  // o clique registrar. Quem cobre esse caso é o `preventDefault` do `mousedown` na opção, abaixo,
  // que impede o blur de acontecer. NÃO MEDIDO em teste: o jsdom não executa a ação padrão do
  // `mousedown` (o `fireEvent.click` do teste de clique não dispara foco nem blur), então nenhuma
  // das duas guardas é exercitada pela suíte — é desenho vindo do padrão de combobox da WAI-ARIA,
  // não sintoma observado aqui.
  function aoPerderFoco(evento: FocusEvent<HTMLDivElement>) {
    if (evento.currentTarget.contains(evento.relatedTarget)) return
    voltarAoRepouso()
  }

  return (
    <div className="relative" onBlur={aoPerderFoco}>
      <Campo rotulo={rotulo}>
        {(idDoCampo) => (
          <input
            id={idDoCampo}
            type="text"
            role="combobox"
            autoComplete="off"
            className={CLASSES_DE_CONTROLE}
            placeholder="Busque por código ou descrição"
            value={rascunho ?? rotuloDe(valorSelecionado)}
            aria-expanded={aberto}
            aria-controls={aberto ? idDaLista : undefined}
            aria-autocomplete="list"
            aria-activedescendant={destaque >= 0 ? idDaOpcao(destaque) : undefined}
            onChange={(e) => {
              setRascunho(e.target.value)
              busca.mudarBusca(e.target.value)
              setAberto(true)
            }}
            onFocus={() => setAberto(true)}
            onClick={() => setAberto(true)}
            onKeyDown={aoTeclar}
          />
        )}
      </Campo>

      {aberto && (
        <div className="absolute z-10 mt-1 max-h-72 w-full overflow-auto rounded-lg border border-borda bg-superficie p-1 shadow-lg">
          <BannerDeErro
            mensagem={
              busca.erro == null
                ? null
                : mensagemDeErro(busca.erro, 'Não foi possível carregar os componentes.')
            }
          />
          {busca.carregando && <EstadoCarregando />}
          {!busca.carregando && busca.erro == null && itens.length === 0 && (
            <EstadoVazio
              titulo="Nenhum componente encontrado."
              // O que distingue "não achei" de "não há nada": a primeira frase cita o que foi
              // procurado, a segunda diz que o catálogo está vazio.
              descricao={
                busca.busca === ''
                  ? 'Não há componente ativo cadastrado.'
                  : `Nada corresponde a “${busca.busca}”.`
              }
            />
          )}
          <ul role="listbox" id={idDaLista} aria-label={rotulo}>
            {itens.map((item, indice) => (
              <li
                key={item.id}
                id={idDaOpcao(indice)}
                role="option"
                aria-selected={item.id === valorSelecionado?.id}
                // Ver o comentário de `aoPerderFoco`: é isto que impede o blur de fechar o painel
                // antes de o clique chegar na opção.
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => selecionar(item)}
                className={`flex cursor-pointer flex-col rounded-md px-3 py-2 ${
                  indice === destaque ? 'bg-acao text-superficie' : 'text-tinta'
                }`}
              >
                <span className="font-mono text-sm">{item.codigo}</span>
                <span className={indice === destaque ? 'text-sm' : 'text-sm text-tinta-fraca'}>
                  {item.descricao}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
