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

    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] PristineCheck
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages

    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
    | [<Inherit;AltCommandLine("-c")>] CleanCheckout of bool
    | [<Inherit>] Token of string
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
            | Release -> "runs build, tests, then creates and validates packages"
            | SingleTarget _ -> "runs the provided sub command without running its dependencies"
            | CleanCheckout _ -> "skip the clean checkout check that guards the release target"
            | Token _ -> "token used to authenticate with GitHub"
            | PristineCheck
            | GeneratePackages
            | ValidatePackages
                -> "Undocumented, dependent target"

    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
