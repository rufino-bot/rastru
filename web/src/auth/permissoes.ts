export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos'

/**
 * Espelho dos `[Authorize(Roles = …)]` do backend, conferidos no disco em 2026-08-10.
 *
 * **Isto é conveniência de interface, não segurança.** A autorização real é do backend e continua
 * sendo: esconder um botão não impede requisição nenhuma. O que esta tabela evita é o usuário
 * preencher um formulário inteiro para receber 403 no fim.
 *
 * A divergência com o backend é silenciosa nos dois sentidos — liberar demais dá 403 no fim do
 * formulário (chato, visível); liberar de menos some com a ação para quem tinha direito a ela
 * (invisível, e o suspeito natural vira o backend, que está certo). Por isso ela não é vigiada
 * por leitura: `permissoesEspelhamOBackend.test.ts` lê os controllers e compara.
 */
const ESCRITA: Record<Recurso, readonly string[]> = {
  setores: ['Administrador'],
  materiais: ['Administrador'],
  componentes: ['Administrador', 'PCP'],
  pedidos: ['PCP', 'Administrador'],
  agrupamentos: ['PCP', 'Administrador'],
}

export function podeEscrever(perfil: string, recurso: Recurso): boolean {
  return ESCRITA[recurso].includes(perfil)
}

/** Exportado só para a guarda de espelhamento. Não use em tela: use `podeEscrever`. */
export const TABELA_DE_ESCRITA = ESCRITA
