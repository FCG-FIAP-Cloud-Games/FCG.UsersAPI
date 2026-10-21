# FCG.UsersAPI

Microsserviço de usuários e autenticação do FIAP Cloud Games, em .NET 8. Possui banco PostgreSQL próprio, JWT RS256, auditoria e publicação de `UserCreatedEvent` com Outbox e RabbitMQ.

## Executar junto com as quatro APIs

A composição oficial fica no repositório [FCG-FIAP-Repo-Infra](https://github.com/FCG-FIAP-Cloud-Games/FCG-FIAP-Repo-Infra). Consulte o README desse repositório e execute `docker compose up -d` na pasta `docker-compose` dele.

Esta alteração está preparada para **teste local**, sem publicação das imagens novas. A imagem do Users é `fcg/users-api:simplificado-local`; ela acrescenta a exportação automática da chave pública. A versão `fgcprojetofiap/users-api:1.3` já publicada mantém o bootstrap anterior, mas não contém essa exportação nova. O build local é:

```powershell
docker build -t fcg/users-api:simplificado-local .
```

Não há uma segunda imagem nem um container preparador. O entrypoint da imagem executa a preparação interna apenas quando `DemonstracaoLocal__Habilitada=true`; depois, a API assume o processo principal.

## Como a chave chega ao Catalog

1. Users gera ou valida seu par RSA persistente em `/data/jwt`.
2. No modo de demonstração, `Jwt__PublicKeyExport__Path=/data/jwt-publica/public.pem` ativa a exportação somente da pública para outro volume.
3. Catalog monta `jwt-publica` como somente leitura e lê `Jwt__PublicKeys__0__PublicKeyPath=/data/jwt-publica/public.pem` depois do Users saudável.
4. Catalog valida os tokens localmente. Não consulta Users em cada operação.

O volume privado `/data/jwt` pertence exclusivamente ao Users. O arquivo exportado é reconstituído a partir dos componentes públicos RSA, gravado de forma atômica e conservado quando seu conteúdo não mudou. No Linux, o diretório público usa `0755` e o arquivo `0644`; a privada permanece `0600`, dentro de diretório `0700`, com usuário `app`/1654.

Sem a configuração de exportação, o comportamento anterior permanece. Trocar uma chave intencionalmente exige reiniciar Catalog para ele carregar a nova pública. A inicialização normal preserva o par existente.

## Preparação local

A ferramenta interna em `tools/FCG.Users.DemoSetup` prepara chaves, aplica as migrations Users e cria o primeiro administrador sintético antes do HTTP. O banco e seu usuário já precisam existir; sua criação pertence à infraestrutura.

O modo exige container Linux, `Development`, `DemonstracaoLocal__Habilitada=true`, servidor `postgres` e banco `fcg_users_demo`. A configuração é explícita: fora desse modo, a aplicação exige banco, tabelas e chaves previamente preparados. [Detalhes do bootstrap](tools/FCG.Users.DemoSetup/README.md).

Configurações principais da composição:

| Configuração | Finalidade |
|---|---|
| `ConnectionStrings__UsersDatabase` | Conexão ao banco exclusivo de Users. |
| `Jwt__Issuer` / `Jwt__Audience` | Emissor e destinatário reconhecidos pelo Catalog. |
| `Jwt__Signing__ActiveKeyId=users-demo-v1` | Identificador da chave de assinatura local. |
| `Jwt__Signing__PrivateKeyPath=/data/jwt/private.pem` | Privada exclusiva do Users. |
| `Jwt__Validation__Keys__0__KeyId=users-demo-v1` | Identificador da pública correspondente. |
| `Jwt__Validation__Keys__0__PublicKeyPath=/data/jwt/public.pem` | Pública usada pelo Users para verificar tokens. |
| `Jwt__PublicKeyExport__Path=/data/jwt-publica/public.pem` | Exportação opcional para o Catalog. |
| `Outbox__Enabled=true` | Ativa o publicador das intenções persistidas. |
| `RabbitMq__Host`, `Port`, `VirtualHost`, `Username`, `Password` | Broker compartilhado da infraestrutura. |

Admin local de um banco novo: `admin@fcg.local` / `Admin_Demo_2026!`. Credenciais exclusivamente de demonstração.

## Desenvolvimento isolado do Users

O `compose.yaml` deste repositório é uma alternativa para desenvolver apenas Users e suas dependências. Ele não substitui a composição dos quatro microsserviços:

```powershell
docker compose up --build -d
docker compose ps -a
```

Swagger padrão: http://localhost:5088/swagger. Esse ambiente isolado tem seu próprio nome de projeto e volumes. Não remova os arquivos de `infra/demo`, utilizados por esse Compose e suas verificações.

## Arquitetura e comunicação

- `Api`: HTTP, autenticação, autorização e contratos de entrada/saída.
- `Application`: casos de uso, interfaces e contrato de integração.
- `Domain`: regras de usuário, perfil, sessão e auditoria.
- `Infrastructure`: PostgreSQL, migrations, RSA, transações, Outbox e RabbitMQ.
- `tools/FCG.Users.DemoSetup`: preparação interna explícita de demonstração.

Cadastro público, administrativo e primeiro administrador persistem usuário, auditoria e evento na mesma transação. O worker publica `UserCreatedEvent` para Notifications. A comunicação não utiliza chamada HTTP direta nem dependência da DLL Notifications. A confirmação do broker não comprova processamento ou envio de e-mail; o consumidor deve tratar possíveis repetições.

Access tokens duram 15 minutos; refresh tokens são persistidos como hash e duram sete dias. A renovação gira o refresh, e o logout revoga os refreshs ativos. Um access token já emitido pode continuar válido até expirar. Alterações de dados, perfil e inativação são auditadas.

Rotas principais: cadastro em `POST /api/v1/usuarios`, login/refresh/logout em `/api/v1/auth`, consulta e edição do próprio cadastro ou por administrador, criação administrativa e alteração de perfil/inativação por administrador. Os exemplos estão em [FCG.Users.Api.http](src/FCG.Users.Api/FCG.Users.Api.http).

## Testes

```powershell
dotnet test FCG.Users.sln -c Release
```

Os testes de integração usam Docker quando precisam de PostgreSQL/RabbitMQ. Os testes de exportação cobrem PEM público, rejeição de privada/configurações indevidas, gravação atômica, preservação, permissões e links simbólicos. O verificador `scripts/Verificar-DemonstracaoE15.ps1` é uma prova opcional de instalação e retomada; não participa da execução do grupo.

As evidências temporárias ficam em `TestResults`, ignorado pelo Git. Os materiais antigos E16/E17 e os exemplos anteriores de distribuição foram preservados no histórico local durante a revisão de pendências; não são necessários para construir ou executar esta versão.
