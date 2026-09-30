# FCG.Users.Domain

Camada dos conceitos e invariantes do UsersAPI. Não referencia outros projetos nem pacotes NuGet.

Na E04, recebeu Usuario, Perfil, PerfisSistema, Token e Permissao em `Entities/`. As regras da origem foram preservadas, com namespaces adaptados. Token representa o registro do refresh token, com hash, expiração e revogação.

Permissao permanece como classe enquanto o grupo define sua presença no modelo final; não há mapeamento ou banco nesta entrega. Autorizacao/posse de jogos não integra Users. LogUsuario aguarda o alinhamento de auditoria da E05.

Nascimento ainda usa DateTimeOffset e os limites de campos ainda são os da origem. Sua compatibilização com o PDF será feita antes da parte afetada da persistência, conforme as decisões de modelagem.

Leia o [capítulo da E04](../../docs/aprendizado/E04-DOMINIO-E-CASOS-DE-USO.md) e o [mapa de extração](../../docs/planejamento/MAPA-EXTRACAO-IDENTITY-E04.md).
