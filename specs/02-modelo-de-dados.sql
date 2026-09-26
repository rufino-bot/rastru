/* =====================================================================
   MODELO DE RASTREAMENTO DE PEÇAS - SQL Server (T-SQL)
   Camadas: Catálogo (receita padrão) > Pedido/Agrupamento > Estrutura real
            (árvore recursiva) > Execução/rastreamento > Dimensional

   Observação: o schema não usa mais índices filtrados; a flag -I do sqlcmd
   deixou de ser obrigatória (é inofensiva se mantida).
   ===================================================================== */

/* ---------------------------------------------------------------------
   TABELAS DE APOIO
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Setor (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Nome            NVARCHAR(100)       NOT NULL,
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Setor_Ativo DEFAULT (1),
    CONSTRAINT PK_Setor PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Setor_Nome UNIQUE (Nome)
);

CREATE TABLE dbo.Material (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL, -- identificador unico do material NESTE sistema; alfanumerico, atribuido por quem cadastra. Numeracao de fornecedor nao e modelada aqui (ver glossario em 01)
    Descricao       NVARCHAR(200)       NOT NULL,
    UnidadeMedida   NVARCHAR(10)        NOT NULL, -- ex: UN, M, KG, M2
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Material_Ativo DEFAULT (1),
    CONSTRAINT PK_Material PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Material_Codigo UNIQUE (Codigo)
);

/* ---------------------------------------------------------------------
   USUÁRIOS E PERFIS (login próprio + JWT)
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Perfil (
    Id      INT IDENTITY(1,1) NOT NULL,
    Nome    NVARCHAR(30)      NOT NULL, -- Operador | Almoxarifado | Movimentador | PCP | Qualidade | Gestao | Administrador
    CONSTRAINT PK_Perfil PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Perfil_Nome UNIQUE (Nome)
);

-- MVP: um perfil por usuário. Se no futuro precisar de múltiplos perfis por
-- pessoa, trocar por tabela associativa UsuarioPerfil (N:N).
CREATE TABLE dbo.Usuario (
    Id              INT IDENTITY(1,1)  NOT NULL,
    NomeUsuario     NVARCHAR(50)        NOT NULL,
    SenhaHash       NVARCHAR(200)       NOT NULL,
    NomeCompleto    NVARCHAR(200)       NOT NULL,
    PerfilId        INT                 NOT NULL,
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Usuario_Ativo DEFAULT (1),
    -- Lockout de conta (anti brute-force): falhas de senha em sequência e até quando a conta
    -- está trancada. BloqueadoAte NULL = destrancada; a trava expira sozinha, sem ação de admin.
    -- O contador zera no login bem-sucedido e também no momento em que a trava é aplicada.
    FalhasConsecutivas INT              NOT NULL CONSTRAINT DF_Usuario_FalhasConsecutivas DEFAULT (0),
    BloqueadoAte    DATETIME2           NULL,
    CONSTRAINT PK_Usuario PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Usuario_NomeUsuario UNIQUE (NomeUsuario),
    CONSTRAINT FK_Usuario_Perfil FOREIGN KEY (PerfilId) REFERENCES dbo.Perfil (Id)
);
GO

CREATE TABLE dbo.RefreshToken (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    UsuarioId               INT                 NOT NULL,
    TokenHash               NVARCHAR(200)       NOT NULL,   -- SHA-256 do refresh token (nunca em claro)
    ExpiraEm                DATETIME2           NOT NULL,
    CriadoEm                DATETIME2           NOT NULL CONSTRAINT DF_RefreshToken_CriadoEm DEFAULT (SYSUTCDATETIME()),
    RevogadoEm              DATETIME2           NULL,       -- NULL = ativo; preenchido no logout ou na rotação
    SubstituidoPorTokenHash NVARCHAR(200)       NULL,       -- rastro de rotação (auditoria)
    RowVersion              ROWVERSION          NOT NULL,   -- token de concorrência otimista
    CONSTRAINT PK_RefreshToken PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RefreshToken_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_RefreshToken_TokenHash UNIQUE (TokenHash),
    CONSTRAINT CK_RefreshToken_ExpiraAposCriado CHECK (ExpiraEm > CriadoEm)
);
GO
CREATE INDEX IX_RefreshToken_Usuario ON dbo.RefreshToken (UsuarioId);
GO

/* ---------------------------------------------------------------------
   CATÁLOGO (receita padrão / template reutilizável entre pedidos)
   --------------------------------------------------------------------- */

