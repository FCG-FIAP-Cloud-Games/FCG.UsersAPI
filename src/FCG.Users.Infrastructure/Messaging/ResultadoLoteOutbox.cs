namespace FCG.Users.Infrastructure.Messaging;

public sealed record ResultadoLoteOutbox(int Tentativas, int Publicadas, int Falhas, int MaiorTentativaComFalha);
