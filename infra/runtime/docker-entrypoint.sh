#!/bin/sh
set -eu

# A preparação é exclusiva da demonstração e nunca é ativada implicitamente.
habilitar_demonstracao="${DemonstracaoLocal__Habilitada-false}"
case "$habilitar_demonstracao" in
    true|false) ;;
    *)
        printf '%s\n' 'DemonstracaoLocal__Habilitada deve ser true ou false.' >&2
        exit 1
        ;;
esac

pid_preparacao=''
iniciando_preparacao=false
codigo_cancelamento=0

cancelar() {
    codigo_cancelamento="$2"
    if [ -z "$pid_preparacao" ] && [ "$iniciando_preparacao" = 'true' ]; then
        # Cobre o intervalo entre criar o processo e atribuir seu PID.
        pid_preparacao="${!:-}"
    fi
    if [ -n "$pid_preparacao" ]; then
        # O helper cancela o trabalho e libera sua sessão/lock antes de sair.
        kill -s "$1" "$pid_preparacao" 2>/dev/null || :
    else
        # Impede iniciar HTTP se o sinal chegar durante a transição entre processos.
        exit "$codigo_cancelamento"
    fi
}

trap 'cancelar TERM 143' TERM
trap 'cancelar INT 130' INT

if [ "$habilitar_demonstracao" = 'true' ]; then
    printf '%s\n' 'Preparando o ambiente explícito de demonstração antes de iniciar o UsersAPI.'
    iniciando_preparacao=true
    dotnet /app/preparacao/FCG.Users.DemoSetup.dll --preparar-demonstracao &
    pid_preparacao=$!
    iniciando_preparacao=false

    # wait pode ser interrompido pelo trap enquanto o helper ainda está finalizando.
    while :; do
        if wait "$pid_preparacao"; then
            codigo_preparacao=0
        else
            codigo_preparacao=$?
        fi

        if [ "$codigo_cancelamento" -eq 0 ] || ! kill -0 "$pid_preparacao" 2>/dev/null; then
            break
        fi
    done
    pid_preparacao=''

    if [ "$codigo_cancelamento" -ne 0 ]; then
        exit "$codigo_cancelamento"
    fi
    if [ "$codigo_preparacao" -ne 0 ]; then
        exit "$codigo_preparacao"
    fi
fi

# A API assume PID 1 e recebe diretamente os sinais depois da preparação.
exec dotnet /app/FCG.Users.Api.dll "$@"
