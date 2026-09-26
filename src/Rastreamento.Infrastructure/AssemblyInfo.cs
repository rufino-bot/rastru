using System.Runtime.CompilerServices;

// Fix pass da Task 11 (re-review): so serve para `ExecucaoRepository` ter um construtor `internal`
// que injeta o numero de tentativas do retry de deadlock (ver o XML doc dele) — nunca visivel ao DI,
// que so enumera construtores PUBLICOS. Escopado ao projeto de teste que precisa disso, nao ao
// assembly inteiro de testes de Infrastructure por acidente: se outro projeto de teste vier a
// precisar de algo `internal` daqui, ele entra nesta lista, um `InternalsVisibleTo` por vez.
[assembly: InternalsVisibleTo("Rastreamento.Infrastructure.Tests")]
