// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, cleanup, fireEvent, act } from '@testing-library/react'
import { useBuscaPaginada, type FiltroDeBusca, type PaginaDeBusca } from './useBuscaPaginada'

afterEach(cleanup)

interface Item { id: number; nome: string }

// Componente-hospedeiro: um hook não renderiza nada, então a prova passa por uma tela mínima que
// expõe cada saída num nó com texto asserível. Deliberadamente burro — se ele tiver lógica, o
// teste passa a provar o hospedeiro, não o hook.
function Hospedeiro({ buscar, atraso = 300, tamanhoInicial }: {
  buscar: (f: FiltroDeBusca) => Promise<PaginaDeBusca<Item>>
  atraso?: number
  tamanhoInicial?: number
}) {
  const b = useBuscaPaginada<Item>({ buscar, atrasoDoDebounce: atraso, tamanhoInicial })
  return (
    <div>
      <input aria-label="busca" value={b.textoDaBusca} onChange={(e) => b.mudarBusca(e.target.value)} />
      <input aria-label="inativos" type="checkbox" checked={b.incluirInativos}
             onChange={(e) => b.mudarInativos(e.target.checked)} />
      <select aria-label="tamanho" value={b.tamanho} onChange={(e) => b.mudarTamanho(Number(e.target.value))}>
        <option value={20}>20</option>
        <option value={50}>50</option>
      </select>
      <button onClick={() => b.irParaPagina(b.pagina + 1)}>Próxima</button>
      <button onClick={() => { void b.recarregar() }}>Recarregar</button>
      <p>pagina:{b.pagina}</p>
      <p>total:{b.total}</p>
      <p>paginas:{b.totalDePaginas}</p>
      {b.carregando && <p>carregando</p>}
      {b.erro !== null && <p>erro</p>}
      <ul>{b.itens.map((i) => <li key={i.id}>{i.nome}</li>)}</ul>
    </div>
  )
}

function pagina(itens: Item[], total = itens.length): PaginaDeBusca<Item> {
  return { itens, total }
}

// Timers falsos com `advanceTimersByTimeAsync`, que avança o relógio E drena as microtasks — sem
// ele o `await` da resposta ficaria pendurado e o teste leria um estado intermediário.
// `findBy*` NÃO é usado neste arquivo, de propósito: ele depende do `waitFor`, que tem seu próprio
// relacionamento com timers falsos. Avançar explicitamente e ler com `getBy*` é determinístico.
async function avancar(ms: number) {
  await act(async () => { await vi.advanceTimersByTimeAsync(ms) })
}