/* Blob do arquivo de Componente. Tabela SEPARADA de dbo.Componente de proposito: o catalogo e
   listado paginado, e VARBINARY(MAX) na mesma linha convidaria a arrastar megabytes numa
   listagem. Serve as DUAS colunas de arquivo de Componente -- hoje so o solido tem consumidor
   (Fase 2B); a foto entra depois com uma FK nova e um validador diferente, sem mudar esta tabela. */
CREATE TABLE dbo.ArquivoDeComponente (
    Id                  INT IDENTITY(1,1)   NOT NULL,
    NomeOriginal        NVARCHAR(260)       NOT NULL, -- o nome que o usuario subiu: exibicao na tela e Content-Disposition do download
    Conteudo            VARBINARY(MAX)      NOT NULL,
    -- Calculada pelo banco (emenda de 2026-09-12, na Fase 2B): enquanto era
    -- coluna comum, o invariante TamanhoEmBytes == DATALENGTH(Conteudo) nao tinha dono nem guarda --
    -- se o caso de uso do upload errasse, o tamanho exibido mentia em silencio. O SQL Server recusa
    -- escrita nela (Msg 271), o que torna o invariante inviolavel em vez de apenas disciplinado.
    -- DATALENGTH sobre VARBINARY(MAX) devolve BIGINT -- o CAST mantem INT (medido na bancada).
    TamanhoEmBytes      AS CAST(DATALENGTH(Conteudo) AS INT) PERSISTED,
    -- Idem, mesmo motivo. HASHBYTES('SHA2_256', ...) sozinho produz VARBINARY(8000) (medido na
    -- bancada, achado alem das cinco medicoes da spec): o CAST para BINARY(32) preserva o tipo fixo
    -- de 32 bytes do SHA-256, que e o que o mapeamento EF (HasColumnType) espera.
    Sha256              AS CAST(HASHBYTES('SHA2_256', Conteudo) AS BINARY(32)) PERSISTED,
    CriadoEm            DATETIME2           NOT NULL CONSTRAINT DF_ArquivoDeComponente_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CriadoPorUsuarioId  INT                 NOT NULL,
    CONSTRAINT PK_ArquivoDeComponente PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ArquivoDeComponente_CriadoPorUsuario FOREIGN KEY (CriadoPorUsuarioId)
        REFERENCES dbo.Usuario(Id),
    -- Arquivo de zero byte nao e arquivo. O limite SUPERIOR (16 MiB) fica na aplicacao, nao aqui:
    -- excecao de CHECK sobe como SqlException e vira 500, e o cliente merece 400 -- mesmo criterio
    -- de Componente.Tipo e Agrupamento.Tipo.
    CONSTRAINT CK_ArquivoDeComponente_Tamanho CHECK (TamanhoEmBytes > 0)
);

CREATE TABLE dbo.Componente (
    Id              INT IDENTITY(1,1)   NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL, -- identificador unico da peca de catalogo NESTE sistema; alfanumerico. O sistema nao modela a numeracao do cliente (nem toda peca chega com codigo, e varia por cliente) -- ver glossario em 01
    Descricao       NVARCHAR(200)       NOT NULL,
    Tipo            NVARCHAR(20)        NOT NULL, -- Bruto | Fabricado | Montagem
    -- Solido 3D (STL) do Componente, em dbo.ArquivoDeComponente. NULLABLE de proposito: a
    -- obrigatoriedade e de negocio e vale para Peca de Pedido, nao para toda linha de catalogo (um
    -- Componente 'Bruto' nao tem solido), e o banco nao consegue distinguir os dois casos aqui --
    -- ver regra 18 em 01. O lugar da cobranca do arquivo PREENCHIDO e
    -- MontagemDeEstruturaUseCase.CriarPeca, e o comentario daquele metodo diz se a guarda ja esta
    -- la -- este comentario aponta o lugar, nao afirma o estado, para nao envelhecer errado.
    --
    -- Por que BLOB em tabela propria, e nao caminho de arquivo (2026-09-12, Fase 2B): esta coluna
    -- ERA NVARCHAR(260) com caminho relativo, e o comentario de entao argumentava contra
    -- VARBINARY(MAX). A reversao e a que aquela nota previa -- ela se fechava com "decisao
    -- reversivel enquanto ninguem gravar dado de verdade", e ninguem gravou: nenhum codigo jamais
    -- escreveu a coluna antiga. O ganho do blob e OPERACIONAL, nao de espaco (o binario ocupa
    -- disco igual nos dois desenhos): backup unico, sem pasta nem permissao de escrita como passo
    -- de deploy, e registro que nao pode divergir do arquivo.
    --
    -- O argumento antigo que a reversao CUSTA, registrado para nao ser redescoberto: o pipeline de
    -- silhuetas da busca por foto (fora das fases, condicionado a spike) queria o arquivo em disco
    -- para alimentar a ferramenta CAD. Com blob, ele tera de materializar um arquivo temporario.
    -- Custo pequeno e localizado, mas real.
    --
    -- A terceira razao daquele comentario -- "a API de upload fica mais simples" -- NAO se
    -- sustentou: a API recebe IFormFile do mesmo jeito nos dois desenhos. O que muda e o destino
    -- de dois metodos de repositorio, nao a forma do endpoint.
    ArquivoSolidoId INT                 NULL,
    ArquivoFoto     NVARCHAR(260)       NULL,     -- foto de referencia, OPCIONAL: ajuda o operador a reconhecer a peca. Nao substitui o solido
    Ativo           BIT                 NOT NULL CONSTRAINT DF_Componente_Ativo DEFAULT (1),
    CONSTRAINT PK_Componente PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Componente_Codigo UNIQUE (Codigo),
    CONSTRAINT CK_Componente_Tipo CHECK (Tipo IN ('Bruto', 'Fabricado', 'Montagem')),
    CONSTRAINT FK_Componente_ArquivoSolido FOREIGN KEY (ArquivoSolidoId)
        REFERENCES dbo.ArquivoDeComponente(Id)
);

