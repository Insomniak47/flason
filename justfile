set shell := ["pwsh", "-c"]

test:
    {{justfile_directory()}}/tests/cli-output/run-tests.ps1

run-mtg-bench:
    dotnet run -c release --project "{{justfile_directory()}}/src/Ttd2089.Flason.Cli/Ttd2089.Flason.Cli.csproj" -- "{{justfile_directory()}}/src/Ttd2089.Flason.Cli/test.json"