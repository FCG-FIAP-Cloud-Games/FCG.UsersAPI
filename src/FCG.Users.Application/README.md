# FCG.Users.Application

Casos de uso de usuários/autenticação. Depende somente de Domain; declara interfaces sem ASP.NET Core, EF ou criptografia concreta.

- Usuarios: comandos/resultados e manipuladores de cadastro, consulta, atualização e troca de perfil.
- Auth: login, refresh e logout.
- Abstractions/Repositories: operações implementadas pela Infrastructure desde a E05.
- Abstractions/Security: contratos implementados concretamente nas E06/E07.
- NormalizadorIdentidade: normalização usada na autenticação.

Na E08, os três fluxos de Auth estão conectados ao HTTP e PostgreSQL. Refresh rejeita sessão inválida/usuário inativo, consulta perfil atual, prepara credenciais e retorna o par só após rotação confirmada. Logout recebe identificador autenticado da borda HTTP e coordena revogação. API valida JWT/extrai sub; Infrastructure controla transações/concorrência.

E09 conectará consultas/administração e auditorias restantes. Substitutos ficam só nos testes; nenhuma persistência em memória foi registrada na API.

Veja [guia E08](../../docs/aprendizado/E08-REFRESH-LOGOUT-E-CONCORRENCIA.md) e [mapa da extração E04](../../docs/planejamento/MAPA-EXTRACAO-IDENTITY-E04.md).
