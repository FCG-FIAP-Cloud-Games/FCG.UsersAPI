# Preparação interna da demonstração local

A imagem padrão do UsersAPI contém a API e esta ferramenta em `/app/preparacao`. O `infra/runtime/docker-entrypoint.sh` executa a preparação dentro do próprio container antes de iniciar HTTP quando `DemonstracaoLocal__Habilitada=true`. A execução normal da demonstração não precisa de outra imagem nem de um serviço separado de preparação.

A flag usa `false` por padrão. Ausente ou `false`, o entrypoint inicia diretamente a API, que continua exigindo banco preparado, configuração e chaves válidas. Somente os valores exatos `true` e `false` são aceitos; vazio explícito ou outro valor faz o container terminar com erro sem iniciar HTTP.

O único comando aceito pela ferramenta é `--preparar-demonstracao`. Antes de conectar ou escrever arquivos, exige Linux, `DOTNET_ENVIRONMENT=Development`, `DOTNET_RUNNING_IN_CONTAINER=true`, `DemonstracaoLocal__Habilitada=true` e conexão com `Host=postgres;Database=fcg_users_demo`. Se `ASPNETCORE_ENVIRONMENT` estiver presente, também deve ser `Development`; configurações de ambiente conflitantes são recusadas. Ela verifica ainda a configuração JWT fixa da demonstração. Em Production, a preparação automática é recusada mesmo com a flag habilitada.

Depois de abrir o banco, mantém um lock de sessão PostgreSQL durante toda a preparação, para impedir que duas execuções criem as chaves ou apliquem migrations simultaneamente. A conexão usa `Pooling=false`, para que seu fechamento libere fisicamente a sessão mesmo se houver cancelamento ou falha ao desfazer o lock:

1. Gera o primeiro par RSA de 2048 bits em `/data/jwt`, ou valida e reutiliza o par existente. A chave privada é PKCS#8, a pública é SPKI e o identificador é `users-demo-v1`. O diretório usa permissão 0700; a privada, 0600; a pública, 0644. Par incompleto, inválido ou link simbólico faz a preparação falhar sem substituir arquivos.
2. Aplica as migrations existentes por meio do `UsersDbContext`.
3. Reutiliza o `InicializadorAdministradorLocal`, as regras de cadastro e o hash de senha existentes. Cria somente o primeiro administrador, com auditoria e `UserCreatedEvent` pendente na Outbox. Se já existe administrador, preserva usuário, senha e eventos.
4. Depois da saída 0 da ferramenta, o entrypoint usa `exec` para iniciar a API como PID 1. Só então o host HTTP e o publicador RabbitMQ podem iniciar.

O volume `/data/jwt` precisa permitir escrita pelo usuário `app` (UID 1654), pois o mesmo container agora gera/reutiliza as chaves e executa HTTP. Esse modo da demonstração substitui a montagem somente leitura que a API usava com o job externo. O volume persistente conserva o par RSA entre reinícios, sem embutir chaves privadas na imagem. No modo sem preparação automática, chaves fornecidas externamente ainda podem ser montadas somente para leitura.

O PostgreSQL deve existir, conter o banco de demonstração e estar saudável antes do início do UsersAPI. A ferramenta aplica as tabelas/migrations nesse banco; ela não provisiona o servidor nem cria outro banco. O healthcheck HTTP da imagem concede 90 segundos de período inicial para o preparo; antes do HTTP, a ausência de resposta não indica que a ferramenta já terminou.

A conta sintética é `admin@fcg.local`, com senha `Admin_Demo_2026!`, nome Administrador Demonstração, CPF de exemplo `52998224725` e nascimento `1990-01-01`. Essas credenciais são exclusivamente para o ambiente local de demonstração, publicado em loopback pelo Compose. Um administrador existente conserva sua senha, mesmo que ela seja diferente da senha sintética inicial.

A proteção de loopback do comando administrativo da API permanece intacta. Esta ferramenta reconhece somente o ambiente explícito de demonstração em container; ela não oferece parâmetros para inicializar outra base ou alterar administradores existentes. RS256, contratos do evento e regras dos cadastros não mudam com a preparação interna.

A saída da ferramenta não imprime conexão, senha, conteúdo de chaves, mensagens internas de exceção ou stack traces. Sucesso retorna código 0; configuração inválida, conflito e cancelamento retornam código 1. O entrypoint propaga uma falha da ferramenta e não inicia a API. Durante o preparo, SIGTERM/SIGINT são encaminhados para o processo .NET; o entrypoint aguarda sua conclusão e termina com 143/130, respectivamente, sem iniciar HTTP. Depois do `exec`, a API recebe os sinais diretamente.

O alvo Docker `preparacao` permanece disponível somente para compatibilidade com provas/integrações anteriores que executavam a ferramenta como job separado. No fluxo padrão, publicar o último alvo `api` já distribui ambos os executáveis na mesma imagem.

## Exportação opcional da chave pública para Catalog

Na integração local, configure **Jwt__PublicKeyExport__Path=/data/jwt-publica/public.pem**. Depois de validar as chaves, aplicar migrations e preparar o administrador, o código .NET publica somente a chave pública nesse arquivo, antes de iniciar HTTP. Sem a variável, o comportamento anterior é preservado.

Monte um volume exclusivo para a pública em **/data/jwt-publica**, gravável no Users e somente leitura no Catalog. O Dockerfile cria o diretório com proprietário app (UID 1654) e permissão 0755; o PEM público usa 0644. O volume **/data/jwt** continua exclusivo do Users: não o monte no Catalog.

A exportação aceita somente o caminho de demonstração acima e um único bloco PEM RSA público com pelo menos 2048 bits. O código reexporta apenas os componentes públicos; não copia a origem inteira. Material privado, blocos adicionais e links simbólicos são recusados. A gravação usa arquivo temporário no mesmo volume e substituição atômica, e preserva a cópia existente quando já corresponde à chave. O modo de demonstração, sua validação de ambiente e suas restrições de banco permanecem necessários.

Catalog deve usar **Jwt__PublicKeys__0__PublicKeyPath=/data/jwt-publica/public.pem** e aguardar Users saudável no Compose. A montagem não requer copiar PEM manualmente ou inserir conteúdo da chave no YAML. A implementação está local; ainda não foi publicada uma nova imagem.