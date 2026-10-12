# FCG.Users.Api

Host ASP.NET Core do UsersAPI. Na E08, expõe saúde, cadastro, login, refresh e logout, com Problem Details e Swagger em Development.

| Rota | Comportamento |
|---|---|
| GET /health | Confere processo, sem banco/broker. |
| POST /api/v1/usuarios | Cadastro comum, hash e auditoria atômica; 201/400/409. |
| POST /api/v1/auth/login | JWT RS256 e refresh por hash; 200/400/401. |
| POST /api/v1/auth/refresh | Público; confere sessão e devolve novo par/dados atuais; 200/400/401. |
| POST /api/v1/auth/logout | Bearer válido; sub identifica dono das sessões a revogar; 204/401. |

Falhas inesperadas retornam 500 genérico. Login/refresh mantêm usuario com nome/e-mail/perfil fora das claims. As ações usam no-store/no-cache. Refresh inválido, vencido, revogado ou usuário inativo recebe o mesmo 401.

Program registra serviços/casos de uso reais, exige chaves válidas no startup e aplica Authentication antes de Authorization. O logout não aceita escolha de usuário no corpo: usa JWT validado. SegurancaSwaggerOperationFilter declara Bearer apenas nas operações protegidas; o requisito visual não substitui o middleware.

Sem migration automática ou geração silenciosa de RSA. Consultas/administração e demais auditorias entram na E09. Logout não invalida imediatamente access emitido. Controllers protegidos de demonstração existem só nos testes.

Veja [guia E08](../../docs/aprendizado/E08-REFRESH-LOGOUT-E-CONCORRENCIA.md), [evidências](../../docs/evidencias/E08/README.md) e [README principal](../../README.md).
