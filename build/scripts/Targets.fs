module Targets

#nowarn "3391" // Nullable<int> implicit conversions from ProcNet's ExitCode

open Argu
open System.IO
open Bullseye
open CommandLine
open Fake.Tools.Git
open ProcNet

let exec binary args =
    Proc.Exec (binary, List.toArray args) |> ignore

let private execResult binary args =
    let result = Proc.Start(binary, List.toArray args)
    result.ExitCode

let private restoreTools = lazy(exec "dotnet" ["tool"; "restore"])

let private currentVersion =
    lazy(
        restoreTools.Value |> ignore
        let r = Proc.Start("dotnet", "minver", "-p", "canary.0", "-m", "0.1")
        let o = r.ConsoleOut |> Seq.find (fun l -> not(l.Line.StartsWith("MinVer:")))
        o.Line
    )

let private currentVersionInformational =
    lazy(
        match Paths.IncludeGitHashInInformational with
        | false -> currentVersion.Value
        | true -> sprintf "%s+%s" currentVersion.Value (Information.getCurrentSHA1("."))
    )

let private clean (arguments:ParseResults<Arguments>) =
    if (Paths.Output.Exists) then Paths.Output.Delete (true)
    exec "dotnet" ["clean"; "-c"; "Release"]

let private build (arguments:ParseResults<Arguments>) =
    exec "dotnet" ["build"; "-c"; "Release"]

let private test (arguments:ParseResults<Arguments>) =
    for project in Paths.TestProjects do
        exec "dotnet" ["run"; "--project"; project; "-c"; "Release"]

let private pristineCheck (arguments:ParseResults<Arguments>) =
    let doCheck = arguments.TryGetResult CleanCheckout |> Option.defaultValue true
    match doCheck, Information.isCleanWorkingCopy "." with
    | _, true  -> printfn "The checkout folder does not have pending changes, proceeding"
    | false, _ -> printf "Checkout is dirty but -c was specified to ignore this"
    | _ -> failwithf "The checkout folder has pending changes, aborting"

let private generatePackages (arguments:ParseResults<Arguments>) =
    let version = currentVersionInformational.Value
    // -o is required: UseArtifactsOutput defaults `dotnet pack` to .artifacts/package/release,
    // not Paths.Output, so validatePackages (which reads Paths.Output) would find nothing there.
    if not Paths.Output.Exists then Paths.Output.Create()
    let output = Paths.RootRelative Paths.Output.FullName
    exec "dotnet" ["pack"; "-c"; "Release"; sprintf "-p:Version=%s" version; "-o"; output]

let private validatePackages (arguments:ParseResults<Arguments>) =
    restoreTools.Value |> ignore
    // The DLL's baked-in AssemblyInformationalVersion carries the +hash suffix (it is what
    // generatePackages passed as -p:Version), so validation has to check against the same
    // value curb itself validates against, not the bare tag version.
    let version = currentVersionInformational.Value
    let packages = Directory.GetFiles(Paths.Output.FullName, "*.nupkg")
    for package in packages do
        exec "dotnet" ["nupkg-validator"; package; "-v"; version]

let Setup (parsed:ParseResults<Arguments>) (arguments:Arguments) =
    let singleTarget = parsed.TryGetResult SingleTarget |> Option.defaultValue false

    let target name action =
        if singleTarget then Targets.Target(name, System.Action(fun () -> action parsed))
        else Targets.Target(name, System.Action(fun () -> action parsed))

    let dep name (deps: string list) action =
        if singleTarget then Targets.Target(name, System.Action(fun () -> action parsed))
        else Targets.Target(name, (deps :> string seq), System.Action(fun () -> action parsed))

    target "clean" clean
    target "pristinecheck" pristineCheck
    dep "build" ["clean"] build
    dep "test" ["build"] test
    dep "generatepackages" ["build"] generatePackages
    dep "validatepackages" ["generatepackages"] validatePackages
    dep "pack" ["test"; "generatepackages"; "validatepackages"] (fun _ -> printfn "Pack complete")
    dep "release" ["pristinecheck"; "pack"] (fun _ -> printfn "Release complete")
