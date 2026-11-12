module Program

open System
open System.IO
open Nullean.Make.Fs
open Proc.Fs

/// The build's targets; `./build.sh --help` lists them with these descriptions
type Target =
    /// clean known output locations
    | Clean
    /// runs build
    | Build
    /// runs build then tests
    | Test
    /// publishes examples/Nullean.Entities.AotSmoke NativeAOT (trim/AOT warnings are errors) and runs it
    | Aot
    /// runs every console app under examples/Apps; one that exits non-zero fails the build
    | Apps
    /// builds and packs the NuGet packages
    | Pack
    /// runs the clean checkout check, tests, packs and validates the packages, then writes the public API changes since the last release
    | Release
    /// builds the public docs, applies the landing page override, and serves them
    | Docs
    /// fails when the checkout has pending changes, unless --cleancheckout false (-c false) is given
    | Pristinecheck
    /// packs every package into .artifacts/build-output
    | Generatepackages
    /// checks every package was packed, carries the source generator, and passes the package validator
    | Validatepackages
    /// writes the public API changes since the previous release of every package already on nuget.org
    | Generateapichanges

/// A package that has never been published has nothing to compare the public API with, which is every package on a first release.
let private isOnNuget (package: string) =
    let url = $"https://api.nuget.org/v3-flatcontainer/{package.ToLowerInvariant()}/index.json"
    // anything but a 200, a failing curl included, means there is no previous release to compare with
    try
        shell {
            let! status = text $"curl -s -L -o /dev/null -w %%{{http_code}} {url}"
            return status = "200"
        }
    with _ -> false