describe('useBuscaPaginada', () => {
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => { vi.useRealTimers() })

  it('carrega na montagem e mostra os itens', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([{ id: 1, nome: 'Corte' }]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('Corte')).toBeTruthy()
    expect(buscar).toHaveBeenCalledTimes(1)
    expect(buscar).toHaveBeenCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 20 })
  })

  it('faz UMA requisição para três teclas digitadas em sequência', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(buscar).toHaveBeenCalledTimes(1) // só a carga inicial

    const campo = screen.getByLabelText('busca')
    fireEvent.change(campo, { target: { value: 'S' } })
    fireEvent.change(campo, { target: { value: 'SU' } })
    fireEvent.change(campo, { target: { value: 'SUP' } })

    // Antes de o debounce vencer, nada saiu além da carga da montagem (ainda 1). As TRÊS teclas
    // digitadas viram UMA única consulta: o total ao fim deste teste (linha abaixo, `:86`) é 2 —
    // a da montagem mais essa única consulta debounced —, não uma chamada por tecla.
    await avancar(299)
    expect(buscar).toHaveBeenCalledTimes(1)

    await avancar(1)
    expect(buscar).toHaveBeenCalledTimes(2)
    expect(buscar).toHaveBeenLastCalledWith({ busca: 'SUP', incluirInativos: false, pagina: 1, tamanho: 20 })
  })

  it('mostra o texto digitado no campo imediatamente, sem esperar o debounce', async () => {
    // O debounce atrasa a REQUISIÇÃO, nunca o campo. Um campo que só atualiza depois de 300 ms é
    // percebido como teclado travado.
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'SUP' } })

    expect((screen.getByLabelText('busca') as HTMLInputElement).value).toBe('SUP')
  })

  it('ignora a resposta de uma busca que já foi superada por outra', async () => {
    // A corrida real: "SU" demora muito e "SUP" responde na hora. Sem guarda de sequência, a
    // resposta de "SU" chega DEPOIS e sobrescreve a lista — campo mostrando SUP, lista mostrando o
    // resultado de SU. Foi provado empiricamente na review da Task 6.
    //
    // `ordemDeResposta` não é enfeite: sem ela, `queryByText('RESULTADO DE SU')` ser nulo passaria
    // igualmente num teste em que a resposta atrasada NUNCA chegou — provando nada. Ela afirma que
    // a resposta obsoleta de fato chegou, e chegou POR ÚLTIMO.
    const ordemDeResposta: string[] = []
    const buscar = vi.fn((f: FiltroDeBusca) => {
      if (f.busca === 'SU') {
        return new Promise<PaginaDeBusca<Item>>((resolve) => {
          // 1000 ms, e não 200: o `avancar(300)` que dispara "SUP" avança 300 ms de relógio de uma
          // vez, então uma resposta de "SU" em 200 ms chega DENTRO dessa janela — antes de "SUP" —
          // e a corrida some. Medido nesta task: com 200 ms, apagar a guarda de sequência do bloco
          // de sucesso do hook deixava os 13 testes verdes (mutação M2 sobrevivente).
          setTimeout(() => {
            ordemDeResposta.push('SU')
            resolve(pagina([{ id: 1, nome: 'RESULTADO DE SU' }]))
          }, 1000)
        })
      }
      // Promessa já resolvida: o instante da chamada é o instante da resposta.
      ordemDeResposta.push(f.busca)
      return Promise.resolve(pagina([{ id: 2, nome: 'RESULTADO DE SUP' }]))
    })

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    const campo = screen.getByLabelText('busca')
    fireEvent.change(campo, { target: { value: 'SU' } })
    await avancar(300)   // dispara "SU", que só responde em +1000 ms
    fireEvent.change(campo, { target: { value: 'SUP' } })
    await avancar(300)   // dispara "SUP", que responde na hora
    await avancar(2000)  // deixa a resposta atrasada de "SU" enfim chegar

    expect(ordemDeResposta).toEqual(['', 'SUP', 'SU'])  // a obsoleta chegou, e chegou por último
    expect(screen.getByText('RESULTADO DE SUP')).toBeTruthy()
    expect(screen.queryByText('RESULTADO DE SU')).toBeNull()
  })

  it('volta para a página 1 quando a busca muda', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    expect(screen.getByText('pagina:2')).toBeTruthy()

    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'X' } })
    await avancar(300)

    expect(screen.getByText('pagina:1')).toBeTruthy()
  })

  it('volta para a página 1 quando o filtro de inativos muda', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(buscar).toHaveBeenLastCalledWith({ busca: '', incluirInativos: true, pagina: 1, tamanho: 20 })
  })

  it('volta para a página 1 quando o tamanho de página muda', async () => {
    // Os TRÊS resets têm teste próprio, e cada um exige uma montagem diferente. Confundir as
    // montagens foi o erro que deixou o terceiro reset sem prova na Fase 1B.
    const buscar = vi.fn().mockResolvedValue(pagina([], 100))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)

    fireEvent.change(screen.getByLabelText('tamanho'), { target: { value: '50' } })
    await avancar(0)

    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(buscar).toHaveBeenLastCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 50 })
  })

  it('recua a página quando o total encolhe e a página atual deixa de existir', async () => {
    // Clamp: 100 itens = 5 páginas, o usuário vai para a 3, alguém inativa 90 itens, agora são 10
    // itens = 1 página. Sem clamp a tela fica numa página vazia, com cara de bug.
    //
    // O gatilho da recarga é `recarregar()`, e NÃO o checkbox de inativos. Isto é a diferença entre
    // provar o clamp e não provar nada: `mudarInativos` chama `setPagina(1)` por conta própria, de
    // modo que a página volta para 1 com ou sem clamp. Medido nesta task — com o checkbox como
    // gatilho, apagar o `useEffect` do clamp inteiro deixava os 13 testes verdes (mutação M4
    // sobrevivente); o teste media o reset da M6, não o clamp. `recarregar()` recarrega SEM mexer
    // na página, que é a única forma de o total encolher debaixo de uma página que já não existe.
    let total = 100
    const buscar = vi.fn((f: FiltroDeBusca) =>
      Promise.resolve(pagina(f.pagina === 1 ? [{ id: 1, nome: 'Sobrou' }] : [], total)),
    )

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    fireEvent.click(screen.getByText('Próxima'))
    await avancar(0)
    expect(screen.getByText('pagina:3')).toBeTruthy()
    expect(screen.getByText('paginas:5')).toBeTruthy()

    total = 10
    fireEvent.click(screen.getByText('Recarregar'))
    await avancar(0)

    expect(screen.getByText('paginas:1')).toBeTruthy()
    expect(screen.getByText('pagina:1')).toBeTruthy()
    expect(screen.getByText('Sobrou')).toBeTruthy()
  })

  it('mostra "carregando" em TODA recarga, não só na primeira', async () => {
    // Este é o W3, deferido da Fase 1B com justificativa: `setCarregando(true)` no início de cada
    // carga não tinha prova nenhuma. Sem ele, depois da primeira carga o usuário nunca mais vê
    // sinal de atividade — a lista antiga fica parada na tela até a resposta nova trocar tudo de
    // repente. No wifi da fábrica são segundos olhando para dado errado sem saber.
    let liberar: (p: PaginaDeBusca<Item>) => void = () => {}
    const buscar = vi.fn()
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Antigo' }]))
      .mockImplementationOnce(() => new Promise<PaginaDeBusca<Item>>((r) => { liberar = r }))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.queryByText('carregando')).toBeNull()

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)
    expect(screen.getByText('carregando')).toBeTruthy()
    expect(screen.getByText('Antigo')).toBeTruthy()  // a lista antiga segue visível, mas AVISADA

    await act(async () => { liberar(pagina([{ id: 2, nome: 'Novo' }])) })
    expect(screen.queryByText('carregando')).toBeNull()
    expect(screen.getByText('Novo')).toBeTruthy()
  })

  it('mantém "carregando" quando a resposta obsoleta chega com outra ainda em voo', async () => {
    // A guarda de sequência do `finally`. Os outros testes nunca têm DUAS requisições em voo ao
    // mesmo tempo, então a resposta obsoleta sempre chega com o campo `carregando` já falso e
    // apagá-lo de novo não muda nada: medido nesta task, apagar a guarda do `finally` deixava os
    // 13 testes verdes (mutação M11 sobrevivente). Aqui a obsoleta chega ANTES da atual, e sem a
    // guarda ela apaga o "carregando" de uma requisição que ainda não respondeu — a tela diz
    // "pronto" mostrando dado velho.
    const liberadores: Array<(p: PaginaDeBusca<Item>) => void> = []
    const buscar = vi.fn()
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Inicial' }]))
      .mockImplementation(() => new Promise<PaginaDeBusca<Item>>((r) => { liberadores.push(r) }))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.queryByText('carregando')).toBeNull()

    fireEvent.click(screen.getByLabelText('inativos'))          // requisição A
    await avancar(0)
    fireEvent.change(screen.getByLabelText('tamanho'), { target: { value: '50' } })  // requisição B
    await avancar(0)
    expect(liberadores).toHaveLength(2)                          // as duas de fato em voo
    expect(screen.getByText('carregando')).toBeTruthy()

    await act(async () => { liberadores[0](pagina([{ id: 9, nome: 'OBSOLETO' }])) })

    expect(screen.getByText('carregando')).toBeTruthy()          // B ainda não respondeu
    expect(screen.queryByText('OBSOLETO')).toBeNull()

    await act(async () => { liberadores[1](pagina([{ id: 2, nome: 'ATUAL' }])) })

    expect(screen.queryByText('carregando')).toBeNull()
    expect(screen.getByText('ATUAL')).toBeTruthy()
  })

  it('expõe o erro e o limpa quando uma recarga posterior tem sucesso', async () => {
    const buscar = vi.fn()
      .mockRejectedValueOnce(new Error('rede caiu'))
      .mockResolvedValueOnce(pagina([{ id: 1, nome: 'Corte' }]))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)
    expect(screen.getByText('erro')).toBeTruthy()

    fireEvent.click(screen.getByLabelText('inativos'))
    await avancar(0)

    expect(screen.queryByText('erro')).toBeNull()
    expect(screen.getByText('Corte')).toBeTruthy()
  })

  it('sai do estado de carregando mesmo quando a busca falha', async () => {
    const buscar = vi.fn().mockRejectedValue(new Error('rede caiu'))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.queryByText('carregando')).toBeNull()
  })

  it('calcula totalDePaginas pelo total do servidor, não pelo tamanho da página recebida', async () => {
    // `total` é o total SOB O FILTRO, não `itens.length`. Confundir os dois faz a paginação sumir
    // exatamente quando ela é necessária.
    const buscar = vi.fn().mockResolvedValue(pagina([{ id: 1, nome: 'Corte' }], 41))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('paginas:3')).toBeTruthy()  // ceil(41 / 20)
  })

  it('mantém pelo menos uma página quando não há nada', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([], 0))

    render(<Hospedeiro buscar={buscar} />)
    await avancar(0)

    expect(screen.getByText('paginas:1')).toBeTruthy()
    expect(screen.getByText('pagina:1')).toBeTruthy()
  })

  it('respeita o atrasoDoDebounce passado, não o default de 300ms', async () => {
    // Hardcodar 300 no corpo do hook (ignorando o parâmetro) passaria se este teste só afirmasse
    // "disparou em 50ms" — 300 também "dispara" em algum momento. As DUAS pontas (não disparou
    // antes de 50, disparou aos 50) juntas são o que distingue um atraso de 50 real de um 300
    // hardcodado ou de um 0 hardcodado.
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} atraso={50} />)
    await avancar(0)
    expect(buscar).toHaveBeenCalledTimes(1) // só a carga inicial

    fireEvent.change(screen.getByLabelText('busca'), { target: { value: 'S' } })

    await avancar(49)
    expect(buscar).toHaveBeenCalledTimes(1) // ainda não venceu o atraso configurado (50)

    await avancar(1)
    expect(buscar).toHaveBeenCalledTimes(2) // venceu exatamente nos 50ms passados via prop
  })

  it('usa o tamanhoInicial passado na primeira requisição', async () => {
    const buscar = vi.fn().mockResolvedValue(pagina([]))

    render(<Hospedeiro buscar={buscar} tamanhoInicial={50} />)
    await avancar(0)

    expect(buscar).toHaveBeenCalledWith({ busca: '', incluirInativos: false, pagina: 1, tamanho: 50 })
  })

  it('não reentra em laço de carga quando o chamador passa `buscar` inline (identidade nova a cada render)', async () => {
    // O consumidor real (Task 9) provavelmente escreve `buscar` como lambda inline no corpo do
    // componente da tela — uma identidade NOVA a cada render, porque nada a memoiza. Sem o
    // `buscarRef`, essa identidade nova entraria nas deps de `carregar`: a carga inicial troca
    // `carregando`/`itens`/`total`, o componente re-renderiza, a lambda é recriada, `carregar`
    // muda de identidade, o efeito de carga refaz a chamada, e o ciclo não pára sozinho — é o
    // laço de render infinito descrito no brief deste fix pass.
    let chamadas = 0
    function HospedeiroComBuscaInline() {
      const buscarInline = () => {
        chamadas += 1
        return Promise.resolve(pagina([]))
      }
      useBuscaPaginada<Item>({ buscar: buscarInline })
      return null
    }

    render(<HospedeiroComBuscaInline />)
    await avancar(0)

    expect(chamadas).toBe(1)
  })
})
