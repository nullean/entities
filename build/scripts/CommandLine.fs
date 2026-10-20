module CommandLine

open Argu
open Microsoft.FSharp.Reflection

type Arguments =
    | [<CliPrefix(CliPrefix.None);SubCommand>] Clean
    | [<CliPrefix(CliPrefix.None);SubCommand>] Build
    | [<CliPrefix(CliPrefix.None);SubCommand>] Test
    | [<CliPrefix(CliPrefix.None);SubCommand>] Aot
    | [<CliPrefix(CliPrefix.None);SubCommand>] Apps
    | [<CliPrefix(CliPrefix.None);SubCommand>] Pack
    | [<CliPrefix(CliPrefix.None);SubCommand>] Release
    | [<CliPrefix(CliPrefix.None);SubCommand>] Docs

    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] PristineCheck
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateApiChanges

    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
    | [<Inherit;AltCommandLine("-c")>] CleanCheckout of bool
    | [<Inherit>] Port of int
    | [<Inherit>] NoServe
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clean -> "clean known output locations"
            | Build -> "runs build"
            | Test -> "runs build then tests"
            | Aot -> "publishes examples/Nullean.Entities.AotSmoke NativeAOT (trim/AOT warnings are errors) and runs it"
            | Apps -> "runs every console app under examples/Apps; one that exits non-zero fails the build"
            | Pack -> "builds and packs the NuGet packages"
            | Release -> "runs the clean checkout check, tests, packs and validates the packages, then writes the public API changes since the last release"
            | Docs -> "builds the public docs, applies the landing page override, and serves them"
            | Port _ -> "port to serve the documentation on (default 8080)"
            | NoServe -> "build the documentation without serving it"
            | SingleTarget _ -> "runs the provided sub command without running its dependencies"
            | CleanCheckout _ -> "skip the clean checkout check that guards the release target"
            | PristineCheck
            | GeneratePackages
            | ValidatePackages
            | GenerateApiChanges
                -> "Undocumented, dependent target"

    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
