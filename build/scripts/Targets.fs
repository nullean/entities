module Targets

#nowarn "3391" // Nullable<int> implicit conversions from ProcNet's ExitCode

open Argu
open System
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

// `dotnet tool restore` restores the whole manifest, and on a cold NuGet cache it reports the husky entry as failed ("not contained in the package with Package Id husky;
// the commands contained in the package are curb") while every other tool, including the ones the build runs, restores. husky and curb are git-hook tools the build never
// runs, so a failed restore is reported and the build goes on: a tool that is really missing fails loudly where it is first run.
let private restoreTools =
    lazy(
        try
            exec "dotnet" ["tool"; "restore"]
        with :? ProcExecException as e ->
            printfn "WARNING: dotnet tool restore did not restore every tool (%s); continuing, the tools the build runs fail on their own if they are missing" e.Message
    )

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
        for framework in Paths.Frameworks do
            exec "dotnet" ["run"; "--project"; project; "-c"; "Release"; "--framework"; framework]

let private apps (arguments:ParseResults<Arguments>) =
    for app in Paths.Apps do
        let code = execResult "dotnet" ["run"; "--project"; app; "-c"; "Release"; "--framework"; "net10.0"]
        if code.HasValue && code.Value <> 0 then failwithf "%s exited with %O" app code

let private aot (arguments:ParseResults<Arguments>) =
    exec "dotnet" ["publish"; "examples/Nullean.Entities.AotSmoke"; "-c"; "Release"; "-f"; "net10.0"]
    let binary = Path.Combine(Paths.Root.FullName, ".artifacts", "publish", "Nullean.Entities.AotSmoke", "release_net10.0", if System.OperatingSystem.IsWindows() then "Nullean.Entities.AotSmoke.exe" else "Nullean.Entities.AotSmoke")
    // Proc.Exec, not Proc.Start: the smoke app exits at once, and Proc.Start's buffered readers can then touch a process that is already disposed
    // (ObjectDisposedException on a fast runner). Exec streams to the console and throws on a non-zero exit, which is the same check.
    exec binary []

/// Builds docs/ the way the workflow does, including the landing page override (the reason `docs-builder serve` is not enough), and serves the result.
let private docs (arguments:ParseResults<Arguments>) =
    Documentation.build ()
    if arguments.Contains NoServe then
        printfn "built; --noserve given, not serving"
    else
        Documentation.serve (arguments.TryGetResult Port |> Option.defaultValue 8080)

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
    // a package that is missing is as much a failure as one that is wrong: every package this repository ships must have been packed
    let expected = Paths.mapProjectToNuget |> Map.toList |> List.map fst
    let missing = expected |> List.filter (fun name -> not (packages |> Array.exists (fun p -> Path.GetFileName(p).StartsWith(name + "." + version.Split('+').[0], System.StringComparison.Ordinal))))
    if not missing.IsEmpty then failwithf "These packages were not packed: %s" (String.concat ", " missing)
    // Nullean.Entities carries the [Constraints] source generator as an analyzer; a package without it builds and restores, and then [Constraints] does nothing for whoever installs it
    // (0.1.0-preview.1 shipped like that). The validator checks the assembly, not what else the package holds, so this is checked here.
    let core = packages |> Array.tryFind (fun p -> Path.GetFileName(p).StartsWith("Nullean.Entities." + version.Split('+').[0], System.StringComparison.Ordinal))
    match core with
    | None -> failwith "Nullean.Entities was not packed"
    | Some path ->
        use zip = System.IO.Compression.ZipFile.OpenRead path
        if not (zip.Entries |> Seq.exists (fun e -> e.FullName = "analyzers/dotnet/cs/Nullean.Entities.SourceGenerators.dll")) then
            failwithf "%s does not contain analyzers/dotnet/cs/Nullean.Entities.SourceGenerators.dll, so [Constraints] would do nothing for a consumer" (Path.GetFileName path)
    // The validator is a net8 tool that loads each package's assembly and its references, and a net11.0 assembly references System.Runtime 11, which an older runtime cannot
    // supply ("Could not load file or assembly 'System.Runtime, Version=11.0.0.0'"). Letting it roll forward to the newest runtime installed, previews included, is what lets it read them.
    Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", "LatestMajor")
    Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD_TO_PRERELEASE", "1")
    for package in packages do
        exec "dotnet" ["nupkg-validator"; package; "-v"; version; "-k"; Paths.SignKey]

// A package that has never been published has nothing to compare the public API with, which is every package on a first release.
let private isOnNuget (package:string) =
    let url = sprintf "https://api.nuget.org/v3-flatcontainer/%s/index.json" (package.ToLowerInvariant())
    let r = Proc.Start("curl", "-s", "-L", "-o", "/dev/null", "-w", "%{http_code}", url)
    r.ConsoleOut |> Seq.exists (fun l -> l.Line.Trim() = "200")

let private generateApiChanges (arguments:ParseResults<Arguments>) =
    restoreTools.Value |> ignore
    let output = Paths.RootRelative Paths.Output.FullName
    for package in Paths.mapProjectToNuget |> Map.toList |> List.map fst do
        if not (isOnNuget package) then
            printfn "%s: not on nuget.org yet, no previous public API to compare with" package
        else
            printfn "%s: public API changes since the previous release" package
            exec "dotnet"
                [ "assembly-differ"
                  sprintf "previous-nuget|%s|%s|%s" package currentVersion.Value Paths.MainTFM
                  // UseArtifactsOutput puts a multi-targeted project's build in release_<tfm>
                  sprintf "directory|.artifacts/bin/%s/release_%s" package Paths.MainTFM
                  "-a"; "true"; "--target"; package; "-f"; "github-comment"
                  "--output"; Path.Combine(output, sprintf "breaking-changes-%s.md" package) ]

let Setup (parsed:ParseResults<Arguments>) (arguments:Arguments) =
    let singleTarget = parsed.TryGetResult SingleTarget |> Option.defaultValue false

    let target name action =
        if singleTarget then Targets.Target(name, System.Action(fun () -> action parsed))
        else Targets.Target(name, System.Action(fun () -> action parsed))

    let dep name (deps: string list) action =
        if singleTarget then Targets.Target(name, System.Action(fun () -> action parsed))
        else Targets.Target(name, (deps :> string seq), System.Action(fun () -> action parsed))

    target "clean" clean
    target "docs" docs
    target "pristinecheck" pristineCheck
    dep "build" ["clean"] build
    dep "apps" ["build"] apps
    dep "test" ["build"; "apps"] test
    dep "aot" ["build"] aot
    dep "generatepackages" ["build"] generatePackages
    dep "validatepackages" ["generatepackages"] validatePackages
    dep "pack" ["test"; "generatepackages"; "validatepackages"] (fun _ -> printfn "Pack complete")
    target "generateapichanges" generateApiChanges
    dep "release" ["pristinecheck"; "pack"; "generateapichanges"] (fun _ -> printfn "Release complete")