-- Receita padrão: de quais componentes-filho um componente-pai é composto
CREATE TABLE dbo.ComponenteFilhoPadrao (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    ComponentePaiId         INT                 NOT NULL,
    ComponenteFilhoId       INT                 NOT NULL,
    QuantidadePadrao        DECIMAL(18,4)       NOT NULL,
    CONSTRAINT PK_ComponenteFilhoPadrao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ComponenteFilhoPadrao_Pai
        FOREIGN KEY (ComponentePaiId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_ComponenteFilhoPadrao_Filho
        FOREIGN KEY (ComponenteFilhoId) REFERENCES dbo.Componente (Id),
    CONSTRAINT CK_ComponenteFilhoPadrao_NaoAutoReferencia
        CHECK (ComponentePaiId <> ComponenteFilhoId),
    CONSTRAINT UQ_ComponenteFilhoPadrao UNIQUE (ComponentePaiId, ComponenteFilhoId)
);

CREATE TABLE dbo.ComponenteMaterialPadrao (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    ComponenteId        INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    QuantidadePadrao    DECIMAL(18,4)       NOT NULL,
    CONSTRAINT PK_ComponenteMaterialPadrao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ComponenteMaterialPadrao_Componente
        FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_ComponenteMaterialPadrao_Material
        FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id),
    CONSTRAINT UQ_ComponenteMaterialPadrao UNIQUE (ComponenteId, MaterialId)
);

CREATE TABLE dbo.ComponenteRoteiroPadrao (
    Id              INT IDENTITY(1,1)  NOT NULL,
    ComponenteId    INT                 NOT NULL,
    SetorId         INT                 NOT NULL,
    Ordem           INT                 NOT NULL,
    CONSTRAINT PK_ComponenteRoteiroPadrao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_ComponenteRoteiroPadrao_Componente
        FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_ComponenteRoteiroPadrao_Setor
        FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT UQ_ComponenteRoteiroPadrao UNIQUE (ComponenteId, Ordem)
);

/* ---------------------------------------------------------------------
   PEDIDO / AGRUPAMENTO
   --------------------------------------------------------------------- */

