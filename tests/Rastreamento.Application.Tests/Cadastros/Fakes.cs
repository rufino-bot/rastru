using Rastreamento.Domain.Abstractions;
using Rastreamento.Domain.Entities;

namespace Rastreamento.Application.Tests.Cadastros;

public class FakeSetorRepo : ISetorRepository
{
    private readonly List<Setor> _linhas;
    private int _proximoId;

    public FakeSetorRepo(params Setor[] existentes)
    {
        _linhas = existentes.ToList();
        _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(s => s.Id) + 1;
    }

    /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
    public int Saves { get; private set; }

    public Task<Setor?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(s => s.Id == id));

    /// <summary>
    /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
    /// producao (`SetorRepository` real, `WHERE Nome = @p`). Duplicado-por-caixa (ex.: "solda" vs
    /// "Solda") nao e coberto neste nivel — precisa de um teste ponta a ponta contra o banco real.
    /// </summary>
    public Task<Setor?> ObterPorNomeAsync(string nome, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(s => s.Nome == nome));

    public Task<IReadOnlyList<Setor>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Setor>>(
            _linhas.Where(s => incluirInativos || s.Ativo).OrderBy(s => s.Nome).ToList());

    public Task AdicionarAsync(Setor setor, CancellationToken ct)
    {
        setor.Id = _proximoId++;
        _linhas.Add(setor);
        return Task.CompletedTask;
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

public class FakeMaterialRepo : IMaterialRepository
{
    private readonly List<Material> _linhas;
    private int _proximoId;

    public FakeMaterialRepo(params Material[] existentes)
    {
        _linhas = existentes.ToList();
        _proximoId = _linhas.Count == 0 ? 1 : _linhas.Max(m => m.Id) + 1;
    }

    /// <summary>Quantos commits o repositorio recebeu — prova que o caminho de erro nao escreve.</summary>
    public int Saves { get; private set; }

    public Task<Material?> ObterPorIdAsync(int id, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(m => m.Id == id));

    /// <summary>
    /// Comparacao case-sensitive (`==`), diferente da collation case-insensitive do SQL Server em
    /// producao (`MaterialRepository` real, `WHERE Codigo = @p`) e de `UQ_Material_Codigo`.
    /// Duplicado-por-caixa (ex.: "ch-001" vs "CH-001") nao e coberto neste nivel — precisa de um
    /// teste ponta a ponta contra o banco real. NAO torne o fake case-insensitive: isso simularia
    /// o banco e esconderia a lacuna.
    /// </summary>
    public Task<Material?> ObterPorCodigoAsync(string codigo, CancellationToken ct) =>
        Task.FromResult(_linhas.SingleOrDefault(m => m.Codigo == codigo));

    public Task<IReadOnlyList<Material>> ListarAsync(bool incluirInativos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Material>>(
            _linhas.Where(m => incluirInativos || m.Ativo).OrderBy(m => m.Codigo).ToList());

    public Task AdicionarAsync(Material material, CancellationToken ct)
    {
        material.Id = _proximoId++;
        _linhas.Add(material);
        return Task.CompletedTask;
    }

    public Task SalvarAlteracoesAsync(CancellationToken ct)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
