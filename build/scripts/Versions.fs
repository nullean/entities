module Versions

open Proc.Fs

// `dotnet tool restore` restores the whole manifest, and on a cold NuGet cache it reports the husky entry as failed ("not contained in the package with Package Id husky;
// the commands contained in the package are curb") while every other tool, including the ones the build runs, restores. husky and curb are git-hook tools the build never
// runs, so a failed restore is reported and the build goes on: a tool that is really missing fails loudly where it is first run.
let restoreTools =
    lazy (shell {
        let! code = exitCode $"dotnet tool restore"
        if code <> 0 then
            printfn "WARNING: dotnet tool restore did not restore every tool (exit code %d); continuing, the tools the build runs fail on their own if they are missing" code
    })

/// The version MinVer computes from the tags, which is the version the packages get
let current =
    lazy (shell {
        restoreTools.Value
        let! version = text $"dotnet minver -p canary.0 -m 0.1"
        return version
    })

/// The informational version the packages are packed and validated with: the version, plus the commit it was built from
let informational =
    lazy (shell {
        if Paths.IncludeGitHashInInformational then
            let! sha = text $"git rev-parse HEAD"
            return $"{current.Value}+{sha}"
        else
            return current.Value
    })