CREATE TABLE dbo.Pedido (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    Numero              NVARCHAR(30)        NOT NULL,
    Cliente             NVARCHAR(200)       NOT NULL,
    Tipo                NVARCHAR(20)        NOT NULL, -- Fabricacao | Retrabalho
    PedidoOrigemId      INT                 NULL,      -- preenchido só quando Tipo = Retrabalho
    MotivoRetrabalho    NVARCHAR(30)        NULL,      -- preenchido só quando Tipo = Retrabalho
    Status              NVARCHAR(20)        NOT NULL CONSTRAINT DF_Pedido_Status DEFAULT ('Aberto'),
    DataAbertura        DATETIME2           NOT NULL CONSTRAINT DF_Pedido_DataAbertura DEFAULT (SYSUTCDATETIME()),
    DataConclusao       DATETIME2           NULL,
    CriadoPorUsuarioId  INT                 NOT NULL,  -- autoria: responde "quem abriu este pedido"
    CONSTRAINT PK_Pedido PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Pedido_Numero UNIQUE (Numero),
    CONSTRAINT FK_Pedido_PedidoOrigem
        FOREIGN KEY (PedidoOrigemId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT FK_Pedido_CriadoPorUsuario
        FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Pedido_Tipo CHECK (Tipo IN ('Fabricacao', 'Retrabalho')),
    CONSTRAINT CK_Pedido_Status
        CHECK (Status IN ('Aberto', 'EmProducao', 'AguardandoExpedicao', 'Concluido', 'Cancelado')),
    CONSTRAINT CK_Pedido_OrigemObrigatoriaSeRetrabalho
        CHECK (Tipo <> 'Retrabalho' OR PedidoOrigemId IS NOT NULL),
    CONSTRAINT CK_Pedido_MotivoRetrabalho
        CHECK (MotivoRetrabalho IS NULL
            OR MotivoRetrabalho IN ('ReprovacaoDimensional', 'ErroInterno', 'SolicitacaoCliente', 'Perda')),
    CONSTRAINT CK_Pedido_MotivoSoSeRetrabalho
        CHECK (Tipo = 'Retrabalho' OR MotivoRetrabalho IS NULL),
    CONSTRAINT CK_Pedido_ConclusaoAposAbertura
        CHECK (DataConclusao IS NULL OR DataConclusao >= DataAbertura)
);

CREATE TABLE dbo.Agrupamento (
    Id              INT IDENTITY(1,1)  NOT NULL,
    PedidoId        INT                 NOT NULL,
    Codigo          NVARCHAR(50)        NOT NULL,
    Tipo            NVARCHAR(20)        NOT NULL, -- Kit (vai para solda) | Avulso (não passa por solda); descritivo
    DataConclusao   DATETIME2           NULL, -- preenchida quando todas as Peças do agrupamento fecham
    CriadoPorUsuarioId INT                 NOT NULL,
    CriadoEm        DATETIME2           NOT NULL
        CONSTRAINT DF_Agrupamento_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Agrupamento PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Agrupamento_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT FK_Agrupamento_CriadoPorUsuario
        FOREIGN KEY (CriadoPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT UQ_Agrupamento_PedidoCodigo UNIQUE (PedidoId, Codigo),
    CONSTRAINT CK_Agrupamento_Tipo CHECK (Tipo IN ('Kit', 'Avulso'))
);

/* ---------------------------------------------------------------------
   ESTRUTURA REAL (árvore recursiva efetivamente usada no Pedido/Agrupamento;
   pode ter sido copiada do catálogo e depois customizada)
   --------------------------------------------------------------------- */

CREATE TABLE dbo.EstruturaItem (
    Id                          INT IDENTITY(1,1)  NOT NULL,
    AgrupamentoId               INT                 NOT NULL,
    -- Nullable: item 100% ad-hoc, sem base no catalogo. So um Item (no com pai) pode ser ad-hoc --
    -- uma Peca sempre referencia um Componente, senao o solido (que mora em Componente) nao tem
    -- onde ser pendurado. Ver regra 18 em 01, e CK_EstruturaItem_PecaTemComponente abaixo.
    ComponenteId                INT                 NULL,
    -- Nome proprio do no. NULL = herda a descricao do Componente. Existe porque, com ComponenteId
    -- NULL (item ad-hoc), o no nao tinha NENHUM texto proprio: a consulta de "o que esta no meu
    -- setor" devolvia esse item anonimo para o operador. Ver regra 19 em 01.
    Descricao                   NVARCHAR(200)       NULL,
    EstruturaPaiId              INT                 NULL,       -- self-FK: recursão Peça -> Item -> ... -> Item
    NivelHierarquico            NVARCHAR(10)        NOT NULL,   -- Peca | Item (denormalizado p/ consulta rápida)
    Quantidade                  DECIMAL(18,4)       NOT NULL,   -- lote agregado (divisível por quantidades livres)
    QuantidadePorPai            DECIMAL(18,4)       NULL,       -- regra 26: quantos entram em UMA unidade do pai (só Item)
    RequerRelatorioDimensional  BIT                 NOT NULL
        CONSTRAINT DF_EstruturaItem_RequerRelatorio DEFAULT (0), -- vale p/ Peça (topo); cliente exige no cadastro
    CONSTRAINT PK_EstruturaItem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaItem_Agrupamento FOREIGN KEY (AgrupamentoId) REFERENCES dbo.Agrupamento (Id),
    CONSTRAINT FK_EstruturaItem_Componente FOREIGN KEY (ComponenteId) REFERENCES dbo.Componente (Id),
    CONSTRAINT FK_EstruturaItem_Pai FOREIGN KEY (EstruturaPaiId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT CK_EstruturaItem_NaoAutoReferencia CHECK (EstruturaPaiId <> Id),
    CONSTRAINT CK_EstruturaItem_NivelHierarquico CHECK (NivelHierarquico IN ('Peca', 'Item')),
    -- Peça = topo da árvore dentro do Agrupamento (sem pai); Item = tem pai
    CONSTRAINT CK_EstruturaItem_PecaSemPai
        CHECK ((NivelHierarquico = 'Peca' AND EstruturaPaiId IS NULL)
            OR (NivelHierarquico = 'Item' AND EstruturaPaiId IS NOT NULL)),
    CONSTRAINT CK_EstruturaItem_PecaTemComponente
        CHECK (NivelHierarquico = 'Item' OR ComponenteId IS NOT NULL),
    -- Regra 26 (Fase 3): a montagem baixa N x QuantidadePorPai de cada filho. Peça não tem pai.
    CONSTRAINT CK_EstruturaItem_QuantidadePorPai
        CHECK ((NivelHierarquico = 'Peca' AND QuantidadePorPai IS NULL)
            OR (NivelHierarquico = 'Item' AND QuantidadePorPai IS NOT NULL AND QuantidadePorPai > 0))
);

CREATE TABLE dbo.EstruturaMaterial (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    Quantidade          DECIMAL(18,4)       NOT NULL,
    CONSTRAINT PK_EstruturaMaterial PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaMaterial_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_EstruturaMaterial_Material
        FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id),
    CONSTRAINT UQ_EstruturaMaterial UNIQUE (EstruturaItemId, MaterialId)
);

CREATE TABLE dbo.EstruturaRoteiro (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    SetorId             INT                 NOT NULL,
    Ordem               INT                 NOT NULL,
    CONSTRAINT PK_EstruturaRoteiro PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_EstruturaRoteiro_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_EstruturaRoteiro_Setor
        FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT UQ_EstruturaRoteiro UNIQUE (EstruturaItemId, Ordem)
);

/* ---------------------------------------------------------------------
   EXECUÇÃO / RASTREAMENTO
   --------------------------------------------------------------------- */

-- Registro de "montei N" de um nó com filhos (regra 24). O total montado do nó é a soma de
-- Quantidade das montagens não estornadas. A baixa de CADA filho fica em dbo.Movimentacao
-- (Tipo = 'Montagem', MontagemId = esta linha), com N × QuantidadePorPai gravado: editar a
-- razão depois não reescreve o passado.
CREATE TABLE dbo.Montagem (
    Id                     INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId        INT                 NOT NULL, -- o pai montado (nó com filhos)
    SetorId                INT                 NOT NULL, -- onde foi montado
    Quantidade             DECIMAL(18,4)       NOT NULL, -- N unidades do pai
    DataHora               DATETIME2           NOT NULL CONSTRAINT DF_Montagem_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId              INT                 NOT NULL,
    EstornadaEm            DATETIME2           NULL,     -- NULL = vale; preenchida = estornada (spec da Fase 3, seção 4.5)
    EstornadaPorUsuarioId  INT                 NULL,
    CONSTRAINT PK_Montagem PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Montagem_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Montagem_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Montagem_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT FK_Montagem_EstornadaPorUsuario FOREIGN KEY (EstornadaPorUsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Montagem_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Montagem_EstornoCompleto
        CHECK ((EstornadaEm IS NULL AND EstornadaPorUsuarioId IS NULL)
            OR (EstornadaEm IS NOT NULL AND EstornadaPorUsuarioId IS NOT NULL)),
    CONSTRAINT CK_Montagem_EstornoAposMontagem CHECK (EstornadaEm IS NULL OR EstornadaEm >= DataHora)
);

-- Livro de movimentações: cada linha move Quantidade de um nó de uma posição para outra.
-- SÓ INSERÇÃO: não se edita nem se apaga; correção é um Estorno (movimento inverso que aponta o
-- original). Saldo de uma posição = Σ Quantidade onde ela é destino − Σ onde ela é origem;
-- AIniciar = EstruturaItem.Quantidade − Σ onde ela é origem + Σ onde ela é destino (estorno).
-- Conservação (regra 9) por construção: todo movimento tira de uma posição e põe em outra.
CREATE TABLE dbo.Movimentacao (
    Id               INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId  INT                 NOT NULL,
    Tipo             NVARCHAR(20)        NOT NULL, -- Inicio | Termino | Entrega | Montagem | Estorno
    Quantidade       DECIMAL(18,4)       NOT NULL,
    OrigemPosicao    NVARCHAR(20)        NOT NULL,
    OrigemSetorId    INT                 NULL,
    OrigemOrdem      INT                 NULL,     -- passo do Roteiro do próprio nó
    DestinoPosicao   NVARCHAR(20)        NOT NULL,
    DestinoSetorId   INT                 NULL,
    DestinoOrdem     INT                 NULL,
    MontagemId       INT                 NULL,     -- baixa de filho (e o estorno dela)
    EstornoDeId      INT                 NULL,     -- só no Estorno: o movimento que ele desfaz
    DataHora         DATETIME2           NOT NULL CONSTRAINT DF_Movimentacao_DataHora DEFAULT (SYSUTCDATETIME()),
    UsuarioId        INT                 NOT NULL, -- autor
    CONSTRAINT PK_Movimentacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Movimentacao_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Movimentacao_OrigemSetor FOREIGN KEY (OrigemSetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Movimentacao_DestinoSetor FOREIGN KEY (DestinoSetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Movimentacao_Montagem FOREIGN KEY (MontagemId) REFERENCES dbo.Montagem (Id),
    CONSTRAINT FK_Movimentacao_EstornoDe FOREIGN KEY (EstornoDeId) REFERENCES dbo.Movimentacao (Id),
    CONSTRAINT FK_Movimentacao_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.Usuario (Id),
    CONSTRAINT CK_Movimentacao_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Movimentacao_Tipo
        CHECK (Tipo IN ('Inicio', 'Termino', 'Entrega', 'Montagem', 'Estorno')),
    CONSTRAINT CK_Movimentacao_OrigemPosicao
        CHECK (OrigemPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
    CONSTRAINT CK_Movimentacao_DestinoPosicao
        CHECK (DestinoPosicao IN ('AIniciar', 'NoSetor', 'AguardandoColeta', 'AguardandoMontagem', 'NaExpedicao', 'Montado')),
    -- Setor e passo combinam com a posição (tabela da seção 3.1 da spec da Fase 3)
    CONSTRAINT CK_Movimentacao_OrigemCoerente
        CHECK ((OrigemPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND OrigemSetorId IS NULL AND OrigemOrdem IS NULL)
            OR (OrigemPosicao IN ('NoSetor', 'AguardandoColeta') AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NOT NULL)
            OR (OrigemPosicao = 'AguardandoMontagem' AND OrigemSetorId IS NOT NULL AND OrigemOrdem IS NULL)),
    CONSTRAINT CK_Movimentacao_DestinoCoerente
        CHECK ((DestinoPosicao IN ('AIniciar', 'NaExpedicao', 'Montado') AND DestinoSetorId IS NULL AND DestinoOrdem IS NULL)
            OR (DestinoPosicao IN ('NoSetor', 'AguardandoColeta') AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NOT NULL)
            OR (DestinoPosicao = 'AguardandoMontagem' AND DestinoSetorId IS NOT NULL AND DestinoOrdem IS NULL)),
    -- Cada tipo só faz as transições dele; o Estorno é o inverso de um dos outros
    CONSTRAINT CK_Movimentacao_Transicao
        CHECK ((Tipo = 'Inicio'   AND OrigemPosicao = 'AIniciar' AND DestinoPosicao = 'NoSetor')
            OR (Tipo = 'Termino'  AND OrigemPosicao = 'NoSetor' AND DestinoPosicao = 'AguardandoColeta'
                                  AND OrigemSetorId = DestinoSetorId AND OrigemOrdem = DestinoOrdem)
            OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoColeta'
                                  AND DestinoPosicao IN ('NoSetor', 'AguardandoMontagem', 'NaExpedicao'))
            OR (Tipo = 'Entrega'  AND OrigemPosicao = 'AguardandoMontagem'   -- redirecionamento
                                  AND DestinoPosicao = 'AguardandoMontagem')
            OR (Tipo = 'Montagem' AND OrigemPosicao = 'AguardandoMontagem' AND DestinoPosicao = 'Montado')
            OR (Tipo = 'Estorno')),
    CONSTRAINT CK_Movimentacao_MontagemSoNaBaixa
        CHECK ((Tipo = 'Montagem' AND MontagemId IS NOT NULL)
            OR (Tipo = 'Estorno')
            OR (Tipo NOT IN ('Montagem', 'Estorno') AND MontagemId IS NULL)),
    CONSTRAINT CK_Movimentacao_EstornoApontaOriginal
        CHECK ((Tipo = 'Estorno' AND EstornoDeId IS NOT NULL)
            OR (Tipo <> 'Estorno' AND EstornoDeId IS NULL))
);
GO

CREATE TABLE dbo.MaterialSeparacao (
    Id                  INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId     INT                 NOT NULL,
    MaterialId          INT                 NOT NULL,
    Quantidade          DECIMAL(18,4)       NOT NULL,
    DataSeparacao       DATETIME2           NOT NULL CONSTRAINT DF_MaterialSeparacao_Data DEFAULT (SYSUTCDATETIME()),
    Responsavel         NVARCHAR(100)       NOT NULL,
    CONSTRAINT PK_MaterialSeparacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_MaterialSeparacao_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_MaterialSeparacao_Material
        FOREIGN KEY (MaterialId) REFERENCES dbo.Material (Id)
);

-- Remessa de expedição. Expedição parcial = várias linhas cuja soma <= Quantidade da Peça
-- (a validação "soma <= total" é feita na camada de aplicação, não como constraint).
CREATE TABLE dbo.Expedicao (
    Id              INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId INT                 NOT NULL, -- Peça expedida (NivelHierarquico = 'Peca')
    Quantidade      DECIMAL(18,4)       NOT NULL,
    DataExpedicao   DATETIME2           NOT NULL CONSTRAINT DF_Expedicao_Data DEFAULT (SYSUTCDATETIME()),
    Responsavel     NVARCHAR(100)       NOT NULL,
    CONSTRAINT PK_Expedicao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Expedicao_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT CK_Expedicao_QuantidadePositiva CHECK (Quantidade > 0)
);
GO

/* ---------------------------------------------------------------------
   DIMENSIONAL / QUALIDADE
   --------------------------------------------------------------------- */

-- Header: no máx. um Relatório Dimensional por Peça (opcional; só quando o cliente exige,
-- flag EstruturaItem.RequerRelatorioDimensional). Acumulativo via RelatorioDimensionalAvaliacao.
CREATE TABLE dbo.RelatorioDimensional (
    Id                      INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId         INT                 NOT NULL, -- peça avaliada (NivelHierarquico = 'Peca')
    CriadoEm                DATETIME2           NOT NULL CONSTRAINT DF_RelatorioDimensional_CriadoEm DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_RelatorioDimensional PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RelatorioDimensional_EstruturaItem
        FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT UQ_RelatorioDimensional_EstruturaItem UNIQUE (EstruturaItemId)
);
GO

-- Detalhe: uma avaliação por remessa avaliada pelo cliente (chega conforme a expedição parcial).
-- Aprovação/reprovação é por quantidade; retrabalho (opcional) fica vinculado à avaliação.
CREATE TABLE dbo.RelatorioDimensionalAvaliacao (
    Id                     INT IDENTITY(1,1) NOT NULL,
    RelatorioDimensionalId INT               NOT NULL,
    QuantidadeAvaliada     DECIMAL(18,4)      NOT NULL,
    QuantidadeAprovada     DECIMAL(18,4)      NOT NULL,
    QuantidadeReprovada    DECIMAL(18,4)      NOT NULL,
    Medidas                NVARCHAR(MAX)      NULL,
    InformadoPor           NVARCHAR(200)      NOT NULL, -- contato do cliente que informou
    DataAvaliacao          DATETIME2          NOT NULL CONSTRAINT DF_RDA_Data DEFAULT (SYSUTCDATETIME()),
    PedidoRetrabalhoId     INT               NULL,      -- retrabalho aberto por causa desta avaliação (opcional)
    CONSTRAINT PK_RelatorioDimensionalAvaliacao PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_RDA_Relatorio
        FOREIGN KEY (RelatorioDimensionalId) REFERENCES dbo.RelatorioDimensional (Id),
    CONSTRAINT FK_RDA_PedidoRetrabalho
        FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT CK_RDA_Quantidades
        CHECK (QuantidadeAprovada + QuantidadeReprovada = QuantidadeAvaliada
           AND QuantidadeAvaliada > 0
           AND QuantidadeAprovada >= 0 AND QuantidadeReprovada >= 0)
);
GO

-- Baixa de quantidade perdida em produção (some no armazém ou morre após processo).
-- Bucket terminal. Regra 9, para todo nó (Peça ou Item): a iniciar + nos Setores + aguardando
-- coleta ou montagem + no local de expedição + montado dentro do pai + expedido + perdido = total do nó.
-- Reposição = Pedido de Retrabalho separado (MotivoRetrabalho='Perda'), manual/opcional.
CREATE TABLE dbo.Perda (
    Id                 INT IDENTITY(1,1)  NOT NULL,
    EstruturaItemId    INT                 NOT NULL, -- Peça que sofreu a perda
    Quantidade         DECIMAL(18,4)       NOT NULL,
    MotivoPerda        NVARCHAR(20)        NOT NULL, -- PerdaArmazem | MortaEmProcesso
    Observacao         NVARCHAR(MAX)       NULL,     -- detalhe livre opcional
    SetorId            INT                 NULL,     -- onde estava quando se perdeu (opcional)
    DataPerda          DATETIME2           NOT NULL CONSTRAINT DF_Perda_Data DEFAULT (SYSUTCDATETIME()),
    Responsavel        NVARCHAR(100)       NOT NULL,
    PedidoRetrabalhoId INT                 NULL,     -- retrabalho aberto p/ repor (opcional)
    CONSTRAINT PK_Perda PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Perda_EstruturaItem FOREIGN KEY (EstruturaItemId) REFERENCES dbo.EstruturaItem (Id),
    CONSTRAINT FK_Perda_Setor FOREIGN KEY (SetorId) REFERENCES dbo.Setor (Id),
    CONSTRAINT FK_Perda_PedidoRetrabalho FOREIGN KEY (PedidoRetrabalhoId) REFERENCES dbo.Pedido (Id),
    CONSTRAINT CK_Perda_QuantidadePositiva CHECK (Quantidade > 0),
    CONSTRAINT CK_Perda_Motivo CHECK (MotivoPerda IN ('PerdaArmazem', 'MortaEmProcesso'))
);
GO

/* ---------------------------------------------------------------------
   ÍNDICES DE APOIO (consultas de rastreamento e KPI mais comuns)
   --------------------------------------------------------------------- */

CREATE INDEX IX_EstruturaItem_Agrupamento ON dbo.EstruturaItem (AgrupamentoId);
CREATE INDEX IX_EstruturaItem_Pai ON dbo.EstruturaItem (EstruturaPaiId);
CREATE INDEX IX_Movimentacao_EstruturaItem ON dbo.Movimentacao (EstruturaItemId);
CREATE INDEX IX_Movimentacao_DestinoSetor ON dbo.Movimentacao (DestinoSetorId) WHERE DestinoSetorId IS NOT NULL;
CREATE INDEX IX_Movimentacao_OrigemSetor ON dbo.Movimentacao (OrigemSetorId) WHERE OrigemSetorId IS NOT NULL;
-- Um movimento se estorna uma vez só: JaEstornado garantido pelo banco, não só pela aplicação.
CREATE UNIQUE INDEX UX_Movimentacao_EstornoDe ON dbo.Movimentacao (EstornoDeId) WHERE EstornoDeId IS NOT NULL;
CREATE INDEX IX_Montagem_EstruturaItem ON dbo.Montagem (EstruturaItemId);
CREATE INDEX IX_Pedido_PedidoOrigem ON dbo.Pedido (PedidoOrigemId);
CREATE INDEX IX_Expedicao_EstruturaItem ON dbo.Expedicao (EstruturaItemId);
CREATE INDEX IX_RDA_Relatorio ON dbo.RelatorioDimensionalAvaliacao (RelatorioDimensionalId);
CREATE INDEX IX_Perda_EstruturaItem ON dbo.Perda (EstruturaItemId);
GO

/* =====================================================================
   EXEMPLOS DE CONSULTA (KPIs)
   ===================================================================== */

-- As duas consultas de exemplo que viviam aqui liam dbo.EstruturaSetorHistorico, que saiu na
-- Fase 3 (spec 2026-09-24-fase-3-rastreamento-de-setor-design.md, seção 2.7). Reescrever na Fase 6
-- sobre dbo.Movimentacao, pareando entradas e saídas de cada Setor por ordem de chegada (FIFO):
--   * tempo de liberação por setor: da chegada (destino NoSetor) à saída (origem AguardandoColeta);
--   * tempo total, em fila e em produção por pedido: o início real é o MIN(DataHora) dos
--     movimentos Inicio dos nós do Pedido (regra 14).
