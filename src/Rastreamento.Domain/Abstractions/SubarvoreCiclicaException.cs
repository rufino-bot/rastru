namespace Rastreamento.Domain.Abstractions;

/// <summary>
/// `EstruturaPaiId` tem um ciclo entre dois ou mais nos (ex.: A aponta pra B, B aponta pra A).
/// `CK_EstruturaItem_NaoAutoReferencia` (`specs/02-modelo-de-dados.sql`) so impede um no apontar
/// pra SI MESMO — nao impede um ciclo mais longo entre nos distintos, e o schema nao tem como
/// impedir isso via CHECK (precisaria de uma consulta recursiva). Lancada por
/// `IEstruturaRepository.RemoverSubarvoreAsync` (Minor 6 da review da Task 4) quando a travessia
/// nivel-a-nivel reencontra um Id ja visitado: sem esta guarda, a travessia entraria num laco sem
/// fim, com uma transacao aberta segurando locks — travar e pior que falhar, e o mesmo criterio ja
/// usado por `ArvoreInconsistenteException` (Minor 3 da review da Task 3) para dado corrompido
/// achado numa LEITURA.
/// </summary>
public sealed class SubarvoreCiclicaException(string mensagem) : Exception(mensagem);
