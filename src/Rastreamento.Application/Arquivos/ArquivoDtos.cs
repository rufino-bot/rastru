namespace Rastreamento.Application.Arquivos;

/// <summary>
/// O solido pronto para o controller responder. NAO carrega Id nem hash: o cliente identifica o
/// recurso pelo id do COMPONENTE (e por ele que a rota pergunta), e expor um id de arquivo abriria
/// um segundo caminho para o mesmo recurso.
/// </summary>
public sealed record ArquivoDeSolidoDto(string NomeOriginal, byte[] Conteudo);
