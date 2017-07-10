#I @"tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO
open System.Text

open Fake
open Fake.DotNetCli
open Fake.DocFxHelper

// Variables
let configuration = "Release"

// Directories
let output = __SOURCE_DIRECTORY__  @@ "build"
let outputTests = output @@ "tests"
let outputBinaries = output @@ "binaries"
let outputNuGet = output @@ "nuget"

Target "Clean" (fun _ ->
    CleanDir output
    CleanDir outputTests
    CleanDir outputBinaries
    CleanDir outputNuGet

    CleanDirs !! "./**/bin"
    CleanDirs !! "./**/obj"
)

Target "RestorePackages" (fun _ ->
    DotNetCli.Restore
        (fun p -> 
            { p with
                Project = "./src/Akka.Persistence.Redis.sln"
                NoCache = false })
)

Target "Build" (fun _ ->
    let projects = !! "./**/*.csproj"

    let runSingleProject project =
        DotNetCli.Build
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration })

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Tests targets 
//--------------------------------------------------------------------------------

Target "RunTests" (fun _ ->
    let projects = !! "./**/*.Tests.csproj"

    let runSingleProject project =
        DotNetCli.RunCommand
            (fun p -> 
                { p with 
                    WorkingDir = (Directory.GetParent project).FullName
                    TimeOut = TimeSpan.FromMinutes 10. })
                (sprintf "xunit -parallel none -teamcity -xml %s_xunit.xml" (outputTests @@ fileNameWithoutExt project)) 

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

Target "CreateNuget" (fun _ ->
    let envBuildNumber = environVarOrDefault "APPVEYOR_BUILD_NUMBER" "0"
    let branch =  environVarOrDefault "APPVEYOR_REPO_BRANCH" ""
    let versionSuffix = if branch.Equals("dev") then (sprintf "beta-%s" envBuildNumber) else ""

    let projects = !! "src/**/Akka.Persistence.Redis.csproj"

    let runSingleProject project =
        DotNetCli.Pack
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration
                    AdditionalArgs = ["--include-symbols"]
                    VersionSuffix = versionSuffix
                    OutputPath = outputNuGet })

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "BuildRelease" DoNothing
Target "All" DoNothing
Target "Nuget" DoNothing

// build dependencies
"Clean" ==> "RestorePackages" ==> "Build" ==> "BuildRelease"

// tests dependencies
"Clean" ==> "RestorePackages" ==> "RunTests"

// nuget dependencies
"Clean" ==> "RestorePackages" ==> "Build" ==> "CreateNuget"

// all
"BuildRelease" ==> "All"

RunTargetOrDefault "Help"