# 06 - Roadmap / MVP

Fases pensadas para serem executadas em sequência com Claude Code — cada fase pode virar
uma sessão de agent com escopo fechado, referenciando os arquivos desta pasta como
contexto. Recomenda-se não avançar de fase sem os "pontos em aberto" da fase anterior
resolvidos (ou conscientemente adiados).

## Fase 0 — Setup do projeto

- Criar solution .NET (`Domain`, `Application`, `Infrastructure`, `Api`) conforme
  `03-arquitetura-tecnica.md`.
- Rodar `02-modelo-de-dados.sql` em um SQL Server local (Docker) e mapear entidades
  via EF Core (Database First), incluindo `Usuario`/`Perfil`.
- Implementar login (`POST /auth/login`) com emissão de JWT e claim de Perfil.
- Criar projeto React + TypeScript (Vite), estrutura de pastas inicial, tela de login,
  chamada de exemplo à API autenticada.
- Deploy manual (sem CI/CD por enquanto) — documentar passo a passo de publicação.

## Fase 1 — Cadastros básicos (CRUD)

- Setor, Material, Componente (catálogo) — CRUD simples.
- Pedido, Agrupamento — criação e listagem (sem regra de conclusão ainda).
- Critério de pronto: dá para cadastrar um Pedido com Agrupamentos vazios via tela.

> **1A concluída** (`Setor`, `Material`, `Pedido`, `Agrupamento` — CRUD pela tela, com
> autorização por perfil no backend). Falta **1B**: `Componente` + receita padrão
> (`ComponenteFilhoPadrao`, `ComponenteMaterialPadrao`, `ComponenteRoteiroPadrao`), que recebe
> plano próprio. Dívidas rastreadas de 1A: camada global de erro de API no front e gating de
> navegação por perfil.

## Fase 2 — Estrutura recursiva

- Criar `EstruturaItem` a partir de um `Componente` padrão (copiar receita) ou do zero
  (customizado).
- Visualização em árvore da estrutura de um Agrupamento (Peça → Itens → sub-Itens).
- Upload e exibição de `Componente.ArquivoSolido` (sólido 3D) e da regra de negócio que o
  exige por Peça de Pedido — regra 18 de `01`. Inclui `EstruturaItem.Descricao` (regra 19)
  e decidir onde fica o sólido de um item ad-hoc (ponto em aberto da regra 18).
- Como as colunas nasceram depois do banco de dev, aplicar os `ALTER` idempotentes de
  `ArquivoSolido`/`ArquivoFoto`/`Descricao` ao iniciar a fase, no mesmo padrão dos demais
  em `CLAUDE.md`.
- Critério de pronto: dá para montar visualmente a árvore completa de uma Peça complexa.

## Fase 3 — Rastreamento de setor

- Apontamento de entrada/saída de `EstruturaItem` em `Setor`.
- Validação de conservação de quantidade (soma em setores + expedido + perdido = total
  da Peça; na aplicação, não por índice filtrado).
- Tela de "fila do setor" para o operador.
- Critério de pronto: dá para acompanhar, item por item, em qual setor cada peça está.

## Fase 4 — Separação de materiais

- Registro de `MaterialSeparacao` vinculado a um `EstruturaItem`.
- Critério de pronto: dá para saber quais materiais já foram separados/entregues para
  cada item em fabricação.

## Fase 5 — Dimensional e fechamento

- Registro opcional de `RelatorioDimensional` por Peça (perfil Qualidade), avaliado por
  quantidade (`RelatorioDimensionalAvaliacao`).
- Registro de `Expedicao` (remessas parciais) e de `Perda` por Peça.
- Regra de fechamento de Agrupamento (todas as Peças concluídas — expedidas ou
  perdidas) e de Pedido (último Agrupamento concluído).
- Fluxo de abertura de Pedido de Retrabalho como ação **separada e opcional** a partir
  de uma reprovação, com `MotivoRetrabalho` obrigatório
  (`ReprovacaoDimensional`/`ErroInterno`/`SolicitacaoCliente`/`Perda`).
- Critério de pronto: fluxo ponta a ponta funcionando — cadastro → produção →
  expedição/perda → aprovação/reprovação → (se aplicável, e só quando o usuário decidir)
  retrabalho.

## Fase 6 — KPIs

- Endpoint e tela de tempo médio por setor, calculado a partir de `DataEntrada`
  (chegada) até `DataSaida`. `DataInicioExecucao` já existe no schema e pode ser
  adotado depois, sem migração, caso o negócio queira decompor fila x execução.
- Endpoint e tela de tempo total/fila/produção por pedido.
- Perfil Gestão tem acesso a essas telas; demais perfis não.

## Fora das fases — decidir por spike: busca de peça por foto

**Problema real que motiva:** a fábrica não etiqueta peça. O operador tem a peça na mão e
não sabe qual é nem de que Pedido — e o `Codigo` é obrigatório só do lado do sistema.

**O que torna isso viável e não era verdade antes:** o sólido 3D é obrigatório (regra 18),
então a geometria de referência já existe, é exata e não precisa ser fotografada por
ninguém. A comparação é silhueta contra silhueta, não aprendizado de máquina — descritores
de forma clássicos (momentos de Hu/Zernike, descritores de Fourier), sem treino.

**Forma proposta:** o custo fica no cadastro (renderizar N silhuetas do sólido e guardar um
descritor por vista); na consulta é só comparar números. Nenhuma biblioteca de CAD em
produção.

**Escopo proposto — ordenador, não identificador.** A foto **reordena a lista curta do
setor do operador** (a consulta da Fase 3), não busca no catálogo inteiro. Isso troca um
problema de 1-em-milhares por um de 1-em-dez, e o Pedido vem da lista, não da foto.
Não exibir "% de certeza": o score é *ranking*, não confiança, e exibido como confiança
convida o operador a não conferir.

**Limites que sobrevivem, mesmo dando certo:**
- escala é ambígua entre variantes dimensionais proporcionais, sem referência de tamanho na foto;
- peça espelhada (suporte esquerdo/direito) tem a mesma silhueta;
- a peça muda de forma ao longo do processo (blank plano → dobrado → soldado);
- geometria **não** identifica o Pedido — essa informação não está na peça.

**Critério para virar fase:** um spike de 1–2 dias sobre ~20 peças reais com seus sólidos,
fotografadas na condição de captura pretendida (fundo claro, maior superfície de contato à
mostra), medindo a **taxa de acerto no top-3 dentro de uma lista de setor**. É resultado
empírico — nenhuma análise substitui esse número. Só entra no roadmap depois dele.
