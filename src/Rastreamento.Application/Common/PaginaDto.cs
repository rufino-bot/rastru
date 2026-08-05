namespace Rastreamento.Application.Common;

/// <summary>
/// Uma pagina de resultado. Generico desde o comeco, mesmo com um consumidor so (`Componente`):
/// o que nao pode acontecer e a paginacao nascer com formato ad-hoc e o sistema acumular tres
/// jeitos incompativeis de paginar ate a Fase 6. `Setor` e `Material` NAO migram nesta fase —
/// eles nao tem o problema de volume que motivou isto; migrar depois e preencher, nao redesenhar.
/// <para>
/// `Total` e a contagem sob o MESMO filtro da pagina, e nao o tamanho de `Itens`: e dele que sai
/// o numero de paginas na tela.
/// </para>
/// </summary>
public sealed record PaginaDto<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);
