# FCG.Users.Domain

Entidades e invariantes do UsersAPI, sem dependências de EF Core ou outros projetos.

Na E05, o modelo contém Usuario, Perfil, PerfisSistema, Token e LogUsuario. Nascimento usa DateOnly; CPF interno exige 11 dígitos ASCII sem formatação. Permissao foi retirada por decisão aprovada; o controle de acesso permanece baseado em perfis Usuario/Administrador. Autorizacao/posse de jogos pertence a Catalog.

Token representa o registro do refresh token (hash, expiração e revogação). LogUsuario prepara cadastro, alteração de dados, troca de perfil e inativação com descrições fixas e data UTC. Criar a entidade não grava automaticamente o histórico: essa conexão será feita nos respectivos fluxos.

Consulte o [guia E05](../../docs/aprendizado/E05-PERSISTENCIA-PROPRIA.md).
