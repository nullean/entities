module Paths

open System
open System.IO

let ToolName = "nullean-entities"
let Repository = "nullean/entities"
let MainTFM = "net10.0"
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
        .Add("Nullean.Entities.Bogus", "Nullean.Entities.Bogus")

let buildOnlyPackages = Set.empty<string>

let mapNugetToProject =
    mapProjectToNuget
    |> Map.fold (fun (m: Map<string, string>) key value -> m.Add(value, key)) Map.empty<string, string>

// Test projects discovered under tests/, run in dependency order (Entities has no deps,
// Reactive depends on Entities, CsCheck depends on Reactive, Bogus is standalone).
let TestProjects =
    [ "tests/Nullean.Entities.Tests"
      "tests/Nullean.Entities.Reactive.Tests"
      "tests/Nullean.Entities.Bogus.Tests" ]
