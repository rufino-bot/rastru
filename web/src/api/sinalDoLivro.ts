type OuvinteDoLivro = () => void
const ouvintes = new Set<OuvinteDoLivro>()

/**
 * Escuta "o livro mudou": toda escrita que pode mudar as Tarefas — as da execução (`execucao.ts`) e
 * a edição e a exclusão de nó (`estrutura.ts`), aceitas pelo servidor ou respondidas com 409 (o que
 * estava na tela ficou velho). Devolve a função que para de escutar.
 *
 * Existe para o contador de Tarefas do menu, que tem carga própria a cada 30 s: as telas recarregam
 * os próprios dados depois de cada ação, mas ninguém avisava o contador, e a tela de Tarefas já dizia
 * "Nenhum item pronto" com o menu ainda mostrando 1 (achado na verificação manual da Fase 3).
 *
 * Limite: ação feita em OUTRO aparelho não passa por aqui — para essa, continua valendo o ciclo de
 * 30 s. Escrita nova que mude as Tarefas precisa chamar `avisarQueOLivroMudou`; nada a obriga.
 */
export function aoMudarOLivro(ouvinte: OuvinteDoLivro): () => void {
  ouvintes.add(ouvinte)
  return () => { ouvintes.delete(ouvinte) }
}

/**
 * Cada ouvinte roda isolado: o aviso sai DEPOIS de a escrita ser gravada, e um ouvinte com defeito
 * não pode impedir os outros nem fazer a escrita rejeitar — o operador leria como recusa uma
 * operação que o servidor aceitou. Itera sobre uma cópia porque um ouvinte pode se desinscrever
 * durante o aviso.
 */
export function avisarQueOLivroMudou(): void {
  for (const ouvinte of [...ouvintes]) {
    try {
      ouvinte()
    } catch {
      // Engolido de propósito — ver o comentário da função.
    }
  }
}
