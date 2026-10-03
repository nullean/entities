module Paths

open System
open System.IO

let ToolName = "nullean-entities"
let Repository = "nullean/entities"
let MainTFM = "net10.0"
// Every TFM a multi-targeted project in this repo builds — kept in sync with the
// TargetFrameworks set in the root Directory.Build.props. `test` runs every test project once
// per entry here, so a net11.0-only regression (e.g. a real C# union failing to compile or
// behave the same as the net10.0 record-hierarchy fallback) fails the same gate as net10.0.
let Frameworks = [ "net10.0"; "net11.0" ]
let SignKey = "b04a6ff7fe029dc7"

let IncludeGitHashInInformational = true

let Root =
    let mutable dir = DirectoryInfo(".")
    while dir.GetFiles("*.sln").Length = 0 && dir.GetFiles("*.slnx").Length = 0 do
        dir <- dir.Parent |> Option.ofObj |> Option.defaultWith (fun () -> failwith "no solution file found in any parent directory")
    Environment.CurrentDirectory <- dir.FullName
    dir

let RootRelative path = Path.GetRelativePath(Root.FullName, path)

let Output = DirectoryInfo(Path.Combine(Root.FullName, ".artifacts", "build-output"))

let mapProjectToNuget =
    Map.empty
        .Add("Nullean.Entities", "Nullean.Entities")
        .Add("Nullean.Entities.Reactive", "Nullean.Entities.Reactive")
        .Add("Nullean.Entities.CsCheck", "Nullean.Entities.CsCheck")

let buildOnlyPackages = Set.empty<string>

let mapNugetToProject =
    mapProjectToNuget
    |> Map.fold (fun (m: Map<string, string>) key value -> m.Add(value, key)) Map.empty<string, string>

// Test projects discovered under tests/, run in dependency order (Entities has no deps,
// Reactive depends on Entities, CsCheck depends on Reactive).
/// The console apps under examples/Apps. `apps` runs each; one that exits non-zero (a scenario stopped behaving as narrated) fails the build.
let Apps =
    [ "examples/Apps/OrderFulfilment"
      "examples/Apps/Banking"
      "examples/Apps/SupportDesk"
      "examples/Apps/EventSourcedCart"
      "examples/Apps/Inventory"
      "examples/Apps/DataAudit"
      "examples/Apps/OrgChart"
      "examples/Apps/DocsSiteBuild" ]

let TestProjects =
    [ "tests/Nullean.Entities.Tests"
      "tests/Nullean.Entities.SourceGenerators.Tests"
      "tests/Nullean.Entities.Reactive.Tests"
      "tests/Nullean.Entities.CsCheck.Tests"
      "tests/Nullean.Entities.CsCheck.AutoBogus.Tests"
      "tests/Nullean.Entities.FileSystem.Tests"
      "tests/Nullean.Entities.CsCheck.FileSystem.Tests"
      "tests/Nullean.Entities.Examples.Tests"
      "tests/Nullean.Entities.Apps.Tests" ]
