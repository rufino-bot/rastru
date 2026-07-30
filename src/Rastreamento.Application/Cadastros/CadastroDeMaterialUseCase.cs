using Rastreamento.Application.Common;
using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Cadastros;

/// <summary>
/// Cadastro de Material: criar, editar, listar e (in)ativar. Material nao se exclui — linhas de
/// EstruturaMaterial e ComponenteMaterialPadrao apontam para ele (ver a spec da Fase 1,
/// "Politica de exclusao").
/// </summary>
public sealed class CadastroDeMaterialUseCase
{
    private const string ErroDeCampoObrigatorio =
        "Codigo, descricao e unidade de medida sao obrigatorios.";

    private const string ErroDeCodigoDuplicado = "Ja existe um Material com este codigo.";

    private readonly IMaterialRepository _repositorio;

    public CadastroDeMaterialUseCase(IMaterialRepository repositorio) => _repositorio = repositorio;

    public async Task<Result<MaterialDto>> Cadastrar(NovoMaterialDto novo, CancellationToken ct)
    {
        var (codigo, descricao, unidade) = Normalizar(novo);
        if (codigo.Length == 0 || descricao.Length == 0 || unidade.Length == 0)
            return Result<MaterialDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

        // Checagem ANTES do insert: erro de negocio claro em vez de excecao de UQ_Material_Codigo
        // vazando ate a API. O indice segue como rede de seguranca para a corrida entre as duas.
        if (await _repositorio.ObterPorCodigoAsync(codigo, ct) is not null)
            return Result<MaterialDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

        var material = new Material
        {
            Codigo = codigo, Descricao = descricao, UnidadeMedida = unidade, Ativo = true,
        };
        await _repositorio.AdicionarAsync(material, ct);
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<MaterialDto>.Ok(Projetar(material));
    }

    public async Task<Result<MaterialDto>> Editar(
        int id, NovoMaterialDto alterado, CancellationToken ct)
    {
        var (codigo, descricao, unidade) = Normalizar(alterado);
        if (codigo.Length == 0 || descricao.Length == 0 || unidade.Length == 0)
            return Result<MaterialDto>.Falha(ErroDeCampoObrigatorio, TipoDeErro.Validacao);

        var material = await _repositorio.ObterPorIdAsync(id, ct);
        if (material is null)
            return Result<MaterialDto>.Falha("Material nao encontrado.", TipoDeErro.NaoEncontrado);

        // So e conflito se o codigo pertencer a OUTRA linha: manter o proprio codigo e no-op.
        var homonimo = await _repositorio.ObterPorCodigoAsync(codigo, ct);
        if (homonimo is not null && homonimo.Id != id)
            return Result<MaterialDto>.Falha(ErroDeCodigoDuplicado, TipoDeErro.Conflito);

        material.Codigo = codigo;
        material.Descricao = descricao;
        material.UnidadeMedida = unidade;
        await _repositorio.SalvarAlteracoesAsync(ct);

        return Result<MaterialDto>.Ok(Projetar(material));
    }

    public async Task<IReadOnlyList<MaterialDto>> Listar(bool incluirInativos, CancellationToken ct)
    {
        var materiais = await _repositorio.ListarAsync(incluirInativos, ct);
        return materiais.Select(Projetar).ToList();
    }

    /// <summary>Cobre inativar e reativar — o mesmo endpoint `PATCH /materiais/{id}/ativo`.</summary>
    public async Task<Result> DefinirAtivo(int id, bool ativo, CancellationToken ct)
    {
        var material = await _repositorio.ObterPorIdAsync(id, ct);
        if (material is null)
            return Result.Falha("Material nao encontrado.", TipoDeErro.NaoEncontrado);

        material.Ativo = ativo;
        await _repositorio.SalvarAlteracoesAsync(ct);
        return Result.Ok();
    }

    /// <summary>
    /// Detalhe do 409 para o controller montar o corpo. Chamado so no caminho de erro — o custo da
    /// segunda leitura nao entra no caminho feliz. O campo e `codigo` porque a unicidade e do
    /// Codigo (`UQ_Material_Codigo`); a Descricao pode repetir a vontade.
    /// </summary>
    public async Task<ValorDuplicadoDto?> LocalizarDuplicado(string codigo, CancellationToken ct)
    {
        var existente = await _repositorio.ObterPorCodigoAsync(Normalizar(codigo), ct);
        return existente is null
            ? null
            : new ValorDuplicadoDto("codigo", !existente.Ativo, existente.Id);
    }

    private static (string Codigo, string Descricao, string Unidade) Normalizar(NovoMaterialDto d) =>
        (Normalizar(d.Codigo), Normalizar(d.Descricao), Normalizar(d.UnidadeMedida));

    /// <summary>
    /// Toda entrada de texto passa por aqui antes de virar consulta ou linha: o `Trim` faz
    /// " CH-001 " colidir com "CH-001" como o indice UNIQUE ja faria, e o `?? string.Empty` cobre o
    /// null que o desserializador de JSON entrega mesmo em propriedade nao-anulavel — a anotacao de
    /// nulabilidade nao e garantia em tempo de execucao.
    /// </summary>
    private static string Normalizar(string? valor) => valor?.Trim() ?? string.Empty;

    private static MaterialDto Projetar(Material m) =>
        new(m.Id, m.Codigo, m.Descricao, m.UnidadeMedida, m.Ativo);
}
