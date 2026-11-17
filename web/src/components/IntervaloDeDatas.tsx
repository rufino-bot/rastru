import { Botao } from './Botao'
import { Campo, CLASSES_DE_CONTROLE } from './Campo'

interface Props {
  /** O título do filtro ("Prazo de entrega"); compõe o nome acessível dos dois campos. */
  rotulo: string
  /** Início do intervalo, ISO `aaaa-mm-dd` ou vazio. */
  de: string
  /** Fim do intervalo, ISO `aaaa-mm-dd` ou vazio. */
  ate: string
  aoMudar: (valor: { de: string; ate: string }) => void
}

/**
 * Um intervalo de datas: dois campos nativos, "de" e "até", e o "Limpar". Nativo porque no Android cada campo
 * abre o calendário do próprio celular. Controlado: não busca nada e não sabe de URL. Qualquer lado pode ficar
 * vazio, e os limites são inclusivos (quem filtra é a tela). Se `de` passar de `ate`, a primitiva **não troca** os
 * dois: devolve como foi digitado, e a tela mostra que nenhuma linha casa.
 */
export function IntervaloDeDatas({ rotulo, de, ate, aoMudar }: Props) {
  return (
    <fieldset className="flex flex-wrap items-end gap-3">
      <legend className="sr-only">{rotulo}</legend>
      <Campo rotulo={`${rotulo}: de`}>
        {(id) => (
          <input
            id={id}
            type="date"
            value={de}
            onChange={(e) => aoMudar({ de: e.target.value, ate })}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      <Campo rotulo={`${rotulo}: até`}>
        {(id) => (
          <input
            id={id}
            type="date"
            value={ate}
            onChange={(e) => aoMudar({ de, ate: e.target.value })}
            className={CLASSES_DE_CONTROLE}
          />
        )}
      </Campo>
      {(de || ate) && (
        <Botao variante="secundario" onClick={() => aoMudar({ de: '', ate: '' })}>
          Limpar
        </Botao>
      )}
    </fieldset>
  )
}
