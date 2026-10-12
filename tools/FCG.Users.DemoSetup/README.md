# Preparação da demonstração local

Esta ferramenta é executada pelo serviço de preparação do Docker Compose. Ela não é um servidor HTTP e não inicia o publicador RabbitMQ.

O único comando aceito é `--preparar-demonstracao`. Antes de conectar ou escrever arquivos, exige Linux, `DOTNET_ENVIRONMENT=Development`, `DOTNET_RUNNING_IN_CONTAINER=true`, `DemonstracaoLocal__Habilitada=true` e conexão com `Host=postgres;Database=fcg_users_demo`. Ela também verifica a configuração JWT fixa da demonstração.

Depois de abrir o banco, mantém um lock de sessão PostgreSQL durante toda a preparação, para impedir que duas execuções criem as chaves ou apliquem migrations simultaneamente. A conexão da ferramenta usa `Pooling=false`, para que seu fechamento libere fisicamente a sessão mesmo se houver cancelamento ou falha ao desfazer o lock:

1. Gera o primeiro par RSA de 2048 bits em `/data/jwt`, ou valida e reutiliza o par existente. A chave privada é PKCS#8, a pública é SPKI e o identificador é `users-demo-v1`. O diretório usa permissão 0700; a privada, 0600; a pública, 0644. A API e o preparador precisam executar com o mesmo usuário do contêiner. Par incompleto, inválido ou link simbólico faz a preparação falhar sem substituir arquivos.
2. Aplica as migrations existentes por meio do `UsersDbContext`.
3. Reutiliza o `InicializadorAdministradorLocal`, as regras de cadastro e o hash de senha existentes. Cria somente o primeiro administrador, com auditoria e `UserCreatedEvent` pendente na Outbox. Se já existe administrador, preserva usuário, senha e eventos.

A conta sintética é `admin@fcg.local`, com senha `Admin_Demo_2026!`, nome Administrador Demonstração, CPF de exemplo `52998224725` e nascimento `1990-01-01`. Essas credenciais são exclusivamente para o ambiente local de demonstração, publicado em loopback pelo Compose.

A proteção de loopback do comando administrativo da API permanece intacta. Esta ferramenta separada reconhece somente o ambiente explícito de demonstração em contêiner; ela não oferece parâmetros para inicializar outra base ou alterar administradores existentes.

A saída não imprime conexão, senha, conteúdo de chaves, mensagens internas de exceção ou stack traces. Sucesso retorna código 0; configuração inválida, conflito e cancelamento retornam código 1. O Compose permite iniciar a API somente depois de uma preparação bem-sucedida.
