using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Usuarios;

public sealed class TestesManipuladorCriarUsuario
{
    private static readonly DateTimeOffset Agora = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PerfilId = Guid.Parse("4f642cbc-3720-4bb2-b456-15a97049da5c");

    [Fact]
    public async Task ProcessarComDadosValidosNormalizaEProtegeDadosAntesDePersistir()
    {
        var repositorio = new RepositorioUsuariosStub();
        var resultado = await CriarManipulador(repositorio).ProcessarAsync(
            CriarComando(), TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.Criado, resultado.Status);
        Assert.Equal("Maria da Silva", resultado.Usuario!.Nome);
        Assert.Equal("maria@exemplo.com", resultado.Usuario.Email);
        Assert.Equal(Agora, resultado.Usuario.CriadoEmUtc);
        Assert.Equal("12345678900", repositorio.UsuarioAdicionado!.CPF);
        Assert.Equal("hash-protegido", repositorio.UsuarioAdicionado.SenhaHash);
        Assert.NotEqual("Senha@123", repositorio.UsuarioAdicionado.SenhaHash);
        Assert.NotNull(repositorio.RegistroCadastroAdicionado);
        Assert.Equal(repositorio.UsuarioAdicionado.Id, repositorio.RegistroCadastroAdicionado.UsuarioId);
        Assert.Equal(repositorio.UsuarioAdicionado.CriadoEmUtc, repositorio.RegistroCadastroAdicionado.DataCriacao);
        Assert.Equal("Usuário cadastrado.", repositorio.RegistroCadastroAdicionado.Descricao);
        Assert.Equal(TestContext.Current.CancellationToken, repositorio.TokenCancelamentoRecebido);
        Assert.All(
            new[] { repositorio.UsuarioAdicionado.Nome, repositorio.UsuarioAdicionado.CPF, repositorio.UsuarioAdicionado.Email, repositorio.UsuarioAdicionado.SenhaHash },
            valor => Assert.False(string.IsNullOrWhiteSpace(valor)));
    }

