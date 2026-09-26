import { useCallback, useEffect, useRef, useState } from 'react'
import { mensagemDeErro } from '../api/erros'

/** Fila, tarefas e o contador do menu se atualizam a cada 30 s (spec da Fase 3 §6.2). */
export const INTERVALO_DA_EXECUCAO_MS = 30_000

export interface CargaPeriodica<T> {
  /** `null` até a primeira resposta boa da `chave` atual. */
  dados: T | null
  /** Só a PRIMEIRA carga de cada `chave` — a atualização periódica é silenciosa, sem piscar a tela. */
  carregando: boolean
  /** Já traduzido por `mensagemDeErro`. */
  erro: string | null
  /** Busca de novo agora (depois de uma ação, ou de um 409). */
  recarregar: () => Promise<void>
}

/**
 * Carga de uma tela da execução que se atualiza sozinha (spec §6.2: fila e tarefas a cada 30 s
 * enquanto a tela está aberta, e na hora depois de cada ação).
 *
 * Três decisões, cada uma com teste em `useCargaPeriodica.test.tsx`:
 *
 * 1. **Vence a última requisição ENVIADA**, não a última a responder (o mesmo cancelamento por
 *    sequência de `useBuscaPaginada`). Sem isto, trocar de `/fila/1` para `/fila/2` e receber a
 *    resposta atrasada do Setor 1 mostraria a fila do Setor 1 sob o título do Setor 2.
 * 2. **Falha de atualização mantém os dados.** O wifi da fábrica cai; uma lista que some a cada
 *    30 s é pior que uma lista com o aviso de que não atualizou. A carga INICIAL que falha não tem
 *    dado nenhum a manter. (A regra da Home, spec da Fase 1E §3.4, de não mostrar dado velho ao lado
 *    do erro, é daquela seção; aqui o dado velho é seguro porque toda escrita revalida no servidor
 *    e o 409 recarrega.)
 * 3. **`chave` nova zera tudo** e carrega do zero — dado de outro Setor não é "dado velho", é dado
 *    errado.
 *
 * `intervaloMs` `null` desliga a atualização periódica.
 */
export function useCargaPeriodica<T>(
  buscar: () => Promise<T>,
  chave: string | number,
  intervaloMs: number | null,
  mensagemDeFalha: string,
): CargaPeriodica<T> {
  const [dados, setDados] = useState<T | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const sequenciaRef = useRef(0)
  const buscarRef = useRef(buscar)
  const mensagemRef = useRef(mensagemDeFalha)
  useEffect(() => {
    buscarRef.current = buscar
    mensagemRef.current = mensagemDeFalha
  })

  const executar = useCallback(async () => {
    const minha = ++sequenciaRef.current
    try {
      const resposta = await buscarRef.current()
      if (minha !== sequenciaRef.current) return
      setDados(resposta)
      setErro(null)
    } catch (e) {
      if (minha !== sequenciaRef.current) return
      setErro(mensagemDeErro(e, mensagemRef.current))
    } finally {
      if (minha === sequenciaRef.current) setCarregando(false)
    }
  }, [])

  useEffect(() => {
    setDados(null)
    setErro(null)
    setCarregando(true)
    executar()
    if (intervaloMs === null) return
    const id = setInterval(executar, intervaloMs)
    return () => clearInterval(id)
  }, [chave, intervaloMs, executar])

  return { dados, carregando, erro, recarregar: executar }
}
