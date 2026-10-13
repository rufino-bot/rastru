import { BannerDeErro } from '../components/BannerDeErro'
import { Botao } from '../components/Botao'

interface Props {
  /** O texto do botão de enviar, que quem usa a barra monta ("Iniciar 5 itens", "Entregar 1 Kit"…). */
  rotulo: string
  /** O texto do botão enquanto a requisição está em voo ("Registrando…", "Entregando…"). */
  rotuloCarregando: string
  /** Marcados que o filtro esconde: vão no lote assim mesmo (desvio D8). */
  ocultos: number
  /** Algum marcado com quantidade inválida ou bloqueado: o botão não envia. */
  invalido: boolean
  enviando: boolean
  erro: string | null
  aoEnviar: () => void
  aoLimpar: () => void
}

/**
 * A barra fixa do lote (desvios D11 e D12 do plano 2 dos filtros), usada pela fila do Setor e pelas
 * Tarefas. Só existe enquanto há seleção — quem a usa decide isso —, e só usa tinta e superfície:
 * marcar linhas não é estado de negócio, então nenhuma cor de estado aparece fora do banner da
 * recusa. O texto do botão vem de quem a usa, porque cada tela tem o seu verbo.
 */
export function BarraDoLote({ rotulo, rotuloCarregando, ocultos, invalido, enviando, erro, aoEnviar, aoLimpar }: Props) {
  return (
    <div className="sticky bottom-0 flex flex-col gap-3 border-t border-borda bg-superficie py-3">
      <BannerDeErro mensagem={erro} />
      <div className="flex flex-wrap items-center gap-3">
        <Botao onClick={aoEnviar} carregando={enviando} rotuloCarregando={rotuloCarregando} disabled={invalido}>
          {rotulo}
        </Botao>
        <Botao variante="secundario" onClick={aoLimpar} disabled={enviando}>Limpar seleção</Botao>
      </div>
      {ocultos > 0 && (
        <p className="text-sm text-tinta-fraca">
          {ocultos === 1 ? '1 marcado oculto pelo filtro' : `${ocultos} marcados ocultos pelo filtro`}
        </p>
      )}
    </div>
  )
}