[<EntryPoint>]
let main argv =
    let app = MakeApp<Target>("./build.sh", None)
    let cleanCheckout =
        app.Option<bool>("--cleancheckout", short = "-c", desc = "skip the clean checkout check that guards the release target", defaultValue = true)
    let port = app.Option<int>("--port", desc = "port to serve the documentation on (default 8080)", defaultValue = 8080)
    let noServe = app.Flag("--noserve", desc = "build the documentation without serving it")

    // `-s` skips every dependency a target or command `requires`: `build -s` does not clean, `pack -s` only reports
    app.Bind <| function
        | Clean -> target [] {
              if Paths.Output.Exists then Paths.Output.Delete true
              sh $"dotnet clean -c Release" }
        | Build -> target [Clean] { sh $"dotnet build -c Release" }
        | Apps -> target [Build] {
              for project in Paths.Apps do
                  let! code = exitCode $"dotnet run --project {project} -c Release --framework net10.0"
                  if code <> 0 then failwithf "%s exited with %d" project code }
        | Test -> target [Build; Apps] {
              for project in Paths.TestProjects do
                  for framework in Paths.Frameworks do
                      sh $"dotnet run --project {project} -c Release --framework {framework}" }
        | Aot -> target [Build] {
              sh $"dotnet publish examples/Nullean.Entities.AotSmoke -c Release -f net10.0"
              let binary =
                  Path.Combine(
                      Paths.Root.FullName, ".artifacts", "publish", "Nullean.Entities.AotSmoke", "release_net10.0",
                      if OperatingSystem.IsWindows() then "Nullean.Entities.AotSmoke.exe" else "Nullean.Entities.AotSmoke")
              // a non-zero exit fails the target
              sh $"{binary}" }
        | Docs -> target [] {
              // Builds docs/ the way the workflow does, including the landing page override (the reason `docs-builder serve` is not enough), and serves the result.
              Documentation.build ()
              if noServe.Value then printfn "built; --noserve given, not serving"
              else Documentation.serve port.Value }
        | Pristinecheck -> target [] {
              let! changes = lines $"git status --porcelain"
              match cleanCheckout.Value, List.isEmpty changes with
              | _, true -> printfn "The checkout folder does not have pending changes, proceeding"
              | false, _ -> printf "Checkout is dirty but -c was specified to ignore this"
              | _ -> failwithf "The checkout folder has pending changes, aborting" }
        | Generatepackages -> target [Build] {
              let version = Versions.informational.Value
              // -o is required: UseArtifactsOutput defaults `dotnet pack` to .artifacts/package/release,
              // not Paths.Output, so validatepackages (which reads Paths.Output) would find nothing there.
              if not Paths.Output.Exists then Paths.Output.Create()
              let output = Paths.RootRelative Paths.Output.FullName
              sh $"dotnet pack -c Release -p:Version={version} -o {output}" }
        | Validatepackages -> target [Generatepackages] {
              Versions.restoreTools.Value
              // The DLL's baked-in AssemblyInformationalVersion carries the +hash suffix (it is what
              // generatepackages passed as -p:Version), so validation has to check against the same
              // value curb itself validates against, not the bare tag version.
              let version = Versions.informational.Value
              let packages = Directory.GetFiles(Paths.Output.FullName, "*.nupkg")
              let packedAs (name: string) (path: string) =
                  Path.GetFileName(path).StartsWith(name + "." + version.Split('+').[0], StringComparison.Ordinal)
              // a package that is missing is as much a failure as one that is wrong: every package this repository ships must have been packed
              let expected = Paths.mapProjectToNuget |> Map.toList |> List.map fst
              let missing = expected |> List.filter (fun name -> not (packages |> Array.exists (packedAs name)))
              if not missing.IsEmpty then failwithf "These packages were not packed: %s" (String.concat ", " missing)
              // Nullean.Entities carries the [Constraints] source generator as an analyzer; a package without it builds and restores, and then [Constraints] does nothing for whoever installs it
              // (0.1.0-preview.1 shipped like that). The validator checks the assembly, not what else the package holds, so this is checked here.
              match packages |> Array.tryFind (packedAs "Nullean.Entities") with
              | None -> failwith "Nullean.Entities was not packed"
              | Some path ->
                  use zip = Compression.ZipFile.OpenRead path
                  if not (zip.Entries |> Seq.exists (fun e -> e.FullName = "analyzers/dotnet/cs/Nullean.Entities.SourceGenerators.dll")) then
                      failwithf "%s does not contain analyzers/dotnet/cs/Nullean.Entities.SourceGenerators.dll, so [Constraints] would do nothing for a consumer" (Path.GetFileName path)
              // The validator is a net8 tool that loads each package's assembly and its references, and a net11.0 assembly references System.Runtime 11, which an older runtime cannot
              // supply ("Could not load file or assembly 'System.Runtime, Version=11.0.0.0'"). Letting it roll forward to the newest runtime installed, previews included, is what lets it read them.
              Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD", "LatestMajor")
              Environment.SetEnvironmentVariable("DOTNET_ROLL_FORWARD_TO_PRERELEASE", "1")
              for package in packages do
                  sh $"dotnet nupkg-validator validate {package} -v {version} -k {Paths.SignKey}" }
        | Pack -> command {
              requires [Test; Generatepackages; Validatepackages] }
                  |> executes { printfn "Pack complete" }
        | Generateapichanges -> target [] {
              Versions.restoreTools.Value
              let version = Versions.current.Value
              let output = Paths.RootRelative Paths.Output.FullName
              for package in Paths.mapProjectToNuget |> Map.toList |> List.map fst do
                  if not (isOnNuget package) then
                      printfn "%s: not on nuget.org yet, no previous public API to compare with" package
                  else
                      printfn "%s: public API changes since the previous release" package
                      let report = Path.Combine(output, $"breaking-changes-{package}.md")
                      // quoted: an unquoted | would pipe. UseArtifactsOutput puts a multi-targeted project's build in release_<tfm>
                      sh $"dotnet assembly-differ diff 'previous-nuget|{package}|{version}|{Paths.MainTFM}' 'directory|.artifacts/bin/{package}/release_{Paths.MainTFM}' -a --target {package} -f markdown --output {report}" }
        | Release -> command {
              requires [Pristinecheck; Pack; Generateapichanges] }
                     |> executes { printfn "Release complete" }

    app.RunAsync(argv).GetAwaiter().GetResult()
