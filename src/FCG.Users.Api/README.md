# FCG.Users.Api

Host ASP.NET Core do UsersAPI. **E09 concluída em 06/10/2026.** Rotas descritas no [README principal](../../README.md) e nos [exemplos HTTP](FCG.Users.Api.http).

- Saúde, cadastro público, login e refresh são públicos.
- Logout exige Bearer e usa sub como dono das sessões; não aceita usuário escolhido no corpo.
- GET/PUT de usuário exigem titular ou Administrador, usando policy por GUID antes da consulta.
- Cadastro de administradores, troca de perfil e inativação exigem role Administrador.
- Cadastro público impõe Usuario; administrativo impõe Administrador. Campos extras não elevam privilégios.

JWT é validado antes da autorização, com MapInboundClaims=false, sub/role preservados e kid confiável. Middleware Authentication precede Authorization. Falhas retornam Problem Details: 401/403/404/400/409 ou 500 genérico conforme o caso.

O DTO de usuário tem sete campos, sem CPF/nascimento/hash/senha/tokens. Controllers usam no-store/no-cache. Swagger fica em Development; seu filtro declara Bearer somente nas operações protegidas. A documentação visual não substitui policies/middleware.

Program também possui o modo explícito `--inicializar-admin-local`, restrito a Development/banco loopback. Esse ramo registra apenas cadastro/persistência/hash/inicializador, não inicia Kestrel nem exige RSA. A inicialização HTTP normal exige chaves válidas. Sem migrations automáticas ou geração silenciosa de RSA.

Access anterior conserva claims até expiração +30s, inclusive após perfil/inativação/logout. As operações não consultam o banco para revogação imediata.

[Guia E09](../../docs/aprendizado/E09-OPERACOES-PROTEGIDAS-E-AUDITORIA.md), [evidências](../../docs/evidencias/E09/README.md) e [contrato para Catalog](../../docs/contratos/CONTRATO-JWT-PARA-CATALOG.md).