    [Fact]
    public async Task ProcessarMantemMesmoInstanteNoUsuarioENaAuditoriaMesmoComRelogioAvancando()
    {
        var repositorio = new RepositorioUsuariosStub();
        var manipulador = new ManipuladorCriarUsuario(repositorio, new HashSenhaStub(), new RelogioProgressivo());

        var resultado = await manipulador.ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.Criado, resultado.Status);
        Assert.NotNull(repositorio.UsuarioAdicionado);
        Assert.NotNull(repositorio.RegistroCadastroAdicionado);
        Assert.Equal(repositorio.UsuarioAdicionado.CriadoEmUtc, repositorio.RegistroCadastroAdicionado.DataCriacao);
    }

    [Fact]
    public async Task ProcessarComComandoNuloRejeitaRequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CriarManipulador(new RepositorioUsuariosStub()).ProcessarAsync(
                null!, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", "12345678900", "maria@exemplo.com", "Senha@123", "nome")]
    [InlineData("Maria da Silva", "", "maria@exemplo.com", "Senha@123", "cpf")]
    [InlineData("Maria da Silva", "12345678900", "", "Senha@123", "email")]
    [InlineData("Maria da Silva", "12345678900", "email-invalido", "Senha@123", "email")]
    [InlineData("Maria da Silva", "12345678900", "maria@exemplo.com", "", "senha")]
    public async Task ProcessarComCampoInvalidoRetornaErroDoCampo(
        string nome, string cpf, string email, string senha, string campo)
    {
        var repositorio = new RepositorioUsuariosStub();
        var comando = new ComandoCriarUsuario(nome, cpf, DateOnly.FromDateTime(Agora.UtcDateTime).AddYears(-20), email, senha, PerfilId);

        var resultado = await CriarManipulador(repositorio).ProcessarAsync(
            comando, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.DadosInvalidos, resultado.Status);
        Assert.Contains(campo, resultado.Erros);
        Assert.Null(repositorio.UsuarioAdicionado);
        Assert.Null(repositorio.RegistroCadastroAdicionado);
    }

    [Fact]
    public async Task ProcessarComDataNascimentoFuturaRetornaErro()
    {
        var repositorio = new RepositorioUsuariosStub();
        var hash = new HashSenhaStub();
        var manipulador = new ManipuladorCriarUsuario(repositorio, hash, new RelogioFixo(Agora));
        var comando = CriarComando() with { DataNascimento = DateOnly.FromDateTime(Agora.UtcDateTime).AddDays(1) };

        var resultado = await manipulador.ProcessarAsync(comando, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.DadosInvalidos, resultado.Status);
        Assert.Contains("dataNascimento", resultado.Erros);
        Assert.False(repositorio.Consultado);
        Assert.False(hash.Calculado);
        Assert.Null(repositorio.UsuarioAdicionado);
        Assert.Null(repositorio.RegistroCadastroAdicionado);
    }

    [Fact]
    public async Task ProcessarComPerfilVazioRetornaErro()
    {
        var resultado = await CriarManipulador(new RepositorioUsuariosStub()).ProcessarAsync(
            CriarComando() with { PerfilId = Guid.Empty }, TestContext.Current.CancellationToken);
        Assert.Contains("perfilId", resultado.Erros);
    }

    [Fact]
    public async Task ProcessarComPerfilInexistenteRetornaResultadoEsperado()
    {
        var repositorio = new RepositorioUsuariosStub { PerfilExiste = false };
        var resultado = await CriarManipulador(repositorio).ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCriacaoUsuario.PerfilNaoEncontrado, resultado.Status);
        Assert.Null(repositorio.UsuarioAdicionado);
        Assert.Null(repositorio.RegistroCadastroAdicionado);
    }

    [Fact]
    public async Task ProcessarComEmailDuplicadoRetornaConflito()
    {
        var repositorio = new RepositorioUsuariosStub { EmailExiste = true };
        var resultado = await CriarManipulador(repositorio)
            .ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCriacaoUsuario.EmailJaCadastrado, resultado.Status);
        Assert.Null(repositorio.UsuarioAdicionado);
        Assert.Null(repositorio.RegistroCadastroAdicionado);
    }

    [Fact]
    public async Task ProcessarComCpfDuplicadoRetornaConflito()
    {
        var repositorio = new RepositorioUsuariosStub { CpfExiste = true };
        var resultado = await CriarManipulador(repositorio)
            .ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCriacaoUsuario.CpfJaCadastrado, resultado.Status);
        Assert.Null(repositorio.UsuarioAdicionado);
        Assert.Null(repositorio.RegistroCadastroAdicionado);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("123456789012")]
    [InlineData("１２３４５６７８９００")]
    [InlineData("١٢٣٤٥٦٧٨٩٠٠")]
    [InlineData("12345678900١")]
    [InlineData("a12345678900")]
    public async Task ProcessarComCpfInvalidoRejeitaAntesDeConsultarBancoOuCalcularHash(string cpf)
    {
        var repositorio = new RepositorioUsuariosStub();
        var hash = new HashSenhaStub();
        var manipulador = new ManipuladorCriarUsuario(repositorio, hash, new RelogioFixo(Agora));

        var resultado = await manipulador.ProcessarAsync(
            CriarComando() with { CPF = cpf }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.DadosInvalidos, resultado.Status);
        Assert.Contains("cpf", resultado.Erros);
        Assert.False(repositorio.Consultado);
        Assert.False(hash.Calculado);
        Assert.Null(repositorio.UsuarioAdicionado);
        Assert.Null(repositorio.RegistroCadastroAdicionado);
    }

    [Fact]
    public async Task ProcessarComCpfFormatadoESemValidarDigitosVerificadoresPreservaOnzeDigitos()
    {
        var repositorio = new RepositorioUsuariosStub();

        var resultado = await CriarManipulador(repositorio).ProcessarAsync(
            CriarComando() with { CPF = " 000.000.000-00 " }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.Criado, resultado.Status);
        Assert.Equal("00000000000", repositorio.UsuarioAdicionado!.CPF);
    }

    [Theory]
    [InlineData(ResultadoGravacaoUsuario.ConflitoEmail, StatusCriacaoUsuario.EmailJaCadastrado)]
    [InlineData(ResultadoGravacaoUsuario.ConflitoCpf, StatusCriacaoUsuario.CpfJaCadastrado)]
    public async Task ProcessarTraduzConflitoNaGravacaoMesmoDepoisDePreConsultasLivres(
        ResultadoGravacaoUsuario gravacao, StatusCriacaoUsuario statusEsperado)
    {
        var repositorio = new RepositorioUsuariosStub { ResultadoGravacao = gravacao };

        var resultado = await CriarManipulador(repositorio).ProcessarAsync(
            CriarComando(), TestContext.Current.CancellationToken);

        Assert.Equal(statusEsperado, resultado.Status);
        Assert.Null(resultado.Usuario);
        Assert.NotNull(repositorio.UsuarioAdicionado);
    }

    [Fact]
    public async Task ProcessarComDataNascimentoAusenteNaoConsultaBancoNemCalculaHash()
    {
        var repositorio = new RepositorioUsuariosStub();
        var hash = new HashSenhaStub();
        var manipulador = new ManipuladorCriarUsuario(repositorio, hash, new RelogioFixo(Agora));

        var resultado = await manipulador.ProcessarAsync(
            CriarComando() with { DataNascimento = default }, TestContext.Current.CancellationToken);

        Assert.Contains("dataNascimento", resultado.Erros);
        Assert.False(repositorio.Consultado);
        Assert.False(hash.Calculado);
    }

    [Fact]
    public async Task ProcessarComNascimentoHojeAceitaDataCivilSemHorario()
    {
        var hoje = DateOnly.FromDateTime(Agora.UtcDateTime);
        var repositorio = new RepositorioUsuariosStub();
        var manipulador = new ManipuladorCriarUsuario(
            repositorio, new HashSenhaStub(), new RelogioFixo(new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero)));

        var resultado = await manipulador.ProcessarAsync(
            CriarComando() with { DataNascimento = hoje }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.Criado, resultado.Status);
        Assert.Equal(hoje, repositorio.UsuarioAdicionado!.DataNascimento);
    }

    [Theory]
    [InlineData(100, 150, StatusCriacaoUsuario.Criado)]
    [InlineData(101, 150, StatusCriacaoUsuario.DadosInvalidos)]
    [InlineData(100, 151, StatusCriacaoUsuario.DadosInvalidos)]
    public async Task ProcessarPreservaLimitesDaAplicacaoMesmoComColunasMaiores(
        int tamanhoNome, int tamanhoEmail, StatusCriacaoUsuario statusEsperado)
    {
        var repositorio = new RepositorioUsuariosStub();
        var hash = new HashSenhaStub();
        var manipulador = new ManipuladorCriarUsuario(repositorio, hash, new RelogioFixo(Agora));
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', tamanhoEmail - 129);
        var comando = CriarComando() with { Nome = new string('n', tamanhoNome), Email = email };

        var resultado = await manipulador.ProcessarAsync(comando, TestContext.Current.CancellationToken);

        Assert.Equal(statusEsperado, resultado.Status);
        if (statusEsperado == StatusCriacaoUsuario.DadosInvalidos)
        {
            Assert.Contains(tamanhoNome > 100 ? "nome" : "email", resultado.Erros);
            Assert.False(repositorio.Consultado);
            Assert.False(hash.Calculado);
            Assert.Null(repositorio.UsuarioAdicionado);
            Assert.Null(repositorio.RegistroCadastroAdicionado);
        }
        else
        {
            Assert.Equal(tamanhoNome, repositorio.UsuarioAdicionado!.Nome.Length);
            Assert.Equal(tamanhoEmail, repositorio.UsuarioAdicionado.Email.Length);
        }
    }

    private static ComandoCriarUsuario CriarComando() => new(
        "  Maria   da Silva  ", "123.456.789-00", DateOnly.FromDateTime(Agora.UtcDateTime).AddYears(-20),
        "  MARIA@EXEMPLO.COM  ", "Senha@123", PerfilId);

    private static ManipuladorCriarUsuario CriarManipulador(RepositorioUsuariosStub repositorio) =>
        new(repositorio, new HashSenhaStub(), new RelogioFixo(Agora));

    private sealed class RepositorioUsuariosStub : IRepositoryUsuarios
    {
        public bool PerfilExiste { get; init; } = true;
        public bool Consultado { get; private set; }
        public ResultadoGravacaoUsuario ResultadoGravacao { get; init; } = ResultadoGravacaoUsuario.Sucesso;
        public bool EmailExiste { get; init; }
        public bool CpfExiste { get; init; }
        public Usuario? UsuarioAdicionado { get; private set; }
        public LogUsuario? RegistroCadastroAdicionado { get; private set; }
        public CancellationToken TokenCancelamentoRecebido { get; private set; }
        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken token = default) => RegistrarConsulta<Usuario?>(null);
        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken token = default) => RegistrarConsulta<Usuario?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(string email, CancellationToken token = default) => RegistrarConsulta<UsuarioAutenticacao?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(Guid id, CancellationToken token = default) => RegistrarConsulta<UsuarioAutenticacao?>(null);
        public Task<bool> ExisteEmailAsync(string email, Guid? ignorarId, CancellationToken token = default) => RegistrarConsulta(EmailExiste);
        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarId, CancellationToken token = default) => RegistrarConsulta(CpfExiste);
        public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken token = default) => RegistrarConsulta(PerfilExiste);
        private Task<T> RegistrarConsulta<T>(T valor)
        {
            Consultado = true;
            return Task.FromResult(valor);
        }
        public Task<ResultadoGravacaoUsuario> TentarAdicionarAsync(Usuario usuario, LogUsuario registroCadastro, CancellationToken token = default)
        {
            UsuarioAdicionado = usuario;
            RegistroCadastroAdicionado = registroCadastro;
            TokenCancelamentoRecebido = token;
            return Task.FromResult(ResultadoGravacao);
        }
        public Task<ResultadoGravacaoUsuario> AtualizarAsync(Usuario usuario, CancellationToken token = default) => Task.FromResult(ResultadoGravacaoUsuario.Sucesso);
    }

    private sealed class HashSenhaStub : IHashSenha
    {
        public bool Calculado { get; private set; }
        public string Criar(string senha)
        {
            Calculado = true;
            return "hash-protegido";
        }
    }

    private sealed class RelogioProgressivo : TimeProvider
    {
        private DateTimeOffset _agora = Agora;

        public override DateTimeOffset GetUtcNow()
        {
            var instante = _agora;
            _agora = _agora.AddSeconds(1);
            return instante;
        }
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
