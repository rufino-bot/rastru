export type Recurso = 'setores' | 'materiais' | 'componentes' | 'pedidos' | 'agrupamentos' | 'estrutura'

/**
 * Espelho dos `[Authorize(Roles = …)]` do backend, conferidos no disco em 2026-08-10.
 *
 * **Isto é conveniência de interface, não segurança.** A autorização real é do backend e continua
 * sendo: esconder um botão não impede requisição nenhuma. O que esta tabela evita é o usuário
 * preencher um formulário inteiro para receber 403 no fim.
 *
 * A divergência com o backend é silenciosa nos dois sentidos — liberar demais dá 403 no fim do
 * formulário (chato, visível); liberar de menos some com a ação para quem tinha direito a ela
 * (invisível, e o suspeito natural vira o backend, que está certo). `permissoesEspelhamOBackend.test.ts`
 * lê os controllers e compara os VALORES dos atributos `[Authorize(Roles = …)]` que existem — ela
 * não vê a REMOÇÃO de um atributo (medido em 2026-08-10: tirando o `[Authorize]` do `POST` de
 * `SetoresController`, a guarda do front continua 11/11 verde). Quem pega essa remoção é a suíte
 * .NET (`SetoresEndpointsTests.cs`), não esta guarda.
 */
const ESCRITA: Readonly<Record<Recurso, readonly string[]>> = {
  setores: ['Administrador'],
  materiais: ['Administrador'],
  // `componentes` cobre TAMBÉM a receita padrão (filhos, materiais e roteiro do Componente): é um
  // conceito de permissão só, e não dois — o backend declara os mesmos perfis nos dois controllers
  // de propósito. A tela da receita usa `usePodeEscrever('componentes')`; não existe
  // `'receitaPadrao'`, e a razão de não existir está no comentário do mapa de
  // `permissoesEspelhamOBackend.test.ts`, junto da guarda que fica vermelha se os dois divergirem.
  componentes: ['Administrador', 'PCP'],
  pedidos: ['PCP', 'Administrador'],
  agrupamentos: ['PCP', 'Administrador'],
  // A árvore real de um Agrupamento. `Recurso` PRÓPRIO, e não carona em `agrupamentos`: os dois têm
  // os mesmos perfis hoje, mas são conceitos separados — criar um Agrupamento vazio e montar a
  // árvore dele são ações distintas, e a primeira vez que os perfis divergirem (a Fase 3 mexe em
  // quem aponta setor) a carona seria descoberta como bug, não como decisão.
  estrutura: ['PCP', 'Administrador'],
}

export function podeEscrever(perfil: string, recurso: Recurso): boolean {
  return ESCRITA[recurso].includes(perfil)
}

/** Exportado só para a guarda de espelhamento. Não use em tela: use `podeEscrever`. */
export const TABELA_DE_ESCRITA = ESCRITA
