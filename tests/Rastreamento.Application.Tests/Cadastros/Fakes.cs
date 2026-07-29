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
